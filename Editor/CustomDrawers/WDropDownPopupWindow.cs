// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Editor.Styles;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Data model for configuring the dropdown popup window.
    /// </summary>
    public sealed class WDropDownPopupData
    {
        /// <summary>
        /// Display labels shown to the user.
        /// </summary>
        public string[] DisplayLabels { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Optional tooltips for each option.
        /// </summary>
        public string[] Tooltips { get; set; }

        /// <summary>
        /// The currently selected index, or -1 if none.
        /// </summary>
        public int SelectedIndex { get; set; } = -1;

        /// <summary>
        /// Maximum items per page (from settings).
        /// </summary>
        public int PageSize { get; set; } = 10;

        /// <summary>
        /// Callback invoked when a selection is made. Receives the selected index.
        /// </summary>
        public Action<int> OnSelectionChanged { get; set; }

        /// <summary>
        /// Optional title for the popup window.
        /// </summary>
        public string Title { get; set; } = string.Empty;
    }

    /// <summary>
    /// A UI Toolkit-based dropdown popup window that supports search, pagination, and keyboard navigation.
    /// Uses <see cref="EditorWindow.ShowAsDropDown"/> for positioning relative to the triggering button.
    /// </summary>
    public sealed class WDropDownPopupWindow : EditorWindow
    {
        private const float PopupWidth = 360f;
        private const float SearchRowHeight = 26f;
        private const float PaginationRowHeight = 26f;
        private const float OptionRowHeight = 24f;
        private const float SuggestionRowHeight = 18f;
        private const float NoResultsHeight = 40f;
        private const float VerticalPadding = 8f;
        private const float ButtonWidth = 28f;
        private const float PageLabelWidth = 90f;
        private const string NoResultsMessage = "No results match the current search.";
        private const string ClearButtonActiveClass = "w-dropdown-clear-button--active";

        private static readonly Dictionary<string, string> TabCompleteTextCache = new(
            StringComparer.Ordinal
        );

        /// <summary>
        /// Gets a cached pagination label. Delegates to <see cref="EditorCacheHelper.GetPaginationLabel"/>.
        /// </summary>
        private static string GetPaginationLabel(int page, int totalPages)
        {
            return EditorCacheHelper.GetPaginationLabel(page, totalPages);
        }

        private WDropDownPopupData _data;
        private VisualElement _root;
        private TextField _searchField;
        private Button _clearButton;
        private VisualElement _paginationContainer;
        private Button _previousButton;
        private Label _pageLabel;
        private Button _nextButton;
        private ScrollView _optionsContainer;
        private Label _noResultsLabel;
        private Label _suggestionLabel;

        private List<int> _filteredIndices;
        private PooledResource<List<int>> _filteredIndicesLease;
        private string _searchText = string.Empty;
        private string _suggestion = string.Empty;
        private int _suggestionIndex = -1;
        private int _pageIndex;
        private int _focusedOptionIndex = -1;
        private bool _closing;

        /// <summary>
        /// Shows the dropdown popup window positioned relative to the given button rect.
        /// </summary>
        /// <param name="buttonRect">The screen-space rect of the triggering button.</param>
        /// <param name="data">Configuration data for the popup.</param>
        public static void Show(Rect buttonRect, WDropDownPopupData data)
        {
            if (data == null || data.DisplayLabels == null || data.DisplayLabels.Length == 0)
            {
                return;
            }

            WDropDownPopupWindow window = CreateInstance<WDropDownPopupWindow>();
            window._data = data;
            window.titleContent = new GUIContent(
                string.IsNullOrEmpty(data.Title) ? "Select" : data.Title
            );

            Vector2 windowSize = window.CalculateInitialWindowSize(
                data.DisplayLabels.Length,
                data.PageSize
            );

            window.ShowAsDropDown(buttonRect, windowSize);
        }

        /// <summary>
        /// Shows the dropdown popup for a StringInList property.
        /// </summary>
        /// <param name="buttonRect">The GUI rect of the button that triggered the popup (in GUI space).</param>
        /// <param name="property">The serialized property being edited.</param>
        /// <param name="options">Available options.</param>
        /// <param name="displayLabels">Display labels for options (can be same as options).</param>
        /// <param name="tooltips">Optional tooltips for each option.</param>
        /// <param name="pageSize">Maximum items per page.</param>
        public static void ShowForStringInList(
            Rect buttonRect,
            SerializedProperty property,
            string[] options,
            string[] displayLabels,
            string[] tooltips,
            int pageSize
        )
        {
            if (property == null || options == null || options.Length == 0)
            {
                return;
            }

            int currentIndex = ResolveCurrentIndex(property, options);
            SerializedObject serializedObject = property.serializedObject;
            string propertyPath = property.propertyPath;
            bool isStringProperty = property.propertyType == SerializedPropertyType.String;
            bool isIntegerProperty = property.propertyType == SerializedPropertyType.Integer;

            WDropDownPopupData data = new()
            {
                DisplayLabels = displayLabels ?? options,
                Tooltips = tooltips,
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
                        "Change StringInList Selection"
                    );

                    if (isStringProperty)
                    {
                        prop.stringValue = options[selectedIndex] ?? string.Empty;
                    }
                    else if (isIntegerProperty)
                    {
                        prop.intValue = selectedIndex;
                    }

                    serializedObject.ApplyModifiedProperties();
                },
            };

            Rect screenRect = GUIUtility.GUIToScreenRect(buttonRect);
            Show(screenRect, data);
        }

        /// <summary>
        /// Shows the dropdown popup for a WValueDropDown property.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="buttonRect">The GUI rect of the button that triggered the popup (in GUI space).</param>
        /// <param name="property">The serialized property being edited.</param>
        /// <param name="values">Available values.</param>
        /// <param name="displayLabels">Display labels for values.</param>
        /// <param name="tooltips">Optional tooltips for each value.</param>
        /// <param name="pageSize">Maximum items per page.</param>
        /// <param name="applyValue">Function to apply the selected value to the property.</param>
        /// <param name="currentValueIndex">The currently selected value index.</param>
        public static void ShowForValueDropDown<T>(
            Rect buttonRect,
            SerializedProperty property,
            T[] values,
            string[] displayLabels,
            string[] tooltips,
            int pageSize,
            Action<SerializedProperty, T> applyValue,
            int currentValueIndex
        )
        {
            if (property == null || values == null || values.Length == 0 || applyValue == null)
            {
                return;
            }

            SerializedObject serializedObject = property.serializedObject;
            string propertyPath = property.propertyPath;

            WDropDownPopupData data = new()
            {
                DisplayLabels =
                    displayLabels ?? Array.ConvertAll(values, v => v?.ToString() ?? string.Empty),
                Tooltips = tooltips,
                SelectedIndex = currentValueIndex,
                PageSize = pageSize,
                OnSelectionChanged = (selectedIndex) =>
                {
                    if (selectedIndex < 0 || selectedIndex >= values.Length)
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
                    applyValue(prop, values[selectedIndex]);
                    serializedObject.ApplyModifiedProperties();
                },
            };

            Rect screenRect = GUIUtility.GUIToScreenRect(buttonRect);
            Show(screenRect, data);
        }

        /// <summary>
        /// Shows the dropdown popup for an IntDropDown property.
        /// </summary>
        /// <param name="buttonRect">The GUI rect of the button that triggered the popup (in GUI space).</param>
        /// <param name="property">The serialized property being edited.</param>
        /// <param name="options">Available integer options.</param>
        /// <param name="displayLabels">Display labels for options.</param>
        /// <param name="pageSize">Maximum items per page.</param>
        public static void ShowForIntDropDown(
            Rect buttonRect,
            SerializedProperty property,
            int[] options,
            string[] displayLabels,
            int pageSize
        )
        {
            if (property == null || options == null || options.Length == 0)
            {
                return;
            }

            int currentValue = property.intValue;
            int currentIndex = Array.IndexOf(options, currentValue);
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
                        "Change IntDropDown Selection"
                    );

                    prop.intValue = options[selectedIndex];
                    serializedObject.ApplyModifiedProperties();
                },
            };

            Rect screenRect = GUIUtility.GUIToScreenRect(buttonRect);
            Show(screenRect, data);
        }

        private static int ResolveCurrentIndex(SerializedProperty property, string[] options)
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

            return -1;
        }

        private Vector2 CalculateInitialWindowSize(int totalOptions, int pageSize)
        {
            int visibleCount = Mathf.Min(totalOptions, pageSize);
            bool hasPagination = totalOptions > pageSize;

            float height = VerticalPadding * 2;
            height += SearchRowHeight + 4f;
            if (hasPagination)
            {
                height += PaginationRowHeight + 4f;
            }

            height += visibleCount * OptionRowHeight;
            height += 8f;

            return new Vector2(PopupWidth, Mathf.Max(height, 100f));
        }

        private Vector2 CalculateWindowSize()
        {
            if (_data == null)
            {
                return new Vector2(PopupWidth, 100f);
            }

            int pageSize = _data.PageSize;
            int filteredCount = _filteredIndices?.Count ?? CalculateFilteredCount();
            bool hasPagination = filteredCount > pageSize;
            int rowsOnPage = CalculateRowsOnPage(filteredCount, pageSize, _pageIndex);

            float height = VerticalPadding * 2;
            height += SearchRowHeight + 4f;
            if (hasPagination)
            {
                height += PaginationRowHeight + 4f;
            }

            if (filteredCount == 0)
            {
                height += NoResultsHeight;
            }
            else
            {
                height += rowsOnPage * OptionRowHeight;
            }

            if (!string.IsNullOrEmpty(_suggestion))
            {
                height += SuggestionRowHeight;
            }

            height += 8f;

            return new Vector2(PopupWidth, Mathf.Max(height, 100f));
        }

        private void OnEnable()
        {
            _filteredIndicesLease = Buffers<int>.List.Get(out _filteredIndices);
        }

        private void OnDisable()
        {
            _filteredIndicesLease.Dispose();
            _filteredIndices = null;
        }

        private void OnLostFocus()
        {
            if (!_closing)
            {
                _closing = true;
                Close();
            }
        }

        private void CreateGUI()
        {
            _root = rootVisualElement;
            WDropDownStyleLoader.ApplyStyles(_root);

            _root.AddToClassList(WDropDownStyleLoader.ClassNames.Popup);

            _root.style.paddingTop = VerticalPadding;
            _root.style.paddingBottom = VerticalPadding;
            _root.style.paddingLeft = 8f;
            _root.style.paddingRight = 8f;

            BuildSearchRow();
            BuildSuggestionLabel();
            BuildPaginationRow();
            BuildOptionsContainer();
            BuildNoResultsLabel();

            _root.RegisterCallback<KeyDownEvent>(OnKeyDown);

            RefreshDisplay();

            EditorApplication.delayCall += () => _searchField?.Focus();
        }

        private void BuildSearchRow()
        {
            VisualElement searchRow = new()
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 4f,
                    height = SearchRowHeight,
                    overflow = Overflow.Hidden,
                    flexShrink = 0f,
                },
            };
            searchRow.AddToClassList(WDropDownStyleLoader.ClassNames.SearchContainer);

            Label searchLabel = new("Search") { style = { width = 50f, marginRight = 4f } };

            _searchField = new TextField
            {
                style =
                {
                    flexGrow = 1f,
                    flexShrink = 1f,
                    marginLeft = 0f,
                    marginRight = 0f,
                    overflow = Overflow.Hidden,
                },
            };
            _searchField.AddToClassList(WDropDownStyleLoader.ClassNames.Search);
            _searchField.RegisterValueChangedCallback(OnSearchChanged);

            _clearButton = new Button(OnClearClicked)
            {
                text = "Clear",
                style = { marginLeft = 4f, width = 50f },
            };
            _clearButton.AddToClassList(WDropDownStyleLoader.ClassNames.ClearButton);
            _clearButton.SetEnabled(false);

            searchRow.Add(searchLabel);
            searchRow.Add(_searchField);
            searchRow.Add(_clearButton);
            _root.Add(searchRow);
        }

        private void BuildSuggestionLabel()
        {
            _suggestionLabel = new Label
            {
                style =
                {
                    display = DisplayStyle.None,
                    marginLeft = 54f,
                    marginBottom = 2f,
                    color = new Color(0.7f, 0.85f, 1f, 0.75f),
                    unityFontStyleAndWeight = FontStyle.Italic,
                    fontSize = 11f,
                    height = SuggestionRowHeight,
                },
                pickingMode = PickingMode.Ignore,
            };
            _suggestionLabel.AddToClassList(WDropDownStyleLoader.ClassNames.Suggestion);
            _root.Add(_suggestionLabel);
        }

        private void BuildPaginationRow()
        {
            _paginationContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    marginBottom = 4f,
                    height = PaginationRowHeight,
                    display = DisplayStyle.None,
                },
            };
            _paginationContainer.AddToClassList(WDropDownStyleLoader.ClassNames.Pagination);

            _previousButton = new Button(OnPreviousPage)
            {
                text = "‹",
                style = { width = ButtonWidth, height = 20f },
            };
            _previousButton.AddToClassList(WDropDownStyleLoader.ClassNames.PaginationButton);

            _pageLabel = new Label
            {
                style = { width = PageLabelWidth, unityTextAlign = TextAnchor.MiddleCenter },
            };
            _pageLabel.AddToClassList(WDropDownStyleLoader.ClassNames.PaginationLabel);

            _nextButton = new Button(OnNextPage)
            {
                text = "›",
                style = { width = ButtonWidth, height = 20f },
            };
            _nextButton.AddToClassList(WDropDownStyleLoader.ClassNames.PaginationButton);

            _paginationContainer.Add(_previousButton);
            _paginationContainer.Add(_pageLabel);
            _paginationContainer.Add(_nextButton);
            _root.Add(_paginationContainer);
        }

        private void BuildOptionsContainer()
        {
            _optionsContainer = new ScrollView(ScrollViewMode.Vertical)
            {
                style = { flexGrow = 1f, marginBottom = 4f },
            };
            _optionsContainer.AddToClassList(WDropDownStyleLoader.ClassNames.OptionsContainer);
            _root.Add(_optionsContainer);
        }

        private void BuildNoResultsLabel()
        {
            _noResultsLabel = new Label(NoResultsMessage)
            {
                style =
                {
                    display = DisplayStyle.None,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    paddingTop = 8f,
                    paddingBottom = 8f,
                    color = new Color(0.7f, 0.7f, 0.7f, 1f),
                },
            };
            _noResultsLabel.AddToClassList(WDropDownStyleLoader.ClassNames.NoResults);
            _root.Add(_noResultsLabel);
        }

        private void OnSearchChanged(ChangeEvent<string> evt)
        {
            _searchText = evt.newValue ?? string.Empty;
            _pageIndex = 0;
            _focusedOptionIndex = -1;
            RefreshDisplay();
            ResizeWindow();
        }

        private void OnClearClicked()
        {
            _searchText = string.Empty;
            _searchField.SetValueWithoutNotify(string.Empty);
            _pageIndex = 0;
            _focusedOptionIndex = -1;
            RefreshDisplay();
            ResizeWindow();
            _searchField.Focus();
        }

        private void OnPreviousPage()
        {
            if (_pageIndex > 0)
            {
                _pageIndex--;
                _focusedOptionIndex = -1;
                RefreshDisplay();
            }
        }

        private void OnNextPage()
        {
            int pageCount = CalculatePageCount();
            if (_pageIndex < pageCount - 1)
            {
                _pageIndex++;
                _focusedOptionIndex = -1;
                RefreshDisplay();
            }
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    evt.StopPropagation();
                    Close();
                    break;

                case KeyCode.DownArrow:
                    evt.StopPropagation();
                    MoveFocus(1);
                    break;

                case KeyCode.UpArrow:
                    evt.StopPropagation();
                    MoveFocus(-1);
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    evt.StopPropagation();
                    if (_focusedOptionIndex >= 0)
                    {
                        SelectFocusedOption();
                    }
                    else if (_suggestionIndex >= 0)
                    {
                        AcceptSuggestion();
                    }
                    break;

                case KeyCode.Tab:
                    if (!string.IsNullOrEmpty(_suggestion) && _suggestionIndex >= 0)
                    {
                        evt.StopPropagation();
                        AcceptSuggestion();
                    }
                    break;

                case KeyCode.PageDown:
                    evt.StopPropagation();
                    OnNextPage();
                    break;

                case KeyCode.PageUp:
                    evt.StopPropagation();
                    OnPreviousPage();
                    break;
            }
        }

        private void MoveFocus(int delta)
        {
            int optionCount = _optionsContainer.childCount;
            if (optionCount == 0)
            {
                return;
            }

            _focusedOptionIndex += delta;
            if (_focusedOptionIndex < 0)
            {
                _focusedOptionIndex = optionCount - 1;
            }
            else if (_focusedOptionIndex >= optionCount)
            {
                _focusedOptionIndex = 0;
            }

            UpdateOptionFocus();
        }

        private void UpdateOptionFocus()
        {
            for (int i = 0; i < _optionsContainer.childCount; i++)
            {
                VisualElement child = _optionsContainer[i];
                if (i == _focusedOptionIndex)
                {
                    child.AddToClassList(WDropDownStyleLoader.ClassNames.OptionFocused);
                }
                else
                {
                    child.RemoveFromClassList(WDropDownStyleLoader.ClassNames.OptionFocused);
                }
            }
        }

        private void SelectFocusedOption()
        {
            if (_focusedOptionIndex < 0 || _focusedOptionIndex >= _optionsContainer.childCount)
            {
                return;
            }

            VisualElement optionElement = _optionsContainer[_focusedOptionIndex];
            if (optionElement.userData is int optionIndex)
            {
                SelectOption(optionIndex);
            }
        }

        private void AcceptSuggestion()
        {
            if (_suggestionIndex < 0)
            {
                return;
            }

            SelectOption(_suggestionIndex);
        }

        private void SelectOption(int optionIndex)
        {
            _data?.OnSelectionChanged?.Invoke(optionIndex);
            Close();
        }

        private void RefreshDisplay()
        {
            if (_data == null)
            {
                return;
            }

            UpdateFilteredIndices();
            UpdateClearButton();
            UpdateSuggestion();
            UpdatePagination();
            UpdateOptions();
            UpdateNoResults();
        }

        private string GetNormalizedLabel(int index)
        {
            string label = _data.DisplayLabels[index];
            if (string.IsNullOrEmpty(label))
            {
                return DropDownShared.GetFallbackOptionLabel(index);
            }
            return label;
        }

        private void UpdateFilteredIndices()
        {
            _filteredIndices.Clear();

            if (string.IsNullOrEmpty(_searchText))
            {
                for (int i = 0; i < _data.DisplayLabels.Length; i++)
                {
                    _filteredIndices.Add(i);
                }
            }
            else
            {
                for (int i = 0; i < _data.DisplayLabels.Length; i++)
                {
                    string label = GetNormalizedLabel(i);
                    if (label.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _filteredIndices.Add(i);
                    }
                }
            }
        }

        private void UpdateClearButton()
        {
            bool hasSearch = !string.IsNullOrEmpty(_searchText);
            _clearButton.SetEnabled(hasSearch);
            if (hasSearch)
            {
                _clearButton.AddToClassList(ClearButtonActiveClass);
            }
            else
            {
                _clearButton.RemoveFromClassList(ClearButtonActiveClass);
            }
        }

        private void UpdateSuggestion()
        {
            _suggestion = string.Empty;
            _suggestionIndex = -1;

            if (string.IsNullOrEmpty(_searchText) || _filteredIndices.Count == 0)
            {
                _suggestionLabel.style.display = DisplayStyle.None;
                return;
            }

            for (int i = 0; i < _filteredIndices.Count; i++)
            {
                int index = _filteredIndices[i];
                string label = GetNormalizedLabel(index);
                if (label.StartsWith(_searchText, StringComparison.OrdinalIgnoreCase))
                {
                    _suggestion = label;
                    _suggestionIndex = index;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(_suggestion))
            {
                if (!TabCompleteTextCache.TryGetValue(_suggestion, out string tabCompleteText))
                {
                    tabCompleteText = "Tab to complete: " + _suggestion;
                    TabCompleteTextCache[_suggestion] = tabCompleteText;
                }
                _suggestionLabel.text = tabCompleteText;
                _suggestionLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                _suggestionLabel.style.display = DisplayStyle.None;
            }
        }

        private void UpdatePagination()
        {
            int filteredCount = _filteredIndices.Count;
            int pageSize = _data.PageSize;
            int pageCount = CalculatePageCount();

            _pageIndex = Mathf.Clamp(_pageIndex, 0, Mathf.Max(0, pageCount - 1));

            bool showPagination = pageCount > 1;
            _paginationContainer.style.display = showPagination
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            if (showPagination)
            {
                _previousButton.SetEnabled(_pageIndex > 0);
                _nextButton.SetEnabled(_pageIndex < pageCount - 1);
                _pageLabel.text = GetPaginationLabel(_pageIndex + 1, pageCount);
            }
        }

        private void UpdateOptions()
        {
            _optionsContainer.Clear();
            _focusedOptionIndex = -1;

            if (_filteredIndices.Count == 0)
            {
                _optionsContainer.style.display = DisplayStyle.None;
                return;
            }

            _optionsContainer.style.display = DisplayStyle.Flex;

            int pageSize = _data.PageSize;
            int startIndex = _pageIndex * pageSize;
            int endIndex = Mathf.Min(startIndex + pageSize, _filteredIndices.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                int optionIndex = _filteredIndices[i];
                VisualElement optionRow = CreateOptionRow(optionIndex);
                _optionsContainer.Add(optionRow);
            }
        }

        private VisualElement CreateOptionRow(int optionIndex)
        {
            string label = GetNormalizedLabel(optionIndex);
            string tooltip =
                _data.Tooltips != null && optionIndex < _data.Tooltips.Length
                    ? _data.Tooltips[optionIndex] ?? string.Empty
                    : string.Empty;

            bool isSelected = optionIndex == _data.SelectedIndex;

            Button optionButton = new()
            {
                text = label,
                tooltip = tooltip,
                userData = optionIndex,
                style =
                {
                    height = OptionRowHeight,
                    marginBottom = 2f,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    paddingLeft = 8f,
                    paddingRight = 8f,
                },
            };

            optionButton.AddToClassList(WDropDownStyleLoader.ClassNames.Option);

            if (isSelected)
            {
                optionButton.AddToClassList(WDropDownStyleLoader.ClassNames.OptionSelected);
            }

            optionButton.RegisterCallback<ClickEvent>(OnOptionButtonClick);
            optionButton.RegisterCallback<MouseEnterEvent, Button>(
                OnOptionMouseEnter,
                optionButton
            );
            optionButton.RegisterCallback<MouseLeaveEvent, Button>(
                OnOptionMouseLeave,
                optionButton
            );

            return optionButton;
        }

        private void OnOptionButtonClick(ClickEvent evt)
        {
            if (evt.target is Button button && button.userData is int optionIndex)
            {
                SelectOption(optionIndex);
            }
        }

        private static void OnOptionMouseEnter(MouseEnterEvent evt, Button button)
        {
            button.AddToClassList(WDropDownStyleLoader.ClassNames.OptionHover);
        }

        private static void OnOptionMouseLeave(MouseLeaveEvent evt, Button button)
        {
            button.RemoveFromClassList(WDropDownStyleLoader.ClassNames.OptionHover);
        }

        private void UpdateNoResults()
        {
            bool showNoResults = _filteredIndices.Count == 0 && !string.IsNullOrEmpty(_searchText);
            _noResultsLabel.style.display = showNoResults ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ResizeWindow()
        {
            if (_data == null)
            {
                return;
            }

            Vector2 newSize = CalculateWindowSize();
            Rect pos = position;
            pos.size = newSize;
            position = pos;
        }

        private int CalculateFilteredCount()
        {
            if (_data == null || _data.DisplayLabels == null)
            {
                return 0;
            }

            if (string.IsNullOrEmpty(_searchText))
            {
                return _data.DisplayLabels.Length;
            }

            int count = 0;
            for (int i = 0; i < _data.DisplayLabels.Length; i++)
            {
                string label = GetNormalizedLabel(i);
                if (label.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    count++;
                }
            }
            return count;
        }

        private int CalculatePageCount()
        {
            if (_data == null)
            {
                return 1;
            }

            int filteredCount = _filteredIndices?.Count ?? CalculateFilteredCount();
            int pageSize = Mathf.Max(1, _data.PageSize);

            if (filteredCount <= 0)
            {
                return 1;
            }

            return (filteredCount + pageSize - 1) / pageSize;
        }

        private int CalculateRowsOnPage(int filteredCount, int pageSize, int currentPage)
        {
            if (filteredCount <= 0 || pageSize <= 0)
            {
                return 0;
            }

            int pageCount = (filteredCount + pageSize - 1) / pageSize;
            int clampedPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, pageCount - 1));
            int startIndex = clampedPage * pageSize;
            int remaining = filteredCount - startIndex;

            return Mathf.Clamp(remaining, 0, pageSize);
        }
    }
#endif
}
