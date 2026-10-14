// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    /// <summary>Preserves an original asset's text and its proposed retained-data conversion.</summary>
    public sealed class OdinMigrationAssetProposal
    {
        /// <summary>Gets the original project-relative asset path.</summary>
        public string AssetPath { get; }

        /// <summary>Gets the complete original asset text.</summary>
        public string OriginalSource { get; }

        /// <summary>Gets the complete proposed asset text.</summary>
        public string ProposedSource { get; }

        /// <summary>Gets the number of converted Unity documents.</summary>
        public int ConvertedDocuments { get; }

        internal OdinMigrationAssetProposal(
            string assetPath,
            string originalSource,
            string proposedSource,
            int convertedDocuments
        )
        {
            AssetPath = assetPath;
            OriginalSource = originalSource;
            ProposedSource = proposedSource;
            ConvertedDocuments = convertedDocuments;
        }
    }
}
