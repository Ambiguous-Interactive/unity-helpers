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

        // Prevents reentrant execution during domain reloads/asset refreshes
        private static bool _isEnsuring;
        private static bool _ensureScheduled;
        private static int _retryAttempts;
        private static int _consecutiveZeroProgressRetries;
        private static bool? _assetImportWorkerEnvCachedValue;
        private static Func<bool> _defaultAssetImportWorkerDetector;
        private static bool _mainThreadConfirmed;
        private static bool _mainThreadConfirmationPending;
        private static int _capturedMainThreadId;

        // Controls whether informational logs are emitted. Warnings still always log.
        internal static bool VerboseLogging { get; set; }

        internal static bool IncludeTestAssemblies { get; set; }
        internal static bool DisableAutomaticRetries { get; set; }
        internal static Func<bool> AssetImportWorkerProcessCheck { get; set; }

        // Optional hook so tests can restrict the candidate singleton types that auto-creation processes.
        internal static Func<Type, bool> TypeFilter { get; set; }

        // When true, types with [ExcludeFromSingletonCreation] will still be processed.
        // This is intended for testing scenarios only.
        internal static bool IgnoreExclusionAttribute { get; set; }

        // When true, allows EnsureSingletonAssets to run even when EditorUi.Suppress is true.
        // This is for tests that need to explicitly invoke singleton asset creation.
        internal static bool AllowAssetCreationDuringSuppression { get; set; }

        // When true, bypasses the EditorApplication.isCompiling/isUpdating check.
        // This is for tests that need to explicitly invoke singleton asset creation
        // regardless of Unity's compilation state, which can report false positives
        // during test runs after AssetDatabase operations.
        internal static bool IgnoreCompilationState { get; set; }

        static ScriptableObjectSingletonCreator()
        {
            // Defer singleton asset creation to avoid conflicts during Unity initialization.
            // EditorApplication.delayCall ensures we run after Unity is fully loaded.
            EditorApplication.delayCall += EnsureSingletonAssets;
        }

        internal static void EnsureSingletonAssets()
        {
            CancelScheduledEnsureInvocation();

            // Skip automatic asset creation during test runs to avoid Unity's internal modal dialogs
            // when asset operations fail. Tests that need singleton assets must set
            // AllowAssetCreationDuringSuppression = true before calling EnsureSingletonAssets.
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

            // Defer asset creation during compilation or asset database updates to avoid
            // "Unable to import newly created asset" errors that occur when Unity is in
            // an intermediate state during domain reloads or asset imports.
            // Tests can bypass this check by setting IgnoreCompilationState = true.
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

            // Use unified batch helper - disable auto-refresh since we handle it manually
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                try
                {
                    // Clean up stale metadata entries that point to non-existent assets
                    int staleCount = ScriptableObjectSingletonMetadataUtility.CleanupStaleEntries();
                    if (staleCount > 0)
                    {
                        LogVerbose(
                            $"ScriptableObjectSingletonCreator: Removed {staleCount} stale metadata entries."
                        );
                        anyChanges = true;
                    }

                    // Collect candidate types once and detect simple name collisions (same class name, different namespaces)
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

                    // Build collision map by simple type name
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

                        // Skip name-collision types to avoid creating overlapping assets like TypeName.asset
                        if (
                            byName.TryGetValue(derivedType.Name, out List<Type> group)
                            && group.Count > 1
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

                        // Extra safety: if any asset exists at the exact path, do not create a duplicate.
                        // Prefer to use/move existing assets rather than generating unique names.
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
                            // Note: Removed intermediate Refresh here - we defer all refreshes to the end
                            // to avoid domain reload loops during processing

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

                        // Only a real on-disk asset body means the path is genuinely occupied. A
                        // lingering GUID/.meta with no body (the body was deleted; Unity 6000.3+ keeps
                        // the path->GUID mapping) is a stale artifact, not an occupant -- fall through
                        // and recreate rather than permanently skipping (the bug that left
                        // RecreatesAssetWhenGuidRemainsButFileIsMissing red on Unity 6).
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
                            // Reaching here means a GUID/.meta lingers but no asset body exists on
                            // disk (Unity 6000.3+ keeps the path->GUID mapping after the body is
                            // deleted). Remove the orphan .meta first so CreateAsset below re-maps the
                            // path deterministically on every Unity version instead of depending on
                            // version-specific .meta-adoption behavior.
                            TryRemoveStaleAssetArtifacts(targetAssetPath);
                        }

                        ScriptableObject instance = ScriptableObject.CreateInstance(derivedType);
                        try
                        {
                            // The fixture-wide batch opened above defers CreateFolder, so the
                            // resolved target folder may not be registered with the AssetDatabase
                            // at this point. Re-assert the parent folder through the pause-aware
                            // helper so CreateAsset cannot fail with "Parent directory must exist".
                            AssetDatabaseBatchHelper.EnsureAssetParentFolder(targetAssetPath);
                            AssetDatabase.CreateAsset(instance, targetAssetPath);
                            // Force Unity to import the asset synchronously so LoadAssetAtPath works immediately.
                            // This avoids the race condition where the file exists on disk but
                            // AssetDatabase hasn't indexed it yet.
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
                            // Use allowDestroyingAssets=true because CreateAsset may have partially succeeded,
                            // associating the instance with an asset before throwing the exception.
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
                            // Check if file exists on disk but Unity hasn't imported it yet
                            bool assetExistsOnDisk = DoesAssetFileExistOnDisk(targetAssetPath);
                            if (assetExistsOnDisk)
                            {
                                LogVerbose(
                                    $"ScriptableObjectSingletonCreator: Asset file created at {targetAssetPath} but not yet visible to AssetDatabase. Will retry without deleting the file."
                                );
                                // DON'T destroy the instance here - the file is valid, just not imported yet.
                                // The in-memory instance may be associated with the asset path, and calling
                                // DestroyImmediate with allowDestroyingAssets=true could potentially delete
                                // the on-disk file. Let the instance be garbage collected; the retry logic
                                // will load the asset fresh from disk after AssetDatabase.Refresh().
                                retryRequested = true;
                                continue;
                            }

                            Debug.LogError(
                                $"ScriptableObjectSingletonCreator: CreateAsset appeared to succeed but asset not found at {targetAssetPath}. This may indicate a stale asset database state."
                            );
                            // File doesn't exist on disk - this is a real failure, clean up
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

                    // Cleanup duplicate singleton assets for types that have opted in
                    // Folder cleanup is deferred to after StopAssetEditing() for proper AssetDatabase sync
                    int duplicatesRemoved = CleanupDuplicateSingletonAssets(
                        allCandidates,
                        out emptyFolderCandidates
                    );
                    if (duplicatesRemoved > 0)
                    {
                        LogVerbose(
                            $"ScriptableObjectSingletonCreator: Removed {duplicatesRemoved} duplicate singleton assets."
                        );
                        anyChanges = true;
                    }
                }
                finally
                {
                    // Note: AssetDatabase.StopAssetEditing() is handled by the using block's Dispose
                }
            } // End of using block - AssetDatabase batch operations are now complete

            // Post-batch cleanup - these operations must happen AFTER StopAssetEditing
            _isEnsuring = false;

            // Clean up empty folders AFTER StopAssetEditing
            // Track if cleanup actually deleted anything
            bool foldersDeleted = false;
            if (emptyFolderCandidates is { Count: > 0 })
            {
                foldersDeleted = CleanupEmptyFolders(emptyFolderCandidates);
            }

            // Only do ONE SaveAssets/Refresh at the very end to avoid re-serialization loops
            if (anyChanges || foldersDeleted)
            {
                AssetDatabase.SaveAssets();
                // Defer Refresh if Unity is in a state where it could cause a deadlock
                // (e.g., during scene loading which triggers "Open Project: Open Scene" hang)
                if (
                    EditorApplication.isCompiling
                    || EditorApplication.isUpdating
                    || EditorApplication.isPlayingOrWillChangePlaymode
                )
                {
                    // Defer the refresh to avoid blocking during critical operations
                    EditorApplication.delayCall += () =>
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
                else
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
            }

            // Determine if we made progress this run
            bool madeProgress = singletonsSucceeded > 0 || anyChanges;

            if (retryRequested && !DisableAutomaticRetries)
            {
                ScheduleEnsureSingletonAssets(madeProgress);
            }
            else
            {
                _retryAttempts = 0;
                _consecutiveZeroProgressRetries = 0;
                // Mark initial ensure as completed - this enables metadata-related warnings
                // that were suppressed during early initialization
                MarkInitialEnsureCompleted();
            }

            if (VerboseLogging && singletonsProcessed > 0)
            {
                LogVerbose(
                    $"ScriptableObjectSingletonCreator: Processed {singletonsProcessed} singleton types, {singletonsSucceeded} succeeded, retry={retryRequested}, progress={madeProgress}."
                );
            }
        }

        private static void MarkInitialEnsureCompleted()
        {
            // Mark initial ensure as completed globally
            // This enables warnings that were suppressed during early Unity initialization
            UnityHelpers.Utils.ScriptableObjectSingletonInitState.InitialEnsureCompleted = true;
        }

        private static void ScheduleEnsureSingletonAssets(bool madeProgress)
        {
            if (_ensureScheduled)
            {
                return;
            }

            // If we made progress (created/validated at least one singleton), reset the
            // zero-progress counter since the system is making forward progress
            if (madeProgress)
            {
                _consecutiveZeroProgressRetries = 0;
            }
            else
            {
                _consecutiveZeroProgressRetries++;
            }

            // Use a stricter limit for consecutive zero-progress retries
            // This prevents infinite loops when all remaining singletons are permanently blocked
            const int MaxZeroProgressRetries = 3;
            if (_consecutiveZeroProgressRetries >= MaxZeroProgressRetries)
            {
                Debug.LogWarning(
                    $"ScriptableObjectSingletonCreator: {MaxZeroProgressRetries} consecutive retry attempts made no progress. "
                        + "Further retries are suppressed. Check for permanent blockers (name collisions, missing folders, etc.)."
                );
                return;
            }

            if (_retryAttempts >= MaxRetryAttempts)
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

            if (_retryAttempts > 0)
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
            emptyFolderCandidates = new List<string>();

            foreach (Type derivedType in candidateTypes)
            {
                // Only process types that have opted into duplicate cleanup
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

                int removed = CleanupDuplicatesForType(derivedType, emptyFolderCandidates);
                totalRemoved += removed;
            }

            return totalRemoved;
        }

        private static int CleanupDuplicatesForType(Type type, List<string> emptyFolderCandidates)
        {
            // Get the canonical path for this type
            string resourcesSubFolder = GetResourcesSubFolder(type);
            string targetFolder = string.IsNullOrWhiteSpace(resourcesSubFolder)
                ? ResourcesRoot
                : CombinePaths(ResourcesRoot, resourcesSubFolder);
            string canonicalAssetPath = CombinePaths(targetFolder, type.Name + ".asset");
            canonicalAssetPath = NormalizePath(canonicalAssetPath);

            // Load the canonical asset
            Object canonicalAsset = AssetDatabase.LoadAssetAtPath(canonicalAssetPath, type);
            if (canonicalAsset == null)
            {
                // No canonical asset exists - nothing to compare against
                return 0;
            }

            // Find all assets of this type under Resources
            string[] guids = AssetDatabase.FindAssets("t:" + type.Name, new[] { ResourcesRoot });
            if (guids == null || guids.Length <= 1)
            {
                // No duplicates possible
                return 0;
            }

            // Get the serialized content of the canonical asset for comparison
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
                    // This is the canonical asset - skip
                    continue;
                }

                Object duplicateAsset = AssetDatabase.LoadAssetAtPath(assetPath, type);
                if (duplicateAsset == null)
                {
                    continue;
                }

                // Compare serialized content
                string duplicateJson = EditorJsonUtility.ToJson(duplicateAsset, prettyPrint: false);
                if (!string.Equals(canonicalJson, duplicateJson, StringComparison.Ordinal))
                {
                    // Content differs - this is a real duplicate with different data
                    // Only warn, don't delete
                    Debug.LogWarning(
                        $"ScriptableObjectSingletonCreator: Found duplicate singleton asset for {type.FullName} at '{assetPath}' with different content than canonical asset at '{canonicalAssetPath}'. Manual resolution required."
                    );
                    continue;
                }

                // Content is identical - safe to delete
                string parentFolder = Path.GetDirectoryName(assetPath)?.SanitizePath();

                // Verify the asset still exists before attempting deletion
                // (it may have been deleted by another process or test cleanup)
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

                    // Track parent folder for potential cleanup
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
                    // Only warn if the asset actually exists but couldn't be deleted
                    if (AssetDatabase.LoadAssetAtPath(assetPath, type) != null)
                    {
                        Debug.LogWarning(
                            $"ScriptableObjectSingletonCreator: Failed to delete duplicate singleton asset for {type.FullName} at '{assetPath}'."
                        );
                    }
                }
            }

            // Note: Folder cleanup is now handled by the caller AFTER StopAssetEditing()
            // to ensure AssetDatabase operations are properly committed
            return removed;
        }

        private static bool CleanupEmptyFolders(List<string> folderPaths)
        {
            if (folderPaths == null || folderPaths.Count == 0)
            {
                return false;
            }

            // Sort by depth (deepest first) to clean up bottom-up
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

            // Normalize and check if already processed
            string normalized = NormalizePath(folderPath);
            if (!processed.Add(normalized))
            {
                return false;
            }

            // Don't delete the Resources root or above
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

            // Check if folder exists and is valid
            if (!AssetDatabase.IsValidFolder(normalized))
            {
                return false;
            }

            bool anyDeleted = false;

            // First, recursively clean up any empty subfolders
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

                // Re-check subfolders after recursive cleanup - some may have been deleted
                subfolders = AssetDatabase.GetSubFolders(normalized);
            }

            // Re-check folder validity after subfolder cleanup
            if (!AssetDatabase.IsValidFolder(normalized))
            {
                return anyDeleted;
            }

            // Check if folder has any direct asset contents (not subfolders)
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

            // Re-check folder validity in case it was deleted during FindAssets
            if (!AssetDatabase.IsValidFolder(normalized))
            {
                return anyDeleted;
            }

            if (contents is { Length: > 0 })
            {
                // Folder has contents - check if they're all in subfolders (which would be non-empty subfolders)
                bool hasDirectContents = false;
                foreach (string guid in contents)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    // Skip if this is a subfolder itself
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

            // Re-check for subfolders one more time - if any remain, don't delete
            subfolders = AssetDatabase.GetSubFolders(normalized);
            if (subfolders is { Length: > 0 })
            {
                return anyDeleted;
            }

            // Folder is empty - try to delete it
            if (AssetDatabase.DeleteAsset(normalized))
            {
                LogVerbose(
                    $"ScriptableObjectSingletonCreator: Deleted empty folder '{normalized}'."
                );
                anyDeleted = true;

                // Try to clean up parent folder
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

            // Ensure the target parent folder exists and use its exact-cased path
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

            // Try finding by script name (won't work for nested/private classes but harmless to try)
            string[] guids = AssetDatabase.FindAssets("t:" + type.Name);
            if (guids != null && guids.Length != 0)
            {
                foreach (string guid in guids)
                {
                    AddCandidate(AssetDatabase.GUIDToAssetPath(guid));
                }
            }

            // Load from Resources - this works for already-indexed assets
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

            // Search only within Assets/Resources folder for ScriptableObject assets of this type
            // This is more targeted than searching all assets and catches newly created test assets
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

                // Final guard: ensure parent exists just before moving
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
                    // Ensure parent folder exists and get its resolved path
                    string resolvedParent = EnsureAndResolveFolderPath(parentDir);
                    if (!string.IsNullOrWhiteSpace(resolvedParent))
                    {
                        normalizedTarget = CombinePaths(resolvedParent, assetName);
                        parentDir = resolvedParent;
                    }

                    // Note: Removed intermediate SaveAssets/Refresh here - we defer all refreshes to the end
                    // to avoid domain reload loops during processing. The folder should be registered via ImportAsset.

                    // Verify parent folder is now valid in AssetDatabase
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

            // Trim whitespace and normalize separators
            string normalized = PathHelper.Sanitize(path.Trim());

            // Collapse duplicate separators
            while (normalized.Contains("//"))
            {
                normalized = normalized.Replace("//", "/");
            }

            // Remove trailing separator (except for bare "Assets")
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

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                return string.Empty;
            }

            string combined = Path.Combine(projectRoot, normalized);
            return Path.GetFullPath(combined);
        }

        private static bool ProjectDirectoryExists(string assetsRelativePath)
        {
            string absolutePath = TryGetAbsoluteAssetsPath(assetsRelativePath);
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return false;
            }

            return Directory.Exists(absolutePath);
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
            // The .asset BODY only -- deliberately ignores a lingering .asset.meta. Unity 6000.3+
            // retains the path->GUID mapping (and the .meta) for an asset whose body was deleted, so
            // "a GUID/.meta exists" is NOT proof a real asset is present. Creation decisions must key
            // on the body file, or a body-less orphan permanently blocks recreation of the singleton.
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

            // Clean up any partially created files on disk
            if (!string.IsNullOrWhiteSpace(targetAssetPath))
            {
                TryCleanupPartiallyCreatedAsset(targetAssetPath);
            }

            // Use allowDestroyingAssets=true because CreateAsset may have partially succeeded,
            // associating the instance with an asset even if the import failed.
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
                // First try to delete via AssetDatabase
                if (AssetDatabase.DeleteAsset(assetsRelativePath))
                {
                    LogVerbose(
                        $"ScriptableObjectSingletonCreator: Cleaned up partially created asset at {assetsRelativePath}."
                    );
                    return;
                }
            }
            catch (Exception)
            {
                // AssetDatabase.DeleteAsset may fail, continue to file system cleanup
            }

            // If AssetDatabase couldn't delete it, try file system directly
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

            string absoluteAssetPath = TryGetAbsoluteAssetsPath(assetsRelativePath);
            string absoluteMetaPath = TryGetAbsoluteAssetsPath(assetsRelativePath + ".meta");

            try
            {
                if (!string.IsNullOrWhiteSpace(absoluteAssetPath) && File.Exists(absoluteAssetPath))
                {
                    File.Delete(absoluteAssetPath);
                    removed = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"ScriptableObjectSingletonCreator: Failed deleting stale asset file '{absoluteAssetPath}': {ex.Message}"
                );
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(absoluteMetaPath) && File.Exists(absoluteMetaPath))
                {
                    File.Delete(absoluteMetaPath);
                    removed = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"ScriptableObjectSingletonCreator: Failed deleting stale meta file '{absoluteMetaPath}': {ex.Message}"
                );
            }

            if (removed)
            {
                // Note: Removed intermediate Refresh here - we defer all refreshes to the end
                // to avoid domain reload loops during processing
                LogVerbose(
                    $"ScriptableObjectSingletonCreator: Cleared stale artifacts blocking singleton creation at '{assetsRelativePath}'."
                );
            }

            return removed;
        }

        private static bool EnsureFolderExistsOnDisk(string assetsRelativePath)
        {
            string absolutePath = TryGetAbsoluteAssetsPath(assetsRelativePath);
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(absolutePath);
                if (!Directory.Exists(absolutePath))
                {
                    return false;
                }

                return RegisterFolderWithAssetDatabase(assetsRelativePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"ScriptableObjectSingletonCreator: Directory.CreateDirectory fallback failed for '{assetsRelativePath}': {ex.Message}"
                );
                return false;
            }
        }

        private static bool RegisterFolderWithAssetDatabase(string assetsRelativePath)
        {
            string normalized = NormalizePath(assetsRelativePath);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (AssetDatabase.IsValidFolder(normalized))
            {
                return true;
            }

            // If we're inside a batch scope, we need to temporarily exit to allow ImportAsset to work
            using (AssetDatabaseBatchHelper.PauseBatch())
            {
                try
                {
                    AssetDatabase.ImportAsset(normalized, ImportAssetOptions.ForceUpdate);
                    if (AssetDatabase.IsValidFolder(normalized))
                    {
                        return true;
                    }

                    // Note: Removed intermediate Refresh here - we defer all refreshes to the end
                    // to avoid domain reload loops during processing. ImportAsset should be sufficient.
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"ScriptableObjectSingletonCreator: Failed to register folder '{normalized}' with AssetDatabase: {ex.Message}"
                    );
                }

                return AssetDatabase.IsValidFolder(normalized);
            }
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

            // If the whole folder already exists, return the exact casing Unity knows
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                // Resolve to exact-cased path by walking down from Assets
                return ResolveExistingFolderPath(folderPath);
            }

            // Always anchor to Unity's "Assets" root with correct casing
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
                    if (ProjectDirectoryExists(intendedPath))
                    {
                        if (EnsureFolderExistsOnDisk(intendedPath))
                        {
                            current = ResolveExistingFolderPath(intendedPath);
                            LogVerbose(
                                $"ScriptableObjectSingletonCreator: Registered existing folder '{current}'."
                            );
                            continue;
                        }
                    }

                    // Final check before CreateFolder: the folder may now exist in AssetDatabase
                    // (e.g., after a refresh or import) even though FindMatchingSubfolder missed it.
                    // This prevents Unity from creating numbered duplicates like "Resources 1".
                    if (AssetDatabase.IsValidFolder(intendedPath))
                    {
                        current = ResolveExistingFolderPath(intendedPath);
                        continue;
                    }

                    string createdGuid = string.Empty;
                    string createdPath = string.Empty;
                    try
                    {
                        createdGuid = AssetDatabase.CreateFolder(current, desiredName);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(
                            $"ScriptableObjectSingletonCreator: AssetDatabase.CreateFolder threw while ensuring '{intendedPath}'. Falling back to disk creation. {ex.Message}"
                        );
                    }

                    if (!string.IsNullOrEmpty(createdGuid))
                    {
                        createdPath = NormalizePath(AssetDatabase.GUIDToAssetPath(createdGuid));

                        // Race condition detection: Unity may create numbered duplicates like "Resources 1"
                        // when parallel tests try to create the same folder simultaneously.
                        // Check if createdPath differs from intendedPath and matches the pattern "Name N".
                        if (
                            !string.IsNullOrEmpty(createdPath)
                            && !string.Equals(
                                createdPath,
                                intendedPath,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            string createdName = Path.GetFileName(createdPath);
                            if (IsNumberedDuplicate(createdName, desiredName))
                            {
                                // This is a numbered duplicate created due to race condition.
                                // Delete it and check if the intended folder now exists.
                                LogVerbose(
                                    $"ScriptableObjectSingletonCreator: Detected numbered duplicate folder '{createdPath}' (intended: '{intendedPath}'). Deleting duplicate."
                                );
                                bool duplicateDeleted = AssetDatabase.DeleteAsset(createdPath);
                                if (!duplicateDeleted)
                                {
                                    Debug.LogWarning(
                                        $"ScriptableObjectSingletonCreator: Failed to delete numbered duplicate folder '{createdPath}'. Manual cleanup may be required."
                                    );
                                }
                                createdGuid = string.Empty;
                                createdPath = string.Empty;

                                // Re-check if intended folder now exists (created by concurrent operation)
                                string matchAfterDelete = FindMatchingSubfolder(
                                    current,
                                    desiredName
                                );
                                if (!string.IsNullOrEmpty(matchAfterDelete))
                                {
                                    current = matchAfterDelete;
                                    continue;
                                }

                                // Also check via IsValidFolder in case FindMatchingSubfolder missed it
                                if (AssetDatabase.IsValidFolder(intendedPath))
                                {
                                    current = ResolveExistingFolderPath(intendedPath);
                                    continue;
                                }

                                // Folder still doesn't exist - try disk creation fallback
                                if (EnsureFolderExistsOnDisk(intendedPath))
                                {
                                    createdPath = intendedPath;
                                }
                            }
                        }
                    }
                    else if (EnsureFolderExistsOnDisk(intendedPath))
                    {
                        createdPath = intendedPath;
                    }

                    string actualPath = FindMatchingSubfolder(current, desiredName);
                    if (string.IsNullOrEmpty(actualPath))
                    {
                        actualPath = createdPath;
                    }

                    bool intendedValid = AssetDatabase.IsValidFolder(intendedPath);
                    bool actualValid =
                        !string.IsNullOrEmpty(actualPath)
                        && AssetDatabase.IsValidFolder(actualPath);

                    if (!intendedValid && !actualValid)
                    {
                        bool directoryExists =
                            ProjectDirectoryExists(intendedPath)
                            || (
                                !string.IsNullOrEmpty(actualPath)
                                && ProjectDirectoryExists(actualPath)
                            );
                        if (directoryExists)
                        {
                            RegisterFolderWithAssetDatabase(intendedPath);
                            if (!string.IsNullOrEmpty(actualPath))
                            {
                                RegisterFolderWithAssetDatabase(actualPath);
                            }
                        }
                        if (directoryExists || !string.IsNullOrEmpty(createdGuid))
                        {
                            ForceAssetDatabaseSync();
                        }

                        intendedValid = AssetDatabase.IsValidFolder(intendedPath);
                        if (!intendedValid)
                        {
                            actualPath = FindMatchingSubfolder(current, desiredName);
                            if (
                                string.IsNullOrEmpty(actualPath)
                                && !string.IsNullOrEmpty(createdGuid)
                            )
                            {
                                actualPath = NormalizePath(
                                    AssetDatabase.GUIDToAssetPath(createdGuid)
                                );
                            }

                            if (string.IsNullOrEmpty(actualPath) && directoryExists)
                            {
                                actualPath = intendedPath;
                            }

                            actualValid =
                                !string.IsNullOrEmpty(actualPath)
                                && AssetDatabase.IsValidFolder(actualPath);
                        }
                        else
                        {
                            actualPath = intendedPath;
                            actualValid = true;
                        }
                    }

                    if (intendedValid)
                    {
                        current = ResolveExistingFolderPath(intendedPath);
                        LogVerbose(
                            $"ScriptableObjectSingletonCreator: Created folder '{current}'."
                        );
                        continue;
                    }

                    if (
                        actualValid
                        && string.Equals(
                            actualPath,
                            intendedPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        string renameError = AssetDatabase.MoveAsset(actualPath, intendedPath);
                        if (string.IsNullOrEmpty(renameError))
                        {
                            LogVerbose(
                                $"ScriptableObjectSingletonCreator: Renamed folder '{actualPath}' to '{intendedPath}' to correct casing."
                            );
                            current = ResolveExistingFolderPath(intendedPath);
                            continue;
                        }

                        string lastError = renameError;
                        string currentTerminal = actualPath;
                        int ls = currentTerminal.LastIndexOf('/', currentTerminal.Length - 1);
                        currentTerminal =
                            ls >= 0 ? currentTerminal.Substring(ls + 1) : currentTerminal;
                        string desiredTerminal = desiredName;

                        if (
                            string.Equals(
                                currentTerminal,
                                desiredTerminal,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            string tempName = desiredTerminal + "__CaseFix__";
                            string tempPath = current + "/" + tempName;
                            string toTempErr = AssetDatabase.MoveAsset(actualPath, tempPath);
                            if (string.IsNullOrEmpty(toTempErr))
                            {
                                string toFinalErr = AssetDatabase.MoveAsset(tempPath, intendedPath);
                                if (string.IsNullOrEmpty(toFinalErr))
                                {
                                    LogVerbose(
                                        $"ScriptableObjectSingletonCreator: Renamed folder '{actualPath}' to '{intendedPath}' via temporary '{tempPath}' to correct casing."
                                    );
                                    current = ResolveExistingFolderPath(intendedPath);
                                    continue;
                                }

                                lastError = toFinalErr;
                            }
                            else
                            {
                                lastError = toTempErr;
                            }
                        }

                        DeleteCreatedFolder(actualPath, createdGuid);
                        Debug.LogError(
                            $"ScriptableObjectSingletonCreator: Unable to correct folder casing from '{actualPath}' to '{intendedPath}' (last error: {lastError})."
                        );
                        return string.Empty;
                    }

                    if (actualValid && AssetDatabase.IsValidFolder(actualPath))
                    {
                        DeleteCreatedFolder(actualPath, createdGuid);
                        Debug.LogError(
                            $"ScriptableObjectSingletonCreator: Expected to create folder '{intendedPath}', but Unity created '{actualPath}'. Aborting to avoid duplicate folders."
                        );
                        return string.Empty;
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
                        // Exact match, just continue
                        current = matchedExisting;
                    }
                    else
                    {
                        // Case-insensitive match with different casing. Attempt to rename to the intended casing
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
                            // Some platforms/filesystems require a two-step rename to change only casing
                            // Attempt rename -> temp -> intended when paths differ only by case
                            string currentTerminal = matchedExisting;
                            int ls = currentTerminal.LastIndexOf('/', currentTerminal.Length - 1);
                            currentTerminal =
                                ls >= 0 ? currentTerminal.Substring(ls + 1) : currentTerminal;

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
                                // If mismatch isn't purely casing, fall back to using existing
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

            // IMPORTANT: Check disk FIRST for case-insensitive file systems (Windows/macOS)
            // This is critical because:
            // 1. Inside StartAssetEditing/StopAssetEditing scope, GetSubFolders may return stale data
            // 2. On case-insensitive file systems, a folder may exist on disk with different casing
            //    that AssetDatabase doesn't know about yet
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
                                // Found a case-insensitive match on disk
                                // Return the path in Unity format using the actual disk casing
                                string matchedPath = parent + "/" + folderName;

                                // Try to ensure it's registered in AssetDatabase (outside of editing scope)
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
                    catch
                    {
                        // If disk access fails, fall through to AssetDatabase check
                    }
                }
            }

            // Fallback: try AssetDatabase (may have stale data inside editing scope)
            string[] subFolders = AssetDatabase.GetSubFolders(parent);
            if (subFolders is { Length: > 0 })
            {
                foreach (string sub in subFolders)
                {
                    int lastSlash = sub.LastIndexOf('/', sub.Length - 1);
                    string terminal = lastSlash >= 0 ? sub.Substring(lastSlash + 1) : sub;
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

                // Check disk FIRST to get actual casing (critical for case-insensitive file systems)
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
                                    // Found match on disk - use actual casing
                                    current = current + "/" + folderName;
                                    goto NextPart;
                                }
                            }
                        }
                        catch
                        {
                            // If disk access fails, fall through to AssetDatabase check
                        }
                    }
                }

                // Fallback: try AssetDatabase
                string[] subs = AssetDatabase.GetSubFolders(current);
                if (subs is { Length: > 0 })
                {
                    foreach (string sub in subs)
                    {
                        int last = sub.LastIndexOf('/', sub.Length - 1);
                        string name = last >= 0 ? sub.Substring(last + 1) : sub;
                        if (string.Equals(name, desired, StringComparison.OrdinalIgnoreCase))
                        {
                            current = sub;
                            goto NextPart;
                        }
                    }
                }

                // No match found - check if intended path exists anyway
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

        private static void ForceAssetDatabaseSync()
        {
            // Skip if we're in the ensure scope to avoid domain reload loops
            // The final Refresh in EnsureSingletonAssets will sync everything
            if (_isEnsuring)
            {
                return;
            }

            using (AssetDatabaseBatchHelper.PauseBatch())
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        private static void DeleteCreatedFolder(string candidatePath, string createdGuid)
        {
            string path = candidatePath;
            if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrEmpty(createdGuid))
            {
                path = NormalizePath(AssetDatabase.GUIDToAssetPath(createdGuid));
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            if (!AssetDatabase.DeleteAsset(path))
            {
                Debug.LogWarning(
                    $"ScriptableObjectSingletonCreator: Temporary folder '{path}' was created while attempting to ensure a target path, but it could not be removed."
                );
            }
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

            // Check if actualName starts with desiredName followed by a space
            if (!actualName.StartsWith(desiredName + " ", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Extract the suffix after "desiredName "
            string suffix = actualName.Substring(desiredName.Length + 1);

            // Reject if suffix starts with whitespace (handles double-space like "Folder  1")
            // int.TryParse would otherwise accept " 1" as valid since it trims whitespace
            if (suffix.Length == 0 || char.IsWhiteSpace(suffix[0]))
            {
                return false;
            }

            // Check if the suffix is a positive integer
            return int.TryParse(suffix, out int number) && number > 0;
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

            // Also reset related state that could affect subsequent tests
            CancelScheduledEnsureInvocation();
            _retryAttempts = 0;
            _consecutiveZeroProgressRetries = 0;

            // AssetDatabase batch cleanup is now handled by AssetDatabaseBatchHelper.ResetBatchDepth()
            // which is called by CommonTestBase in setUp/tearDown
        }
    }
#endif
}
