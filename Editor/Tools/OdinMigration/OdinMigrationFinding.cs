// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    /// <summary>Describes a source or serialized-data review finding.</summary>
    public readonly struct OdinMigrationFinding
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
    }
}
