// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Xml;
    using System.Xml.Linq;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Helper;

    internal sealed class AnalyzerPolicyWindow : EditorWindow
    {
        private const string RulesetFileName = "Default.ruleset";
        private const float DetailCardHeight = 270f;
        private const float StackedExamplesWidth = 620f;
        private static readonly List<string> SeverityLabels = new()
        {
            "Default",
            "Off",
            "Info",
            "Warning",
            "Error",
            "Hidden",
        };
        private static readonly string[] SeverityActions =
        {
            "Default",
            "None",
            "Info",
            "Warning",
            "Error",
            "Hidden",
        };
        private static readonly string[] MonospaceFonts =
        {
            "Consolas",
            "Menlo",
            "DejaVu Sans Mono",
            "Liberation Mono",
            "Courier New",
            "Lucida Console",
            "Monaco",
            "Courier",
        };
        private static readonly AnalyzerPolicy[] Policies = AnalyzerPolicyAPI.Policies;
        internal VisualElement ExamplesContainer
        {
            get { return _examplesContainer; }
        }

        internal Label BadCodeLabel
        {
            get { return _badCode; }
        }

        internal Button CopyFixButton
        {
            get { return _copyFixButton; }
        }

        internal VisualElement DetailsCard
        {
            get { return _details; }
        }

        internal string RulesetPathOverride;
        private readonly Dictionary<string, string> _actions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DropdownField> _severityFields = new(
            StringComparer.Ordinal
        );
        private readonly Dictionary<string, VisualElement> _rows = new(StringComparer.Ordinal);
        private bool _canEdit;
        private double _nextRefreshTime;
        private string _stateMessage;
        private ToolbarSearchField _searchField;
        private Label _status;
        private Label _emptyResult;
        private VisualElement _details;
        private VisualElement _examplesContainer;
        private ScrollView _examplesScroll;
        private Label _detailsHeading;
        private Label _reason;
        private Label _badCode;
        private Label _goodCode;
        private Button _pinButton;
        private Button _copyFixButton;
        private IVisualElementScheduledItem _hoverTask;
        private IVisualElementScheduledItem _hideTask;
        private bool _detailsPinned;
        private bool _pointerOverDetails;
        private VisualElement _detailsAnchor;
        private string _currentGoodCode;
        private Font _codeFont;
        private bool _ownsCodeFont;

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Analyzer Policies", priority = -1)]
        internal static void ShowWindow()
        {
            GetWindow<AnalyzerPolicyWindow>("Analyzer Policies");
        }

        internal static IReadOnlyList<AnalyzerPolicy> GetPolicies()
        {
            return Policies;
        }

        internal static string GetRulesetPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, RulesetFileName));
        }

        internal string GetAction(string id)
        {
            return _actions.TryGetValue(id, out string action) ? action : "Default";
        }

        internal DropdownField GetSeverityField(string id)
        {
            return _severityFields.TryGetValue(id, out DropdownField field) ? field : null;
        }

        internal VisualElement GetPolicyRow(string id)
        {
            return _rows.TryGetValue(id, out VisualElement row) ? row : null;
        }

        internal bool TryApplySeverity(string id, string action)
        {
            bool written =
                RulesetPathOverride == null
                    ? AnalyzerPolicyAPI.TrySetSeverity(id, action, out _stateMessage)
                    : AnalyzerPolicyRuleset.TryWriteSeverity(
                        GetCurrentRulesetPath(),
                        id,
                        action,
                        Policies,
                        out _stateMessage
                    );
            if (written)
            {
                RefreshState();
            }
            else
            {
                _canEdit = false;
                RefreshControls();
            }
            return written;
        }

        internal void OnInspectorUpdate()
        {
            if (_nextRefreshTime <= EditorApplication.timeSinceStartup)
            {
                _nextRefreshTime = EditorApplication.timeSinceStartup + 0.5;
                RefreshState();
            }
        }

        internal void RefreshState()
        {
            _canEdit = AnalyzerPolicyRuleset.TryReadActions(
                GetCurrentRulesetPath(),
                Policies,
                _actions,
                out _stateMessage
            );
            RefreshControls();
        }

        internal void BuildUserInterface()
        {
            minSize = new Vector2(420f, 400f);
            _hoverTask?.Pause();
            _hideTask?.Pause();
            _rows.Clear();
            _severityFields.Clear();
            _detailsPinned = false;
            _pointerOverDetails = false;
            _detailsAnchor = null;
            VisualElement root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = 5f;
            root.style.paddingRight = 5f;
            root.style.paddingTop = 3f;
            root.style.paddingBottom = 3f;
            Toolbar toolbar = new();
            toolbar.Add(
                new ToolbarButton(() => ApplyState(AnalyzerPolicyState.Enabled))
                {
                    text = "Enable All",
                    tooltip = "Set every analyzer to Warning.",
                }
            );
            toolbar.Add(
                new ToolbarButton(() => ApplyState(AnalyzerPolicyState.Disabled))
                {
                    text = "Disable All",
                    tooltip = "Turn every analyzer off.",
                }
            );
            toolbar.Add(
                new ToolbarButton(RefreshState)
                {
                    text = "Refresh",
                    tooltip = "Read the project ruleset without changing it.",
                }
            );
            foreach (VisualElement child in toolbar.Children())
            {
                child.style.flexShrink = 0f;
                child.style.minWidth = 66f;
            }
            _searchField = new ToolbarSearchField
            {
                name = nameof(_searchField),
                tooltip = "Search diagnostic IDs, titles and descriptions.",
            };
            _searchField.style.flexGrow = 1f;
            _searchField.style.flexShrink = 1f;
            _searchField.style.flexBasis = 0f;
            _searchField.style.minWidth = 0f;
            TextField searchInput = _searchField.Q<TextField>();
            if (searchInput != null)
            {
                searchInput.style.minWidth = 0f;
                searchInput.style.flexShrink = 1f;
            }
            _searchField.RegisterValueChangedCallback(_ => FilterRows());
            toolbar.Add(_searchField);
            toolbar.style.flexShrink = 0f;
            root.Add(toolbar);
            _status = new Label { name = nameof(_status) };
            _status.style.fontSize = 11f;
            _status.style.marginTop = 3f;
            _status.style.marginBottom = 4f;
            root.Add(_status);
            ScrollView list = new(ScrollViewMode.Vertical);
            list.style.flexGrow = 1f;
            list.style.flexBasis = 0f;
            root.Add(list);
            foreach (AnalyzerPolicy policy in Policies)
            {
                VisualElement row = BuildPolicyRow(policy);
                _rows.Add(policy.Id, row);
                list.Add(row);
            }
            _emptyResult = new Label("No matching diagnostics.");
            _emptyResult.style.paddingTop = 8f;
            list.Add(_emptyResult);
            Label hint = new(
                "Hover or focus a rule for examples · Click or press Enter to pin · Esc closes"
            );
            hint.style.fontSize = 10f;
            hint.style.opacity = 0.7f;
            hint.style.marginTop = 3f;
            root.Add(hint);
            BuildDetailsCard(root);
            root.UnregisterCallback<GeometryChangedEvent>(OnWindowGeometryChanged);
            root.RegisterCallback<GeometryChangedEvent>(OnWindowGeometryChanged);
            root.UnregisterCallback<KeyDownEvent>(OnRootKeyDown);
            root.RegisterCallback<KeyDownEvent>(OnRootKeyDown);
            root.UnregisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);
            RefreshControls();
            FilterRows();
        }

        private void OnRootKeyDown(KeyDownEvent eventData)
        {
            if (eventData.keyCode == KeyCode.Escape)
            {
                HideDetails();
                eventData.StopPropagation();
            }
        }

        private void OnRootPointerDown(PointerDownEvent eventData)
        {
            VisualElement target = eventData.target as VisualElement;
            if (
                !_detailsPinned
                && _details != null
                && target != null
                && !_details.Contains(target)
                && !ReferenceEquals(target, _details)
            )
            {
                HideDetails();
            }
        }

        private void OnEnable()
        {
            RefreshState();
        }

        private void OnDisable()
        {
            _hoverTask?.Pause();
            _hideTask?.Pause();
            if (_ownsCodeFont && _codeFont != null)
            {
                UnityEngine.Object.DestroyImmediate(_codeFont);
                _codeFont = null;
                _ownsCodeFont = false;
            }
        }

        private void OnFocus()
        {
            RefreshState();
        }

        private void CreateGUI()
        {
            BuildUserInterface();
        }

        private VisualElement BuildPolicyRow(AnalyzerPolicy policy)
        {
            VisualElement row = new() { name = policy.Id };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 27f;
            row.style.flexShrink = 0f;
            row.style.borderBottomWidth = 1f;
            row.style.borderBottomColor = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.06f)
                : new Color(0f, 0f, 0f, 0.08f);
            Button title = new(() => ShowDetails(policy, row, true))
            {
                text = policy.Id + "  " + policy.Title,
                tooltip = policy.Description + " Click to pin code examples.",
            };
            title.style.flexGrow = 1f;
            title.style.unityTextAlign = TextAnchor.MiddleLeft;
            title.style.marginLeft = 0f;
            title.style.marginRight = 6f;
            title.style.borderLeftWidth = 0f;
            title.style.borderRightWidth = 0f;
            title.style.borderTopWidth = 0f;
            title.style.borderBottomWidth = 0f;
            title.style.backgroundColor = Color.clear;
            title.RegisterCallback<FocusInEvent>(_ => ShowDetails(policy, row, false));
            title.RegisterCallback<KeyDownEvent>(eventData =>
            {
                if (
                    eventData.keyCode == KeyCode.Return
                    || eventData.keyCode == KeyCode.KeypadEnter
                    || eventData.keyCode == KeyCode.Space
                )
                {
                    ShowDetails(policy, row, true);
                    eventData.StopPropagation();
                }
            });
            row.RegisterCallback<MouseEnterEvent>(_ =>
            {
                _hoverTask?.Pause();
                _hideTask?.Pause();
                _hoverTask = row
                    .schedule.Execute(() => ShowDetails(policy, row, false))
                    .StartingIn(300);
            });
            row.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                _hoverTask?.Pause();
                ScheduleDetailsHide();
            });
            title.RegisterCallback<FocusOutEvent>(_ => ScheduleDetailsHide());
            row.Add(title);
            DropdownField severity = new(SeverityLabels, 0)
            {
                name = policy.Id + "Severity",
                tooltip =
                    "Project override for " + policy.Id + ". Default uses the shipped setting.",
            };
            severity.style.width = 102f;
            severity.style.flexShrink = 0f;
            severity.style.marginRight = 2f;
            severity.RegisterValueChangedCallback(eventData =>
            {
                int selected = SeverityLabels.IndexOf(eventData.newValue);
                if (0 <= selected && selected < SeverityActions.Length)
                {
                    TryApplySeverity(policy.Id, SeverityActions[selected]);
                }
            });
            _severityFields.Add(policy.Id, severity);
            row.Add(severity);
            return row;
        }

        private void BuildDetailsCard(VisualElement root)
        {
            EnsureCodeFont();
            _details = new VisualElement { name = nameof(_details) };
            _details.style.position = Position.Absolute;
            _details.style.left = 8f;
            _details.style.right = 8f;
            _details.style.maxHeight = DetailCardHeight;
            _details.style.backgroundColor = EditorGUIUtility.isProSkin
                ? new Color(0.14f, 0.16f, 0.19f)
                : new Color(0.94f, 0.95f, 0.97f);
            _details.style.borderLeftWidth =
                _details.style.borderRightWidth =
                _details.style.borderTopWidth =
                _details.style.borderBottomWidth =
                    1f;
            Color border = EditorGUIUtility.isProSkin
                ? new Color(0.35f, 0.48f, 0.62f)
                : new Color(0.45f, 0.56f, 0.68f);
            _details.style.borderLeftColor =
                _details.style.borderRightColor =
                _details.style.borderTopColor =
                _details.style.borderBottomColor =
                    border;
            _details.style.paddingLeft = _details.style.paddingRight = 9f;
            _details.style.paddingTop = _details.style.paddingBottom = 7f;
            _details.style.display = DisplayStyle.None;
            _details.RegisterCallback<MouseEnterEvent>(_ =>
            {
                _pointerOverDetails = true;
                _hideTask?.Pause();
            });
            _details.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                _pointerOverDetails = false;
                ScheduleDetailsHide();
            });
            VisualElement header = new();
            header.style.flexDirection = FlexDirection.Row;
            _detailsHeading = new Label { name = nameof(_detailsHeading) };
            _detailsHeading.style.unityFontStyleAndWeight = FontStyle.Bold;
            _detailsHeading.style.flexGrow = 1f;
            _detailsHeading.style.minWidth = 0f;
            _detailsHeading.style.overflow = Overflow.Hidden;
            _detailsHeading.style.textOverflow = TextOverflow.Ellipsis;
            header.style.flexShrink = 0f;
            header.Add(_detailsHeading);
            _pinButton = new Button(() =>
            {
                _detailsPinned = !_detailsPinned;
                _pinButton.text = _detailsPinned ? "Unpin" : "Pin";
            })
            {
                text = "Pin",
                tooltip = "Keep this example visible while editing other rules.",
            };
            _pinButton.style.flexShrink = 0f;
            header.Add(_pinButton);
            _copyFixButton = new Button(() =>
                GUIUtility.systemCopyBuffer = _currentGoodCode ?? string.Empty
            )
            {
                text = "Copy fix",
                tooltip = "Copy the recommended code example.",
            };
            _copyFixButton.style.flexShrink = 0f;
            header.Add(_copyFixButton);
            Button close = new(HideDetails) { text = "×", tooltip = "Close examples (Escape)." };
            close.style.flexShrink = 0f;
            header.Add(close);
            _details.Add(header);
            _reason = new Label { name = nameof(_reason) };
            _reason.style.whiteSpace = WhiteSpace.Normal;
            _reason.style.marginBottom = 5f;
            _reason.style.flexShrink = 0f;
            _details.Add(_reason);
            _examplesScroll = new ScrollView(ScrollViewMode.Vertical);
            _examplesScroll.style.flexShrink = 1f;
            _examplesScroll.style.minHeight = 0f;
            _examplesScroll.style.maxHeight = 180f;
            _examplesContainer = new VisualElement { name = nameof(_examplesContainer) };
            _examplesContainer.style.minHeight = 0f;
            _badCode = AddExamplePanel(
                _examplesContainer,
                "Avoid",
                EditorGUIUtility.isProSkin
                    ? new Color(1f, 0.6f, 0.6f)
                    : new Color(0.64f, 0.08f, 0.08f)
            );
            _goodCode = AddExamplePanel(
                _examplesContainer,
                "Prefer",
                EditorGUIUtility.isProSkin
                    ? new Color(0.4f, 0.85f, 0.63f)
                    : new Color(0.15f, 0.44f, 0.15f)
            );
            _badCode.name = nameof(_badCode);
            _goodCode.name = nameof(_goodCode);
            _examplesScroll.Add(_examplesContainer);
            _details.Add(_examplesScroll);
            _details.RegisterCallback<GeometryChangedEvent>(_ => PlaceDetailsCard());
            root.Add(_details);
            UpdateExamplesLayout();
        }

        private void EnsureCodeFont()
        {
            if (_codeFont != null)
            {
                return;
            }
            _codeFont = EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font;
            if (_codeFont != null)
            {
                return;
            }
            try
            {
                string[] installedFonts = Font.GetOSInstalledFontNames();
                foreach (string preferredFont in MonospaceFonts)
                {
                    foreach (string installedFont in installedFonts)
                    {
                        if (
                            string.Equals(
                                preferredFont,
                                installedFont,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            _codeFont = Font.CreateDynamicFontFromOSFont(installedFont, 12);
                            _ownsCodeFont = _codeFont != null;
                            return;
                        }
                    }
                }
            }
            catch (Exception)
            {
                _codeFont = null;
            }
        }

        private Label AddExamplePanel(VisualElement container, string heading, Color accent)
        {
            VisualElement panel = new();
            panel.style.flexGrow = 1f;
            panel.style.flexBasis = 0f;
            panel.style.marginRight = 4f;
            panel.style.minWidth = 0f;
            Label headingLabel = new(heading);
            headingLabel.style.color = accent;
            headingLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            panel.Add(headingLabel);
            ScrollView scroll = new(ScrollViewMode.VerticalAndHorizontal);
            scroll.style.maxHeight = 180f;
            Label code = new() { enableRichText = true };
            code.style.fontSize = 12f;
            code.style.whiteSpace = WhiteSpace.NoWrap;
            if (_codeFont != null)
            {
                code.style.unityFont = _codeFont;
                code.style.unityFontDefinition = FontDefinition.FromFont(_codeFont);
            }
            scroll.Add(code);
            panel.Add(scroll);
            container.Add(panel);
            return code;
        }

        private void ShowDetails(AnalyzerPolicy policy, VisualElement row, bool pin)
        {
            if (_details == null || (_detailsPinned && !pin))
            {
                return;
            }
            _hideTask?.Pause();
            _detailsAnchor = row;
            _detailsPinned = pin;
            _pinButton.text = pin ? "Unpin" : "Pin";
            _detailsHeading.text = policy.Id + " · " + policy.Title;
            _reason.text = AnalyzerPolicyExamples.TryGetExplanation(
                policy.Id,
                out string explanation
            )
                ? explanation
                : policy.Description;
            if (AnalyzerPolicyExamples.TryGet(policy.Id, out string badCode, out string goodCode))
            {
                _badCode.text = AnalyzerPolicyCodeHighlighting.Format(badCode);
                _goodCode.text = AnalyzerPolicyCodeHighlighting.Format(goodCode);
                _currentGoodCode = goodCode;
            }
            UpdateExamplesLayout();
            _details.style.display = DisplayStyle.Flex;
            PlaceDetailsCard();
        }

        private void OnWindowGeometryChanged(GeometryChangedEvent eventData)
        {
            UpdateExamplesLayout();
            PlaceDetailsCard();
        }

        private void UpdateExamplesLayout()
        {
            if (_examplesContainer == null)
            {
                return;
            }
            float width = rootVisualElement.resolvedStyle.width;
            bool stacked = !float.IsNaN(width) && width < StackedExamplesWidth;
            _examplesContainer.style.flexDirection = stacked
                ? FlexDirection.Column
                : FlexDirection.Row;
            foreach (VisualElement panel in _examplesContainer.Children())
            {
                panel.style.flexGrow = stacked ? 0f : 1f;
                panel.style.flexShrink = stacked ? 0f : 1f;
                panel.style.flexBasis = stacked
                    ? new StyleLength(StyleKeyword.Auto)
                    : new StyleLength(0f);
                panel.style.marginBottom = stacked ? 6f : 0f;
            }
        }

        private void PlaceDetailsCard()
        {
            if (
                _details == null
                || _detailsAnchor == null
                || _details.style.display.value == DisplayStyle.None
            )
            {
                return;
            }
            Rect rootBounds = rootVisualElement.worldBound;
            float availableHeight = rootBounds.height;
            if (float.IsNaN(availableHeight) || availableHeight <= 0f)
            {
                return;
            }
            float maximumHeight = Mathf.Max(0f, Mathf.Min(DetailCardHeight, availableHeight - 16f));
            _details.style.maxHeight = maximumHeight;
            float cardHeight = _details.resolvedStyle.height;
            if (float.IsNaN(cardHeight) || cardHeight <= 0f)
            {
                cardHeight = maximumHeight;
            }
            cardHeight = Mathf.Min(cardHeight, maximumHeight);
            float top;
            if (_detailsAnchor.resolvedStyle.display == DisplayStyle.None)
            {
                top = _details.resolvedStyle.top;
            }
            else
            {
                top = _detailsAnchor.worldBound.yMax - rootBounds.y + 3f;
                if (availableHeight < top + cardHeight + 8f)
                {
                    top = _detailsAnchor.worldBound.y - rootBounds.y - cardHeight - 3f;
                }
            }
            _details.style.top = Mathf.Clamp(
                top,
                8f,
                Mathf.Max(8f, availableHeight - cardHeight - 8f)
            );
        }

        private void ScheduleDetailsHide()
        {
            _hideTask?.Pause();
            if (!_detailsPinned && _details != null)
            {
                _hideTask = _details
                    .schedule.Execute(() =>
                    {
                        VisualElement focused =
                            rootVisualElement.panel?.focusController.focusedElement
                            as VisualElement;
                        if (
                            !_detailsPinned
                            && !_pointerOverDetails
                            && (
                                focused == null
                                || (
                                    !_details.Contains(focused)
                                    && (_detailsAnchor == null || !_detailsAnchor.Contains(focused))
                                )
                            )
                        )
                        {
                            HideDetails();
                        }
                    })
                    .StartingIn(180);
            }
        }

        private void HideDetails()
        {
            _hoverTask?.Pause();
            _hideTask?.Pause();
            _detailsPinned = false;
            _pointerOverDetails = false;
            _detailsAnchor = null;
            if (_details != null)
            {
                _details.style.display = DisplayStyle.None;
            }
        }

        private void FilterRows()
        {
            string search = _searchField.value?.Trim() ?? string.Empty;
            int visible = 0;
            foreach (AnalyzerPolicy policy in Policies)
            {
                bool matches =
                    search.Length == 0
                    || 0 <= policy.Id.IndexOf(search, StringComparison.OrdinalIgnoreCase)
                    || 0 <= policy.Title.IndexOf(search, StringComparison.OrdinalIgnoreCase)
                    || 0 <= policy.Description.IndexOf(search, StringComparison.OrdinalIgnoreCase);
                if (_rows.TryGetValue(policy.Id, out VisualElement row))
                {
                    row.style.display = matches ? DisplayStyle.Flex : DisplayStyle.None;
                }
                if (matches)
                {
                    ++visible;
                }
            }
            _emptyResult.style.display = visible == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (!_detailsPinned)
            {
                HideDetails();
            }
        }

        private void RefreshControls()
        {
            if (_status == null)
            {
                return;
            }
            _status.text = _canEdit
                ? "Assets/Default.ruleset · live sync · edits save immediately"
                : _stateMessage;
            _status.tooltip = _stateMessage;
            _status.style.color = _canEdit
                ? StyleKeyword.Null
                : new StyleColor(new Color(0.9f, 0.35f, 0.3f));
            foreach (KeyValuePair<string, DropdownField> entry in _severityFields)
            {
                string action = GetAction(entry.Key);
                int selected = 0;
                for (int index = 0; index < SeverityActions.Length; ++index)
                {
                    if (
                        string.Equals(
                            SeverityActions[index],
                            action,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        selected = index;
                        break;
                    }
                }
                entry.Value.SetValueWithoutNotify(SeverityLabels[selected]);
                entry.Value.SetEnabled(_canEdit);
            }
        }

        private void ApplyState(AnalyzerPolicyState state)
        {
            bool written =
                RulesetPathOverride == null
                    ? AnalyzerPolicyAPI.TrySetEnabled(
                        state == AnalyzerPolicyState.Enabled,
                        out _stateMessage
                    )
                    : AnalyzerPolicyRuleset.TryWrite(
                        GetCurrentRulesetPath(),
                        state,
                        Policies,
                        out _stateMessage
                    );
            if (written)
            {
                RefreshState();
            }
            else
            {
                _canEdit = false;
                RefreshControls();
            }
        }

        private string GetCurrentRulesetPath()
        {
            return RulesetPathOverride ?? GetRulesetPath();
        }
    }

    internal static class AnalyzerPolicyRuleset
    {
        private const string AnalyzerId = "WallstopStudios.UnityHelpers.Analyzers";
        private const string RuleNamespace = "WallstopStudios.UnityHelpers.Analyzers";
        private const string RulesetNamespace =
            "http://schemas.microsoft.com/developer/msbuild/2003";

        internal static bool TryRead(
            string path,
            IReadOnlyList<AnalyzerPolicy> policies,
            out AnalyzerPolicyState state,
            out string message
        )
        {
            if (string.IsNullOrWhiteSpace(path) || policies == null || policies.Count == 0)
            {
                state = AnalyzerPolicyState.Drifted;
                message = "The analyzer policy catalog or ruleset path is invalid.";
                return false;
            }

            if (!File.Exists(path))
            {
                message =
                    "Assets/Default.ruleset is missing. Enable or disable the policies to create it.";
                state = AnalyzerPolicyState.Missing;
                return true;
            }

            if (!TryLoad(path, out XDocument document, out message))
            {
                state = AnalyzerPolicyState.Drifted;
                return false;
            }

            return TryClassify(document, policies, out state, out message);
        }

        internal static bool TryWrite(
            string path,
            AnalyzerPolicyState requestedState,
            IReadOnlyList<AnalyzerPolicy> policies,
            out string message
        )
        {
            if (
                string.IsNullOrWhiteSpace(path)
                || policies == null
                || policies.Count == 0
                || (
                    requestedState != AnalyzerPolicyState.Enabled
                    && requestedState != AnalyzerPolicyState.Disabled
                )
            )
            {
                message = "Only a valid enabled or disabled analyzer policy can be written.";
                return false;
            }

            XDocument document;
            if (File.Exists(path))
            {
                if (!TryLoad(path, out document, out message))
                {
                    return false;
                }
            }
            else
            {
                XNamespace rulesetNamespace = RulesetNamespace;
                document = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    new XElement(
                        rulesetNamespace + "RuleSet",
                        new XAttribute("Name", "Default Rules"),
                        new XAttribute("ToolsVersion", "15.0")
                    )
                );
            }

            XElement root = document.Root;
            if (
                root == null
                || !string.Equals(root.Name.LocalName, "RuleSet", StringComparison.Ordinal)
            )
            {
                message = "The existing ruleset has no valid RuleSet root and was left unchanged.";
                return false;
            }

            List<XElement> managedGroups = FindManagedGroups(root);
            List<XElement> replacedRules = new();
            foreach (XElement managedGroup in managedGroups)
            {
                foreach (XElement rule in managedGroup.Elements())
                {
                    if (
                        string.Equals(rule.Name.LocalName, "Rule", StringComparison.Ordinal)
                        && IsKnownPolicy((string)rule.Attribute("Id"), policies)
                    )
                    {
                        replacedRules.Add(rule);
                    }
                }
            }
            foreach (XElement rule in replacedRules)
            {
                rule.Remove();
            }
            XElement replacement = CreateManagedGroup(
                root.Name.Namespace,
                requestedState,
                policies
            );
            if (managedGroups.Count == 0)
            {
                root.Add(replacement);
            }
            else
            {
                managedGroups[0].Add(replacement.Elements());
            }
            if (!TrySaveAtomically(path, document, out message))
            {
                return false;
            }

            message =
                requestedState == AnalyzerPolicyState.Enabled
                    ? "All Unity Helpers analyzer policies are enabled for user code."
                    : "All Unity Helpers analyzer policies are disabled for user code.";
            return true;
        }

        internal static bool TryReadActions(
            string path,
            IReadOnlyList<AnalyzerPolicy> policies,
            Dictionary<string, string> actions,
            out string message
        )
        {
            actions.Clear();
            if (!File.Exists(path))
            {
                message = "No project overrides. Each analyzer uses its shipped default.";
                return true;
            }
            if (
                !TryLoad(path, out XDocument document, out message)
                || !TryGetRoot(document, out XElement root, out message)
            )
            {
                return false;
            }
            foreach (XElement group in FindManagedGroups(root))
            {
                foreach (XElement rule in group.Elements())
                {
                    if (!string.Equals(rule.Name.LocalName, "Rule", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string id = (string)rule.Attribute("Id");
                    if (!IsKnownPolicy(id, policies))
                    {
                        continue;
                    }
                    string action = (string)rule.Attribute("Action");
                    if (!IsValidAction(action) || actions.ContainsKey(id))
                    {
                        message =
                            id
                            + " has an invalid or duplicate override. Correct the file before editing.";
                        return false;
                    }
                    actions.Add(id, action);
                }
            }
            message =
                "Project severity overrides are synchronized with the ruleset. Default uses the analyzer's shipped setting.";
            return true;
        }

        internal static bool TryWriteSeverity(
            string path,
            string id,
            string action,
            IReadOnlyList<AnalyzerPolicy> policies,
            out string message
        )
        {
            if (
                string.IsNullOrWhiteSpace(path)
                || !IsKnownPolicy(id, policies)
                || !IsValidAction(action)
            )
            {
                message =
                    "A known analyzer, valid ruleset path, and Default, None, Info, Warning, Error, or Hidden action are required.";
                return false;
            }
            string canonicalAction;
            if (string.Equals(action, "None", StringComparison.OrdinalIgnoreCase))
            {
                canonicalAction = "None";
            }
            else if (string.Equals(action, "Info", StringComparison.OrdinalIgnoreCase))
            {
                canonicalAction = "Info";
            }
            else if (string.Equals(action, "Warning", StringComparison.OrdinalIgnoreCase))
            {
                canonicalAction = "Warning";
            }
            else if (string.Equals(action, "Error", StringComparison.OrdinalIgnoreCase))
            {
                canonicalAction = "Error";
            }
            else if (string.Equals(action, "Hidden", StringComparison.OrdinalIgnoreCase))
            {
                canonicalAction = "Hidden";
            }
            else
            {
                canonicalAction = "Default";
            }
            XDocument document;
            if (File.Exists(path))
            {
                if (!TryLoad(path, out document, out message))
                {
                    return false;
                }
            }
            else
            {
                document = new XDocument(
                    new XElement(
                        "RuleSet",
                        new XAttribute("Name", "Default Rules"),
                        new XAttribute("ToolsVersion", "15.0")
                    )
                );
            }
            if (!TryGetRoot(document, out XElement root, out message))
            {
                return false;
            }
            List<XElement> groups = FindManagedGroups(root);
            XElement destination = groups.Count == 0 ? null : groups[0];
            XElement existing = null;
            foreach (XElement group in groups)
            {
                foreach (XElement rule in group.Elements())
                {
                    if (
                        string.Equals(rule.Name.LocalName, "Rule", StringComparison.Ordinal)
                        && string.Equals((string)rule.Attribute("Id"), id, StringComparison.Ordinal)
                    )
                    {
                        if (existing != null)
                        {
                            message = id + " has duplicate overrides and was left unchanged.";
                            return false;
                        }
                        existing = rule;
                    }
                }
            }
            if (existing != null)
            {
                existing.SetAttributeValue("Action", canonicalAction);
            }
            else
            {
                if (destination == null)
                {
                    destination = new XElement(
                        root.Name.Namespace + "Rules",
                        new XAttribute("AnalyzerId", AnalyzerId),
                        new XAttribute("RuleNamespace", RuleNamespace)
                    );
                    root.Add(destination);
                }
                destination.Add(
                    new XElement(
                        root.Name.Namespace + "Rule",
                        new XAttribute("Id", id),
                        new XAttribute("Action", canonicalAction)
                    )
                );
            }
            return TrySaveAtomically(path, document, out message);
        }

        private static bool TryGetRoot(XDocument document, out XElement root, out string message)
        {
            XElement candidate = document.Root;
            bool valid =
                candidate != null
                && string.Equals(candidate.Name.LocalName, "RuleSet", StringComparison.Ordinal);
            message = valid
                ? null
                : "The existing ruleset has no valid RuleSet root and was left unchanged.";
            root = candidate;
            return valid;
        }

        private static bool IsKnownPolicy(string id, IReadOnlyList<AnalyzerPolicy> policies)
        {
            if (policies != null)
            {
                foreach (AnalyzerPolicy policy in policies)
                {
                    if (string.Equals(policy.Id, id, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsValidAction(string action)
        {
            return string.Equals(action, "Default", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "None", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "Info", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "Warning", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "Error", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "Hidden", StringComparison.OrdinalIgnoreCase);
        }

        private static XElement CreateManagedGroup(
            XNamespace rulesetNamespace,
            AnalyzerPolicyState state,
            IReadOnlyList<AnalyzerPolicy> policies
        )
        {
            XElement group = new(
                rulesetNamespace + "Rules",
                new XAttribute("AnalyzerId", AnalyzerId),
                new XAttribute("RuleNamespace", RuleNamespace)
            );
            string action = state == AnalyzerPolicyState.Enabled ? "Warning" : "None";
            for (int index = 0; index < policies.Count; ++index)
            {
                group.Add(
                    new XElement(
                        rulesetNamespace + "Rule",
                        new XAttribute("Id", policies[index].Id),
                        new XAttribute("Action", action)
                    )
                );
            }

            return group;
        }

        private static List<XElement> FindManagedGroups(XElement root)
        {
            List<XElement> groups = new();
            foreach (XElement element in root.Elements())
            {
                if (
                    string.Equals(element.Name.LocalName, "Rules", StringComparison.Ordinal)
                    && string.Equals(
                        (string)element.Attribute("AnalyzerId"),
                        AnalyzerId,
                        StringComparison.Ordinal
                    )
                )
                {
                    groups.Add(element);
                }
            }

            return groups;
        }

        private static bool TryClassify(
            XDocument document,
            IReadOnlyList<AnalyzerPolicy> policies,
            out AnalyzerPolicyState state,
            out string message
        )
        {
            XElement root = document.Root;
            if (
                root == null
                || !string.Equals(root.Name.LocalName, "RuleSet", StringComparison.Ordinal)
            )
            {
                state = AnalyzerPolicyState.Drifted;
                message = "Assets/Default.ruleset has no valid RuleSet root.";
                return false;
            }

            List<XElement> groups = FindManagedGroups(root);
            if (groups.Count != 1)
            {
                message =
                    groups.Count == 0
                        ? "The Unity Helpers analyzer policy block is missing."
                        : "The Unity Helpers analyzer policy block is duplicated.";
                state = AnalyzerPolicyState.Drifted;
                return true;
            }

            Dictionary<string, string> actions = new(StringComparer.Ordinal);
            foreach (XElement rule in groups[0].Elements())
            {
                if (!string.Equals(rule.Name.LocalName, "Rule", StringComparison.Ordinal))
                {
                    continue;
                }

                string id = (string)rule.Attribute("Id");
                string action = (string)rule.Attribute("Action");
                if (
                    string.IsNullOrWhiteSpace(id)
                    || string.IsNullOrWhiteSpace(action)
                    || actions.ContainsKey(id)
                )
                {
                    message =
                        "The Unity Helpers analyzer policy block contains an invalid or duplicate rule.";
                    state = AnalyzerPolicyState.Drifted;
                    return true;
                }

                actions.Add(id, action);
            }

            AnalyzerPolicyState detected = AnalyzerPolicyState.Missing;
            for (int index = 0; index < policies.Count; ++index)
            {
                if (!actions.TryGetValue(policies[index].Id, out string action))
                {
                    message =
                        policies[index].Id + " is missing from the Unity Helpers policy block.";
                    state = AnalyzerPolicyState.Drifted;
                    return true;
                }

                AnalyzerPolicyState ruleState;
                if (string.Equals(action, "Warning", StringComparison.OrdinalIgnoreCase))
                {
                    ruleState = AnalyzerPolicyState.Enabled;
                }
                else if (string.Equals(action, "None", StringComparison.OrdinalIgnoreCase))
                {
                    ruleState = AnalyzerPolicyState.Disabled;
                }
                else
                {
                    state = AnalyzerPolicyState.Drifted;
                    message = policies[index].Id + " has unsupported action '" + action + "'.";
                    return true;
                }

                if (detected == AnalyzerPolicyState.Missing)
                {
                    detected = ruleState;
                }
                else if (detected != ruleState)
                {
                    message =
                        "Unity Helpers analyzer policies are mixed instead of uniformly enabled or disabled.";
                    state = AnalyzerPolicyState.Drifted;
                    return true;
                }
            }

            if (actions.Count != policies.Count)
            {
                state = AnalyzerPolicyState.Drifted;
                message = "The Unity Helpers analyzer policy block contains an unknown rule.";
                return true;
            }

            message =
                detected == AnalyzerPolicyState.Enabled
                    ? "All Unity Helpers analyzer policies are enabled for user code."
                    : "All Unity Helpers analyzer policies are disabled for user code.";
            state = detected;
            return true;
        }

        private static bool TryLoad(string path, out XDocument document, out string message)
        {
            try
            {
                document = XDocument.Load(path, LoadOptions.None);
                message = null;
                return true;
            }
            catch (Exception exception)
                when (exception is IOException
                    || exception is UnauthorizedAccessException
                    || exception is XmlException
                )
            {
                message =
                    "Assets/Default.ruleset could not be read and was left unchanged: "
                    + exception.Message;
                document = null;
                return false;
            }
        }

        private static bool TrySaveAtomically(string path, XDocument document, out string message)
        {
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                message = "The ruleset path has no writable parent directory.";
                return false;
            }

            try
            {
                Directory.CreateDirectory(directory);
                XmlWriterSettings settings = new()
                {
                    Encoding = new UTF8Encoding(false),
                    Indent = true,
                    NewLineChars = "\n",
                    NewLineHandling = NewLineHandling.Replace,
                };
                using MemoryStream output = new();
                using (XmlWriter writer = XmlWriter.Create(output, settings))
                {
                    document.Save(writer);
                }

                if (!DurableFile.TryWriteAllBytes(path, output.ToArray(), out Exception writeError))
                {
                    message =
                        "Assets/Default.ruleset could not be written: "
                        + (writeError != null ? writeError.Message : "Unknown write failure.");
                    return false;
                }

                message = null;
                return true;
            }
            catch (Exception exception)
                when (exception is IOException
                    || exception is UnauthorizedAccessException
                    || exception is XmlException
                )
            {
                message = "Assets/Default.ruleset could not be written: " + exception.Message;
                return false;
            }
        }
    }

    internal enum AnalyzerPolicyState
    {
        Missing,
        Enabled,
        Disabled,
        Drifted,
    }

    internal readonly struct AnalyzerPolicy
    {
        internal readonly string Id;
        internal readonly string Title;
        internal readonly string Description;
        internal readonly GUIContent HeadingContent;
        internal readonly GUIContent DescriptionContent;

        internal AnalyzerPolicy(string id, string title, string description)
        {
            Id = id;
            Title = title;
            Description = description;
            HeadingContent = new GUIContent(id + " — " + title);
            DescriptionContent = new GUIContent(description);
        }
    }
#endif
}
