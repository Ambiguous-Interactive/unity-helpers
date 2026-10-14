// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    /// <summary>Describes a proven inspector attribute replacement.</summary>
    public readonly struct OdinMigrationChange
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
    }
}
