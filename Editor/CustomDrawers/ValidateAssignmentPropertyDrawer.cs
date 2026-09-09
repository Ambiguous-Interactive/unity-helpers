// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;

    /// <summary>
    /// Property drawer for <see cref="ValidateAssignmentAttribute"/> that displays a warning or error
    /// in the inspector when the decorated field is invalid (null, empty string, or empty collection).
    /// </summary>
    [CustomPropertyDrawer(typeof(ValidateAssignmentAttribute))]
    public sealed class ValidateAssignmentPropertyDrawer : PropertyDrawer
    {
        /// <summary>
        /// Checks if the property value is invalid (null, empty string, or empty collection).
        /// </summary>
        /// <param name="property">The property to check.</param>
        /// <returns>True if the property value is invalid.</returns>
        internal static bool IsPropertyInvalid(SerializedProperty property)
        {
            return ValidationShared.IsPropertyInvalid(property);
        }

        /// <summary>
        /// Draws a validation HelpBox for the property if it is invalid.
        /// Call this from custom editors for array/list properties that won't have
        /// their PropertyDrawer invoked at the array level.
        /// </summary>
        /// <returns>True if a HelpBox was drawn, false otherwise.</returns>
        internal static bool DrawValidationHelpBoxIfNeeded(
            SerializedProperty property,
            ValidateAssignmentAttribute validateAttribute
        )
        {
            return ValidationShared.DrawValidateAssignmentHelpBoxIfNeeded(
                property,
                validateAttribute
            );
        }

        /// <summary>
        /// Clears the height cache. Useful for tests or when font settings change.
        /// </summary>
        internal static void ClearHeightCache()
        {
            ValidationShared.ClearHeightCache();
        }

        /// <summary>
        /// Gets the total property height including the help box when the field is invalid.
        /// </summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float baseHeight = EditorGUI.GetPropertyHeight(property, label, true);

            if (ValidationShared.IsPropertyInvalid(property))
            {
                string message = GetMessage(property);
                float helpBoxHeight = ValidationShared.GetHelpBoxHeight(message);
                return baseHeight + helpBoxHeight + ValidationShared.HelpBoxPadding;
            }

            return baseHeight;
        }

        /// <summary>
        /// Draws the property field and displays a help box warning/error when the field is invalid.
        /// </summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            try
            {
                bool isInvalid = ValidationShared.IsPropertyInvalid(property);
                float propertyHeight = EditorGUI.GetPropertyHeight(property, label, true);

                if (isInvalid)
                {
                    string message = GetMessage(property);
                    float helpBoxHeight = ValidationShared.GetHelpBoxHeight(message);

                    Rect helpBoxRect = new(position.x, position.y, position.width, helpBoxHeight);
                    Rect propertyRect = new(
                        position.x,
                        position.y + helpBoxHeight + ValidationShared.HelpBoxPadding,
                        position.width,
                        propertyHeight
                    );

                    MessageType messageType = GetMessageType();
                    EditorGUI.HelpBox(helpBoxRect, message, messageType);
                    EditorGUI.PropertyField(propertyRect, property, label, true);
                }
                else
                {
                    EditorGUI.PropertyField(position, property, label, true);
                }
            }
            finally
            {
                EditorGUI.EndProperty();
            }
        }

        /// <summary>
        /// Creates a UI Toolkit visual element for the property, including help box when invalid.
        /// </summary>
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            VisualElement container = new();
            container.style.flexDirection = FlexDirection.Column;

            HelpBox helpBox = new(GetMessage(property), GetHelpBoxMessageType());
            helpBox.style.display = ValidationShared.IsPropertyInvalid(property)
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            PropertyField propertyField = new(property);
            propertyField.label = property.displayName;

            propertyField.RegisterValueChangeCallback(evt =>
            {
                bool isInvalid = ValidationShared.IsPropertyInvalid(property);
                helpBox.text = GetMessage(property);
                helpBox.style.display = isInvalid ? DisplayStyle.Flex : DisplayStyle.None;
            });

            container.Add(helpBox);
            container.Add(propertyField);

            return container;
        }

        private string GetMessage(SerializedProperty property)
        {
            ValidateAssignmentAttribute validateAttribute =
                attribute as ValidateAssignmentAttribute;
            return ValidationShared.GetValidateAssignmentMessage(property, validateAttribute);
        }

        private MessageType GetMessageType()
        {
            ValidateAssignmentAttribute validateAttribute =
                attribute as ValidateAssignmentAttribute;
            return ValidationShared.GetMessageType(validateAttribute);
        }

        private HelpBoxMessageType GetHelpBoxMessageType()
        {
            ValidateAssignmentAttribute validateAttribute =
                attribute as ValidateAssignmentAttribute;
            return ValidationShared.GetHelpBoxMessageType(validateAttribute);
        }
    }
#endif
}
