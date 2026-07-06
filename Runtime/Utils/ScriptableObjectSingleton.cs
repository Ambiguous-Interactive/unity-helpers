// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
#if WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using ScriptableObjectSingletonBase = Sirenix.OdinInspector.SerializedScriptableObject;
#else
    using ScriptableObjectSingletonBase = UnityEngine.ScriptableObject;
#endif
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /// <summary>
    /// Provides a global, lazily loaded singleton pattern for <see cref="ScriptableObject"/> assets.
    /// Ensures that exactly one asset instance of <typeparamref name="T"/> is used at runtime.
    /// </summary>
    /// <remarks>
    /// Lookup order (lazy):
    /// 1) Load from a custom Resources subfolder when the type is decorated with
    ///    <see cref="WallstopStudios.UnityHelpers.Core.Attributes.ScriptableSingletonPathAttribute"/>.
    /// 2) Load from a folder named after the type (Resources/&lt;TypeName&gt;).
    /// 3) Load by exact type name in Resources root, then fallback to all matches in Resources.
    ///
    /// If multiple assets are found, a warning is logged and the first result ordered by name is returned.
    /// The editor utility “ScriptableObject Singleton Creator” automatically creates and relocates assets to
    /// the correct path on editor load — see docs/features/editor-tools/editor-tools-guide.md#scriptableobject-singleton-creator.
    ///
    /// Odin compatibility: this runtime type derives from Odin's serialized base when the
    /// <c>odininspector</c> package is installed; otherwise it falls back to <see cref="ScriptableObject"/>.
    /// </remarks>
    /// <typeparam name="T">Concrete singleton ScriptableObject type that derives from this base.</typeparam>
    /// <threadsafety>
    /// Thread-safety notes:
    /// <list type="bullet">
    ///   <item><description>
    ///     The <see cref="Instance"/> property must only be accessed from the main thread (enforced by <see cref="UnityMainThreadGuard"/>).
    ///   </description></item>
    ///   <item><description>
    ///     Warning deduplication uses two independent locks: <c>_metadataFolderWarnings</c> and <c>_missingInstanceWarnings</c>.
    ///     These locks are never nested and are only held briefly to check/add to their respective HashSets.
    ///     No deadlock risk exists because each lock guards an independent data structure with no cross-dependencies.
    ///   </description></item>
    /// </list>
    /// </threadsafety>
    public abstract class ScriptableObjectSingleton<T> : ScriptableObjectSingletonBase
        where T : ScriptableObjectSingleton<T>
    {
        private static ScriptableObjectSingletonMetadata _metadataAsset;
        private static bool _metadataLoadAttempted;
        private static bool _metadataMissingWarningLogged;
        private static bool _metadataLoadFailureWarningLogged;
        private static readonly HashSet<string> _metadataFolderWarnings = new(
            StringComparer.Ordinal
        );
        private static readonly HashSet<string> _missingInstanceWarnings = new(
            StringComparer.Ordinal
        );
#if UNITY_EDITOR
        private static bool _duplicateMetadataWarningLogged;
#endif

        static ScriptableObjectSingleton()
        {
            ScriptableObjectSingletonRegistry.Register(ClearInstance);
        }

        private static string GetResourcesPath()
        {
            Type type = typeof(T);
            if (
                ReflectionHelpers.TryGetAttributeSafe<ScriptableSingletonPathAttribute>(
                    type,
                    out ScriptableSingletonPathAttribute attribute,
                    inherit: false
                ) && !string.IsNullOrWhiteSpace(attribute.resourcesPath)
            )
            {
                return attribute.resourcesPath;
            }

            // Return empty string to search from Resources root when no attribute is specified
            return string.Empty;
        }

        /// <summary>
        /// Clears the cached singleton instance, allowing a fresh load on next access.
        /// If an instance was loaded, <see cref="OnInstanceCleared"/> is invoked before clearing.
        /// </summary>
        internal static void ClearInstance()
        {
            if (!_lazyInstance.IsValueCreated)
            {
                return;
            }

            T value = _lazyInstance.Value;
            if (value != null)
            {
                value.OnInstanceCleared();
            }

            _lazyInstance = CreateLazy();
        }

        /// <summary>
        /// Called when the singleton instance is being cleared via <see cref="ClearInstance"/>.
        /// Override in derived classes to handle cleanup when the instance is cleared.
        /// Called automatically by <see cref="OnInstanceCleared"/>.
        /// </summary>
        /// <remarks>
        /// This method is intentionally named differently from Unity's <c>OnDisable()</c> magic method
        /// to avoid confusion. Unity's <c>OnDisable()</c> is called by the engine when a ScriptableObject
        /// is disabled or destroyed, whereas this method is only called when explicitly clearing the
        /// singleton instance via the registry.
        /// </remarks>
        protected virtual void OnInstanceCleared()
        {
            // Default no-op; derived classes may override
        }

        protected internal static Lazy<T> _lazyInstance = CreateLazy();

        internal static Lazy<T> CreateLazy()
        {
            return new Lazy<T>(() =>
            {
                Type type = typeof(T);
                List<T> candidates = new();

                bool metadataHit = TryPopulateCandidatesFromMetadata(type, candidates);

                if (!metadataHit)
                {
                    string resourcesPath = GetResourcesPath();
                    TryAddCandidate(candidates, LoadFromResourcesPath(resourcesPath, type.Name));

                    if (candidates.Count == 0)
                    {
                        TryAddCandidate(candidates, Resources.Load<T>(type.Name));
                    }

#if UNITY_EDITOR
                    AddEditorCandidates(type, resourcesPath, candidates);
#endif

                    if (candidates.Count == 0)
                    {
                        AddRuntimeDiscoveredCandidates(type, candidates);
                    }

                    if (candidates.Count == 0)
                    {
                        AddGlobalResourcesCandidates(type, candidates);
                    }
                }

                return ResolveCandidates(type, candidates);
            });
        }

        private static bool TryPopulateCandidatesFromMetadata(Type type, List<T> candidates)
        {
            if (!TryGetMetadataEntry(type, out ScriptableObjectSingletonMetadata.Entry entry))
            {
                WarnMetadataMissing(type);
                return false;
            }

            bool loadPathAttempted = false;
            string loadPath = entry.resourcesLoadPath;
            if (!string.IsNullOrWhiteSpace(loadPath))
            {
                loadPathAttempted = true;
                T direct = Resources.Load<T>(loadPath);
                if (direct != null)
                {
                    TryAddCandidate(candidates, direct);
#if UNITY_EDITOR
                    WarnDuplicateSingletonAssets(type, entry);
#endif
                    return candidates.Count > 0;
                }
            }

            string folder = entry.resourcesPath;
            if (!string.IsNullOrWhiteSpace(folder))
            {
                T[] scoped = Resources.LoadAll<T>(folder);
                if (scoped is { Length: > 0 })
                {
                    foreach (T candidate in scoped)
                    {
                        TryAddCandidate(candidates, candidate);
                    }

                    if (candidates.Count > 0)
                    {
                        return true;
                    }
                }
                else
                {
                    WarnMetadataFolderEmpty(type, folder);
                }
            }

            if (loadPathAttempted)
            {
                WarnMetadataLoadFailure(type, loadPath);
            }

            return false;
        }

        private static void TryAddCandidate(List<T> candidates, T candidate)
        {
            if (candidate == null || candidates == null)
            {
                return;
            }

            if (!candidates.Contains(candidate))
            {
                candidates.Add(candidate);
            }
        }

        private static T LoadFromResourcesPath(string resourcesPath, string typeName)
        {
            string loadPath = BuildLoadPath(resourcesPath, typeName);
            return string.IsNullOrEmpty(loadPath) ? null : Resources.Load<T>(loadPath);
        }

        private static string BuildLoadPath(string resourcesPath, string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(resourcesPath))
            {
                return typeName;
            }

            string trimmed = resourcesPath.Trim().Trim('/');
            return string.IsNullOrEmpty(trimmed) ? typeName : $"{trimmed}/{typeName}";
        }

