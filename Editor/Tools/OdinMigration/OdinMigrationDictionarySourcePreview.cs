// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration;

    /// <summary>Holds proposed source replacements for compiler-bound dictionary fields.</summary>
    public sealed class OdinMigrationDictionarySourcePreview
    {
        /// <summary>Gets the verified original compiler binding.</summary>
        public OdinMigrationCompiledBinding Binding { get; }

        /// <summary>Gets immutable original and replacement sources by compiler source path.</summary>
        public IReadOnlyDictionary<string, OdinMigrationSourceProposal> Sources { get; }

        internal OdinMigrationDictionarySourcePreview(
            OdinMigrationCompiledBinding binding,
            Dictionary<string, OdinMigrationSourceProposal> sources
        )
        {
            Binding = binding;
            Sources = new ReadOnlyDictionary<string, OdinMigrationSourceProposal>(
                new Dictionary<string, OdinMigrationSourceProposal>(
                    sources,
                    System.StringComparer.Ordinal
                )
            );
        }
    }
}
