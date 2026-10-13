// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    /// <summary>Preserves original source text and its proposed migration replacement.</summary>
    public sealed class OdinMigrationSourceProposal
    {
        /// <summary>Gets the original compiler source path.</summary>
        public string SourcePath { get; }

        /// <summary>Gets the source text verified against the compiler checksum.</summary>
        public string OriginalSource { get; }

        /// <summary>Gets the proposed source text without writing it.</summary>
        public string ProposedSource { get; }

        internal OdinMigrationSourceProposal(
            string sourcePath,
            string originalSource,
            string proposedSource
        )
        {
            SourcePath = sourcePath;
            OriginalSource = originalSource;
            ProposedSource = proposedSource;
        }
    }
}
