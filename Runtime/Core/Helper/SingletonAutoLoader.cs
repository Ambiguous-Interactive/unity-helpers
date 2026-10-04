// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEngine;
    using Utils;
    using WallstopStudios.UnityHelpers.Tags;

    /// <summary>
    /// Reflection-driven auto-loader that instantiates opt-in singletons during specific Unity load phases.
    /// </summary>
    internal static class SingletonAutoLoader
    {
        internal static readonly Dictionary<string, Action> _cachedLoaders = new(
            StringComparer.Ordinal
        );
        internal static readonly HashSet<RuntimeInitializeLoadType> _executedLoadTypes = new();
        internal static readonly object _executionLock = new();
        internal static readonly Dictionary<Type, PropertyInfo> _runtimeInstanceProperties = new();
        internal static readonly Dictionary<Type, PropertyInfo> _scriptableInstanceProperties =
            new();
        internal static readonly object _loaderBuildLock = new();

        internal static void ExecuteEntries(
            IReadOnlyList<AttributeMetadataCache.AutoLoadSingletonEntry> entries,
            RuntimeInitializeLoadType loadType,
            bool enforceSingleExecution,
            bool requirePlayMode
        )
        {
            if (entries == null || entries.Count == 0)
            {
                return;
            }

            bool isPlayMode = Application.isPlaying;

            if (requirePlayMode && !isPlayMode)
            {
                return;
            }

            if (enforceSingleExecution)
            {
                lock (_executionLock)
                {
                    if (!_executedLoadTypes.Add(loadType))
                    {
                        return;
                    }
                }
            }

            for (int i = 0; i < entries.Count; ++i)
            {
                AttributeMetadataCache.AutoLoadSingletonEntry entry = entries[i];
                if (entry == null || entry.loadType != loadType)
                {
                    continue;
                }

                try
                {
                    Action loader = GetOrCreateLoader(entry);
                    loader?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogError(
                        $"SingletonAutoLoader: Failed to auto-load '{entry.typeName}'. {e}"
                    );
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void AutoLoadSubsystemRegistration()
        {
            // SubsystemRegistration re-arms loaders each session when domain reload is disabled.
            lock (_executionLock)
            {
                _executedLoadTypes.Clear();
            }

            ExecuteForLoadType(RuntimeInitializeLoadType.SubsystemRegistration);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void AutoLoadAfterAssemblies()
        {
            ExecuteForLoadType(RuntimeInitializeLoadType.AfterAssembliesLoaded);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void AutoLoadBeforeSplashScreen()
        {
            ExecuteForLoadType(RuntimeInitializeLoadType.BeforeSplashScreen);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoLoadBeforeSceneLoad()
        {
            ExecuteForLoadType(RuntimeInitializeLoadType.BeforeSceneLoad);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoLoadAfterSceneLoad()
        {
            ExecuteForLoadType(RuntimeInitializeLoadType.AfterSceneLoad);
        }

        private static void ExecuteForLoadType(RuntimeInitializeLoadType loadType)
        {
            AttributeMetadataCache cache = AttributeMetadataCache.Instance;
            AttributeMetadataCache.AutoLoadSingletonEntry[] entries =
                cache != null
                    ? cache.AutoLoadSingletons
                    : Array.Empty<AttributeMetadataCache.AutoLoadSingletonEntry>();
            entries ??= Array.Empty<AttributeMetadataCache.AutoLoadSingletonEntry>();

            ExecuteEntries(entries, loadType, enforceSingleExecution: true, requirePlayMode: true);
        }

        private static Action GetOrCreateLoader(AttributeMetadataCache.AutoLoadSingletonEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.typeName))
            {
                return null;
            }

            if (_cachedLoaders.TryGetValue(entry.typeName, out Action cached))
            {
                return cached;
            }

            lock (_loaderBuildLock)
            {
                if (_cachedLoaders.TryGetValue(entry.typeName, out cached))
                {
                    return cached;
                }

                Action loader = BuildLoader(entry);
                _cachedLoaders[entry.typeName] = loader;
                return loader;
            }
        }

        private static Action BuildLoader(AttributeMetadataCache.AutoLoadSingletonEntry entry)
        {
            Type singletonType = ReflectionHelpers.TryResolveType(entry.typeName);
            if (singletonType == null)
            {
                Debug.LogWarning(
                    $"SingletonAutoLoader: Unable to resolve type '{entry.typeName}' for auto-load."
                );
                return null;
            }

            switch (entry.kind)
            {
                case SingletonAutoLoadKind.Runtime:
                    return BuildRuntimeLoader(singletonType);
                case SingletonAutoLoadKind.ScriptableObject:
                    return BuildScriptableLoader(singletonType);
                default:
                    Debug.LogWarning(
                        $"SingletonAutoLoader: Unsupported singleton kind '{entry.kind}' for type '{entry.typeName}'."
                    );
                    return null;
            }
        }

        private static Action BuildRuntimeLoader(Type singletonType)
        {
            PropertyInfo instanceProperty = GetRuntimeInstanceProperty(singletonType);
            if (instanceProperty == null)
            {
                Debug.LogWarning(
                    $"SingletonAutoLoader: {singletonType.FullName} does not derive from RuntimeSingleton<>."
                );
                return null;
            }

            return () =>
            {
                _ = instanceProperty.GetValue(null);
            };
        }

        private static Action BuildScriptableLoader(Type singletonType)
        {
            PropertyInfo instanceProperty = GetScriptableInstanceProperty(singletonType);
            if (instanceProperty == null)
            {
                Debug.LogWarning(
                    $"SingletonAutoLoader: {singletonType.FullName} does not derive from ScriptableObjectSingleton<>."
                );
                return null;
            }

            return () =>
            {
                _ = instanceProperty.GetValue(null);
            };
        }

        private static PropertyInfo GetRuntimeInstanceProperty(Type singletonType)
        {
            lock (_loaderBuildLock)
            {
                if (_runtimeInstanceProperties.TryGetValue(singletonType, out PropertyInfo cached))
                {
                    return cached;
                }

                PropertyInfo property = ResolveInstanceProperty(
                    singletonType,
                    typeof(RuntimeSingleton<>)
                );
                _runtimeInstanceProperties[singletonType] = property;
                return property;
            }
        }

        private static PropertyInfo GetScriptableInstanceProperty(Type singletonType)
        {
            lock (_loaderBuildLock)
            {
                if (
                    _scriptableInstanceProperties.TryGetValue(
                        singletonType,
                        out PropertyInfo cached
                    )
                )
                {
                    return cached;
                }

                PropertyInfo property = ResolveInstanceProperty(
                    singletonType,
                    typeof(ScriptableObjectSingleton<>)
                );
                _scriptableInstanceProperties[singletonType] = property;
                return property;
            }
        }

        private static PropertyInfo ResolveInstanceProperty(
            Type singletonType,
            Type openGenericBase
        )
        {
            try
            {
                Type closed = openGenericBase.MakeGenericType(singletonType);

                // IL2CPP does not reliably validate closed generic constraints; check self-type inheritance explicitly.
                if (!closed.IsAssignableFrom(singletonType))
                {
                    return null;
                }

                return closed.GetProperty(
                    nameof(RuntimeSingleton<CoroutineHandler>.Instance),
                    BindingFlags.Public | BindingFlags.Static
                );
            }
            catch
            {
                return null;
            }
        }
    }
}
