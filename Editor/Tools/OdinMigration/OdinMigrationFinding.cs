// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System.Collections.Generic;

    /// <summary>Describes a source or serialized-data review finding.</summary>
    public readonly struct OdinMigrationFinding : global::System.IEquatable<OdinMigrationFinding>
    {
        /// <summary>Gets the one-based source line.</summary>
        public readonly int Line;

        /// <summary>Gets the finding description.</summary>
        public readonly string Message;

        internal OdinMigrationFinding(int line, string message)
        {
            Line = line;
            Message = message;
        }

        /// <summary>Compares the stored fields with another value.</summary>
        public bool Equals(OdinMigrationFinding other)
        {
            return Line.Equals(other.Line)
                && EqualityComparer<string>.Default.Equals(Message, other.Message);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is OdinMigrationFinding other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(Line, Message);
        }
    }
}