#if UNITY_EDITOR
        private static void AddEditorCandidates(Type type, string resourcesPath, List<T> candidates)
        {
            string typeName = type.Name;
            List<string> candidatePaths = new();
            if (!string.IsNullOrWhiteSpace(resourcesPath))
            {
                candidatePaths.Add($"Assets/Resources/{resourcesPath}/{typeName}.asset");
            }
            candidatePaths.Add($"Assets/Resources/{typeName}.asset");

            foreach (string candidate in candidatePaths)
            {
                T atPath = AssetDatabase.LoadAssetAtPath<T>(candidate);
                if (atPath != null)
                {
                    TryAddCandidate(candidates, atPath);
                    return;
                }

                string guid = AssetDatabase.AssetPathToGUID(candidate);
                if (string.IsNullOrEmpty(guid))
                {
                    continue;
                }

                UnityEngine.Object[] allAtPath = AssetDatabase.LoadAllAssetsAtPath(candidate);
                if (allAtPath is { Length: > 0 })
                {
                    foreach (UnityEngine.Object obj in allAtPath)
                    {
                        if (obj == null || !type.IsInstanceOfType(obj))
                        {
                            continue;
                        }

                        TryAddCandidate(candidates, (T)obj);
                        return;
                    }
                }
            }
        }
