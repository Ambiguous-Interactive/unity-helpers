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

        private readonly Func<UnityEngine.Object, bool> _acceptsObject;

        private OdinMigrationDictionaryField(
            string fieldName,
            Type keyType,
            Type elementType,
            Func<UnityEngine.Object, bool> acceptsObject
        )
        {
            FieldName = fieldName;
            KeyType = keyType;
            ElementType = elementType;
            _acceptsObject = acceptsObject;
        }

        /// <summary>Captures the original field types without resolving any recorded type names.</summary>
        public static OdinMigrationDictionaryField Create<TKey, TObject>(string fieldName)
            where TKey : struct, Enum
            where TObject : UnityEngine.Object
        {
            return new OdinMigrationDictionaryField(
                fieldName,
                typeof(TKey),
                typeof(TObject),
                IsExpectedObject<TObject>
            );
        }

        private static bool IsExpectedObject<TObject>(UnityEngine.Object value)
            where TObject : UnityEngine.Object
        {
            return value is TObject;
        }

        /// <summary>Checks whether a live object or null slot fits the declared element type.</summary>
        public bool AcceptsObject(UnityEngine.Object value)
        {
            return value == null || _acceptsObject(value);
        }
    }
}
