// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration
{
#if UNITY_EDITOR
    /// <summary>Preserves a dictionary field's compiler-resolved types and source location.</summary>
    public sealed class OdinMigrationCompiledField
    {
        /// <summary>Gets the declared field name.</summary>
        public string Name { get; }

        /// <summary>Gets the original enum's CLR metadata name.</summary>
        public string KeyTypeName { get; }

        /// <summary>Gets the original Unity element's CLR metadata name.</summary>
        public string ElementTypeName { get; }

        /// <summary>Gets the enum's original assembly identity.</summary>
        public string KeyAssemblyName { get; }

        /// <summary>Gets the element's original assembly identity.</summary>
        public string ElementAssemblyName { get; }

        /// <summary>Gets the source path containing the field declaration.</summary>
        public string SourcePath { get; }

        /// <summary>Gets the declared type's first source character.</summary>
        public int TypeStart { get; }

        /// <summary>Gets the declared type's source character count.</summary>
        public int TypeLength { get; }

        /// <summary>Captures the original field identity emitted by the compiler.</summary>
        public OdinMigrationCompiledField(
            string name,
            string keyTypeName,
            string elementTypeName,
            string keyAssemblyName,
            string elementAssemblyName,
            string sourcePath,
            int typeStart,
            int typeLength
        )
        {
            Name = name;
            KeyTypeName = keyTypeName;
            ElementTypeName = elementTypeName;
            KeyAssemblyName = keyAssemblyName;
            ElementAssemblyName = elementAssemblyName;
            SourcePath = sourcePath;
            TypeStart = typeStart;
            TypeLength = typeLength;
        }
    }
#endif
}
