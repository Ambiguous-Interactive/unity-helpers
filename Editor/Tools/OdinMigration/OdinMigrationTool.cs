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
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using Object = UnityEngine.Object;

    /// <summary>Previews and applies conservative Odin source migrations in the current Unity project.</summary>
    public static class OdinMigrationTool
    {
        private const string MenuRoot = "Tools/Wallstop Studios/Unity Helpers/Odin Migration/";

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        /// <summary>Analyzes source without reading files or displaying editor UI.</summary>
        public static OdinMigrationAnalysis AnalyzeSource(string source)
        {
            try
            {
                return OdinMigrationSourceAnalyzer.Analyze(source);
            }
            catch (Exception exception)
            {
                return OdinMigrationAnalysis.Invalid(exception.Message);
            }
        }

        /// <summary>Reads immutable Odin Nodes records from Unity text YAML without loading assets.</summary>
        /// <remarks>This reads records, not typed objects; conversion and prefab override validation remain separate.</remarks>
        /// <param name="source">The complete Unity text YAML asset.</param>
        /// <param name="payloads">Receives complete payload snapshots, or an empty list on failure.</param>
        /// <param name="failure">Receives the unsupported-format or parsing failure.</param>
        /// <returns>True when the complete asset was read; check the payload count separately.</returns>
        public static bool TryReadSerializedNodes(
            string source,
            out IReadOnlyList<OdinMigrationSerializedData> payloads,
            out string failure
        )
        {
            return OdinMigrationNodeReader.TryRead(source, out payloads, out failure);
        }

        /// <summary>Converts an enum-to-object-list dictionary payload into Unity Helpers field YAML.</summary>
        /// <remarks>This produces a fragment; source changes, reference type checks, and paired apply remain separate.</remarks>
        /// <typeparam name="TKey">The recorded int-backed enum key.</typeparam>
        /// <typeparam name="TObject">The recorded Unity object element type.</typeparam>
        /// <param name="payload">The complete retained payload.</param>
        /// <param name="fieldName">The dictionary field to convert.</param>
        /// <param name="yaml">Receives the converted field fragment, or null on failure.</param>
        /// <param name="failure">Receives the schema or payload failure.</param>
        /// <returns>True when every record was converted without discarded state.</returns>
        public static bool TryConvertSerializedDictionary<TKey, TObject>(
            OdinMigrationSerializedData payload,
            string fieldName,
            out string yaml,
            out string failure
        )
            where TKey : struct, Enum
            where TObject : UnityEngine.Object
        {
            return OdinMigrationDictionaryConverter.TryConvert(
                payload,
                fieldName,
                typeof(TKey),
                typeof(TObject),
                out yaml,
                out failure
            );
        }

        /// <summary>Converts every declared dictionary in a retained payload into Unity Helpers field YAML.</summary>
        /// <remarks>Every root record must match one schema; references, source changes, and paired apply require separate validation.</remarks>
        /// <param name="payload">The complete retained payload.</param>
        /// <param name="fields">The original types and names of all retained dictionary fields.</param>
        /// <param name="yaml">Receives complete field fragments in retained record order, or null on failure.</param>
        /// <param name="failure">Receives the schema or payload failure.</param>
        /// <returns>True when every declared field and retained record was converted.</returns>
        public static bool TryConvertSerializedDictionaries(
            OdinMigrationSerializedData payload,
            IReadOnlyList<OdinMigrationDictionaryField> fields,
            out string yaml,
            out string failure
        )
        {
            return OdinMigrationDictionaryConverter.TryConvert(
                payload,
                fields,
                out yaml,
                out failure
            );
        }

        /// <summary>Previews all source scripts and serialized assets under Assets without editor UI.</summary>
        public static bool TryPreviewAssets(
            out OdinMigrationPreview preview,
            out string failure,
            Func<string, int, int, bool> shouldCancel = null
        )
        {
            try
            {
                return TryPreview(CollectAssetPaths(false), out preview, out failure, shouldCancel);
            }
            catch (Exception exception)
            {
                preview = null;
                failure = exception.Message;
                return false;
            }
        }

        /// <summary>Previews explicit Assets script paths and all serialized Assets without editor UI.</summary>
        public static bool TryPreview(
            IReadOnlyList<string> sourceAssetPaths,
            out OdinMigrationPreview preview,
            out string failure,
            Func<string, int, int, bool> shouldCancel = null
        )
        {
            if (sourceAssetPaths == null)
            {
                preview = null;
                failure = "Source asset paths are null.";
                return false;
            }
            try
            {
                string projectRoot = ProjectRoot;
                List<string> paths = new List<string>();
                HashSet<string> uniquePaths = new HashSet<string>(StringComparer.Ordinal);
                foreach (string path in sourceAssetPaths.AsValueEnumerableRequired())
                {
                    ValidateAssetPath(projectRoot, path);
                    if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        preview = null;
                        failure = "Source paths must name C# scripts under Assets.";
                        return false;
                    }
                    if (uniquePaths.Add(path))
                    {
                        paths.Add(path);
                    }
                }
                ScanResult state = BuildPlans(
                    paths,
                    CollectSerializedAssetPaths(),
                    new PhysicalOdinMigrationScanContext(projectRoot, shouldCancel, false)
                );
                OdinMigrationPreview result = new OdinMigrationPreview(projectRoot, state);
                if (state.Cancelled || 0 < state.Failures.Count)
                {
                    string scanFailure = state.Cancelled
                        ? "The migration scan was cancelled."
                        : string.Join("; ", state.Failures);
                    preview = result;
                    failure = scanFailure;
                    return false;
                }
                preview = result;
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                preview = null;
                failure = exception.Message;
                return false;
            }
        }

        /// <summary>Applies a preview with byte backups without dialogs or an Asset Database refresh.</summary>
        /// <remarks>Filesystem writes are outside Unity Undo; refresh on the editor thread after success.</remarks>
        public static bool TryApply(
            OdinMigrationPreview preview,
            out string backupRoot,
            out string failure
        )
        {
            if (preview == null || !preview.CanApply)
            {
                backupRoot = null;
                failure = "A complete, unapplied preview with source changes is required.";
                return false;
            }
            string destination = null;
            try
            {
                string projectRoot = ProjectRoot;
                if (!string.Equals(projectRoot, preview.ProjectRoot, StringComparison.Ordinal))
                {
                    backupRoot = null;
                    failure = "The preview belongs to a different Unity project.";
                    return false;
                }
                foreach (OdinMigrationFilePlan plan in preview.State.Plans)
                {
                    ValidateAssetPath(projectRoot, plan.AssetPath);
                }
                destination = Path.Combine(
                    projectRoot,
                    "Library",
                    "WallstopOdinMigrationBackups",
                    DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")
                );
                EnsureNoReparsePoints(projectRoot, destination);
                bool applied = OdinMigrationFileTransaction.TryApply(
                    preview.State.Plans,
                    destination,
                    new PhysicalOdinMigrationFileStore(),
                    out string applyFailure
                );
                if (applied)
                {
                    preview.Applied = true;
                }
                backupRoot = destination;
                failure = applyFailure;
                return applied;
            }
            catch (Exception exception)
            {
                backupRoot = destination;
                failure = exception.Message;
                return false;
            }
        }

        internal static ScanResult BuildPlans(
            IReadOnlyList<string> assetPaths,
            IOdinMigrationScanContext context
        )
        {
            return BuildPlans(assetPaths, Array.Empty<string>(), context);
        }

        internal static ScanResult BuildPlans(
            IReadOnlyList<string> assetPaths,
            IReadOnlyList<string> serializedAssetPaths,
            IOdinMigrationScanContext context
        )
        {
            ScanResult result = new ScanResult();
            if (assetPaths == null || serializedAssetPaths == null || context == null)
            {
                result.Failures.Add("The migration scan did not receive valid inputs.");
                return result;
            }

            int sourceFileCount = assetPaths.Count;
            int serializedFileCount = serializedAssetPaths.Count;
            int totalFiles = sourceFileCount + serializedFileCount;

            for (int index = 0; index < sourceFileCount; ++index)
            {
                string assetPath = assetPaths[index];
                if (context.ShouldCancel(assetPath, index, totalFiles))
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
                    ++result.GeneratedFiles;
                    continue;
                }

                OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(
                    decoded.Source
                );
                result.Blockers += analysis.Blockers.Count;
                result.ManualReviews += analysis.ManualReviews.Count;
                ++result.AnalyzedFiles;
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

            if (result.Cancelled)
            {
                return result;
            }

            for (int index = 0; index < serializedFileCount; ++index)
            {
                string assetPath = serializedAssetPaths[index];
                if (context.ShouldCancel(assetPath, sourceFileCount + index, totalFiles))
                {
                    result.Cancelled = true;
                    break;
                }

                ScanSerializedAsset(assetPath, context, result);
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

        internal static string BuildReport(ScanResult result)
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
                foreach (
                    OdinMigrationChange change in file.Analysis.Changes.AsValueEnumerableRequired()
                )
                {
                    report.AppendLine(
                        $"  CHANGE: {file.AssetPath}:{change.Line}: {change.From} => {change.To}"
                    );
                }
                AppendFindings(report, "BLOCKER", file.AssetPath, file.Analysis.Blockers);
                AppendFindings(report, "REVIEW", file.AssetPath, file.Analysis.ManualReviews);
            }
            foreach (SerializedFileAnalysis file in result.SerializedAnalyses)
            {
                AppendFindings(report, "DATA REVIEW", file.AssetPath, file.Findings);
            }
            foreach (string failure in result.Failures)
            {
                report.AppendLine($"FAILURE: {failure}");
            }
            foreach (string failure in result.SerializedScanFailures)
            {
                report.AppendLine($"DATA SCAN FAILURE: {failure}");
            }
            return report.ToString();
        }

        private static void ScanSerializedAsset(
            string assetPath,
            IOdinMigrationScanContext context,
            ScanResult result
        )
        {
            try
            {
                ScanSerializedAssetContents(assetPath, context, result);
            }
            catch (Exception exception)
            {
                result.SerializedScanFailures.Add($"{assetPath}: {exception.Message}");
            }
        }

        private static void ScanSerializedAssetContents(
            string assetPath,
            IOdinMigrationScanContext context,
            ScanResult result
        )
        {
            byte[] bytes = context.ReadAllBytes(context.GetFullPath(assetPath));
            if (
                !OdinMigrationEncodedSource.TryDecode(
                    bytes,
                    out OdinMigrationDecodedSource decoded,
                    out string decodeFailure
                )
            )
            {
                result.SerializedScanFailures.Add($"{assetPath}: {decodeFailure}");
                return;
            }
            if (!OdinMigrationSerializedDataScanner.LooksLikeUnityYaml(decoded.Source))
            {
                result.SerializedScanFailures.Add(
                    $"{assetPath}: not Unity text YAML; serialized data cannot be checked."
                );
                return;
            }

            if (
                !OdinMigrationSerializedDataScanner.TryAnalyze(
                    decoded.Source,
                    out IReadOnlyList<OdinMigrationFinding> findings
                )
            )
            {
                result.SerializedScanFailures.Add(
                    $"{assetPath}: no Unity YAML documents were found; serialized data cannot be checked."
                );
                return;
            }
            ++result.SerializedAssetsScanned;
            result.SerializedDataFindings += findings.Count;
            if (0 < findings.Count)
            {
                result.SerializedAnalyses.Add(new SerializedFileAnalysis(assetPath, findings));
            }
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
            List<string> assetPaths;
            List<string> serializedAssetPaths;
            try
            {
                assetPaths = CollectAssetPaths(selectedOnly);
                serializedAssetPaths = CollectSerializedAssetPaths();
            }
            catch (Exception exception)
            {
                Debug.LogError($"Odin migration could not discover assets: {exception}");
                EditorUtility.DisplayDialog(
                    "Odin Migration Preview",
                    $"Asset discovery failed: {exception.Message}",
                    "OK"
                );
                return;
            }
            if (assetPaths.Count == 0 && (selectedOnly || serializedAssetPaths.Count == 0))
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
                result = BuildPlans(
                    assetPaths,
                    serializedAssetPaths,
                    new PhysicalOdinMigrationScanContext(ProjectRoot)
                );
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

            if (
                !TryApply(
                    new OdinMigrationPreview(ProjectRoot, result),
                    out string backupRoot,
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

        private static void AppendFindings(
            StringBuilder report,
            string category,
            string assetPath,
            IReadOnlyList<OdinMigrationFinding> findings
        )
        {
            foreach (OdinMigrationFinding finding in findings.AsValueEnumerableRequired())
            {
                report.AppendLine($"  {category}: {assetPath}:{finding.Line}: {finding.Message}");
            }
        }

        private static string Summary(ScanResult result)
        {
            return $"Analyzed {result.AnalyzedFiles} file(s); "
                + $"{result.Replacements} safe replacement(s) in {result.Plans.Count} file(s); "
                + $"{result.Blockers} blocker(s); {result.ManualReviews} manual review item(s); "
                + $"{result.SerializedAssetsScanned} serialized asset(s) checked; "
                + $"{result.SerializedDataFindings} serialized-data review item(s); "
                + $"{result.SerializedScanFailures.Count} serialized-data scan failure(s); "
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

        private static List<string> CollectSerializedAssetPaths()
        {
            SortedSet<string> paths = new SortedSet<string>(StringComparer.Ordinal);
            string assetsRoot = Application.dataPath;
            foreach (
                string fullPath in Directory.EnumerateFiles(
                    assetsRoot,
                    "*",
                    SearchOption.AllDirectories
                )
            )
            {
                string path = fullPath.Substring(ProjectRoot.Length + 1).SanitizePath();
                if (
                    path.StartsWith("Assets/", StringComparison.Ordinal)
                    && (
                        path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    paths.Add(path);
                }
            }

            return new List<string>(paths);
        }

        private static void ValidateAssetPath(string projectRoot, string assetPath)
        {
            if (
                string.IsNullOrWhiteSpace(assetPath)
                || !assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                || 0 <= assetPath.IndexOf('\\')
                || 0 <= assetPath.IndexOf(':')
            )
            {
                throw new IOException("Migration paths must be forward-slash paths under Assets.");
            }
            foreach (string segment in assetPath.Split('/'))
            {
                if (
                    segment.Length == 0
                    || string.Equals(segment, ".", StringComparison.Ordinal)
                    || string.Equals(segment, "..", StringComparison.Ordinal)
                )
                {
                    throw new IOException(
                        "Migration paths cannot contain empty or traversal segments."
                    );
                }
            }
            EnsureNoReparsePoints(projectRoot, Path.Combine(projectRoot, assetPath));
        }

        private static void EnsureNoReparsePoints(string projectRoot, string fullPath)
        {
            string current = fullPath;
            while (!string.Equals(current, projectRoot, StringComparison.Ordinal))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new IOException(
                            "Migration refuses symbolic links and reparse points: " + current
                        );
                    }
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                current = Path.GetDirectoryName(current);
                if (string.IsNullOrWhiteSpace(current))
                {
                    throw new IOException("Migration path is outside the project.");
                }
            }
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

        internal sealed class SerializedFileAnalysis
        {
            internal readonly string AssetPath;
            internal readonly IReadOnlyList<OdinMigrationFinding> Findings;

            internal SerializedFileAnalysis(
                string assetPath,
                IReadOnlyList<OdinMigrationFinding> findings
            )
            {
                AssetPath = assetPath;
                Findings = findings;
            }
        }

        internal sealed class ScanResult
        {
            internal readonly List<FileAnalysis> Analyses = new List<FileAnalysis>();
            internal readonly List<SerializedFileAnalysis> SerializedAnalyses =
                new List<SerializedFileAnalysis>();
            internal readonly List<string> Failures = new List<string>();
            internal readonly List<string> SerializedScanFailures = new List<string>();
            internal readonly List<OdinMigrationFilePlan> Plans = new List<OdinMigrationFilePlan>();
            internal int AnalyzedFiles;
            internal int Blockers;
            internal bool Cancelled;
            internal int GeneratedFiles;
            internal int ManualReviews;
            internal int Replacements;
            internal int SerializedAssetsScanned;
            internal int SerializedDataFindings;
        }

        internal interface IOdinMigrationScanContext
        {
            string GetFullPath(string assetPath);

            byte[] ReadAllBytes(string fullPath);

            bool ShouldCancel(string assetPath, int index, int count);
        }

        private sealed class PhysicalOdinMigrationScanContext : IOdinMigrationScanContext
        {
            private readonly string _projectRoot;
            private readonly Func<string, int, int, bool> _shouldCancel;
            private readonly bool _interactive;

            internal PhysicalOdinMigrationScanContext(
                string projectRoot,
                Func<string, int, int, bool> shouldCancel = null,
                bool interactive = true
            )
            {
                _projectRoot = projectRoot;
                _shouldCancel = shouldCancel;
                _interactive = interactive;
            }

            public string GetFullPath(string assetPath)
            {
                ValidateAssetPath(_projectRoot, assetPath);
                return Path.GetFullPath(Path.Combine(_projectRoot, assetPath));
            }

            public byte[] ReadAllBytes(string fullPath)
            {
                return File.ReadAllBytes(fullPath);
            }

            public bool ShouldCancel(string assetPath, int index, int count)
            {
                if (_shouldCancel != null)
                {
                    return _shouldCancel(assetPath, index, count);
                }
                return _interactive
                    && EditorUtility.DisplayCancelableProgressBar(
                        "Odin Migration",
                        $"Analyzing {assetPath}",
                        (float)index / count
                    );
            }
        }
    }
}
