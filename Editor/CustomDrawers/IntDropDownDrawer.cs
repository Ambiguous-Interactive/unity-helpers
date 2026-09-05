// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Base;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Editor.Settings;

    [CustomPropertyDrawer(typeof(IntDropDownAttribute))]
    public sealed class IntDropDownDrawer : PropertyDrawer
    {
        private static readonly Dictionary<int, string[]> DisplayOptionsCache = new();
        private static readonly GUIContent ReusableDropDownButtonContent = new();

        private static string[] GetOrCreateDisplayOptions(int[] options)
        {
            if (options == null || options.Length == 0)
            {
                return Array.Empty<string>();
            }

            int hashCode = DropDownShared.ComputeOptionsHash(options);
            if (DisplayOptionsCache.TryGetValue(hashCode, out string[] cached))
            {
                if (cached.Length == options.Length)
                {
                    bool match = true;
                    for (int i = 0; i < options.Length && match; i++)
                    {
                        if (
                            !string.Equals(
                                cached[i],
                                DropDownShared.GetCachedIntString(options[i]),
                                StringComparison.Ordinal
                            )
                        )
                        {
                            match = false;
                        }
                    }
                    if (match)
                    {
                        return cached;
                    }
                }
            }

            string[] displayOptions = new string[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                displayOptions[i] = DropDownShared.GetCachedIntString(options[i]);
            }
            DisplayOptionsCache[hashCode] = displayOptions;
            return displayOptions;
        }

        /// <summary>
        /// Renders a dropdown that allows selecting one of the configured integer options.
        /// When the number of options exceeds the page size, a popup window with search and
        /// pagination is used.
        /// </summary>
        /// <param name="position">The rectangle reserved for drawing the control.</param>
        /// <param name="property">The backing serialized property.</param>
        /// <param name="label">The label displayed next to the field.</param>
        /// <example>
        /// <code>
        /// [IntDropDown(1, 2, 3)]
        /// public int qualityLevel;
        /// </code>
        /// </example>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (attribute is not IntDropDownAttribute dropdown)
            {
                return;
            }

            if (property.propertyType != SerializedPropertyType.Integer)
            {
                string typeMismatchMessage = GetTypeMismatchMessage(property);
                EditorGUI.HelpBox(position, typeMismatchMessage, MessageType.Error);
                return;
            }

            UnityEngine.Object context = property.serializedObject?.targetObject;
            int[] options = dropdown.GetOptions(context) ?? Array.Empty<int>();
            if (options.Length == 0)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            int pageSize = Mathf.Max(1, UnityHelpersSettings.GetStringInListPageLimit());
            string[] displayedOptions = GetOrCreateDisplayOptions(options);

            EditorGUI.BeginProperty(position, label, property);
            try
            {
                if (pageSize < options.Length)
                {
                    DrawPopupDropDown(
                        position,
                        property,
                        label,
                        options,
                        displayedOptions,
                        pageSize
                    );
                }
                else
                {
                    DrawGenericMenuDropDown(position, property, label, options, displayedOptions);
                }
            }
            finally
            {
                EditorGUI.EndProperty();
            }
        }

        /// <summary>
        /// Draws a dropdown using GenericMenu for a small number of options.
        /// </summary>
        /// <remarks>
        /// If the current property value is not found in the options array,
        /// the display shows "(Invalid)" but does NOT modify the property during render.
        /// Property modifications only occur in the GenericMenu callback.
        /// </remarks>
        private static void DrawGenericMenuDropDown(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            int[] options,
            string[] displayedOptions
        )
        {
            Rect fieldRect = EditorGUI.PrefixLabel(position, label);

            bool previousMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;

            // Rendering must preserve invalid serialized values until the user makes a selection.
            int currentValue = property.intValue;
            int selectedIndex = Array.IndexOf(options, currentValue);

            string displayValue;
            if (property.hasMultipleDifferentValues)
            {
                displayValue = "\u2014";
            }
            else if (0 <= selectedIndex && selectedIndex < displayedOptions.Length)
            {
                displayValue = displayedOptions[selectedIndex];
            }
            else
            {
                displayValue = DropDownShared.GetCachedIntString(currentValue) + " (Invalid)";
            }

            ReusableDropDownButtonContent.text = displayValue;
            ReusableDropDownButtonContent.tooltip = string.Empty;

            if (
                EditorGUI.DropdownButton(
                    fieldRect,
                    ReusableDropDownButtonContent,
                    FocusType.Keyboard
                )
            )
            {
                SerializedObject serializedObject = property.serializedObject;
                string propertyPath = property.propertyPath;

                GenericMenu menu = new();
                for (int i = 0; i < options.Length; i++)
                {
                    int capturedIndex = i;
                    bool isSelected = i == selectedIndex && !property.hasMultipleDifferentValues;
                    menu.AddItem(
                        new GUIContent(displayedOptions[i]),
                        isSelected,
                        () =>
                        {
                            serializedObject.Update();
                            SerializedProperty prop = serializedObject.FindProperty(propertyPath);
                            if (prop == null)
                            {
                                return;
                            }

                            Undo.RecordObjects(
                                serializedObject.targetObjects,
                                "Change IntDropDown Selection"
                            );
                            prop.intValue = options[capturedIndex];
                            serializedObject.ApplyModifiedProperties();
                        }
                    );
                }
                menu.DropDown(fieldRect);
            }

            EditorGUI.showMixedValue = previousMixed;
        }

        /// <summary>
        /// Draws a popup button for large option lists that opens a searchable selection window.
        /// </summary>
        /// <remarks>
        /// If the current property value is not found in the options array,
        /// the display shows "(Invalid)" but does NOT modify the property during render.
        /// Property modifications only occur in the popup window callback.
        /// </remarks>
        private static void DrawPopupDropDown(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            int[] options,
            string[] displayedOptions,
            int pageSize
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

            EditorGUI.LabelField(labelRect, label);

            bool previousMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;

            // Rendering must preserve invalid serialized values until the user makes a selection.
            int currentValue = property.intValue;
            int selectedIndex = Array.IndexOf(options, currentValue);

            string displayValue;
            if (property.hasMultipleDifferentValues)
            {
                displayValue = "\u2014";
            }
            else if (0 <= selectedIndex && selectedIndex < displayedOptions.Length)
            {
                displayValue = displayedOptions[selectedIndex];
            }
            else
            {
                displayValue = DropDownShared.GetCachedIntString(currentValue) + " (Invalid)";
            }

            if (GUI.Button(fieldRect, displayValue, EditorStyles.popup))
            {
                WDropDownPopupWindow.ShowForIntDropDown(
                    fieldRect,
                    property,
                    options,
                    displayedOptions,
                    pageSize
                );
            }

            EditorGUI.showMixedValue = previousMixed;
        }

        /// <inheritdoc/>
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (attribute is not IntDropDownAttribute dropdown)
            {
                PropertyField fallback = new(property) { label = property.displayName };
                return fallback;
            }

            if (property.propertyType != SerializedPropertyType.Integer)
            {
                return new HelpBox(GetTypeMismatchMessage(property), HelpBoxMessageType.Error);
            }

            UnityEngine.Object context = property.serializedObject?.targetObject;
            int[] options = dropdown.GetOptions(context) ?? Array.Empty<int>();

            if (options.Length == 0)
            {
                return new HelpBox(
                    "No options available for IntDropDown.",
                    HelpBoxMessageType.Info
                );
            }

            int pageSize = Mathf.Max(1, UnityHelpersSettings.GetStringInListPageLimit());
            string[] displayedOptions = GetOrCreateDisplayOptions(options);

            if (pageSize < options.Length)
            {
                IntDropDownPopupSelectorElement popupElement = new(options, displayedOptions);
                popupElement.BindProperty(property, property.displayName);
                return popupElement;
            }

            IntDropDownSelector selector = new(options, displayedOptions);
            selector.BindProperty(property, property.displayName);
            return selector;
        }

        private static string GetTypeMismatchMessage(SerializedProperty property)
        {
            string fieldName = property.displayName;
            string actualType = GetPropertyTypeName(property);
            return $"[IntDropDown] Type mismatch: '{fieldName}' is {actualType}, but IntDropDown requires int. Change the field type to int.";
        }

        private static string GetPropertyTypeName(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.String => "a string",
                SerializedPropertyType.Float => "a float",
                SerializedPropertyType.Boolean => "a bool",
                SerializedPropertyType.Enum => "an enum",
                SerializedPropertyType.ObjectReference => "an object reference",
                SerializedPropertyType.Vector2 => "a Vector2",
                SerializedPropertyType.Vector3 => "a Vector3",
                SerializedPropertyType.Vector4 => "a Vector4",
                SerializedPropertyType.Color => "a Color",
                SerializedPropertyType.Rect => "a Rect",
                SerializedPropertyType.ArraySize => "an array size",
                SerializedPropertyType.Character => "a char",
                SerializedPropertyType.AnimationCurve => "an AnimationCurve",
                SerializedPropertyType.Bounds => "a Bounds",
                SerializedPropertyType.Quaternion => "a Quaternion",
                SerializedPropertyType.ExposedReference => "an exposed reference",
                SerializedPropertyType.FixedBufferSize => "a fixed buffer size",
                SerializedPropertyType.Vector2Int => "a Vector2Int",
                SerializedPropertyType.Vector3Int => "a Vector3Int",
                SerializedPropertyType.RectInt => "a RectInt",
                SerializedPropertyType.BoundsInt => "a BoundsInt",
                SerializedPropertyType.ManagedReference => "a managed reference",
                SerializedPropertyType.Hash128 => "a Hash128",
                _ => $"type '{property.propertyType}'",
            };
        }

        /// <summary>
        /// UI Toolkit popup selector element for IntDropDown with large option lists.
        /// Uses IMGUI rendering via IMGUIContainer to show the popup button.
        /// </summary>
        private sealed class IntDropDownPopupSelectorElement : WDropDownPopupSelectorBase<int>
        {
            private readonly int[] _options;
            private readonly string[] _displayedOptions;

            public IntDropDownPopupSelectorElement(int[] options, string[] displayedOptions)
            {
                _options = options ?? Array.Empty<int>();
                _displayedOptions = displayedOptions ?? Array.Empty<string>();
            }

            protected override int OptionCount => _options.Length;

            protected override string GetDisplayValue(SerializedProperty property)
            {
                if (property.hasMultipleDifferentValues)
                {
                    return "\u2014";
                }

                int currentValue = property.intValue;
                int selectedIndex = Array.IndexOf(_options, currentValue);

                if (0 <= selectedIndex && selectedIndex < _displayedOptions.Length)
                {
                    return _displayedOptions[selectedIndex];
                }

                return DropDownShared.GetCachedIntString(currentValue) + " (Invalid)";
            }

            protected override int GetFieldValue(SerializedProperty property)
            {
                return property.intValue;
            }

            protected override void ShowPopup(
                Rect controlRect,
                SerializedProperty property,
                int pageSize
            )
            {
                WDropDownPopupWindow.ShowForIntDropDown(
                    controlRect,
                    property,
                    _options,
                    _displayedOptions,
                    pageSize
                );
            }
        }

        /// <summary>
        /// UI Toolkit inline selector for IntDropDown with small option lists.
        /// Provides search, pagination, and autocomplete functionality.
        /// </summary>
        private sealed class IntDropDownSelector : WDropDownSelectorBase<int>
        {
            private readonly int[] _options;
            private readonly string[] _displayedOptions;

            public IntDropDownSelector(int[] options, string[] displayedOptions)
            {
                _options = options ?? Array.Empty<int>();
                _displayedOptions = displayedOptions ?? Array.Empty<string>();
                InitializeSearchVisibility();
            }

            protected override int OptionCount => _options.Length;

            protected override string GetDisplayLabel(int optionIndex)
            {
                return 0 <= optionIndex && optionIndex < _displayedOptions.Length
                    ? _displayedOptions[optionIndex]
                    : DropDownShared.GetCachedIntString(_options[optionIndex]);
            }

            protected override int GetCurrentSelectionIndex(SerializedProperty property)
            {
                if (property.hasMultipleDifferentValues)
                {
                    return -1;
                }

                return Array.IndexOf(_options, property.intValue);
            }

            protected override void ApplySelectionToProperty(
                SerializedProperty property,
                int optionIndex
            )
            {
                property.intValue = _options[optionIndex];
            }

            protected override int GetValueForOption(int optionIndex)
            {
                return _options[optionIndex];
            }

            protected override int GetDefaultValue() => 0 < _options.Length ? _options[0] : 0;

            protected override string UndoActionName => "Change IntDropDown Selection";
        }
    }
#endif
}
