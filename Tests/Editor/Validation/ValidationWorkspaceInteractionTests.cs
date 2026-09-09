// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// UNH-SUPPRESS UNH003: disposable authorization must precede CommonTestBase setup mutations.

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Editor.Validation.Continuous;

    /// <summary>Executes real validation UI callbacks only in a marked disposable batchmode project.</summary>
    [TestFixture]
    [Category("Integration")]
    [Explicit(
        "Requires an explicitly marked disposable Unity project; changes project settings and Undo."
    )]
    public sealed class ValidationWorkspaceInteractionTests
    {
        private static void VerifyStatusPreferences(ValidationWorkspaceSettings settings)
        {
            Assert.IsFalse(ValidationResults.HasRun);
            const string ruleId = "project.status.probe";
            const string assetGuid = "00000000000000000000000000000001";
            ValidationFinding finding = new ValidationFinding(
                ruleId,
                ValidationSeverity.Error,
                null,
                assetGuid,
                "Assets/StatusProbe.asset",
                "field",
                "Status probe"
            );
            ValidationStatusSurfaces.SuppressionsChanged(ValidationSuppressions.Empty);
            try
            {
                ValidationResults.Replace(assetGuid, new[] { finding });
                Assert.AreEqual("Sentinel · 1 ! · 0 ⚠", ValidationStatusSurfaces.Badge);
                foreach (
                    (
                        bool enabled,
                        bool overridden,
                        ValidationSeverity severity,
                        string badge
                    ) in new[]
                    {
                        (false, false, ValidationSeverity.Error, "Sentinel · 0 ! · 0 ⚠"),
                        (true, true, ValidationSeverity.Warning, "Sentinel · 0 ! · 1 ⚠"),
                        (true, true, ValidationSeverity.Info, "Sentinel · 0 ! · 0 ⚠"),
                        (true, false, ValidationSeverity.Info, "Sentinel · 1 ! · 0 ⚠"),
                        (false, true, ValidationSeverity.Warning, "Sentinel · 0 ! · 0 ⚠"),
                        (true, true, ValidationSeverity.Error, "Sentinel · 1 ! · 0 ⚠"),
                    }
                )
                {
                    settings.SetRulePreference(ruleId, enabled, overridden, severity);
                    Assert.AreEqual(badge, ValidationStatusSurfaces.Badge);
                    Assert.AreEqual(
                        ValidationSeverity.Error,
                        ValidationResults.Snapshot()[0].Severity
                    );
                }
                ValidationStatusSurfaces.SuppressionsChanged(
                    ValidationSuppressions.Parse(finding.Id)
                );
                Assert.AreEqual("Sentinel · 0 ! · 0 ⚠", ValidationStatusSurfaces.Badge);
                settings.SetRulePreference(ruleId, true, true, ValidationSeverity.Warning);
                Assert.AreEqual("Sentinel · 0 ! · 0 ⚠", ValidationStatusSurfaces.Badge);
                ValidationStatusSurfaces.SuppressionsChanged(ValidationSuppressions.Empty);
                Assert.AreEqual("Sentinel · 0 ! · 1 ⚠", ValidationStatusSurfaces.Badge);
                ValidationWorkspaceSettings.RulePreference preference = settings.PreferenceFor(
                    ruleId
                );
                preference.overrideSeverity = false;
                settings.SaveAfterUndo();
                Assert.AreEqual("Sentinel · 1 ! · 0 ⚠", ValidationStatusSurfaces.Badge);
            }
            finally
            {
                ValidationStatusSurfaces.SuppressionsChanged(ValidationSuppressions.Empty);
                ValidationResults.Clear();
            }
        }

        private static T Field<T>(VisualElement root, string label)
            where T : VisualElement
        {
            return root.Query<T>()
                .ToList()
                .Single(element =>
                    element is TextField text && text.label == label
                    || element is DropdownField choice && choice.label == label
                    || element is IntegerField integer && integer.label == label
                    || element is Toggle toggle && toggle.label == label
                );
        }

        private static Button ButtonWithText(VisualElement root, string label)
        {
            return root.Query<Button>().ToList().Single(button => button.text == label);
        }

        private static void Submit(Button button)
        {
            Assert.IsTrue(button.panel != null);
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        private static void RequireDisposableProject()
        {
            Assert.IsTrue(Application.isBatchMode, "Interactive Unity projects are forbidden.");
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string token = Environment.GetEnvironmentVariable(
                "WALLSTOP_SENTINEL_INTERACTION_TOKEN"
            );
            string expected = Environment.GetEnvironmentVariable(
                "WALLSTOP_SENTINEL_INTERACTION_PROJECT"
            );
            Assert.IsTrue(
                Guid.TryParseExact(token, "N", out _),
                "Missing disposable-project token."
            );
            Assert.AreEqual(
                project,
                expected,
                "This probe is forbidden outside its explicitly provisioned disposable project."
            );
            Assert.IsTrue(
                Path.GetFileName(project)
                    .StartsWith("sentinel-interaction-", StringComparison.Ordinal)
            );
            Assert.AreEqual(
                token,
                File.ReadAllText(Path.Combine(project, ".sentinel-interaction-disposable")).Trim()
            );
        }

        /// <summary>Retains the builder draft across modes and persists settings through native callbacks.</summary>
        [Test]
        public void NativePanelCallbacksRetainDraftAndPersistSettings()
        {
            RequireDisposableProject();
            Assert.IsFalse(ValidationScheduler.IsRunning);
            Assert.AreEqual(0, ValidationAutoRun.PendingCount);
            ValidationWorkspaceSettings settings = ValidationWorkspaceSettings.instance;
            string originalJson = EditorJsonUtility.ToJson(settings);
            string settingsPath = "ProjectSettings/UnityHelpersValidation.asset";
            byte[] originalFile = File.Exists(settingsPath)
                ? File.ReadAllBytes(settingsPath)
                : null;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            bool sentinelEnabled = ValidationPreferences.Enabled;
            ValidationRun blocker = null;
            PanelSettings panel = null;
            GameObject panelObject = null;
            ValidationWindow window = null;
            try
            {
                ValidationPreferences.Enabled = true;
                settings.selectedProfile = "Probe A";
                settings.profiles = new List<ValidationWorkspaceSettings.Profile>
                {
                    new ValidationWorkspaceSettings.Profile { name = "Probe A" },
                    new ValidationWorkspaceSettings.Profile { name = "Probe B" },
                };
                settings.projectRules = new List<ValidationWorkspaceSettings.RuleDefinition>();
                settings.rulePreferences = new List<ValidationWorkspaceSettings.RulePreference>();
                settings.Normalize();
                VerifyStatusPreferences(settings);
                panel = ScriptableObject.CreateInstance<PanelSettings>(); // UNH-SUPPRESS UNH002: owned by the guarded fixture and destroyed in finally.
                panelObject = new GameObject("Sentinel interaction panel"); // UNH-SUPPRESS UNH002: owned by the guarded fixture and destroyed in finally.
                UIDocument document = panelObject.AddComponent<UIDocument>();
                document.panelSettings = panel;
                Assert.IsTrue(document.rootVisualElement != null);
                window = ScriptableObject.CreateInstance<ValidationWindow>(); // UNH-SUPPRESS UNH002: owned by the guarded fixture and destroyed in finally.
                VisualElement root = window.PrepareCapture("Builder");
                document.rootVisualElement.Add(root);
                Assert.IsTrue(
                    root.panel != null,
                    "No native runtime panel: callback acceptance was not executed."
                );
                TextField control = new TextField();
                int delivered = 0;
                control.RegisterValueChangedCallback(_ => delivered++);
                root.Add(control);
                control.value = "native event control";
                Assert.AreEqual(
                    1,
                    delivered,
                    "The native panel must dispatch real change callbacks."
                );
                root.Remove(control);

                VisualElement builder = root.Q("builder-view");
                Field<TextField>(builder, "Rule name").value = "Retained probe rule";
                Field<TextField>(builder, "Path filter").value = "Assets/NoScanProbe";
                Field<DropdownField>(builder, "Auto-fix").value = "None (report only)";
                Field<TextField>(builder, "Message").value = "Form message";
                TextField condition = builder.Q<TextField>(className: "sentinel-condition-value");
                condition.value = "0.75";
                Submit(ButtonWithText(builder, "Graph"));
                Assert.IsTrue(
                    builder.Q(className: "sentinel-form").ClassListContains("sentinel-graph")
                );
                Assert.AreSame(
                    condition,
                    builder.Q<TextField>(className: "sentinel-condition-value")
                );
                condition.value = "0.9";
                Field<TextField>(builder, "Message").value = "Graph message";
                Submit(ButtonWithText(builder, "Form"));
                Assert.IsFalse(
                    builder.Q(className: "sentinel-form").ClassListContains("sentinel-graph")
                );
                Assert.AreSame(root, window.PrepareCapture("Builder"));
                Assert.IsTrue(root.panel != null, "Rebuilding must retain the native panel.");
                builder = root.Q("builder-view");
                Assert.AreEqual(
                    "Retained probe rule",
                    Field<TextField>(builder, "Rule name").value
                );
                Assert.AreEqual("Graph message", Field<TextField>(builder, "Message").value);
                Assert.AreEqual(
                    "0.9",
                    builder.Q<TextField>(className: "sentinel-condition-value").value
                );

                blocker = new ValidationRun(
                    new IValidationRule[0],
                    new[]
                    {
                        new ValidationTarget(
                            "probe",
                            "Assets/NoScanProbe.asset",
                            typeof(ScriptableObject)
                        ),
                    },
                    _ =>
                        throw new InvalidOperationException(
                            "The acceptance probe must not scan assets."
                        )
                );
                Assert.IsTrue(ValidationScheduler.TryStart(blocker));
                Submit(ButtonWithText(builder, "Save Rule"));
                Assert.AreSame(blocker, ValidationScheduler.Active);
                Assert.AreEqual(0, blocker.ProcessedCount);
                Assert.AreEqual(1, settings.projectRules.Count);
                Assert.AreEqual("0.9", settings.projectRules[0].checks[0].value);
                Assert.AreEqual("Graph message", settings.projectRules[0].message);
                ValidationWorkspaceSettings.RuleDefinition roundTrip =
                    JsonUtility.FromJson<ValidationWorkspaceSettings.RuleDefinition>(
                        JsonUtility.ToJson(settings.projectRules[0])
                    );
                Assert.AreEqual("Retained probe rule", roundTrip.name);
                Assert.AreEqual("0.9", roundTrip.checks[0].value);

                Assert.AreSame(root, window.PrepareCapture("Settings"));
                Assert.IsTrue(root.panel != null, "Changing views must retain the native panel.");
                VisualElement settingsView = root.Q("settings-view");
                Submit(ButtonWithText(settingsView, "Probe B"));
                Assert.AreEqual("Probe B", settings.selectedProfile);
                Field<IntegerField>(settingsView, "Frame budget (ms)").value = 0;
                Assert.AreEqual(1, settings.frameBudget);
                Field<IntegerField>(settingsView, "Frame budget (ms)").value = 17;
                Field<IntegerField>(settingsView, "Report worker threads").value = 999;
                Assert.AreEqual(32, settings.workerThreads);
                Button trigger = settingsView
                    .Query<Button>()
                    .ToList()
                    .Single(button => button.tooltip == "Prefabs: On save");
                Submit(trigger);
                Assert.AreEqual(1, settings.ActiveProfile.triggers[0]);
                Assert.AreEqual(0, settings.profiles[0].triggers[0]);
                Field<Toggle>(settingsView, "Gate builds on validation").value = true;
                Field<DropdownField>(settingsView, "Fail build on").value = "Warning";
                Assert.IsTrue(settings.ActiveProfile.gateBuild);
                Assert.AreEqual(ValidationSeverity.Warning, settings.ActiveProfile.failOn);
                string saved = File.ReadAllText(settingsPath);
                StringAssert.Contains("selectedProfile: Probe B", saved);
                StringAssert.Contains("frameBudget: 17", saved);
                StringAssert.Contains("workerThreads: 32", saved);
                StringAssert.Contains("Retained probe rule", saved);
                Assert.AreSame(blocker, ValidationScheduler.Active);
                Assert.AreEqual(0, blocker.ProcessedCount);
            }
            finally
            {
                ValidationPreferences.Enabled = sentinelEnabled;
                try
                {
                    if (blocker != null && ReferenceEquals(ValidationScheduler.Active, blocker))
                    {
                        ValidationScheduler.Stop();
                    }
                }
                finally
                {
                    try
                    {
                        try
                        {
                            Undo.FlushUndoRecordObjects();
                            Undo.RevertAllDownToGroup(undoGroup);
                        }
                        finally
                        {
                            try
                            {
                                EditorJsonUtility.FromJsonOverwrite(originalJson, settings);
                            }
                            finally
                            {
                                if (originalFile == null)
                                {
                                    File.Delete(settingsPath);
                                }
                                else
                                {
                                    File.WriteAllBytes(settingsPath, originalFile);
                                }
                            }
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (window != null)
                            {
                                UnityEngine.Object.DestroyImmediate(window); // UNH-SUPPRESS UNH001: guarded fixture owns this native object.
                            }
                        }
                        finally
                        {
                            try
                            {
                                if (panelObject != null)
                                {
                                    UnityEngine.Object.DestroyImmediate(panelObject); // UNH-SUPPRESS UNH001: guarded fixture owns this native object.
                                }
                            }
                            finally
                            {
                                if (panel != null)
                                {
                                    UnityEngine.Object.DestroyImmediate(panel); // UNH-SUPPRESS UNH001: guarded fixture owns this native object.
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
