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

        // Declared after other static fields so its delegate target (ProcessPendingAssetChangesCore)
        // is fully visible to the compiler at field initialization order — avoids any surprise
        // from forward references in the field-init sequence.
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
            EnsureInitialized();
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
            // The asset-change watcher is an editor-authoring concern, not a play-mode one.
            // EnsureInitialized() below runs BuildWatchers, an all-types/all-methods reflection
            // scan; if a play-mode test mutates the AssetDatabase, Unity invokes this callback
            // recursively inside the import phase and that scan destabilizes the asset pipeline
            // (a native mono crash on some Unity versions, multi-minute importer stalls on others),
            // aborting the play-mode test run with a zero-count results.xml. Skip while playing.
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
                // Defer out of the asset-import phase: AssetDatabase.LoadAllAssetsAtPath
                // and GetComponentsInChildren (used during callback resolution) trigger
                // Unity's internal sprite/renderer lifecycle relays, which emit
                // "SendMessage cannot be called..." warnings while the import is active.
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
                while (PendingAssetChanges.Count > 0)
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
                        if (processedBatches >= MaxPendingChangeSetsPerCycle)
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
                if (!_loopProtectionActive && processedBatches > 0)
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
                if (createdPaths.Count > 0)
                {
                    triggeredFlags |= AssetChangeFlags.Created;
                }

                if (deletedPaths.Count > 0)
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

                if (createdPaths.Count > 0)
                {
                    foreach (string assetPath in createdPaths)
                    {
                        watcher.KnownAssetPaths.Add(assetPath);
                    }
                }

                if (deletedPaths.Count > 0)
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

                // First try to load the main asset directly
                UnityEngine.Object mainAsset = AssetDatabase.LoadAssetAtPath(path, loadType);
                if (mainAsset != null)
                {
                    instances.Add(mainAsset);
                    continue;
                }

                // If main asset doesn't match, check sub-assets (e.g., Sprites in a Texture2D)
                // Scene files crash LoadAllAssetsAtPath (ReadObjectThreaded not allowed)
                if (!IsScenePath(path))
                {
                    UnityEngine.Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                    if (allAssets != null)
                    {
                        for (int j = 0; j < allAssets.Length; j++)
                        {
                            UnityEngine.Object subAsset = allAssets[j];
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
            HashSet<int> yieldedInstanceIds = new();

            // Determine if this is a Component type - these require explicit prefab/scene search
            // and should NOT be found via the primary asset search (which would incorrectly
            // return prefab assets containing the component regardless of search flags)
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

            // Primary search using Unity's type filter - ONLY for non-Component types
            // (ScriptableObjects and other direct asset types that exist as standalone .asset files)
            // Component types must use the explicit prefab/scene search paths below
            if (!isComponentType)
            {
                string filter = $"t:{declaringType.Name}";
                string[] guids = AssetDatabase.FindAssets(filter);
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
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

                // Fallback for test assets: Unity's t:TypeName filter may fail to find assets
                // when the class is defined in a file that doesn't match the class name.
                // Search test directories directly by scanning for ScriptableObject assets.
                // This only applies to non-Component types (ScriptableObjects).
                if (_includeTestAssets)
                {
                    string testFolder = "Assets/" + TestAssetFolderMarker;
                    if (!AssetDatabase.IsValidFolder(testFolder))
                    {
                        yield break;
                    }

                    // Additional filesystem check to avoid race condition where Unity logs a warning
                    // before the exception can be caught
                    string fullTestFolderPath = Path.Combine(
                        Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
                        testFolder
                    );
                    if (!Directory.Exists(fullTestFolderPath))
                    {
                        yield break;
                    }

                    // Wrap in try-catch to handle race condition where folder may be deleted
                    // between IsValidFolder check and FindAssets call
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
                        // Race condition: folder may have been deleted between validation and search.
                        // Silently ignore - this is expected during test cleanup.
                        testGuids = Array.Empty<string>();
                    }

                    for (int i = 0; i < testGuids.Length; i++)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(testGuids[i]);
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

            // Search prefabs for MonoBehaviour components - ONLY when searchPrefabs is true
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

            // Search open scenes for MonoBehaviour components - ONLY when searchSceneObjects is true
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
            HashSet<int> yieldedInstanceIds
        )
        {
            // Find all prefabs in the project
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
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

                // Get all components of the declaring type (including children)
                Component[] components = prefab.GetComponentsInChildren(declaringType, true);
                for (int j = 0; j < components.Length; j++)
                {
                    Component component = components[j];
                    if (component == null)
                    {
                        continue;
                    }

                    int instanceId = component.GetUnityObjectId();
                    if (yieldedInstanceIds.Add(instanceId))
                    {
                        yield return component;
                    }
                }
            }
        }

        private static IEnumerable<UnityEngine.Object> EnumerateSceneComponents(
            Type declaringType,
            HashSet<int> yieldedInstanceIds
        )
        {
            // Search all loaded scenes
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
                for (int i = 0; i < rootObjects.Length; i++)
                {
                    GameObject root = rootObjects[i];
                    if (root == null)
                    {
                        continue;
                    }

                    // Get all components of the declaring type (including children)
                    Component[] components = root.GetComponentsInChildren(declaringType, true);
                    for (int j = 0; j < components.Length; j++)
                    {
                        Component component = components[j];
                        if (component == null)
                        {
                            continue;
                        }

                        int instanceId = component.GetUnityObjectId();
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

                // Check for sub-assets (e.g., Sprites are sub-assets of Texture2D)
                // This is necessary because types like Sprite are not the main asset type
                if (mainType != null && HasMatchingSubAsset(path, assetType))
                {
                    buffer.Add(path);
                    continue;
                }

                // Fallback for test assets: Unity's GetMainAssetTypeAtPath may return incorrect
                // types when test classes are defined in files that don't match the class name.
                // Actually load the asset and check its runtime type.
                if (
                    _includeTestAssets
                    && path.IndexOf(TestAssetFolderMarker, StringComparison.OrdinalIgnoreCase) >= 0
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

            UnityEngine.Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (allAssets == null || allAssets.Length <= 1)
            {
                return false;
            }

            for (int i = 0; i < allAssets.Length; i++)
            {
                UnityEngine.Object asset = allAssets[i];
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
            if (_initialized)
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
                // Skip null types and abstract types, but allow static classes
                // (static classes are compiled as abstract sealed)
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
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
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

            // Fallback for test assets: Unity's type filter may not find assets when test
            // classes are defined in files that don't match the class name. Scan the test
            // folder directly and check runtime types.
            if (_includeTestAssets)
            {
                string testFolder = "Assets/" + TestAssetFolderMarker;
                if (!AssetDatabase.IsValidFolder(testFolder))
                {
                    return;
                }

                // Additional filesystem check to avoid race condition where Unity logs a warning
                // before the exception can be caught
                string fullTestFolderPath = Path.Combine(
                    Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
                    testFolder
                );
                if (!Directory.Exists(fullTestFolderPath))
                {
                    return;
                }

                // Now safe to call FindAssets since folder definitely exists
                // Wrap in try-catch to handle race condition where folder may be deleted
                // between IsValidFolder check and FindAssets call
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
                    // Race condition: folder may have been deleted between validation and search.
                    // Silently ignore - this is expected during test cleanup.
                    return;
                }

                for (int i = 0; i < testGuids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(testGuids[i]);
                    if (watcher.KnownAssetPaths.Contains(path))
                    {
                        continue;
                    }

                    UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                        path
                    );
                    if (asset != null && watcher.AssetType.IsInstanceOfType(asset))
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
            mode = SubscriptionParameterMode.None;
            createdElementType = null;

            if (method.ReturnType != typeof(void))
            {
                LogUnsupportedSignature(
                    declaringType,
                    method,
                    "must return void to receive DetectAssetChanged notifications."
                );
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                mode = SubscriptionParameterMode.None;
                return true;
            }

            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(AssetChangeContext))
            {
                mode = SubscriptionParameterMode.Context;
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
            return false;
        }

        private static bool TryResolveCreatedParameterType(Type parameterType, out Type elementType)
        {
            elementType = null;
            if (parameterType == null || !parameterType.IsArray)
            {
                return false;
            }

            elementType = parameterType.GetElementType();
            if (elementType == null)
            {
                elementType = null;
                return false;
            }

            bool isUnityObjectType = typeof(UnityEngine.Object).IsAssignableFrom(elementType);
            bool isInterfaceType = elementType.IsInterface;
            if (!isUnityObjectType && !isInterfaceType)
            {
                elementType = null;
                return false;
            }

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

            if (now - _lastChangeProcessTimestamp > loopWindow)
            {
                _consecutiveChangeBatches = 0;
            }

            _lastChangeProcessTimestamp = now;
            _consecutiveChangeBatches += processedBatches;
            if (_consecutiveChangeBatches >= MaxConsecutiveChangeSetsWithinWindow)
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

            // Test-only fixture-scoped allowlist: when tests declare a specific test-asset
            // folder via `TestAssetFolderAllowlist`, paths outside the allowlisted
            // prefixes are skipped even with `IncludeTestAssets = true`. This keeps one
            // fixture's asset mutations from triggering handler invocations for another
            // fixture's committed prefabs (the prior cause of cross-fixture pollution).
            if (_includeTestAssets && _testAssetFolderAllowlist != null)
            {
                bool allowed = false;
                for (int i = 0; i < _testAssetFolderAllowlist.Count; i++)
                {
                    string prefix = _testAssetFolderAllowlist[i];
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
                && assetPath.IndexOf(TestAssetFolderMarker, StringComparison.OrdinalIgnoreCase) >= 0
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
    }
}
