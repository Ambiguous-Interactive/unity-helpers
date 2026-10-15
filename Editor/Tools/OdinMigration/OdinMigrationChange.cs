// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System.Collections.Generic;

    /// <summary>Describes a proven inspector attribute replacement.</summary>
    public readonly struct OdinMigrationChange : global::System.IEquatable<OdinMigrationChange>
    {
        /// <summary>Gets the one-based source line.</summary>
        public readonly int Line;

        /// <summary>Gets the original attribute name.</summary>
        public readonly string From;

        /// <summary>Gets the replacement attribute name.</summary>
        public readonly string To;

        internal OdinMigrationChange(int line, string from, string to)
        {
            Line = line;
            From = from;
            To = to;
        }

        /// <summary>Compares the stored fields with another value.</summary>
        public bool Equals(OdinMigrationChange other)
        {
            return Line.Equals(other.Line)
                && EqualityComparer<string>.Default.Equals(From, other.From)
                && EqualityComparer<string>.Default.Equals(To, other.To);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is OdinMigrationChange other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(
                Line,
                From,
                To
            );
        }
    }
}