#endif

        private static void AddRuntimeDiscoveredCandidates(Type type, List<T> candidates)
        {
            T[] found = Resources.FindObjectsOfTypeAll<T>();
            if (found is not { Length: > 0 })
            {
                return;
            }

            foreach (T candidate in found)
            {
                if (candidate == null || !type.IsInstanceOfType(candidate))
                {
                    continue;
                }

                TryAddCandidate(candidates, candidate);
            }
        }

        private static void AddGlobalResourcesCandidates(Type type, List<T> candidates)
        {
            T[] all = Resources.LoadAll<T>(string.Empty);
            if (all is not { Length: > 0 })
            {
                return;
            }

            foreach (T candidate in all)
            {
                if (candidate == null || !type.IsInstanceOfType(candidate))
                {
                    continue;
                }

                TryAddCandidate(candidates, candidate);
            }
        }

        private static T ResolveCandidates(Type type, List<T> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                WarnNoInstancesFound(type);
                return null;
            }

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            Debug.LogWarning(
                $"Found multiple ScriptableSingletons of type {type.Name}, defaulting to first by name."
            );
            candidates.Sort(UnityObjectNameComparer<T>.Instance);
            return candidates[0];
        }

        private static bool TryGetMetadataEntry(
            Type type,
            out ScriptableObjectSingletonMetadata.Entry entry
        )
        {
            ScriptableObjectSingletonMetadata metadata = _metadataAsset;
            if (metadata == null && !_metadataLoadAttempted)
            {
                _metadataLoadAttempted = true;
                metadata = Resources.Load<ScriptableObjectSingletonMetadata>(
                    ScriptableObjectSingletonMetadata.ResourcePath
                );
                _metadataAsset = metadata;
            }

            if (metadata == null)
            {
                entry = default;
                return false;
            }

            return metadata.TryGetEntry(type, out entry);
        }

        private static void WarnMetadataMissing(Type type)
        {
#if UNITY_EDITOR
            // Suppress warning during early initialization before singleton creator has run
            if (!ScriptableObjectSingletonInitState.InitialEnsureCompleted)
            {
                return;
            }
#endif
            string message =
                $"ScriptableObjectSingleton metadata entry not found for {type.FullName}. Falling back to heuristic Resources search.";
            LogMetadataWarning(message, ref _metadataMissingWarningLogged);
        }

        private static void WarnMetadataLoadFailure(Type type, string path)
        {
#if UNITY_EDITOR
            // Suppress warning during early initialization - asset may not be created yet
            if (!ScriptableObjectSingletonInitState.InitialEnsureCompleted)
            {
                return;
            }
#endif
            string message =
                $"ScriptableObjectSingleton metadata entry for {type.FullName} points to '{path}', but the asset could not be loaded.";
            LogMetadataWarning(message, ref _metadataLoadFailureWarningLogged);
        }

        private static void WarnMetadataFolderEmpty(Type type, string folder)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

#if UNITY_EDITOR
            // Suppress warning during early initialization - asset may not be created yet
            if (!ScriptableObjectSingletonInitState.InitialEnsureCompleted)
            {
                return;
            }
#endif

            string key = $"{type.FullName}|{folder}";
            lock (_metadataFolderWarnings)
            {
                if (!_metadataFolderWarnings.Add(key))
                {
                    return;
                }
            }

            Debug.LogWarning(
                $"ScriptableObjectSingleton metadata entry for {type.FullName} points to folder '{folder}', but no assets were found there. Falling back to heuristic search."
            );
