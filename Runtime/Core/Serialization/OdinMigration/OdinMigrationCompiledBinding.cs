// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;

    /// <summary>Preserves the source and dictionary declarations resolved for a Unity type.</summary>
    public sealed class OdinMigrationCompiledBinding
    {
        /// <summary>Gets the compiled Unity type.</summary>
        public Type OriginalType { get; }

        /// <summary>Gets the original source snapshots.</summary>
        public IReadOnlyList<OdinMigrationCompiledSource> Sources { get; }

        /// <summary>Gets the original dictionary field declarations.</summary>
        public IReadOnlyList<OdinMigrationCompiledField> Fields { get; }

        /// <summary>Gets source replacements for original Odin base classes.</summary>
        public IReadOnlyList<OdinMigrationCompiledSourceEdit> BaseEdits { get; }

        /// <summary>Gets original declaring types whose source changes affect their derived Unity objects.</summary>
        public IReadOnlyList<Type> AffectedTypes { get; }

        /// <summary>Captures immutable compiler metadata for a Unity type.</summary>
        public OdinMigrationCompiledBinding(
            Type originalType,
            OdinMigrationCompiledSource[] sources,
            OdinMigrationCompiledField[] fields,
            OdinMigrationCompiledSourceEdit[] baseEdits = null,
            Type[] affectedTypes = null
        )
        {
            OriginalType = originalType;
            Sources = Array.AsReadOnly(
                sources == null
                    ? Array.Empty<OdinMigrationCompiledSource>()
                    : (OdinMigrationCompiledSource[])sources.Clone()
            );
            Fields = Array.AsReadOnly(
                fields == null
                    ? Array.Empty<OdinMigrationCompiledField>()
                    : (OdinMigrationCompiledField[])fields.Clone()
            );
            BaseEdits = Array.AsReadOnly(
                baseEdits == null
                    ? Array.Empty<OdinMigrationCompiledSourceEdit>()
                    : (OdinMigrationCompiledSourceEdit[])baseEdits.Clone()
            );
            AffectedTypes = Array.AsReadOnly(
                affectedTypes == null ? Array.Empty<Type>() : (Type[])affectedTypes.Clone()
            );
        }
    }
#endif
}
