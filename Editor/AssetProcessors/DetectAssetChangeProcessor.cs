// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.AssetProcessors
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Utils;

    internal sealed class DetectAssetChangeProcessor : AssetPostprocessor
    {
        internal const int MaxPendingChangeSetsPerCycle = 32;
        internal const int MaxConsecutiveChangeSetsWithinWindow = 128;

        private const string SupportedSignatureDescription =
            "Supported signatures: () with no parameters; (AssetChangeContext context); or (TAsset[] createdAssets, string[] deletedAssetPaths) where TAsset derives from UnityEngine.Object.";
        private const string InfiniteLoopWarning =
            "[DetectAssetChanged] Detected a potentially infinite asset change loop triggered by DetectAssetChanged handlers. Additional change batches will be skipped to prevent recursion until the editor domain reloads. Please fix the offending callbacks.";

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
        /// <see cref="BuildWatchers"/> inspects loaded types for assignable asset matches and
        /// uses Unity's type cache to discover attributed methods. Running watcher construction
        /// inside Unity's import phase has caused native mono crashes and importer stalls on
        /// some Unity versions. The play-mode guard in
        /// <see cref="OnPostprocessAllAssets"/> covers one door into that scan; a headless
        /// `-batchmode` run is not play mode and went through the other one. The watcher is an
        /// editor-authoring convenience, and a headless run has no author to act on a callback, so
        /// the scan there is unobservable work at best and a crash at worst. A headless asset
        /// pipeline that does want the watcher can opt back in through
        /// <see cref="AssetChangeDetectionUtility.Enabled"/>.
        /// </remarks>
        internal static bool IsEnabled => _enabledOverride ?? !Application.isBatchMode;

        internal static readonly Dictionary<Type, AssetWatcher> WatchersByAssetType = new();
        internal static readonly Queue<PendingAssetChangeSet> PendingAssetChanges = new();
        internal static bool _initialized;
        internal static bool _processingAssetChanges;
        internal static bool _loopProtectionActive;
        internal static int _consecutiveChangeBatches;
        internal static double _lastChangeProcessTimestamp;
        internal static bool _diagnosticsEnabled;
        internal static bool? _enabledOverride;

        private static readonly Action DrainPendingChangesAction = ProcessPendingAssetChangesCore;

        static DetectAssetChangeProcessor()
        {
            EditorApplication.delayCall += EnsureInitialized;
        }

        internal static void ResetLoopProtection()
        {
            _loopProtectionActive = false;
            _consecutiveChangeBatches = 0;
            _lastChangeProcessTimestamp = 0d;
            PendingAssetChanges.Clear();
        }

        internal static void EnqueueAssetChanges(
            IReadOnlyList<string> importedAssets,
            IReadOnlyList<string> deletedAssets,
            IReadOnlyList<string> movedAssets,
            IReadOnlyList<string> movedFromAssetPaths
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

            // Defer beyond import guards; type-only questions must still use metadata.
            AssetPostprocessorDeferral.Schedule(DrainPendingChangesAction);
        }

        internal static void ProcessPendingAssetChangesCore()
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
                while (PendingAssetChanges.TryDequeue(out PendingAssetChangeSet changeSet))
                {
                    bool handled = HandleAssetChanges(
                        changeSet.Imported,
                        changeSet.Deleted,
                        changeSet.Moved,
                        changeSet.MovedFrom
                    );

                    if (handled)
                    {
                        ++processedBatches;
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
                    UpdateLoopWindow(
                        processedBatches,
                        EditorApplication.timeSinceStartup,
                        ResolveLoopWindowSeconds()
                    );
                }
            }
        }

        internal static void EnsureInitialized()
        {
            if (_initialized || !IsEnabled)
            {
                return;
            }
            _initialized = true;
            BuildWatchers();
        }

        internal static void BuildWatchers()
        {
            WatchersByAssetType.Clear();

            List<Type> loadedTypes = new();
            IEnumerable<Type> discoveredTypes = ReflectionHelpers.GetAllLoadedTypes();
            if (discoveredTypes != null)
            {
                foreach (Type discoveredType in discoveredTypes)
                {
                    if (discoveredType != null)
                    {
                        loadedTypes.Add(discoveredType);
                    }
                }
            }
            List<MethodInfo> attributedMethods = new();
            HashSet<MethodInfo> discoveredMethods = new();
            HashSet<Type> inheritedHandlerBases = new();
            foreach (
                MethodInfo method in TypeCache.GetMethodsWithAttribute<DetectAssetChangedAttribute>()
            )
            {
                if (discoveredMethods.Add(method))
                {
                    attributedMethods.Add(method);
                }
                if (method.IsVirtual && method.DeclaringType != null)
                {
                    inheritedHandlerBases.Add(method.DeclaringType);
                }
            }

            if (0 < inheritedHandlerBases.Count)
            {
                BindingFlags inheritedFlags =
                    BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly;
                foreach (Type type in loadedTypes)
                {
                    if (type.IsAbstract && !type.IsSealed)
                    {
                        continue;
                    }
                    bool inheritsHandler = false;
                    for (
                        Type ancestor = type.BaseType;
                        ancestor != null;
                        ancestor = ancestor.BaseType
                    )
                    {
                        if (
                            inheritedHandlerBases.Contains(ancestor)
                            || (
                                ancestor.IsGenericType
                                && inheritedHandlerBases.Contains(
                                    ancestor.GetGenericTypeDefinition()
                                )
                            )
                        )
                        {
                            inheritsHandler = true;
                            break;
                        }
                    }
                    if (!inheritsHandler)
                    {
                        continue;
                    }
                    foreach (MethodInfo method in type.GetMethods(inheritedFlags))
                    {
                        if (
                            method.IsVirtual
                            && discoveredMethods.Add(method)
                            && method.GetAllAttributesSafe<DetectAssetChangedAttribute>().Length
                                != 0
                        )
                        {
                            attributedMethods.Add(method);
                        }
                    }
                }
            }

            foreach (MethodInfo method in attributedMethods)
            {
                Type type = method.DeclaringType;
                // Static classes are abstract sealed and can still declare handlers.
                if (type == null || (type.IsAbstract && !type.IsSealed))
                {
                    continue;
                }

                DetectAssetChangedAttribute[] attributes =
                    method.GetAllAttributesSafe<DetectAssetChangedAttribute>();
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

                    bool alreadyExists = false;
                    foreach (MethodSubscription existing in watcher.Subscriptions)
                    {
                        if (existing._declaringType == type && existing._method == method)
                        {
                            alreadyExists = true;
                            break;
                        }
                    }
                    if (!alreadyExists)
                    {
                        watcher.Subscriptions.Add(subscription);
                    }
                }
            }
        }

        internal static bool TryResolveParameterMode(
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

        internal static void UpdateLoopWindow(int processedBatches, double now, double loopWindow)
        {
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

            EnqueueAssetChanges(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);
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
            string[] importedSearchFolders = null;
            string[] movedSearchFolders = null;
            foreach (AssetWatcher watcher in WatchersByAssetType.Values)
            {
                using PooledResource<List<string>> createdPathsLease = Buffers<string>.List.Get(
                    out List<string> createdPaths
                );
                using PooledResource<List<string>> deletedPathsLease = Buffers<string>.List.Get(
                    out List<string> deletedPaths
                );
                CollectCreatedAssets(
                    watcher,
                    importedAssets,
                    movedAssets,
                    createdPaths,
                    ref importedSearchFolders,
                    ref movedSearchFolders
                );
                CollectDeletedAssets(watcher, deletedAssets, movedFromAssetPaths, deletedPaths);

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
                PooledResource<List<UnityEngine.Object>> createdAssetInstancesLease = default;
                Dictionary<Type, Array> createdAssetArrays = null;
                PooledResource<Dictionary<Type, Array>> createdAssetArraysLease = default;
                string[] createdPathsArray = null;
                string[] deletedPathsContextArray = null;
                string[] deletedPathsArray = null;
                try
                {
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
                            ref createdAssetInstancesLease,
                            ref createdAssetArrays,
                            ref createdAssetArraysLease,
                            ref createdPathsArray,
                            ref deletedPathsContextArray,
                            ref deletedPathsArray
                        );

                        InvokeSubscription(subscription, args);
                    }
                }
                finally
                {
                    createdAssetArraysLease.Dispose();
                    createdAssetInstancesLease.Dispose();
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
            ref PooledResource<List<UnityEngine.Object>> createdAssetInstancesLease,
            ref Dictionary<Type, Array> createdAssetArrays,
            ref PooledResource<Dictionary<Type, Array>> createdAssetArraysLease,
            ref string[] createdPathsArray,
            ref string[] deletedPathsContextArray,
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
                                ? GetPathsArgument(createdPaths, ref createdPathsArray)
                                : Array.Empty<string>(),
                            relevantFlags.HasFlagNoAlloc(AssetChangeFlags.Deleted)
                                ? GetPathsArgument(deletedPaths, ref deletedPathsContextArray)
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
                            ref createdAssetInstancesLease,
                            ref createdAssetArrays,
                            ref createdAssetArraysLease
                        )
                        : Array.CreateInstance(subscription._createdParameterElementType, 0);
                    string[] deletedArgument = relevantFlags.HasFlagNoAlloc(
                        AssetChangeFlags.Deleted
                    )
                        ? GetPathsArgument(deletedPaths, ref deletedPathsArray)
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
            ref PooledResource<List<UnityEngine.Object>> createdAssetInstancesLease,
            ref Dictionary<Type, Array> createdAssetArrays,
            ref PooledResource<Dictionary<Type, Array>> createdAssetArraysLease
        )
        {
            if (createdPaths == null || createdPaths.Count == 0)
            {
                return Array.CreateInstance(subscription._createdParameterElementType, 0);
            }

            if (createdAssetInstances == null)
            {
                createdAssetInstancesLease = Buffers<UnityEngine.Object>.GetList(
                    createdPaths.Count,
                    out createdAssetInstances
                );
                LoadCreatedAssetInstances(assetType, createdPaths, createdAssetInstances);
            }
            if (createdAssetArrays == null)
            {
                createdAssetArraysLease = DictionaryBuffer<Type, Array>.Dictionary.Get(
                    out createdAssetArrays
                );
            }

            if (
                !createdAssetArrays.TryGetValue(
                    subscription._createdParameterElementType,
                    out Array typedArray
                )
            )
            {
                int assetCount = createdAssetInstances.Count;
                typedArray = Array.CreateInstance(
                    subscription._createdParameterElementType,
                    assetCount
                );
                for (int i = 0; i < assetCount; ++i)
                {
                    typedArray.SetValue(createdAssetInstances[i], i);
                }

                createdAssetArrays.Add(subscription._createdParameterElementType, typedArray);
            }

            return typedArray;
        }

        private static void LoadCreatedAssetInstances(
            Type assetType,
            IReadOnlyList<string> createdPaths,
            List<UnityEngine.Object> instances
        )
        {
            int pathCount = createdPaths.Count;
            Type loadType = typeof(UnityEngine.Object).IsAssignableFrom(assetType)
                ? assetType
                : typeof(UnityEngine.Object);
            for (int i = 0; i < pathCount; ++i)
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
        }

        private static string[] GetPathsArgument(
            IReadOnlyList<string> paths,
            ref string[] pathsArray
        )
        {
            if (paths == null || paths.Count == 0)
            {
                return Array.Empty<string>();
            }

            if (pathsArray == null)
            {
                int pathCount = paths.Count;
                pathsArray = new string[pathCount];
                for (int i = 0; i < pathCount; ++i)
                {
                    pathsArray[i] = paths[i];
                }
            }

            return pathsArray;
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
            using PooledResource<HashSet<string>> pathsLease = SetBuffers<string>
                .GetHashSetPool(StringComparer.OrdinalIgnoreCase)
                .Get(out HashSet<string> yieldedPaths);
            using PooledResource<HashSet<long>> instanceIdsLease = Buffers<long>.HashSet.Get(
                out HashSet<long> yieldedInstanceIds
            );

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
            }

            if (searchPrefabs && isComponentType)
            {
                int prefabCount = 0;
                foreach (
                    UnityEngine.Object component in EnumeratePrefabComponents(
                        declaringType,
                        yieldedInstanceIds
                    )
                )
                {
                    ++prefabCount;
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
                    ++sceneCount;
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
            for (int sceneIndex = 0; sceneIndex < sceneCount; ++sceneIndex)
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

        private static void CollectCreatedAssets(
            AssetWatcher watcher,
            IReadOnlyList<string> importedAssets,
            IReadOnlyList<string> movedAssets,
            List<string> buffer,
            ref string[] importedSearchFolders,
            ref string[] movedSearchFolders
        )
        {
            AppendCreatedAssets(watcher, importedAssets, buffer, ref importedSearchFolders);
            AppendCreatedAssets(watcher, movedAssets, buffer, ref movedSearchFolders);
        }

        private static void AppendCreatedAssets(
            AssetWatcher watcher,
            IReadOnlyList<string> candidatePaths,
            List<string> buffer,
            ref string[] searchFolders
        )
        {
            if (candidatePaths == null)
            {
                return;
            }

            HashSet<string> matchingSubAssetGuids = null;
            PooledResource<HashSet<string>> matchingGuidLease = default;
            int candidateCount = candidatePaths.Count;
            try
            {
                for (int i = 0; i < candidateCount; ++i)
                {
                    string path = candidatePaths[i];
                    if (ShouldSkipPath(path))
                    {
                        continue;
                    }

                    Type mainType = AssetDatabase.GetMainAssetTypeAtPath(path);
                    if (mainType != null && watcher.AssetType.IsAssignableFrom(mainType))
                    {
                        buffer.Add(path);
                        continue;
                    }

                    if (
                        mainType != null
                        && HasMatchingSubAsset(
                            path,
                            watcher,
                            candidatePaths,
                            candidateCount,
                            ref searchFolders,
                            ref matchingSubAssetGuids,
                            ref matchingGuidLease
                        )
                    )
                    {
                        buffer.Add(path);
                        continue;
                    }

                    // Only fixture-scoped test assets may be loaded to resolve missing metadata; production queries must use metadata.
                }
            }
            finally
            {
                matchingGuidLease.Dispose();
            }
        }

        private static bool HasMatchingSubAsset(
            string path,
            AssetWatcher watcher,
            IReadOnlyList<string> candidatePaths,
            int candidateCount,
            ref string[] searchFolders,
            ref HashSet<string> matchingGuids,
            ref PooledResource<HashSet<string>> matchingGuidLease
        )
        {
            if (IsScenePath(path) || IsPrefabPath(path))
            {
                return false;
            }

            if (string.IsNullOrEmpty(watcher.SubAssetSearchFilter))
            {
                return false;
            }

            searchFolders ??= CollectSearchFolders(candidatePaths, candidateCount);
            if (searchFolders.Length == 0)
            {
                return false;
            }

            if (matchingGuids == null)
            {
                matchingGuidLease = SetBuffers<string>
                    .GetHashSetPool(StringComparer.OrdinalIgnoreCase)
                    .Get(out matchingGuids);
                string[] foundGuids = AssetDatabase.FindAssets(
                    watcher.SubAssetSearchFilter,
                    searchFolders
                );
                foreach (string foundGuid in foundGuids)
                {
                    matchingGuids.Add(foundGuid);
                }
            }

            string guid = AssetDatabase.AssetPathToGUID(path);
            return !string.IsNullOrEmpty(guid) && matchingGuids.Contains(guid);
        }

        private static string[] CollectSearchFolders(
            IReadOnlyList<string> candidatePaths,
            int candidateCount
        )
        {
            using PooledResource<HashSet<string>> folderLease = SetBuffers<string>
                .GetHashSetPool(StringComparer.OrdinalIgnoreCase)
                .Get(out HashSet<string> folders);
            for (int i = 0; i < candidateCount; ++i)
            {
                string candidate = candidatePaths[i];
                if (
                    string.IsNullOrWhiteSpace(candidate)
                    || IsScenePath(candidate)
                    || IsPrefabPath(candidate)
                )
                {
                    continue;
                }

                int separator = candidate.LastIndexOf('/');
                if (0 < separator)
                {
                    folders.Add(candidate.Substring(0, separator));
                }
            }

            if (folders.Count == 0)
            {
                return Array.Empty<string>();
            }

            string[] searchFolders = new string[folders.Count];
            folders.CopyTo(searchFolders);
            return searchFolders;
        }

        private static void CollectDeletedAssets(
            AssetWatcher watcher,
            IReadOnlyList<string> deletedAssets,
            IReadOnlyList<string> movedFromAssetPaths,
            List<string> buffer
        )
        {
            AppendDeletedAssets(watcher, deletedAssets, buffer);
            AppendDeletedAssets(watcher, movedFromAssetPaths, buffer);
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

            int candidateCount = candidatePaths.Count;
            for (int i = 0; i < candidateCount; ++i)
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

        private static void PopulateKnownAssetPaths(
            AssetWatcher watcher,
            IReadOnlyList<Type> loadedTypes
        )
        {
            if (watcher == null)
            {
                return;
            }

            using PooledResource<StringBuilder> filterLease = Buffers.StringBuilder.Get(
                out StringBuilder subAssetSearchFilter
            );
            foreach (
                Type searchType in ResolveSearchableAssetTypes(
                    watcher.AssetType,
                    watcher.IncludeAssignableTypes,
                    loadedTypes
                )
            )
            {
                if (0 < subAssetSearchFilter.Length)
                {
                    subAssetSearchFilter.Append(' ');
                }
                subAssetSearchFilter.Append("t:");
                subAssetSearchFilter.Append(searchType.Name);
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

            watcher.SubAssetSearchFilter = subAssetSearchFilter.ToString();
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

            int loadedTypeCount = loadedTypes.Count;
            for (int i = 0; i < loadedTypeCount; ++i)
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

        private static double ResolveLoopWindowSeconds()
        {
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
            return string.IsNullOrWhiteSpace(assetPath);
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

        internal sealed class AssetWatcher
        {
            internal Type AssetType { get; }
            internal bool IncludeAssignableTypes { get; private set; }
            internal bool SearchPrefabs { get; private set; }
            internal bool SearchSceneObjects { get; private set; }
            internal HashSet<string> KnownAssetPaths { get; }
            internal string SubAssetSearchFilter { get; set; }
            internal List<MethodSubscription> Subscriptions { get; }

            internal AssetWatcher(Type assetType, bool includeAssignableTypes)
            {
                AssetType = assetType;
                IncludeAssignableTypes = includeAssignableTypes;
                KnownAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Subscriptions = new List<MethodSubscription>();
            }

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
            internal IReadOnlyList<string> Imported { get; }
            internal IReadOnlyList<string> Deleted { get; }
            internal IReadOnlyList<string> Moved { get; }
            internal IReadOnlyList<string> MovedFrom { get; }

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
        }
    }
}
