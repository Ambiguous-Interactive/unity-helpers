// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR
    using System;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Thin wrapper that forwards SerializableType editing to the underlying StringInList-enabled field.
    /// </summary>
    [CustomPropertyDrawer(typeof(SerializableType))]
    public sealed class SerializableTypeDrawer : PropertyDrawer
    {
        /// <summary>
        /// The number of property paths whose resolved child property is retained.
        /// </summary>
        /// <remarks>
        /// Each entry holds a <c>SerializedProperty</c>, which roots its <c>SerializedObject</c> and
        /// so the inspected asset or scene object, and the key varies with every selection because
        /// an array contributes one path per element. Sized above the paths one selection can
        /// present so a single inspector never evicts its own entries.
        /// </remarks>
        private const int MaxPropertyCacheEntries = 512;

        private static readonly BoundedLruCache<string, CachedProperty> PropertyCache = new(
            static () => MaxPropertyCacheEntries,
            StringComparer.Ordinal
        );

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            SerializedProperty typeNameProperty = GetCachedTypeNameProperty(property);
            if (typeNameProperty == null)
            {
                return EditorGUIUtility.singleLineHeight;
            }
            return EditorGUI.GetPropertyHeight(typeNameProperty, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty typeNameProperty = GetCachedTypeNameProperty(property);
            if (typeNameProperty == null)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            EditorGUI.PropertyField(position, typeNameProperty, label, true);
        }

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            SerializedProperty typeNameProperty = property.FindPropertyRelative(
                SerializableType.SerializedPropertyNames.AssemblyQualifiedName
            );
            if (typeNameProperty == null)
            {
                return new PropertyField(property);
            }

            return new PropertyField(typeNameProperty, property.displayName);
        }

        private static SerializedProperty GetCachedTypeNameProperty(SerializedProperty property)
        {
            string key = property.propertyPath;
            int currentFrame = Time.frameCount;

            if (PropertyCache.TryGet(key, out CachedProperty cached))
            {
                if (cached.lastCacheFrame == currentFrame && cached.typeNameProperty != null)
                {
                    return cached.typeNameProperty;
                }
            }
            else
            {
                cached = new CachedProperty();
                PropertyCache.Set(key, cached);
            }

            cached.typeNameProperty = property.FindPropertyRelative(
                SerializableType.SerializedPropertyNames.AssemblyQualifiedName
            );
            cached.lastCacheFrame = currentFrame;
            return cached.typeNameProperty;
        }

        private sealed class CachedProperty
        {
            public SerializedProperty typeNameProperty;
            public int lastCacheFrame = -1;
        }
    }
#endif
}
