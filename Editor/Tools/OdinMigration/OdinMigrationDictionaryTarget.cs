// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using Object = UnityEngine.Object;

    /// <summary>Identifies one retained Unity document and its original dictionary schemas.</summary>
    public sealed class OdinMigrationDictionaryTarget
    {
        /// <summary>Gets the existing project-relative asset path.</summary>
        public string AssetPath { get; }

        /// <summary>Gets the owning document's file identifier.</summary>
        public long FileId { get; }

        /// <summary>Gets the immutable original dictionary schemas.</summary>
        public IReadOnlyList<OdinMigrationDictionaryField> Fields { get; }

        /// <summary>Gets the captured live objects in retained reference-table order.</summary>
        public IReadOnlyList<Object> ReferencedObjects { get; }

        /// <summary>Captures a document selection without loading or writing its asset.</summary>
        public OdinMigrationDictionaryTarget(
            string assetPath,
            long fileId,
            OdinMigrationDictionaryField[] fields,
            Object[] referencedObjects
        )
        {
            AssetPath = assetPath;
            FileId = fileId;
            Fields = Array.AsReadOnly(
                fields == null
                    ? Array.Empty<OdinMigrationDictionaryField>()
                    : (OdinMigrationDictionaryField[])fields.Clone()
            );
            ReferencedObjects = Array.AsReadOnly(
                referencedObjects == null
                    ? Array.Empty<Object>()
                    : (Object[])referencedObjects.Clone()
            );
        }
    }
}
