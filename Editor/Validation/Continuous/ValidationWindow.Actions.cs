// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using System.Collections.Generic;
    using System.Text;
    using UnityEditor;
    using UnityEngine.UIElements;

    public sealed partial class ValidationWindow
    {
        private ValidationRun _lastCompletedRun;
        private VisualElement _toast;
        private Action _undoAction;

        internal static void OpenFinding(ValidationFinding finding, string action)
        {
            Open();
            ValidationWindow window = GetWindow<ValidationWindow>();
            if (action == "fix")
                window.FixFindings(new[] { finding });
            else if (action == "suppress")
                window.SetSuppressed(finding, true);
            else
            {
                window._axis = "All Issues";
                window._query = finding.AssetPath;
                window.Refresh();
                for (int index = 0; index < window._visible.Count; index++)
                    if (window._visible[index].Id == finding.Id)
                    {
                        window.Select(index);
                        break;
                    }
                window.ShowView("Issues");
            }
        }

        private void WorkspaceChanged()
        {
            _lastCompletedRun = null;
            Refresh();
            RefreshRules();
            RefreshSettings();
        }

        private void Say(string message, Action undo = null)
        {
            _status = message;
            _undoAction = undo;
            if (_toast == null)
                _toast = Element(rootVisualElement, "sentinel-toast", "dx-row");
            _toast.Clear();
            AddLabel(_toast, message, "dx-grow");
            if (undo != null)
                _toast.Add(new Button(() => _undoAction?.Invoke()) { text = "Undo" });
            _toast.Add(new Button(() => _toast.Clear()) { text = "×", tooltip = "Dismiss" });
        }

        internal static string WithSuppression(
            string original,
            ValidationFinding finding,
            bool suppress
        )
        {
            string text = original ?? string.Empty;
            ValidationSuppressions existing = ValidationSuppressions.Parse(text);
            if (suppress)
            {
                return existing.IsSuppressed(in finding)
                    ? text
                    : text
                        + (text.EndsWith("\n", StringComparison.Ordinal) ? string.Empty : "\n")
                        + ValidationSuppressions.Render(new[] { finding });
            }
            return WithoutSuppression(text, finding.Id);
        }

        private static string WithoutSuppression(string text, string id)
        {
            StringBuilder remaining = new StringBuilder();
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
                if (!string.Equals(line.Trim(), id, StringComparison.Ordinal))
                    remaining.Append(line).Append('\n');
            return remaining.ToString();
        }

        private void SetSuppressed(ValidationFinding finding, bool suppress)
        {
            ChangeSuppression(finding.Id, finding, suppress);
        }

        private void ChangeSuppression(string id, ValidationFinding? finding, bool suppress)
        {
            try
            {
                string previous = File.Exists(DefaultSuppressionsPath)
                    ? File.ReadAllText(DefaultSuppressionsPath)
                    : string.Empty;
                string updated = finding.HasValue
                    ? WithSuppression(previous, finding.Value, suppress)
                    : WithoutSuppression(previous, id);
                File.WriteAllText(DefaultSuppressionsPath, updated);
                ReloadSuppressions();
                RefreshSettings();
                Say(
                    suppress ? "Issue suppressed." : "Suppression restored.",
                    () =>
                    {
                        try
                        {
                            if (
                                !File.Exists(DefaultSuppressionsPath)
                                || File.ReadAllText(DefaultSuppressionsPath) != updated
                            )
                            {
                                Say(
                                    "The suppression file changed since this action. Reload it before editing again."
                                );
                                return;
                            }
                            File.WriteAllText(DefaultSuppressionsPath, previous);
                            ReloadSuppressions();
                            RefreshSettings();
                            Say("Suppression change undone.");
                        }
                        catch (Exception thrown)
                        {
                            Say("Could not undo suppression: " + thrown.Message);
                        }
                    }
                );
            }
            catch (Exception thrown)
            {
                Say("Could not save suppression: " + thrown.Message);
            }
        }

        private ValidationWorkspaceSettings.RuleDefinition FixRule(ValidationFinding finding)
        {
            foreach (
                ValidationWorkspaceSettings.RuleDefinition rule in ValidationWorkspaceSettings
                    .instance
                    .projectRules
            )
                if (rule.id == finding.RuleId)
                    return rule;
            return null;
        }

        private void FixSelected()
        {
            if (0 <= _selected && _selected < _visible.Count)
                FixFindings(new[] { _visible[_selected] });
        }

        private void FixVisible()
        {
            FixFindings(_visible.ToArray());
        }

        private void FixFindings(IEnumerable<ValidationFinding> findings)
        {
            if (ValidationScheduler.IsRunning)
            {
                Say("Wait for the active scan before applying fixes.");
                return;
            }
            List<ValidationProjectFix.Request> requests = new List<ValidationProjectFix.Request>();
            foreach (ValidationFinding finding in findings)
            {
                ValidationWorkspaceSettings.RuleDefinition rule = FixRule(finding);
                if (ValidationProjectFix.CanFix(rule) && !_suppressions.IsSuppressed(in finding))
                    requests.Add(new ValidationProjectFix.Request(rule, finding));
            }
            List<string> failures = new List<string>();
            List<Action> undo = ValidationProjectFix.ApplyMany(requests, failures);
            foreach (string failure in failures)
                UnityEngine.Debug.LogWarning("[Sentinel] " + failure);
            int failed = failures.Count;
            int targetedUndoCount = 0;
            foreach (Action restore in undo)
                if (restore != null)
                    targetedUndoCount++;
            Say(
                undo.Count
                    + " fixes applied · "
                    + failed
                    + " failed. Scene component removals use Edit > Undo.",
                targetedUndoCount == 0
                    ? null
                    : () =>
                    {
                        int restored = 0;
                        for (int index = undo.Count - 1; 0 <= index; index--)
                        {
                            try
                            {
                                if (undo[index] != null)
                                {
                                    undo[index]();
                                    restored++;
                                }
                            }
                            catch (Exception thrown)
                            {
                                UnityEngine.Debug.LogWarning(
                                    "[Sentinel] Undo failed: " + thrown.Message
                                );
                            }
                        }
                        RunOrCancel();
                        Say(restored + " fixes undone.");
                    }
            );
            if (0 < undo.Count)
                RunOrCancel();
        }

        private void ExportReport(bool junit)
        {
            if (_lastCompletedRun == null)
            {
                Say(
                    "Run Validate Project before exporting. The export includes that run's coverage and failures."
                );
                return;
            }
            string path = EditorUtility.SaveFilePanel(
                "Export validation report",
                string.Empty,
                "validation",
                junit ? "xml" : "json"
            );
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                ValidationWorkspaceSettings settings = ValidationWorkspaceSettings.instance;
                string report = junit
                    ? ValidationWorkspaceReport.ToJUnit(
                        _lastCompletedRun,
                        _suppressions,
                        settings.ActiveProfile.failOn,
                        settings.workerThreads
                    )
                    : ValidationReport.ToJson(_lastCompletedRun, _suppressions);
                File.WriteAllText(path, report);
                Say("Exported " + Path.GetFileName(path) + ".");
            }
            catch (Exception thrown)
            {
                Say("Export failed: " + thrown.Message);
            }
        }
    }
#endif
}
