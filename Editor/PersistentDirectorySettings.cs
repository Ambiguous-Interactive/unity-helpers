// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor
{
#if UNITY_EDITOR
    using System;
    using UnityEngine;
    using UnityEditor;
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine.Serialization;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;
    using Debug = UnityEngine.Debug;

    [Serializable]
    public sealed class DirectoryUsageData
    {
        public string path;
        public int count;
        public long lastUsedTicks;

        public DirectoryUsageData(string p)
        {
            path = p;
            count = 0;
            lastUsedTicks = DateTime.UtcNow.Ticks;
        }

        public void MarkUsed()
        {
            count++;
            lastUsedTicks = DateTime.UtcNow.Ticks;
        }
    }

    [Serializable]
    public sealed class ContextHistory
    {
        public string contextKey;
        public List<DirectoryUsageData> directories = new();

        public ContextHistory() { }

        public ContextHistory(string key)
        {
            contextKey = key;
        }

        public DirectoryUsageData GetOrAddDirectory(string path)
        {
            DirectoryUsageData dirData = directories.Find(directoryData =>
                string.Equals(directoryData.path, path, StringComparison.Ordinal)
            );
            if (dirData != null)
            {
                return dirData;
            }

            dirData = new DirectoryUsageData(path);
            directories.Add(dirData);
            return dirData;
        }
    }

    [Serializable]
    public sealed class ToolHistory
    {
        public string toolName;
        public List<ContextHistory> contexts = new();

        public ToolHistory() { }

        public ToolHistory(string name)
        {
            toolName = name;
        }

        public ContextHistory GetOrAddContext(string contextKey)
        {
            ContextHistory context = contexts.Find(c =>
                string.Equals(c.contextKey, contextKey, StringComparison.Ordinal)
            );
            if (context != null)
            {
                return context;
            }

            context = new ContextHistory(contextKey);
            contexts.Add(context);
            return context;
        }
    }

    [WallstopStudios.UnityHelpers.Core.Attributes.ScriptableSingletonPath(
        "Wallstop Studios/Unity Helpers/Editor"
    )]
    [WallstopStudios.UnityHelpers.Core.Attributes.AllowDuplicateCleanup]
    public sealed class PersistentDirectorySettings
        : ScriptableObjectSingleton<PersistentDirectorySettings>
    {
        internal const string ResourcesRoot = "Assets/Resources";
        internal const string SubPath = "Wallstop Studios/Unity Helpers/Editor";
        internal const string LegacySubPath = "Wallstop Studios/Editor";

        /// <summary>
        /// The root folder for all Wallstop Studios assets. This folder must NEVER be deleted
        /// as it contains production, client-facing data.
        /// </summary>
        internal const string WallstopStudiosRoot = ResourcesRoot + "/Wallstop Studios";

        internal static string TargetFolder => SanitizePath(Path.Combine(ResourcesRoot, SubPath));

        internal static string TargetAssetPath =>
            SanitizePath(
                Path.Combine(TargetFolder, nameof(PersistentDirectorySettings) + ".asset")
            );

        internal static string LegacyFolder =>
            SanitizePath(Path.Combine(ResourcesRoot, LegacySubPath));

        internal static string LegacyAssetPath =>
            SanitizePath(
                Path.Combine(LegacyFolder, nameof(PersistentDirectorySettings) + ".asset")
            );

        [FormerlySerializedAs("allToolHistories")]
        [SerializeField]
        private List<ToolHistory> _allToolHistories = new();

        // Automatic migration and consolidation to Resources/Wallstop Studios/Unity Helpers/Editor
        [InitializeOnLoadMethod]
        private static void EnsureSingletonAndMigrate()
        {
            // Defer operations that may conflict with Unity's initialization
            // EditorApplication.delayCall runs after Unity is fully loaded
            EditorApplication.delayCall += () =>
            {
                // Skip automatic migration during test runs to avoid Unity's internal modal dialogs
                // when asset operations fail, unless explicitly allowed.
                if (
                    Utils.EditorUi.Suppress
                    && !Utils.ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression
                )
                {
                    return;
                }

                RunMigration();
                CleanupLegacyEmptyFolders();
            };
        }

        /// <summary>
        /// Scans for and removes empty folders under Assets/Resources/Wallstop Studios that may have been
        /// left behind from previous versions of this package. This is safe to call at any time.
        /// </summary>
        internal static void CleanupLegacyEmptyFolders()
        {
            try
            {
                string wallstopRoot = SanitizePath(Path.Combine(ResourcesRoot, "Wallstop Studios"));
                if (!AssetDatabase.IsValidFolder(wallstopRoot))
                {
                    return;
                }

                bool anyDeleted = CleanupEmptyFoldersRecursive(wallstopRoot);
                if (anyDeleted)
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(
                    $"CleanupLegacyEmptyFolders encountered an issue: {e.Message}"
                );
            }
        }

        private static bool CleanupEmptyFoldersRecursive(string folderPath)
        {
            return CleanupEmptyFoldersRecursive(
                folderPath,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            );
        }

        private static bool CleanupEmptyFoldersRecursive(
            string folderPath,
            HashSet<string> deletedFolders
        )
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                return false;
            }

            // CRITICAL: Never delete the root "Wallstop Studios" folder - this is production data
            // Check this FIRST before any recursive operations to ensure it's never deleted
            string normalizedPath = SanitizePath(folderPath);
            if (
                string.Equals(
                    normalizedPath,
                    WallstopStudiosRoot,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                // Only clean up subfolders, never the root itself
                bool anyDeleted = false;
                string[] subFolders = AssetDatabase.GetSubFolders(folderPath);
                if (subFolders != null)
                {
                    foreach (string subFolder in subFolders)
                    {
                        if (CleanupEmptyFoldersRecursive(subFolder, deletedFolders))
                        {
                            anyDeleted = true;
                        }
                    }
                }
                // Explicitly return here - never fall through to deletion code for WallstopStudiosRoot
                return anyDeleted;
            }

            // CRITICAL: Also protect Assets/Resources from deletion
            if (string.Equals(normalizedPath, ResourcesRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            bool deleted = false;

            // First, recursively clean up subfolders
            string[] childFolders = AssetDatabase.GetSubFolders(folderPath);
            if (childFolders != null)
            {
                foreach (string subFolder in childFolders)
                {
                    if (CleanupEmptyFoldersRecursive(subFolder, deletedFolders))
                    {
                        deleted = true;
                    }
                }
            }

            // Check if this folder is now empty (no assets and no subfolders)
            // Note: AssetDatabase.GetSubFolders may return stale data after deletions,
            // so we filter out folders that we know have been deleted in this pass.
            string[] remainingSubFolders = AssetDatabase.GetSubFolders(folderPath);
            int actualSubFolderCount = 0;
            if (remainingSubFolders != null)
            {
                foreach (string subFolder in remainingSubFolders)
                {
                    if (!deletedFolders.Contains(subFolder))
                    {
                        actualSubFolderCount++;
                    }
                }
            }

            // Guard against calling FindAssets on invalid folders (can happen during initialization)
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                return deleted;
            }

            string[] assets = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });

            // FindAssets with empty search in a specific folder returns all assets in that folder and subfolders
            // We need to check if there are any direct children
            bool hasDirectAssets = false;
            if (assets != null)
            {
                foreach (string guid in assets)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(assetPath))
                    {
                        continue;
                    }

                    string assetDir = Path.GetDirectoryName(assetPath);
                    if (assetDir != null)
                    {
                        assetDir = SanitizePath(assetDir);
                    }

                    if (string.Equals(assetDir, folderPath, StringComparison.OrdinalIgnoreCase))
                    {
                        hasDirectAssets = true;
                        break;
                    }
                }
            }

            bool hasSubFolders = actualSubFolderCount > 0;

            if (!hasDirectAssets && !hasSubFolders)
            {
                // Folder is empty - delete it
                if (AssetDatabase.DeleteAsset(folderPath))
                {
                    deletedFolders.Add(folderPath);
                    deleted = true;
                }
            }

            return deleted;
        }

        /// <summary>
        /// Runs the migration logic to consolidate all PersistentDirectorySettings assets
        /// into the canonical target location. This method is idempotent and safe to call multiple times.
        /// </summary>
        /// <returns>The target PersistentDirectorySettings instance after migration, or null if migration failed.</returns>
        internal static PersistentDirectorySettings RunMigration()
        {
            try
            {
                string targetFolder = TargetFolder;
                string targetAssetPath = TargetAssetPath;

                EnsureFolderExists(ResourcesRoot);
                EnsureFolderExists(targetFolder);

                // Find all existing assets of this type
                string[] guids = AssetDatabase.FindAssets(
                    "t:" + nameof(PersistentDirectorySettings)
                );
                using (Buffers<string>.List.Get(out List<string> candidatePaths))
                {
                    using (
                        SetBuffers<string>
                            .GetHashSetPool(StringComparer.OrdinalIgnoreCase)
                            .Get(out HashSet<string> seen)
                    )
                    {
                        foreach (string guid in guids)
                        {
                            string path = AssetDatabase.GUIDToAssetPath(guid);
                            if (string.IsNullOrWhiteSpace(path))
                            {
                                continue;
                            }
                            string sanitized = SanitizePath(path);
                            if (seen.Add(sanitized))
                            {
                                candidatePaths.Add(sanitized);
                            }
                        }
                    }

                    // Load or create target asset
                    PersistentDirectorySettings target =
                        AssetDatabase.LoadAssetAtPath<PersistentDirectorySettings>(targetAssetPath);

                    if (target == null && candidatePaths.Count == 0)
                    {
                        // No assets exist anywhere - create fresh
                        target = CreateInstance<PersistentDirectorySettings>();
                        AssetDatabase.CreateAsset(target, targetAssetPath);
                        AssetDatabase.SaveAssets();
                        AssetDatabase.Refresh();
                        return target;
                    }

                    if (target == null && candidatePaths.Count > 0)
                    {
                        // Target doesn't exist but we have candidates - move the first one
                        string primaryPath = candidatePaths[0];
                        PersistentDirectorySettings primary =
                            AssetDatabase.LoadAssetAtPath<PersistentDirectorySettings>(primaryPath);
                        if (primary != null && !PathsEqual(primaryPath, targetAssetPath))
                        {
                            string moveResult = AssetDatabase.MoveAsset(
                                primaryPath,
                                targetAssetPath
                            );
                            if (string.IsNullOrEmpty(moveResult))
                            {
                                // Move succeeded - refresh database before cleanup
                                AssetDatabase.SaveAssets();
                                AssetDatabase.Refresh();
                                TryDeleteEmptyParentFolders(primaryPath);
                                target = AssetDatabase.LoadAssetAtPath<PersistentDirectorySettings>(
                                    targetAssetPath
                                );
                                candidatePaths.RemoveAt(0);
                            }
                            else
                            {
                                // Move failed - create new target and merge data from primary
                                UnityEngine.Debug.LogWarning(
                                    $"Failed to move {nameof(PersistentDirectorySettings)} from {primaryPath} to {targetAssetPath}: {moveResult}. Will create new and merge."
                                );

                                // Ensure target folder exists before creating asset
                                EnsureFolderExists(targetFolder);
                                AssetDatabase.SaveAssets();
                                AssetDatabase.Refresh();

                                target = CreateInstance<PersistentDirectorySettings>();

                                // Copy data from primary before creating the asset
                                MergeSettings(target, primary);

                                AssetDatabase.CreateAsset(target, targetAssetPath);
                                EditorUtility.SetDirty(target);
                                AssetDatabase.SaveAssets();

                                // Delete the primary since we've copied its data
                                if (AssetDatabase.DeleteAsset(primaryPath))
                                {
                                    TryDeleteEmptyParentFolders(primaryPath);
                                }
                                candidatePaths.RemoveAt(0);
                            }
                        }
                        else if (primary == null)
                        {
                            target = CreateInstance<PersistentDirectorySettings>();
                            AssetDatabase.CreateAsset(target, targetAssetPath);
                            candidatePaths.RemoveAt(0);
                        }
                    }

                    // Target now exists - merge and delete any remaining duplicates
                    if (target != null && candidatePaths.Count > 0)
                    {
                        bool anyMerged = false;
                        List<string> deletedPaths = new();
                        foreach (string path in candidatePaths)
                        {
                            if (PathsEqual(path, targetAssetPath))
                            {
                                continue;
                            }

                            PersistentDirectorySettings other =
                                AssetDatabase.LoadAssetAtPath<PersistentDirectorySettings>(path);
                            if (other == null)
                            {
                                continue;
                            }

                            // Merge data from duplicate into target
                            MergeSettings(target, other);
                            EditorUtility.SetDirty(target);
                            anyMerged = true;

                            // Delete the duplicate asset
                            if (AssetDatabase.DeleteAsset(path))
                            {
                                deletedPaths.Add(path);
                            }
                        }

                        if (anyMerged)
                        {
                            AssetDatabase.SaveAssets();
                            AssetDatabase.Refresh();

                            // Clean up empty folders after refresh
                            foreach (string deletedPath in deletedPaths)
                            {
                                TryDeleteEmptyParentFolders(deletedPath);
                            }
                        }
                    }

                    return target;
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(
                    $"{nameof(PersistentDirectorySettings)} migration encountered an issue: {e.Message}\n{e}"
                );
                return null;
            }
        }

        private ToolHistory GetOrAddToolHistory(string toolName)
        {
            ToolHistory toolHistory = _allToolHistories.Find(toolHistory =>
                string.Equals(toolHistory.toolName, toolName, StringComparison.Ordinal)
            );
            if (toolHistory != null)
            {
                return toolHistory;
            }

            toolHistory = new ToolHistory(toolName);
            _allToolHistories.Add(toolHistory);

            return toolHistory;
        }

        public void RecordPath(string toolName, string contextKey, string path)
        {
            if (
                string.IsNullOrWhiteSpace(toolName)
                || string.IsNullOrWhiteSpace(contextKey)
                || string.IsNullOrWhiteSpace(path)
            )
            {
                this.LogWarn($"RecordPath: toolName, contextKey, or path cannot be empty");
                return;
            }

            string sanitizedPath = PathHelper.Sanitize(path);
            if (
                !sanitizedPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !AssetDatabase.IsValidFolder(sanitizedPath)
            )
            {
                if (
                    !Path.IsPathRooted(sanitizedPath)
                    && !sanitizedPath.StartsWith("Assets/", StringComparison.Ordinal)
                )
                {
                    this.LogWarn(
                        $"Recording path '{sanitizedPath}' that is not an 'Assets/' relative path or an absolute path. This might be intentional"
                    );
                }
            }

            ToolHistory tool = GetOrAddToolHistory(toolName);
            ContextHistory context = tool.GetOrAddContext(contextKey);
            DirectoryUsageData dirData = context.GetOrAddDirectory(sanitizedPath);
            dirData.MarkUsed();
            EditorUtility.SetDirty(this);
        }

        public DirectoryUsageData[] GetPaths(
            string toolName,
            string contextKey,
            bool topOnly = false,
            int topN = 5
        )
        {
            ToolHistory tool = _allToolHistories.Find(th =>
                string.Equals(th.toolName, toolName, StringComparison.Ordinal)
            );
            if (tool == null)
            {
                return Array.Empty<DirectoryUsageData>();
            }

            ContextHistory context = tool.contexts.Find(c =>
                string.Equals(c.contextKey, contextKey, StringComparison.Ordinal)
            );
            if (context == null)
            {
                return Array.Empty<DirectoryUsageData>();
            }

            List<DirectoryUsageData> list = context.directories;
            if (list == null || list.Count == 0)
            {
                return Array.Empty<DirectoryUsageData>();
            }

            DirectoryUsageData[] sortedDirectories = new DirectoryUsageData[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                sortedDirectories[i] = list[i];
            }
            Array.Sort(
                sortedDirectories,
                static (a, b) =>
                {
                    int cmp = b.count.CompareTo(a.count);
                    if (cmp != 0)
                    {
                        return cmp;
                    }
                    return b.lastUsedTicks.CompareTo(a.lastUsedTicks);
                }
            );

            if (!topOnly)
            {
                return sortedDirectories;
            }

            int n =
                topN < 0 ? 0 : (topN > sortedDirectories.Length ? sortedDirectories.Length : topN);
            if (n == sortedDirectories.Length)
            {
                return sortedDirectories;
            }
            DirectoryUsageData[] result = new DirectoryUsageData[n];
            for (int i = 0; i < n; i++)
            {
                result[i] = sortedDirectories[i];
            }
            return result;
        }

        internal static void MergeSettings(
            PersistentDirectorySettings target,
            PersistentDirectorySettings other
        )
        {
            if (other == null || target == null || ReferenceEquals(target, other))
            {
                return;
            }

            foreach (ToolHistory otherTool in other._allToolHistories)
            {
                if (otherTool == null || string.IsNullOrWhiteSpace(otherTool.toolName))
                {
                    continue;
                }

                ToolHistory targetTool = target._allToolHistories.Find(t =>
                    string.Equals(t.toolName, otherTool.toolName, StringComparison.Ordinal)
                );
                if (targetTool == null)
                {
                    // Deep copy to avoid shared references
                    ToolHistory copyTool = new(otherTool.toolName)
                    {
                        contexts = new List<ContextHistory>(),
                    };

                    foreach (ContextHistory oc in otherTool.contexts)
                    {
                        if (oc == null || string.IsNullOrWhiteSpace(oc.contextKey))
                        {
                            continue;
                        }
                        ContextHistory cc = new(oc.contextKey)
                        {
                            directories = new List<DirectoryUsageData>(),
                        };
                        foreach (DirectoryUsageData od in oc.directories)
                        {
                            if (od == null || string.IsNullOrWhiteSpace(od.path))
                            {
                                continue;
                            }
                            cc.directories.Add(
                                new DirectoryUsageData(od.path)
                                {
                                    count = od.count,
                                    lastUsedTicks = od.lastUsedTicks,
                                }
                            );
                        }
                        copyTool.contexts.Add(cc);
                    }

                    target._allToolHistories.Add(copyTool);
                    continue;
                }

                // Merge contexts
                foreach (ContextHistory otherContext in otherTool.contexts)
                {
                    if (otherContext == null || string.IsNullOrWhiteSpace(otherContext.contextKey))
                    {
                        continue;
                    }

                    ContextHistory targetContext = targetTool.contexts.Find(c =>
                        string.Equals(
                            c.contextKey,
                            otherContext.contextKey,
                            StringComparison.Ordinal
                        )
                    );
                    if (targetContext == null)
                    {
                        ContextHistory cc = new(otherContext.contextKey)
                        {
                            directories = new List<DirectoryUsageData>(),
                        };
                        foreach (DirectoryUsageData od in otherContext.directories)
                        {
                            if (od == null || string.IsNullOrWhiteSpace(od.path))
                            {
                                continue;
                            }
                            cc.directories.Add(
                                new DirectoryUsageData(od.path)
                                {
                                    count = od.count,
                                    lastUsedTicks = od.lastUsedTicks,
                                }
                            );
                        }
                        targetTool.contexts.Add(cc);
                        continue;
                    }

                    // Merge directories
                    foreach (DirectoryUsageData od in otherContext.directories)
                    {
                        if (od == null || string.IsNullOrWhiteSpace(od.path))
                        {
                            continue;
                        }
                        DirectoryUsageData td = targetContext.directories.Find(d =>
                            string.Equals(d.path, od.path, StringComparison.Ordinal)
                        );
                        if (td == null)
                        {
                            targetContext.directories.Add(
                                new DirectoryUsageData(od.path)
                                {
                                    count = od.count,
                                    lastUsedTicks = od.lastUsedTicks,
                                }
                            );
                        }
                        else
                        {
                            long maxTicks = Math.Max(td.lastUsedTicks, od.lastUsedTicks);
                            long sumCount = (long)td.count + od.count;
                            td.count = sumCount > int.MaxValue ? int.MaxValue : (int)sumCount;
                            td.lastUsedTicks = maxTicks;
                        }
                    }
                }
            }
        }

        private static string SanitizePath(string path)
        {
            return PathHelper.Sanitize(path);
        }

        private static bool PathsEqual(string a, string b)
        {
            return string.Equals(
                SanitizePath(a),
                SanitizePath(b),
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static void EnsureFolderExists(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            // Route through the single batch-safe folder helper. It pauses any active batch and
            // creates each missing segment via AssetDatabase.CreateFolder (never raw disk, which
            // would leave the AssetDatabase out of sync and spawn numbered duplicate folders).
            if (!AssetDatabaseBatchHelper.EnsureAssetFolder(folderPath))
            {
                UnityEngine.Debug.LogWarning(
                    $"PersistentDirectorySettings: Failed to ensure folder '{SanitizePath(folderPath)}'."
                );
            }
        }

        private static void TryDeleteEmptyParentFolders(string assetOrFolderPath)
        {
            try
            {
                string path = SanitizePath(assetOrFolderPath);
                string folder = AssetDatabase.IsValidFolder(path)
                    ? path
                    : Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(folder))
                {
                    return;
                }

                folder = SanitizePath(folder);
                while (
                    !string.IsNullOrWhiteSpace(folder)
                    && !string.Equals(folder, "Assets", StringComparison.OrdinalIgnoreCase)
                )
                {
                    if (!AssetDatabase.IsValidFolder(folder))
                    {
                        folder = Path.GetDirectoryName(folder);
                        folder = SanitizePath(folder);
                        continue;
                    }

                    string[] contents = AssetDatabase.FindAssets(string.Empty, new[] { folder });
                    if (contents == null || contents.Length == 0)
                    {
                        if (AssetDatabase.DeleteAsset(folder))
                        {
                            string parent = Path.GetDirectoryName(folder);
                            folder = SanitizePath(parent);
                            continue;
                        }
                    }
                    break;
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(
                    $"Failed to delete {assetOrFolderPath} with error: {e}."
                );
            }
        }
    }
#endif
}
