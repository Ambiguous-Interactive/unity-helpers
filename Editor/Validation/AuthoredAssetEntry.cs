// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
    using System.Collections.Generic;
#if UNITY_EDITOR
    /// <summary>
    /// One <c>key: value</c> pair Unity wrote inside a serialized document, with the lines it spans.
    /// </summary>
    /// <remarks>
    /// The line numbers are what makes a finding actionable: a check reports
    /// <c>path:line: field</c> and the reader opens the asset on the offending row rather than
    /// searching a file that is thousands of lines long.
    /// </remarks>
    public readonly struct AuthoredAssetEntry : global::System.IEquatable<AuthoredAssetEntry>
    {
        /// <summary>The key Unity wrote, with any surrounding quotes removed.</summary>
        public string Key { get; }

        /// <summary>
        /// The value written on the same line, trimmed, or the empty string when the value is a
        /// block sequence or a nested mapping on the lines that follow.
        /// </summary>
        public string InlineValue { get; }

        /// <summary>How many leading spaces the key carries.</summary>
        public int Indent { get; }

        /// <summary>The one-based line the key is on.</summary>
        public int LineNumber { get; }

        /// <summary>The one-based line after the last line this entry's value occupies.</summary>
        public int EndLineNumber { get; }

        /// <summary>Whether the value continues onto the lines that follow the key.</summary>
        public bool HasBlockValue => LineNumber + 1 < EndLineNumber;

        /// <summary>Initializes a new instance of the <see cref="AuthoredAssetEntry"/> struct.</summary>
        /// <param name="key">The key with any surrounding quotes removed.</param>
        /// <param name="inlineValue">The value on the same line, trimmed; empty when the value is a block.</param>
        /// <param name="indent">How many leading spaces the key carries.</param>
        /// <param name="lineNumber">The one-based line the key is on.</param>
        /// <param name="endLineNumber">The one-based line after the last line the value occupies.</param>
        public AuthoredAssetEntry(
            string key,
            string inlineValue,
            int indent,
            int lineNumber,
            int endLineNumber
        )
        {
            Key = key;
            InlineValue = inlineValue;
            Indent = indent;
            LineNumber = lineNumber;
            EndLineNumber = endLineNumber;
        }

        /// <summary>Renders the entry as the location a reader can open.</summary>
        /// <returns>A human-readable description.</returns>
        public override string ToString()
        {
            return $"line {LineNumber}: {Key}: {InlineValue}";
        }

        /// <summary>Compares the stored fields with another value.</summary>
        public bool Equals(AuthoredAssetEntry other)
        {
            return EqualityComparer<string>.Default.Equals(Key, other.Key)
                && EqualityComparer<string>.Default.Equals(InlineValue, other.InlineValue)
                && Indent.Equals(other.Indent)
                && LineNumber.Equals(other.LineNumber)
                && EndLineNumber.Equals(other.EndLineNumber);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is AuthoredAssetEntry other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(
                Key,
                InlineValue,
                Indent,
                LineNumber,
                EndLineNumber
            );
        }
    }
#endif
}
