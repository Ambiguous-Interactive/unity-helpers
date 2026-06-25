// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers.Base
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Styles;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Base class for UI Toolkit inline dropdown selectors with search, pagination, and autocomplete.
    /// Subclasses must implement type-specific methods for display, selection, and matching logic.
    /// </summary>
    /// <typeparam name="TValue">The type of the field value (string, int, etc.).</typeparam>
    public abstract class WDropDownSelectorBase<TValue> : BaseField<TValue>
    {
        private const float ButtonWidth = 24f;
        private const float PaginationButtonHeight = 20f;
        private const float DropDownBottomPadding = 6f;
        private const float NoResultsVerticalPadding = 6f;
        private const float NoResultsHorizontalPadding = 6f;

        private readonly VisualElement _searchRow;
        private readonly TextField _searchField;
        private readonly Button _clearButton;
        private readonly VisualElement _paginationContainer;
        private readonly Button _previousButton;
        private readonly Label _pageLabel;
        private readonly Button _nextButton;
        private readonly DropdownField _dropdown;
        private readonly Label _noResultsLabel;
        private readonly Label _suggestionHintLabel;

        private List<int> _filteredIndices;
        private PooledResource<List<int>> _filteredIndicesLease;
        private List<int> _pageOptionIndices;
        private PooledResource<List<int>> _pageOptionIndicesLease;
        private List<string> _pageChoices;
        private PooledResource<List<string>> _pageChoicesLease;

        private SerializedObject _boundObject;
        private string _propertyPath = string.Empty;
        private string _searchText = string.Empty;
        private string _suggestion = string.Empty;
        private int _pageIndex;
        private int _lastResolvedPageSize;
        private bool _searchVisible;
        private int _suggestionOptionIndex;
        private int _currentFilteredCount;
        private bool _buffersInitialized;

        /// <summary>
        /// Gets the total number of options available.
        /// </summary>
        protected abstract int OptionCount { get; }

        /// <summary>
        /// Gets the display label for the option at the specified index.
        /// </summary>
        /// <param name="optionIndex">The index of the option.</param>
        /// <returns>The display label string.</returns>
        protected abstract string GetDisplayLabel(int optionIndex);

        /// <summary>
        /// Gets the normalized display label for the option at the specified index.
        /// Applies a fallback of "(Option N)" when the raw label is null or empty,
        /// ensuring consistent behavior across rendering, search, and suggestion logic.
        /// </summary>
        /// <param name="optionIndex">The index of the option.</param>
        /// <returns>The normalized display label, never null or empty.</returns>
        protected string GetNormalizedDisplayLabel(int optionIndex)
        {
            string label = GetDisplayLabel(optionIndex);
            if (string.IsNullOrEmpty(label))
            {
                return DropDownShared.GetFallbackOptionLabel(optionIndex);
            }
            return label;
        }

        /// <summary>
        /// Gets the tooltip for the option at the specified index.
        /// Return null or empty string if no tooltip is needed.
        /// </summary>
        /// <param name="optionIndex">The index of the option.</param>
        /// <returns>The tooltip string, or null/empty for no tooltip.</returns>
        protected virtual string GetTooltip(int optionIndex) => string.Empty;

        /// <summary>
        /// Gets the index of the currently selected option from the property.
        /// Returns -1 if no valid selection.
        /// </summary>
        /// <param name="property">The serialized property.</param>
        /// <returns>The selected option index, or -1 if none.</returns>
        protected abstract int GetCurrentSelectionIndex(SerializedProperty property);

        /// <summary>
        /// Applies the selection at the specified option index to the property.
        /// </summary>
        /// <param name="property">The serialized property.</param>
        /// <param name="optionIndex">The index of the option to apply.</param>
        protected abstract void ApplySelectionToProperty(
            SerializedProperty property,
            int optionIndex
        );

        /// <summary>
        /// Gets the value to set via SetValueWithoutNotify after selection.
        /// </summary>
        /// <param name="optionIndex">The index of the selected option.</param>
        /// <returns>The value to set on the field.</returns>
        protected abstract TValue GetValueForOption(int optionIndex);

        /// <summary>
        /// Gets the default value when no selection is available.
        /// </summary>
        /// <returns>The default value.</returns>
        protected abstract TValue GetDefaultValue();

        /// <summary>
        /// Checks if the option at the specified index matches the search term.
        /// Default implementation performs case-insensitive prefix match on the display label.
        /// </summary>
        /// <param name="optionIndex">The index of the option.</param>
        /// <param name="searchTerm">The search term to match.</param>
        /// <returns>True if the option matches the search.</returns>
        protected virtual bool MatchesSearch(int optionIndex, string searchTerm)
        {
            string label = GetNormalizedDisplayLabel(optionIndex);
            return label.StartsWith(searchTerm, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the undo action name for selection changes.
        /// </summary>
        protected virtual string UndoActionName => "Change DropDown Selection";

        private static VisualElement CreateInputElement(out VisualElement element)
        {
            element = new VisualElement();
            return element;
        }

        protected WDropDownSelectorBase()
            : base(string.Empty, CreateInputElement(out VisualElement baseInput))
        {
            EnsureBuffers();
            RegisterCallback<AttachToPanelEvent>(OnAttachedToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);

            _lastResolvedPageSize = Mathf.Max(1, UnityHelpersSettings.GetStringInListPageLimit());
            _suggestionOptionIndex = -1;

            WDropDownStyleLoader.ApplyStyles(this);

            AddToClassList("unity-base-field");
            AddToClassList("unity-base-field__aligned");
            labelElement.AddToClassList("unity-base-field__label");
            labelElement.AddToClassList("unity-label");

            baseInput.AddToClassList("unity-base-field__input");
            baseInput.style.flexGrow = 1f;
            baseInput.style.marginLeft = 0f;
            baseInput.style.paddingLeft = 0f;
            baseInput.style.flexDirection = FlexDirection.Column;

            _searchRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 4f,
                    marginLeft = 0f,
                    paddingLeft = 0f,
                },
            };
            _searchRow.AddToClassList(WDropDownStyleLoader.ClassNames.SearchContainer);

            VisualElement searchWrapper = new()
            {
                style = { flexGrow = 1f, position = Position.Relative },
            };
            searchWrapper.AddToClassList(WDropDownStyleLoader.ClassNames.SearchWrapper);

            _searchField = new TextField { name = "DropDownSearch", style = { flexGrow = 1f } };
            _searchField.AddToClassList(WDropDownStyleLoader.ClassNames.Search);
            _searchField.RegisterValueChangedCallback(OnSearchChanged);
            _searchField.RegisterCallback<KeyDownEvent>(OnSearchKeyDown);
            searchWrapper.Add(_searchField);

            _clearButton = new Button(OnClearClicked)
            {
                text = "Clear",
                style = { marginLeft = 4f },
            };
            _clearButton.AddToClassList(WDropDownStyleLoader.ClassNames.ClearButton);
            _clearButton.SetEnabled(false);

            _paginationContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginLeft = 4f,
                    display = DisplayStyle.None,
                },
            };
            _paginationContainer.AddToClassList(WDropDownStyleLoader.ClassNames.Pagination);

            _previousButton = new Button(OnPreviousPage)
            {
                text = "<",
                style =
                {
                    marginRight = 0f,
                    minWidth = ButtonWidth,
                    height = PaginationButtonHeight,
                    paddingLeft = 6f,
                    paddingRight = 6f,
                },
            };
            _previousButton.AddToClassList("unity-toolbar-button");
            _previousButton.AddToClassList(WDropDownStyleLoader.ClassNames.PaginationButton);

            _pageLabel = new Label
            {
                style =
                {
                    minWidth = 80f,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginRight = 0f,
                    paddingLeft = 6f,
                    paddingRight = 6f,
                    minHeight = PaginationButtonHeight,
                    alignSelf = Align.Center,
                },
            };
            _pageLabel.AddToClassList(WDropDownStyleLoader.ClassNames.PaginationLabel);

            _nextButton = new Button(OnNextPage)
            {
                text = ">",
                style =
                {
                    minWidth = ButtonWidth,
                    height = PaginationButtonHeight,
                    paddingLeft = 6f,
                    paddingRight = 6f,
                },
            };
            _nextButton.AddToClassList("unity-toolbar-button");
            _nextButton.AddToClassList(WDropDownStyleLoader.ClassNames.PaginationButton);

            _paginationContainer.Add(_previousButton);
            _paginationContainer.Add(_pageLabel);
            _paginationContainer.Add(_nextButton);

            _searchRow.Add(searchWrapper);
            _searchRow.Add(_clearButton);
            _searchRow.Add(_paginationContainer);
            baseInput.Add(_searchRow);

            _suggestionHintLabel = new Label
            {
                style =
                {
                    display = DisplayStyle.None,
                    marginLeft = 4f,
                    marginBottom = 2f,
                    color = new Color(0.7f, 0.85f, 1f, 0.75f),
                    unityFontStyleAndWeight = FontStyle.Italic,
                    fontSize = 11f,
                },
                pickingMode = PickingMode.Ignore,
            };
            _suggestionHintLabel.AddToClassList(WDropDownStyleLoader.ClassNames.Suggestion);
            baseInput.Add(_suggestionHintLabel);

            _dropdown = new DropdownField
            {
                choices = _pageChoices,
                style =
                {
                    flexGrow = 1f,
                    marginLeft = 0f,
                    paddingLeft = 0f,
                    marginBottom = DropDownBottomPadding,
                },
                label = string.Empty,
            };
            _dropdown.labelElement.style.display = DisplayStyle.None;
            _dropdown.RegisterValueChangedCallback(OnDropDownValueChanged);
            baseInput.Add(_dropdown);

            _noResultsLabel = new Label("No results match the current search.")
            {
                style =
                {
                    display = DisplayStyle.None,
                    marginLeft = 0f,
                    marginTop = 4f,
                    paddingTop = NoResultsVerticalPadding,
                    paddingBottom = NoResultsVerticalPadding,
                    paddingLeft = NoResultsHorizontalPadding,
                    paddingRight = NoResultsHorizontalPadding,
                    unityTextAlign = TextAnchor.MiddleCenter,
                },
            };
            _noResultsLabel.AddToClassList("unity-help-box");
            _noResultsLabel.AddToClassList(WDropDownStyleLoader.ClassNames.NoResults);
            baseInput.Add(_noResultsLabel);

            // Note: Search visibility is initialized by derived classes calling InitializeSearchVisibility()
            // after their options are set, since OptionCount is accessed during initialization.

            RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += OnUndoRedo);
            RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= OnUndoRedo);
        }

        /// <summary>
        /// Initializes search visibility based on option count. Must be called by derived classes
        /// after their options have been set, since OptionCount is accessed during this call.
        /// </summary>
        protected void InitializeSearchVisibility()
        {
            ApplySearchVisibility(ShouldShowSearch(_lastResolvedPageSize));
        }

        /// <summary>
        /// Binds this selector to a serialized property.
        /// </summary>
        /// <param name="property">The property to bind.</param>
        /// <param name="labelText">The label text to display.</param>
        public virtual void BindProperty(SerializedProperty property, string labelText)
        {
            _boundObject = property.serializedObject;
            _propertyPath = property.propertyPath;
            UpdateLabel(labelText, property.tooltip);
            _pageIndex = 0;
            _searchText = string.Empty;
            _suggestion = string.Empty;

            _searchField.SetValueWithoutNotify(string.Empty);
            UpdateClearButton(_searchVisible);
            UpdateSuggestionDisplay(string.Empty, -1, -1);

            UpdateFromProperty();
        }

        /// <summary>
        /// Unbinds this selector from any property.
        /// </summary>
        public void UnbindProperty()
        {
            _boundObject = null;
            _propertyPath = string.Empty;
            UpdateLabel(string.Empty, string.Empty);
        }

        private void UpdateLabel(string labelText, string labelTooltip)
        {
            bool hasLabel = !string.IsNullOrWhiteSpace(labelText);
            label = hasLabel ? labelText : string.Empty;
            labelElement.style.display = hasLabel ? DisplayStyle.Flex : DisplayStyle.None;
            labelElement.tooltip = labelTooltip;
            _dropdown.tooltip = labelTooltip;
        }

        private void OnUndoRedo()
        {
            UpdateFromProperty();
        }

        private void OnSearchChanged(ChangeEvent<string> evt)
        {
            if (!_searchVisible)
            {
                return;
            }

            _searchText = evt.newValue ?? string.Empty;
            _pageIndex = 0;
            UpdateClearButton(_searchVisible);
            UpdateFromProperty();
        }

        private void OnSearchKeyDown(KeyDownEvent evt)
        {
            if (!_searchVisible)
            {
                return;
            }

            if (
                (
                    evt.keyCode == KeyCode.Tab
                    || evt.keyCode == KeyCode.Return
                    || evt.keyCode == KeyCode.KeypadEnter
                ) && !string.IsNullOrEmpty(_suggestion)
            )
            {
                evt.StopPropagation();
                evt.StopImmediatePropagation();
                bool commitSelection = evt.keyCode == KeyCode.Tab && !evt.shiftKey;
                AcceptSuggestion(commitSelection);
            }
        }

        private void OnClearClicked()
        {
            if (!_searchVisible || string.IsNullOrEmpty(_searchText))
            {
                return;
            }

            _searchText = string.Empty;
            _searchField.SetValueWithoutNotify(string.Empty);

            _pageIndex = 0;
            UpdateClearButton(_searchVisible);
            UpdateSuggestionDisplay(string.Empty, -1, -1);
            UpdateFromProperty();
        }

        private void OnPreviousPage()
        {
            if (_pageIndex <= 0)
            {
                return;
            }

            _pageIndex--;
            UpdateFromProperty();
        }

        private void OnNextPage()
        {
            int pageSize = ResolvePageSize();
            int pageCount = CalculatePageCount(pageSize, _currentFilteredCount);
            if (_pageIndex >= pageCount - 1)
            {
                return;
            }

            _pageIndex++;
            UpdateFromProperty();
        }

        private void OnDropDownValueChanged(ChangeEvent<string> evt)
        {
            string newValue = evt.newValue;
            if (string.IsNullOrEmpty(newValue))
            {
                return;
            }

            int optionIndex = ResolveOptionIndex(newValue);
            if (optionIndex < 0)
            {
                return;
            }

            ApplySelection(optionIndex);
        }

        private void UpdateFromProperty()
        {
            EnsureBuffers();

            if (_boundObject == null || string.IsNullOrEmpty(_propertyPath))
            {
                return;
            }

            _boundObject.Update();

            SerializedProperty property = _boundObject.FindProperty(_propertyPath);
            if (property == null)
            {
                return;
            }

            int pageSize = ResolvePageSize();
            bool searchActive = ShouldShowSearch(pageSize);
            ApplySearchVisibility(searchActive);

            int selectedOptionIndex = GetCurrentSelectionIndex(property);

            _filteredIndices.Clear();
            string effectiveSearch = searchActive ? (_searchText ?? string.Empty) : string.Empty;
            bool hasSearch = searchActive && !string.IsNullOrWhiteSpace(effectiveSearch);
            if (hasSearch)
            {
                for (int i = 0; i < OptionCount; i++)
                {
                    if (MatchesSearch(i, effectiveSearch))
                    {
                        _filteredIndices.Add(i);
                    }
                }
            }

            int filteredCount = hasSearch ? _filteredIndices.Count : OptionCount;
            if (filteredCount == 0)
            {
                ToggleDropDownVisibility(false);
                _pageChoices.Clear();
                _dropdown.choices = _pageChoices;
                _dropdown.SetValueWithoutNotify(string.Empty);
                SetValueWithoutNotify(GetDefaultValue());
                _dropdown.SetEnabled(false);
                _dropdown.tooltip = string.Empty;
                _noResultsLabel.style.display = DisplayStyle.Flex;
                UpdatePagination(searchActive, 0, pageSize, 0);
                UpdateSuggestionDisplay(string.Empty, -1, -1);
                _currentFilteredCount = 0;
                return;
            }

            _noResultsLabel.style.display = DisplayStyle.None;
            ToggleDropDownVisibility(true);

            int pageCount = CalculatePageCount(pageSize, filteredCount);
            if (selectedOptionIndex >= 0)
            {
                int filteredIndex = hasSearch
                    ? _filteredIndices.IndexOf(selectedOptionIndex)
                    : selectedOptionIndex;
                if (filteredIndex >= 0)
                {
                    _pageIndex = filteredIndex / pageSize;
                }
                else if (_pageIndex >= pageCount)
                {
                    _pageIndex = 0;
                }
            }
            else if (_pageIndex >= pageCount)
            {
                _pageIndex = 0;
            }

            UpdatePagination(searchActive, pageCount, pageSize, filteredCount);

            bool paginate = searchActive && filteredCount > pageSize;

            _pageOptionIndices.Clear();
            _pageChoices.Clear();

            int startIndex = paginate ? _pageIndex * pageSize : 0;
            int endIndex = paginate
                ? Math.Min(filteredCount, startIndex + pageSize)
                : filteredCount;

            for (int i = startIndex; i < endIndex; i++)
            {
                int optionIndex = hasSearch ? _filteredIndices[i] : i;
                string displayLabel = GetNormalizedDisplayLabel(optionIndex);
                _pageOptionIndices.Add(optionIndex);
                _pageChoices.Add(displayLabel);
            }

            _dropdown.choices = _pageChoices;

            string dropdownValue = string.Empty;
            string dropdownTooltip = string.Empty;
            if (selectedOptionIndex >= 0 && selectedOptionIndex < OptionCount)
            {
                dropdownValue = GetNormalizedDisplayLabel(selectedOptionIndex);
                dropdownTooltip = GetTooltip(selectedOptionIndex);
            }

            if (string.IsNullOrEmpty(dropdownValue) && _pageChoices.Count > 0)
            {
                dropdownValue = _pageChoices[0];
                dropdownTooltip =
                    _pageOptionIndices.Count > 0 ? GetTooltip(_pageOptionIndices[0]) : string.Empty;
            }

            _dropdown.SetValueWithoutNotify(dropdownValue);
            if (selectedOptionIndex >= 0)
            {
                SetValueWithoutNotify(GetValueForOption(selectedOptionIndex));
            }
            else
            {
                SetValueWithoutNotify(GetDefaultValue());
            }
            _dropdown.SetEnabled(_pageChoices.Count > 0);
            _dropdown.tooltip = dropdownTooltip ?? string.Empty;

            _currentFilteredCount = filteredCount;

            UpdateSuggestion(hasSearch);
        }

        private void EnsureBuffers()
        {
            if (_buffersInitialized)
            {
                return;
            }

            _filteredIndicesLease = Buffers<int>.List.Get(out _filteredIndices);
            _pageOptionIndicesLease = Buffers<int>.List.Get(out _pageOptionIndices);
            _pageChoicesLease = Buffers<string>.List.Get(out _pageChoices);
            _buffersInitialized = true;
        }

        private void OnAttachedToPanel(AttachToPanelEvent _)
        {
            if (!_buffersInitialized)
            {
                EnsureBuffers();
            }
        }

        private void OnDetachedFromPanel(DetachFromPanelEvent _)
        {
            ReleaseBuffers();
        }

        private void ReleaseBuffers()
        {
            if (!_buffersInitialized)
            {
                return;
            }

            _filteredIndicesLease.Dispose();
            _pageOptionIndicesLease.Dispose();
            _pageChoicesLease.Dispose();

            _filteredIndices = null;
            _pageOptionIndices = null;
            _pageChoices = null;
            _buffersInitialized = false;
        }

        private void UpdatePagination(
            bool searchActive,
            int pageCount,
            int pageSize,
            int filteredCount
        )
        {
            if (
                _paginationContainer == null
                || _previousButton == null
                || _nextButton == null
                || _pageLabel == null
            )
            {
                return;
            }

            bool showPagination = searchActive && filteredCount > pageSize;
            _paginationContainer.style.display = showPagination
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            if (!showPagination)
            {
                _pageLabel.text = string.Empty;
                _previousButton.SetEnabled(false);
                _nextButton.SetEnabled(false);
                return;
            }

            int clampedPageCount = Math.Max(1, pageCount);
            _pageIndex = Mathf.Clamp(_pageIndex, 0, clampedPageCount - 1);
            _pageLabel.text = GetPaginationLabel(_pageIndex + 1, clampedPageCount);
            _previousButton.SetEnabled(_pageIndex > 0);
            _nextButton.SetEnabled(_pageIndex < clampedPageCount - 1);
        }

        private void UpdateClearButton(bool searchActive)
        {
            if (_clearButton == null)
            {
                return;
            }

            _clearButton.SetEnabled(searchActive && !string.IsNullOrEmpty(_searchText));
        }

        private void UpdateSuggestion(bool hasSearch)
        {
            if (!hasSearch || _filteredIndices.Count == 0)
            {
                UpdateSuggestionDisplay(string.Empty, -1, -1);
                return;
            }

            bool searchVisible = hasSearch && !string.IsNullOrEmpty(_searchText);
            int optionIndex = _filteredIndices[0];
            string optionLabel = GetNormalizedDisplayLabel(optionIndex);
            bool prefixMatch =
                searchVisible
                && optionLabel.StartsWith(_searchText, StringComparison.OrdinalIgnoreCase);

            UpdateSuggestionDisplay(optionLabel, optionIndex, prefixMatch ? 0 : -1);
        }

        private void UpdateSuggestionDisplay(
            string suggestionValue,
            int optionIndex,
            int matchPosition
        )
        {
            _suggestion = suggestionValue;
            _suggestionOptionIndex = optionIndex;
            bool suggestionsVisible =
                _searchVisible
                && !string.IsNullOrEmpty(suggestionValue)
                && optionIndex >= 0
                && matchPosition == 0;

            if (_suggestionHintLabel != null)
            {
                _suggestionHintLabel.text = suggestionsVisible
                    ? $"↹ Tab selects: {suggestionValue}"
                    : string.Empty;
                _suggestionHintLabel.style.display = suggestionsVisible
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (!suggestionsVisible)
            {
                _suggestionOptionIndex = -1;
            }
        }

        private void ToggleDropDownVisibility(bool hasResults)
        {
            if (_dropdown == null)
            {
                return;
            }

            _dropdown.style.display = hasResults ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void AcceptSuggestion(bool commitSelection)
        {
            if (
                !_searchVisible
                || string.IsNullOrEmpty(_suggestion)
                || _searchField == null
                || _suggestionOptionIndex < 0
            )
            {
                return;
            }

            string previous = _searchText ?? string.Empty;
            int originalLength = previous.Length;
            int optionIndexToApply = commitSelection ? _suggestionOptionIndex : -1;

            _searchText = _suggestion;
            _searchField.SetValueWithoutNotify(_suggestion);
            int selectionStart = Mathf.Clamp(originalLength, 0, _suggestion.Length);
            _searchField.schedule.Execute(() =>
            {
                _searchField.Focus();
                _searchField.SelectRange(selectionStart, _suggestion.Length);
            });
            UpdateClearButton(_searchVisible);
            UpdateSuggestionDisplay(string.Empty, -1, -1);
            UpdateFromProperty();

            if (commitSelection && optionIndexToApply >= 0)
            {
                ApplySelection(optionIndexToApply);
            }
        }

        private int ResolvePageSize()
        {
            int resolved = Mathf.Max(1, UnityHelpersSettings.GetStringInListPageLimit());
            if (resolved != _lastResolvedPageSize)
            {
                _lastResolvedPageSize = resolved;
                _pageIndex = 0;
            }

            return resolved;
        }

        private bool ShouldShowSearch(int pageSize)
        {
            return OptionCount > pageSize;
        }

        private void ApplySearchVisibility(bool searchVisible)
        {
            if (_searchRow == null)
            {
                return;
            }

            _searchVisible = searchVisible;
            _searchRow.style.display = searchVisible ? DisplayStyle.Flex : DisplayStyle.None;

            if (_dropdown != null)
            {
                _dropdown.style.marginTop = searchVisible ? 2f : 0f;
            }

            if (!searchVisible)
            {
                if (!string.IsNullOrEmpty(_searchText))
                {
                    _searchText = string.Empty;
                    _searchField.SetValueWithoutNotify(string.Empty);
                }

                UpdateSuggestionDisplay(string.Empty, -1, -1);
                _suggestionOptionIndex = -1;

                if (_paginationContainer != null)
                {
                    _paginationContainer.style.display = DisplayStyle.None;
                }

                if (_previousButton != null)
                {
                    _previousButton.SetEnabled(false);
                }

                if (_nextButton != null)
                {
                    _nextButton.SetEnabled(false);
                }
            }

            UpdateClearButton(searchVisible);
            if (!searchVisible && _suggestionHintLabel != null)
            {
                _suggestionHintLabel.text = string.Empty;
                _suggestionHintLabel.style.display = DisplayStyle.None;
            }
        }

        private int ResolveOptionIndex(string optionLabel)
        {
            if (string.IsNullOrEmpty(optionLabel))
            {
                return -1;
            }

            for (int i = 0; i < _pageChoices.Count && i < _pageOptionIndices.Count; i++)
            {
                if (string.Equals(_pageChoices[i], optionLabel, StringComparison.Ordinal))
                {
                    return _pageOptionIndices[i];
                }
            }

            for (int i = 0; i < OptionCount; i++)
            {
                string label = GetNormalizedDisplayLabel(i);
                if (string.Equals(label, optionLabel, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        internal void ApplySelection(int optionIndex)
        {
            if (_boundObject == null || string.IsNullOrEmpty(_propertyPath))
            {
                return;
            }

            if (optionIndex < 0 || optionIndex >= OptionCount)
            {
                return;
            }

            SerializedObject serializedObject = _boundObject;
            Undo.RecordObjects(serializedObject.targetObjects, UndoActionName);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(_propertyPath);
            if (property == null)
            {
                return;
            }

            ApplySelectionToProperty(property, optionIndex);
            SetValueWithoutNotify(GetValueForOption(optionIndex));
            serializedObject.ApplyModifiedProperties();
            UpdateFromProperty();
        }

        private static int CalculatePageCount(int pageSize, int filteredCount)
        {
            if (filteredCount <= 0)
            {
                return 1;
            }

            return (filteredCount + pageSize - 1) / pageSize;
        }

        /// <summary>
        /// Gets a cached pagination label. Delegates to <see cref="EditorCacheHelper.GetPaginationLabel"/>.
        /// </summary>
        private static string GetPaginationLabel(int page, int totalPages)
        {
            return EditorCacheHelper.GetPaginationLabel(page, totalPages);
        }
    }
#endif
}
