// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// Draws a <see cref="SerializableList{T}"/> as the list it wraps, so adopting the wrapper to
    /// survive Unity's nested-collection limit costs no extra foldout in the inspector.
    /// </summary>
    [CustomPropertyDrawer(typeof(SerializableList<>), true)]
    public sealed class SerializableListPropertyDrawer : PropertyDrawer
    {
        /// <summary>
        /// Reports the height of the wrapped list.
        /// </summary>
        /// <param name="property">The serialized wrapper.</param>
        /// <param name="label">The label rendered for the field.</param>
        /// <returns>The height required to draw the wrapped list.</returns>
        /// <example>
        /// <code><![CDATA[
        /// public SerializableList<int> weights;
        /// ]]></code>
        /// </example>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            SerializedProperty itemsProperty = property.FindPropertyRelative(
                SerializableListSerializedPropertyNames.Items
            );
            if (itemsProperty == null)
            {
                return EditorGUI.GetPropertyHeight(property, label, true);
            }

            return EditorGUI.GetPropertyHeight(itemsProperty, label, true);
        }

        /// <summary>
        /// Draws the wrapped list under the wrapper's own label.
        /// </summary>
        /// <param name="position">The rectangle provided by Unity.</param>
        /// <param name="property">The serialized wrapper.</param>
        /// <param name="label">The label shown for the field.</param>
        /// <example>
        /// <code><![CDATA[
        /// EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(weights)));
        /// ]]></code>
        /// </example>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty itemsProperty = property.FindPropertyRelative(
                SerializableListSerializedPropertyNames.Items
            );
            if (itemsProperty == null)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.PropertyField(position, itemsProperty, label, true);
            EditorGUI.EndProperty();
        }
    }
#endif
}
