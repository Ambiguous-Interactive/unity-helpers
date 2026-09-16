// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    internal static class OdinMigrationTool
    {
        private const string MenuRoot = "Tools/Wallstop Studios/Odin Migration/";

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        internal static ScanResult BuildPlans(
            IReadOnlyList<string> assetPaths,
            IOdinMigrationScanContext context
        )
        {
            ScanResult result = new ScanResult();
            if (assetPaths == null || context == null)
            {
                result.Failures.Add("The migration scan did not receive valid inputs.");
                return result;
            }

            for (int index = 0; index < assetPaths.Count; index++)
            {
                string assetPath = assetPaths[index];
                if (context.ShouldCancel(assetPath, index, assetPaths.Count))
                {
                    result.Cancelled = true;
                    break;
                }
                string fullPath;
                byte[] original;
                try
                {
                    fullPath = context.GetFullPath(assetPath);
                    original = context.ReadAllBytes(fullPath);
                }
                catch (Exception exception)
                {
                    result.Failures.Add($"{assetPath}: {exception.Message}");
                    continue;
                }

                if (
                    !OdinMigrationEncodedSource.TryDecode(
                        original,
                        out OdinMigrationDecodedSource decoded,
                        out string decodeFailure
                    )
                )
                {
                    result.Failures.Add($"{assetPath}: {decodeFailure}");
                    continue;
                }
                if (OdinMigrationSourceAnalyzer.LooksGenerated(assetPath, decoded.Source))
                {
                    result.GeneratedFiles++;
                    continue;
                }

                OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(
                    decoded.Source
                );
                result.Blockers += analysis.Blockers.Count;
                result.ManualReviews += analysis.ManualReviews.Count;
                result.AnalyzedFiles++;
                if (
                    0 < analysis.ReplacementCount
                    || 0 < analysis.Blockers.Count
                    || 0 < analysis.ManualReviews.Count
                )
                {
                    result.Analyses.Add(new FileAnalysis(assetPath, analysis));
                }
                if (analysis.ReplacementCount == 0)
                {
                    continue;
                }

                byte[] upgraded = OdinMigrationEncodedSource.Encode(
                    decoded,
                    analysis.UpgradedSource
                );
                result.Plans.Add(
                    new OdinMigrationFilePlan(assetPath, fullPath, original, upgraded, analysis)
                );
                result.Replacements += analysis.ReplacementCount;
            }
            return result;
        }

        internal static bool CanApply(ScanResult result)
        {
            return result != null
                && 0 < result.Plans.Count
                && !result.Cancelled
                && result.Failures.Count == 0;
        }

        [MenuItem(MenuRoot + "Preview Assets")]
        private static void PreviewAssets()
        {
            Run(false, false);
        }

        [MenuItem(MenuRoot + "Preview Selected Scripts")]
        private static void PreviewSelected()
        {
            Run(true, false);
        }

        [MenuItem(MenuRoot + "Apply to Assets")]
        private static void ApplyAssets()
        {
            Run(false, true);
        }

        [MenuItem(MenuRoot + "Apply to Selected Scripts")]
        private static void ApplySelected()
        {
            Run(true, true);
        }

        private static void Run(bool selectedOnly, bool apply)
        {
            List<string> assetPaths = CollectAssetPaths(selectedOnly);
            if (assetPaths.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Odin Migration",
                    selectedOnly
                        ? "Select one or more scripts or folders under Assets."
                        : "No C# scripts were found under Assets.",
                    "OK"
                );
                return;
            }

            ScanResult result;
            try
            {
                result = BuildPlans(assetPaths, new PhysicalOdinMigrationScanContext(ProjectRoot));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            string report = BuildReport(result);
            Debug.Log(report);
            if (!apply || !CanApply(result))
            {
                EditorUtility.DisplayDialog("Odin Migration Preview", Summary(result), "OK");
                return;
            }

            if (
                !EditorUtility.DisplayDialog(
                    "Apply Odin Migration",
                    Summary(result)
                        + "\n\nOnly proven inspector-attribute equivalents will change. "
                        + "Serialized-state and collection findings remain untouched.",
                    "Apply",
                    "Cancel"
                )
            )
            {
                return;
            }

            string backupRoot = Path.Combine(
                ProjectRoot,
                "Library",
                "WallstopOdinMigrationBackups",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")
            );
            if (
                !OdinMigrationFileTransaction.TryApply(
                    result.Plans,
                    backupRoot,
                    new PhysicalOdinMigrationFileStore(),
                    out string failure
                )
            )
            {
                Debug.LogError($"Odin migration failed and rollback was attempted: {failure}");
                EditorUtility.DisplayDialog("Odin Migration Failed", failure, "OK");
                return;
            }

            Debug.Log(
                $"Odin migration updated {result.Plans.Count} file(s). Backups: {backupRoot}"
            );
            try
            {
                AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Odin migration files were updated, but Asset Database refresh failed: {exception}"
                );
                EditorUtility.DisplayDialog(
                    "Odin Migration Applied",
                    $"Updated {result.Plans.Count} file(s), but refresh failed. "
                        + $"Refresh manually.\nBackups: {backupRoot}\n\n{exception.Message}",
                    "OK"
                );
                return;
            }
            EditorUtility.DisplayDialog(
                "Odin Migration Complete",
                $"Updated {result.Plans.Count} file(s).\nBackups: {backupRoot}",
                "OK"
            );
        }

        private static string BuildReport(ScanResult result)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("Odin migration analysis (no serialized-state rewrites):");
            report.AppendLine(Summary(result));
            foreach (FileAnalysis file in result.Analyses)
            {
                if (
                    file.Analysis.ReplacementCount == 0
                    && file.Analysis.Blockers.Count == 0
                    && file.Analysis.ManualReviews.Count == 0
                )
                {
                    continue;
                }
                report.AppendLine(
                    $"{file.AssetPath}: {file.Analysis.ReplacementCount} safe replacement(s)"
                );
                foreach (OdinMigrationChange change in file.Analysis.Changes)
                {
                    report.AppendLine(
                        $"  CHANGE: {file.AssetPath}:{change.Line}: {change.From} => {change.To}"
                    );
                }
                AppendFindings(report, "BLOCKER", file.AssetPath, file.Analysis.Blockers);
                AppendFindings(report, "REVIEW", file.AssetPath, file.Analysis.ManualReviews);
            }
            foreach (string failure in result.Failures)
            {
                report.AppendLine($"FAILURE: {failure}");
            }
            return report.ToString();
        }

        private static void AppendFindings(
            StringBuilder report,
            string category,
            string assetPath,
            IReadOnlyList<OdinMigrationFinding> findings
        )
        {
            foreach (OdinMigrationFinding finding in findings)
            {
                report.AppendLine($"  {category}: {assetPath}:{finding.Line}: {finding.Message}");
            }
        }

        private static string Summary(ScanResult result)
        {
            return $"Analyzed {result.AnalyzedFiles} file(s); "
                + $"{result.Replacements} safe replacement(s) in {result.Plans.Count} file(s); "
                + $"{result.Blockers} blocker(s); {result.ManualReviews} manual review item(s); "
                + $"{result.Failures.Count} failure(s); {result.GeneratedFiles} generated file(s) skipped; "
                + (result.Cancelled ? "scan cancelled." : "scan complete.");
        }

        private static List<string> CollectAssetPaths(bool selectedOnly)
        {
            SortedSet<string> paths = new SortedSet<string>(StringComparer.Ordinal);
            List<string> folders = new List<string>();
            if (selectedOnly)
            {
                foreach (Object selected in Selection.objects)
                {
                    string path = AssetDatabase.GetAssetPath(selected);
                    if (
                        !path.StartsWith("Assets/", StringComparison.Ordinal)
                        && !string.Equals(path, "Assets", StringComparison.Ordinal)
                    )
                    {
                        continue;
                    }
                    if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        paths.Add(path);
                    }
                    else if (AssetDatabase.IsValidFolder(path))
                    {
                        folders.Add(path);
                    }
                }
            }
            else
            {
                folders.Add("Assets");
            }

            if (0 < folders.Count)
            {
                string[] guids = AssetDatabase.FindAssets("t:MonoScript", folders.ToArray());
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (
                        path.StartsWith("Assets/", StringComparison.Ordinal)
                        && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        paths.Add(path);
                    }
                }
            }
            return new List<string>(paths);
        }

        internal sealed class FileAnalysis
        {
            internal readonly string AssetPath;
            internal readonly OdinMigrationAnalysis Analysis;

            internal FileAnalysis(string assetPath, OdinMigrationAnalysis analysis)
            {
                AssetPath = assetPath;
                Analysis = analysis;
            }
        }

        internal sealed class ScanResult
        {
            internal readonly List<FileAnalysis> Analyses = new List<FileAnalysis>();
            internal readonly List<string> Failures = new List<string>();
            internal readonly List<OdinMigrationFilePlan> Plans = new List<OdinMigrationFilePlan>();
            internal int AnalyzedFiles;
            internal int Blockers;
            internal bool Cancelled;
            internal int GeneratedFiles;
            internal int ManualReviews;
            internal int Replacements;
        }

        internal interface IOdinMigrationScanContext
        {
            string GetFullPath(string assetPath);

            byte[] ReadAllBytes(string fullPath);

            bool ShouldCancel(string assetPath, int index, int count);
        }

        private sealed class PhysicalOdinMigrationScanContext : IOdinMigrationScanContext
        {
            private readonly string projectRoot;

            internal PhysicalOdinMigrationScanContext(string projectRoot)
            {
                this.projectRoot = projectRoot;
            }

            public string GetFullPath(string assetPath)
            {
                return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            }

            public byte[] ReadAllBytes(string fullPath)
            {
                return File.ReadAllBytes(fullPath);
            }

            public bool ShouldCancel(string assetPath, int index, int count)
            {
                return EditorUtility.DisplayCancelableProgressBar(
                    "Odin Migration",
                    $"Analyzing {assetPath}",
                    (float)index / count
                );
            }
        }
    }
}
