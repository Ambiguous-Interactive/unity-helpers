// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Editor.Styles;

    public sealed partial class ValidationWindow
    {
        private string _axis = "All Issues";
        private readonly Dictionary<string, Button> _axisButtons = new Dictionary<string, Button>();
        private readonly Dictionary<ValidationSeverity, Button> _severityButtons =
            new Dictionary<ValidationSeverity, Button>();
        private readonly HashSet<ValidationSeverity> _enabledSeverities =
            new HashSet<ValidationSeverity>
            {
                ValidationSeverity.Error,
                ValidationSeverity.Warning,
                ValidationSeverity.Info,
            };
        private Label _detailTitle;
        private Label _detailMessage;
        private Label _detailPath;
        private Button _selectAsset;
        private Button _suppress;
        private Button _fix;
        private Button _fixVisible;

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            _toast = null;
            EditorTheme.Apply(root);
            StyleSheet sheet = EditorTheme.Load("ValidationWindow.uss");
            if (sheet != null && !root.styleSheets.Contains(sheet))
            {
                root.styleSheets.Add(sheet);
            }

            root.AddToClassList("sentinel");
            VisualElement pages = CreateNavigation(root);
            root = Page(pages, "Issues");
            VisualElement toolbar = Element(root, "sentinel-toolbar", "dx-row");
            Label title = new Label("Issues");
            title.AddToClassList("sentinel-title");
            toolbar.Add(title);
            _run = new Button(RunOrCancel) { text = "Validate Project", name = "validate-project" };
            _run.AddToClassList("dx-primary");
            toolbar.Add(_run);
            _fixVisible = new Button(FixVisible) { text = "Fix Visible" };
            _fixVisible.AddToClassList("dx-positive");
            toolbar.Add(_fixVisible);

            TextField search = new TextField
            {
                value = _query,
                name = "issue-search",
                tooltip = "Search issue messages, paths and rule identifiers",
            };
            search.AddToClassList("sentinel-search");
            search.RegisterValueChangedCallback(changed =>
            {
                _query = changed.newValue;
                Refresh();
            });
            toolbar.Add(search);

            _severity = new DropdownField(
                "Severity",
                new List<string> { "Info", "Warning", "Error" },
                (int)_minimum - 1
            )
            {
                name = "minimum-severity",
                tooltip = "Show this severity and higher",
            };
            _severity.RegisterValueChangedCallback(changed =>
            {
                if (Enum.TryParse(changed.newValue, out ValidationSeverity severity))
                {
                    _minimum = severity;
                    Refresh();
                }
            });
            toolbar.Add(_severity);
            _severityButtons.Clear();
            foreach (
                ValidationSeverity severity in new[]
                {
                    ValidationSeverity.Error,
                    ValidationSeverity.Warning,
                    ValidationSeverity.Info,
                }
            )
            {
                Button chip = new Button(() =>
                {
                    if (!_enabledSeverities.Remove(severity))
                        _enabledSeverities.Add(severity);
                    Refresh();
                })
                {
                    tooltip = "Toggle " + severity + " findings",
                };
                chip.AddToClassList("sentinel-severity-chip");
                chip.AddToClassList(
                    severity == ValidationSeverity.Error ? "dx-error"
                    : severity == ValidationSeverity.Warning ? "dx-warning"
                    : "dx-info"
                );
                toolbar.Add(chip);
                _severityButtons.Add(severity, chip);
            }

            _progressBar = new ProgressBar
            {
                name = "validation-progress",
                lowValue = 0,
                highValue = 1,
            };
            _progressBar.AddToClassList("sentinel-progress");
            _progressBar.AddToClassList("dx-hidden");
            root.Add(_progressBar);

            VisualElement options = Element(root, "sentinel-options", "dx-row");
            Toggle auto = new Toggle("Re-check on import") { value = ValidationAutoRun.Enabled };
            auto.RegisterValueChangedCallback(changed =>
                ValidationAutoRun.Enabled = changed.newValue
            );
            options.Add(auto);
            Toggle suppressed = new Toggle("Show suppressed") { value = _includeSuppressed };
            suppressed.RegisterValueChangedCallback(changed =>
            {
                _includeSuppressed = changed.newValue;
                Refresh();
            });
            options.Add(suppressed);

            VisualElement body = Element(root, "sentinel-body", "dx-row", "dx-grow");
            ScrollView sidebar = new ScrollView();
            sidebar.AddToClassList("sentinel-sidebar");
            body.Add(sidebar);
            _axisButtons.Clear();
            AddAxis(sidebar, "All Issues");
            foreach (string category in ValidationWorkspaceSettings.Categories)
                AddAxis(sidebar, category);
            AddAxis(sidebar, "Suppressed");
            VisualElement content = Element(body, "sentinel-content", "dx-grow");
            VisualElement header = Element(content, "sentinel-columns", "dx-row");
            AddLabel(header, string.Empty, "sentinel-icon");
            AddLabel(header, "Issue", "sentinel-message");
            AddLabel(header, "Object", "sentinel-object");
            AddLabel(header, "Rule", "sentinel-rule");
            _list = new ListView
            {
                name = "validation-findings",
                fixedItemHeight = 26f,
                selectionType = SelectionType.Single,
                itemsSource = _visible,
                makeItem = MakeRow,
                bindItem = BindRow,
            };
            _list.AddToClassList("dx-grow");
            content.Add(_list);

            VisualElement detail = Element(content, "sentinel-details");
            _detailTitle = AddLabel(detail, string.Empty, "sentinel-detail-title");
            _detailMessage = AddLabel(detail, string.Empty, "sentinel-detail-message");
            _detailPath = AddLabel(detail, string.Empty, "dx-link");
            VisualElement actions = Element(detail, "dx-row");
            _selectAsset = new Button(RevealSelected)
            {
                text = "Select Asset",
                name = "select-asset",
            };
            actions.Add(_selectAsset);
            _fix = new Button(FixSelected) { text = "Auto-Fix" };
            _fix.AddToClassList("dx-positive");
            actions.Add(_fix);
            _suppress = new Button(SuppressSelected)
            {
                text = "Suppress",
                name = "suppress-finding",
            };
            actions.Add(_suppress);
            actions.Add(new Button(ReloadSuppressions) { text = "Reload Suppressions" });

            VisualElement footer = Element(root, "sentinel-footer", "dx-row");
            _summary = AddLabel(footer, string.Empty, "dx-grow");
            _progress = AddLabel(footer, string.Empty, "dx-muted");
            ReloadSuppressions();
            CreateRulesView(pages);
            CreateBuilderView(pages);
            CreateSettingsView(pages);
            ShowView(_activeView);
        }

        internal VisualElement PrepareCapture(string view, bool graph = false)
        {
            CreateGUI();
            ShowView(view);
            if (view == "Builder")
                SetBuilderMode(graph);
            if (0 < _visible.Count)
                Select(0);
            return rootVisualElement;
        }

        private void AddAxis(VisualElement sidebar, string category)
        {
            Button button = new Button(() =>
            {
                _axis = category;
                Refresh();
            });
            button.AddToClassList("sentinel-axis");
            sidebar.Add(button);
            _axisButtons.Add(category, button);
        }

        private bool IsVisibleInWorkspace(ValidationFinding finding)
        {
            bool suppressed = _suppressions.IsSuppressed(in finding);
            return _enabledSeverities.Contains(finding.Severity)
                && (
                    _axis == "Suppressed"
                        ? suppressed
                        : _axis == "All Issues"
                            || ValidationWorkspaceSettings.CategoryFor(finding.AssetPath) == _axis
                );
        }

        private void RefreshNavigation()
        {
            foreach (KeyValuePair<string, Button> entry in _axisButtons)
            {
                int count = 0;
                foreach (ValidationFinding finding in _known)
                {
                    if (
                        entry.Key == "All Issues"
                        || (
                            entry.Key == "Suppressed"
                                ? _suppressions.IsSuppressed(in finding)
                                : ValidationWorkspaceSettings.CategoryFor(finding.AssetPath)
                                    == entry.Key
                        )
                    )
                        count++;
                }
                entry.Value.text = entry.Key + "  " + count;
                entry.Value.EnableInClassList("dx-selected", _axis == entry.Key);
            }
            foreach (KeyValuePair<ValidationSeverity, Button> entry in _severityButtons)
            {
                int count = 0;
                foreach (ValidationFinding finding in _known)
                    if (finding.Severity == entry.Key)
                        count++;
                entry.Value.text = entry.Key + " " + count;
                entry.Value.EnableInClassList(
                    "dx-selected",
                    _enabledSeverities.Contains(entry.Key)
                );
                entry.Value.EnableInClassList(
                    "sentinel-chip-off",
                    !_enabledSeverities.Contains(entry.Key)
                );
            }
        }

        private static VisualElement Element(VisualElement parent, params string[] classes)
        {
            VisualElement element = new VisualElement();
            foreach (string className in classes)
            {
                element.AddToClassList(className);
            }
            parent.Add(element);
            return element;
        }

        private static Label AddLabel(VisualElement parent, string text, string className)
        {
            Label label = new Label(text);
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        private VisualElement MakeRow()
        {
            FindingRow row = new FindingRow();
            row.RegisterCallback<ClickEvent>(clicked =>
            {
                Select(row.userData as int?);
                if (clicked.clickCount == 2)
                {
                    RevealSelected();
                }
            });
            return row;
        }

        private void BindRow(VisualElement element, int index)
        {
            if (!(element is FindingRow row) || index < 0 || _visible.Count <= index)
            {
                return;
            }

            ValidationFinding finding = _visible[index];
            bool suppressed = _suppressions.IsSuppressed(in finding);
            row.userData = index;
            row.Message.text = (suppressed ? "(suppressed) " : string.Empty) + finding.Message;
            row.Object.text = Path.GetFileName(finding.AssetPath);
            row.Rule.text = finding.RuleId;
            row.tooltip = finding.AssetPath + "\n" + finding.Id;
            row.Icon.text = finding.Severity == ValidationSeverity.Info ? "i" : "!";
            row.Icon.EnableInClassList("dx-error", finding.Severity == ValidationSeverity.Error);
            row.Icon.EnableInClassList(
                "dx-warning",
                finding.Severity == ValidationSeverity.Warning
            );
            row.Icon.EnableInClassList("dx-info", finding.Severity == ValidationSeverity.Info);
            row.EnableInClassList("sentinel-suppressed", suppressed);
        }

        private void RevealSelected()
        {
            if (0 <= _selected && _selected < _visible.Count)
            {
                Reveal(_visible[_selected]);
            }
        }

        private void RefreshDetails()
        {
            if (_detailTitle == null)
            {
                return;
            }

            bool selected = 0 <= _selected && _selected < _visible.Count;
            _selectAsset.SetEnabled(selected);
            _suppress.SetEnabled(selected);
            _fix.SetEnabled(selected && ValidationProjectFix.CanFix(FixRule(_visible[_selected])));
            if (!selected)
            {
                _detailTitle.text = "Select an issue to inspect it here";
                _detailMessage.text =
                    "Select Asset pings the affected object. Double-click an issue to reveal it.";
                _detailPath.text = string.Empty;
                return;
            }

            ValidationFinding finding = _visible[_selected];
            _detailTitle.text = finding.RuleId + " · " + finding.Severity;
            _detailMessage.text = finding.Message;
            _detailPath.text = finding.AssetPath;
            _suppress.text = _suppressions.IsSuppressed(in finding) ? "Restore" : "Suppress";
            ValidationWorkspaceSettings.RuleDefinition rule = FixRule(finding);
            _fix.text = ValidationProjectFix.CanFix(rule)
                ? "Auto-Fix: " + rule.fix
                : "No automatic fix";
        }

        private sealed class FindingRow : VisualElement
        {
            internal readonly Label Icon;
            internal readonly Label Message;
            internal readonly Label Object;
            internal readonly Label Rule;

            internal FindingRow()
            {
                AddToClassList("sentinel-finding");
                AddToClassList("dx-row");
                Icon = AddLabel(this, string.Empty, "sentinel-icon");
                Message = AddLabel(this, string.Empty, "sentinel-message");
                Object = AddLabel(this, string.Empty, "sentinel-object");
                Rule = AddLabel(this, string.Empty, "sentinel-rule");
            }
        }
    }
#endif
}
