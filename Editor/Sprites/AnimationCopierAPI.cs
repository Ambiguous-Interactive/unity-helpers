// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    /// <summary>
    /// Analyzes and copies standalone animation clips without an editor window.
    /// </summary>
    public static class AnimationCopierAPI
    {
        /// <summary>
        /// Classifies standalone animation clips under two Assets folders.
        /// </summary>
        public static bool TryAnalyze(
            string sourceFolder,
            string destinationFolder,
            List<Entry> sourceEntries,
            List<Entry> destinationOrphans,
            out string error,
            Func<string, int, int, bool> cancelRequested = null
        )
        {
            if (
                sourceEntries == null
                || destinationOrphans == null
                || ReferenceEquals(sourceEntries, destinationOrphans)
            )
            {
                error = "Separate source and orphan destination lists are required.";
                return false;
            }

            sourceEntries.Clear();
            destinationOrphans.Clear();
            if (
                !TryValidateRoots(
                    sourceFolder,
                    destinationFolder,
                    out string sourceRoot,
                    out string destinationRoot,
                    out error
                )
            )
            {
                return false;
            }

            try
            {
                string[] sourceGuids = AssetDatabase.FindAssets(
                    "t:AnimationClip",
                    new[] { sourceRoot }
                );
                HashSet<string> expectedDestinations = new(StringComparer.OrdinalIgnoreCase);
                HashSet<string> sourcePaths = new(StringComparer.OrdinalIgnoreCase);
                for (int index = 0; index < sourceGuids.Length; index++)
                {
                    string sourcePath = AssetDatabase.GUIDToAssetPath(sourceGuids[index]);
                    if (
                        cancelRequested != null
                        && cancelRequested(sourcePath, index + 1, sourceGuids.Length)
                    )
                    {
                        sourceEntries.Clear();
                        error = "Analysis cancelled.";
                        return false;
                    }
                    if (
                        !IsStandaloneClipPath(sourcePath)
                        || !IsSameOrChild(sourcePath, sourceRoot)
                        || !sourcePaths.Add(sourcePath)
                    )
                    {
                        continue;
                    }
                    string sourceFullPath = ToFullPath(sourcePath);
                    if (!File.Exists(sourceFullPath))
                    {
                        continue;
                    }
                    AnimationClip sourceClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                        sourcePath
                    );
                    if (sourceClip == null)
                    {
                        continue;
                    }
                    string destinationPath =
                        destinationRoot + sourcePath.Substring(sourceRoot.Length);
                    expectedDestinations.Add(destinationPath);
                    if (
                        !TryClassify(
                            sourceClip,
                            destinationPath,
                            out Status status,
                            out string classifyError
                        )
                    )
                    {
                        sourceEntries.Clear();
                        error = classifyError;
                        return false;
                    }
                    sourceEntries.Add(new Entry(sourcePath, destinationPath, status));
                }

                if (AssetDatabase.IsValidFolder(destinationRoot))
                {
                    string[] destinationGuids = AssetDatabase.FindAssets(
                        "t:AnimationClip",
                        new[] { destinationRoot }
                    );
                    HashSet<string> destinationPaths = new(StringComparer.OrdinalIgnoreCase);
                    for (int index = 0; index < destinationGuids.Length; index++)
                    {
                        string destinationPath = AssetDatabase.GUIDToAssetPath(
                            destinationGuids[index]
                        );
                        if (
                            cancelRequested != null
                            && cancelRequested(destinationPath, index + 1, destinationGuids.Length)
                        )
                        {
                            sourceEntries.Clear();
                            destinationOrphans.Clear();
                            error = "Analysis cancelled.";
                            return false;
                        }
                        if (
                            !IsStandaloneClipPath(destinationPath)
                            || !IsSameOrChild(destinationPath, destinationRoot)
                            || !destinationPaths.Add(destinationPath)
                            || expectedDestinations.Contains(destinationPath)
                        )
                        {
                            continue;
                        }
                        if (File.Exists(ToFullPath(destinationPath)))
                        {
                            destinationOrphans.Add(
                                new Entry(string.Empty, destinationPath, Status.Orphan)
                            );
                        }
                    }
                }

                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                sourceEntries.Clear();
                destinationOrphans.Clear();
                error = exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Previews or applies one operation to explicit animation asset paths.
        /// </summary>
        /// <remarks>
        /// Copy, delete, and reimport side effects cannot be fully reversed by Unity Undo.
        /// Counts describe eligible operations during a preview and completed operations when applying.
        /// </remarks>
        public static Result Run(
            string sourceFolder,
            string destinationFolder,
            IReadOnlyList<string> selectedAssetPaths,
            Operation operation,
            bool applyChanges,
            bool forceReplaceUnchanged = false,
            Func<string, int, int, bool> cancelRequested = null
        )
        {
            List<string> diagnostics = new();
            if (selectedAssetPaths == null)
            {
                return new Result(
                    false,
                    false,
                    0,
                    0,
                    0,
                    0,
                    "Selected asset paths are required.",
                    diagnostics
                );
            }
            if (!IsValidOperation(operation))
            {
                return new Result(
                    false,
                    false,
                    selectedAssetPaths.Count,
                    0,
                    0,
                    0,
                    "Operation is invalid.",
                    diagnostics
                );
            }
            if (
                !TryValidateRoots(
                    sourceFolder,
                    destinationFolder,
                    out string sourceRoot,
                    out string destinationRoot,
                    out string rootError
                )
            )
            {
                return new Result(
                    false,
                    false,
                    selectedAssetPaths.Count,
                    0,
                    0,
                    0,
                    rootError,
                    diagnostics
                );
            }
            if (selectedAssetPaths.Count == 0)
            {
                return new Result(true, false, 0, 0, 0, 0, string.Empty, diagnostics);
            }

            int processed = 0;
            int skipped = 0;
            int failed = 0;
            bool cancelled = false;
            string error = string.Empty;
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            AssetDatabaseBatchScope batch = default;
            try
            {
                if (applyChanges)
                {
                    batch = AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false);
                }
                for (int index = 0; index < selectedAssetPaths.Count; index++)
                {
                    string selected = selectedAssetPaths[index];
                    if (
                        cancelRequested != null
                        && cancelRequested(selected, index + 1, selectedAssetPaths.Count)
                    )
                    {
                        cancelled = true;
                        break;
                    }
                    if (
                        !TryNormalizeAssetPath(selected, out string path, out string pathError)
                        || !seen.Add(path)
                    )
                    {
                        failed++;
                        diagnostics.Add(
                            $"{selected ?? "<null>"}: {(string.IsNullOrEmpty(pathError) ? "A duplicate asset path was selected." : pathError)}"
                        );
                        continue;
                    }
                    bool deletingDestination = operation == Operation.DeleteDestinationOrphans;
                    string selectionRoot = deletingDestination ? destinationRoot : sourceRoot;
                    if (!IsSameOrChild(path, selectionRoot) || !IsStandaloneClipPath(path))
                    {
                        failed++;
                        diagnostics.Add(
                            $"Selected asset is not a standalone animation clip under '{selectionRoot}': '{path}'."
                        );
                        continue;
                    }

                    string suffix = path.Substring(selectionRoot.Length);
                    string sourcePath = deletingDestination ? sourceRoot + suffix : path;
                    string destinationPath = deletingDestination ? path : destinationRoot + suffix;
                    try
                    {
                        if (
                            !TryProcessOne(
                                sourcePath,
                                destinationPath,
                                operation,
                                applyChanges,
                                forceReplaceUnchanged,
                                out bool eligible,
                                out string itemError
                            )
                        )
                        {
                            failed++;
                            diagnostics.Add($"{path}: {itemError}");
                        }
                        else if (eligible)
                        {
                            processed++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch (Exception exception)
                    {
                        failed++;
                        diagnostics.Add($"{path}: {exception.Message}");
                    }
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
            }
            finally
            {
                batch.Dispose();
                if (applyChanges)
                {
                    try
                    {
                        AssetDatabase.SaveAssets();
                        AssetDatabaseBatchHelper.RefreshIfNotBatching();
                    }
                    catch (Exception exception)
                    {
                        error = exception.Message;
                    }
                }
            }

            return new Result(
                failed == 0 && !cancelled && string.IsNullOrEmpty(error),
                cancelled,
                selectedAssetPaths.Count,
                processed,
                skipped,
                failed,
                error,
                diagnostics
            );
        }

        private static bool TryProcessOne(
            string sourcePath,
            string destinationPath,
            Operation operation,
            bool applyChanges,
            bool forceReplaceUnchanged,
            out bool eligible,
            out string error
        )
        {
            eligible = false;
            error = string.Empty;
            string sourceFullPath = ToFullPath(sourcePath);
            string destinationFullPath = ToFullPath(destinationPath);
            bool sourceExists = File.Exists(sourceFullPath);
            bool destinationExists = File.Exists(destinationFullPath);

            if (operation == Operation.DeleteDestinationOrphans)
            {
                if (!destinationExists)
                {
                    return true;
                }
                if (sourceExists)
                {
                    return true;
                }
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(destinationPath) == null)
                {
                    error = "Destination is not an animation clip.";
                    return false;
                }
                eligible = true;
                if (applyChanges && !AssetDatabase.DeleteAsset(destinationPath))
                {
                    error = "Could not delete the destination clip.";
                    return false;
                }
                return true;
            }

            if (!sourceExists)
            {
                error = "Source clip does not exist.";
                return false;
            }
            AnimationClip sourceClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(sourcePath);
            if (sourceClip == null)
            {
                error = "Source is not an animation clip.";
                return false;
            }
            if (!TryClassify(sourceClip, destinationPath, out Status status, out error))
            {
                return false;
            }

            if (operation == Operation.DeleteUnchangedSource)
            {
                if (status != Status.Unchanged)
                {
                    return true;
                }
                eligible = true;
                if (applyChanges && !AssetDatabase.DeleteAsset(sourcePath))
                {
                    error = "Could not delete the source clip.";
                    return false;
                }
                return true;
            }

            eligible = operation switch
            {
                Operation.CopyNew => status == Status.New,
                Operation.CopyChanged => status == Status.Changed,
                Operation.CopyAll => status != Status.Unchanged || forceReplaceUnchanged,
                _ => false,
            };
            if (!eligible || !applyChanges)
            {
                return true;
            }
            if (status == Status.New)
            {
                if (!AssetDatabaseBatchHelper.EnsureAssetParentFolder(destinationPath))
                {
                    error = "Could not create the destination folder.";
                    return false;
                }
                if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
                {
                    error = "Could not copy the source clip.";
                    return false;
                }
                return true;
            }
            if (!destinationExists)
            {
                error = "Destination clip disappeared before replacement.";
                return false;
            }
            FileUtil.ReplaceFile(sourceFullPath, destinationFullPath);
            AssetDatabase.ImportAsset(destinationPath, ImportAssetOptions.ForceUpdate);
            return true;
        }

        private static bool TryClassify(
            AnimationClip source,
            string destinationPath,
            out Status status,
            out string error
        )
        {
            status = Status.New;
            error = string.Empty;
            string destinationFullPath = ToFullPath(destinationPath);
            if (!File.Exists(destinationFullPath))
            {
                return true;
            }
            AnimationClip destination = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                destinationPath
            );
            if (source == null || destination == null)
            {
                error = "Source or destination file is not an animation clip.";
                return false;
            }
            status = AnimationClipContentComparer.AreAnimationClipsContentEqual(source, destination)
                ? Status.Unchanged
                : Status.Changed;
            return true;
        }

        private static bool TryValidateRoots(
            string sourceFolder,
            string destinationFolder,
            out string sourceRoot,
            out string destinationRoot,
            out string error
        )
        {
            sourceRoot = string.Empty;
            destinationRoot = string.Empty;
            if (
                !TryNormalizeAssetPath(sourceFolder, out sourceRoot, out error)
                || !TryNormalizeAssetPath(destinationFolder, out destinationRoot, out error)
            )
            {
                return false;
            }
            if (
                IsSameOrChild(sourceRoot, destinationRoot)
                || IsSameOrChild(destinationRoot, sourceRoot)
            )
            {
                error = "Source and destination folders cannot overlap.";
                return false;
            }
            try
            {
                if (!AssetDatabase.IsValidFolder(sourceRoot))
                {
                    error = "Source folder does not exist in the AssetDatabase.";
                    return false;
                }
                if (File.Exists(ToFullPath(destinationRoot)))
                {
                    error = "Destination folder path points to a file.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static bool TryNormalizeAssetPath(
            string input,
            out string normalized,
            out string error
        )
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                error = "An Assets-relative path is required.";
                return false;
            }
            string slashes = input.Trim().Replace('\\', '/').TrimEnd('/');
            if (
                !string.Equals(slashes, "Assets", StringComparison.OrdinalIgnoreCase)
                && !slashes.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
            )
            {
                error = "Path must be under Assets.";
                return false;
            }
            try
            {
                string assetsFullPath = Path.GetFullPath(Application.dataPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string projectRoot = Path.GetDirectoryName(assetsFullPath);
                string fullPath = Path.GetFullPath(Path.Combine(projectRoot, slashes));
                if (!IsSameOrChild(fullPath, assetsFullPath))
                {
                    error = "Path escapes Assets.";
                    return false;
                }
                normalized =
                    "Assets" + fullPath.Substring(assetsFullPath.Length).Replace('\\', '/');
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static bool IsSameOrChild(string path, string root)
        {
            char separator = Path.IsPathRooted(root) ? Path.DirectorySeparatorChar : '/';
            return string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(
                    root.TrimEnd('/', '\\') + separator,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private static bool IsStandaloneClipPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && string.Equals(
                    Path.GetExtension(path),
                    ".anim",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private static bool IsValidOperation(Operation operation)
        {
            return operation == Operation.CopyNew
                || operation == Operation.CopyChanged
                || operation == Operation.CopyAll
                || operation == Operation.DeleteUnchangedSource
                || operation == Operation.DeleteDestinationOrphans;
        }

        private static string ToFullPath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        /// <summary>The action to preview or apply to selected animation paths.</summary>
        public enum Operation
        {
            /// <summary>No operation has been selected.</summary>
            [Obsolete("Choose an animation operation.")]
            Unknown = 0,

            /// <summary>Copy clips without a destination file.</summary>
            CopyNew = 1,

            /// <summary>Replace clips whose contents differ.</summary>
            CopyChanged = 2,

            /// <summary>Copy new and changed clips, with optional unchanged replacement.</summary>
            CopyAll = 3,

            /// <summary>Delete source clips currently equal to their destinations.</summary>
            DeleteUnchangedSource = 4,

            /// <summary>Delete destination clips with no matching source file.</summary>
            DeleteDestinationOrphans = 5,
        }

        /// <summary>The current relation between a source clip and its destination.</summary>
        public enum Status
        {
            /// <summary>No classification has been made.</summary>
            [Obsolete("Analyze an animation clip before using its status.")]
            Unknown = 0,

            /// <summary>The destination does not exist.</summary>
            New = 1,

            /// <summary>The destination differs from the source.</summary>
            Changed = 2,

            /// <summary>The destination matches the source.</summary>
            Unchanged = 3,

            /// <summary>The destination has no matching source.</summary>
            Orphan = 4,
        }

        /// <summary>A source clip or destination orphan found by analysis.</summary>
        public readonly struct Entry
        {
            /// <summary>The source asset path, or empty for an orphan.</summary>
            public string SourcePath { get; }

            /// <summary>The destination asset path.</summary>
            public string DestinationPath { get; }

            /// <summary>The status found during analysis.</summary>
            public Status Classification { get; }

            internal Entry(string sourcePath, string destinationPath, Status classification)
            {
                SourcePath = sourcePath;
                DestinationPath = destinationPath;
                Classification = classification;
            }
        }

        /// <summary>The outcome of a selected animation operation.</summary>
        public sealed class Result
        {
            /// <summary>Whether the operation finished without failure or cancellation.</summary>
            public bool Succeeded { get; }

            /// <summary>Whether the caller cancelled the operation.</summary>
            public bool Cancelled { get; }

            /// <summary>The number of supplied paths.</summary>
            public int SelectedCount { get; }

            /// <summary>The number of eligible or completed operations.</summary>
            public int ProcessedCount { get; }

            /// <summary>The number of paths that no longer match the operation.</summary>
            public int SkippedCount { get; }

            /// <summary>The number of paths that failed validation or mutation.</summary>
            public int FailedCount { get; }

            /// <summary>A global error, or an empty string.</summary>
            public string Error { get; }

            /// <summary>Per-path validation and mutation errors.</summary>
            public IReadOnlyList<string> Diagnostics { get; }

            internal Result(
                bool succeeded,
                bool cancelled,
                int selectedCount,
                int processedCount,
                int skippedCount,
                int failedCount,
                string error,
                IReadOnlyList<string> diagnostics
            )
            {
                Succeeded = succeeded;
                Cancelled = cancelled;
                SelectedCount = selectedCount;
                ProcessedCount = processedCount;
                SkippedCount = skippedCount;
                FailedCount = failedCount;
                Error = error;
                Diagnostics = diagnostics;
            }
        }
    }
#endif
}