#else
            _ = type;
            _ = folder;
#endif
        }

        private static void WarnNoInstancesFound(Type type)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
#if UNITY_EDITOR
            // Suppress warning during early initialization - asset may not be created yet
            if (!ScriptableObjectSingletonInitState.InitialEnsureCompleted)
            {
                return;
            }
#endif

            string key = type.FullName ?? type.Name;
            lock (_missingInstanceWarnings)
            {
                if (!_missingInstanceWarnings.Add(key))
                {
                    return;
                }
            }

            Debug.LogWarning(
                $"ScriptableObjectSingleton could not locate any asset for {type.FullName}. Returning null."
            );
#else
            _ = type;
#endif
        }

        private static void LogMetadataWarning(string message, ref bool flag)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!flag)
            {
                flag = true;
                Debug.LogWarning(message);
            }
#else
            flag = true;
            _ = message;
#endif
        }

        /// <summary>
        /// Gets a value indicating whether the lazy instance has been created and is non‑null.
        /// </summary>
        public static bool HasInstance =>
            _lazyInstance.IsValueCreated && _lazyInstance.Value != null;

        /// <summary>
        /// Gets the global asset instance, loading it from <c>Resources</c> on first access.
        /// </summary>
        /// <example>
        /// <code>
        /// [ScriptableSingletonPath("Settings/Audio")]
        /// public sealed class AudioSettings : ScriptableObjectSingleton&lt;AudioSettings&gt;
        /// {
        ///     public float musicVolume = 0.8f;
        /// }
        ///
        /// // Access anywhere
        /// float volume = AudioSettings.Instance.musicVolume;
        /// </code>
        /// </example>
        public static T Instance
        {
            get
            {
                if (_lazyInstance.IsValueCreated)
                {
                    return _lazyInstance.Value;
                }

                UnityMainThreadGuard.EnsureMainThread();
                return _lazyInstance.Value;
            }
        }

#if UNITY_EDITOR
        private static void WarnDuplicateSingletonAssets(
            Type type,
            ScriptableObjectSingletonMetadata.Entry entry
        )
        {
            if (_duplicateMetadataWarningLogged)
            {
                return;
            }

            string folder = entry.resourcesPath;
            string loadPath = entry.resourcesLoadPath;
            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(loadPath))
            {
                return;
            }

            string assetFolder = $"Assets/Resources/{folder}".Replace("\\", "/").TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(assetFolder))
            {
                return;
            }

            string canonicalAssetPath = BuildCanonicalAssetPath(loadPath);
            string[] guids = AssetDatabase.FindAssets("t:" + type.Name, new[] { assetFolder });
            if (guids == null || guids.Length <= 1)
            {
                return;
            }

            List<string> duplicates = new();
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (
                    string.IsNullOrWhiteSpace(assetPath)
                    || string.Equals(
                        assetPath,
                        canonicalAssetPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                UnityEngine.Object candidate = AssetDatabase.LoadAssetAtPath(assetPath, type);
                if (candidate != null)
                {
                    duplicates.Add(assetPath);
                }
            }

            if (duplicates.Count == 0)
            {
                return;
            }

            _duplicateMetadataWarningLogged = true;
            string canonicalLabel = string.IsNullOrWhiteSpace(canonicalAssetPath)
                ? loadPath
                : canonicalAssetPath;
            Debug.LogWarning(
                $"ScriptableObjectSingleton detected duplicate assets for {type.FullName} under '{assetFolder}'. Using '{canonicalLabel}'. Remove extra copies or add [AllowDuplicateCleanup] attribute for automatic cleanup:{Environment.NewLine} - {string.Join(Environment.NewLine + " - ", duplicates)}"
            );
        }

        private static string BuildCanonicalAssetPath(string loadPath)
        {
            if (string.IsNullOrWhiteSpace(loadPath))
            {
                return null;
            }

            string sanitized = loadPath.Replace("\\", "/").Trim('/');
            if (string.IsNullOrEmpty(sanitized))
            {
                return null;
            }

            return $"Assets/Resources/{sanitized}.asset".Replace("//", "/");
        }
#endif
    }
}
