// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;

    /// <summary>Identifies a saved Unity document potentially affected by dictionary source changes.</summary>
    public sealed class OdinMigrationAffectedDocument
    {
        /// <summary>Gets the project-relative asset path.</summary>
        public string AssetPath { get; }

        /// <summary>Gets the saved document's local file identifier.</summary>
        public long FileId { get; }

        /// <summary>Gets the original script GUID.</summary>
        public string ScriptGuid { get; }

        /// <summary>Gets the original compiled Unity type.</summary>
        public Type OriginalType { get; }

        /// <summary>Gets whether this document carries an Odin serialization payload.</summary>
        public bool HasRetainedData { get; }

        /// <summary>Gets whether this document is included in the paired preview.</summary>
        public bool Selected { get; }

        internal OdinMigrationAffectedDocument(
            string assetPath,
            long fileId,
            string scriptGuid,
            Type originalType,
            bool hasRetainedData,
            bool selected
        )
        {
            AssetPath = assetPath;
            FileId = fileId;
            ScriptGuid = scriptGuid;
            OriginalType = originalType;
            HasRetainedData = hasRetainedData;
            Selected = selected;
        }
    }
}
