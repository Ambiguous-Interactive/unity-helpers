// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration;

    /// <summary>Holds coordinated dictionary source and retained asset proposals without applying them.</summary>
    public sealed class OdinMigrationDictionaryMigrationPreview
    {
        /// <summary>Gets source proposals merged across all selected documents.</summary>
        public IReadOnlyDictionary<string, OdinMigrationSourceProposal> Sources { get; }

        /// <summary>Gets complete asset proposals by project-relative path.</summary>
        public IReadOnlyDictionary<string, OdinMigrationAssetProposal> Assets { get; }

        /// <summary>Gets the original compiler bindings used by the selected documents.</summary>
        public IReadOnlyList<OdinMigrationCompiledBinding> Bindings { get; }

        internal OdinMigrationDictionaryMigrationPreview(
            Dictionary<string, OdinMigrationSourceProposal> sources,
            Dictionary<string, OdinMigrationAssetProposal> assets,
            List<OdinMigrationCompiledBinding> bindings
        )
        {
            Sources = new ReadOnlyDictionary<string, OdinMigrationSourceProposal>(
                new Dictionary<string, OdinMigrationSourceProposal>(sources, StringComparer.Ordinal)
            );
            Assets = new ReadOnlyDictionary<string, OdinMigrationAssetProposal>(
                new Dictionary<string, OdinMigrationAssetProposal>(assets, StringComparer.Ordinal)
            );
            Bindings = bindings.AsReadOnly();
        }
    }
}
