// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    [CustomPropertyDrawer(typeof(SerializableNullable<>), true)]
    public sealed class SerializableNullablePropertyDrawer : PropertyDrawer
    {
        /// <summary>
        /// Calculates the vertical space the drawer needs, accounting for the nullable value field.
        /// </summary>
        /// <param name="property">The serialized nullable wrapper.</param>
        /// <param name="label">The label rendered for the field.</param>
        /// <returns>The height required to draw the control.</returns>
        /// <example>
        /// <code>
        /// public SerializableNullable&lt;float&gt; speed;
        /// </code>
        /// </example>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            SerializedProperty hasValueProperty = property.FindPropertyRelative(
                SerializableNullableSerializedPropertyNames.HasValue
            );
            SerializedProperty valueProperty = property.FindPropertyRelative(
                SerializableNullableSerializedPropertyNames.Value
            );
            if (hasValueProperty == null)
            {
                return EditorGUI.GetPropertyHeight(property, label, true);
            }

            float lineHeight = EditorGUIUtility.singleLineHeight;
            if (!hasValueProperty.boolValue || valueProperty == null)
            {
                return lineHeight;
            }

            float valueHeight = EditorGUI.GetPropertyHeight(valueProperty, true);
            return Mathf.Max(lineHeight, valueHeight);
        }

        /// <summary>
        /// Draws the nullable toggle alongside the wrapped property field inside the inspector.
        /// </summary>
        /// <param name="position">The rectangle provided by Unity.</param>
        /// <param name="property">The serialized nullable wrapper.</param>
        /// <param name="label">The label shown for the field.</param>
        /// <example>
        /// <code>
        /// EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(speed)));
        /// </code>
        /// </example>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty hasValueProperty = property.FindPropertyRelative(
                SerializableNullableSerializedPropertyNames.HasValue
            );
            SerializedProperty valueProperty = property.FindPropertyRelative(
                SerializableNullableSerializedPropertyNames.Value
            );

            if (hasValueProperty == null || valueProperty == null)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            Rect fieldRect = EditorGUI.PrefixLabel(
                position,
                GUIUtility.GetControlID(FocusType.Passive),
                label
            );
            using IndentLevelScope indentScope = IndentLevelScope.AtLevel(0);

            float toggleWidth = EditorGUIUtility.singleLineHeight;
            float toggleHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float toggleY = position.y + (position.height - toggleHeight) * 0.5f;

            Rect toggleRect = new(fieldRect.x, toggleY, toggleWidth, toggleHeight);

            EditorGUI.BeginChangeCheck();
            bool hasValue = hasValueProperty.boolValue;
            bool updatedHasValue = EditorGUI.Toggle(toggleRect, hasValue);
            if (EditorGUI.EndChangeCheck())
            {
                hasValueProperty.boolValue = updatedHasValue;
            }

            if (updatedHasValue)
            {
                float valueWidth = Mathf.Max(0f, fieldRect.width - toggleWidth - spacing);
                Rect valueRect = new(
                    toggleRect.xMax + spacing,
                    position.y,
                    valueWidth,
                    position.height
                );
                EditorGUI.PropertyField(valueRect, valueProperty, GUIContent.none, true);
            }

            EditorGUI.EndProperty();
        }
    }
#endif
}
