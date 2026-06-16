// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Base;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// UI Toolkit drawer for <see cref="WValueDropDownAttribute"/> that provides search, pagination, and autocomplete.
    /// </summary>
    [CustomPropertyDrawer(typeof(WValueDropDownAttribute))]
    public sealed class WValueDropDownDrawer : PropertyDrawer
    {
        private sealed class PopupState
        {
            public string search = string.Empty;
            public int page;
        }

        private sealed class DisplayLabelsCache
        {
            public object[] sourceOptions;
            public string[] labels;
        }

        private const float ButtonWidth = DropDownShared.ButtonWidth;
        private const float PageLabelWidth = DropDownShared.PageLabelWidth;
        private const float PaginationButtonHeight = DropDownShared.PaginationButtonHeight;
        private const float PopupWidth = DropDownShared.PopupWidth;
        private const float OptionBottomPadding = DropDownShared.OptionBottomPadding;
        private const float OptionRowExtraHeight = DropDownShared.OptionRowExtraHeight;
        private const float EmptySearchHorizontalPadding =
            DropDownShared.EmptySearchHorizontalPadding;
        private const float EmptySearchExtraPadding = DropDownShared.EmptySearchExtraPadding;
        private const string EmptyResultsMessage = DropDownShared.EmptyResultsMessage;
        private static readonly GUIContent EmptyResultsContent = DropDownShared.EmptyResultsContent;
        private static float s_cachedOptionControlHeight = -1f;
        private static float s_cachedOptionRowHeight = -1f;
        private static readonly Dictionary<string, PopupState> PopupStates = new();
        private static readonly Dictionary<string, DisplayLabelsCache> DisplayLabelsCaches = new(
            StringComparer.Ordinal
        );
        private static readonly Dictionary<object, string> FormattedOptionCache = new();
        private static readonly GUIContent ReusableDropDownButtonContent = new();

        private static string GetPaginationLabel(int page, int totalPages)
        {
            return DropDownShared.GetPaginationLabel(page, totalPages);
        }

        private static PopupState GetOrCreateState(string key)
        {
            if (!PopupStates.TryGetValue(key, out PopupState state))
            {
                state = new PopupState();
                PopupStates[key] = state;
            }
            return state;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (attribute is not WValueDropDownAttribute dropdownAttribute)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            UnityEngine.Object context = property.serializedObject?.targetObject;
            object[] options = dropdownAttribute.GetOptions(context) ?? Array.Empty<object>();
            int pageSize = Mathf.Max(1, UnityHelpersSettings.GetStringInListPageLimit());

            if (options.Length == 0)
            {
                EditorGUI.HelpBox(
                    position,
                    "No options available for WValueDropDown.",
                    MessageType.Info
                );
                return;
            }

            if (!IsSupportedProperty(property, dropdownAttribute))
            {
                string typeMismatchMessage = GetTypeMismatchMessage(property, dropdownAttribute);
                EditorGUI.HelpBox(position, typeMismatchMessage, MessageType.Error);
                return;
            }

            if (options.Length > pageSize)
            {
                DrawPopupDropDown(position, property, label, options, pageSize, dropdownAttribute);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            Rect fieldRect = EditorGUI.PrefixLabel(position, label);
            bool previousMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;

            string displayValue = ResolveDisplayValue(
                property,
                options,
                dropdownAttribute,
                out string tooltip
            );
            ReusableDropDownButtonContent.text = displayValue;
            ReusableDropDownButtonContent.tooltip = tooltip;

            if (
                EditorGUI.DropdownButton(
                    fieldRect,
                    ReusableDropDownButtonContent,
                    FocusType.Keyboard
                )
            )
            {
                string cacheKey = property.propertyPath;
                string[] displayLabels = GetOrCreateDisplayLabels(cacheKey, options);
                int currentIndex = ResolveSelectedIndex(
                    property,
                    dropdownAttribute.ValueType,
                    options
                );

                SerializedObject serializedObject = property.serializedObject;
                string propertyPath = property.propertyPath;

                GenericMenu menu = new();
                for (int i = 0; i < options.Length; i++)
                {
                    int capturedIndex = i;
                    bool isSelected = i == currentIndex && !property.hasMultipleDifferentValues;
                    menu.AddItem(
                        new GUIContent(displayLabels[i]),
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
                                "Change ValueDropDown Selection"
                            );
                            ApplyOption(prop, options[capturedIndex]);
                            serializedObject.ApplyModifiedProperties();
                        }
                    );
                }
                menu.DropDown(fieldRect);
            }

            EditorGUI.showMixedValue = previousMixed;
            EditorGUI.EndProperty();
        }

        /// <inheritdoc/>
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (attribute is not WValueDropDownAttribute dropdownAttribute)
            {
                PropertyField fallback = new(property) { label = property.displayName };
                return fallback;
            }

            UnityEngine.Object context = property.serializedObject?.targetObject;
            object[] options = dropdownAttribute.GetOptions(context) ?? Array.Empty<object>();
            int pageSize = Mathf.Max(1, UnityHelpersSettings.GetStringInListPageLimit());

            if (options.Length == 0)
            {
                return new HelpBox(
                    "No options available for WValueDropDown.",
                    HelpBoxMessageType.Info
                );
            }

            if (!IsSupportedProperty(property, dropdownAttribute))
            {
                return new HelpBox(
                    GetTypeMismatchMessage(property, dropdownAttribute),
                    HelpBoxMessageType.Error
                );
            }

            if (options.Length > pageSize)
            {
                WValueDropDownPopupSelectorElement popupElement = new(options, dropdownAttribute);
                popupElement.BindProperty(property, property.displayName);
                return popupElement;
            }

            WValueDropDownSelector selector = new(options, dropdownAttribute);
            selector.BindProperty(property, property.displayName);
            return selector;
        }

        private static bool IsSupportedProperty(
            SerializedProperty property,
            WValueDropDownAttribute attribute
        )
        {
            // Exclude property types that cannot be meaningfully assigned from a dropdown
            // Note: String properties have isArray=true in Unity's serialization (stored as char arrays),
            // so we explicitly exclude strings from the array check.
            if (
                property.propertyType == SerializedPropertyType.ArraySize
                || property.propertyType == SerializedPropertyType.FixedBufferSize
                || property.propertyType == SerializedPropertyType.Gradient
                || (property.isArray && property.propertyType != SerializedPropertyType.String)
            )
            {
                return false;
            }

            // Check type compatibility between property and dropdown options
            return IsTypeCompatible(property, attribute);
        }

        private static bool IsTypeCompatible(
            SerializedProperty property,
            WValueDropDownAttribute attribute
        )
        {
            Type valueType = attribute?.ValueType;
            if (valueType == null || valueType == typeof(object))
            {
                // No specific type constraint - allow all non-excluded properties
                return true;
            }

            return property.propertyType switch
            {
                SerializedPropertyType.Integer => valueType == typeof(int)
                    || valueType == typeof(long)
                    || valueType == typeof(short)
                    || valueType == typeof(byte)
                    || valueType == typeof(sbyte)
                    || valueType == typeof(uint)
                    || valueType == typeof(ulong)
                    || valueType == typeof(ushort)
                    || valueType.IsEnum,
                SerializedPropertyType.Float => valueType == typeof(float)
                    || valueType == typeof(double),
                SerializedPropertyType.String => valueType == typeof(string),
                SerializedPropertyType.Boolean => valueType == typeof(bool),
                SerializedPropertyType.Character => valueType == typeof(char),
                SerializedPropertyType.Enum => valueType.IsEnum || valueType == typeof(string),
                SerializedPropertyType.ObjectReference =>
                    typeof(UnityEngine.Object).IsAssignableFrom(valueType),
                SerializedPropertyType.Vector2 => valueType == typeof(Vector2),
                SerializedPropertyType.Vector3 => valueType == typeof(Vector3),
                SerializedPropertyType.Vector4 => valueType == typeof(Vector4),
                SerializedPropertyType.Vector2Int => valueType == typeof(Vector2Int),
                SerializedPropertyType.Vector3Int => valueType == typeof(Vector3Int),
                SerializedPropertyType.Color => valueType == typeof(Color)
                    || valueType == typeof(Color32),
                SerializedPropertyType.Rect => valueType == typeof(Rect),
                SerializedPropertyType.RectInt => valueType == typeof(RectInt),
                SerializedPropertyType.Bounds => valueType == typeof(Bounds),
                SerializedPropertyType.BoundsInt => valueType == typeof(BoundsInt),
                SerializedPropertyType.Quaternion => valueType == typeof(Quaternion),
                SerializedPropertyType.AnimationCurve => valueType == typeof(AnimationCurve),
                SerializedPropertyType.Hash128 => valueType == typeof(Hash128),
                SerializedPropertyType.Generic => IsSerializableTypeProperty(property)
                    || IsGenericSerializedProperty(property),
                _ => false,
            };
        }

        private static bool IsSerializableTypeProperty(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.Generic)
            {
                return false;
            }

            SerializedProperty assemblyQualifiedNameProperty = property.FindPropertyRelative(
                SerializableType.SerializedPropertyNames.AssemblyQualifiedName
            );
            return assemblyQualifiedNameProperty != null
                && assemblyQualifiedNameProperty.propertyType == SerializedPropertyType.String;
        }

        private static bool IsGenericSerializedProperty(SerializedProperty property)
        {
            // Support arbitrary generic/serialized properties that are value types or structs
            // This allows WValueDropDown to work with any serializable type that has proper
            // Equals/ToString implementations (like SerializableType or custom structs)
            return property.propertyType == SerializedPropertyType.Generic
                && !property.isArray
                && property.hasVisibleChildren;
        }

        private static SerializedProperty GetSerializableTypeStringProperty(
            SerializedProperty property
        )
        {
            if (property.propertyType != SerializedPropertyType.Generic)
            {
                return null;
            }

            return property.FindPropertyRelative(
                SerializableType.SerializedPropertyNames.AssemblyQualifiedName
            );
        }

        private static int CalculatePageCount(int pageSize, int filteredCount)
        {
            if (filteredCount <= 0)
            {
                return 1;
            }

            return (filteredCount + pageSize - 1) / pageSize;
        }

        private static int CalculateRowsOnPage(int filteredCount, int pageSize, int currentPage)
        {
            if (filteredCount <= 0 || pageSize <= 0)
            {
                return 1;
            }

            int maxPageIndex = CalculatePageCount(pageSize, filteredCount) - 1;
            int clampedPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, maxPageIndex));
            int startIndex = clampedPage * pageSize;
            int remaining = filteredCount - startIndex;
            if (remaining <= 0)
            {
                return 1;
            }

            return Mathf.Min(pageSize, remaining);
        }

        private static void DrawPopupDropDown(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            object[] options,
            int pageSize,
            WValueDropDownAttribute attribute
        )
        {
            EditorGUI.BeginProperty(position, label, property);
            Rect fieldRect = EditorGUI.PrefixLabel(position, label);
            bool previousMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;

            string displayValue = ResolveDisplayValue(
                property,
                options,
                attribute,
                out string tooltip
            );
            ReusableDropDownButtonContent.text = displayValue;
            ReusableDropDownButtonContent.tooltip = tooltip;
            if (
                EditorGUI.DropdownButton(
                    fieldRect,
                    ReusableDropDownButtonContent,
                    FocusType.Keyboard
                )
            )
            {
                string cacheKey = property.propertyPath + "::popup";
                string[] displayLabels = GetOrCreateDisplayLabels(cacheKey, options);
                int currentIndex = ResolveSelectedIndex(property, attribute.ValueType, options);

                SerializedObject serializedObject = property.serializedObject;
                string propertyPath = property.propertyPath;

                WDropDownPopupData data = new()
                {
                    DisplayLabels = displayLabels,
                    Tooltips = null,
                    SelectedIndex = property.hasMultipleDifferentValues ? -1 : currentIndex,
                    PageSize = pageSize,
                    OnSelectionChanged = (selectedIndex) =>
                    {
                        if (selectedIndex < 0 || selectedIndex >= options.Length)
                        {
                            return;
                        }

                        serializedObject.Update();
                        SerializedProperty prop = serializedObject.FindProperty(propertyPath);
                        if (prop == null)
                        {
                            return;
                        }

                        Undo.RecordObjects(
                            serializedObject.targetObjects,
                            "Change ValueDropDown Selection"
                        );
                        ApplyOption(prop, options[selectedIndex]);
                        serializedObject.ApplyModifiedProperties();
                    },
                };

                Rect screenRect = GUIUtility.GUIToScreenRect(fieldRect);
                WDropDownPopupWindow.Show(screenRect, data);
            }

            EditorGUI.showMixedValue = previousMixed;

            EditorGUI.EndProperty();
        }

        private static int ResolveSelectedIndex(
            SerializedProperty property,
            Type valueType,
            object[] options
        )
        {
            for (int index = 0; index < options.Length; index += 1)
            {
                if (OptionMatches(property, valueType, options[index]))
                {
                    return index;
                }
            }

            return -1;
        }

        private static string ResolveDisplayValue(
            SerializedProperty property,
            object[] options,
            WValueDropDownAttribute attribute,
            out string tooltip
        )
        {
            tooltip = string.Empty;
            if (property == null)
            {
                return string.Empty;
            }

            if (property.hasMultipleDifferentValues)
            {
                return "\u2014";
            }

            int selectedIndex = ResolveSelectedIndex(property, attribute.ValueType, options);
            if (selectedIndex >= 0 && selectedIndex < options.Length)
            {
                return FormatOptionCached(options[selectedIndex]);
            }

            return string.Empty;
        }

        private static bool OptionMatches(
            SerializedProperty property,
            Type valueType,
            object option
        )
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return MatchesInteger(property, valueType, option);
                case SerializedPropertyType.Float:
                    return MatchesFloat(property, valueType, option);
                case SerializedPropertyType.String:
                    return MatchesString(property, option);
                case SerializedPropertyType.Enum:
                    return MatchesEnum(property, option);
                case SerializedPropertyType.Boolean:
                    return MatchesBoolean(property, option);
                case SerializedPropertyType.Character:
                    return MatchesCharacter(property, option);
                case SerializedPropertyType.ObjectReference:
                    return MatchesObjectReference(property, option);
                case SerializedPropertyType.Vector2:
                    return option is Vector2 v2 && property.vector2Value == v2;
                case SerializedPropertyType.Vector3:
                    return option is Vector3 v3 && property.vector3Value == v3;
                case SerializedPropertyType.Vector4:
                    return option is Vector4 v4 && property.vector4Value == v4;
                case SerializedPropertyType.Vector2Int:
                    return option is Vector2Int v2i && property.vector2IntValue == v2i;
                case SerializedPropertyType.Vector3Int:
                    return option is Vector3Int v3i && property.vector3IntValue == v3i;
                case SerializedPropertyType.Color:
                    if (option is Color color)
                    {
                        return property.colorValue == color;
                    }
                    return option is Color32 c32 && property.colorValue == (Color)c32;
                case SerializedPropertyType.Rect:
                    return option is Rect rect && property.rectValue == rect;
                case SerializedPropertyType.RectInt:
                    return option is RectInt ri && property.rectIntValue.Equals(ri);
                case SerializedPropertyType.Bounds:
                    return option is Bounds bounds && property.boundsValue == bounds;
                case SerializedPropertyType.BoundsInt:
                    return option is BoundsInt bi && property.boundsIntValue == bi;
                case SerializedPropertyType.Quaternion:
                    return option is Quaternion q && property.quaternionValue == q;
                case SerializedPropertyType.AnimationCurve:
                    return option is AnimationCurve curve
                        && property.animationCurveValue != null
                        && property.animationCurveValue.Equals(curve);
                case SerializedPropertyType.Hash128:
                    return option is Hash128 hash && property.hash128Value == hash;
                case SerializedPropertyType.Generic:
                    if (IsSerializableTypeProperty(property))
                    {
                        return MatchesSerializableType(property, option);
                    }
                    return MatchesGenericProperty(property, valueType, option);
                default:
                    return MatchesGenericProperty(property, valueType, option);
            }
        }

        private static bool MatchesSerializableType(SerializedProperty property, object option)
        {
            SerializedProperty assemblyQualifiedNameProperty = GetSerializableTypeStringProperty(
                property
            );
            if (assemblyQualifiedNameProperty == null)
            {
                return false;
            }

            string currentValue = assemblyQualifiedNameProperty.stringValue ?? string.Empty;
            string optionValue = GetAssemblyQualifiedNameFromOption(option);

            return string.Equals(currentValue, optionValue, StringComparison.Ordinal);
        }

        private static bool MatchesGenericProperty(
            SerializedProperty property,
            Type valueType,
            object option
        )
        {
            if (option == null)
            {
                return false;
            }

            // Try to read the boxed value from the property and compare
            object boxedValue = GetBoxedPropertyValue(property, valueType);
            if (boxedValue == null)
            {
                return false;
            }

            // Use Equals for proper comparison (relies on IEquatable<T> or Equals override)
            return boxedValue.Equals(option);
        }

        private static object GetBoxedPropertyValue(SerializedProperty property, Type valueType)
        {
            if (property == null || valueType == null)
            {
                return null;
            }

            try
            {
                // Use reflection to get the actual value from the serialized object
                UnityEngine.Object targetObject = property.serializedObject?.targetObject;
                if (targetObject == null)
                {
                    return null;
                }

                // Navigate the property path to get the actual field value
                return GetFieldValueFromPropertyPath(targetObject, property.propertyPath);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static object GetFieldValueFromPropertyPath(object target, string propertyPath)
        {
            if (target == null || string.IsNullOrEmpty(propertyPath))
            {
                return null;
            }

            object current = target;
            string[] pathParts = propertyPath.Split('.');

            for (int i = 0; i < pathParts.Length; i++)
            {
                if (current == null)
                {
                    return null;
                }

                string part = pathParts[i];

                // Handle array access pattern: "Array.data[index]"
                if (
                    part == "Array"
                    && i + 1 < pathParts.Length
                    && pathParts[i + 1].StartsWith("data[", StringComparison.Ordinal)
                )
                {
                    string indexPart = pathParts[i + 1];
                    int startIndex = indexPart.IndexOf('[') + 1;
                    int endIndex = indexPart.IndexOf(']');
                    if (startIndex > 0 && endIndex > startIndex)
                    {
                        string indexStr = indexPart.Substring(startIndex, endIndex - startIndex);
                        if (int.TryParse(indexStr, out int arrayIndex))
                        {
                            if (
                                current is System.Collections.IList list
                                && arrayIndex >= 0
                                && arrayIndex < list.Count
                            )
                            {
                                current = list[arrayIndex];
                                i++; // Skip the "data[x]" part
                                continue;
                            }
                        }
                    }
                    return null;
                }

                Type currentType = current.GetType();
                System.Reflection.FieldInfo field = currentType.GetField(
                    part,
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                );

                if (field == null)
                {
                    // Try property as fallback
                    System.Reflection.PropertyInfo prop = currentType.GetProperty(
                        part,
                        System.Reflection.BindingFlags.Instance
                            | System.Reflection.BindingFlags.Public
                            | System.Reflection.BindingFlags.NonPublic
                    );

                    if (prop == null || !prop.CanRead)
                    {
                        return null;
                    }

                    current = prop.GetValue(current);
                }
                else
                {
                    current = field.GetValue(current);
                }
            }

            return current;
        }

        private static string GetAssemblyQualifiedNameFromOption(object option)
        {
            if (option == null)
            {
                return string.Empty;
            }

            if (option is Type type)
            {
                return SerializableType.NormalizeTypeName(type);
            }

            if (option is SerializableType serializableType)
            {
                return serializableType.AssemblyQualifiedName;
            }

            if (option is string stringOption)
            {
                return stringOption;
            }

            return string.Empty;
        }

        private static bool MatchesInteger(
            SerializedProperty property,
            Type valueType,
            object option
        )
        {
            if (option == null)
            {
                return false;
            }

            try
            {
                Type targetType = Nullable.GetUnderlyingType(valueType) ?? valueType;
                object converted = Convert.ChangeType(
                    property.longValue,
                    targetType,
                    CultureInfo.InvariantCulture
                );
                return Equals(converted, option);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool MatchesFloat(SerializedProperty property, Type valueType, object option)
        {
            if (option == null)
            {
                return false;
            }

            try
            {
                Type targetType = Nullable.GetUnderlyingType(valueType) ?? valueType;
                double currentValue = IsDoubleProperty(property)
                    ? property.doubleValue
                    : property.floatValue;
                object converted = Convert.ChangeType(
                    currentValue,
                    targetType,
                    CultureInfo.InvariantCulture
                );
                return Equals(converted, option);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool MatchesString(SerializedProperty property, object option)
        {
            if (option == null)
            {
                return string.IsNullOrEmpty(property.stringValue);
            }

            return string.Equals(property.stringValue, option as string, StringComparison.Ordinal);
        }

        private static bool MatchesEnum(SerializedProperty property, object option)
        {
            if (option == null)
            {
                return false;
            }

            if (option is Enum enumValue)
            {
                string optionName = enumValue.ToString();
                if (property.enumNames == null || property.enumNames.Length == 0)
                {
                    return false;
                }

                int enumIndex = property.enumValueIndex;
                if (enumIndex < 0 || enumIndex >= property.enumNames.Length)
                {
                    return false;
                }

                string currentName = property.enumNames[enumIndex];
                return string.Equals(currentName, optionName, StringComparison.Ordinal);
            }

            if (option is string optionString)
            {
                if (property.enumNames == null || property.enumNames.Length == 0)
                {
                    return false;
                }

                int enumIndex = property.enumValueIndex;
                if (enumIndex < 0 || enumIndex >= property.enumNames.Length)
                {
                    return false;
                }

                string currentName = property.enumNames[enumIndex];
                return string.Equals(currentName, optionString, StringComparison.Ordinal);
            }

            return false;
        }

        private static bool MatchesBoolean(SerializedProperty property, object option)
        {
            if (option == null)
            {
                return false;
            }

            if (option is bool boolOption)
            {
                return property.boolValue == boolOption;
            }

            return false;
        }

        private static bool MatchesCharacter(SerializedProperty property, object option)
        {
            if (option == null)
            {
                return false;
            }

            if (option is char charOption)
            {
                // Unity stores char as intValue in SerializedProperty
                return property.intValue == charOption;
            }

            return false;
        }

        private static bool MatchesObjectReference(SerializedProperty property, object option)
        {
            UnityEngine.Object currentValue = property.objectReferenceValue;

            // Both null - match
            if (currentValue == null && option == null)
            {
                return true;
            }

            // One null, one not - no match
            if (currentValue == null || option == null)
            {
                return false;
            }

            // Option must be a UnityEngine.Object
            if (option is not UnityEngine.Object optionObject)
            {
                return false;
            }

            // Compare by reference (Unity objects use reference equality)
            return ReferenceEquals(currentValue, optionObject);
        }

        private static string[] GetOrCreateDisplayLabels(string cacheKey, object[] options)
        {
            if (
                DisplayLabelsCaches.TryGetValue(cacheKey, out DisplayLabelsCache cached)
                && cached != null
            )
            {
                if (ReferenceEquals(cached.sourceOptions, options))
                {
                    return cached.labels;
                }

                if (
                    cached.sourceOptions != null
                    && cached.sourceOptions.Length == options.Length
                    && cached.labels != null
                    && cached.labels.Length == options.Length
                )
                {
                    bool match = true;
                    for (int i = 0; i < options.Length && match; i++)
                    {
                        if (!Equals(cached.sourceOptions[i], options[i]))
                        {
                            match = false;
                        }
                    }
                    if (match)
                    {
                        return cached.labels;
                    }
                }
            }

            string[] labels = BuildDisplayLabelsUncached(options);
            DisplayLabelsCaches[cacheKey] = new DisplayLabelsCache
            {
                sourceOptions = options,
                labels = labels,
            };
            return labels;
        }

        private static string[] BuildDisplayLabelsUncached(object[] options)
        {
            string[] labels = new string[options.Length];
            for (int index = 0; index < options.Length; index += 1)
            {
                labels[index] = FormatOptionCached(options[index]);
            }

            return labels;
        }

        private static string FormatOptionCached(object option)
        {
            if (option == null)
            {
                return "(null)";
            }

            if (FormattedOptionCache.TryGetValue(option, out string cached))
            {
                return cached;
            }

            string formatted;
            if (option is Type type)
            {
                formatted = SerializableTypeCatalog.GetDisplayName(type);
            }
            else if (option is SerializableType serializableType)
            {
                formatted = serializableType.DisplayName;
            }
            else if (option is UnityEngine.Object unityObject)
            {
                // Handle Unity objects with null-safe name access
                // Unity objects may be destroyed but not null, so check explicitly
                if (unityObject == null)
                {
                    formatted = "(None)";
                }
                else
                {
                    string objectName = unityObject.name;
                    formatted = string.IsNullOrEmpty(objectName)
                        ? unityObject.GetType().Name
                        : objectName;
                }
            }
            else if (option is IFormattable formattable)
            {
                formatted = formattable.ToString(null, CultureInfo.InvariantCulture);
            }
            else
            {
                formatted = option.ToString();
            }

            if (string.IsNullOrEmpty(formatted))
            {
                formatted = $"({option.GetType().Name})";
            }

            FormattedOptionCache[option] = formatted;
            return formatted;
        }

        internal static void ApplyOption(SerializedProperty property, object selectedOption)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    ApplyInteger(property, selectedOption);
                    break;
                case SerializedPropertyType.Float:
                    ApplyFloat(property, selectedOption);
                    break;
                case SerializedPropertyType.String:
                    ApplyString(property, selectedOption);
                    break;
                case SerializedPropertyType.Enum:
                    ApplyEnum(property, selectedOption);
                    break;
                case SerializedPropertyType.Boolean:
                    ApplyBoolean(property, selectedOption);
                    break;
                case SerializedPropertyType.Character:
                    ApplyCharacter(property, selectedOption);
                    break;
                case SerializedPropertyType.ObjectReference:
                    ApplyObjectReference(property, selectedOption);
                    break;
                case SerializedPropertyType.Vector2:
                    ApplyVector2(property, selectedOption);
                    break;
                case SerializedPropertyType.Vector3:
                    ApplyVector3(property, selectedOption);
                    break;
                case SerializedPropertyType.Vector4:
                    ApplyVector4(property, selectedOption);
                    break;
                case SerializedPropertyType.Vector2Int:
                    ApplyVector2Int(property, selectedOption);
                    break;
                case SerializedPropertyType.Vector3Int:
                    ApplyVector3Int(property, selectedOption);
                    break;
                case SerializedPropertyType.Color:
                    ApplyColor(property, selectedOption);
                    break;
                case SerializedPropertyType.Rect:
                    ApplyRect(property, selectedOption);
                    break;
                case SerializedPropertyType.RectInt:
                    ApplyRectInt(property, selectedOption);
                    break;
                case SerializedPropertyType.Bounds:
                    ApplyBounds(property, selectedOption);
                    break;
                case SerializedPropertyType.BoundsInt:
                    ApplyBoundsInt(property, selectedOption);
                    break;
                case SerializedPropertyType.Quaternion:
                    ApplyQuaternion(property, selectedOption);
                    break;
                case SerializedPropertyType.AnimationCurve:
                    ApplyAnimationCurve(property, selectedOption);
                    break;
                case SerializedPropertyType.Hash128:
                    ApplyHash128(property, selectedOption);
                    break;
                case SerializedPropertyType.Generic:
                    if (IsSerializableTypeProperty(property))
                    {
                        ApplySerializableType(property, selectedOption);
                    }
                    else
                    {
                        ApplyGenericProperty(property, selectedOption);
                    }
                    break;
                default:
                    ApplyGenericProperty(property, selectedOption);
                    break;
            }
        }

        private static void ApplyBoolean(SerializedProperty property, object selectedOption)
        {
            if (selectedOption is bool boolValue)
            {
                property.boolValue = boolValue;
            }
        }

        private static void ApplyCharacter(SerializedProperty property, object selectedOption)
        {
            if (selectedOption is char charValue)
            {
                // Unity stores char as intValue in SerializedProperty
                property.intValue = charValue;
            }
        }

        private static void ApplyObjectReference(SerializedProperty property, object selectedOption)
        {
            if (selectedOption == null)
            {
                property.objectReferenceValue = null;
                return;
            }

            if (selectedOption is UnityEngine.Object unityObject)
            {
                property.objectReferenceValue = unityObject;
            }
        }

        private static void ApplyVector2(SerializedProperty property, object selectedOption)
        {
            if (selectedOption is Vector2 value)
            {
                property.vector2Value = value;
            }
        }

        private static void ApplyVector3(SerializedProperty property, object selectedOption)
        {
            if (selectedOption is Vector3 value)
            {
                property.vector3Value = value;
            }
        }

        private static void ApplyVector4(SerializedProperty property, object selectedOption)
        {
            if (selectedOption is Vector4 value)
            {
                property.vector4Value = value;
            }
        }

        private static void ApplyVector2Int(SerializedProperty property, object selectedOption)
        {
            if (selectedOption is Vector2Int value)
            {
                property.vector2IntValue = value;
            }
        }

        private static void ApplyVector3Int(SerializedProperty property, object selectedOption)
        {
            if (selectedOption is Vector3Int value)
            {
                property.vector3IntValue = value;
            }
        }

        private static void ApplyColor(SerializedProperty property, object selectedOption)
        {
            if (selectedOption is Color value)
            {
                property.colorValue = value;
            }
            else if (selectedOption is Color32 color32)
            {
                property.colorValue = color32;
            }
        }

        private static void ApplyRect(SerializedProperty property, object selectedOption)
        {
            if (selectedOption is Rect value)
            {
                property.rectValue = value;
            }
        }

        private static void ApplyRectInt(SerializedProperty property, object selectedOption)
        {
            if (selectedOption is RectInt value)
            {
                property.rectIntValue = value;
            }
        }

        private static void ApplyBounds(SerializedProperty property, object selectedOption)
        {
            if (selectedOption is Bounds value)
            {
                property.boundsValue = value;
            }
        }

        private static void ApplyBoundsInt(SerializedProperty property, object selectedOption)
        {
            if (selectedOption is BoundsInt value)
            {
                property.boundsIntValue = value;
            }
        }

        private static void ApplyQuaternion(SerializedProperty property, object selectedOption)
        {
            if (selectedOption is Quaternion value)
            {
                property.quaternionValue = value;
            }
        }

        private static void ApplyAnimationCurve(SerializedProperty property, object selectedOption)
        {
            if (selectedOption is AnimationCurve value)
            {
                property.animationCurveValue = value;
            }
        }

        private static void ApplyHash128(SerializedProperty property, object selectedOption)
        {
            if (selectedOption is Hash128 value)
            {
                property.hash128Value = value;
            }
        }

        private static void ApplySerializableType(
            SerializedProperty property,
            object selectedOption
        )
        {
            SerializedProperty assemblyQualifiedNameProperty = GetSerializableTypeStringProperty(
                property
            );
            if (assemblyQualifiedNameProperty == null)
            {
                return;
            }

            string assemblyQualifiedName = GetAssemblyQualifiedNameFromOption(selectedOption);
            assemblyQualifiedNameProperty.stringValue = assemblyQualifiedName;
        }

        private static void ApplyGenericProperty(SerializedProperty property, object selectedOption)
        {
            if (selectedOption == null)
            {
                return;
            }

            try
            {
                SerializedObject serializedObject = property.serializedObject;
                if (serializedObject == null)
                {
                    return;
                }

                UnityEngine.Object[] targetObjects = serializedObject.targetObjects;
                if (targetObjects == null || targetObjects.Length == 0)
                {
                    return;
                }

                string path = property.propertyPath;
                for (int i = 0; i < targetObjects.Length; i++)
                {
                    UnityEngine.Object target = targetObjects[i];
                    if (target == null)
                    {
                        continue;
                    }

                    SetFieldValueFromPropertyPath(target, path, selectedOption);
                    EditorUtility.SetDirty(target);
                }
            }
            catch (Exception)
            {
                // Silently fail if we can't set the value
            }
        }

        private static void SetFieldValueFromPropertyPath(
            object target,
            string propertyPath,
            object value
        )
        {
            if (target == null || string.IsNullOrEmpty(propertyPath))
            {
                return;
            }

            string[] pathParts = propertyPath.Split('.');
            object current = target;

            // Navigate to the parent of the final field
            for (int i = 0; i < pathParts.Length - 1; i++)
            {
                if (current == null)
                {
                    return;
                }

                string part = pathParts[i];

                // Handle array access pattern
                if (
                    part == "Array"
                    && i + 1 < pathParts.Length - 1
                    && pathParts[i + 1].StartsWith("data[", StringComparison.Ordinal)
                )
                {
                    string indexPart = pathParts[i + 1];
                    int startIndex = indexPart.IndexOf('[') + 1;
                    int endIndex = indexPart.IndexOf(']');
                    if (startIndex > 0 && endIndex > startIndex)
                    {
                        string indexStr = indexPart.Substring(startIndex, endIndex - startIndex);
                        if (int.TryParse(indexStr, out int arrayIndex))
                        {
                            if (
                                current is System.Collections.IList list
                                && arrayIndex >= 0
                                && arrayIndex < list.Count
                            )
                            {
                                current = list[arrayIndex];
                                i++;
                                continue;
                            }
                        }
                    }
                    return;
                }

                Type currentType = current.GetType();
                System.Reflection.FieldInfo field = currentType.GetField(
                    part,
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                );

                if (field == null)
                {
                    return;
                }

                current = field.GetValue(current);
            }

            if (current == null)
            {
                return;
            }

            // Set the final field
            string finalPart = pathParts[pathParts.Length - 1];

            // Handle array element assignment
            if (finalPart.StartsWith("data[", StringComparison.Ordinal))
            {
                int startIndex = finalPart.IndexOf('[') + 1;
                int endIndex = finalPart.IndexOf(']');
                if (startIndex > 0 && endIndex > startIndex)
                {
                    string indexStr = finalPart.Substring(startIndex, endIndex - startIndex);
                    if (int.TryParse(indexStr, out int arrayIndex))
                    {
                        if (
                            current is System.Collections.IList list
                            && arrayIndex >= 0
                            && arrayIndex < list.Count
                        )
                        {
                            list[arrayIndex] = value;
                            return;
                        }
                    }
                }
                return;
            }

            Type finalType = current.GetType();
            System.Reflection.FieldInfo finalField = finalType.GetField(
                finalPart,
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
            );

            if (finalField != null && finalField.FieldType.IsAssignableFrom(value.GetType()))
            {
                finalField.SetValue(current, value);
            }
        }

        private static void ApplyInteger(SerializedProperty property, object selectedOption)
        {
            if (selectedOption == null)
            {
                return;
            }

            try
            {
                long value = Convert.ToInt64(selectedOption, CultureInfo.InvariantCulture);
                property.longValue = value;
            }
            catch (Exception) { }
        }

        private static void ApplyFloat(SerializedProperty property, object selectedOption)
        {
            if (selectedOption == null)
            {
                return;
            }

            try
            {
                double value = Convert.ToDouble(selectedOption, CultureInfo.InvariantCulture);
                if (IsDoubleProperty(property))
                {
                    property.doubleValue = value;
                }
                else
                {
                    property.floatValue = (float)value;
                }
            }
            catch (Exception) { }
        }

        private static bool IsDoubleProperty(SerializedProperty property)
        {
            if (property == null)
            {
                return false;
            }

            return string.Equals(property.type, "double", StringComparison.Ordinal);
        }

        private static void ApplyString(SerializedProperty property, object selectedOption)
        {
            property.stringValue =
                selectedOption == null
                    ? string.Empty
                    : Convert.ToString(selectedOption, CultureInfo.InvariantCulture)
                        ?? string.Empty;
        }

        private static void ApplyEnum(SerializedProperty property, object selectedOption)
        {
            if (selectedOption == null)
            {
                return;
            }

            string optionName;
            if (selectedOption is Enum enumValue)
            {
                optionName = enumValue.ToString();
            }
            else if (selectedOption is string stringValue)
            {
                optionName = stringValue;
            }
            else
            {
                optionName = Convert.ToString(selectedOption, CultureInfo.InvariantCulture);
            }

            if (property.enumNames == null || property.enumNames.Length == 0)
            {
                return;
            }

            for (int index = 0; index < property.enumNames.Length; index += 1)
            {
                if (string.Equals(property.enumNames[index], optionName, StringComparison.Ordinal))
                {
                    property.enumValueIndex = index;
                    return;
                }
            }
        }

        private sealed class WValueDropDownPopupContent : PopupWindowContent
        {
            private readonly SerializedObject _serializedObject;
            private readonly string _propertyPath;
            private readonly object[] _options;
            private readonly PopupState _state;
            private readonly WValueDropDownAttribute _attribute;
            private static readonly GUIContent PreviousPageContent = new("<", "Previous page");
            private static readonly GUIContent NextPageContent = new(">", "Next page");
            private static readonly GUIContent ReusableOptionContent = new();
            private int _pageSize;
            private float _emptyStateMeasuredHeight = -1f;

            public WValueDropDownPopupContent(
                SerializedProperty property,
                object[] options,
                PopupState state,
                int pageSize,
                WValueDropDownAttribute attribute
            )
            {
                _serializedObject = property.serializedObject;
                _propertyPath = property.propertyPath;
                _options = options ?? Array.Empty<object>();
                _state = state ?? new PopupState();
                _pageSize = Mathf.Max(1, pageSize);
                _attribute = attribute;
            }

            public override Vector2 GetWindowSize()
            {
                int pageSize = ResolvePageSize();
                int filteredCount = CalculateFilteredCount();
                bool includePagination = filteredCount > pageSize;
                float height;
                if (filteredCount == 0)
                {
                    float measured = _emptyStateMeasuredHeight;
                    height = CalculateEmptySearchHeight(measuredHelpBoxHeight: measured);
                    return new Vector2(PopupWidth, height);
                }

                int pageCount = CalculatePageCount(pageSize, filteredCount);
                _state.page = Mathf.Clamp(_state.page, 0, pageCount - 1);
                int rowsOnPage = CalculateRowsOnPage(filteredCount, pageSize, _state.page);
                includePagination = pageCount > 1;
                height = CalculatePopupTargetHeight(rowsOnPage, includePagination);
                return new Vector2(PopupWidth, height);
            }

            public override void OnGUI(Rect rect)
            {
                if (_serializedObject == null || string.IsNullOrEmpty(_propertyPath))
                {
                    EditorGUILayout.HelpBox(
                        "Unable to resolve property for WValueDropDown.",
                        MessageType.Warning
                    );
                    return;
                }

                _serializedObject.UpdateIfRequiredOrScript();
                SerializedProperty property = _serializedObject.FindProperty(_propertyPath);
                if (property == null)
                {
                    EditorGUILayout.HelpBox(
                        "Unable to resolve property for WValueDropDown.",
                        MessageType.Warning
                    );
                    return;
                }

                DrawSearchControls();

                using PooledResource<List<int>> filteredLease = Buffers<int>.List.Get(
                    out List<int> filtered
                );

                bool hasSearch = !string.IsNullOrWhiteSpace(_state.search);
                string searchTerm = _state.search ?? string.Empty;
                if (hasSearch)
                {
                    for (int i = 0; i < _options.Length; i++)
                    {
                        string optionLabel = FormatOptionCached(_options[i]);
                        if (
                            optionLabel.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0
                        )
                        {
                            filtered.Add(i);
                        }
                    }
                }

                int filteredCount = hasSearch ? filtered.Count : _options.Length;
                int pageSize = ResolvePageSize();
                int pageCount = CalculatePageCount(pageSize, filteredCount);
                _state.page = Mathf.Clamp(_state.page, 0, pageCount - 1);
                if (filteredCount == 0)
                {
                    DrawEmptyResultsMessage();
                    _state.page = 0;
                    return;
                }

                if (pageCount > 1)
                {
                    DrawPaginationControls(pageCount);
                }
                else
                {
                    EditorGUILayout.Space(EditorGUIUtility.standardVerticalSpacing);
                }

                int startIndex = _state.page * pageSize;
                int endIndex = Math.Min(filteredCount, startIndex + pageSize);
                int rowsOnPage = Mathf.Max(1, endIndex - startIndex);
                int currentSelectionIndex = property.hasMultipleDifferentValues
                    ? -1
                    : ResolveSelectedIndex(property, _attribute.ValueType, _options);

                using (new EditorGUILayout.VerticalScope())
                {
                    for (int i = startIndex; i < endIndex; i++)
                    {
                        int optionIndex = hasSearch ? filtered[i] : i;
                        GUIContent optionContent = GetOptionContent(optionIndex);
                        bool isSelected = optionIndex == currentSelectionIndex;
                        GUIStyle style = isSelected
                            ? PopupStyles.SelectedOptionButton
                            : PopupStyles.OptionButton;
                        if (
                            GUILayout.Button(
                                optionContent,
                                style,
                                GUILayout.ExpandWidth(true),
                                GUILayout.Height(GetOptionControlHeight())
                            )
                        )
                        {
                            ApplySelection(property, optionIndex);
                        }
                    }

                    GUILayout.Space(EditorGUIUtility.standardVerticalSpacing + OptionBottomPadding);
                }

                bool includePagination = pageCount > 1;
                EnsureWindowFitsPageSize(rowsOnPage, includePagination);
                _emptyStateMeasuredHeight = -1f;
            }

            private void DrawSearchControls()
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Search", GUILayout.Width(55f));
                    EditorGUI.BeginChangeCheck();
                    string newSearch = EditorGUILayout.TextField(
                        _state.search ?? string.Empty,
                        GUILayout.ExpandWidth(true)
                    );
                    if (EditorGUI.EndChangeCheck())
                    {
                        _state.search = newSearch ?? string.Empty;
                        _state.page = 0;
                    }

                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_state.search)))
                    {
                        if (GUILayout.Button("Clear", GUILayout.Width(60f)))
                        {
                            _state.search = string.Empty;
                            _state.page = 0;
                        }
                    }
                }
            }

            private void DrawEmptyResultsMessage()
            {
                EditorGUILayout.Space(EditorGUIUtility.standardVerticalSpacing);
                EditorGUILayout.HelpBox(EmptyResultsMessage, MessageType.Info);
                float measuredHelpHeight = TryGetLastRectHeight();
                if (measuredHelpHeight > 0f)
                {
                    _emptyStateMeasuredHeight = measuredHelpHeight;
                }
                EditorGUILayout.Space(EditorGUIUtility.standardVerticalSpacing);
                float targetHeight = CalculateEmptySearchHeight(_emptyStateMeasuredHeight);
                EnsureWindowHeight(targetHeight);
            }

            private void EnsureWindowHeight(float targetHeight)
            {
                if (editorWindow == null)
                {
                    return;
                }

                Rect windowPosition = editorWindow.position;
                float delta = Mathf.Abs(windowPosition.height - targetHeight);
                if (delta <= 0.5f)
                {
                    return;
                }

                windowPosition.height = targetHeight;
                editorWindow.position = windowPosition;
            }

            private static float TryGetLastRectHeight()
            {
                Event evt = Event.current;
                if (evt == null || evt.type != EventType.Repaint)
                {
                    return -1f;
                }

                Rect lastRect = GUILayoutUtility.GetLastRect();
                return lastRect.height > 0f ? lastRect.height : -1f;
            }

            private void DrawPaginationControls(int pageCount)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(_state.page <= 0))
                    {
                        if (
                            GUILayout.Button(
                                PreviousPageContent,
                                PopupStyles.PaginationButtonLeft,
                                GUILayout.Width(ButtonWidth + 8f)
                            )
                        )
                        {
                            _state.page = Mathf.Max(0, _state.page - 1);
                        }
                    }

                    GUILayout.Label(
                        GetPaginationLabel(_state.page + 1, Mathf.Max(1, pageCount)),
                        PopupStyles.PaginationLabel,
                        GUILayout.Width(PageLabelWidth),
                        GUILayout.Height(PopupStyles.PaginationButtonLeft.fixedHeight)
                    );

                    using (new EditorGUI.DisabledScope(_state.page >= pageCount - 1))
                    {
                        if (
                            GUILayout.Button(
                                NextPageContent,
                                PopupStyles.PaginationButtonRight,
                                GUILayout.Width(ButtonWidth + 8f)
                            )
                        )
                        {
                            _state.page = Mathf.Min(pageCount - 1, _state.page + 1);
                        }
                    }
                    GUILayout.FlexibleSpace();
                }
            }

            private void EnsureWindowFitsPageSize(int rowsOnPage, bool includePagination)
            {
                if (editorWindow == null)
                {
                    return;
                }

                float measuredHeight = CalculateMeasuredContentHeight(includePagination);
                float fallbackHeight = CalculatePopupTargetHeight(rowsOnPage, includePagination);
                float targetHeight = measuredHeight > 0f ? measuredHeight : fallbackHeight;
                EnsureWindowHeight(targetHeight);
            }

            private float CalculateMeasuredContentHeight(bool includePagination)
            {
                Event current = Event.current;
                if (current == null || current.type != EventType.Repaint)
                {
                    return -1f;
                }

                Rect lastRect = GUILayoutUtility.GetLastRect();
                if (lastRect.height <= 0f && lastRect.yMax <= 0f)
                {
                    return -1f;
                }

                float measuredHeight = lastRect.yMax;
                float minimumHeight =
                    CalculatePopupChromeHeight(includePagination) + GetOptionRowHeight();
                float result = Mathf.Max(measuredHeight, minimumHeight);
                return result;
            }

            private int ResolvePageSize()
            {
                int resolved = Mathf.Max(1, UnityHelpersSettings.GetStringInListPageLimit());
                if (resolved != _pageSize)
                {
                    _pageSize = resolved;
                    _state.page = 0;
                }
                return _pageSize;
            }

            private int CalculateFilteredCount()
            {
                if (string.IsNullOrWhiteSpace(_state.search))
                {
                    return _options.Length;
                }

                string searchTerm = _state.search ?? string.Empty;
                int count = 0;
                for (int i = 0; i < _options.Length; i++)
                {
                    string optionLabel = FormatOptionCached(_options[i]);
                    if (optionLabel.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        count++;
                    }
                }
                return count;
            }

            private void ApplySelection(SerializedProperty property, int optionIndex)
            {
                if (
                    optionIndex < 0
                    || optionIndex >= _options.Length
                    || _serializedObject == null
                    || string.IsNullOrEmpty(_propertyPath)
                )
                {
                    return;
                }

                SerializedObject serializedObject = _serializedObject;
                Undo.RecordObjects(serializedObject.targetObjects, "Change Value DropDown");
                serializedObject.Update();

                SerializedProperty prop = serializedObject.FindProperty(_propertyPath);
                if (prop == null)
                {
                    serializedObject.ApplyModifiedProperties();
                    return;
                }

                ApplyOption(prop, _options[optionIndex]);

                serializedObject.ApplyModifiedProperties();
                editorWindow?.Close();
                GUIUtility.ExitGUI();
            }

            private GUIContent GetOptionContent(int optionIndex)
            {
                string label =
                    optionIndex >= 0 && optionIndex < _options.Length
                        ? FormatOptionCached(_options[optionIndex])
                        : string.Empty;
                ReusableOptionContent.text = label;
                ReusableOptionContent.tooltip = string.Empty;
                return ReusableOptionContent;
            }
        }

        private sealed class WValueDropDownPopupSelectorElement : WDropDownPopupSelectorBase<string>
        {
            private readonly object[] _options;
            private readonly WValueDropDownAttribute _attribute;

            public WValueDropDownPopupSelectorElement(
                object[] options,
                WValueDropDownAttribute attribute
            )
            {
                _options = options ?? Array.Empty<object>();
                _attribute = attribute;
            }

            protected override int OptionCount => _options.Length;

            protected override string GetDisplayValue(SerializedProperty property)
            {
                return ResolveDisplayValue(property, _options, _attribute, out _);
            }

            protected override string GetFieldValue(SerializedProperty property)
            {
                return GetDisplayValue(property);
            }

            protected override void ShowPopup(
                Rect controlRect,
                SerializedProperty property,
                int pageSize
            )
            {
                string cacheKey = property.propertyPath + "::popup";
                string[] displayLabels = GetOrCreateDisplayLabels(cacheKey, _options);
                int currentIndex = ResolveSelectedIndex(property, _attribute.ValueType, _options);

                SerializedObject serializedObject = property.serializedObject;
                string propertyPath = property.propertyPath;
                object[] options = _options;

                WDropDownPopupData data = new()
                {
                    DisplayLabels = displayLabels,
                    Tooltips = null,
                    SelectedIndex = property.hasMultipleDifferentValues ? -1 : currentIndex,
                    PageSize = pageSize,
                    OnSelectionChanged = (selectedIndex) =>
                    {
                        if (selectedIndex < 0 || selectedIndex >= options.Length)
                        {
                            return;
                        }

                        serializedObject.Update();
                        SerializedProperty prop = serializedObject.FindProperty(propertyPath);
                        if (prop == null)
                        {
                            return;
                        }

                        Undo.RecordObjects(
                            serializedObject.targetObjects,
                            "Change ValueDropDown Selection"
                        );
                        ApplyOption(prop, options[selectedIndex]);
                        serializedObject.ApplyModifiedProperties();
                    },
                };

                Rect screenRect = GUIUtility.GUIToScreenRect(controlRect);
                WDropDownPopupWindow.Show(screenRect, data);
            }
        }

        private sealed class WValueDropDownSelector : WDropDownSelectorBase<string>
        {
            private readonly object[] _options;
            private readonly WValueDropDownAttribute _attribute;

            public WValueDropDownSelector(object[] options, WValueDropDownAttribute attribute)
            {
                _options = options ?? Array.Empty<object>();
                _attribute = attribute;
                InitializeSearchVisibility();
            }

            protected override int OptionCount => _options.Length;

            protected override string GetDisplayLabel(int optionIndex)
            {
                return FormatOptionCached(_options[optionIndex]);
            }

            protected override int GetCurrentSelectionIndex(SerializedProperty property)
            {
                if (property.hasMultipleDifferentValues)
                {
                    return -1;
                }
                return ResolveSelectedIndex(property, _attribute.ValueType, _options);
            }

            protected override void ApplySelectionToProperty(
                SerializedProperty property,
                int optionIndex
            )
            {
                ApplyOption(property, _options[optionIndex]);
            }

            protected override string GetValueForOption(int optionIndex)
            {
                return FormatOptionCached(_options[optionIndex]);
            }

            protected override string GetDefaultValue() => string.Empty;

            protected override string UndoActionName => "Change Value DropDown";
        }

        private static float CalculatePopupTargetHeight(int rowsOnPage, bool includePagination)
        {
            int clampedRows = Mathf.Max(1, rowsOnPage);
            float chromeHeight = CalculatePopupChromeHeight(includePagination);
            float optionListHeight = clampedRows * GetOptionRowHeight();
            float unclampedHeight = chromeHeight + optionListHeight;
            return unclampedHeight;
        }

        private static float CalculatePopupChromeHeight(bool includePagination)
        {
            float searchHeight = EditorGUIUtility.singleLineHeight;
            float paginationHeight = includePagination
                ? PopupStyles.PaginationButtonLeft.fixedHeight
                : EditorGUIUtility.standardVerticalSpacing;
            float footerHeight = EditorGUIUtility.standardVerticalSpacing + OptionBottomPadding;
            return searchHeight + paginationHeight + footerHeight;
        }

        private static float CalculateEmptySearchHeight(float measuredHelpBoxHeight = -1f)
        {
            GUIStyle helpStyle = EditorStyles.helpBox;
            int helpMargin = helpStyle.margin?.horizontal ?? 0;
            float availableWidth = PopupWidth - EmptySearchHorizontalPadding - helpMargin;
            availableWidth = Mathf.Max(32f, availableWidth);
            float helpBoxHeight;
            if (measuredHelpBoxHeight > 0f)
            {
                helpBoxHeight = measuredHelpBoxHeight;
            }
            else
            {
                float calculated = helpStyle.CalcHeight(EmptyResultsContent, availableWidth);
                float marginVertical = helpStyle.margin?.vertical ?? 0;
                helpBoxHeight = calculated + marginVertical;
            }

            float searchRow =
                EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            float topSpacer = EditorGUIUtility.standardVerticalSpacing;
            float bottomSpacer = EditorGUIUtility.standardVerticalSpacing;
            float footer =
                EditorGUIUtility.standardVerticalSpacing
                + OptionBottomPadding
                + EmptySearchExtraPadding;

            float result = searchRow + topSpacer + helpBoxHeight + bottomSpacer + footer;
            return result;
        }

        private static float GetOptionRowHeight()
        {
            if (s_cachedOptionRowHeight > 0f)
            {
                return s_cachedOptionRowHeight;
            }

            float controlHeight = GetOptionControlHeight();
            RectOffset margin = PopupStyles.OptionButton.margin;
            float adjustedMargin = 0f;
            if (margin != null)
            {
                adjustedMargin = Mathf.Max(
                    0f,
                    margin.vertical - EditorGUIUtility.standardVerticalSpacing
                );
            }
            else
            {
                adjustedMargin = EditorGUIUtility.standardVerticalSpacing;
            }

            s_cachedOptionRowHeight = controlHeight + adjustedMargin;
            return s_cachedOptionRowHeight;
        }

        private static float GetOptionControlHeight()
        {
            if (s_cachedOptionControlHeight > 0f)
            {
                return s_cachedOptionControlHeight;
            }

            float width = PopupWidth - 32f;
            float measured = PopupStyles.OptionButton.CalcHeight(GUIContent.none, width);
            if (measured <= 0f || float.IsNaN(measured))
            {
                measured = EditorGUIUtility.singleLineHeight + OptionRowExtraHeight;
            }

            s_cachedOptionControlHeight = measured;
            return measured;
        }

        internal static class TestHooks
        {
            public static float CalculatePopupTargetHeight(int rowsOnPage, bool includePagination)
            {
                return WValueDropDownDrawer.CalculatePopupTargetHeight(
                    rowsOnPage,
                    includePagination
                );
            }

            public static float CalculatePopupChromeHeight(bool includePagination)
            {
                return WValueDropDownDrawer.CalculatePopupChromeHeight(includePagination);
            }

            public static float GetOptionRowHeight()
            {
                return WValueDropDownDrawer.GetOptionRowHeight();
            }

            public static float GetOptionControlHeight()
            {
                return WValueDropDownDrawer.GetOptionControlHeight();
            }

            public static int OptionButtonMarginVertical =>
                PopupStyles.OptionButton.margin?.vertical ?? 0;

            public static float OptionFooterPadding => OptionBottomPadding;

            public static float PaginationButtonHeight =>
                PopupStyles.PaginationButtonLeft.fixedHeight;

            public static float PopupWidthValue => PopupWidth;

            public static float EmptySearchHorizontalPaddingValue => EmptySearchHorizontalPadding;

            public static string EmptyResultsMessageValue => EmptyResultsMessage;

            public static float EmptySearchExtraPaddingValue => EmptySearchExtraPadding;

            public static float CalculateEmptySearchHeight()
            {
                return WValueDropDownDrawer.CalculateEmptySearchHeight();
            }

            public static float CalculateEmptySearchHeightWithMeasurement(float measuredHelpHeight)
            {
                return WValueDropDownDrawer.CalculateEmptySearchHeight(measuredHelpHeight);
            }

            public static int CalculateRowsOnPage(int filteredCount, int pageSize, int currentPage)
            {
                return WValueDropDownDrawer.CalculateRowsOnPage(
                    filteredCount,
                    pageSize,
                    currentPage
                );
            }

            public static int ResolveSelectedIndex(
                SerializedProperty property,
                Type valueType,
                object[] options
            )
            {
                return WValueDropDownDrawer.ResolveSelectedIndex(property, valueType, options);
            }

            public static string FormatOptionCached(object option)
            {
                return WValueDropDownDrawer.FormatOptionCached(option);
            }

            public static string[] BuildDisplayLabelsUncached(object[] options)
            {
                return WValueDropDownDrawer.BuildDisplayLabelsUncached(options);
            }
        }

        private static class PopupStyles
        {
            // Built lazily on first GUI access. A static constructor that touches EditorStyles
            // throws a NullReferenceException when the type is first loaded outside an active
            // IMGUI context (e.g. batch-mode test runs); lazy initialization defers that access
            // to actual rendering, where the editor skin is ready.
            private static GUIStyle _optionButton;
            private static GUIStyle _selectedOptionButton;
            private static GUIStyle _paginationButtonLeft;
            private static GUIStyle _paginationButtonRight;
            private static GUIStyle _paginationLabel;

            public static GUIStyle OptionButton =>
                _optionButton ??= new GUIStyle("Button")
                {
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(6, 6, 1, 1),
                };

            public static GUIStyle SelectedOptionButton =>
                _selectedOptionButton ??= new GUIStyle(OptionButton) { fontStyle = FontStyle.Bold };

            public static GUIStyle PaginationButtonLeft =>
                _paginationButtonLeft ??= new GUIStyle(EditorStyles.miniButtonLeft)
                {
                    fixedHeight = PaginationButtonHeight,
                    padding = new RectOffset(6, 6, 0, 0),
                };

            public static GUIStyle PaginationButtonRight =>
                _paginationButtonRight ??= new GUIStyle(EditorStyles.miniButtonRight)
                {
                    fixedHeight = PaginationButtonHeight,
                    padding = new RectOffset(6, 6, 0, 0),
                };

            public static GUIStyle PaginationLabel =>
                _paginationLabel ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(0, 0, 0, 0),
                };
        }

        private static string GetTypeMismatchMessage(
            SerializedProperty property,
            WValueDropDownAttribute dropdownAttribute
        )
        {
            string fieldName = property.displayName;
            string actualType = GetPropertyTypeName(property);
            string expectedType = GetExpectedTypeName(dropdownAttribute);
            return $"[WValueDropDown] Type mismatch: '{fieldName}' is {actualType}, but the dropdown provides {expectedType} values. Most serializable types are supported (primitives, enums, UnityEngine.Object, Vector2/3/4, Color, structs, etc.). Arrays are not supported.";
        }

        private static string GetExpectedTypeName(WValueDropDownAttribute dropdownAttribute)
        {
            if (dropdownAttribute?.ValueType == null)
            {
                return "unknown";
            }

            Type valueType = dropdownAttribute.ValueType;
            if (valueType == typeof(int))
            {
                return "int";
            }
            if (valueType == typeof(float))
            {
                return "float";
            }
            if (valueType == typeof(double))
            {
                return "double";
            }
            if (valueType == typeof(string))
            {
                return "string";
            }
            if (valueType == typeof(long))
            {
                return "long";
            }
            if (valueType == typeof(short))
            {
                return "short";
            }
            if (valueType == typeof(byte))
            {
                return "byte";
            }
            if (valueType.IsEnum)
            {
                return $"enum ({valueType.Name})";
            }

            return valueType.Name;
        }

        private static string GetPropertyTypeName(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.Integer => "an int",
                SerializedPropertyType.Float => "a float",
                SerializedPropertyType.String => "a string",
                SerializedPropertyType.Enum => "an enum",
                SerializedPropertyType.Boolean => "a bool",
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
                SerializedPropertyType.Generic when property.isArray =>
                    $"an array of {property.arrayElementType}",
                _ => $"type '{property.propertyType}'",
            };
        }
    }
#endif
}
