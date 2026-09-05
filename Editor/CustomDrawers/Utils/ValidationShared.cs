// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Provides shared constants, validation logic, and helper methods for validation attribute drawers.
    /// </summary>
    /// <remarks>
    /// This utility class consolidates common code used by ValidateAssignment and WNotNull drawers
    /// (both standard PropertyDrawer and Odin Inspector implementations). By centralizing these
    /// elements, we ensure consistent behavior and eliminate code duplication.
    /// </remarks>
    public static class ValidationShared
    {
        /// <summary>
        /// Padding between the help box and the property field in pixels.
        /// </summary>
        public const float HelpBoxPadding = 2f;

        /// <summary>
        /// Default message format for ValidateAssignment validation failures.
        /// Use with string.Format where {0} is the field name.
        /// </summary>
        public const string ValidateAssignmentMessageFormat = "{0} is not assigned or is empty";

        /// <summary>
        /// Fallback message when field name cannot be determined for ValidateAssignment.
        /// </summary>
        public const string ValidateAssignmentFallbackMessage = "Field is not assigned or is empty";

        /// <summary>
        /// Default message format for WNotNull validation failures.
        /// Use with string.Format where {0} is the field name.
        /// </summary>
        public const string NotNullMessageFormat = "{0} must not be null";

        /// <summary>
        /// Fallback message when field name cannot be determined for WNotNull.
        /// </summary>
        public const string NotNullFallbackMessage = "Field is null or unassigned";

        private static readonly Dictionary<string, float> HelpBoxHeightCache = new(
            StringComparer.Ordinal
        );

        private static readonly GUIContent ReusableContent = new();

        /// <summary>
        /// Clears the help box height cache. Useful for tests or when font settings change.
        /// </summary>
        public static void ClearHeightCache()
        {
            HelpBoxHeightCache.Clear();
        }

        /// <summary>
        /// Calculates the height of a help box for the given message text.
        /// Results are cached for performance.
        /// </summary>
        /// <param name="message">The message to display in the help box.</param>
        /// <returns>The calculated height in pixels.</returns>
        public static float GetHelpBoxHeight(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return EditorGUIUtility.singleLineHeight * 2f;
            }

            if (HelpBoxHeightCache.TryGetValue(message, out float cachedHeight))
            {
                return cachedHeight;
            }

            ReusableContent.text = message;
            GUIStyle helpBoxStyle = EditorStyles.helpBox;
            float minHeight = EditorGUIUtility.singleLineHeight * 2f;
            float viewWidth = 600f;
            try
            {
                viewWidth = Mathf.Max(0f, EditorGUIUtility.currentViewWidth);
            }
            catch
            {
                // Called outside OnGUI context; use fallback
                viewWidth = 600f;
            }
            float calculatedHeight = helpBoxStyle.CalcHeight(ReusableContent, viewWidth - 40f);
            float height = Mathf.Max(minHeight, calculatedHeight);

            HelpBoxHeightCache[message] = height;
            return height;
        }

        /// <summary>
        /// Converts a <see cref="ValidateAssignmentMessageType"/> to an IMGUI <see cref="MessageType"/>.
        /// </summary>
        /// <param name="messageType">The validation message type.</param>
        /// <returns>The corresponding IMGUI message type.</returns>
        public static MessageType ToMessageType(ValidateAssignmentMessageType messageType)
        {
            return messageType switch
            {
                ValidateAssignmentMessageType.Error => MessageType.Error,
                _ => MessageType.Warning,
            };
        }

        /// <summary>
        /// Converts a <see cref="ValidateAssignmentMessageType"/> to a UI Toolkit <see cref="HelpBoxMessageType"/>.
        /// </summary>
        /// <param name="messageType">The validation message type.</param>
        /// <returns>The corresponding UI Toolkit help box message type.</returns>
        public static HelpBoxMessageType ToHelpBoxMessageType(
            ValidateAssignmentMessageType messageType
        )
        {
            return messageType switch
            {
                ValidateAssignmentMessageType.Error => HelpBoxMessageType.Error,
                _ => HelpBoxMessageType.Warning,
            };
        }

        /// <summary>
        /// Gets the IMGUI <see cref="MessageType"/> for a <see cref="ValidateAssignmentAttribute"/>.
        /// </summary>
        /// <param name="validateAttribute">The attribute, or null for default behavior.</param>
        /// <returns>The corresponding IMGUI message type (Warning if attribute is null).</returns>
        public static MessageType GetMessageType(ValidateAssignmentAttribute validateAttribute)
        {
            if (validateAttribute == null)
            {
                return MessageType.Warning;
            }

            return ToMessageType(validateAttribute.MessageType);
        }

        /// <summary>
        /// Gets the UI Toolkit <see cref="HelpBoxMessageType"/> for a <see cref="ValidateAssignmentAttribute"/>.
        /// </summary>
        /// <param name="validateAttribute">The attribute, or null for default behavior.</param>
        /// <returns>The corresponding UI Toolkit help box message type (Warning if attribute is null).</returns>
        public static HelpBoxMessageType GetHelpBoxMessageType(
            ValidateAssignmentAttribute validateAttribute
        )
        {
            if (validateAttribute == null)
            {
                return HelpBoxMessageType.Warning;
            }

            return ToHelpBoxMessageType(validateAttribute.MessageType);
        }

        /// <summary>
        /// Converts a <see cref="WNotNullMessageType"/> to an IMGUI <see cref="MessageType"/>.
        /// </summary>
        /// <param name="messageType">The not-null message type.</param>
        /// <returns>The corresponding IMGUI message type.</returns>
        public static MessageType ToMessageType(WNotNullMessageType messageType)
        {
            return messageType switch
            {
                WNotNullMessageType.Error => MessageType.Error,
                _ => MessageType.Warning,
            };
        }

        /// <summary>
        /// Converts a <see cref="WNotNullMessageType"/> to a UI Toolkit <see cref="HelpBoxMessageType"/>.
        /// </summary>
        /// <param name="messageType">The not-null message type.</param>
        /// <returns>The corresponding UI Toolkit help box message type.</returns>
        public static HelpBoxMessageType ToHelpBoxMessageType(WNotNullMessageType messageType)
        {
            return messageType switch
            {
                WNotNullMessageType.Error => HelpBoxMessageType.Error,
                _ => HelpBoxMessageType.Warning,
            };
        }

        /// <summary>
        /// Gets the IMGUI <see cref="MessageType"/> for a <see cref="WNotNullAttribute"/>.
        /// </summary>
        /// <param name="notNullAttribute">The attribute, or null for default behavior.</param>
        /// <returns>The corresponding IMGUI message type (Warning if attribute is null).</returns>
        public static MessageType GetMessageType(WNotNullAttribute notNullAttribute)
        {
            if (notNullAttribute == null)
            {
                return MessageType.Warning;
            }

            return ToMessageType(notNullAttribute.MessageType);
        }

        /// <summary>
        /// Gets the UI Toolkit <see cref="HelpBoxMessageType"/> for a <see cref="WNotNullAttribute"/>.
        /// </summary>
        /// <param name="notNullAttribute">The attribute, or null for default behavior.</param>
        /// <returns>The corresponding UI Toolkit help box message type (Warning if attribute is null).</returns>
        public static HelpBoxMessageType GetHelpBoxMessageType(WNotNullAttribute notNullAttribute)
        {
            if (notNullAttribute == null)
            {
                return HelpBoxMessageType.Warning;
            }

            return ToHelpBoxMessageType(notNullAttribute.MessageType);
        }

        /// <summary>
        /// Gets the validation message for a <see cref="ValidateAssignmentAttribute"/> using the property's display name.
        /// </summary>
        /// <param name="property">The serialized property being validated.</param>
        /// <param name="validateAttribute">The attribute, or null for default behavior.</param>
        /// <returns>The message to display in the help box.</returns>
        public static string GetValidateAssignmentMessage(
            SerializedProperty property,
            ValidateAssignmentAttribute validateAttribute
        )
        {
            if (validateAttribute != null && !string.IsNullOrEmpty(validateAttribute.CustomMessage))
            {
                return validateAttribute.CustomMessage;
            }

            string fieldName = property?.displayName ?? ValidateAssignmentFallbackMessage;
            return string.Format(ValidateAssignmentMessageFormat, fieldName);
        }

        /// <summary>
        /// Gets the validation message for a <see cref="ValidateAssignmentAttribute"/> using a custom field name.
        /// Primarily used by Odin Inspector drawers which use Property.NiceName.
        /// </summary>
        /// <param name="fieldName">The display name of the field.</param>
        /// <param name="validateAttribute">The attribute, or null for default behavior.</param>
        /// <returns>The message to display in the help box.</returns>
        public static string GetValidateAssignmentMessage(
            string fieldName,
            ValidateAssignmentAttribute validateAttribute
        )
        {
            if (validateAttribute != null && !string.IsNullOrEmpty(validateAttribute.CustomMessage))
            {
                return validateAttribute.CustomMessage;
            }

            string displayName = fieldName ?? ValidateAssignmentFallbackMessage;
            return string.Format(ValidateAssignmentMessageFormat, displayName);
        }

        /// <summary>
        /// Gets the validation message for a <see cref="WNotNullAttribute"/> using the property's display name.
        /// </summary>
        /// <param name="property">The serialized property being validated.</param>
        /// <param name="notNullAttribute">The attribute, or null for default behavior.</param>
        /// <returns>The message to display in the help box.</returns>
        public static string GetNotNullMessage(
            SerializedProperty property,
            WNotNullAttribute notNullAttribute
        )
        {
            if (notNullAttribute != null && !string.IsNullOrEmpty(notNullAttribute.CustomMessage))
            {
                return notNullAttribute.CustomMessage;
            }

            string fieldName = property?.displayName ?? NotNullFallbackMessage;
            return string.Format(NotNullMessageFormat, fieldName);
        }

        /// <summary>
        /// Gets the validation message for a <see cref="WNotNullAttribute"/> using a custom field name.
        /// Primarily used by Odin Inspector drawers which use Property.NiceName.
        /// </summary>
        /// <param name="fieldName">The display name of the field.</param>
        /// <param name="notNullAttribute">The attribute, or null for default behavior.</param>
        /// <returns>The message to display in the help box.</returns>
        public static string GetNotNullMessage(string fieldName, WNotNullAttribute notNullAttribute)
        {
            if (notNullAttribute != null && !string.IsNullOrEmpty(notNullAttribute.CustomMessage))
            {
                return notNullAttribute.CustomMessage;
            }

            string displayName = fieldName ?? NotNullFallbackMessage;
            return string.Format(NotNullMessageFormat, displayName);
        }

        /// <summary>
        /// Checks if a value is null for WNotNull validation purposes.
        /// Handles both standard CLR null and Unity Object fake null.
        /// </summary>
        /// <param name="value">The value to check.</param>
        /// <returns>True if the value is null or a destroyed Unity Object.</returns>
        public static bool IsValueNull(object value)
        {
            if (value == null)
            {
                return true;
            }

            if (value is UnityEngine.Object unityObject)
            {
                return unityObject == null;
            }

            return false;
        }

        /// <summary>
        /// Checks if a value is invalid for ValidateAssignment validation purposes.
        /// Handles null, empty strings, empty collections, and empty enumerables.
        /// </summary>
        /// <param name="value">The value to check.</param>
        /// <returns>True if the value is invalid (null, empty, or has no elements).</returns>
        public static bool IsValueInvalid(object value)
        {
            if (value == null)
            {
                return true;
            }

            if (value is UnityEngine.Object unityObject)
            {
                return unityObject == null;
            }

            if (value is string stringValue)
            {
                return string.IsNullOrWhiteSpace(stringValue);
            }

            if (value is ICollection collection)
            {
                return collection.Count <= 0;
            }

            if (value is IEnumerable enumerable)
            {
                IEnumerator enumerator = enumerable.GetEnumerator();
                try
                {
                    return !enumerator.MoveNext();
                }
                finally
                {
                    if (enumerator is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if a serialized property value is null.
        /// </summary>
        /// <param name="property">The property to check.</param>
        /// <returns>True if the property value is null.</returns>
        public static bool IsPropertyNull(SerializedProperty property)
        {
            if (property == null)
            {
                return true;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceValue == null;
                case SerializedPropertyType.ExposedReference:
                    return property.exposedReferenceValue == null;
                case SerializedPropertyType.ManagedReference:
                    return property.managedReferenceValue == null;
                case SerializedPropertyType.String:
                    return string.IsNullOrEmpty(property.stringValue);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Checks if a serialized property value is invalid (null, empty string, or empty collection).
        /// </summary>
        /// <param name="property">The property to check.</param>
        /// <returns>True if the property value is invalid.</returns>
        public static bool IsPropertyInvalid(SerializedProperty property)
        {
            if (property == null)
            {
                return true;
            }

            // A string reports isArray = true as well, so it is excluded here and handled below.
            if (property.isArray && property.propertyType != SerializedPropertyType.String)
            {
                return property.arraySize <= 0;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceValue == null;
                case SerializedPropertyType.ExposedReference:
                    return property.exposedReferenceValue == null;
                case SerializedPropertyType.ManagedReference:
                    return property.managedReferenceValue == null;
                case SerializedPropertyType.String:
                    return string.IsNullOrWhiteSpace(property.stringValue);
                case SerializedPropertyType.Generic:
                    return IsGenericPropertyInvalid(property);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Checks if a generic serialized property (typically a collection) is invalid.
        /// </summary>
        /// <param name="property">The property to check.</param>
        /// <returns>True if the property is an empty collection.</returns>
        public static bool IsGenericPropertyInvalid(SerializedProperty property)
        {
            if (property.isArray)
            {
                return property.arraySize <= 0;
            }

            SerializedProperty arraySizeProperty = property.FindPropertyRelative("Array.size");
            if (arraySizeProperty != null)
            {
                return arraySizeProperty.intValue <= 0;
            }

            SerializedProperty countProperty = property.FindPropertyRelative("_size");
            if (countProperty != null)
            {
                return countProperty.intValue <= 0;
            }

            countProperty = property.FindPropertyRelative("m_Size");
            if (countProperty != null)
            {
                return countProperty.intValue <= 0;
            }

            return false;
        }

        /// <summary>
        /// Draws a validation help box for a ValidateAssignment property if it is invalid.
        /// Call this from custom editors for array/list properties that won't have
        /// their PropertyDrawer invoked at the array level.
        /// </summary>
        /// <param name="property">The property to validate.</param>
        /// <param name="validateAttribute">The attribute containing validation settings.</param>
        /// <returns>True if a help box was drawn, false otherwise.</returns>
        public static bool DrawValidateAssignmentHelpBoxIfNeeded(
            SerializedProperty property,
            ValidateAssignmentAttribute validateAttribute
        )
        {
            if (!IsPropertyInvalid(property))
            {
                return false;
            }

            string message = GetValidateAssignmentMessage(property, validateAttribute);
            MessageType messageType = GetMessageType(validateAttribute);
            EditorGUILayout.HelpBox(message, messageType);
            return true;
        }

        /// <summary>
        /// Draws a validation help box for a WNotNull property if it is null.
        /// Call this from custom editors for array/list properties that won't have
        /// their PropertyDrawer invoked at the array level.
        /// </summary>
        /// <param name="property">The property to validate.</param>
        /// <param name="notNullAttribute">The attribute containing validation settings.</param>
        /// <returns>True if a help box was drawn, false otherwise.</returns>
        public static bool DrawNotNullHelpBoxIfNeeded(
            SerializedProperty property,
            WNotNullAttribute notNullAttribute
        )
        {
            if (!IsPropertyNull(property))
            {
                return false;
            }

            string message = GetNotNullMessage(property, notNullAttribute);
            MessageType messageType = GetMessageType(notNullAttribute);
            EditorGUILayout.HelpBox(message, messageType);
            return true;
        }
    }
#endif
}
