// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;

    /// <summary>Describes a conservative Odin source migration.</summary>
    public sealed class OdinMigrationAnalysis
    {
        /// <summary>Gets source with proven attribute replacements.</summary>
        public string UpgradedSource { get; }

        /// <summary>Gets the planned source replacements.</summary>
        public IReadOnlyList<OdinMigrationChange> Changes { get; }

        /// <summary>Gets the number of planned replacements.</summary>
        public int ReplacementCount => Changes.Count;

        /// <summary>Gets findings that prevent automatic source edits.</summary>
        public IReadOnlyList<OdinMigrationFinding> Blockers { get; }

        /// <summary>Gets source findings requiring manual review.</summary>
        public IReadOnlyList<OdinMigrationFinding> ManualReviews { get; }

        /// <summary>Gets the analysis failure, or null on success.</summary>
        public string Failure { get; }

        /// <summary>Gets whether analysis completed successfully.</summary>
        public bool IsValid => Failure == null;

        internal OdinMigrationAnalysis(
            string upgradedSource,
            IReadOnlyList<OdinMigrationChange> changes,
            IReadOnlyList<OdinMigrationFinding> blockers,
            IReadOnlyList<OdinMigrationFinding> manualReviews,
            string failure
        )
        {
            UpgradedSource = upgradedSource;
            Changes = new List<OdinMigrationChange>(changes).AsReadOnly();
            Blockers = new List<OdinMigrationFinding>(blockers).AsReadOnly();
            ManualReviews = new List<OdinMigrationFinding>(manualReviews).AsReadOnly();
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
}
