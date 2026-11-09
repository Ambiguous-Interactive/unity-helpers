// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>A bounded mapping or sequence item within one authored YAML document.</summary>
    /// <remarks>The region borrows the parsed document and original lines; keep both unchanged while reading it.</remarks>
    public readonly struct AuthoredAssetRegion : IEquatable<AuthoredAssetRegion>
    {
        /// <summary>Whether this value identifies a region returned by the YAML reader.</summary>
        public bool IsValid => Document != null && SourceLines != null;

        /// <summary>The first included one-based source line.</summary>
        public int StartLineNumber { get; }

        /// <summary>The one-based line immediately after the region.</summary>
        public int EndLineNumber { get; }

        internal AuthoredAssetDocument Document { get; }
        internal System.Collections.Generic.IReadOnlyList<string> SourceLines { get; }
        internal int FieldColumn { get; }
        internal int SequenceDepth { get; }

        internal AuthoredAssetRegion(
            AuthoredAssetDocument document,
            System.Collections.Generic.IReadOnlyList<string> lines,
            int startLineNumber,
            int endLineNumber,
            int fieldColumn,
            int sequenceDepth
        )
        {
            Document = document;
            SourceLines = lines;
            StartLineNumber = startLineNumber;
            EndLineNumber = endLineNumber;
            FieldColumn = fieldColumn;
            SequenceDepth = sequenceDepth;
        }

        /// <summary>Compares source identity, bounds, and mapping depth.</summary>
        public bool Equals(AuthoredAssetRegion other)
        {
            return ReferenceEquals(Document, other.Document)
                && ReferenceEquals(SourceLines, other.SourceLines)
                && StartLineNumber == other.StartLineNumber
                && EndLineNumber == other.EndLineNumber
                && FieldColumn == other.FieldColumn
                && SequenceDepth == other.SequenceDepth;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is AuthoredAssetRegion other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Objects.HashCode(
                Document,
                SourceLines,
                StartLineNumber,
                EndLineNumber,
                FieldColumn,
                SequenceDepth
            );
        }
    }
#endif
}
