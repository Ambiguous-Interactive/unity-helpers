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
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Base;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// UI Toolkit drawer for <see cref="StringInListAttribute"/> that provides search, pagination, and autocomplete.
    /// </summary>
    [CustomPropertyDrawer(typeof(StringInListAttribute))]
    public sealed class StringInListDrawer : PropertyDrawer
    {
        private sealed class PopupState
        {
            public string search = string.Empty;
            public int page;
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

        private static void DrawGenericMenuDropDown(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            string[] options,
            StringInListAttribute attribute
        )
        {
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
                string[] displayLabels = GetOptionDisplayArray(attribute, options);
                int currentIndex = ResolveCurrentSelectionIndex(property, options);

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
                                "Change StringInList Selection"
                            );
                            ApplySelection(prop, options, capturedIndex);
                            serializedObject.ApplyModifiedProperties();
                        }
                    );
                }
                menu.DropDown(fieldRect);
            }

            EditorGUI.showMixedValue = previousMixed;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            StringInListAttribute stringInList = (StringInListAttribute)attribute;
            UnityEngine.Object context = property.serializedObject?.targetObject;
            string[] options = stringInList.GetOptions(context) ?? Array.Empty<string>();
            int pageSize = Mathf.Max(1, UnityHelpersSettings.GetStringInListPageLimit());
            if (options.Length > pageSize && IsSupportedSimpleProperty(property))
            {
                return EditorGUIUtility.singleLineHeight;
            }

            return EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            StringInListAttribute stringInList = (StringInListAttribute)attribute;
            UnityEngine.Object context = property.serializedObject?.targetObject;
            string[] options = stringInList.GetOptions(context) ?? Array.Empty<string>();
            int pageSize = Mathf.Max(1, UnityHelpersSettings.GetStringInListPageLimit());

            if (options.Length == 0)
            {
                EditorGUI.HelpBox(
                    position,
                    "No options available for StringInList.",
                    MessageType.Info
                );
                return;
            }

            if (IsSupportedArray(property))
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            if (!IsSupportedSimpleProperty(property))
            {
                string typeMismatchMessage = GetTypeMismatchMessage(property);
                EditorGUI.HelpBox(position, typeMismatchMessage, MessageType.Error);
                return;
            }

            if (options.Length > pageSize)
            {
                DrawPopupDropDown(position, property, label, options, pageSize, stringInList);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            DrawGenericMenuDropDown(position, property, label, options, stringInList);
            EditorGUI.EndProperty();
        }

        /// <inheritdoc/>
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            StringInListAttribute stringInList = (StringInListAttribute)attribute;
            UnityEngine.Object context = property.serializedObject?.targetObject;
            string[] options = stringInList.GetOptions(context) ?? Array.Empty<string>();
            int pageSize = Mathf.Max(1, UnityHelpersSettings.GetStringInListPageLimit());

            if (options.Length == 0)
            {
                return new HelpBox(
                    "No options available for StringInList.",
                    HelpBoxMessageType.Info
                );
            }

            if (IsSupportedArray(property))
            {
                return new StringInListArrayElement(property, options, stringInList);
            }

            if (!IsSupportedSimpleProperty(property))
            {
                return new HelpBox(GetTypeMismatchMessage(property), HelpBoxMessageType.Error);
            }

            if (options.Length > pageSize)
            {
                StringInListPopupSelectorElement popupElement = new(options, stringInList);
                popupElement.BindProperty(property, property.displayName);
                return popupElement;
            }

            StringInListSelector selector = new(options, stringInList);
            selector.BindProperty(property, property.displayName);
            return selector;
        }

        private static bool IsSupportedSimpleProperty(SerializedProperty property)
        {
            return property.propertyType == SerializedPropertyType.String
                || property.propertyType == SerializedPropertyType.Integer
                || IsSerializableTypeProperty(property);
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

        private static bool IsSupportedArray(SerializedProperty property)
        {
            if (!property.isArray || property.propertyType != SerializedPropertyType.Generic)
            {
                return false;
            }

            string elementType = property.arrayElementType;
            return string.Equals(elementType, "string", StringComparison.Ordinal)
                || string.Equals(elementType, "int", StringComparison.Ordinal);
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
            string[] options,
            int pageSize,
            StringInListAttribute attribute
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
                string[] displayLabels = GetOptionDisplayArray(attribute, options);
                string[] tooltips = BuildTooltipsArray(attribute, options);
                WDropDownPopupWindow.ShowForStringInList(
                    fieldRect,
                    property,
                    options,
                    displayLabels,
                    tooltips,
                    pageSize
                );
            }

            EditorGUI.showMixedValue = previousMixed;

            EditorGUI.EndProperty();
        }

        private static string[] BuildTooltipsArray(
            StringInListAttribute attribute,
            string[] options
        )
        {
            if (!IsSerializableTypeProvider(attribute))
            {
                return null;
            }

            string[] tooltips = SerializableTypeCatalog.GetTooltips();
            if (tooltips == null || tooltips.Length != options.Length)
            {
                return null;
            }

            return tooltips;
        }

        private static int ResolveCurrentSelectionIndex(
            SerializedProperty property,
            string[] options
        )
        {
            if (property.propertyType == SerializedPropertyType.String)
            {
                string selected = property.stringValue ?? string.Empty;
                return Array.IndexOf(options, selected);
            }

            if (property.propertyType == SerializedPropertyType.Integer)
            {
                int index = property.intValue;
                if (index >= 0 && index < options.Length)
                {
                    return index;
                }
            }

            if (IsSerializableTypeProperty(property))
            {
                SerializedProperty assemblyQualifiedNameProperty =
                    GetSerializableTypeStringProperty(property);
                if (assemblyQualifiedNameProperty != null)
                {
                    string selected = assemblyQualifiedNameProperty.stringValue ?? string.Empty;
                    return Array.IndexOf(options, selected);
                }
            }

            return -1;
        }

        private static string ResolveDisplayValue(
            SerializedProperty property,
            string[] options,
            StringInListAttribute attribute,
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

            if (property.propertyType == SerializedPropertyType.String)
            {
                string selected = property.stringValue ?? string.Empty;
                return GetOptionLabel(attribute, selected, out tooltip);
            }

            if (property.propertyType == SerializedPropertyType.Integer)
            {
                int index = property.intValue;
                if (index >= 0 && index < options.Length)
                {
                    return GetOptionLabel(attribute, options[index] ?? string.Empty, out tooltip);
                }

                return GetOptionLabel(attribute, property.intValue.ToString(), out tooltip);
            }

            if (IsSerializableTypeProperty(property))
            {
                SerializedProperty assemblyQualifiedNameProperty =
                    GetSerializableTypeStringProperty(property);
                if (assemblyQualifiedNameProperty != null)
                {
                    string selected = assemblyQualifiedNameProperty.stringValue ?? string.Empty;
                    return GetOptionLabel(attribute, selected, out tooltip);
                }
            }

            return string.Empty;
        }

        private static void ApplySelection(
            SerializedProperty property,
            string[] options,
            int optionIndex
        )
        {
            if (optionIndex < 0 || optionIndex >= options.Length)
            {
                return;
            }

            if (property.propertyType == SerializedPropertyType.String)
            {
                property.stringValue = options[optionIndex] ?? string.Empty;
            }
            else if (property.propertyType == SerializedPropertyType.Integer)
            {
                property.intValue = optionIndex;
            }
            else if (IsSerializableTypeProperty(property))
            {
                SerializedProperty assemblyQualifiedNameProperty =
                    GetSerializableTypeStringProperty(property);
                if (assemblyQualifiedNameProperty != null)
                {
                    assemblyQualifiedNameProperty.stringValue =
                        options[optionIndex] ?? string.Empty;
                }
            }
        }

        private sealed class StringInListPopupContent : PopupWindowContent
        {
            private readonly SerializedObject _serializedObject;
            private readonly string _propertyPath;
            private readonly string[] _options;
            private readonly PopupState _state;
            private readonly bool _isStringProperty;
            private readonly bool _isIntegerProperty;
            private readonly StringInListAttribute _attribute;
            private static readonly GUIContent PreviousPageContent = new("<", "Previous page");
            private static readonly GUIContent NextPageContent = new(">", "Next page");
            private static readonly GUIContent ReusableOptionContent = new();
            private int _pageSize;
            private float _emptyStateMeasuredHeight = -1f;

            public StringInListPopupContent(
                SerializedProperty property,
                string[] options,
                PopupState state,
                int pageSize,
                StringInListAttribute attribute
            )
            {
                _serializedObject = property.serializedObject;
                _propertyPath = property.propertyPath;
                _options = options ?? Array.Empty<string>();
                _state = state ?? new PopupState();
                _isStringProperty = property.propertyType == SerializedPropertyType.String;
                _isIntegerProperty = property.propertyType == SerializedPropertyType.Integer;
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
                        "Unable to resolve property for StringInList.",
                        MessageType.Warning
                    );
                    return;
                }

                _serializedObject.UpdateIfRequiredOrScript();
                SerializedProperty property = _serializedObject.FindProperty(_propertyPath);
                if (property == null)
                {
                    EditorGUILayout.HelpBox(
                        "Unable to resolve property for StringInList.",
                        MessageType.Warning
                    );
                    return;
                }

                DrawSearchControls();

                using PooledResource<List<int>> filteredLease = Buffers<int>.List.Get(
                    out List<int> filtered
                );
                filtered.Clear();

                bool hasSearch = !string.IsNullOrWhiteSpace(_state.search);
                string searchTerm = _state.search ?? string.Empty;
                if (hasSearch)
                {
                    for (int i = 0; i < _options.Length; i++)
                    {
                        string option = _options[i] ?? string.Empty;
                        if (option.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0)
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
                    : ResolveCurrentSelectionIndex(property, _options);

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
                            ApplySelection(optionIndex);
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
                    string option = _options[i] ?? string.Empty;
                    if (option.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        count++;
                    }
                }
                return count;
            }

            private void ApplySelection(int optionIndex)
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
                Undo.RecordObjects(serializedObject.targetObjects, "Change String In List");
                serializedObject.Update();

                SerializedProperty property = serializedObject.FindProperty(_propertyPath);
                if (property == null)
                {
                    serializedObject.ApplyModifiedProperties();
                    return;
                }

                if (_isStringProperty)
                {
                    property.stringValue = _options[optionIndex] ?? string.Empty;
                }
                else if (_isIntegerProperty)
                {
                    property.intValue = optionIndex;
                }

                serializedObject.ApplyModifiedProperties();
                editorWindow?.Close();
                GUIUtility.ExitGUI();
            }

            private GUIContent GetOptionContent(int optionIndex)
            {
                string value =
                    optionIndex >= 0 && optionIndex < _options.Length
                        ? _options[optionIndex] ?? string.Empty
                        : string.Empty;
                string label = GetOptionLabel(_attribute, value, out string tooltip);
                ReusableOptionContent.text = label;
                ReusableOptionContent.tooltip = tooltip;
                return ReusableOptionContent;
            }
        }

        private sealed class StringInListPopupSelectorElement : WDropDownPopupSelectorBase<string>
        {
            private readonly string[] _options;
            private readonly StringInListAttribute _attribute;

            public StringInListPopupSelectorElement(
                string[] options,
                StringInListAttribute attribute
            )
            {
                _options = options ?? Array.Empty<string>();
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
                string[] displayLabels = GetOptionDisplayArray(_attribute, _options);
                string[] tooltips = BuildTooltipsArray(_attribute, _options);
                WDropDownPopupWindow.ShowForStringInList(
                    controlRect,
                    property,
                    _options,
                    displayLabels,
                    tooltips,
                    pageSize
                );
            }
        }

        private sealed class StringInListSelector : WDropDownSelectorBase<string>
        {
            private readonly string[] _options;
            private readonly StringInListAttribute _attribute;
            private bool _isStringProperty;
            private bool _isIntegerProperty;
            private bool _isSerializableTypeProperty;

            public StringInListSelector(string[] options, StringInListAttribute attribute)
            {
                _options = options ?? Array.Empty<string>();
                _attribute = attribute;
                InitializeSearchVisibility();
            }

            protected override int OptionCount => _options.Length;

            protected override string GetDisplayLabel(int optionIndex)
            {
                string rawValue = _options[optionIndex] ?? string.Empty;
                return GetOptionLabel(_attribute, rawValue, out _);
            }

            protected override string GetTooltip(int optionIndex)
            {
                string rawValue = _options[optionIndex] ?? string.Empty;
                GetOptionLabel(_attribute, rawValue, out string tooltip);
                return tooltip;
            }

            protected override int GetCurrentSelectionIndex(SerializedProperty property)
            {
                if (property.hasMultipleDifferentValues)
                {
                    return -1;
                }

                if (_isStringProperty)
                {
                    string selectionValue = property.stringValue ?? string.Empty;
                    return Array.IndexOf(_options, selectionValue);
                }

                if (_isIntegerProperty)
                {
                    int index = property.intValue;
                    if (index < 0 || index >= _options.Length)
                    {
                        return -1;
                    }

                    return index;
                }

                if (_isSerializableTypeProperty)
                {
                    SerializedProperty assemblyQualifiedNameProperty =
                        GetSerializableTypeStringProperty(property);
                    if (assemblyQualifiedNameProperty != null)
                    {
                        string selectionValue =
                            assemblyQualifiedNameProperty.stringValue ?? string.Empty;
                        return Array.IndexOf(_options, selectionValue);
                    }
                }

                return -1;
            }

            protected override void ApplySelectionToProperty(
                SerializedProperty property,
                int optionIndex
            )
            {
                string selectedValue = _options[optionIndex] ?? string.Empty;

                if (_isStringProperty)
                {
                    property.stringValue = selectedValue;
                }
                else if (_isIntegerProperty)
                {
                    property.intValue = optionIndex;
                }
                else if (_isSerializableTypeProperty)
                {
                    SerializedProperty assemblyQualifiedNameProperty =
                        GetSerializableTypeStringProperty(property);
                    if (assemblyQualifiedNameProperty != null)
                    {
                        assemblyQualifiedNameProperty.stringValue = selectedValue;
                    }
                }
            }

            protected override string GetValueForOption(int optionIndex)
            {
                return _options[optionIndex] ?? string.Empty;
            }

            protected override string GetDefaultValue() => string.Empty;

            protected override string UndoActionName => "Change String In List";

            protected override bool MatchesSearch(int optionIndex, string searchTerm)
            {
                string option = _options[optionIndex] ?? string.Empty;
                bool matchesValue = option.StartsWith(
                    searchTerm,
                    StringComparison.OrdinalIgnoreCase
                );
                if (matchesValue)
                {
                    return true;
                }

                string optionLabel = GetOptionLabel(_attribute, option, out _);
                if (
                    !string.IsNullOrEmpty(optionLabel)
                    && optionLabel.StartsWith(searchTerm, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }

                string normalized = GetNormalizedDisplayLabel(optionIndex);
                return normalized.StartsWith(searchTerm, StringComparison.OrdinalIgnoreCase);
            }

            public override void BindProperty(SerializedProperty property, string labelText)
            {
                _isStringProperty = property.propertyType == SerializedPropertyType.String;
                _isIntegerProperty = property.propertyType == SerializedPropertyType.Integer;
                _isSerializableTypeProperty = IsSerializableTypeProperty(property);
                base.BindProperty(property, labelText);
            }
        }

        private sealed class StringInListArrayElement : VisualElement
        {
            private readonly string[] _options;
            private readonly SerializedObject _serializedObject;
            private readonly string _propertyPath;
            private readonly List<int> _indices = new();
            private readonly ListView _listView;
            private readonly ToolbarButton _removeButton;
            private readonly int _pageSize;
            private readonly StringInListAttribute _attribute;

            public StringInListArrayElement(
                SerializedProperty property,
                string[] options,
                StringInListAttribute attribute
            )
            {
                _options = options ?? Array.Empty<string>();
                _serializedObject = property.serializedObject;
                _propertyPath = property.propertyPath;
                _pageSize = Mathf.Max(1, UnityHelpersSettings.GetStringInListPageLimit());
                _attribute = attribute;

                AddToClassList("unity-base-field");
                style.flexDirection = FlexDirection.Column;

                Label header = new(property.displayName);
                header.AddToClassList("unity-base-field__label");
                header.style.unityFontStyleAndWeight = FontStyle.Bold;
                header.style.marginBottom = 2f;
                Add(header);

                Toolbar toolbar = new() { style = { marginLeft = -2f, marginBottom = 4f } };

                ToolbarButton addButton = new(AddItem) { text = "Add" };
                _removeButton = new ToolbarButton(RemoveSelected) { text = "Remove" };
                _removeButton.SetEnabled(false);

                toolbar.Add(addButton);
                toolbar.Add(_removeButton);
                Add(toolbar);

                _listView = new ListView(_indices, -1f, MakeItem, BindItem)
                {
                    unbindItem = UnbindItem,
                    name = "StringInListArray",
                    showAlternatingRowBackgrounds = AlternatingRowBackground.All,
                    selectionType = SelectionType.Single,
                    reorderable = true,
                    virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                    style = { flexGrow = 1f },
                };
                _listView.itemIndexChanged += OnItemIndexChanged;
                _listView.itemsRemoved += OnItemsRemoved;
                _listView.selectionChanged += OnSelectionChanged;
                _listView.RegisterCallback<KeyDownEvent>(OnListKeyDown);
                Add(_listView);

                RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += OnUndoRedo);
                RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= OnUndoRedo);

                Refresh();
            }

            private VisualElement MakeItem()
            {
                if (_options.Length > _pageSize)
                {
                    StringInListPopupSelectorElement popup = new(_options, _attribute)
                    {
                        style = { marginBottom = 4f },
                    };
                    return popup;
                }

                StringInListSelector selector = new(_options, _attribute)
                {
                    style = { marginBottom = 4f },
                };
                return selector;
            }

            private void BindItem(VisualElement element, int index)
            {
                SerializedProperty arrayProperty = GetArrayProperty();
                if (arrayProperty == null || index < 0 || index >= arrayProperty.arraySize)
                {
                    return;
                }

                SerializedProperty elementProperty = arrayProperty.GetArrayElementAtIndex(index);
                if (element is StringInListSelector selector)
                {
                    selector.BindProperty(elementProperty, elementProperty.displayName);
                }
                else if (element is StringInListPopupSelectorElement popup)
                {
                    popup.BindProperty(elementProperty, elementProperty.displayName);
                }
            }

            private void UnbindItem(VisualElement element, int index)
            {
                if (element is StringInListSelector selector)
                {
                    selector.UnbindProperty();
                }
                else if (element is StringInListPopupSelectorElement popup)
                {
                    popup.UnbindProperty();
                }
            }

            private void AddItem()
            {
                SerializedProperty arrayProperty = GetArrayProperty();
                if (arrayProperty == null)
                {
                    return;
                }

                Undo.RecordObjects(_serializedObject.targetObjects, "Add String Entry");
                _serializedObject.Update();

                int newIndex = arrayProperty.arraySize;
                arrayProperty.arraySize++;
                SerializedProperty newElement = arrayProperty.GetArrayElementAtIndex(newIndex);
                if (newElement.propertyType == SerializedPropertyType.String)
                {
                    newElement.stringValue = string.Empty;
                }
                else if (newElement.propertyType == SerializedPropertyType.Integer)
                {
                    newElement.intValue = 0;
                }

                _serializedObject.ApplyModifiedProperties();
                Refresh();
                _listView.selectedIndex = _indices.Count - 1;
            }

            private void RemoveSelected()
            {
                int selectedIndex = _listView.selectedIndex;
                if (selectedIndex < 0)
                {
                    return;
                }

                SerializedProperty arrayProperty = GetArrayProperty();
                if (arrayProperty == null)
                {
                    return;
                }

                Undo.RecordObjects(_serializedObject.targetObjects, "Remove String Entry");
                _serializedObject.Update();
                arrayProperty.DeleteArrayElementAtIndex(selectedIndex);
                _serializedObject.ApplyModifiedProperties();
                Refresh();

                if (_indices.Count > 0)
                {
                    _listView.selectedIndex = Mathf.Clamp(selectedIndex, 0, _indices.Count - 1);
                }
            }

            private void OnItemIndexChanged(int oldIndex, int newIndex)
            {
                if (oldIndex == newIndex)
                {
                    return;
                }

                SerializedProperty arrayProperty = GetArrayProperty();
                if (arrayProperty == null)
                {
                    return;
                }

                Undo.RecordObjects(_serializedObject.targetObjects, "Reorder String Entries");
                _serializedObject.Update();
                arrayProperty.MoveArrayElement(oldIndex, newIndex);
                _serializedObject.ApplyModifiedProperties();
                Refresh();
                _listView.selectedIndex = newIndex;
            }

            private void OnItemsRemoved(IEnumerable<int> removedIndices)
            {
                Refresh();
            }

            private void OnSelectionChanged(IEnumerable<object> _)
            {
                _removeButton.SetEnabled(_listView.selectedIndex >= 0 && _indices.Count > 0);
            }

            private void OnListKeyDown(KeyDownEvent evt)
            {
                if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
                {
                    RemoveSelected();
                    evt.StopPropagation();
                }
            }

            private void OnUndoRedo()
            {
                Refresh();
            }

            private SerializedProperty GetArrayProperty()
            {
                return _serializedObject.FindProperty(_propertyPath);
            }

            private void Refresh()
            {
                _serializedObject.Update();
                SerializedProperty arrayProperty = GetArrayProperty();

                _indices.Clear();
                if (arrayProperty != null)
                {
                    int size = arrayProperty.arraySize;
                    for (int i = 0; i < size; i++)
                    {
                        _indices.Add(i);
                    }
                }

                _listView.RefreshItems();
                _removeButton.SetEnabled(_listView.selectedIndex >= 0 && _indices.Count > 0);
            }
        }

        private static bool IsSerializableTypeProvider(StringInListAttribute attribute)
        {
            return attribute?.ProviderType == typeof(SerializableTypeCatalog)
                && string.Equals(
                    attribute.ProviderMethodName,
                    nameof(SerializableTypeCatalog.GetAssemblyQualifiedNames),
                    StringComparison.Ordinal
                );
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
                // GUILayout collapses part of the vertical margin into the enclosing layout scopes.
                // Subtract the standard spacing so the cached row height matches the measured repaint height.
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
                return StringInListDrawer.CalculatePopupTargetHeight(rowsOnPage, includePagination);
            }

            public static float CalculatePopupChromeHeight(bool includePagination)
            {
                return StringInListDrawer.CalculatePopupChromeHeight(includePagination);
            }

            public static float GetOptionRowHeight()
            {
                return StringInListDrawer.GetOptionRowHeight();
            }

            public static float GetOptionControlHeight()
            {
                return StringInListDrawer.GetOptionControlHeight();
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
                return StringInListDrawer.CalculateEmptySearchHeight();
            }

            public static float CalculateEmptySearchHeightWithMeasurement(float measuredHelpHeight)
            {
                return StringInListDrawer.CalculateEmptySearchHeight(measuredHelpHeight);
            }

            public static int CalculateRowsOnPage(int filteredCount, int pageSize, int currentPage)
            {
                return StringInListDrawer.CalculateRowsOnPage(filteredCount, pageSize, currentPage);
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

        private static string[] GetOptionDisplayArray(
            StringInListAttribute attribute,
            string[] options
        )
        {
            if (!IsSerializableTypeProvider(attribute))
            {
                return options;
            }

            string[] names = SerializableTypeCatalog.GetDisplayNames();
            if (names == null || names.Length != options.Length)
            {
                return options;
            }

            return names;
        }

        private static string GetOptionLabel(
            StringInListAttribute attribute,
            string optionValue,
            out string tooltip
        )
        {
            if (
                IsSerializableTypeProvider(attribute)
                && SerializableTypeCatalog.TryGetDisplayInfo(
                    optionValue,
                    out string displayName,
                    out string displayTooltip
                )
            )
            {
                tooltip = displayTooltip;
                return displayName;
            }

            tooltip = string.Empty;
            return optionValue ?? string.Empty;
        }

        private static string GetTypeMismatchMessage(SerializedProperty property)
        {
            string fieldName = property.displayName;
            string actualType = GetPropertyTypeName(property);
            return $"[StringInList] Type mismatch: '{fieldName}' is {actualType}, but StringInList requires string, int, string[], int[], or SerializableType. Change the field type.";
        }

        private static string GetPropertyTypeName(SerializedProperty property)
        {
            return property.propertyType switch
            {
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
                SerializedPropertyType.Generic when property.isArray =>
                    $"an array of {property.arrayElementType}",
                _ => $"type '{property.propertyType}'",
            };
        }
    }
#endif
}
