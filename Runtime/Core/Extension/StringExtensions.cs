// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Globalization;
    using System.Text;
    using Serialization;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Defines string casing formats for text transformation.
    /// </summary>
    public enum StringCase
    {
        /// <summary>Invalid string case placeholder.</summary>
        [Obsolete("Please use a valid StringCase enum value.")]
        None = 0,

        /// <summary>PascalCase - FirstLetterCapitalized for each word, no separators (e.g., "HelloWorld").</summary>
        PascalCase = 1,

        /// <summary>camelCase - first letter lowercase, subsequent words capitalized, no separators (e.g., "helloWorld").</summary>
        CamelCase = 2,

        /// <summary>snake_case - all lowercase with underscores between words (e.g., "hello_world").</summary>
        SnakeCase = 3,

        /// <summary>kebab-case - all lowercase with hyphens between words (e.g., "hello-world").</summary>
        KebabCase = 4,

        /// <summary>Title Case - Each Word Capitalized With Spaces (e.g., "Hello World").</summary>
        TitleCase = 5,

        /// <summary>lowercase - all characters converted to lowercase (e.g., "hello world").</summary>
        LowerCase = 6,

        /// <summary>UPPERCASE - all characters converted to uppercase (e.g., "HELLO WORLD").</summary>
        UpperCase = 7,

        /// <summary>lowercase invariant - culture-invariant lowercase conversion.</summary>
        LowerInvariant = 8,

        /// <summary>UPPERCASE INVARIANT - culture-invariant uppercase conversion.</summary>
        UpperInvariant = 9,
    }

    /// <summary>
    /// Extension methods for string manipulation including case conversion, encoding, serialization, and text analysis.
    /// </summary>
    /// <remarks>
    /// Thread Safety: All methods are thread-safe as they operate on immutable strings.
    /// Performance: Methods use StringBuilder and pooled buffers for efficiency where possible.
    /// Allocations: Most methods allocate new strings; some use pooled resources to minimize intermediate allocations.
    /// </remarks>
    public static class StringExtensions
    {
        private static readonly ImmutableHashSet<char> WordSeparators = new HashSet<char>
        {
            '_',
            '-',
            ' ',
            '\r',
            '\n',
            '\t',
            '.',
            '"',
        }.ToImmutableHashSet();

        private static readonly ImmutableHashSet<char> CharsToStrip = new HashSet<char>
        {
            '\'',
        }.ToImmutableHashSet();

        private const char CombiningDotAbove = '\u0307';
        private const char CapitalIWithDot = '\u0130';
        private static readonly string CombiningDotAboveString = CombiningDotAbove.ToString();
        private static readonly string CapitalIWithDotString = CapitalIWithDot.ToString();

        private enum CharacterCategory
        {
            None,
            Lower,
            Upper,
            Digit,
            Other,
        }

        private enum CaseTokenKind
        {
            Word,
            Separator,
        }

        private readonly struct CaseToken
        {
            public CaseToken(
                CaseTokenKind kind,
                string value,
                bool hasLetter,
                bool hasDigit,
                bool hasUppercase
            )
            {
                Kind = kind;
                Value = value;
                HasLetter = hasLetter;
                HasDigit = hasDigit;
                HasUppercase = hasUppercase;
            }

            public CaseTokenKind Kind { get; }

            public string Value { get; }

            public bool HasLetter { get; }

            public bool HasDigit { get; }

            public bool HasUppercase { get; }

            public bool IsNumeric => !HasLetter && HasDigit;
        }

        /// <summary>
        /// Centers a string within a field of the specified total length by padding spaces on both sides.
        /// </summary>
        /// <param name="input">The string to center.</param>
        /// <param name="length">The total width of the resulting string.</param>
        /// <returns>
        /// The centered string when <paramref name="length"/> exceeds the input length; otherwise the original string.
        /// </returns>
        /// <remarks>
        /// Returns the original string when <paramref name="length"/> is less than or equal to the input length.
        /// Uses space characters for padding; does not truncate.
        /// </remarks>
        /// <example>
        /// <code>
        /// string s = "hi";
        /// string centered = s.Center(6); // "  hi  "
        /// </code>
        /// </example>
        public static string Center(this string input, int length)
        {
            if (input == null || length <= input.Length)
            {
                return input;
            }

            return input.PadLeft((length - input.Length) / 2 + input.Length).PadRight(length);
        }

        /// <summary>
        /// Converts a string to its UTF-8 byte array representation.
        /// </summary>
        /// <param name="input">The string to convert.</param>
        /// <returns>A byte array containing the UTF-8 encoded bytes, or an empty array if input is null or empty.</returns>
        /// <remarks>
        /// Null handling: Returns Array.Empty&lt;byte&gt;() if input is null or empty.
        /// Thread-safe: Yes.
        /// Performance: O(n) where n is the string length.
        /// Allocations: Allocates a new byte array. Returns cached empty array for null/empty input.
        /// Edge cases: Empty or null strings return empty array.
        /// </remarks>
        public static byte[] GetBytes(this string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return Array.Empty<byte>();
            }
            return Encoding.UTF8.GetBytes(input);
        }

        /// <summary>
        /// Converts a UTF-8 byte array to a string.
        /// </summary>
        /// <param name="bytes">The byte array to convert.</param>
        /// <returns>The decoded string, or an empty string if bytes is null or empty.</returns>
        /// <remarks>
        /// Null handling: Returns string.Empty if bytes is null or empty.
        /// Thread-safe: Yes.
        /// Performance: O(n) where n is the byte array length.
        /// Allocations: Allocates a new string.
        /// Edge cases: Empty or null byte arrays return empty string.
        /// </remarks>
        public static string GetString(this byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Serializes an object to a JSON string representation.
        /// </summary>
        /// <typeparam name="T">The type of the value to serialize.</typeparam>
        /// <param name="value">The value to serialize to JSON.</param>
        /// <returns>A JSON string representation of the value.</returns>
        /// <remarks>
        /// Null handling: Behavior depends on Serializer.JsonStringify implementation.
        /// Thread-safe: Yes.
        /// Performance: O(n) where n is the complexity of the object graph.
        /// Allocations: Allocates new string and intermediate serialization structures.
        /// Edge cases: Complex object graphs may serialize with circular reference handling depending on serializer.
        /// </remarks>
        public static string ToJson<T>(this T value)
        {
            return Serializer.JsonStringify(value);
        }

        public static int LevenshteinDistance(this string source1, string source2)
        {
            source1 ??= string.Empty;
            source2 ??= string.Empty;

            int len1 = source1.Length;
            int len2 = source2.Length;

            if (len1 == 0)
            {
                return len2;
            }

            if (len2 == 0)
            {
                return len1;
            }

            using PooledArray<int> prevLease = SystemArrayPool<int>.Get(len2 + 1, out int[] prev);
            using PooledArray<int> currLease = SystemArrayPool<int>.Get(len2 + 1, out int[] curr);

            for (int j = 0; j <= len2; ++j)
            {
                prev[j] = j;
            }

            for (int i = 1; i <= len1; ++i)
            {
                curr[0] = i;
                char c1 = source1[i - 1];
                for (int j = 1; j <= len2; ++j)
                {
                    int cost = source2[j - 1] == c1 ? 0 : 1;
                    int deletion = prev[j] + 1;
                    int insertion = curr[j - 1] + 1;
                    int substitution = prev[j - 1] + cost;
                    int min = deletion < insertion ? deletion : insertion;
                    curr[j] = min < substitution ? min : substitution;
                }

                // swap prev and curr
                int[] tmp = prev;
                prev = curr;
                curr = tmp;
            }

            return prev[len2];
        }

        private static List<CaseToken> TokenizeForCase(string value)
        {
            List<CaseToken> tokens = new();
            if (string.IsNullOrEmpty(value))
            {
                return tokens;
            }

            using PooledResource<StringBuilder> bufferResource = Buffers.GetStringBuilder(
                value.Length,
                out StringBuilder buffer
            );

            CaseTokenKind? currentKind = null;
            CharacterCategory lastCategory = CharacterCategory.None;

            for (int i = 0; i < value.Length; ++i)
            {
                char current = value[i];
                bool isSeparator = WordSeparators.Contains(current) || char.IsWhiteSpace(current);

                if (isSeparator)
                {
                    if (currentKind == CaseTokenKind.Word && 0 < buffer.Length)
                    {
                        tokens.Add(CreateWordToken(buffer.ToString()));
                        buffer.Clear();
                    }

                    if (currentKind != CaseTokenKind.Separator)
                    {
                        if (currentKind == CaseTokenKind.Separator && 0 < buffer.Length)
                        {
                            tokens.Add(
                                new CaseToken(
                                    CaseTokenKind.Separator,
                                    buffer.ToString(),
                                    false,
                                    false,
                                    false
                                )
                            );
                            buffer.Clear();
                        }

                        currentKind = CaseTokenKind.Separator;
                        buffer.Clear();
                    }

                    _ = buffer.Append(current);
                    lastCategory = CharacterCategory.None;
                    continue;
                }

                CharacterCategory category = CategorizeChar(current);

                if (currentKind == CaseTokenKind.Separator && 0 < buffer.Length)
                {
                    tokens.Add(
                        new CaseToken(
                            CaseTokenKind.Separator,
                            buffer.ToString(),
                            false,
                            false,
                            false
                        )
                    );
                    buffer.Clear();
                }

                if (currentKind != CaseTokenKind.Word)
                {
                    currentKind = CaseTokenKind.Word;
                    lastCategory = CharacterCategory.None;
                }
                else if (ShouldStartNewWord(category, lastCategory, value, i) && 0 < buffer.Length)
                {
                    tokens.Add(CreateWordToken(buffer.ToString()));
                    buffer.Clear();
                    lastCategory = CharacterCategory.None;
                }

                _ = buffer.Append(current);
                if (category != CharacterCategory.Other)
                {
                    lastCategory = category;
                }
            }

            if (currentKind == CaseTokenKind.Separator && 0 < buffer.Length)
            {
                tokens.Add(
                    new CaseToken(CaseTokenKind.Separator, buffer.ToString(), false, false, false)
                );
            }
            else if (currentKind == CaseTokenKind.Word && 0 < buffer.Length)
            {
                tokens.Add(CreateWordToken(buffer.ToString()));
            }

            return tokens;
        }

        private static CharacterCategory CategorizeChar(char value)
        {
            if (char.IsDigit(value))
            {
                return CharacterCategory.Digit;
            }

            if (char.IsLetter(value))
            {
                return char.IsUpper(value) ? CharacterCategory.Upper : CharacterCategory.Lower;
            }

            return CharacterCategory.Other;
        }

        private static bool ShouldStartNewWord(
            CharacterCategory currentCategory,
            CharacterCategory lastCategory,
            string value,
            int index
        )
        {
            if (
                lastCategory == CharacterCategory.None
                || currentCategory == CharacterCategory.Other
            )
            {
                return false;
            }

            if (currentCategory == CharacterCategory.Upper)
            {
                if (
                    lastCategory == CharacterCategory.Lower
                    || lastCategory == CharacterCategory.Digit
                )
                {
                    return true;
                }

                if (lastCategory == CharacterCategory.Upper)
                {
                    int nextIndex = index + 1;
                    if (nextIndex < value.Length && char.IsLower(value[nextIndex]))
                    {
                        return true;
                    }
                }
            }

            bool lastWasDigit = lastCategory == CharacterCategory.Digit;
            bool currentIsDigit = currentCategory == CharacterCategory.Digit;
            bool lastWasLetter =
                lastCategory == CharacterCategory.Lower || lastCategory == CharacterCategory.Upper;
            bool currentIsLetter =
                currentCategory == CharacterCategory.Lower
                || currentCategory == CharacterCategory.Upper;

            if (currentIsDigit && lastWasLetter)
            {
                return true;
            }

            if (currentIsLetter && lastWasDigit)
            {
                return currentCategory == CharacterCategory.Upper;
            }

            return false;
        }

        private static CaseToken CreateWordToken(string value)
        {
            bool hasLetter = false;
            bool hasDigit = false;
            bool hasUppercase = false;

            for (int i = 0; i < value.Length; ++i)
            {
                char c = value[i];
                if (char.IsDigit(c))
                {
                    hasDigit = true;
                }
                else if (char.IsLetter(c))
                {
                    hasLetter = true;
                    if (char.IsUpper(c))
                    {
                        hasUppercase = true;
                    }
                }
            }

            return new CaseToken(CaseTokenKind.Word, value, hasLetter, hasDigit, hasUppercase);
        }

        private static string SanitizeWord(string value, bool removeStripChars)
        {
            if (string.IsNullOrEmpty(value) || !removeStripChars)
            {
                return value;
            }

            bool needsSanitization = false;
            for (int i = 0; i < value.Length; ++i)
            {
                if (CharsToStrip.Contains(value[i]))
                {
                    needsSanitization = true;
                    break;
                }
            }

            if (!needsSanitization)
            {
                return value;
            }

            using PooledResource<StringBuilder> builderResource = Buffers.GetStringBuilder(
                value.Length,
                out StringBuilder builder
            );

            for (int i = 0; i < value.Length; ++i)
            {
                char c = value[i];
                if (!CharsToStrip.Contains(c))
                {
                    _ = builder.Append(c);
                }
            }

            return builder.ToString();
        }

        private static void AppendWordWithCasing(
            StringBuilder builder,
            string word,
            bool uppercaseFirstLetter
        )
        {
            if (string.IsNullOrEmpty(word))
            {
                return;
            }

            char firstChar = word[0];
            _ = builder.Append(
                uppercaseFirstLetter
                    ? char.ToUpperInvariant(firstChar)
                    : char.ToLowerInvariant(firstChar)
            );

            for (int i = 1; i < word.Length; ++i)
            {
                char c = word[i];
                _ = builder.Append(char.ToLowerInvariant(c));
            }
        }

        private const char SlugSeparator = '-';

        /// <summary>
        /// Replaces accented characters with their unaccented ASCII base, leaving everything else
        /// alone.
        /// </summary>
        /// <remarks>
        /// Normalization is skipped when the string is already in Form D, and refused input is
        /// returned unchanged rather than thrown from: <see cref="string.Normalize(NormalizationForm)"/>
        /// throws <see cref="ArgumentException"/> on a lone surrogate, which is a shape real data
        /// arrives in, and a slug is not worth failing a caller over.
        /// </remarks>
        private static string FoldDiacritics(string value)
        {
            string decomposed;
            try
            {
                if (value.IsNormalized(NormalizationForm.FormD))
                {
                    decomposed = value;
                }
                else
                {
                    decomposed = value.Normalize(NormalizationForm.FormD);
                }
            }
            catch (ArgumentException)
            {
                return value;
            }

            int firstMark = -1;
            for (int i = 0; i < decomposed.Length; ++i)
            {
                if (
                    CharUnicodeInfo.GetUnicodeCategory(decomposed[i])
                    == UnicodeCategory.NonSpacingMark
                )
                {
                    firstMark = i;
                    break;
                }
            }

            if (firstMark < 0)
            {
                return decomposed;
            }

            using PooledResource<StringBuilder> pooled = Buffers.GetStringBuilder(
                decomposed.Length,
                out StringBuilder builder
            );
            builder.Append(decomposed, 0, firstMark);
            for (int i = firstMark; i < decomposed.Length; ++i)
            {
                char current = decomposed[i];
                if (CharUnicodeInfo.GetUnicodeCategory(current) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(current);
                }
            }

            return builder.ToString();
        }

        private static string ToDelimitedCase(string value, char delimiter)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            List<CaseToken> tokens = TokenizeForCase(value);
            if (tokens.Count == 0)
            {
                return string.Empty;
            }

            using PooledResource<StringBuilder> stringBuilderBuffer = Buffers.GetStringBuilder(
                value.Length,
                out StringBuilder stringBuilder
            );

            bool previousWasWord = false;
            bool previousWasNumeric = false;
            bool previousHadUppercase = false;
            bool forceDelimiter = false;

            for (int i = 0; i < tokens.Count; ++i)
            {
                CaseToken token = tokens[i];
                if (token.Kind == CaseTokenKind.Separator)
                {
                    if (previousWasWord)
                    {
                        forceDelimiter = true;
                    }

                    continue;
                }

                string sanitized = SanitizeWord(token.Value, removeStripChars: true);
                if (string.IsNullOrEmpty(sanitized))
                {
                    continue;
                }

                bool isNumeric = true;
                bool hasLetter = false;
                for (int j = 0; j < sanitized.Length; ++j)
                {
                    char c = sanitized[j];
                    if (!char.IsDigit(c))
                    {
                        isNumeric = false;
                    }

                    if (char.IsLetter(c))
                    {
                        hasLetter = true;
                    }
                }

                bool startsWithDigit = char.IsDigit(sanitized[0]);
                bool tokenHasUppercase = token.HasUppercase;

                bool nextWordHasUppercase = false;
                for (int lookahead = i + 1; lookahead < tokens.Count; ++lookahead)
                {
                    CaseToken lookaheadToken = tokens[lookahead];
                    if (lookaheadToken.Kind == CaseTokenKind.Separator)
                    {
                        continue;
                    }

                    string lookaheadSanitized = SanitizeWord(
                        lookaheadToken.Value,
                        removeStripChars: true
                    );
                    if (string.IsNullOrEmpty(lookaheadSanitized))
                    {
                        continue;
                    }

                    nextWordHasUppercase = lookaheadToken.HasUppercase;
                    break;
                }

                bool allowDigitLetterContinuation =
                    previousWasWord
                    && !forceDelimiter
                    && startsWithDigit
                    && hasLetter
                    && !tokenHasUppercase
                    && !previousHadUppercase
                    && !nextWordHasUppercase;

                bool allowNumericContinuation =
                    previousWasWord
                    && !forceDelimiter
                    && isNumeric
                    && !previousWasNumeric
                    && !previousHadUppercase
                    && !nextWordHasUppercase;

                if (!allowDigitLetterContinuation && !allowNumericContinuation && previousWasWord)
                {
                    if (0 < stringBuilder.Length && stringBuilder[^1] != delimiter)
                    {
                        _ = stringBuilder.Append(delimiter);
                    }
                }

                _ = stringBuilder.Append(sanitized.ToLowerInvariant());
                previousWasWord = true;
                previousWasNumeric = isNumeric;
                previousHadUppercase = tokenHasUppercase;
                forceDelimiter = false;
            }

            return stringBuilder.ToString();
        }

        private static bool StartsWithLowercaseWord(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (int i = 0; i < value.Length; ++i)
            {
                char c = value[i];
                if (CharsToStrip.Contains(c))
                {
                    continue;
                }

                if (WordSeparators.Contains(c) || char.IsWhiteSpace(c))
                {
                    continue;
                }

                if (char.IsLetter(c))
                {
                    return char.IsLower(c);
                }

                if (char.IsDigit(c))
                {
                    return false;
                }
            }

            return false;
        }

        private static void AppendTitleCasedWord(StringBuilder builder, string word)
        {
            if (string.IsNullOrEmpty(word))
            {
                return;
            }

            bool firstLetterHandled = false;
            for (int i = 0; i < word.Length; ++i)
            {
                char c = word[i];
                if (!firstLetterHandled && char.IsLetter(c))
                {
                    _ = builder.Append(char.ToUpperInvariant(c));
                    firstLetterHandled = true;
                }
                else if (!firstLetterHandled && char.IsDigit(c))
                {
                    _ = builder.Append(c);
                }
                else if (!firstLetterHandled)
                {
                    _ = builder.Append(c);
                }
                else if (char.IsLetter(c))
                {
                    _ = builder.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    _ = builder.Append(c);
                }
            }
        }

        private static void AppendLowerInvariant(StringBuilder builder, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            for (int i = 0; i < value.Length; ++i)
            {
                char c = value[i];
                _ = builder.Append(char.IsLetter(c) ? char.ToLowerInvariant(c) : c);
            }
        }

        private static bool IsAllUppercaseLetters(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            bool foundLetter = false;
            for (int i = 0; i < value.Length; ++i)
            {
                char c = value[i];
                if (!char.IsLetter(c))
                {
                    continue;
                }

                foundLetter = true;
                if (!char.IsUpper(c))
                {
                    return false;
                }
            }

            return foundLetter;
        }

        private static string CollapseSpaces(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            using PooledResource<StringBuilder> builderResource = Buffers.GetStringBuilder(
                value.Length,
                out StringBuilder builder
            );

            bool previousWasSpace = false;
            for (int i = 0; i < value.Length; ++i)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c))
                {
                    if (!previousWasSpace)
                    {
                        _ = builder.Append(' ');
                        previousWasSpace = true;
                    }
                }
                else
                {
                    _ = builder.Append(c);
                    previousWasSpace = false;
                }
            }

            return builder.ToString().Trim();
        }

        private static string ToTitleCaseInternal(string value, bool preserveSeparators)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            List<CaseToken> tokens = TokenizeForCase(value);
            if (tokens.Count == 0)
            {
                return string.Empty;
            }

            bool startsWithLowerWord = StartsWithLowercaseWord(value);

            using PooledResource<StringBuilder> stringBuilderBuffer = Buffers.GetStringBuilder(
                value.Length,
                out StringBuilder stringBuilder
            );

            CaseTokenKind? previousTokenKind = null;
            int previousWordLength = 0;

            for (int i = 0; i < tokens.Count; ++i)
            {
                CaseToken token = tokens[i];
                if (token.Kind == CaseTokenKind.Separator)
                {
                    if (preserveSeparators)
                    {
                        _ = stringBuilder.Append(token.Value);
                    }
                    else if (0 < stringBuilder.Length && stringBuilder[^1] != ' ')
                    {
                        _ = stringBuilder.Append(' ');
                    }

                    previousTokenKind = CaseTokenKind.Separator;
                    previousWordLength = 0;
                    continue;
                }

                string sanitized = SanitizeWord(token.Value, removeStripChars: false);
                if (string.IsNullOrEmpty(sanitized))
                {
                    previousTokenKind = CaseTokenKind.Word;
                    continue;
                }

                bool implicitBoundary = previousTokenKind == CaseTokenKind.Word;
                bool treatAsContinuation =
                    implicitBoundary
                    && startsWithLowerWord
                    && previousWordLength <= 1
                    && IsAllUppercaseLetters(sanitized);

                if (implicitBoundary && !treatAsContinuation)
                {
                    bool shouldInsertSpace = !startsWithLowerWord;
                    if (preserveSeparators)
                    {
                        if (shouldInsertSpace)
                        {
                            _ = stringBuilder.Append(' ');
                        }
                    }
                    else if (
                        shouldInsertSpace
                        && 0 < stringBuilder.Length
                        && stringBuilder[^1] != ' '
                    )
                    {
                        _ = stringBuilder.Append(' ');
                    }
                }

                if (treatAsContinuation)
                {
                    AppendLowerInvariant(stringBuilder, sanitized);
                    previousWordLength += sanitized.Length;
                }
                else
                {
                    AppendTitleCasedWord(stringBuilder, sanitized);
                    previousWordLength = sanitized.Length;
                }

                previousTokenKind = CaseTokenKind.Word;
            }

            if (!preserveSeparators)
            {
                return CollapseSpaces(stringBuilder.ToString());
            }

            return stringBuilder.ToString();
        }

        public static string ToPascalCase(this string value, string separator = "")
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            List<CaseToken> tokens = TokenizeForCase(value);
            if (tokens.Count == 0)
            {
                return string.Empty;
            }

            using PooledResource<StringBuilder> stringBuilderBuffer = Buffers.GetStringBuilder(
                value.Length,
                out StringBuilder stringBuilder
            );

            bool isFirstWord = true;
            foreach (CaseToken token in tokens)
            {
                if (token.Kind != CaseTokenKind.Word)
                {
                    continue;
                }

                string sanitized = SanitizeWord(token.Value, removeStripChars: true);
                if (string.IsNullOrEmpty(sanitized))
                {
                    continue;
                }

                if (!isFirstWord && !string.IsNullOrEmpty(separator))
                {
                    _ = stringBuilder.Append(separator);
                }

                AppendWordWithCasing(stringBuilder, sanitized, uppercaseFirstLetter: true);
                isFirstWord = false;
            }

            return stringBuilder.ToString();
        }

        public static bool NeedsLowerInvariantConversion(this string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            foreach (char inputCharacter in input)
            {
                if (char.ToLowerInvariant(inputCharacter) != inputCharacter)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool NeedsTrim(this string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            return char.IsWhiteSpace(input[0]) || char.IsWhiteSpace(input[^1]);
        }

        /// <summary>
        /// Shortens a string to at most <paramref name="maxLength"/> characters, marking what was
        /// removed with <paramref name="ellipsis"/>.
        /// </summary>
        /// <param name="input">The string to shorten.</param>
        /// <param name="maxLength">The maximum length of the result.</param>
        /// <param name="ellipsis">The marker appended in place of the removed text.</param>
        /// <returns>The original string when it already fits, otherwise a string of at most <paramref name="maxLength"/> characters.</returns>
        /// <remarks>
        /// The result never exceeds <paramref name="maxLength"/>. When the ellipsis alone would not
        /// fit there is no room to mark the elision, so it is dropped and the input is cut instead.
        /// The cut never lands between the halves of a surrogate pair, so the result is always
        /// well-formed UTF-16 and may be one character shorter than requested.
        /// </remarks>
        public static string Truncate(this string input, int maxLength, string ellipsis = "...")
        {
            if (string.IsNullOrEmpty(input) || maxLength < 0)
            {
                return input;
            }

            if (input.Length <= maxLength)
            {
                return input;
            }

            if (string.IsNullOrEmpty(ellipsis) || maxLength < ellipsis.Length)
            {
                return input.Substring(0, WholeCharacterLength(input, maxLength));
            }

            int truncateLength = WholeCharacterLength(input, maxLength - ellipsis.Length);
            return input.Substring(0, truncateLength) + ellipsis;
        }

        /*
            Cutting between the halves of a surrogate pair leaves a lone surrogate, which has no
            UTF-8 encoding and becomes U+FFFD the moment the string is written out. The character is
            destroyed rather than dropped, so cut in front of the pair instead.
        */
        private static int WholeCharacterLength(string input, int length)
        {
            if (
                0 < length
                && length < input.Length
                && char.IsHighSurrogate(input[length - 1])
                && char.IsLowSurrogate(input[length])
            )
            {
                return length - 1;
            }

            return length;
        }

        private static bool IsAlreadyCamelCase(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            bool hasSeenLetter = false;

            for (int i = 0; i < value.Length; ++i)
            {
                char c = value[i];

                if (WordSeparators.Contains(c) || char.IsWhiteSpace(c) || CharsToStrip.Contains(c))
                {
                    return false;
                }

                if (char.IsLetter(c))
                {
                    if (!hasSeenLetter)
                    {
                        if (!char.IsLower(c))
                        {
                            return false;
                        }

                        hasSeenLetter = true;
                    }

                    if (char.IsUpper(c))
                    {
                        int nextIndex = i + 1;
                        if (nextIndex < value.Length && char.IsUpper(value[nextIndex]))
                        {
                            return false;
                        }
                    }
                }
                else if (!char.IsDigit(c))
                {
                    return false;
                }
            }

            return true;
        }

        public static string ToCamelCase(this string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (IsAlreadyCamelCase(value))
            {
                return value;
            }

            string pascalCase = value.ToPascalCase();
            if (pascalCase.Length == 0)
            {
                return string.Empty;
            }

            if (pascalCase.Length == 1)
            {
                return char.ToLowerInvariant(pascalCase[0]).ToString();
            }

            pascalCase = RemoveCombiningDotAboveIfPresent(pascalCase);
            using PooledResource<StringBuilder> stringBuilderBuffer = Buffers.GetStringBuilder(
                value.Length,
                out StringBuilder stringBuilder
            );
            _ = stringBuilder.Append(char.ToLowerInvariant(pascalCase[0]));

            for (int i = 1; i < pascalCase.Length; ++i)
            {
                _ = stringBuilder.Append(pascalCase[i]);
            }

            return stringBuilder.ToString();
        }

        public static string ToSnakeCase(this string value)
        {
            return ToDelimitedCase(value, '_');
        }

        public static string ToKebabCase(this string value)
        {
            return ToDelimitedCase(value, '-');
        }

        public static string ToTitleCase(this string value, bool preserveSeparators = true)
        {
            return ToTitleCaseInternal(value, preserveSeparators);
        }

        /// <summary>
        /// Converts a string to a URL- and filename-safe slug: lowercase ASCII letters and digits,
        /// single hyphens between words, no leading or trailing hyphen.
        /// </summary>
        /// <param name="value">The text to slugify.</param>
        /// <returns>
        /// The slug, or <see cref="string.Empty"/> when the input is null, empty, or contains no
        /// ASCII letter or digit once accents are folded.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Word boundaries come from the same tokenizer the casing family uses, so
        /// <c>"PlayerHPMax"</c> slugs to <c>"player-hp-max"</c> rather than to one run.
        /// </para>
        /// <para>
        /// Accents are folded to their ASCII base rather than dropped, so <c>"Café"</c> keeps all
        /// four of its letters and slugs to <c>"cafe"</c>. Characters with no ASCII form, emoji and
        /// ideographic scripts among them, are removed, which is why a string written entirely in
        /// such a script slugs to empty. Check for that rather than assuming a non-empty input
        /// yields a non-empty slug.
        /// </para>
        /// <para>
        /// <see cref="ToKebabCase"/> is not a substitute: it preserves punctuation and accents, so
        /// <c>"Café Menu -- 50% Off!"</c> kebabs to <c>"café-menu-50%-off!"</c> and slugs to
        /// <c>"cafe-menu-50-off"</c>.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// string key = "Level 10: The Descent".Slugify();   // "level-10-the-descent"
        /// </code>
        /// </example>
        public static string Slugify(this string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            string delimited = ToDelimitedCase(FoldDiacritics(value), SlugSeparator);
            if (string.IsNullOrEmpty(delimited))
            {
                return string.Empty;
            }

            using PooledResource<StringBuilder> pooled = Buffers.GetStringBuilder(
                delimited.Length,
                out StringBuilder builder
            );

            bool separatorPending = false;
            for (int i = 0; i < delimited.Length; ++i)
            {
                char current = delimited[i];
                bool isAsciiAlphanumeric =
                    current is >= '0' and <= '9'
                    || current is >= 'a' and <= 'z'
                    || current is >= 'A' and <= 'Z';
                if (!isAsciiAlphanumeric)
                {
                    // Every rejected run collapses to at most one separator, and a pending one is
                    // only emitted once something survives after it, which is what trims both ends.
                    separatorPending = 0 < builder.Length;
                    continue;
                }

                if (separatorPending)
                {
                    builder.Append(SlugSeparator);
                    separatorPending = false;
                }

                // Invariant specifically, not ToLower(): under tr-TR, 'I' lowercases to the
                // dotless '\u0131', which is not ASCII and would break what this method promises.
                builder.Append(char.ToLowerInvariant(current));
            }

            return builder.ToString();
        }

        public static bool ContainsIgnoreCase(this string input, string value)
        {
            if (input == null || value == null)
            {
                return false;
            }

            return 0 <= input.IndexOf(value, StringComparison.OrdinalIgnoreCase);
        }

        public static bool EqualsIgnoreCase(this string input, string value)
        {
            return string.Equals(input, value, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reverses the characters of a string.
        /// </summary>
        /// <param name="input">The string to reverse.</param>
        /// <returns>The reversed string, or the input when it is null or a single character.</returns>
        /// <remarks>
        /// Surrogate pairs are kept intact, so a string containing emoji or any other non-BMP
        /// character reverses to well-formed UTF-16 rather than to replacement characters.
        /// Combining marks are reversed along with everything else, so text that uses them is
        /// re-ordered rather than re-rendered; reversing twice still returns the original.
        /// </remarks>
        public static string Reverse(this string input)
        {
            if (input == null || input.Length <= 1)
            {
                return input;
            }

            int len = input.Length;
            return string.Create(
                len,
                input,
                static (span, src) =>
                {
                    int write = 0;
                    for (int read = src.Length - 1; 0 <= read; --read)
                    {
                        char current = src[read];
                        if (
                            0 < read
                            && char.IsLowSurrogate(current)
                            && char.IsHighSurrogate(src[read - 1])
                        )
                        {
                            span[write++] = src[read - 1];
                            span[write++] = current;
                            --read;
                            continue;
                        }

                        span[write++] = current;
                    }
                }
            );
        }

        public static string RemoveWhitespace(this string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            using PooledResource<StringBuilder> stringBuilderBuffer = Buffers.GetStringBuilder(
                input.Length,
                out StringBuilder stringBuilder
            );

            foreach (char c in input)
            {
                if (!char.IsWhiteSpace(c))
                {
                    _ = stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString();
        }

        public static int CountOccurrences(this string input, char character)
        {
            if (string.IsNullOrEmpty(input))
            {
                return 0;
            }

            int count = 0;
            foreach (char c in input)
            {
                if (c == character)
                {
                    ++count;
                }
            }

            return count;
        }

        public static int CountOccurrences(this string input, string substring)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(substring))
            {
                return 0;
            }

            int count = 0;
            int index = 0;

            while (0 <= (index = input.IndexOf(substring, index, StringComparison.Ordinal)))
            {
                ++count;
                index += substring.Length;
            }

            return count;
        }

        public static bool IsNumeric(this string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            foreach (char c in input)
            {
                if (!char.IsDigit(c))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsAlphabetic(this string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            foreach (char c in input)
            {
                if (!char.IsLetter(c))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsAlphanumeric(this string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            foreach (char c in input)
            {
                if (!char.IsLetterOrDigit(c))
                {
                    return false;
                }
            }

            return true;
        }

        public static string ToBase64(this string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(input);
            return Convert.ToBase64String(bytes);
        }

        public static string FromBase64(this string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            if (TryDecodeBase64Utf8(input, out string decoded))
            {
                return decoded;
            }

            return string.Empty;
        }

        private static bool IsLikelyBase64(string s)
        {
            int len = s.Length;
            if (len == 0 || (len & 3) != 0)
            {
                return false;
            }

            // Count '=' padding at end (0..2), and ensure it only appears at the end
            int padding = 0;
            if (s[len - 1] == '=')
            {
                padding = 1;
                if (s[len - 2] == '=')
                {
                    padding = 2;
                }
            }

            int effectiveLen = len - padding;
            for (int i = 0; i < effectiveLen; ++i)
            {
                char c = s[i];
                bool isAlphaUpper = c is >= 'A' and <= 'Z';
                bool isAlphaLower = c is >= 'a' and <= 'z';
                bool isDigit = c is >= '0' and <= '9';
                bool isPlusSlash = c == '+' || c == '/';
                if (!(isAlphaUpper || isAlphaLower || isDigit || isPlusSlash))
                {
                    return false;
                }
            }

            // Ensure no '=' appears before the padding region
            for (int i = 0; i < effectiveLen; ++i)
            {
                if (s[i] == '=')
                {
                    return false;
                }
            }

            // Basic checks passed
            return true;
        }

        private static bool TryDecodeBase64Utf8(string s, out string result)
        {
            if (!IsLikelyBase64(s))
            {
                result = string.Empty;
                return false;
            }

            int len = s.Length;
            int padding = 0;
            if (0 < len && s[len - 1] == '=')
            {
                padding = 1;
                if (1 < len && s[len - 2] == '=')
                {
                    padding = 2;
                }
            }

            int outputLen = (len >> 2) * 3 - padding;
            if (outputLen < 0)
            {
                result = string.Empty;
                return false;
            }

            using PooledArray<byte> lease = SystemArrayPool<byte>.Get(outputLen, out byte[] buffer);

            int k = 0;
            for (int i = 0; i < len; i += 4)
            {
                int v0 = Base64Map(s[i]);
                int v1 = Base64Map(s[i + 1]);
                char c2 = s[i + 2];
                char c3 = s[i + 3];
                int v2 = c2 == '=' ? 0 : Base64Map(c2);
                int v3 = c3 == '=' ? 0 : Base64Map(c3);

                if (v0 < 0 || v1 < 0 || (c2 != '=' && v2 < 0) || (c3 != '=' && v3 < 0))
                {
                    result = string.Empty;
                    return false;
                }

                if (k < outputLen)
                {
                    buffer[k++] = (byte)((v0 << 2) | (v1 >> 4));
                }
                if (k < outputLen)
                {
                    buffer[k++] = (byte)((v1 << 4) | (v2 >> 2));
                }
                if (k < outputLen)
                {
                    buffer[k++] = (byte)((v2 << 6) | v3);
                }
            }

            result = Encoding.UTF8.GetString(buffer, 0, outputLen);
            return true;
        }

        private static int Base64Map(char c)
        {
            if (c is >= 'A' and <= 'Z')
            {
                return c - 'A';
            }
            if (c is >= 'a' and <= 'z')
            {
                return c - 'a' + 26;
            }
            if (c is >= '0' and <= '9')
            {
                return c - '0' + 52;
            }
            if (c == '+')
            {
                return 62;
            }
            if (c == '/')
            {
                return 63;
            }
            return -1;
        }

        public static string Repeat(this string input, int count)
        {
            if (string.IsNullOrEmpty(input) || count <= 0)
            {
                return string.Empty;
            }

            if (count == 1)
            {
                return input;
            }

            int estimated = 0;
            if (1 < count && 0 < input.Length)
            {
                int maxMultiplier = int.MaxValue / input.Length;
                if (count <= maxMultiplier)
                {
                    estimated = input.Length * count;
                }
                // else leave estimated = 0 to let the builder grow dynamically
            }
            using PooledResource<StringBuilder> stringBuilderBuffer = Buffers.GetStringBuilder(
                estimated,
                out StringBuilder stringBuilder
            );

            for (int i = 0; i < count; ++i)
            {
                _ = stringBuilder.Append(input);
            }

            return stringBuilder.ToString();
        }

        public static string[] SplitCamelCase(this string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return Array.Empty<string>();
            }

            using PooledResource<List<string>> listBuffer = Buffers<string>.List.Get(
                out List<string> words
            );
            using PooledResource<StringBuilder> stringBuilderBuffer = Buffers.GetStringBuilder(
                input.Length,
                out StringBuilder currentWord
            );

            for (int i = 0; i < input.Length; ++i)
            {
                char current = input[i];

                if (WordSeparators.Contains(current))
                {
                    if (0 < currentWord.Length)
                    {
                        words.Add(currentWord.ToString());
                        currentWord.Clear();
                    }
                    continue;
                }

                if (char.IsUpper(current) && 0 < i)
                {
                    char previous = input[i - 1];
                    if (!char.IsUpper(previous) && !WordSeparators.Contains(previous))
                    {
                        if (0 < currentWord.Length)
                        {
                            words.Add(currentWord.ToString());
                            currentWord.Clear();
                        }
                    }
                    else if (i + 1 < input.Length && char.IsLower(input[i + 1]))
                    {
                        if (0 < currentWord.Length)
                        {
                            words.Add(currentWord.ToString());
                            currentWord.Clear();
                        }
                    }
                }

                _ = currentWord.Append(current);
            }

            if (0 < currentWord.Length)
            {
                words.Add(currentWord.ToString());
            }

            return words.ToArray();
        }

        public static string ReplaceFirst(this string input, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(oldValue))
            {
                return input;
            }

            int index = input.IndexOf(oldValue, StringComparison.Ordinal);
            if (index < 0)
            {
                return input;
            }

            int oldLen = oldValue.Length;
            int newLen = input.Length - oldLen + (newValue?.Length ?? 0);
            return string.Create(
                newLen,
                (input, index, oldLen, newValue),
                static (dst, state) =>
                {
                    state.input.AsSpan(0, state.index).CopyTo(dst);
                    int pos = state.index;
                    if (state.newValue != null)
                    {
                        state.newValue.AsSpan().CopyTo(dst.Slice(pos));
                        pos += state.newValue.Length;
                    }
                    state.input.AsSpan(state.index + state.oldLen).CopyTo(dst.Slice(pos));
                }
            );
        }

        public static string ReplaceLast(this string input, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(oldValue))
            {
                return input;
            }

            int index = input.LastIndexOf(oldValue, StringComparison.Ordinal);
            if (index < 0)
            {
                return input;
            }

            int oldLen = oldValue.Length;
            int newLen = input.Length - oldLen + (newValue?.Length ?? 0);
            return string.Create(
                newLen,
                (input, index, oldLen, newValue),
                static (dst, state) =>
                {
                    state.input.AsSpan(0, state.index).CopyTo(dst);
                    int pos = state.index;
                    if (state.newValue != null)
                    {
                        state.newValue.AsSpan().CopyTo(dst.Slice(pos));
                        pos += state.newValue.Length;
                    }
                    state.input.AsSpan(state.index + state.oldLen).CopyTo(dst.Slice(pos));
                }
            );
        }

        private static string RemoveCombiningDotAboveIfPresent(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            bool containsSpecialCharacter = false;
            for (int i = 0; i < value.Length; ++i)
            {
                char c = value[i];
                if (c == CombiningDotAbove || c == CapitalIWithDot)
                {
                    containsSpecialCharacter = true;
                    break;
                }
            }

            if (!containsSpecialCharacter)
            {
                return value;
            }

            using PooledResource<StringBuilder> builderResource = Buffers.GetStringBuilder(
                value.Length,
                out StringBuilder builder
            );

            for (int i = 0; i < value.Length; ++i)
            {
                char c = value[i];
                if (c == CombiningDotAbove)
                {
                    continue;
                }

                if (c == CapitalIWithDot)
                {
                    _ = builder.Append(char.ToLowerInvariant('I'));
                    continue;
                }

                _ = builder.Append(c);
            }

            return builder.ToString();
        }

        public static string ToCase(this string value, StringCase stringCase)
        {
            switch (stringCase)
            {
                case StringCase.PascalCase:
                    return value.ToPascalCase();
                case StringCase.CamelCase:
                    return value.ToCamelCase();
                case StringCase.SnakeCase:
                    return value.ToSnakeCase();
                case StringCase.KebabCase:
                    return value.ToKebabCase();
                case StringCase.TitleCase:
                    return value.ToTitleCase(preserveSeparators: false);
                case StringCase.LowerCase:
                    return value == null
                        ? string.Empty
                        : RemoveCombiningDotAboveIfPresent(value.ToLowerInvariant());
                case StringCase.UpperCase:
                    return value?.ToUpperInvariant() ?? string.Empty;
                case StringCase.LowerInvariant:
                    return value == null
                        ? string.Empty
                        : RemoveCombiningDotAboveIfPresent(value.ToLowerInvariant());
                case StringCase.UpperInvariant:
                    return value?.ToUpperInvariant() ?? string.Empty;
#pragma warning disable CS0618 // Type or member is obsolete
                case StringCase.None:
#pragma warning restore CS0618 // Type or member is obsolete
                default:
                    return value ?? string.Empty;
            }
        }
    }
}
