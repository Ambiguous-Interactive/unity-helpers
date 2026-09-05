// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Text;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Reports and, separately, repairs serialized keys no field claims.
    /// </summary>
    public static class StaleSerializedKeyMenu
    {
        private const string MenuRoot = "Tools/Wallstop Studios/Unity Helpers/Authored Assets/";

        /// <summary>Reports every stale key under <c>Assets/</c>.</summary>
        [MenuItem(MenuRoot + "Report Stale Serialized Keys", priority = 140)]
        public static void ReportProject()
        {
            Report(AuthoredAssetPaths.AuthoredAssetsUnderProjectRoot());
        }

        /// <summary>Reports every stale key in the selected assets.</summary>
        [MenuItem(MenuRoot + "Report Stale Serialized Keys In Selection", priority = 141)]
        public static void ReportSelection()
        {
            Report(SelectedAssetPaths());
        }

        /// <summary>
        /// Rewrites the selected assets so Unity drops every key no field claims, undoing any
        /// rewrite that loses content.
        /// </summary>
        [MenuItem(MenuRoot + "Repair Stale Serialized Keys In Selection", priority = 142)]
        public static void RepairSelection()
        {
            IReadOnlyList<string> paths = SelectedAssetPaths();
            if (paths.Count <= 0)
            {
                Debug.LogWarning(
                    "[Unity Helpers] Select the assets to repair. Repair rewrites files, so it never "
                        + "runs over a whole project from a menu."
                );
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Repair Stale Serialized Keys",
                $"{paths.Count} asset(s) will be rewritten from what Unity loads. Every key no field "
                    + "claims is dropped, every new field is written at its default, and every "
                    + "FormerlySerializedAs alias is migrated. Any rewrite that comes back with "
                    + "fewer objects than it went in with is undone.\n\nCommit or stash first.",
                "Rewrite",
                "Cancel"
            );

            if (!confirmed)
            {
                return;
            }

            Dictionary<string, StaleSerializedKeyRepairOutcome> outcomes = new();
            if (!StaleSerializedKeyRepair.TryRepair(paths, outcomes))
            {
                Debug.LogWarning("[Unity Helpers] The repair could not run.");
                return;
            }

            StringBuilder message = new();
            message.Append("[Unity Helpers] stale serialized keys: repaired ");
            int repaired = 0;
            int refused = 0;
            foreach (KeyValuePair<string, StaleSerializedKeyRepairOutcome> outcome in outcomes)
            {
                if (outcome.Value == StaleSerializedKeyRepairOutcome.Repaired)
                {
                    ++repaired;
                    continue;
                }

                if (outcome.Value != StaleSerializedKeyRepairOutcome.NotRewritten)
                {
                    ++refused;
                }
            }

            message.Append(repaired).Append(" of ").Append(outcomes.Count).Append(" asset(s)");
            foreach (KeyValuePair<string, StaleSerializedKeyRepairOutcome> outcome in outcomes)
            {
                if (outcome.Value == StaleSerializedKeyRepairOutcome.Repaired)
                {
                    continue;
                }

                message
                    .AppendLine()
                    .Append("  ")
                    .Append(outcome.Value)
                    .Append(": ")
                    .Append(outcome.Key);
            }

            if (0 < refused)
            {
                Debug.LogWarning(message.ToString());
                return;
            }

            Debug.Log(message.ToString());
        }

        private static void Report(IReadOnlyList<string> paths)
        {
            List<StaleSerializedKeyFinding> findings = new();
            List<string> unreadable = new();
            MonoScriptIndex.ClearCaches();
            if (
                !StaleSerializedKeyValidator.TryScan(
                    paths,
                    findings,
                    unreadable,
                    out int unresolvedScripts
                )
            )
            {
                Debug.LogWarning("[Unity Helpers] The stale key scan could not run.");
                return;
            }

            IReadOnlyDictionary<string, int> causes = StaleSerializedKeyValidator.CausesOf(
                findings
            );
            StringBuilder message = new();
            message
                .Append("[Unity Helpers] stale serialized keys: ")
                .Append(findings.Count)
                .Append(" site(s) from ")
                .Append(causes.Count)
                .Append(" cause(s) across ")
                .Append(paths.Count)
                .Append(" asset(s); ")
                .Append(unresolvedScripts)
                .Append(" document(s) named a script that resolves to nothing and were not judged");

            UnreadableAssetPaths.Append(message, unreadable);

            foreach (KeyValuePair<string, int> cause in causes)
            {
                message
                    .AppendLine()
                    .Append("  ")
                    .Append(cause.Key)
                    .Append(" (")
                    .Append(cause.Value)
                    .Append(" site(s))");
            }

            // Unreadability alone does not raise severity; see AuthoredAssetValidationMenu.Report.
            if (findings.Count <= 0)
            {
                Debug.Log(message.ToString());
                return;
            }

            Debug.LogWarning(message.ToString());
        }

        private static IReadOnlyList<string> SelectedAssetPaths()
        {
            List<string> paths = new();
            foreach (Object selected in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (string.IsNullOrEmpty(path) || paths.Contains(path))
                {
                    continue;
                }

                for (int index = 0; index < AuthoredAssetYaml.AuthoredExtensions.Count; ++index)
                {
                    if (
                        !path.EndsWith(
                            AuthoredAssetYaml.AuthoredExtensions[index],
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        continue;
                    }

                    paths.Add(path);
                    break;
                }
            }

            return paths;
        }
    }
#endif
}
