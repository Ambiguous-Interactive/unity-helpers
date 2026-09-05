// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed partial class ValidationWindow
    {
        [SerializeField]
        private string _activeView = "Issues";

        [SerializeField]
        private ValidationWorkspaceSettings.RuleDefinition _draft =
            new ValidationWorkspaceSettings.RuleDefinition();
        private readonly Dictionary<string, VisualElement> _views =
            new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, Button> _tabs = new Dictionary<string, Button>();
        private string _ruleQuery = string.Empty;
        private string _ruleCategory = "All Rules";
        private ScrollView _rulesTable;
        private VisualElement _builderChecks;
        private Label _drySummary;
        private ScrollView _dryFindings;
        private Button _dryRun;
        private VisualElement _settingsContent;

        private VisualElement CreateNavigation(VisualElement root)
        {
            _views.Clear();
            _tabs.Clear();
            VisualElement navigation = Element(root, "sentinel-navigation", "dx-row");
            foreach (string name in new[] { "Issues", "Rules", "Builder", "Settings" })
            {
                Button tab = new Button(() => ShowView(name))
                {
                    text = name,
                    name = "view-" + name.ToLowerInvariant(),
                };
                navigation.Add(tab);
                _tabs.Add(name, tab);
            }
            return Element(root, "dx-grow");
        }

        private VisualElement Page(VisualElement parent, string name)
        {
            VisualElement page = Element(parent, "dx-grow");
            page.name = name.ToLowerInvariant() + "-view";
            _views.Add(name, page);
            return page;
        }

        private void ShowView(string name)
        {
            _activeView = name;
            foreach (KeyValuePair<string, VisualElement> view in _views)
                view.Value.EnableInClassList("dx-hidden", view.Key != name);
            foreach (KeyValuePair<string, Button> tab in _tabs)
                tab.Value.EnableInClassList("dx-selected", tab.Key == name);
            if (name == "Rules")
                RefreshRules();
            if (name == "Settings")
                RefreshSettings();
        }

        private void CreateRulesView(VisualElement parent)
        {
            VisualElement page = Page(parent, "Rules");
            VisualElement toolbar = Element(page, "sentinel-toolbar", "dx-row");
            TextField search = new TextField("Search rules") { value = _ruleQuery };
            search.AddToClassList("dx-grow");
            search.RegisterValueChangedCallback(changed =>
            {
                _ruleQuery = changed.newValue;
                RefreshRules();
            });
            toolbar.Add(search);
            Button create = new Button(() => ShowView("Builder")) { text = "New Rule → Builder" };
            create.AddToClassList("dx-primary");
            toolbar.Add(create);
            VisualElement body = Element(page, "sentinel-body", "dx-row", "dx-grow");
            ScrollView categories = new ScrollView();
            categories.AddToClassList("sentinel-sidebar");
            body.Add(categories);
            foreach (
                string name in new[]
                {
                    "All Rules",
                    "References & Fields",
                    "GameObjects & Scripts",
                    "Naming",
                    "Assets & Import",
                    "Settings & Build",
                    "Project Rules",
                }
            )
            {
                Button category = new Button(() =>
                {
                    _ruleCategory = name;
                    RefreshRules();
                })
                {
                    text = name,
                };
                category.AddToClassList("sentinel-axis");
                categories.Add(category);
            }
            _rulesTable = new ScrollView();
            _rulesTable.AddToClassList("dx-grow");
            body.Add(_rulesTable);
        }

        private void RefreshRules()
        {
            if (_rulesTable == null)
                return;
            _rulesTable.Clear();
            List<IValidationRule> rules = ValidationBatch.DiscoverRules(null, false);
            ValidationWorkspaceSettings settings = ValidationWorkspaceSettings.instance;
            foreach (ValidationWorkspaceSettings.RuleDefinition definition in settings.projectRules)
            {
                if (ValidationProjectRule.ValidateDefinition(definition, out string failure))
                    rules.Add(new ValidationProjectRule(definition));
                else
                    AddLabel(_rulesTable, definition.name + ": " + failure, "dx-error");
            }
            foreach (IValidationRule rule in rules)
            {
                bool authored = rule is ValidationProjectRule;
                string category = RuleCategory(rule, authored);
                if (_ruleCategory != "All Rules" && _ruleCategory != category)
                    continue;
                if (
                    !string.IsNullOrWhiteSpace(_ruleQuery)
                    && (rule.DisplayName + " " + rule.RuleId).IndexOf(
                        _ruleQuery,
                        StringComparison.OrdinalIgnoreCase
                    ) < 0
                )
                    continue;
                VisualElement row = Element(_rulesTable, "sentinel-rule-row", "dx-row");
                Toggle enabled = new Toggle
                {
                    value = settings.IsEnabled(rule.RuleId),
                    tooltip = "Enable " + rule.DisplayName,
                };
                enabled.RegisterValueChangedCallback(changed =>
                {
                    ValidationWorkspaceSettings.RulePreference old = settings.PreferenceFor(
                        rule.RuleId
                    );
                    settings.SetRulePreference(
                        rule.RuleId,
                        changed.newValue,
                        old != null && old.overrideSeverity,
                        old == null ? ValidationSeverity.Warning : old.severity
                    );
                    Refresh();
                });
                row.Add(enabled);
                AddLabel(row, rule.DisplayName, "sentinel-rule-name");
                AddLabel(row, category + " · " + rule.RuleId, "dx-grow");
                ValidationWorkspaceSettings.RulePreference preference = settings.PreferenceFor(
                    rule.RuleId
                );
                string chosen =
                    preference != null && preference.overrideSeverity
                        ? preference.severity.ToString()
                        : "Default";
                Choice(
                    row,
                    string.Empty,
                    new[] { "Default", "Error", "Warning", "Info" },
                    chosen,
                    value =>
                    {
                        ValidationSeverity severity = ValidationSeverity.Warning;
                        bool overridden = value != "Default" && Enum.TryParse(value, out severity);
                        settings.SetRulePreference(
                            rule.RuleId,
                            settings.IsEnabled(rule.RuleId),
                            overridden,
                            severity
                        );
                        Refresh();
                    }
                );
                int hits = 0;
                foreach (ValidationFinding finding in _known)
                    if (finding.RuleId == rule.RuleId)
                        hits++;
                AddLabel(row, hits + " hits", "dx-muted");
                if (authored)
                {
                    row.Add(
                        new Button(() =>
                        {
                            settings.Change(
                                "Delete validation rule",
                                () =>
                                    settings.projectRules.RemoveAll(definition =>
                                        definition.id == rule.RuleId
                                    )
                            );
                            settings.SetRulePreference(
                                rule.RuleId,
                                false,
                                false,
                                ValidationSeverity.Warning
                            );
                            RefreshRules();
                            Refresh();
                        })
                        {
                            text = "×",
                            tooltip = "Delete project rule",
                        }
                    );
                }
            }
            if (_rulesTable.childCount == 0)
                AddLabel(_rulesTable, "No rules match.", "sentinel-empty");
        }

        private static string RuleCategory(IValidationRule rule, bool authored)
        {
            if (authored)
                return "Project Rules";
            if (rule.RuleId.IndexOf("script", StringComparison.OrdinalIgnoreCase) != -1)
                return "GameObjects & Scripts";
            if (
                rule.RuleId.IndexOf("required", StringComparison.OrdinalIgnoreCase) != -1
                || rule.RuleId.IndexOf("dictionary", StringComparison.OrdinalIgnoreCase) != -1
            )
                return "References & Fields";
            if (rule.RuleId.IndexOf("name", StringComparison.OrdinalIgnoreCase) != -1)
                return "Naming";
            if (rule.RuleId.IndexOf("setting", StringComparison.OrdinalIgnoreCase) != -1)
                return "Settings & Build";
            return "Assets & Import";
        }

        private void CreateBuilderView(VisualElement parent)
        {
            VisualElement page = Page(parent, "Builder");
            VisualElement toolbar = Element(page, "sentinel-toolbar", "dx-row");
            TextField name = new TextField("Rule name") { value = _draft.name };
            name.AddToClassList("dx-grow");
            name.RegisterValueChangedCallback(changed => _draft.name = changed.newValue);
            toolbar.Add(name);
            toolbar.Add(new Button(() => SetBuilderMode(false)) { text = "Form" });
            toolbar.Add(new Button(() => SetBuilderMode(true)) { text = "Graph" });
            toolbar.Add(new Button(AddCondition) { text = "+ Condition" });
            _dryRun = new Button(DryRun) { text = "Dry Run" };
            toolbar.Add(_dryRun);
            Button save = new Button(SaveRule) { text = "Save Rule" };
            save.AddToClassList("dx-primary");
            toolbar.Add(save);
            VisualElement body = Element(page, "sentinel-body", "dx-row", "dx-grow");
            ScrollView form = new ScrollView();
            form.AddToClassList("sentinel-form");
            form.AddToClassList("dx-grow");
            body.Add(form);
            _builderSurface = form;
            _graphContent = Element(form, "sentinel-graph-content");
            _graphConnections = new ValidationGraphConnections(_graphNodes);
            _graphContent.Add(_graphConnections);
            VisualElement targetNode = BuilderNode("WHEN — TARGET", 0);
            VisualElement checksNode = BuilderNode("CHECK — ALL CONDITIONS MATCH", 1);
            VisualElement reportNode = BuilderNode("REPORT", 2);
            _targetNode = targetNode;
            _checksNode = checksNode;
            _reportNode = reportNode;
            Choice(
                targetNode,
                "Runs on",
                ValidationWorkspaceSettings.Categories,
                _draft.target,
                value => _draft.target = value
            );
            TextField path = new TextField("Path filter")
            {
                value = _draft.pathFilter,
                tooltip =
                    "Optional project-relative folder. Empty includes every asset in the category.",
            };
            path.RegisterValueChangedCallback(changed => _draft.pathFilter = changed.newValue);
            targetNode.Add(path);
            _builderChecks = Element(checksNode, "sentinel-checks");
            RefreshConditions();
            checksNode.Add(new Button(AddCondition) { text = "+ Add condition" });
            Choice(
                reportNode,
                "Severity",
                new[] { "Error", "Warning", "Info" },
                _draft.severity.ToString(),
                value =>
                {
                    if (Enum.TryParse(value, out ValidationSeverity severity))
                        _draft.severity = severity;
                }
            );
            Choice(
                reportNode,
                "Auto-fix",
                ValidationWorkspaceSettings.Fixes,
                _draft.fix,
                value => _draft.fix = value
            );
            TextField fixValue = new TextField("Fix value / name pattern")
            {
                value = _draft.fixValue,
                tooltip =
                    "Maximum texture dimension, or a name pattern containing {name}. Component removal uses the first component property in this rule.",
            };
            fixValue.RegisterValueChangedCallback(changed => _draft.fixValue = changed.newValue);
            reportNode.Add(fixValue);
            TextField message = new TextField("Message") { value = _draft.message };
            message.RegisterValueChangedCallback(changed => _draft.message = changed.newValue);
            reportNode.Add(message);
            SetBuilderMode(_graphMode);
            VisualElement preview = Element(body, "sentinel-preview");
            AddLabel(preview, "DRY RUN — SAFE PREVIEW", "sentinel-section-title");
            _drySummary = AddLabel(
                preview,
                "Preview the real matching assets. Nothing is written.",
                "sentinel-detail-message"
            );
            _dryFindings = new ScrollView();
            _dryFindings.AddToClassList("dx-grow");
            preview.Add(_dryFindings);
        }

        private void AddCondition()
        {
            _draft.checks.Add(new ValidationWorkspaceSettings.RuleCondition());
            RefreshConditions();
        }

        private void RefreshConditions()
        {
            bool graph = _graphMode;
            SetBuilderMode(false);
            _builderChecks.Clear();
            foreach (ValidationWorkspaceSettings.RuleCondition condition in _draft.checks)
            {
                VisualElement row = Element(_builderChecks, "sentinel-condition", "dx-row");
                Choice(
                    row,
                    string.Empty,
                    ValidationWorkspaceSettings.Properties,
                    condition.property,
                    value => condition.property = value
                );
                Choice(
                    row,
                    string.Empty,
                    ValidationWorkspaceSettings.Conditions,
                    condition.comparison,
                    value => condition.comparison = value
                );
                TextField value = new TextField { value = condition.value };
                value.AddToClassList("sentinel-condition-value");
                value.RegisterValueChangedCallback(changed => condition.value = changed.newValue);
                row.Add(value);
                Button remove = new Button(() =>
                {
                    _draft.checks.Remove(condition);
                    RefreshConditions();
                })
                {
                    text = "−",
                };
                remove.SetEnabled(1 < _draft.checks.Count);
                row.Add(remove);
            }
            SetBuilderMode(graph);
        }

        private bool ValidateDraft()
        {
            if (ValidationProjectRule.ValidateDefinition(_draft, out string failure))
                return true;
            _drySummary.text = failure;
            return false;
        }

        private void DryRun()
        {
            if (!ValidateDraft())
                return;
            if (ValidationScheduler.IsRunning)
            {
                _drySummary.text = "Another validation run is active. Try again when it finishes.";
                return;
            }
            ValidationProjectRule rule = new ValidationProjectRule(_draft);
            List<ValidationTarget> targets = ValidationTargets.Enumerate();
            targets.RemoveAll(target => !rule.AppliesTo(in target));
            if (targets.Count == 0)
            {
                _drySummary.text =
                    "No assets match the target and path filter; the rule has not been exercised.";
                _dryFindings.Clear();
                return;
            }
            ValidationRun run = new ValidationRun(new IValidationRule[] { rule }, targets);
            _dryFindings.Clear();
            if (
                ValidationScheduler.TryStart(
                    run,
                    ValidationWorkspaceSettings.instance.frameBudget,
                    complete =>
                    {
                        if (_dryRun == null)
                            return;
                        _dryRun.SetEnabled(true);
                        _drySummary.text = complete.IsCancelled
                            ? "Dry run cancelled."
                            : complete.Findings.Count
                                + " matches · "
                                + complete.ProcessedCount
                                + " assets checked · "
                                + complete.Failures.Count
                                + " failures";
                        foreach (ValidationFinding finding in complete.Findings)
                        {
                            Button row = new Button(() => Reveal(finding))
                            {
                                text = finding.AssetPath,
                                tooltip = finding.Message,
                            };
                            _dryFindings.Add(row);
                        }
                        foreach (ValidationRuleFailure failure in complete.Failures)
                            AddLabel(_dryFindings, failure.ToString(), "dx-error");
                    }
                )
            )
            {
                _drySummary.text = "Scanning " + _draft.target + "…";
                _dryRun.SetEnabled(false);
            }
        }

        private void SaveRule()
        {
            if (!ValidateDraft())
                return;
            ValidationWorkspaceSettings.RuleDefinition definition =
                JsonUtility.FromJson<ValidationWorkspaceSettings.RuleDefinition>(
                    JsonUtility.ToJson(_draft)
                );
            definition.id = "project." + Guid.NewGuid().ToString("N");
            ValidationWorkspaceSettings settings = ValidationWorkspaceSettings.instance;
            settings.Change("Create validation rule", () => settings.projectRules.Add(definition));
            _ruleCategory = "Project Rules";
            ShowView("Rules");
            if (!ValidationScheduler.IsRunning)
                RunOrCancel();
        }

        private void CreateSettingsView(VisualElement parent)
        {
            VisualElement page = Page(parent, "Settings");
            VisualElement body = Element(page, "sentinel-body", "dx-row", "dx-grow");
            VisualElement sidebar = Element(body, "sentinel-sidebar");
            foreach (
                ValidationWorkspaceSettings.Profile profile in ValidationWorkspaceSettings
                    .instance
                    .profiles
            )
            {
                Button button = new Button(() =>
                {
                    ValidationWorkspaceSettings settings = ValidationWorkspaceSettings.instance;
                    settings.Change(
                        "Select validation profile",
                        () => settings.selectedProfile = profile.name
                    );
                    RefreshSettings();
                })
                {
                    text = profile.name,
                };
                button.AddToClassList("sentinel-axis");
                sidebar.Add(button);
            }
            ScrollView settingsScroll = new ScrollView();
            settingsScroll.AddToClassList("dx-grow");
            settingsScroll.AddToClassList("sentinel-form");
            body.Add(settingsScroll);
            _settingsContent = settingsScroll;
        }

        private void RefreshSettings()
        {
            if (_settingsContent == null)
                return;
            _settingsContent.Clear();
            ValidationWorkspaceSettings settings = ValidationWorkspaceSettings.instance;
            ValidationWorkspaceSettings.Profile profile = settings.ActiveProfile;
            AddLabel(
                _settingsContent,
                "VALIDATION TRIGGERS — " + profile.name,
                "sentinel-section-title"
            );
            VisualElement triggerHeader = Element(_settingsContent, "sentinel-rule-row", "dx-row");
            AddLabel(triggerHeader, "Asset category", "sentinel-trigger-label");
            foreach (string triggerName in new[] { "On change", "On save", "Manual" })
                AddLabel(triggerHeader, triggerName, "sentinel-trigger");
            for (int index = 0; index < ValidationWorkspaceSettings.Categories.Length; index++)
            {
                int categoryIndex = index;
                VisualElement row = Element(_settingsContent, "sentinel-rule-row", "dx-row");
                AddLabel(
                    row,
                    ValidationWorkspaceSettings.Categories[index],
                    "sentinel-trigger-label"
                );
                for (int mode = 0; mode < 3; mode++)
                {
                    int choice = mode;
                    Button toggle = new Button(() =>
                        settings.Change(
                            "Set validation trigger",
                            () => profile.triggers[categoryIndex] = choice
                        )
                    )
                    {
                        text = profile.triggers[index] == mode ? "●" : "○",
                        tooltip =
                            ValidationWorkspaceSettings.Categories[index]
                            + ": "
                            + new[] { "On change", "On save", "Manual" }[mode],
                    };
                    toggle.AddToClassList("sentinel-trigger");
                    toggle.EnableInClassList("dx-selected", profile.triggers[index] == mode);
                    row.Add(toggle);
                }
            }
            AddLabel(_settingsContent, "PERFORMANCE", "sentinel-section-title");
            IntegerField budget = new IntegerField("Frame budget (ms)")
            {
                value = settings.frameBudget,
                isDelayed = true,
            };
            budget.RegisterValueChangedCallback(changed =>
                settings.Change(
                    "Set validation frame budget",
                    () => settings.frameBudget = changed.newValue
                )
            );
            _settingsContent.Add(budget);
            IntegerField workers = new IntegerField("Report worker threads")
            {
                value = settings.workerThreads,
                isDelayed = true,
            };
            workers.RegisterValueChangedCallback(changed =>
                settings.Change(
                    "Set validation workers",
                    () => settings.workerThreads = changed.newValue
                )
            );
            _settingsContent.Add(workers);
            AddLabel(
                _settingsContent,
                "Unity object validation runs on the editor thread; report workers prepare independent JUnit entries.",
                "sentinel-detail-message"
            );
            AddLabel(_settingsContent, "CONTINUOUS INTEGRATION", "sentinel-section-title");
            Toggle gate = new Toggle("Gate builds on validation") { value = profile.gateBuild };
            gate.RegisterValueChangedCallback(changed =>
                settings.Change(
                    "Set validation build gate",
                    () => profile.gateBuild = changed.newValue
                )
            );
            _settingsContent.Add(gate);
            Choice(
                _settingsContent,
                "Fail build on",
                new[] { "Error", "Warning" },
                profile.failOn.ToString(),
                value =>
                    settings.Change(
                        "Set validation failure threshold",
                        () =>
                        {
                            if (Enum.TryParse(value, out ValidationSeverity severity))
                                profile.failOn = severity;
                        }
                    )
            );
            VisualElement exports = Element(_settingsContent, "dx-row");
            exports.Add(new Button(() => ExportReport(false)) { text = "Export JSON" });
            exports.Add(new Button(() => ExportReport(true)) { text = "Export JUnit" });
            AddLabel(_settingsContent, "SUPPRESSIONS", "sentinel-section-title");
            if (_suppressions.Count == 0)
                AddLabel(_settingsContent, "Nothing suppressed.", "dx-muted");
            for (int index = 0; index < _suppressions.Ids.Count; index++)
            {
                string id = _suppressions.Ids[index];
                ValidationFinding finding = default;
                foreach (ValidationFinding known in _known)
                    if (known.Id == id)
                    {
                        finding = known;
                        break;
                    }
                VisualElement row = Element(_settingsContent, "sentinel-rule-row", "dx-row");
                AddLabel(
                    row,
                    string.IsNullOrEmpty(finding.RuleId) ? id : finding.Message,
                    "dx-grow"
                );
                ValidationFinding chosen = finding;
                row.Add(
                    new Button(() =>
                        ChangeSuppression(
                            id,
                            string.IsNullOrEmpty(chosen.RuleId) ? (ValidationFinding?)null : chosen,
                            false
                        )
                    )
                    {
                        text = "Restore",
                    }
                );
            }
        }

        private static DropdownField Choice(
            VisualElement parent,
            string label,
            IReadOnlyList<string> choices,
            string current,
            Action<string> changed
        )
        {
            List<string> values = new List<string>(choices.Count);
            for (int index = 0; index < choices.Count; index++)
                values.Add(choices[index]);
            int selected = values.IndexOf(current);
            DropdownField field = new DropdownField(label, values, Math.Max(0, selected));
            field.RegisterValueChangedCallback(value => changed(value.newValue));
            parent.Add(field);
            return field;
        }
    }
#endif
}
