// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using Sirenix.OdinInspector.Editor;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;

    /// <summary>
    /// Odin Inspector attribute drawer for <see cref="WValueDropDownAttribute"/>.
    /// Renders fields decorated with WValueDropDown as dropdown selectors.
    /// </summary>
    /// <remarks>
    /// This drawer ensures WValueDropDown works correctly when Odin Inspector is installed
    /// and classes derive from SerializedMonoBehaviour or SerializedScriptableObject,
    /// where Unity's standard PropertyDrawer system is bypassed.
    /// </remarks>
    public sealed class WValueDropDownOdinDrawer : OdinAttributeDrawer<WValueDropDownAttribute>
    {
        /// <summary>
        /// Draws the property as a dropdown selector with the options provided by the attribute.
        /// </summary>
        /// <param name="label">The label to display for the property.</param>
        protected override void DrawPropertyLayout(GUIContent label)
        {
            WValueDropDownAttribute dropdownAttribute = Attribute;
            if (dropdownAttribute == null)
            {
                CallNextDrawer(label);
                return;
            }

            object parentValue = Property.Parent?.ValueEntry?.WeakSmartValue;
            object[] options = dropdownAttribute.GetOptions(parentValue) ?? Array.Empty<object>();

            if (options.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No options available for WValueDropDown.",
                    MessageType.Info
                );
                return;
            }

            // Check for mixed values
            bool hasMultipleDifferentValues = false;
            if (Property.ValueEntry.ValueCount > 1)
            {
                object firstValue = Property.ValueEntry.WeakValues[0];
                for (int i = 1; i < Property.ValueEntry.ValueCount; i++)
                {
                    if (!Equals(firstValue, Property.ValueEntry.WeakValues[i]))
                    {
                        hasMultipleDifferentValues = true;
                        break;
                    }
                }
            }

            // Set showMixedValue FIRST, before any index calculations
            bool previousMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = hasMultipleDifferentValues;

            object currentValue = Property.ValueEntry?.WeakSmartValue;
            int currentIndex = FindSelectedIndex(currentValue, options);
            string[] displayOptions = GetDisplayOptions(options);

            Rect controlRect = EditorGUILayout.GetControlRect(
                true,
                EditorGUIUtility.singleLineHeight
            );

            try
            {
                DrawPopupDropDown(
                    controlRect,
                    label,
                    options,
                    displayOptions,
                    currentIndex,
                    currentValue,
                    hasMultipleDifferentValues
                );
            }
            finally
            {
                EditorGUI.showMixedValue = previousMixed;
            }
        }

        private void DrawPopupDropDown(
            Rect position,
            GUIContent label,
            object[] options,
            string[] displayOptions,
            int currentIndex,
            object currentValue,
            bool hasMultipleDifferentValues
        )
        {
            Rect labelRect = new(
                position.x,
                position.y,
                EditorGUIUtility.labelWidth,
                position.height
            );

            Rect fieldRect = new(
                position.x + EditorGUIUtility.labelWidth + 2f,
                position.y,
                position.width - EditorGUIUtility.labelWidth - 2f,
                position.height
            );

            if (label != null && label != GUIContent.none)
            {
                EditorGUI.LabelField(labelRect, label);
            }

            string displayValue;
            if (hasMultipleDifferentValues)
            {
                displayValue = "\u2014"; // Em dash for mixed values
            }
            else if (currentIndex >= 0 && currentIndex < displayOptions.Length)
            {
                displayValue = displayOptions[currentIndex];
            }
            else
            {
                displayValue = DropDownShared.FormatOption(currentValue);
            }

            if (GUI.Button(fieldRect, displayValue, EditorStyles.popup))
            {
                ShowPopupMenu(
                    fieldRect,
                    options,
                    displayOptions,
                    currentIndex,
                    hasMultipleDifferentValues
                );
            }
        }

        private void ShowPopupMenu(
            Rect buttonRect,
            object[] options,
            string[] displayOptions,
            int currentIndex,
            bool hasMultipleDifferentValues
        )
        {
            GenericMenu menu = new();
            for (int i = 0; i < options.Length; i++)
            {
                int capturedIndex = i;
                bool isSelected = i == currentIndex && !hasMultipleDifferentValues;
                menu.AddItem(
                    new GUIContent(displayOptions[i]),
                    isSelected,
                    () => ApplySelection(options[capturedIndex])
                );
            }
            menu.DropDown(buttonRect);
        }

        private void ApplySelection(object newValue)
        {
            Property.ValueEntry.WeakSmartValue = newValue;
        }

        internal static int FindSelectedIndex(object currentValue, object[] options)
        {
            if (currentValue == null)
            {
                return -1;
            }

            for (int i = 0; i < options.Length; i++)
            {
                if (DropDownShared.ValuesMatch(currentValue, options[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        internal static string[] GetDisplayOptions(object[] options)
        {
            string[] displayOptions = new string[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                displayOptions[i] = DropDownShared.FormatOption(options[i]);
            }
            return displayOptions;
        }
    }
#endif
}
