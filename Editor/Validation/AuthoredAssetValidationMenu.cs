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

        private const string ProjectAssetRoot = AuthoredAssetPaths.AssetsFolder;

        /// <summary>
        /// Reports every type that cannot be authored, and every script asset that misnames what it
        /// binds, under <c>Assets/</c>.
        /// </summary>
        [MenuItem(MenuRoot + "Report Script Bindings", priority = 120)]
        public static void ReportScriptBindings()
        {
            List<MonoScriptBindingFinding> findings = new();
            List<string> unreadable = new();
            MonoScriptIndex.ClearCaches();
            if (
                !MonoScriptBindingValidator.TryScan(
                    new[] { ProjectAssetRoot + "/" },
                    findings,
                    unreadable,
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
                unreadable,
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
            List<string> unreadable = new();
            MonoScriptIndex.ClearCaches();
            if (
                !AuthoredRequirementValidator.TryScan(
                    AuthoredAssetPaths.AuthoredAssetsUnderProjectRoot(),
                    findings,
                    exemptions,
                    unreadable,
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

            Report("unfilled required fields", budget.ToString(), unreadable, findings);
        }

        /// <summary>
        /// Reports every authored dictionary under <c>Assets/</c> that lost its key-value pairing.
        /// </summary>
        [MenuItem(MenuRoot + "Report Broken Serializable Dictionaries", priority = 122)]
        public static void ReportBrokenSerializableDictionaries()
        {
            List<SerializableDictionaryAssetFinding> findings = new();
            List<string> unreadable = new();
            if (
                !SerializableDictionaryAssetValidator.TryScan(
                    AuthoredAssetPaths.AuthoredAssetsUnderProjectRoot(),
                    findings,
                    unreadable,
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
                unreadable,
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
            List<string> unreadable = new();
            if (
                !AnimationClipKeyframeValidator.TryScan(
                    new[] { ProjectAssetRoot + "/" },
                    findings,
                    unreadable,
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
                unreadable,
                findings
            );
        }

        /// <summary>Builds the line a command logs, so the wording and the severity are testable.</summary>
        /// <param name="subject">What was scanned, for the first line.</param>
        /// <param name="budget">What the scan judged, so a vacuous pass is visible.</param>
        /// <param name="unreadable">The asset paths the scan could not read.</param>
        /// <param name="findings">The defects found.</param>
        /// <returns>The message, and whether it is a warning.</returns>
        internal static (string Message, bool Warn) Compose<T>(
            string subject,
            string budget,
            IReadOnlyList<string> unreadable,
            IReadOnlyList<T> findings
        )
        {
            StringBuilder message = new();
            message
                .Append("[Unity Helpers] ")
                .Append(subject)
                .Append(": ")
                .Append(findings.Count)
                .Append(findings.Count == 1 ? " finding across " : " findings across ")
                .Append(budget);

            UnreadableAssetPaths.Append(message, unreadable);

            for (int index = 0; index < findings.Count; ++index)
            {
                message.AppendLine().Append("  ").Append(findings[index]);
            }

            return (message.ToString(), 0 < findings.Count);
        }

        /// <remarks>
        /// The unreadable set is printed the way the exemption budget is, and it always prints --
        /// but it does not raise the severity on its own. A warning claims there is something to
        /// fix, and the commonest unreadable file is not: Unity writes <c>LightingData.asset</c> as
        /// binary whatever the serialization mode says, measured 2026-09-01 on two of two under
        /// <c>ForceText</c>, so any project with baked lighting would warn on every run forever. A
        /// gate that wants to fail on a coverage hole asserts the list, which is what the scan
        /// returns it for.
        /// </remarks>
        private static void Report<T>(
            string subject,
            string budget,
            IReadOnlyList<string> unreadable,
            IReadOnlyList<T> findings
        )
        {
            (string message, bool warn) = Compose(subject, budget, unreadable, findings);
            if (warn)
            {
                Debug.LogWarning(message);
                return;
            }

            Debug.Log(message);
        }
    }
#endif
}
