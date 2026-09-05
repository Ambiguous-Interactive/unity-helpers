// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Editor.Styles;
    using Object = UnityEngine.Object;

    [InitializeOnLoad]
    internal static class ValidationStatusSurfaces
    {
        private static readonly List<ValidationFinding> Findings = new List<ValidationFinding>();
        private static string _badge = "Sentinel · not scanned";
        private static ValidationSuppressions _suppressions = ValidationSuppressions.Empty;
        internal static event Action StatusChanged;
        internal static string Badge => _badge;

        static ValidationStatusSurfaces()
        {
            ValidationResults.Changed += Changed;
            UnityEditor.Editor.finishedDefaultHeaderGUI += InspectorHeader;
            Changed();
        }

        private static void Changed()
        {
            ValidationResults.CopyInto(Findings);
            int errors = 0;
            int warnings = 0;
            foreach (ValidationFinding finding in Findings)
            {
                if (_suppressions.IsSuppressed(in finding))
                    continue;
                if (finding.Severity == ValidationSeverity.Error)
                    errors++;
                if (finding.Severity == ValidationSeverity.Warning)
                    warnings++;
            }
            _badge = ValidationResults.HasRun
                ? "Sentinel · " + errors + " ! · " + warnings + " ⚠"
                : "Sentinel · not scanned";
            StatusChanged?.Invoke();
#if UNITY_6000_3_OR_NEWER
            UnityEditor.Toolbars.MainToolbar.Refresh("Sentinel/Validation");
#endif
            SceneView.RepaintAll();
        }

        internal static void SuppressionsChanged(ValidationSuppressions suppressions)
        {
            _suppressions = suppressions ?? ValidationSuppressions.Empty;
            Changed();
        }

        internal static VisualElement CreatePanel()
        {
            VisualElement panel = new VisualElement();
            EditorTheme.Apply(panel);
            StyleSheet styles = EditorTheme.Load("ValidationWindow.uss");
            if (styles != null)
                panel.styleSheets.Add(styles);
            panel.AddToClassList("sentinel-overlay-content");
            Label badge = new Label();
            Label status = new Label();
            panel.Add(badge);
            panel.Add(status);
            panel.Add(new Button(ValidationWindow.Open) { text = "Open Sentinel →" });
            panel
                .schedule.Execute(() =>
                {
                    badge.text = Badge;
                    ValidationRun run = ValidationScheduler.Active;
                    string text =
                        run != null ? "Validating " + run.ProcessedCount + " / " + run.TotalCount
                        : ValidationResults.HasRun
                            ? ValidationResults.CheckedAssetCount + " assets checked"
                        : "Open Sentinel to run validation";
                    if (status.text != text)
                        status.text = text;
                })
                .Every(250);
            return panel;
        }

        private static void InspectorHeader(UnityEditor.Editor editor)
        {
            if (editor == null || editor.target == null)
                return;
            Object inspected = editor.target;
            GameObject gameObject = inspected is Component component
                ? component.gameObject
                : inspected as GameObject;
            string path = AssetDatabase.GetAssetPath(inspected);
            List<ValidationFinding> matches = new List<ValidationFinding>();
            foreach (ValidationFinding finding in Findings)
            {
                if (_suppressions.IsSuppressed(in finding))
                    continue;
                if (finding.TryGetTarget(out Object target))
                {
                    GameObject owner = target is Component related
                        ? related.gameObject
                        : target as GameObject;
                    if (target == inspected || gameObject != null && owner == gameObject)
                        matches.Add(finding);
                }
                else if (!string.IsNullOrEmpty(path) && finding.AssetPath == path)
                    matches.Add(finding);
            }
            if (matches.Count == 0)
                return;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "Sentinel · " + matches.Count + " issues",
                EditorStyles.boldLabel
            );
            foreach (ValidationFinding finding in matches)
            {
                EditorGUILayout.LabelField(
                    finding.Severity + ": " + finding.Message,
                    EditorStyles.wordWrappedLabel
                );
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Inspect"))
                    ValidationWindow.OpenFinding(finding, "inspect");
                if (GUILayout.Button("Fix"))
                    ValidationWindow.OpenFinding(finding, "fix");
                if (GUILayout.Button("Suppress"))
                    ValidationWindow.OpenFinding(finding, "suppress");
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }
    }
#endif
}
