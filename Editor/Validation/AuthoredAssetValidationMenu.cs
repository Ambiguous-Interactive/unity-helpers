// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using System.Text;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Runs the authored-asset checks over a project from the menu, and prints what they found.
    /// </summary>
    /// <remarks>
    /// Reporting is never destructive: every command here reads, and none of them opens a scene, so
    /// running one cannot dirty the project it is measuring.
    /// </remarks>
    public static class AuthoredAssetValidationMenu
    {
        private const string MenuRoot = "Tools/Wallstop Studios/Unity Helpers/Authored Assets/";

        private const string ProjectAssetRoot = "Assets";

        /// <summary>
        /// Reports every type that cannot be authored, and every script asset that misnames what it
        /// binds, under <c>Assets/</c>.
        /// </summary>
        [MenuItem(MenuRoot + "Report Script Bindings", priority = 120)]
        public static void ReportScriptBindings()
        {
            List<MonoScriptBindingFinding> findings = new();
            MonoScriptIndex.ClearCaches();
            if (
                !MonoScriptBindingValidator.TryScan(
                    new[] { ProjectAssetRoot + "/" },
                    findings,
                    out int typesConsidered,
                    out int scriptsConsidered
                )
            )
            {
                Debug.LogWarning("[Unity Helpers] The script binding scan could not run.");
                return;
            }

            Report(
                "script bindings",
                $"{typesConsidered} concrete types and {scriptsConsidered} script assets",
                findings
            );
        }

        /// <summary>
        /// Reports every required slot an author left empty in the assets under <c>Assets/</c>.
        /// </summary>
        [MenuItem(MenuRoot + "Report Unfilled Required Fields", priority = 121)]
        public static void ReportUnfilledRequiredFields()
        {
            List<AuthoredRequirementFinding> findings = new();
            List<AuthoredRequirementExemption> exemptions = new();
            MonoScriptIndex.ClearCaches();
            if (
                !AuthoredRequirementValidator.TryScan(
                    AuthoredAssetYaml.EnumerateAuthoredAssets(ProjectAssetRoot),
                    findings,
                    exemptions,
                    out int documentsInspected
                )
            )
            {
                Debug.LogWarning("[Unity Helpers] The required field scan could not run.");
                return;
            }

            StringBuilder budget = new();
            budget.Append(documentsInspected).Append(" documents carried an annotated type");
            if (0 < exemptions.Count)
            {
                budget
                    .Append("; ")
                    .Append(exemptions.Count)
                    .Append(" annotated fields could not be read from any asset:");
                for (int index = 0; index < exemptions.Count; ++index)
                {
                    budget.AppendLine().Append("    ").Append(exemptions[index]);
                }
            }

            Report("unfilled required fields", budget.ToString(), findings);
        }

        /// <summary>
        /// Reports every authored dictionary under <c>Assets/</c> that lost its key-value pairing.
        /// </summary>
        [MenuItem(MenuRoot + "Report Broken Serializable Dictionaries", priority = 122)]
        public static void ReportBrokenSerializableDictionaries()
        {
            List<SerializableDictionaryAssetFinding> findings = new();
            if (
                !SerializableDictionaryAssetValidator.TryScan(
                    AuthoredAssetYaml.EnumerateAuthoredAssets(ProjectAssetRoot),
                    findings,
                    out int dictionariesInspected
                )
            )
            {
                Debug.LogWarning("[Unity Helpers] The dictionary scan could not run.");
                return;
            }

            Report(
                "authored dictionaries",
                $"{dictionariesInspected} dictionaries inspected",
                findings
            );
        }

        /// <summary>
        /// Reports every animation keyframe under <c>Assets/</c> whose object no longer resolves.
        /// </summary>
        [MenuItem(MenuRoot + "Report Empty Animation Keyframes", priority = 123)]
        public static void ReportEmptyAnimationKeyframes()
        {
            List<AnimationKeyframeFinding> findings = new();
            if (
                !AnimationClipKeyframeValidator.TryScan(
                    new[] { ProjectAssetRoot + "/" },
                    findings,
                    out int clipsInspected,
                    out int keyframesInspected
                )
            )
            {
                Debug.LogWarning("[Unity Helpers] The animation keyframe scan could not run.");
                return;
            }

            Report(
                "animation keyframes",
                $"{clipsInspected} clips and {keyframesInspected} object keyframes inspected",
                findings
            );
        }

        private static void Report<T>(string subject, string budget, IReadOnlyList<T> findings)
        {
            StringBuilder message = new();
            message
                .Append("[Unity Helpers] ")
                .Append(subject)
                .Append(": ")
                .Append(findings.Count)
                .Append(findings.Count == 1 ? " finding across " : " findings across ")
                .Append(budget);

            for (int index = 0; index < findings.Count; ++index)
            {
                message.AppendLine().Append("  ").Append(findings[index]);
            }

            if (findings.Count <= 0)
            {
                Debug.Log(message.ToString());
                return;
            }

            Debug.LogWarning(message.ToString());
        }
    }
#endif
}
