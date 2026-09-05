// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.AssetProcessors
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Settings;

    internal sealed class DetectAssetChangeProcessor : AssetPostprocessor
    {
        private const string TestAssetFolderMarker = "__DetectAssetChangedTests__";
        private const string SupportedSignatureDescription =
            "Supported signatures: () with no parameters; (AssetChangeContext context); or (TAsset[] createdAssets, string[] deletedAssetPaths) where TAsset derives from UnityEngine.Object.";
        private const string InfiniteLoopWarning =
            "[DetectAssetChanged] Detected a potentially infinite asset change loop triggered by DetectAssetChanged handlers. Additional change batches will be skipped to prevent recursion until the editor domain reloads. Please fix the offending callbacks.";

        internal const int MaxPendingChangeSetsPerCycle = 32;
        internal const int MaxConsecutiveChangeSetsWithinWindow = 128;

        private static readonly Func<double> DefaultTimeProvider = () =>
            EditorApplication.timeSinceStartup;

        private static readonly Dictionary<Type, AssetWatcher> WatchersByAssetType = new();
        private static readonly Queue<PendingAssetChangeSet> PendingAssetChanges = new();
        private static bool _initialized;
        private static bool _includeTestAssets;
        private static List<string> _testAssetFolderAllowlist;
        private static bool _processingAssetChanges;
        private static bool _loopProtectionActive;
        private static int _consecutiveChangeBatches;
        private static double _lastChangeProcessTimestamp;
        private static Func<double> _timeProvider = DefaultTimeProvider;
        private static double? _loopWindowSecondsOverride;
        private static bool _diagnosticsEnabled;
        private static bool? _enabledOverride;

        private static readonly Action DrainPendingChangesAction = ProcessPendingAssetChangesCore;

        internal static Func<double> TimeProvider
        {
            get => _timeProvider;
            set => _timeProvider = value ?? DefaultTimeProvider;
        }

        internal static double? LoopWindowSecondsOverride
        {
            get => _loopWindowSecondsOverride;
            set => _loopWindowSecondsOverride = value;
        }

        internal static bool IncludeTestAssets
        {
            get => _includeTestAssets;
            set => _includeTestAssets = value;
        }

        /// <summary>
        /// Test-only: when non-null AND <see cref="IncludeTestAssets"/> is <see langword="true"/>,
        /// asset paths not starting with any of the listed prefixes are skipped. Lets each
        /// test fixture declare its own folder so cross-fixture pollution is structurally
        /// impossible even if <c>Clear()</c> is forgotten. Production code never sets this,
        /// and <see cref="ShouldSkipPath"/> treats <see langword="null"/> as "no allowlist —
        /// preserve legacy include-all-test-assets behavior."
        /// </summary>
        internal static IReadOnlyList<string> TestAssetFolderAllowlist
        {
            get => _testAssetFolderAllowlist;
            set => _testAssetFolderAllowlist = value == null ? null : new List<string>(value);
        }

        /// <summary>
        /// Enables diagnostic logging for debugging asset change detection behavior.
        /// When enabled, logs detailed information about instance enumeration and search options.
        /// </summary>
        internal static bool DiagnosticsEnabled
        {
            get => _diagnosticsEnabled;
            set => _diagnosticsEnabled = value;
        }

        /// <summary>
        /// Forces the watcher on or off, or restores the default policy when
        /// <see langword="null"/>.
        /// </summary>
        internal static bool? EnabledOverride
        {
            get => _enabledOverride;
            set => _enabledOverride = value;
        }

        /// <summary>
        /// Whether the watcher may initialize. Defaults to off in batch mode.
        /// </summary>
        /// <remarks>
        /// <see cref="BuildWatchers"/> is an all-types / all-methods reflection scan. Running it
        /// inside Unity's import phase destabilizes the asset pipeline: a native mono crash
        /// (STATUS_ACCESS_VIOLATION inside GetMethodsByName_native) on some Unity versions,
        /// multi-minute importer stalls on others. The play-mode guard in
        /// <see cref="OnPostprocessAllAssets"/> covers one door into that scan; a headless
        /// `-batchmode` run is not play mode and went through the other one. The watcher is an
        /// editor-authoring convenience, and a headless run has no author to act on a callback, so
        /// the scan there is unobservable work at best and a crash at worst. A headless asset
        /// pipeline that does want the watcher can opt back in through
        /// <see cref="AssetChangeDetectionUtility.Enabled"/>.
        /// </remarks>
        internal static bool IsEnabled => _enabledOverride ?? !Application.isBatchMode;

        static DetectAssetChangeProcessor()
        {
            EditorApplication.delayCall += EnsureInitialized;
        }

        internal static void ProcessChangesForTesting(
            string[] imported,
            string[] deleted,
            string[] moved,
            string[] movedFrom
        )
        {
            EnsureInitialized(force: true);
            EnqueueAssetChanges(
                imported ?? Array.Empty<string>(),
                deleted ?? Array.Empty<string>(),
                moved ?? Array.Empty<string>(),
                movedFrom ?? Array.Empty<string>(),
                deferProcessing: false
            );
        }

        internal static AssetWatcherSettings GetSettingsForTesting()
        {
            return new AssetWatcherSettings
            {
                Initialized = _initialized,
                IncludeTestAssets = _includeTestAssets,
                TestAssetFolderAllowlist =
                    _testAssetFolderAllowlist == null
                        ? null
                        : new List<string>(_testAssetFolderAllowlist),
                WatchersByAssetType = new Dictionary<Type, AssetWatcher>(WatchersByAssetType),
                PendingAssetChanges = new Queue<PendingAssetChangeSet>(PendingAssetChanges),
                ProcessingAssetChanges = _processingAssetChanges,
                LoopProtectionActive = _loopProtectionActive,
                ConsecutiveChangeBatches = _consecutiveChangeBatches,
                LastChangeProcessTimestamp = _lastChangeProcessTimestamp,
                TimeProvider = _timeProvider,
                LoopWindowSecondsOverride = _loopWindowSecondsOverride,
                DiagnosticsEnabled = _diagnosticsEnabled,
                EnabledOverride = _enabledOverride,
            };
        }

        internal static void ResetForTesting(AssetWatcherSettings settings = null)
        {
            _initialized = settings?.Initialized ?? false;
            IncludeTestAssets = settings?.IncludeTestAssets ?? false;
            _testAssetFolderAllowlist =
                settings?.TestAssetFolderAllowlist == null
                    ? null
                    : new List<string>(settings.TestAssetFolderAllowlist);
            WatchersByAssetType.Clear();
            foreach (
                var kvp in settings?.WatchersByAssetType
                    ?? Enumerable.Empty<KeyValuePair<Type, AssetWatcher>>()
            )
            {
                WatchersByAssetType.Add(kvp.Key, kvp.Value);
            }
            PendingAssetChanges.Clear();
            foreach (
                var pendingChange in settings?.PendingAssetChanges
                    ?? Enumerable.Empty<PendingAssetChangeSet>()
            )
            {
                PendingAssetChanges.Enqueue(pendingChange);
            }
            _processingAssetChanges = settings?.ProcessingAssetChanges ?? false;
            _loopProtectionActive = settings?.LoopProtectionActive ?? false;
            _consecutiveChangeBatches = settings?.ConsecutiveChangeBatches ?? 0;
            _lastChangeProcessTimestamp = settings?.LastChangeProcessTimestamp ?? 0;
            TimeProvider = settings?.TimeProvider ?? DefaultTimeProvider;
            LoopWindowSecondsOverride = settings?.LoopWindowSecondsOverride;
            DiagnosticsEnabled = settings?.DiagnosticsEnabled ?? false;
            _enabledOverride = settings?.EnabledOverride;
        }

        internal static void EnsureInitializedForTesting()
        {
            EnsureInitialized();
        }

        internal static void ResetLoopProtection()
        {
            _loopProtectionActive = false;
            _consecutiveChangeBatches = 0;
            _lastChangeProcessTimestamp = 0d;
            PendingAssetChanges.Clear();
        }

        internal static bool ValidateMethodSignatureForTesting(
            Type declaringType,
            string methodName
        )
        {
            if (declaringType == null)
            {
                throw new ArgumentNullException(nameof(declaringType));
            }

            if (string.IsNullOrWhiteSpace(methodName))
            {
                throw new ArgumentException(nameof(methodName));
            }

            BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic;
            MethodInfo method = declaringType.GetMethod(methodName, flags);
            if (method == null)
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Method {0}.{1} was not found.",
                        declaringType.FullName,
                        methodName
                    ),
                    nameof(methodName)
                );
            }

            return TryResolveParameterMode(declaringType, method, out _, out _);
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            // Play-mode asset changes must not trigger a reflection scan recursively inside Unity import.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            EnsureInitialized();
            if (WatchersByAssetType.Count == 0)
            {
                return;
            }

            EnqueueAssetChanges(
                importedAssets,
                deletedAssets,
                movedAssets,
                movedFromAssetPaths,
                deferProcessing: true
            );
        }

        private static void EnqueueAssetChanges(
            IReadOnlyList<string> importedAssets,
            IReadOnlyList<string> deletedAssets,
            IReadOnlyList<string> movedAssets,
            IReadOnlyList<string> movedFromAssetPaths,
            bool deferProcessing
        )
        {
            if (_loopProtectionActive)
            {
                PendingAssetChanges.Clear();
                return;
            }

            PendingAssetChanges.Enqueue(
                new PendingAssetChangeSet(
                    importedAssets,
                    deletedAssets,
                    movedAssets,
                    movedFromAssetPaths
                )
            );

            if (deferProcessing)
            {
                // Defer beyond import guards; type-only questions must still use metadata because loading invokes consumer OnValidate.
                AssetPostprocessorDeferral.Schedule(DrainPendingChangesAction);
                return;
            }

            ProcessPendingAssetChangesCore();
        }

        private static void ProcessPendingAssetChangesCore()
        {
            if (_loopProtectionActive)
            {
                PendingAssetChanges.Clear();
                return;
            }

            if (_processingAssetChanges)
            {
                return;
            }

            _processingAssetChanges = true;
            int processedBatches = 0;
            try
            {
                while (0 < PendingAssetChanges.Count)
                {
                    PendingAssetChangeSet changeSet = PendingAssetChanges.Dequeue();
                    bool handled = HandleAssetChanges(
                        changeSet.Imported,
                        changeSet.Deleted,
                        changeSet.Moved,
                        changeSet.MovedFrom
                    );

                    if (handled)
                    {
                        processedBatches++;
                        if (MaxPendingChangeSetsPerCycle <= processedBatches)
                        {
                            EnterLoopProtection();
                            break;
                        }
                    }
                }
            }
            finally
            {
                _processingAssetChanges = false;
                if (!_loopProtectionActive && 0 < processedBatches)
                {
                    UpdateLoopWindow(processedBatches);
                }
            }
        }

        private static bool HandleAssetChanges(
            IReadOnlyList<string> importedAssets,
            IReadOnlyList<string> deletedAssets,
            IReadOnlyList<string> movedAssets,
            IReadOnlyList<string> movedFromAssetPaths
        )
        {
            if (_loopProtectionActive)
            {
                return false;
            }

            bool handledChange = false;
            foreach (AssetWatcher watcher in WatchersByAssetType.Values)
            {
                List<string> createdPaths = CollectCreatedAssets(
                    watcher.AssetType,
                    importedAssets,
                    movedAssets
                );
                List<string> deletedPaths = CollectDeletedAssets(
                    watcher,
                    deletedAssets,
                    movedFromAssetPaths
                );

                AssetChangeFlags triggeredFlags = AssetChangeFlags.None;
                if (0 < createdPaths.Count)
                {
                    triggeredFlags |= AssetChangeFlags.Created;
                }

                if (0 < deletedPaths.Count)
                {
                    triggeredFlags |= AssetChangeFlags.Deleted;
                }

                if (triggeredFlags == AssetChangeFlags.None)
                {
                    continue;
                }

                handledChange = true;
                List<UnityEngine.Object> createdAssetInstances = null;
                Dictionary<Type, Array> createdAssetArrays = null;
                string[] deletedPathsArray = null;

                foreach (MethodSubscription subscription in watcher.Subscriptions)
                {
                    AssetChangeFlags relevant = subscription._flags & triggeredFlags;
                    if (relevant == AssetChangeFlags.None)
                    {
                        continue;
                    }

                    object[] args = BuildInvocationArguments(
                        subscription,
                        watcher.AssetType,
                        relevant,
                        createdPaths,
                        deletedPaths,
                        ref createdAssetInstances,
                        ref createdAssetArrays,
                        ref deletedPathsArray
                    );

                    InvokeSubscription(subscription, args);
                }

                if (0 < createdPaths.Count)
                {
                    foreach (string assetPath in createdPaths)
                    {
                        watcher.KnownAssetPaths.Add(assetPath);
                    }
                }

                if (0 < deletedPaths.Count)
                {
                    foreach (string deletedPath in deletedPaths)
                    {
                        watcher.KnownAssetPaths.Remove(deletedPath);
                    }
                }
            }

            return handledChange;
        }

        private static object[] BuildInvocationArguments(
            MethodSubscription subscription,
            Type assetType,
            AssetChangeFlags relevantFlags,
            IReadOnlyList<string> createdPaths,
            IReadOnlyList<string> deletedPaths,
            ref List<UnityEngine.Object> createdAssetInstances,
            ref Dictionary<Type, Array> createdAssetArrays,
            ref string[] deletedPathsArray
        )
        {
            switch (subscription._parameterMode)
            {
                case SubscriptionParameterMode.None:
                    return Array.Empty<object>();
                case SubscriptionParameterMode.Context:
                    return new object[]
                    {
                        new AssetChangeContext(
                            assetType,
                            relevantFlags,
                            relevantFlags.HasFlagNoAlloc(AssetChangeFlags.Created)
                                ? createdPaths
                                : Array.Empty<string>(),
                            relevantFlags.HasFlagNoAlloc(AssetChangeFlags.Deleted)
                                ? deletedPaths
                                : Array.Empty<string>()
                        ),
                    };
                case SubscriptionParameterMode.CreatedAndDeleted:
                    Array createdArgument = relevantFlags.HasFlagNoAlloc(AssetChangeFlags.Created)
                        ? GetCreatedAssetsArgument(
                            subscription,
                            assetType,
                            createdPaths,
                            ref createdAssetInstances,
                            ref createdAssetArrays
                        )
                        : Array.CreateInstance(subscription._createdParameterElementType, 0);
                    string[] deletedArgument = relevantFlags.HasFlagNoAlloc(
                        AssetChangeFlags.Deleted
                    )
                        ? GetDeletedPathsArgument(deletedPaths, ref deletedPathsArray)
                        : Array.Empty<string>();
                    return new object[] { createdArgument, deletedArgument };
                default:
                    return Array.Empty<object>();
            }
        }

        private static Array GetCreatedAssetsArgument(
            MethodSubscription subscription,
            Type assetType,
            IReadOnlyList<string> createdPaths,
            ref List<UnityEngine.Object> createdAssetInstances,
            ref Dictionary<Type, Array> createdAssetArrays
        )
        {
            if (createdPaths == null || createdPaths.Count == 0)
            {
                return Array.CreateInstance(subscription._createdParameterElementType, 0);
            }

            createdAssetInstances ??= LoadCreatedAssetInstances(assetType, createdPaths);
            createdAssetArrays ??= new Dictionary<Type, Array>();

            if (
                !createdAssetArrays.TryGetValue(
                    subscription._createdParameterElementType,
                    out Array typedArray
                )
            )
            {
                typedArray = Array.CreateInstance(
                    subscription._createdParameterElementType,
                    createdAssetInstances.Count
                );
                for (int i = 0; i < createdAssetInstances.Count; i++)
                {
                    typedArray.SetValue(createdAssetInstances[i], i);
                }

                createdAssetArrays.Add(subscription._createdParameterElementType, typedArray);
            }

            return typedArray;
        }

        private static List<UnityEngine.Object> LoadCreatedAssetInstances(
            Type assetType,
            IReadOnlyList<string> createdPaths
        )
        {
            List<UnityEngine.Object> instances = new(createdPaths.Count);
            Type loadType = typeof(UnityEngine.Object).IsAssignableFrom(assetType)
                ? assetType
                : typeof(UnityEngine.Object);
            for (int i = 0; i < createdPaths.Count; i++)
            {
                string path = createdPaths[i];

                UnityEngine.Object mainAsset = AssetDatabase.LoadAssetAtPath(path, loadType);
                if (mainAsset != null)
                {
                    instances.Add(mainAsset);
                    continue;
                }

                // Scene files crash LoadAllAssetsAtPath (ReadObjectThreaded not allowed)
                if (!IsScenePath(path))
                {
                    UnityEngine.Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                    if (allAssets != null)
                    {
                        foreach (UnityEngine.Object subAsset in allAssets)
                        {
                            if (subAsset != null && assetType.IsInstanceOfType(subAsset))
                            {
                                instances.Add(subAsset);
                            }
                        }
                    }
                }
            }

            return instances;
        }

        private static string[] GetDeletedPathsArgument(
            IReadOnlyList<string> deletedPaths,
            ref string[] deletedPathsArray
        )
        {
            if (deletedPaths == null || deletedPaths.Count == 0)
            {
                return Array.Empty<string>();
            }

            if (deletedPathsArray == null)
            {
                deletedPathsArray = new string[deletedPaths.Count];
                for (int i = 0; i < deletedPaths.Count; i++)
                {
                    deletedPathsArray[i] = deletedPaths[i];
                }
            }

            return deletedPathsArray;
        }

        private static void InvokeSubscription(MethodSubscription subscription, object[] args)
        {
            if (subscription._method.IsStatic)
            {
                InvokeSubscriptionMethod(subscription, null, args);
                return;
            }

            foreach (
                UnityEngine.Object instance in EnumeratePersistedInstances(
                    subscription._declaringType,
                    subscription._searchPrefabs,
                    subscription._searchSceneObjects
                )
            )
            {
                if (instance == null)
                {
                    continue;
                }

                InvokeSubscriptionMethod(subscription, instance, args);
            }
        }

        private static void InvokeSubscriptionMethod(
            MethodSubscription subscription,
            UnityEngine.Object target,
            object[] args
        )
        {
            try
            {
                subscription._method.Invoke(target, args);
            }
            catch (Exception ex)
            {
                Debug.LogException(
                    new InvalidOperationException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Failed invoking DetectAssetChanged watcher {0}.{1}",
                            subscription._declaringType.FullName,
                            subscription._method.Name
                        ),
                        ex
                    ),
                    target
                );
            }
        }

        private static IEnumerable<UnityEngine.Object> EnumeratePersistedInstances(
            Type declaringType,
            bool searchPrefabs = false,
            bool searchSceneObjects = false
        )
        {
            HashSet<string> yieldedPaths = new(StringComparer.OrdinalIgnoreCase);
            HashSet<long> yieldedInstanceIds = new();

            // Component handlers require explicit prefab or scene search flags; primary asset searches would bypass them.
            bool isComponentType = typeof(Component).IsAssignableFrom(declaringType);

            if (_diagnosticsEnabled)
            {
                Debug.Log(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "[DetectAssetChanged] EnumeratePersistedInstances: type={0}, isComponent={1}, searchPrefabs={2}, searchSceneObjects={3}",
                        declaringType.FullName,
                        isComponentType,
                        searchPrefabs,
                        searchSceneObjects
                    )
                );
            }

            if (!isComponentType)
            {
                string filter = $"t:{declaringType.Name}";
                string[] guids = AssetDatabase.FindAssets(filter);
                foreach (string guidsElement in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guidsElement);
                    if (ShouldSkipPath(path))
                    {
                        continue;
                    }

                    UnityEngine.Object instance = AssetDatabase.LoadAssetAtPath(
                        path,
                        declaringType
                    );
                    if (instance != null)
                    {
                        yieldedPaths.Add(path);
                        yield return instance;
                    }
                }

                // Test types without matching filenames can evade Unity type filters; scan only fixture folders as fallback.
                if (_includeTestAssets)
                {
                    string testFolder = "Assets/" + TestAssetFolderMarker;
                    if (!AssetDatabase.IsValidFolder(testFolder))
                    {
                        yield break;
                    }

                    // Check the filesystem too because Unity can log before throwing for a deleted folder.
                    string fullTestFolderPath = Path.Combine(
                        Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
                        testFolder
                    );
                    if (!Directory.Exists(fullTestFolderPath))
                    {
                        yield break;
                    }

                    // The folder may disappear between validation and search.
                    string[] testGuids;
                    try
                    {
                        testGuids = AssetDatabase.FindAssets(
                            "t:ScriptableObject",
                            new[] { testFolder }
                        );
                    }
                    catch (Exception ex)
                        when (ex is not OutOfMemoryException and not StackOverflowException)
                    {
                        // Fixture cleanup can delete the folder between validation and search.
                        testGuids = Array.Empty<string>();
                    }

                    foreach (string testGuidsElement in testGuids)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(testGuidsElement);
                        if (yieldedPaths.Contains(path))
                        {
                            continue;
                        }

                        UnityEngine.Object asset =
                            AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                        if (asset != null && declaringType.IsInstanceOfType(asset))
                        {
                            yieldedPaths.Add(path);
                            yield return asset;
                        }
                    }
                }
            }

            if (searchPrefabs && isComponentType)
            {
                int prefabCount = 0;
                foreach (
                    UnityEngine.Object component in EnumeratePrefabComponents(
                        declaringType,
                        yieldedPaths,
                        yieldedInstanceIds
                    )
                )
                {
                    prefabCount++;
                    yield return component;
                }

                if (_diagnosticsEnabled)
                {
                    Debug.Log(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "[DetectAssetChanged] Prefab search for {0} found {1} instances",
                            declaringType.Name,
                            prefabCount
                        )
                    );
                }
            }

            if (searchSceneObjects && isComponentType)
            {
                int sceneCount = 0;
                foreach (
                    UnityEngine.Object component in EnumerateSceneComponents(
                        declaringType,
                        yieldedInstanceIds
                    )
                )
                {
                    sceneCount++;
                    yield return component;
                }

                if (_diagnosticsEnabled)
                {
                    Debug.Log(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "[DetectAssetChanged] Scene search for {0} found {1} instances",
                            declaringType.Name,
                            sceneCount
                        )
                    );
                }
            }
        }

        private static IEnumerable<UnityEngine.Object> EnumeratePrefabComponents(
            Type declaringType,
            HashSet<string> yieldedPaths,
            HashSet<long> yieldedInstanceIds
        )
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            foreach (string prefabGuidsElement in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(prefabGuidsElement);
                if (ShouldSkipPath(path))
                {
                    continue;
                }

                if (yieldedPaths.Contains(path))
                {
                    continue;
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                Component[] components = prefab.GetComponentsInChildren(declaringType, true);
                foreach (Component component in components)
                {
                    if (component == null)
                    {
                        continue;
                    }

                    long instanceId = component.GetUnityObjectId();
                    if (yieldedInstanceIds.Add(instanceId))
                    {
                        yield return component;
                    }
                }
            }
        }

        private static IEnumerable<UnityEngine.Object> EnumerateSceneComponents(
            Type declaringType,
            HashSet<long> yieldedInstanceIds
        )
        {
            int sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (int sceneIndex = 0; sceneIndex < sceneCount; sceneIndex++)
            {
                UnityEngine.SceneManagement.Scene scene =
                    UnityEngine.SceneManagement.SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded)
                {
                    continue;
                }

                GameObject[] rootObjects = scene.GetRootGameObjects();
                foreach (GameObject root in rootObjects)
                {
                    if (root == null)
                    {
                        continue;
                    }

                    Component[] components = root.GetComponentsInChildren(declaringType, true);
                    foreach (Component component in components)
                    {
                        if (component == null)
                        {
                            continue;
                        }

                        long instanceId = component.GetUnityObjectId();
                        if (yieldedInstanceIds.Add(instanceId))
                        {
                            yield return component;
                        }
                    }
                }
            }
        }

        private static List<string> CollectCreatedAssets(
            Type assetType,
            IReadOnlyList<string> importedAssets,
            IReadOnlyList<string> movedAssets
        )
        {
            List<string> buffer = new();
            AppendCreatedAssets(assetType, importedAssets, buffer);
            AppendCreatedAssets(assetType, movedAssets, buffer);
            return buffer;
        }

        private static void AppendCreatedAssets(
            Type assetType,
            IReadOnlyList<string> candidatePaths,
            List<string> buffer
        )
        {
            if (candidatePaths == null)
            {
                return;
            }

            for (int i = 0; i < candidatePaths.Count; i++)
            {
                string path = candidatePaths[i];
                if (ShouldSkipPath(path))
                {
                    continue;
                }

                Type mainType = AssetDatabase.GetMainAssetTypeAtPath(path);
                if (mainType != null && assetType.IsAssignableFrom(mainType))
                {
                    buffer.Add(path);
                    continue;
                }

                // A Sprite is a sub-asset of its Texture2D, so the main asset type never matches.
                if (mainType != null && HasMatchingSubAsset(path, assetType))
                {
                    buffer.Add(path);
                    continue;
                }

                // Only fixture-scoped test assets may be loaded to resolve missing metadata; production queries must use metadata.
                if (
                    _includeTestAssets
                    && 0 <= path.IndexOf(TestAssetFolderMarker, StringComparison.OrdinalIgnoreCase)
                )
                {
                    UnityEngine.Object loadedAsset =
                        AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    if (loadedAsset != null && assetType.IsInstanceOfType(loadedAsset))
                    {
                        buffer.Add(path);
                    }
                }
            }
        }

        private static bool HasMatchingSubAsset(string path, Type assetType)
        {
            // Scene files crash LoadAllAssetsAtPath (ReadObjectThreaded not allowed)
            if (IsScenePath(path))
            {
                return false;
            }

            // Do not load prefabs for type queries: deserialization invokes consumer OnValidate. Nested prefab sub-assets are consequently excluded.
            if (IsPrefabPath(path))
            {
                return false;
            }

            UnityEngine.Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (allAssets == null || allAssets.Length <= 1)
            {
                return false;
            }

            foreach (UnityEngine.Object asset in allAssets)
            {
                if (asset != null && assetType.IsInstanceOfType(asset))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> CollectDeletedAssets(
            AssetWatcher watcher,
            IReadOnlyList<string> deletedAssets,
            IReadOnlyList<string> movedFromAssetPaths
        )
        {
            List<string> buffer = new();
            AppendDeletedAssets(watcher, deletedAssets, buffer);
            AppendDeletedAssets(watcher, movedFromAssetPaths, buffer);
            return buffer;
        }

        private static void AppendDeletedAssets(
            AssetWatcher watcher,
            IReadOnlyList<string> candidatePaths,
            List<string> buffer
        )
        {
            if (candidatePaths == null)
            {
                return;
            }

            for (int i = 0; i < candidatePaths.Count; i++)
            {
                string path = candidatePaths[i];
                if (ShouldSkipPath(path))
                {
                    continue;
                }

                if (!watcher.KnownAssetPaths.Contains(path))
                {
                    continue;
                }

                buffer.Add(path);
            }
        }

        private static void EnsureInitialized()
        {
            EnsureInitialized(force: false);
        }

        // Guard every entry to watcher construction; only explicit test requests bypass batch-mode suppression.
        private static void EnsureInitialized(bool force)
        {
            if (_initialized)
            {
                return;
            }

            if (!force && !IsEnabled)
            {
                return;
            }

            _initialized = true;
            BuildWatchers();
        }

        private static void BuildWatchers()
        {
            WatchersByAssetType.Clear();

            Type[] loadedTypes =
                ReflectionHelpers.GetAllLoadedTypes()?.Where(t => t != null).ToArray()
                ?? Array.Empty<Type>();
            foreach (Type type in loadedTypes)
            {
                // Static classes are abstract sealed and can still declare handlers.
                if (type == null || (type.IsAbstract && !type.IsSealed))
                {
                    continue;
                }

                BindingFlags flags =
                    BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly;
                MethodInfo[] methods = type.GetMethods(flags);
                foreach (MethodInfo method in methods)
                {
                    DetectAssetChangedAttribute[] attributes = method
                        .GetCustomAttributes(typeof(DetectAssetChangedAttribute), true)
                        .OfType<DetectAssetChangedAttribute>()
                        .ToArray();
                    if (attributes.Length == 0)
                    {
                        continue;
                    }

                    if (
                        !TryResolveParameterMode(
                            type,
                            method,
                            out SubscriptionParameterMode parameterMode,
                            out Type createdElementType
                        )
                    )
                    {
                        continue;
                    }

                    foreach (DetectAssetChangedAttribute attribute in attributes)
                    {
                        if (
                            parameterMode == SubscriptionParameterMode.CreatedAndDeleted
                            && !ResolutionSupportsAssetType(createdElementType, attribute.AssetType)
                        )
                        {
                            Debug.LogWarning(
                                $"[DetectAssetChanged] {type.FullName}.{method.Name} expects created asset parameter type {createdElementType.FullName}, which is not compatible with watched asset type {attribute.AssetType.FullName}."
                            );
                            continue;
                        }

                        bool includeAssignableTypes = attribute.IncludeAssignableTypes;
                        if (
                            !WatchersByAssetType.TryGetValue(
                                attribute.AssetType,
                                out AssetWatcher watcher
                            )
                        )
                        {
                            watcher = new AssetWatcher(attribute.AssetType, includeAssignableTypes);
                            PopulateKnownAssetPaths(watcher, loadedTypes);
                            WatchersByAssetType.Add(attribute.AssetType, watcher);
                        }
                        else if (includeAssignableTypes && !watcher.IncludeAssignableTypes)
                        {
                            watcher.EnableAssignableMatching();
                            PopulateKnownAssetPaths(watcher, loadedTypes);
                        }

                        MethodSubscription subscription = new()
                        {
                            _declaringType = type,
                            _method = method,
                            _flags = attribute.Flags,
                            _parameterMode = parameterMode,
                            _createdParameterElementType = createdElementType,
                            _searchPrefabs = attribute.SearchPrefabs,
                            _searchSceneObjects = attribute.SearchSceneObjects,
                        };

                        if (attribute.SearchPrefabs && !watcher.SearchPrefabs)
                        {
                            watcher.EnablePrefabSearch();
                        }

                        if (attribute.SearchSceneObjects && !watcher.SearchSceneObjects)
                        {
                            watcher.EnableSceneObjectSearch();
                        }

                        bool alreadyExists = watcher.Subscriptions.Any(existing =>
                            existing._declaringType == type && existing._method == method
                        );
                        if (!alreadyExists)
                        {
                            watcher.Subscriptions.Add(subscription);
                        }
                    }
                }
            }
        }

        private static void PopulateKnownAssetPaths(
            AssetWatcher watcher,
            IReadOnlyList<Type> loadedTypes
        )
        {
            if (watcher == null)
            {
                return;
            }

            foreach (
                Type searchType in ResolveSearchableAssetTypes(
                    watcher.AssetType,
                    watcher.IncludeAssignableTypes,
                    loadedTypes
                )
            )
            {
                string filter = $"t:{searchType.Name}";
                string[] guids = AssetDatabase.FindAssets(filter);
                foreach (string guidsElement in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guidsElement);
                    if (ShouldSkipPath(path))
                    {
                        continue;
                    }

                    Type mainType = AssetDatabase.GetMainAssetTypeAtPath(path);
                    if (mainType != null && watcher.AssetType.IsAssignableFrom(mainType))
                    {
                        watcher.KnownAssetPaths.Add(path);
                    }
                }
            }

            // Test types without matching filenames can evade Unity filters; this fallback stays in fixture folders.
            if (_includeTestAssets)
            {
                string testFolder = "Assets/" + TestAssetFolderMarker;
                if (!AssetDatabase.IsValidFolder(testFolder))
                {
                    return;
                }

                // Check the filesystem too because Unity can log before throwing for a deleted folder.
                string fullTestFolderPath = Path.Combine(
                    Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
                    testFolder
                );
                if (!Directory.Exists(fullTestFolderPath))
                {
                    return;
                }

                // The folder may disappear between validation and search.
                string[] testGuids;
                try
                {
                    testGuids = AssetDatabase.FindAssets(
                        "t:ScriptableObject",
                        new[] { testFolder }
                    );
                }
                catch (Exception ex)
                    when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    // Fixture cleanup can delete the folder between validation and search.
                    return;
                }

                foreach (string testGuidsElement in testGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(testGuidsElement);
                    if (watcher.KnownAssetPaths.Contains(path))
                    {
                        continue;
                    }

                    // Metadata answers the main-asset type without deserializing consumer objects during import.
                    Type testAssetType = AssetDatabase.GetMainAssetTypeAtPath(path);
                    if (testAssetType != null && watcher.AssetType.IsAssignableFrom(testAssetType))
                    {
                        watcher.KnownAssetPaths.Add(path);
                    }
                }
            }
        }

        private static IEnumerable<Type> ResolveSearchableAssetTypes(
            Type requestedAssetType,
            bool includeAssignableTypes,
            IReadOnlyList<Type> loadedTypes
        )
        {
            if (requestedAssetType == null)
            {
                yield break;
            }

            bool isUnityObjectType = typeof(UnityEngine.Object).IsAssignableFrom(
                requestedAssetType
            );
            HashSet<Type> yieldedTypes;
            if (isUnityObjectType)
            {
                yield return requestedAssetType;
                if (!includeAssignableTypes)
                {
                    yield break;
                }

                yieldedTypes = new HashSet<Type> { requestedAssetType };
            }
            else
            {
                if (!includeAssignableTypes)
                {
                    yield break;
                }

                yieldedTypes = new HashSet<Type>();
            }

            if (loadedTypes == null)
            {
                yield break;
            }

            for (int i = 0; i < loadedTypes.Count; i++)
            {
                Type candidate = loadedTypes[i];
                if (candidate == null)
                {
                    continue;
                }

                if (!typeof(UnityEngine.Object).IsAssignableFrom(candidate))
                {
                    continue;
                }

                if (candidate.IsAbstract || candidate == requestedAssetType)
                {
                    continue;
                }

                if (!requestedAssetType.IsAssignableFrom(candidate))
                {
                    continue;
                }

                if (yieldedTypes.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        private static bool TryResolveParameterMode(
            Type declaringType,
            MethodInfo method,
            out SubscriptionParameterMode mode,
            out Type createdElementType
        )
        {
            if (method.ReturnType != typeof(void))
            {
                LogUnsupportedSignature(
                    declaringType,
                    method,
                    "must return void to receive DetectAssetChanged notifications."
                );
                mode = SubscriptionParameterMode.None;
                createdElementType = null;
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                mode = SubscriptionParameterMode.None;
                createdElementType = null;
                return true;
            }

            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(AssetChangeContext))
            {
                mode = SubscriptionParameterMode.Context;
                createdElementType = null;
                return true;
            }

            if (
                parameters.Length == 2
                && TryResolveCreatedParameterType(parameters[0].ParameterType, out Type elementType)
                && parameters[1].ParameterType == typeof(string[])
            )
            {
                mode = SubscriptionParameterMode.CreatedAndDeleted;
                createdElementType = elementType;
                return true;
            }

            LogUnsupportedSignature(
                declaringType,
                method,
                "has an unsupported parameter signature for DetectAssetChanged."
            );
            mode = SubscriptionParameterMode.None;
            createdElementType = null;
            return false;
        }

        private static bool TryResolveCreatedParameterType(Type parameterType, out Type elementType)
        {
            if (parameterType == null || !parameterType.IsArray)
            {
                elementType = null;
                return false;
            }

            Type resolvedElementType = parameterType.GetElementType();
            if (resolvedElementType == null)
            {
                elementType = null;
                return false;
            }

            bool isUnityObjectType = typeof(UnityEngine.Object).IsAssignableFrom(
                resolvedElementType
            );
            bool isInterfaceType = resolvedElementType.IsInterface;
            if (!isUnityObjectType && !isInterfaceType)
            {
                elementType = null;
                return false;
            }

            elementType = resolvedElementType;
            return true;
        }

        private static bool ResolutionSupportsAssetType(Type parameterElementType, Type assetType)
        {
            if (parameterElementType == null || assetType == null)
            {
                return true;
            }

            return parameterElementType.IsAssignableFrom(assetType);
        }

        private static void UpdateLoopWindow(int processedBatches)
        {
            double now = TimeProvider();
            double loopWindow = ResolveLoopWindowSeconds();
            if (loopWindow <= 0d)
            {
                loopWindow = UnityHelpersSettings.DefaultDetectAssetChangeLoopWindowSeconds;
            }

            if (loopWindow < now - _lastChangeProcessTimestamp)
            {
                _consecutiveChangeBatches = 0;
            }

            _lastChangeProcessTimestamp = now;
            _consecutiveChangeBatches += processedBatches;
            if (MaxConsecutiveChangeSetsWithinWindow <= _consecutiveChangeBatches)
            {
                EnterLoopProtection();
            }
        }

        private static double ResolveLoopWindowSeconds()
        {
            if (_loopWindowSecondsOverride is > 0d)
            {
                return _loopWindowSecondsOverride.Value;
            }

            double configured;
            try
            {
                configured = UnityHelpersSettings.GetDetectAssetChangeLoopWindowSeconds();
            }
            catch (Exception)
            {
                configured = UnityHelpersSettings.DefaultDetectAssetChangeLoopWindowSeconds;
            }

            return configured < UnityHelpersSettings.MinDetectAssetChangeLoopWindowSeconds
                ? UnityHelpersSettings.MinDetectAssetChangeLoopWindowSeconds
                : configured;
        }

        private static void EnterLoopProtection()
        {
            if (_loopProtectionActive)
            {
                return;
            }

            _loopProtectionActive = true;
            _consecutiveChangeBatches = 0;
            PendingAssetChanges.Clear();
            Debug.LogError(InfiniteLoopWarning);
        }

        private static void LogUnsupportedSignature(
            Type declaringType,
            MethodInfo method,
            string detail
        )
        {
            Debug.LogError(
                $"[DetectAssetChanged] {declaringType.FullName}.{method.Name} {detail} {SupportedSignatureDescription}"
            );
        }

        private static bool ShouldSkipPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return true;
            }

            // Fixture-scoped folders prevent one test's asset changes from invoking another test's handlers.
            if (_includeTestAssets && _testAssetFolderAllowlist != null)
            {
                bool allowed = false;
                foreach (string prefix in _testAssetFolderAllowlist)
                {
                    if (
                        !string.IsNullOrEmpty(prefix)
                        && assetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        allowed = true;
                        break;
                    }
                }
                if (!allowed)
                {
                    return true;
                }
            }

            if (
                !_includeTestAssets
                && 0 <= assetPath.IndexOf(TestAssetFolderMarker, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }

            return false;
        }

        private static bool IsScenePath(string assetPath)
        {
            return assetPath != null
                && (
                    assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                    || assetPath.EndsWith(".scenetemplate", StringComparison.OrdinalIgnoreCase)
                );
        }

        private static bool IsPrefabPath(string assetPath)
        {
            return assetPath != null
                && assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        }

        internal enum SubscriptionParameterMode
        {
            None = 0,
            Context = 1,
            CreatedAndDeleted = 2,
        }

        internal sealed class MethodSubscription
        {
            internal Type _declaringType;
            internal MethodInfo _method;
            internal AssetChangeFlags _flags;
            internal SubscriptionParameterMode _parameterMode;
            internal Type _createdParameterElementType;
            internal bool _searchPrefabs;
            internal bool _searchSceneObjects;
        }

        internal sealed class AssetWatcherSettings
        {
            internal bool IncludeAssignableTypes { get; set; }
            internal bool SearchPrefabs { get; set; }
            internal bool SearchSceneObjects { get; set; }
            internal HashSet<string> KnownAssetPaths { get; set; }
            internal List<MethodSubscription> Subscriptions { get; set; }
            internal Dictionary<Type, AssetWatcher> WatchersByAssetType { get; set; } = new();
            internal Queue<PendingAssetChangeSet> PendingAssetChanges { get; set; } = new();
            internal bool Initialized { get; set; }
            internal bool IncludeTestAssets { get; set; }
            internal IReadOnlyList<string> TestAssetFolderAllowlist { get; set; }
            internal bool ProcessingAssetChanges { get; set; }
            internal bool LoopProtectionActive { get; set; }
            internal int ConsecutiveChangeBatches { get; set; }
            internal double LastChangeProcessTimestamp { get; set; }
            internal Func<double> TimeProvider { get; set; } = DefaultTimeProvider;
            internal double? LoopWindowSecondsOverride { get; set; }
            internal bool DiagnosticsEnabled { get; set; }
            internal bool? EnabledOverride { get; set; }
        }

        internal sealed class AssetWatcher
        {
            internal AssetWatcher(Type assetType, bool includeAssignableTypes)
            {
                AssetType = assetType;
                IncludeAssignableTypes = includeAssignableTypes;
                KnownAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Subscriptions = new List<MethodSubscription>();
            }

            internal Type AssetType { get; }
            internal bool IncludeAssignableTypes { get; private set; }
            internal bool SearchPrefabs { get; private set; }
            internal bool SearchSceneObjects { get; private set; }
            internal HashSet<string> KnownAssetPaths { get; }
            internal List<MethodSubscription> Subscriptions { get; }

            internal void EnableAssignableMatching()
            {
                IncludeAssignableTypes = true;
            }

            internal void EnablePrefabSearch()
            {
                SearchPrefabs = true;
            }

            internal void EnableSceneObjectSearch()
            {
                SearchSceneObjects = true;
            }
        }

        internal sealed class PendingAssetChangeSet
        {
            internal PendingAssetChangeSet(
                IReadOnlyList<string> imported,
                IReadOnlyList<string> deleted,
                IReadOnlyList<string> moved,
                IReadOnlyList<string> movedFrom
            )
            {
                Imported = imported ?? Array.Empty<string>();
                Deleted = deleted ?? Array.Empty<string>();
                Moved = moved ?? Array.Empty<string>();
                MovedFrom = movedFrom ?? Array.Empty<string>();
            }

            internal IReadOnlyList<string> Imported { get; }
            internal IReadOnlyList<string> Deleted { get; }
            internal IReadOnlyList<string> Moved { get; }
            internal IReadOnlyList<string> MovedFrom { get; }
        }
    }
}
