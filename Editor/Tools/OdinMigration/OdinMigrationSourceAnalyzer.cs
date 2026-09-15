// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;

    internal static class OdinMigrationSourceAnalyzer
    {
        private const string AttributeNamespace = "WallstopStudios.UnityHelpers.Core.Attributes.";

        private static readonly Regex DirectOdinUsing = new Regex(
            @"\busing\s+(?:global::)?Sirenix\.OdinInspector\s*;",
            RegexOptions.CultureInvariant
        );

        private static readonly Regex OdinUsing = new Regex(
            @"\busing\s+(?:[A-Za-z_]\w*\s*=\s*)?(?:global::)?Sirenix\.[^;]+;",
            RegexOptions.CultureInvariant
        );

        private static readonly HashSet<string> ReportedOdinAttributes = new HashSet<string>(
            StringComparer.Ordinal
        )
        {
            "BoxGroup",
            "Button",
            "ButtonGroup",
            "EnumToggleButtons",
            "FoldoutGroup",
            "HideIf",
            "InlineEditor",
            "OdinSerialize",
            "ReadOnly",
            "Required",
            "ShowIf",
            "ValueDropdown",
        };

        private static readonly string[] SerializedStateTokens =
        {
            "Serialized" + "MonoBehaviour",
            "Serialized" + "ScriptableObject",
            "SerializedBehaviour",
            "SerializedComponent",
            "SerializedStateMachineBehaviour",
            "OdinSerialize",
        };

        private static readonly string[] CollectionTokens =
        {
            "Dictionary",
            "HashSet",
            "SerializedDictionary",
        };

        internal static OdinMigrationAnalysis Analyze(string source)
        {
            if (source == null)
            {
                return OdinMigrationAnalysis.Invalid("Source is null.");
            }

            string masked = MaskNonCode(source);
            LineMap lineMap = new LineMap(source);
            SourceContext sourceContext = new SourceContext(masked);
            bool hasDirectOdinUsing = HasCompilationUnitDirectOdinUsing(masked, sourceContext);
            List<Replacement> replacements = new List<Replacement>();
            List<OdinMigrationFinding> blockers = new List<OdinMigrationFinding>();
            List<OdinMigrationFinding> manualReviews = new List<OdinMigrationFinding>();

            AddSerializedStateFindings(masked, blockers, lineMap);
            AddCollectionFindingsWhenOdinOwnsState(masked, blockers, lineMap);
            AddOdinDependencyFindings(masked, manualReviews, lineMap);

            int searchStart = 0;
            while (searchStart < masked.Length)
            {
                int bracketStart = masked.IndexOf('[', searchStart);
                if (bracketStart < 0)
                {
                    break;
                }

                if (!LooksLikeAttributeList(masked, bracketStart))
                {
                    searchStart = bracketStart + 1;
                    continue;
                }

                int bracketEnd = FindAttributeListEnd(masked, bracketStart);
                if (bracketEnd < 0)
                {
                    AddFinding(
                        manualReviews,
                        lineMap,
                        bracketStart,
                        "An unterminated attribute list was left unchanged."
                    );
                    break;
                }

                AnalyzeAttributeList(
                    source,
                    masked,
                    bracketStart,
                    bracketEnd,
                    lineMap,
                    sourceContext,
                    hasDirectOdinUsing,
                    replacements,
                    manualReviews
                );
                searchStart = bracketEnd + 1;
            }

            List<OdinMigrationChange> changes = new List<OdinMigrationChange>(replacements.Count);
            foreach (Replacement replacement in replacements)
            {
                changes.Add(
                    new OdinMigrationChange(
                        lineMap.GetLine(replacement.Start),
                        source.Substring(replacement.Start, replacement.Length),
                        replacement.Value
                    )
                );
            }
            changes.Sort(static (left, right) => left.Line.CompareTo(right.Line));

            string upgradedSource = source;
            if (0 < replacements.Count)
            {
                replacements.Sort(static (left, right) => right.Start.CompareTo(left.Start));
                StringBuilder upgraded = new StringBuilder(source);
                foreach (Replacement replacement in replacements)
                {
                    upgraded.Remove(replacement.Start, replacement.Length);
                    upgraded.Insert(replacement.Start, replacement.Value);
                }
                upgradedSource = upgraded.ToString();
            }

            return new OdinMigrationAnalysis(
                upgradedSource,
                changes,
                blockers,
                manualReviews,
                null
            );
        }

        internal static bool LooksGenerated(string assetPath, string source)
        {
            string normalized = (assetPath ?? string.Empty).Replace('\\', '/');
            int marker = (source ?? string.Empty).IndexOf(
                "<auto-generated",
                StringComparison.OrdinalIgnoreCase
            );
            return 0 <= normalized.IndexOf("/Generated/", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
                || (0 <= marker && marker < 4096);
        }

        private static void AddOdinDependencyFindings(
            string masked,
            List<OdinMigrationFinding> manualReviews,
            LineMap lineMap
        )
        {
            foreach (Match match in OdinUsing.Matches(masked))
            {
                AddFinding(
                    manualReviews,
                    lineMap,
                    match.Index,
                    "A Sirenix using remains after safe attribute rewrites and requires dependency review."
                );
            }
        }

        private static void AddCollectionFindingsWhenOdinOwnsState(
            string masked,
            List<OdinMigrationFinding> blockers,
            LineMap lineMap
        )
        {
            bool odinOwnsState = false;
            foreach (string token in SerializedStateTokens)
            {
                if (ContainsIdentifier(masked, token, out _))
                {
                    odinOwnsState = true;
                    break;
                }
            }

            if (!odinOwnsState)
            {
                return;
            }

            foreach (string token in CollectionTokens)
            {
                int searchStart = 0;
                while (TryFindIdentifier(masked, token, searchStart, out int position))
                {
                    int after = position + token.Length;
                    while (after < masked.Length && char.IsWhiteSpace(masked[after]))
                    {
                        after++;
                    }
                    if (after < masked.Length && masked[after] == '<')
                    {
                        AddFinding(
                            blockers,
                            lineMap,
                            position,
                            $"{token}<...> may contain Odin-owned data and was not rewritten."
                        );
                    }
                    searchStart = position + token.Length;
                }
            }
        }

        private static void AddSerializedStateFindings(
            string masked,
            List<OdinMigrationFinding> blockers,
            LineMap lineMap
        )
        {
            foreach (string token in SerializedStateTokens)
            {
                int searchStart = 0;
                while (TryFindIdentifier(masked, token, searchStart, out int position))
                {
                    AddFinding(
                        blockers,
                        lineMap,
                        position,
                        $"{token} owns serialized state and requires a staged data migration."
                    );
                    searchStart = position + token.Length;
                }
            }
        }

        private static void AnalyzeAttributeList(
            string source,
            string masked,
            int bracketStart,
            int bracketEnd,
            LineMap lineMap,
            SourceContext sourceContext,
            bool hasDirectOdinUsing,
            List<Replacement> replacements,
            List<OdinMigrationFinding> manualReviews
        )
        {
            bool fieldOrPropertyTarget = IsProvableFieldOrPropertyTarget(
                masked,
                bracketStart,
                bracketEnd,
                sourceContext
            );
            int position = bracketStart + 1;
            SkipWhitespace(masked, ref position, bracketEnd);
            string explicitTarget = ReadAttributeTarget(masked, ref position, bracketEnd);
            bool allowedAttributeTarget =
                explicitTarget == null || explicitTarget == "field" || explicitTarget == "property";

            while (position < bracketEnd)
            {
                SkipWhitespace(masked, ref position, bracketEnd);
                if (!TryReadQualifiedName(masked, ref position, bracketEnd, out TextSpan nameSpan))
                {
                    return;
                }

                int argumentsStart = -1;
                int argumentsEnd = -1;
                SkipWhitespace(masked, ref position, bracketEnd);
                if (position < bracketEnd && masked[position] == '(')
                {
                    argumentsStart = position;
                    argumentsEnd = FindMatching(masked, position, bracketEnd, '(', ')');
                    if (argumentsEnd < 0)
                    {
                        AddFinding(
                            manualReviews,
                            lineMap,
                            nameSpan.Start,
                            "An attribute with unterminated arguments was left unchanged."
                        );
                        return;
                    }
                    position = argumentsEnd + 1;
                }

                string writtenName = source.Substring(nameSpan.Start, nameSpan.Length);
                if (
                    TryResolveOdinAttribute(
                        writtenName,
                        hasDirectOdinUsing,
                        out string attributeName,
                        out bool automaticRewrite
                    )
                )
                {
                    AnalyzeOdinAttribute(
                        source,
                        nameSpan,
                        argumentsStart,
                        argumentsEnd,
                        fieldOrPropertyTarget,
                        allowedAttributeTarget,
                        explicitTarget,
                        lineMap,
                        attributeName,
                        automaticRewrite,
                        replacements,
                        manualReviews
                    );
                }

                SkipWhitespace(masked, ref position, bracketEnd);
                if (bracketEnd <= position || masked[position] != ',')
                {
                    return;
                }
                position++;
            }
        }

        private static void AnalyzeOdinAttribute(
            string source,
            TextSpan nameSpan,
            int argumentsStart,
            int argumentsEnd,
            bool fieldOrPropertyTarget,
            bool allowedAttributeTarget,
            string explicitTarget,
            LineMap lineMap,
            string attributeName,
            bool automaticRewrite,
            List<Replacement> replacements,
            List<OdinMigrationFinding> manualReviews
        )
        {
            if (attributeName == "OdinSerialize")
            {
                return;
            }

            if (!automaticRewrite)
            {
                AddFinding(
                    manualReviews,
                    lineMap,
                    nameSpan.Start,
                    $"{attributeName} could not be proven to resolve to the global Odin type and was left unchanged."
                );
                return;
            }

            if (!allowedAttributeTarget)
            {
                AddFinding(
                    manualReviews,
                    lineMap,
                    nameSpan.Start,
                    $"The explicit '{explicitTarget}:' attribute target is not a field or property target and was left unchanged."
                );
                return;
            }

            if (!fieldOrPropertyTarget)
            {
                AddFinding(
                    manualReviews,
                    lineMap,
                    nameSpan.Start,
                    $"{attributeName} is not on a provable field or property declaration and was left unchanged."
                );
                return;
            }

            string arguments =
                argumentsStart < 0
                    ? null
                    : source
                        .Substring(argumentsStart + 1, argumentsEnd - argumentsStart - 1)
                        .Trim();
            string replacement = null;
            switch (attributeName)
            {
                case "ReadOnly":
                    if (string.IsNullOrEmpty(arguments))
                    {
                        replacement = AttributeNamespace + "WReadOnly";
                    }
                    break;
                case "EnumToggleButtons":
                    if (string.IsNullOrEmpty(arguments))
                    {
                        replacement = AttributeNamespace + "WEnumToggleButtons";
                    }
                    break;
                case "Required":
                case "Button":
                case "ShowIf":
                case "HideIf":
                    break;
            }

            if (replacement == null)
            {
                if (ReportedOdinAttributes.Contains(attributeName))
                {
                    AddFinding(
                        manualReviews,
                        lineMap,
                        nameSpan.Start,
                        $"{attributeName} has no proven equivalent for these arguments and was left unchanged."
                    );
                }
                return;
            }

            replacements.Add(new Replacement(nameSpan.Start, nameSpan.Length, replacement));
        }

        private static bool ContainsIdentifier(string source, string identifier, out int position)
        {
            return TryFindIdentifier(source, identifier, 0, out position);
        }

        private static int FindAttributeListEnd(string masked, int bracketStart)
        {
            return FindMatching(masked, bracketStart, masked.Length, '[', ']');
        }

        private static int FindMatching(
            string source,
            int opening,
            int limit,
            char openingCharacter,
            char closingCharacter
        )
        {
            int depth = 0;
            for (int index = opening; index < limit; index++)
            {
                char character = source[index];
                if (character == openingCharacter)
                {
                    depth++;
                }
                else if (character == closingCharacter && --depth == 0)
                {
                    return index;
                }
            }
            return -1;
        }

        private static string MaskNonCode(string source)
        {
            char[] masked = source.ToCharArray();
            int index = 0;
            while (index < masked.Length)
            {
                char character = masked[index];
                if (character == '/' && index + 1 < masked.Length && masked[index + 1] == '/')
                {
                    MaskLineComment(masked, ref index);
                }
                else if (character == '/' && index + 1 < masked.Length && masked[index + 1] == '*')
                {
                    MaskBlockComment(masked, ref index);
                }
                else if (character == '\'')
                {
                    MaskQuoted(masked, ref index, '\'', false);
                }
                else if (character == '"')
                {
                    bool verbatim =
                        (0 < index && masked[index - 1] == '@')
                        || (1 < index && masked[index - 1] == '$' && masked[index - 2] == '@');
                    MaskQuoted(masked, ref index, '"', verbatim);
                }
                else
                {
                    index++;
                }
            }
            return new string(masked);
        }

        private static void MaskBlockComment(char[] characters, ref int index)
        {
            characters[index++] = ' ';
            characters[index++] = ' ';
            while (index < characters.Length)
            {
                if (
                    characters[index] == '*'
                    && index + 1 < characters.Length
                    && characters[index + 1] == '/'
                )
                {
                    characters[index++] = ' ';
                    characters[index++] = ' ';
                    return;
                }
                if (characters[index] != '\r' && characters[index] != '\n')
                {
                    characters[index] = ' ';
                }
                index++;
            }
        }

        private static void MaskLineComment(char[] characters, ref int index)
        {
            while (
                index < characters.Length && characters[index] != '\r' && characters[index] != '\n'
            )
            {
                characters[index++] = ' ';
            }
        }

        private static void MaskQuoted(char[] characters, ref int index, char quote, bool verbatim)
        {
            characters[index++] = ' ';
            while (index < characters.Length)
            {
                char character = characters[index];
                if (character == '\r' || character == '\n')
                {
                    if (!verbatim)
                    {
                        return;
                    }
                    index++;
                    continue;
                }
                characters[index] = ' ';
                index++;
                if (character == quote)
                {
                    if (verbatim && index < characters.Length && characters[index] == quote)
                    {
                        characters[index++] = ' ';
                        continue;
                    }
                    return;
                }
                if (!verbatim && character == '\\' && index < characters.Length)
                {
                    if (characters[index] != '\r' && characters[index] != '\n')
                    {
                        characters[index] = ' ';
                    }
                    index++;
                }
            }
        }

        private static void AddFinding(
            List<OdinMigrationFinding> findings,
            LineMap lineMap,
            int position,
            string message
        )
        {
            findings.Add(new OdinMigrationFinding(lineMap.GetLine(position), message));
        }

        private static string ReadAttributeTarget(string source, ref int position, int limit)
        {
            int original = position;
            if (!TryReadIdentifier(source, ref position, limit, out TextSpan targetSpan))
            {
                return null;
            }
            SkipWhitespace(source, ref position, limit);
            if (
                position < limit
                && source[position] == ':'
                && (limit <= position + 1 || source[position + 1] != ':')
            )
            {
                position++;
                return source.Substring(targetSpan.Start, targetSpan.Length);
            }
            position = original;
            return null;
        }

        private static void SkipWhitespace(string source, ref int position, int limit)
        {
            while (position < limit && char.IsWhiteSpace(source[position]))
            {
                position++;
            }
        }

        private static string TrimAttributeSuffix(string name)
        {
            const string Suffix = "Attribute";
            return name.EndsWith(Suffix, StringComparison.Ordinal)
                ? name.Substring(0, name.Length - Suffix.Length)
                : name;
        }

        private static bool TryFindIdentifier(
            string source,
            string identifier,
            int searchStart,
            out int position
        )
        {
            int candidate = source.IndexOf(identifier, searchStart, StringComparison.Ordinal);
            while (0 <= candidate)
            {
                int end = candidate + identifier.Length;
                bool startsAtBoundary =
                    candidate == 0 || !IsIdentifierCharacter(source[candidate - 1]);
                bool endsAtBoundary = end == source.Length || !IsIdentifierCharacter(source[end]);
                if (startsAtBoundary && endsAtBoundary)
                {
                    position = candidate;
                    return true;
                }
                candidate = source.IndexOf(identifier, end, StringComparison.Ordinal);
            }
            position = -1;
            return false;
        }

        private static bool TryReadIdentifier(
            string source,
            ref int position,
            int limit,
            out TextSpan span
        )
        {
            if (limit <= position || !IsIdentifierStart(source[position]))
            {
                span = default;
                return false;
            }
            int start = position++;
            while (position < limit && IsIdentifierCharacter(source[position]))
            {
                position++;
            }
            span = new TextSpan(start, position - start);
            return true;
        }

        private static bool TryReadQualifiedName(
            string source,
            ref int position,
            int limit,
            out TextSpan span
        )
        {
            int start = position;
            if (!TryReadIdentifier(source, ref position, limit, out _))
            {
                span = default;
                return false;
            }

            if (
                source.Substring(start, position - start) == "global"
                && position + 1 < limit
                && source[position] == ':'
                && source[position + 1] == ':'
            )
            {
                position += 2;
                if (!TryReadIdentifier(source, ref position, limit, out _))
                {
                    span = default;
                    return false;
                }
            }

            while (position < limit && source[position] == '.')
            {
                position++;
                if (!TryReadIdentifier(source, ref position, limit, out _))
                {
                    span = default;
                    return false;
                }
            }
            span = new TextSpan(start, position - start);
            return true;
        }

        private static bool TryResolveOdinAttribute(
            string writtenName,
            bool hasDirectOdinUsing,
            out string attributeName,
            out bool automaticRewrite
        )
        {
            const string GlobalQualifiedPrefix = "global::Sirenix.OdinInspector.";
            if (writtenName.StartsWith(GlobalQualifiedPrefix, StringComparison.Ordinal))
            {
                string resolvedAttributeName = TrimAttributeSuffix(
                    writtenName.Substring(GlobalQualifiedPrefix.Length)
                );
                bool recognized = ReportedOdinAttributes.Contains(resolvedAttributeName);
                attributeName = resolvedAttributeName;
                automaticRewrite = recognized;
                return recognized;
            }

            const string QualifiedPrefix = "Sirenix.OdinInspector.";
            if (writtenName.StartsWith(QualifiedPrefix, StringComparison.Ordinal))
            {
                string resolvedAttributeName = TrimAttributeSuffix(
                    writtenName.Substring(QualifiedPrefix.Length)
                );
                bool recognized = ReportedOdinAttributes.Contains(resolvedAttributeName);
                attributeName = resolvedAttributeName;
                automaticRewrite = false;
                return recognized;
            }

            string unqualifiedAttributeName = TrimAttributeSuffix(writtenName);
            bool unqualifiedRecognized =
                hasDirectOdinUsing
                && writtenName.IndexOf('.') < 0
                && ReportedOdinAttributes.Contains(unqualifiedAttributeName);
            attributeName = unqualifiedAttributeName;
            automaticRewrite = false;
            return unqualifiedRecognized;
        }

        private static bool IsIdentifierCharacter(char character)
        {
            return char.IsLetterOrDigit(character) || character == '_';
        }

        private static bool IsProvableFieldOrPropertyTarget(
            string source,
            int bracketStart,
            int bracketEnd,
            SourceContext sourceContext
        )
        {
            if (!sourceContext.IsDirectTypeBody(bracketStart))
            {
                return false;
            }

            int position = bracketEnd + 1;
            SkipWhitespace(source, ref position, source.Length);
            while (position < source.Length && source[position] == '[')
            {
                int nextBracketEnd = FindAttributeListEnd(source, position);
                if (nextBracketEnd < 0)
                {
                    return false;
                }
                position = nextBracketEnd + 1;
                SkipWhitespace(source, ref position, source.Length);
            }

            int declarationStart = position;
            int angleDepth = 0;
            for (; position < source.Length; position++)
            {
                char character = source[position];
                if (character == '<')
                {
                    angleDepth++;
                    continue;
                }
                if (character == '>' && 0 < angleDepth)
                {
                    angleDepth--;
                    continue;
                }
                if (0 < angleDepth)
                {
                    continue;
                }
                if (character == '(')
                {
                    return false;
                }
                if (character == ';' || character == '=' || character == '{' || character == ',')
                {
                    return HeaderCanDeclareFieldOrProperty(source, declarationStart, position);
                }
            }
            return false;
        }

        private static bool HeaderCanDeclareFieldOrProperty(string source, int start, int end)
        {
            string header = source.Substring(start, end - start);
            string[] rejectedTokens =
            {
                "class",
                "delegate",
                "enum",
                "event",
                "interface",
                "operator",
                "record",
                "struct",
            };
            foreach (string token in rejectedTokens)
            {
                if (ContainsIdentifier(header, token, out _))
                {
                    return false;
                }
            }

            int identifierCount = 0;
            int position = 0;
            while (position < header.Length)
            {
                if (TryReadIdentifier(header, ref position, header.Length, out _))
                {
                    identifierCount++;
                }
                else
                {
                    position++;
                }
            }
            return 2 <= identifierCount;
        }

        private static bool HasCompilationUnitDirectOdinUsing(
            string source,
            SourceContext sourceContext
        )
        {
            foreach (Match match in DirectOdinUsing.Matches(source))
            {
                if (sourceContext.IsCompilationUnit(match.Index))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool LooksLikeAttributeList(string source, int bracketStart)
        {
            int previous = bracketStart - 1;
            while (0 <= previous && char.IsWhiteSpace(source[previous]))
            {
                previous--;
            }

            if (previous < 0)
            {
                return true;
            }

            char character = source[previous];
            return character == '{'
                || character == '}'
                || character == ';'
                || character == ']'
                || character == '('
                || character == ',';
        }

        private static bool IsIdentifierStart(char character)
        {
            return char.IsLetter(character) || character == '_';
        }

        private readonly struct Replacement
        {
            internal readonly int Start;
            internal readonly int Length;
            internal readonly string Value;

            internal Replacement(int start, int length, string value)
            {
                Start = start;
                Length = length;
                Value = value;
            }
        }

        private readonly struct TextSpan
        {
            internal readonly int Start;
            internal readonly int Length;

            internal TextSpan(int start, int length)
            {
                Start = start;
                Length = length;
            }
        }

        private sealed class LineMap
        {
            private readonly int[] lineStarts;

            internal LineMap(string source)
            {
                List<int> starts = new List<int> { 0 };
                for (int index = 0; index < source.Length; index++)
                {
                    if (source[index] == '\r')
                    {
                        if (index + 1 < source.Length && source[index + 1] == '\n')
                        {
                            index++;
                        }
                        starts.Add(index + 1);
                    }
                    else if (source[index] == '\n')
                    {
                        starts.Add(index + 1);
                    }
                }
                lineStarts = starts.ToArray();
            }

            internal int GetLine(int position)
            {
                int index = Array.BinarySearch(lineStarts, Math.Max(0, position));
                return 0 <= index ? index + 1 : ~index;
            }
        }

        private sealed class SourceContext
        {
            private readonly bool[] compilationUnit;
            private readonly bool[] directTypeBody;

            internal SourceContext(string source)
            {
                compilationUnit = new bool[source.Length + 1];
                directTypeBody = new bool[source.Length + 1];
                List<bool> braceKinds = new List<bool>();
                bool headerDeclaresType = false;
                bool headerHasParentheses = false;
                int bracketDepth = 0;
                int parenthesisDepth = 0;
                int index = 0;
                while (index < source.Length)
                {
                    bool atCompilationUnit = braceKinds.Count == 0;
                    bool atDirectTypeBody =
                        0 < braceKinds.Count
                        && braceKinds[braceKinds.Count - 1]
                        && parenthesisDepth == 0;
                    compilationUnit[index] = atCompilationUnit;
                    directTypeBody[index] = atDirectTypeBody;

                    if (
                        IsIdentifierStart(source[index])
                        && (index == 0 || !IsIdentifierCharacter(source[index - 1]))
                    )
                    {
                        int end = index + 1;
                        while (end < source.Length && IsIdentifierCharacter(source[end]))
                        {
                            end++;
                        }
                        if (
                            bracketDepth == 0
                            && (index == 0 || source[index - 1] != '@')
                            && (
                                IdentifierEquals(source, index, end, "class")
                                || IdentifierEquals(source, index, end, "interface")
                                || IdentifierEquals(source, index, end, "record")
                                || IdentifierEquals(source, index, end, "struct")
                            )
                        )
                        {
                            headerDeclaresType = true;
                        }
                        for (int fill = index + 1; fill < end; fill++)
                        {
                            compilationUnit[fill] = atCompilationUnit;
                            directTypeBody[fill] = atDirectTypeBody;
                        }
                        index = end;
                        continue;
                    }

                    switch (source[index])
                    {
                        case '(':
                            parenthesisDepth++;
                            if (bracketDepth == 0)
                            {
                                headerHasParentheses = true;
                            }
                            break;
                        case ')':
                            if (0 < parenthesisDepth)
                            {
                                parenthesisDepth--;
                            }
                            break;
                        case '[':
                            bracketDepth++;
                            break;
                        case ']':
                            if (0 < bracketDepth)
                            {
                                bracketDepth--;
                            }
                            break;
                        case '{':
                            braceKinds.Add(headerDeclaresType && !headerHasParentheses);
                            headerDeclaresType = false;
                            headerHasParentheses = false;
                            break;
                        case '}':
                            if (0 < braceKinds.Count)
                            {
                                braceKinds.RemoveAt(braceKinds.Count - 1);
                            }
                            headerDeclaresType = false;
                            headerHasParentheses = false;
                            break;
                        case ';':
                            headerDeclaresType = false;
                            headerHasParentheses = false;
                            break;
                    }
                    index++;
                }
                compilationUnit[source.Length] = braceKinds.Count == 0;
                directTypeBody[source.Length] =
                    0 < braceKinds.Count
                    && braceKinds[braceKinds.Count - 1]
                    && parenthesisDepth == 0;
            }

            private static bool IdentifierEquals(string source, int start, int end, string expected)
            {
                if (end - start != expected.Length)
                {
                    return false;
                }
                for (int index = 0; index < expected.Length; index++)
                {
                    if (source[start + index] != expected[index])
                    {
                        return false;
                    }
                }
                return true;
            }

            internal bool IsCompilationUnit(int position)
            {
                return 0 <= position
                    && position < compilationUnit.Length
                    && compilationUnit[position];
            }

            internal bool IsDirectTypeBody(int position)
            {
                return 0 <= position
                    && position < directTypeBody.Length
                    && directTypeBody[position];
            }
        }
    }

    internal sealed class OdinMigrationAnalysis
    {
        internal string UpgradedSource { get; }

        internal IReadOnlyList<OdinMigrationChange> Changes { get; }

        internal int ReplacementCount => Changes.Count;

        internal IReadOnlyList<OdinMigrationFinding> Blockers { get; }

        internal IReadOnlyList<OdinMigrationFinding> ManualReviews { get; }

        internal string Failure { get; }

        internal bool IsValid => Failure == null;

        internal OdinMigrationAnalysis(
            string upgradedSource,
            IReadOnlyList<OdinMigrationChange> changes,
            IReadOnlyList<OdinMigrationFinding> blockers,
            IReadOnlyList<OdinMigrationFinding> manualReviews,
            string failure
        )
        {
            UpgradedSource = upgradedSource;
            Changes = changes;
            Blockers = blockers;
            ManualReviews = manualReviews;
            Failure = failure;
        }

        internal static OdinMigrationAnalysis Invalid(string failure)
        {
            return new OdinMigrationAnalysis(
                string.Empty,
                Array.Empty<OdinMigrationChange>(),
                Array.Empty<OdinMigrationFinding>(),
                Array.Empty<OdinMigrationFinding>(),
                failure
            );
        }
    }

    internal readonly struct OdinMigrationChange
    {
        internal readonly int Line;
        internal readonly string From;
        internal readonly string To;

        internal OdinMigrationChange(int line, string from, string to)
        {
            Line = line;
            From = from;
            To = to;
        }
    }

    internal readonly struct OdinMigrationFinding
    {
        internal readonly int Line;
        internal readonly string Message;

        internal OdinMigrationFinding(int line, string message)
        {
            Line = line;
            Message = message;
        }
    }
}
