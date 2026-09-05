// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using Debug = UnityEngine.Debug;
    using Object = UnityEngine.Object;

    [InitializeOnLoad]
    public static class ScriptableObjectSingletonCreator
    {
        private const string ResourcesRoot = "Assets/Resources";
        private const string AssetImportWorkerEnvVar = "UNITY_ASSET_IMPORT_WORKER";
        private const string LegacyAssetImportWorkerEnvVar = "UNITY_ASSETIMPORT_WORKER";
        private const int MaxRetryAttempts = 10;

        private static bool _isEnsuring;
        private static bool _ensureScheduled;
        private static int _retryAttempts;
        private static int _consecutiveZeroProgressRetries;
        private static bool? _assetImportWorkerEnvCachedValue;
        private static Func<bool> _defaultAssetImportWorkerDetector;
        private static bool _mainThreadConfirmed;
        private static bool _mainThreadConfirmationPending;
        private static int _capturedMainThreadId;

        internal static bool VerboseLogging { get; set; }

        internal static bool IncludeTestAssemblies { get; set; }
        internal static bool DisableAutomaticRetries { get; set; }
        internal static Func<bool> AssetImportWorkerProcessCheck { get; set; }

        internal static Func<Type, bool> TypeFilter { get; set; }

        internal static bool IgnoreExclusionAttribute { get; set; }

        internal static bool AllowAssetCreationDuringSuppression { get; set; }

        // Tests can explicitly bypass compilation-state signals that lag asset operations.
        internal static bool IgnoreCompilationState { get; set; }

        static ScriptableObjectSingletonCreator()
        {
            // Defer asset creation until Unity initialization completes.
            EditorApplication.delayCall += EnsureSingletonAssets;
        }

        internal static void EnsureSingletonAssets()
        {
            CancelScheduledEnsureInvocation();

            // Automatic asset creation can open modal dialogs during tests; fixtures must explicitly opt in.
            if (EditorUi.Suppress && !AllowAssetCreationDuringSuppression)
            {
                LogVerbose(
                    "ScriptableObjectSingletonCreator: Skipping ensure because EditorUi.Suppress is true (test mode)."
                );
                return;
            }

            if (IsRunningInsideAssetImportWorkerProcess())
            {
                if (_mainThreadConfirmationPending)
                {
                    ScheduleEnsureSingletonAssets(false);
                    return;
                }

                LogVerbose(
                    "ScriptableObjectSingletonCreator: Skipping ensure while running inside asset import worker process."
                );
                return;
            }

            if (_isEnsuring)
            {
                LogVerbose(
                    "ScriptableObjectSingletonCreator: EnsureSingletonAssets re-entrancy prevented."
                );
                return;
            }

            // Wait until import and compilation finish before creating assets; tests may explicitly bypass this guard.
            if (
                !IgnoreCompilationState
                && (EditorApplication.isCompiling || EditorApplication.isUpdating)
            )
            {
                LogVerbose(
                    "ScriptableObjectSingletonCreator: Deferring ensure during compilation/updating."
                );
                ScheduleEnsureSingletonAssets(madeProgress: false);
                return;
            }

            _isEnsuring = true;
            bool anyChanges = false;
            bool retryRequested = false;
            int singletonsProcessed = 0;
            int singletonsSucceeded = 0;
            List<string> emptyFolderCandidates = null;

            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                try
                {
                    int staleCount = ScriptableObjectSingletonMetadataUtility.CleanupStaleEntries();
                    if (0 < staleCount)
                    {
                        LogVerbose(
                            $"ScriptableObjectSingletonCreator: Removed {staleCount} stale metadata entries."
                        );
                        anyChanges = true;
                    }

                    List<Type> allCandidates = new();
                    foreach (
                        Type t in ReflectionHelpers.GetTypesDerivedFrom(
                            typeof(UnityHelpers.Utils.ScriptableObjectSingleton<>),
                            includeAbstract: false
                        )
                    )
                    {
                        if (
                            !t.IsGenericType
                            && (IncludeTestAssemblies || !TestAssemblyHelper.IsTestType(t))
                            && (TypeFilter == null || TypeFilter(t))
                            && (
                                IgnoreExclusionAttribute
                                || !ReflectionHelpers.TryGetAttributeSafe<ExcludeFromSingletonCreationAttribute>(
                                    t,
                                    out _,
                                    inherit: false
                                )
                            )
                        )
                        {
                            allCandidates.Add(t);
                        }
                    }

                    Dictionary<string, List<Type>> byName = new(StringComparer.OrdinalIgnoreCase);
                    foreach (Type t in allCandidates)
                    {
                        List<Type> list = byName.GetOrAdd(t.Name);
                        list.Add(t);
                    }

                    HashSet<string> collisionLogged = new(StringComparer.OrdinalIgnoreCase);

                    foreach (Type derivedType in allCandidates)
                    {
                        singletonsProcessed++;

                        if (
                            byName.TryGetValue(derivedType.Name, out List<Type> group)
                            && 1 < group.Count
                        )
                        {
                            if (collisionLogged.Add(derivedType.Name))
                            {
                                Debug.LogWarning(
                                    $"ScriptableObjectSingletonCreator: Type name collision detected for '{derivedType.Name}'. Conflicting types: {string.Join(", ", group.ConvertAll(x => x.FullName))}. Skipping auto-creation. Consider adding [ScriptableSingletonPath] to disambiguate."
                                );
                            }
                            // Name collisions are permanently skipped, count as "success" to avoid retry loops
                            singletonsSucceeded++;
                            continue;
                        }

                        string resolvedResourcesRoot = EnsureAndResolveFolderPath(ResourcesRoot);
                        if (string.IsNullOrWhiteSpace(resolvedResourcesRoot))
                        {
                            Debug.LogError(
                                "ScriptableObjectSingletonCreator: Unable to resolve required Resources root folder. Aborting singleton auto-creation."
                            );
                            retryRequested = true;
                            break;
                        }

                        string resourcesSubFolder = GetResourcesSubFolder(derivedType);
                        string targetFolderRequested = CombinePaths(
                            ResourcesRoot,
                            resourcesSubFolder
                        );
                        string targetFolder = EnsureAndResolveFolderPath(targetFolderRequested);
                        if (string.IsNullOrWhiteSpace(targetFolder))
                        {
                            Debug.LogError(
                                $"ScriptableObjectSingletonCreator: Unable to ensure folder '{targetFolderRequested}' for singleton {derivedType.FullName}. Skipping asset creation."
                            );
                            retryRequested = true;
                            continue;
                        }

                        string targetAssetPath = CombinePaths(
                            targetFolder,
                            derivedType.Name + ".asset"
                        );

                        // Reuse occupied paths rather than creating duplicate numbered singleton assets.
                        Object assetAtTarget = AssetDatabase.LoadAssetAtPath(
                            targetAssetPath,
                            derivedType
                        );

                        string existingGuid = AssetDatabase.AssetPathToGUID(targetAssetPath);
                        bool fileExistsOnDisk = DoesAssetFileExistOnDisk(targetAssetPath);
                        if (
                            !string.IsNullOrEmpty(existingGuid)
                            && assetAtTarget == null
                            && !fileExistsOnDisk
                        )
                        {
                            TryRemoveStaleAssetArtifacts(targetAssetPath);
                            // Refresh only after the batch to avoid domain-reload loops.

                            assetAtTarget = AssetDatabase.LoadAssetAtPath(
                                targetAssetPath,
                                derivedType
                            );
                            fileExistsOnDisk = DoesAssetFileExistOnDisk(targetAssetPath);
                            string refreshedGuid = AssetDatabase.AssetPathToGUID(targetAssetPath);

                            existingGuid =
                                assetAtTarget == null && !fileExistsOnDisk
                                    ? string.Empty
                                    : refreshedGuid;
                        }

                        if (assetAtTarget == null)
                        {
                            assetAtTarget = MoveExistingAssetIfNeeded(
                                derivedType,
                                targetAssetPath,
                                ref anyChanges
                            );
                        }

                        if (assetAtTarget != null)
                        {
                            if (UpdateSingletonMetadataEntry(derivedType, targetAssetPath))
                            {
                                anyChanges = true;
                            }
                            singletonsSucceeded++;
                            continue;
                        }

                        // A lingering GUID without an asset body does not occupy the path; recreate missing assets.
                        if (
                            !string.IsNullOrEmpty(existingGuid)
                            && DoesAssetBodyExistOnDisk(targetAssetPath)
                        )
                        {
                            Debug.LogWarning(
                                $"ScriptableObjectSingletonCreator: Singleton target path already occupied at {targetAssetPath}. Skipping creation for {derivedType.FullName}."
                            );
                            // Path is occupied - this is a permanent skip, count as success to avoid retry loops
                            singletonsSucceeded++;
                            continue;
                        }

                        if (DoesAssetBodyExistOnDisk(targetAssetPath))
                        {
                            Debug.LogWarning(
                                $"ScriptableObjectSingletonCreator: Detected on-disk asset at {targetAssetPath} while ensuring {derivedType.FullName}. Unity has not imported it yet; deferring creation until the asset database picks it up."
                            );
                            retryRequested = true;
                            continue;
                        }

                        if (!string.IsNullOrEmpty(existingGuid))
                        {
                            // Remove orphan metadata before recreation for consistent path remapping across Unity versions.
                            TryRemoveStaleAssetArtifacts(targetAssetPath);
                        }

                        ScriptableObject instance = ScriptableObject.CreateInstance(derivedType);
                        try
                        {
                            // Register the parent outside batching before CreateAsset requires it.
                            AssetDatabaseBatchHelper.EnsureAssetParentFolder(targetAssetPath);
                            AssetDatabase.CreateAsset(instance, targetAssetPath);
                            // Import synchronously so the following LoadAssetAtPath can see the new asset.
                            AssetDatabase.ImportAsset(
                                targetAssetPath,
                                ImportAssetOptions.ForceSynchronousImport
                            );
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError(
                                $"ScriptableObjectSingletonCreator: Failed to create singleton for type {derivedType.FullName} at {targetAssetPath}. {ex.Message}"
                            );
                            // Creation may associate the instance with an asset before throwing; cleanup must allow asset destruction.
                            SafeDestroyInstance(instance, targetAssetPath);
                            retryRequested = true;
                            continue;
                        }

                        // Verify the asset was actually created - CreateAsset can fail silently
                        Object createdAsset = AssetDatabase.LoadAssetAtPath(
                            targetAssetPath,
                            derivedType
                        );
                        if (createdAsset == null)
                        {
                            bool assetExistsOnDisk = DoesAssetFileExistOnDisk(targetAssetPath);
                            if (assetExistsOnDisk)
                            {
                                LogVerbose(
                                    $"ScriptableObjectSingletonCreator: Asset file created at {targetAssetPath} but not yet visible to AssetDatabase. Will retry without deleting the file."
                                );
                                // Do not destroy an unindexed but valid asset instance; asset destruction could delete its file before retry.
                                retryRequested = true;
                                continue;
                            }

                            Debug.LogError(
                                $"ScriptableObjectSingletonCreator: CreateAsset appeared to succeed but asset not found at {targetAssetPath}. This may indicate a stale asset database state."
                            );

                            SafeDestroyInstance(instance, targetAssetPath);
                            retryRequested = true;
                            continue;
                        }

                        LogVerbose(
                            $"ScriptableObjectSingletonCreator: Created missing singleton for type {derivedType.FullName} at {targetAssetPath}."
                        );
                        UpdateSingletonMetadataEntry(derivedType, targetAssetPath);
                        anyChanges = true;
                        singletonsSucceeded++;
                    }

                    // Clean folders after batching ends so AssetDatabase reflects completed writes.
                    int duplicatesRemoved = CleanupDuplicateSingletonAssets(
                        allCandidates,
                        out emptyFolderCandidates
                    );
                    if (0 < duplicatesRemoved)
                    {
                        LogVerbose(
                            $"ScriptableObjectSingletonCreator: Removed {duplicatesRemoved} duplicate singleton assets."
                        );
                        anyChanges = true;
                    }
                }
                finally { }
            }

            _isEnsuring = false;

            bool foldersDeleted = false;
            if (emptyFolderCandidates is { Count: > 0 })
            {
                foldersDeleted = CleanupEmptyFolders(emptyFolderCandidates);
            }

            // Save and refresh once at the end to avoid reserialization loops.
            if (anyChanges || foldersDeleted)
            {
                AssetDatabase.SaveAssets();
                // Refresh during scene loading can deadlock; defer until critical initialization finishes.
                if (
                    EditorApplication.isCompiling
                    || EditorApplication.isUpdating
                    || EditorApplication.isPlayingOrWillChangePlaymode
                )
                {
                    EditorApplication.delayCall += () =>
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
                else
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
            }

            bool madeProgress = 0 < singletonsSucceeded || anyChanges;

            if (retryRequested && !DisableAutomaticRetries)
            {
                ScheduleEnsureSingletonAssets(madeProgress);
            }
            else
            {
                _retryAttempts = 0;
                _consecutiveZeroProgressRetries = 0;
                // Enable metadata warnings only after the initial asset-creation opportunity.
                MarkInitialEnsureCompleted();
            }

            if (VerboseLogging && 0 < singletonsProcessed)
            {
                LogVerbose(
                    $"ScriptableObjectSingletonCreator: Processed {singletonsProcessed} singleton types, {singletonsSucceeded} succeeded, retry={retryRequested}, progress={madeProgress}."
                );
            }
        }

        private static void MarkInitialEnsureCompleted()
        {
            UnityHelpers.Utils.ScriptableObjectSingletonInitState.InitialEnsureCompleted = true;
        }

        private static void ScheduleEnsureSingletonAssets(bool madeProgress)
        {
            if (_ensureScheduled)
            {
                return;
            }

            if (madeProgress)
            {
                _consecutiveZeroProgressRetries = 0;
            }
            else
            {
                _consecutiveZeroProgressRetries++;
            }

            // Prevents an infinite loop when every remaining singleton is permanently blocked.
            const int MaxZeroProgressRetries = 3;
            if (MaxZeroProgressRetries <= _consecutiveZeroProgressRetries)
            {
                Debug.LogWarning(
                    $"ScriptableObjectSingletonCreator: {MaxZeroProgressRetries} consecutive retry attempts made no progress. "
                        + "Further retries are suppressed. Check for permanent blockers (name collisions, missing folders, etc.)."
                );
                return;
            }

            if (MaxRetryAttempts <= _retryAttempts)
            {
                Debug.LogWarning(
                    $"ScriptableObjectSingletonCreator: Maximum automatic retry attempts ({MaxRetryAttempts}) reached. Further retries are suppressed to avoid infinite loops."
                );
                return;
            }

            _retryAttempts++;

            _ensureScheduled = true;
            EditorApplication.delayCall += RunScheduledEnsure;
        }

        private static void RunScheduledEnsure()
        {
            EditorApplication.delayCall -= RunScheduledEnsure;
            _ensureScheduled = false;
            EnsureSingletonAssets();
        }

        private static void CancelScheduledEnsureInvocation()
        {
            if (!_ensureScheduled)
            {
                return;
            }

            EditorApplication.delayCall -= RunScheduledEnsure;
            _ensureScheduled = false;

            if (0 < _retryAttempts)
            {
                _retryAttempts--;
            }
        }

        private static int CleanupDuplicateSingletonAssets(
            List<Type> candidateTypes,
            out List<string> emptyFolderCandidates
        )
        {
            int totalRemoved = 0;
            List<string> candidateFolders = new();

            foreach (Type derivedType in candidateTypes)
            {
                if (
                    !ReflectionHelpers.TryGetAttributeSafe<AllowDuplicateCleanupAttribute>(
                        derivedType,
                        out _,
                        inherit: false
                    )
                )
                {
                    continue;
                }

                int removed = CleanupDuplicatesForType(derivedType, candidateFolders);
                totalRemoved += removed;
            }

            emptyFolderCandidates = candidateFolders;
            return totalRemoved;
        }

        private static int CleanupDuplicatesForType(Type type, List<string> emptyFolderCandidates)
        {
            string resourcesSubFolder = GetResourcesSubFolder(type);
            string targetFolder = string.IsNullOrWhiteSpace(resourcesSubFolder)
                ? ResourcesRoot
                : CombinePaths(ResourcesRoot, resourcesSubFolder);
            string canonicalAssetPath = CombinePaths(targetFolder, type.Name + ".asset");
            canonicalAssetPath = NormalizePath(canonicalAssetPath);

            Object canonicalAsset = AssetDatabase.LoadAssetAtPath(canonicalAssetPath, type);
            if (canonicalAsset == null)
            {
                return 0;
            }

            string[] guids = AssetDatabase.FindAssets("t:" + type.Name, new[] { ResourcesRoot });
            if (guids == null || guids.Length <= 1)
            {
                return 0;
            }

            string canonicalJson = EditorJsonUtility.ToJson(canonicalAsset, prettyPrint: false);

            int removed = 0;

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(assetPath))
                {
                    continue;
                }

                string normalizedPath = NormalizePath(assetPath);
                if (
                    string.Equals(
                        normalizedPath,
                        canonicalAssetPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                Object duplicateAsset = AssetDatabase.LoadAssetAtPath(assetPath, type);
                if (duplicateAsset == null)
                {
                    continue;
                }

                string duplicateJson = EditorJsonUtility.ToJson(duplicateAsset, prettyPrint: false);
                if (!string.Equals(canonicalJson, duplicateJson, StringComparison.Ordinal))
                {
                    Debug.LogWarning(
                        $"ScriptableObjectSingletonCreator: Found duplicate singleton asset for {type.FullName} at '{assetPath}' with different content than canonical asset at '{canonicalAssetPath}'. Manual resolution required."
                    );
                    continue;
                }

                string parentFolder = Path.GetDirectoryName(assetPath)?.SanitizePath();

                // It may have been deleted by another process or by test cleanup.
                if (AssetDatabase.LoadAssetAtPath(assetPath, type) == null)
                {
                    LogVerbose(
                        $"ScriptableObjectSingletonCreator: Duplicate singleton asset for {type.FullName} at '{assetPath}' was already deleted."
                    );
                    continue;
                }

                if (AssetDatabase.DeleteAsset(assetPath))
                {
                    LogVerbose(
                        $"ScriptableObjectSingletonCreator: Deleted duplicate singleton asset for {type.FullName} at '{assetPath}' (identical to canonical at '{canonicalAssetPath}')."
                    );
                    removed++;

                    if (
                        !string.IsNullOrWhiteSpace(parentFolder)
                        && !string.Equals(
                            parentFolder,
                            targetFolder,
                            StringComparison.OrdinalIgnoreCase
                        )
                        && parentFolder.StartsWith(
                            ResourcesRoot,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        emptyFolderCandidates.Add(parentFolder);
                    }
                }
                else
                {
                    if (AssetDatabase.LoadAssetAtPath(assetPath, type) != null)
                    {
                        Debug.LogWarning(
                            $"ScriptableObjectSingletonCreator: Failed to delete duplicate singleton asset for {type.FullName} at '{assetPath}'."
                        );
                    }
                }
            }

            // The caller cleans folders after batching ends so AssetDatabase reflects completed writes.
            return removed;
        }

        private static bool CleanupEmptyFolders(List<string> folderPaths)
        {
            if (folderPaths == null || folderPaths.Count == 0)
            {
                return false;
            }

            folderPaths.Sort((a, b) => b.Split('/').Length.CompareTo(a.Split('/').Length));

            HashSet<string> processed = new(StringComparer.OrdinalIgnoreCase);
            bool anyDeleted = false;

            foreach (string folderPath in folderPaths)
            {
                if (CleanupEmptyFolderRecursive(folderPath, processed))
                {
                    anyDeleted = true;
                }
            }

            return anyDeleted;
        }

        private static bool CleanupEmptyFolderRecursive(
            string folderPath,
            HashSet<string> processed
        )
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return false;
            }

            string normalized = NormalizePath(folderPath);
            if (!processed.Add(normalized))
            {
                return false;
            }

            if (
                string.Equals(normalized, ResourcesRoot, StringComparison.OrdinalIgnoreCase)
                || !normalized.StartsWith(ResourcesRoot + "/", StringComparison.OrdinalIgnoreCase)
            )
            {
                return false;
            }

            // CRITICAL: Never delete the Wallstop Studios root folder - this is production data
            const string WallstopStudiosRoot = "Assets/Resources/Wallstop Studios";
            if (string.Equals(normalized, WallstopStudiosRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!AssetDatabase.IsValidFolder(normalized))
            {
                return false;
            }

            bool anyDeleted = false;

            string[] subfolders = AssetDatabase.GetSubFolders(normalized);
            if (subfolders is { Length: > 0 })
            {
                foreach (string subfolder in subfolders)
                {
                    if (CleanupEmptyFolderRecursive(subfolder, processed))
                    {
                        anyDeleted = true;
                    }
                }

                subfolders = AssetDatabase.GetSubFolders(normalized);
            }

            if (!AssetDatabase.IsValidFolder(normalized))
            {
                return anyDeleted;
            }

            // Note: FindAssets can emit a warning if folder is deleted between IsValidFolder check and this call
            string[] contents;
            try
            {
                contents = AssetDatabase.FindAssets(string.Empty, new[] { normalized });
            }
            catch
            {
                // Folder may have been deleted between check and FindAssets
                return anyDeleted;
            }

            if (!AssetDatabase.IsValidFolder(normalized))
            {
                return anyDeleted;
            }

            if (contents is { Length: > 0 })
            {
                bool hasDirectContents = false;
                foreach (string guid in contents)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    if (AssetDatabase.IsValidFolder(path))
                    {
                        continue;
                    }

                    string parent = Path.GetDirectoryName(path)?.SanitizePath();
                    if (string.Equals(parent, normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        hasDirectContents = true;
                        break;
                    }
                }

                if (hasDirectContents)
                {
                    return anyDeleted;
                }
            }

            subfolders = AssetDatabase.GetSubFolders(normalized);
            if (subfolders is { Length: > 0 })
            {
                return anyDeleted;
            }

            if (AssetDatabase.DeleteAsset(normalized))
            {
                LogVerbose(
                    $"ScriptableObjectSingletonCreator: Deleted empty folder '{normalized}'."
                );
                anyDeleted = true;

                string parentFolder = Path.GetDirectoryName(normalized)?.SanitizePath();
                if (!string.IsNullOrWhiteSpace(parentFolder))
                {
                    if (CleanupEmptyFolderRecursive(parentFolder, processed))
                    {
                        anyDeleted = true;
                    }
                }
            }

            return anyDeleted;
        }

        private static string GetResourcesSubFolder(Type type)
        {
            if (
                !ReflectionHelpers.TryGetAttributeSafe(
                    type,
                    out ScriptableSingletonPathAttribute attribute,
                    inherit: false
                )
            )
            {
                return string.Empty;
            }

            string path = attribute.resourcesPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.SanitizePath()?.Trim().Trim('/');
        }

        private static Object MoveExistingAssetIfNeeded(
            Type type,
            string targetAssetPath,
            ref bool anyChanges
        )
        {
            string normalizedTarget = NormalizePath(targetAssetPath);
            string assetName = Path.GetFileName(targetAssetPath);

            string targetParent = Path.GetDirectoryName(normalizedTarget)?.SanitizePath();
            if (!string.IsNullOrWhiteSpace(targetParent))
            {
                string resolvedParent = EnsureAndResolveFolderPath(targetParent);
                if (!string.IsNullOrWhiteSpace(resolvedParent))
                {
                    string rebuilt = CombinePaths(resolvedParent, assetName);
                    normalizedTarget = rebuilt;
                }
            }

            HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);
            List<string> candidatePaths = new();

            string[] guids = AssetDatabase.FindAssets("t:" + type.Name);
            if (guids != null && guids.Length != 0)
            {
                foreach (string guid in guids)
                {
                    AddCandidate(AssetDatabase.GUIDToAssetPath(guid));
                }
            }

            Object[] resourceInstances = Resources.LoadAll(string.Empty, type);
            if (resourceInstances != null)
            {
                foreach (Object instance in resourceInstances)
                {
                    if (instance == null)
                    {
                        continue;
                    }

                    string assetPath = AssetDatabase.GetAssetPath(instance);
                    AddCandidate(assetPath);
                }
            }

            // More targeted than searching every asset, and it catches newly created test assets.
            string[] resourceGuids = AssetDatabase.FindAssets(
                "t:ScriptableObject",
                new[] { ResourcesRoot }
            );
            if (resourceGuids != null)
            {
                foreach (string guid in resourceGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    Object obj = AssetDatabase.LoadAssetAtPath(path, type);
                    if (obj != null)
                    {
                        AddCandidate(path);
                    }
                }
            }

            if (seenPaths.Contains(normalizedTarget))
            {
                return AssetDatabase.LoadAssetAtPath(normalizedTarget, type);
            }

            foreach (string alternatePath in candidatePaths)
            {
                if (
                    string.Equals(
                        alternatePath,
                        normalizedTarget,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                Object asset = AssetDatabase.LoadAssetAtPath(alternatePath, type);
                if (asset == null)
                {
                    continue;
                }

                string parent = Path.GetDirectoryName(normalizedTarget)?.SanitizePath();
                if (!string.IsNullOrWhiteSpace(parent) && !AssetDatabase.IsValidFolder(parent))
                {
                    string ensured = EnsureAndResolveFolderPath(parent);
                    if (!string.IsNullOrWhiteSpace(ensured))
                    {
                        normalizedTarget = CombinePaths(ensured, assetName);
                    }
                }

                string moveResult = AssetDatabase.MoveAsset(alternatePath, normalizedTarget);
                if (string.IsNullOrEmpty(moveResult))
                {
                    LogVerbose(
                        $"Relocated singleton asset for type {type.Name} from {alternatePath} to {normalizedTarget}."
                    );
                    anyChanges = true;
                    return asset;
                }

                // Retry after ensuring parent folder exists (without intermediate Refresh to avoid domain reload loops)
                string parentDir = Path.GetDirectoryName(normalizedTarget)?.SanitizePath();
                bool retried = false;
                if (!string.IsNullOrWhiteSpace(parentDir))
                {
                    string resolvedParent = EnsureAndResolveFolderPath(parentDir);
                    if (!string.IsNullOrWhiteSpace(resolvedParent))
                    {
                        normalizedTarget = CombinePaths(resolvedParent, assetName);
                        parentDir = resolvedParent;
                    }

                    // Refresh after the batch to avoid domain-reload loops; folder registration uses ImportAsset.

                    if (AssetDatabase.IsValidFolder(parentDir))
                    {
                        string retry = AssetDatabase.MoveAsset(alternatePath, normalizedTarget);
                        retried = true;
                        if (string.IsNullOrEmpty(retry))
                        {
                            LogVerbose(
                                $"Relocated singleton asset for type {type.Name} from {alternatePath} to {normalizedTarget} after refresh."
                            );
                            anyChanges = true;
                            return asset;
                        }

                        moveResult = retry;
                    }
                    else
                    {
                        retried = true;
                        moveResult = "Parent directory is not in asset database (after retry)";
                    }
                }

                Debug.LogWarning(
                    $"Failed to move singleton asset {assetName} for type {type.Name} from {alternatePath}: {moveResult}{(retried ? " (after retry)" : string.Empty)}"
                );
            }

            return null;

            void AddCandidate(string rawPath)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    return;
                }

                string normalized = NormalizePath(rawPath);
                if (seenPaths.Add(normalized))
                {
                    candidatePaths.Add(normalized);
                }
            }
        }

        private static bool UpdateSingletonMetadataEntry(Type type, string assetPath)
        {
            string loadPath = ToResourcesLoadPath(assetPath);
            if (string.IsNullOrEmpty(loadPath))
            {
                return false;
            }

            string resourcesFolder = GetResourcesFolderFromLoadPath(loadPath);
            string guid = AssetDatabase.AssetPathToGUID(assetPath) ?? string.Empty;
            return ScriptableObjectSingletonMetadataUtility.UpdateEntry(
                type,
                loadPath,
                resourcesFolder,
                guid
            );
        }

        private static string ToResourcesLoadPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            string normalized = NormalizePath(assetPath);
            if (!normalized.StartsWith(ResourcesRoot, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string relative = normalized.Substring(ResourcesRoot.Length).TrimStart('/');
            if (string.IsNullOrWhiteSpace(relative))
            {
                return null;
            }

            if (relative.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                relative = relative.Substring(0, relative.Length - ".asset".Length);
            }

            return relative.Replace("\\", "/");
        }

        private static string GetResourcesFolderFromLoadPath(string loadPath)
        {
            if (string.IsNullOrWhiteSpace(loadPath))
            {
                return string.Empty;
            }

            string directory = Path.GetDirectoryName(loadPath);
            if (string.IsNullOrEmpty(directory))
            {
                return string.Empty;
            }

            return directory.Replace("\\", "/");
        }

        private static string CombinePaths(string left, string right)
        {
            if (string.IsNullOrEmpty(right))
            {
                return NormalizePath(left);
            }

            return NormalizePath(Path.Combine(left, right));
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string normalized = PathHelper.Sanitize(path.Trim());

            while (normalized.Contains("//"))
            {
                normalized = normalized.Replace("//", "/");
            }

            if (
                normalized.EndsWith("/")
                && !string.Equals(normalized, "Assets", StringComparison.Ordinal)
            )
            {
                normalized = normalized.TrimEnd('/');
            }

            return normalized;
        }

        private static string TryGetAbsoluteAssetsPath(string assetsRelativePath)
        {
            if (string.IsNullOrWhiteSpace(assetsRelativePath))
            {
                return string.Empty;
            }

            string normalized = NormalizePath(assetsRelativePath);
            if (!normalized.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            string projectRoot = Validation.AuthoredAssetPaths.ProjectRoot();
            if (string.IsNullOrEmpty(projectRoot))
            {
                return string.Empty;
            }

            string combined = Path.Combine(projectRoot, normalized);
            return Path.GetFullPath(combined);
        }

        private static bool DoesAssetFileExistOnDisk(string assetsRelativePath)
        {
            string absolutePath = TryGetAbsoluteAssetsPath(assetsRelativePath);
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return false;
            }

            if (File.Exists(absolutePath))
            {
                return true;
            }

            string metaPath = absolutePath + ".meta";
            return File.Exists(metaPath);
        }

        private static bool DoesAssetBodyExistOnDisk(string assetsRelativePath)
        {
            // A lingering GUID or metadata file does not prove the asset body exists; recreate body-less orphans.
            string absolutePath = TryGetAbsoluteAssetsPath(assetsRelativePath);
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return false;
            }

            return File.Exists(absolutePath);
        }

        /// <summary>
        /// Safely destroys a ScriptableObject instance after asset creation may have been attempted.
        /// When <see cref="AssetDatabase.CreateAsset"/> is called, the instance can become associated
        /// with an asset path in Unity's internal state even if the import fails (e.g., "Unable to import
        /// newly created asset" errors). In this case, calling <see cref="Object.DestroyImmediate(Object)"/>
        /// without <c>allowDestroyingAssets=true</c> results in a "Destroying assets is not permitted" error.
        /// This method also cleans up any partially created files on disk.
        /// </summary>
        /// <param name="instance">The ScriptableObject instance to destroy.</param>
        /// <param name="targetAssetPath">The asset path where creation was attempted (for cleanup).</param>
        private static void SafeDestroyInstance(ScriptableObject instance, string targetAssetPath)
        {
            if (instance == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(targetAssetPath))
            {
                TryCleanupPartiallyCreatedAsset(targetAssetPath);
            }

            // Creation may attach the instance to an asset before import fails, so cleanup must allow asset destruction.
            try
            {
                Object.DestroyImmediate(instance, true);
            }
            catch (Exception ex)
            {
                // If destroy still fails, log but don't rethrow - we've done our best
                LogVerbose(
                    $"ScriptableObjectSingletonCreator: Failed to destroy instance after failed asset creation: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// Attempts to clean up any partially created asset files on disk.
        /// This handles the case where CreateAsset wrote the file but Unity failed to import it.
        /// </summary>
        private static void TryCleanupPartiallyCreatedAsset(string assetsRelativePath)
        {
            try
            {
                if (AssetDatabase.DeleteAsset(assetsRelativePath))
                {
                    LogVerbose(
                        $"ScriptableObjectSingletonCreator: Cleaned up partially created asset at {assetsRelativePath}."
                    );
                    return;
                }
            }
            catch (Exception) { }

            string absolutePath = TryGetAbsoluteAssetsPath(assetsRelativePath);
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return;
            }

            bool cleaned = false;
            try
            {
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                    cleaned = true;
                }

                string metaPath = absolutePath + ".meta";
                if (File.Exists(metaPath))
                {
                    File.Delete(metaPath);
                    cleaned = true;
                }
            }
            catch (Exception ex)
            {
                LogVerbose(
                    $"ScriptableObjectSingletonCreator: Failed to clean up partially created asset files at {absolutePath}: {ex.Message}"
                );
            }

            if (cleaned)
            {
                LogVerbose(
                    $"ScriptableObjectSingletonCreator: Cleaned up partially created asset files at {absolutePath}."
                );
            }
        }

        private static bool TryRemoveStaleAssetArtifacts(string assetsRelativePath)
        {
            bool removed = false;

            // Pause batching to remove stale GUID mappings before creating a replacement at the same path.
            using (AssetDatabaseBatchHelper.PauseBatch())
            {
                try
                {
                    if (AssetDatabase.DeleteAsset(assetsRelativePath))
                    {
                        removed = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"ScriptableObjectSingletonCreator: AssetDatabase.DeleteAsset threw while cleaning stale singleton artifacts at '{assetsRelativePath}': {ex.Message}"
                    );
                }

                string absoluteAssetPathInner = TryGetAbsoluteAssetsPath(assetsRelativePath);
                string absoluteMetaPathInner = TryGetAbsoluteAssetsPath(
                    assetsRelativePath + ".meta"
                );

                try
                {
                    if (
                        !string.IsNullOrWhiteSpace(absoluteAssetPathInner)
                        && File.Exists(absoluteAssetPathInner)
                    )
                    {
                        File.Delete(absoluteAssetPathInner);
                        removed = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"ScriptableObjectSingletonCreator: Failed deleting stale asset file '{absoluteAssetPathInner}': {ex.Message}"
                    );
                }

                try
                {
                    if (
                        !string.IsNullOrWhiteSpace(absoluteMetaPathInner)
                        && File.Exists(absoluteMetaPathInner)
                    )
                    {
                        File.Delete(absoluteMetaPathInner);
                        removed = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"ScriptableObjectSingletonCreator: Failed deleting stale meta file '{absoluteMetaPathInner}': {ex.Message}"
                    );
                }

                // Import the missing-body path synchronously so its stale GUID mapping is removed before recreation.
                if (removed)
                {
                    try
                    {
                        AssetDatabase.ImportAsset(
                            NormalizePath(assetsRelativePath),
                            ImportAssetOptions.ForceUpdate
                        );
                    }
                    catch (Exception ex)
                    {
                        LogVerbose(
                            $"ScriptableObjectSingletonCreator: ImportAsset after stale-artifact removal at '{assetsRelativePath}' reported: {ex.Message}"
                        );
                    }
                }
            }

            if (removed)
            {
                LogVerbose(
                    $"ScriptableObjectSingletonCreator: Cleared stale artifacts blocking singleton creation at '{assetsRelativePath}'."
                );
            }

            return removed;
        }

        private static string EnsureAndResolveFolderPath(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return string.Empty;
            }

            folderPath = NormalizePath(folderPath);
            string[] parts = folderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return string.Empty;
            }

            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return ResolveExistingFolderPath(folderPath);
            }

            string current = "Assets";
            if (!string.Equals(parts[0], current, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning(
                    $"Unable to ensure folder for path '{folderPath}' because it does not start with 'Assets'."
                );
                return folderPath;
            }

            for (int i = 1; i < parts.Length; i++)
            {
                string desiredName = parts[i];

                string matchedExisting = FindMatchingSubfolder(current, desiredName);

                if (string.IsNullOrEmpty(matchedExisting))
                {
                    string intendedPath = current + "/" + desiredName;

                    // The batch-aware folder helper adopts unindexed directories and removes empty numbered duplicates.
                    if (AssetDatabaseBatchHelper.EnsureAssetFolder(intendedPath))
                    {
                        current = ResolveExistingFolderPath(intendedPath);
                        continue;
                    }

                    Debug.LogError(
                        $"ScriptableObjectSingletonCreator: Failed to create folder '{intendedPath}'."
                    );
                    return string.Empty;
                }
                else
                {
                    string intendedPath = current + "/" + desiredName;
                    if (string.Equals(matchedExisting, intendedPath, StringComparison.Ordinal))
                    {
                        // Filesystem matches may remain unindexed during batching; register them before CreateAsset needs them.
                        if (!AssetDatabase.IsValidFolder(matchedExisting))
                        {
                            AssetDatabaseBatchHelper.EnsureAssetFolder(matchedExisting);
                        }

                        current = matchedExisting;
                    }
                    else
                    {
                        string renameError = AssetDatabase.MoveAsset(matchedExisting, intendedPath);
                        if (string.IsNullOrEmpty(renameError))
                        {
                            LogVerbose(
                                $"ScriptableObjectSingletonCreator: Renamed folder '{matchedExisting}' to '{intendedPath}' to correct casing."
                            );
                            current = intendedPath;
                        }
                        else
                        {
                            // Case-only renames may require an intermediate name on case-insensitive filesystems.
                            string currentTerminal = matchedExisting;
                            int ls = currentTerminal.LastIndexOf('/', currentTerminal.Length - 1);
                            currentTerminal =
                                0 <= ls ? currentTerminal.Substring(ls + 1) : currentTerminal;

                            if (
                                string.Equals(
                                    currentTerminal,
                                    desiredName,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            {
                                string tempName = desiredName + "__CaseFix__";
                                string tempPath = current + "/" + tempName;
                                string toTempErr = AssetDatabase.MoveAsset(
                                    matchedExisting,
                                    tempPath
                                );
                                if (string.IsNullOrEmpty(toTempErr))
                                {
                                    string toFinalErr = AssetDatabase.MoveAsset(
                                        tempPath,
                                        intendedPath
                                    );
                                    if (string.IsNullOrEmpty(toFinalErr))
                                    {
                                        LogVerbose(
                                            $"ScriptableObjectSingletonCreator: Renamed folder '{matchedExisting}' to '{intendedPath}' via temporary '{tempPath}' to correct casing."
                                        );
                                        current = intendedPath;
                                    }
                                    else
                                    {
                                        LogVerbose(
                                            $"ScriptableObjectSingletonCreator: Reusing existing folder '{matchedExisting}' for requested segment '{desiredName}' (final case-fix rename failed: {toFinalErr})."
                                        );
                                        current = matchedExisting;
                                    }
                                }
                                else
                                {
                                    LogVerbose(
                                        $"ScriptableObjectSingletonCreator: Reusing existing folder '{matchedExisting}' for requested segment '{desiredName}' (case-fix temp rename failed: {toTempErr})."
                                    );
                                    current = matchedExisting;
                                }
                            }
                            else
                            {
                                LogVerbose(
                                    $"ScriptableObjectSingletonCreator: Reusing existing folder '{matchedExisting}' for requested segment '{desiredName}' (rename failed: {renameError})."
                                );
                                current = matchedExisting;
                            }
                        }
                    }
                }
            }

            return current;
        }

        private static string FindMatchingSubfolder(string parent, string desiredName)
        {
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(desiredName))
            {
                return null;
            }

            // Check disk before stale AssetDatabase listings so unindexed folders with different casing are found.
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(projectRoot))
            {
                string absoluteParent = Path.Combine(projectRoot, parent).SanitizePath();
                if (Directory.Exists(absoluteParent))
                {
                    try
                    {
                        string[] diskFolders = Directory.GetDirectories(absoluteParent);
                        foreach (string diskFolder in diskFolders)
                        {
                            string folderName = Path.GetFileName(diskFolder);
                            if (
                                string.Equals(
                                    folderName,
                                    desiredName,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            {
                                string matchedPath = parent + "/" + folderName;

                                if (
                                    !AssetDatabaseBatchHelper.IsCurrentlyBatching
                                    && !AssetDatabase.IsValidFolder(matchedPath)
                                )
                                {
                                    AssetDatabase.ImportAsset(
                                        matchedPath,
                                        ImportAssetOptions.ForceSynchronousImport
                                    );
                                }

                                return matchedPath;
                            }
                        }
                    }
                    catch { }
                }
            }

            // Fallback: try AssetDatabase (may have stale data inside editing scope)
            string[] subFolders = AssetDatabase.GetSubFolders(parent);
            if (subFolders is { Length: > 0 })
            {
                foreach (string sub in subFolders)
                {
                    int lastSlash = sub.LastIndexOf('/', sub.Length - 1);
                    string terminal = 0 <= lastSlash ? sub.Substring(lastSlash + 1) : sub;
                    if (string.Equals(terminal, desiredName, StringComparison.OrdinalIgnoreCase))
                    {
                        return sub;
                    }
                }
            }

            return null;
        }

        private static string ResolveExistingFolderPath(string intended)
        {
            if (string.IsNullOrWhiteSpace(intended))
            {
                return string.Empty;
            }

            intended = NormalizePath(intended);
            string[] parts = intended.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return string.Empty;
            }

            string current = parts[0];
            if (!string.Equals(current, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                return intended;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);

            for (int i = 1; i < parts.Length; i++)
            {
                string desired = parts[i];

                if (!string.IsNullOrEmpty(projectRoot))
                {
                    string absoluteCurrent = Path.Combine(projectRoot, current).SanitizePath();
                    if (Directory.Exists(absoluteCurrent))
                    {
                        try
                        {
                            string[] diskFolders = Directory.GetDirectories(absoluteCurrent);
                            foreach (string diskFolder in diskFolders)
                            {
                                string folderName = Path.GetFileName(diskFolder);
                                if (
                                    string.Equals(
                                        folderName,
                                        desired,
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                )
                                {
                                    current = current + "/" + folderName;
                                    goto NextPart;
                                }
                            }
                        }
                        catch { }
                    }
                }

                string[] subs = AssetDatabase.GetSubFolders(current);
                if (subs is { Length: > 0 })
                {
                    foreach (string sub in subs)
                    {
                        int last = sub.LastIndexOf('/', sub.Length - 1);
                        string name = 0 <= last ? sub.Substring(last + 1) : sub;
                        if (string.Equals(name, desired, StringComparison.OrdinalIgnoreCase))
                        {
                            current = sub;
                            goto NextPart;
                        }
                    }
                }

                string next = current + "/" + desired;
                if (AssetDatabase.IsValidFolder(next))
                {
                    current = next;
                    continue;
                }
                return intended;

                NextPart:
                ;
            }

            return current;
        }

        /// <summary>
        /// Checks if a folder name is a numbered duplicate of the desired name.
        /// Unity creates numbered duplicates like "Resources 1", "Resources 2" when
        /// parallel operations try to create the same folder simultaneously.
        /// </summary>
        /// <param name="actualName">The actual folder name that was created.</param>
        /// <param name="desiredName">The intended folder name.</param>
        /// <returns>True if actualName matches the pattern "desiredName N" where N is a number.</returns>
        internal static bool IsNumberedDuplicate(string actualName, string desiredName)
        {
            if (
                string.IsNullOrEmpty(actualName)
                || string.IsNullOrEmpty(desiredName)
                || actualName.Length <= desiredName.Length
            )
            {
                return false;
            }

            if (!actualName.StartsWith(desiredName + " ", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string suffix = actualName.Substring(desiredName.Length + 1);

            // Reject extra whitespace before parsing; int.TryParse would otherwise accept it.
            if (suffix.Length == 0 || char.IsWhiteSpace(suffix[0]))
            {
                return false;
            }

            return int.TryParse(suffix, out int number) && 0 < number;
        }

        private static bool IsRunningInsideAssetImportWorkerProcess()
        {
            Func<bool> detectorOverride = AssetImportWorkerProcessCheck;
            if (detectorOverride != null)
            {
                _mainThreadConfirmationPending = false;
                return InvokeDetector(detectorOverride, assumeWorkerOnFailure: false);
            }

            if (IsAssetImportWorkerProcessViaEnvironment())
            {
                _mainThreadConfirmationPending = false;
                return true;
            }

            if (!TryConfirmEditorMainThread())
            {
                LogVerbose(
                    "ScriptableObjectSingletonCreator: Main thread not yet confirmed; deferring singleton ensure."
                );
                _mainThreadConfirmationPending = true;
                return true;
            }

            _mainThreadConfirmationPending = false;
            _defaultAssetImportWorkerDetector ??= AssetDatabase.IsAssetImportWorkerProcess;
            return InvokeDetector(_defaultAssetImportWorkerDetector, assumeWorkerOnFailure: false);
        }

        private static bool InvokeDetector(Func<bool> detector, bool assumeWorkerOnFailure)
        {
            try
            {
                return detector();
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"ScriptableObjectSingletonCreator: Asset import worker detector threw {ex.GetType().Name}: {ex.Message}. {(assumeWorkerOnFailure ? "Assuming import worker context." : "Assuming main editor process.")}"
                );
                return assumeWorkerOnFailure;
            }
        }

        private static bool TryConfirmEditorMainThread()
        {
            if (_mainThreadConfirmed)
            {
                return Thread.CurrentThread.ManagedThreadId == _capturedMainThreadId;
            }

            if (!UnityMainThreadGuard.IsMainThread)
            {
                return false;
            }

            _capturedMainThreadId = Thread.CurrentThread.ManagedThreadId;
            _mainThreadConfirmed = true;
            return true;
        }

        private static bool IsAssetImportWorkerProcessViaEnvironment()
        {
            if (_assetImportWorkerEnvCachedValue.HasValue)
            {
                return _assetImportWorkerEnvCachedValue.Value;
            }

            if (IsTruthy(Environment.GetEnvironmentVariable(AssetImportWorkerEnvVar)))
            {
                _assetImportWorkerEnvCachedValue = true;
                return true;
            }

            if (IsTruthy(Environment.GetEnvironmentVariable(LegacyAssetImportWorkerEnvVar)))
            {
                _assetImportWorkerEnvCachedValue = true;
                return true;
            }

            IDictionary variables = null;
            try
            {
                variables = Environment.GetEnvironmentVariables();
            }
            catch (Exception ex)
            {
                LogVerbose(
                    $"ScriptableObjectSingletonCreator: Unable to enumerate environment variables for worker detection: {ex.Message}"
                );
            }

            if (variables != null)
            {
                foreach (DictionaryEntry entry in variables)
                {
                    if (
                        entry.Key is not string key
                        || key.IndexOf(
                            "UNITY_ASSET_IMPORT_WORKER",
                            StringComparison.OrdinalIgnoreCase
                        ) < 0
                        || entry.Value is not string candidateValue
                    )
                    {
                        continue;
                    }

                    if (IsTruthy(candidateValue))
                    {
                        _assetImportWorkerEnvCachedValue = true;
                        return true;
                    }
                }
            }

            _assetImportWorkerEnvCachedValue = false;
            return false;

            static bool IsTruthy(string candidate)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    return false;
                }

                string normalized = candidate.Trim();
                return !string.Equals(normalized, "0", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void LogVerbose(string message)
        {
            if (VerboseLogging)
            {
                Debug.Log(message);
            }
        }

        [Conditional("UNITY_INCLUDE_TESTS")]
        internal static void ResetAssetImportWorkerDetectionStateForTests()
        {
            _assetImportWorkerEnvCachedValue = null;
            _defaultAssetImportWorkerDetector = null;
            _mainThreadConfirmed = false;
            _mainThreadConfirmationPending = false;
            _capturedMainThreadId = 0;
        }

        [Conditional("UNITY_INCLUDE_TESTS")]
        internal static void ResetRetryStateForTests()
        {
            _retryAttempts = 0;
            _consecutiveZeroProgressRetries = 0;
            CancelScheduledEnsureInvocation();
        }

        [Conditional("UNITY_INCLUDE_TESTS")]
        internal static void ResetInitialEnsureStateForTests()
        {
            UnityHelpers.Utils.ScriptableObjectSingletonInitState.InitialEnsureCompleted = false;
        }

        /// <summary>
        /// Resets state for testing. Cleanup of AssetDatabase batch state is now handled
        /// by the unified <see cref="AssetDatabaseBatchHelper"/>.
        /// </summary>
        internal static void ResetAssetEditingScopeDepthForTesting()
        {
            _isEnsuring = false;

            CancelScheduledEnsureInvocation();
            _retryAttempts = 0;
            _consecutiveZeroProgressRetries = 0;
        }
    }
#endif
}
