// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;

    /// <summary>Declares the original enum-to-Unity-object-list type of a retained dictionary field.</summary>
    public sealed class OdinMigrationDictionaryField
    {
        /// <summary>Gets the original field name.</summary>
        public string FieldName { get; }

        /// <summary>Gets the original enum key type.</summary>
        public Type KeyType { get; }

        /// <summary>Gets the original Unity object element type.</summary>
        public Type ElementType { get; }

        private OdinMigrationDictionaryField(string fieldName, Type keyType, Type elementType)
        {
            FieldName = fieldName;
            KeyType = keyType;
            ElementType = elementType;
        }

        /// <summary>Captures the original field types without resolving any recorded type names.</summary>
        public static OdinMigrationDictionaryField Create<TKey, TObject>(string fieldName)
            where TKey : struct, Enum
            where TObject : UnityEngine.Object
        {
            return new OdinMigrationDictionaryField(fieldName, typeof(TKey), typeof(TObject));
        }
    }
}
