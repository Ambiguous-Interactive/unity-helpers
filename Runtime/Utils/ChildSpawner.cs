// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using Core.Extension;
    using Core.Helper;
    using UnityEngine;
    using UnityEngine.Serialization;

    /// <summary>
    /// Selects the MonoBehaviour lifecycle events that should trigger prefab instantiation.
    /// </summary>
    [Flags]
    public enum ChildSpawnMethod
    {
        /// <summary>
        /// No child creation will occur. Mainly retained for serialization compatibility.
        /// </summary>
        [Obsolete]
        None = 0,

        /// <summary>
        /// Spawn children during <c>Awake</c>.
        /// </summary>
        Awake = 1 << 0,

        /// <summary>
        /// Spawn children when the component is enabled.
        /// </summary>
        OnEnabled = 1 << 1,

        /// <summary>
        /// Spawn children during <c>Start</c>.
        /// </summary>
        Start = 1 << 2,
    }

    /// <summary>
    /// Instantiates a curated list of prefabs as children of the current GameObject while ensuring
    /// duplicates across scenes are avoided and optional DontDestroyOnLoad behaviour is applied.
    /// </summary>
    /// <remarks>Destroyed children can be spawned again by the next configured lifecycle event.</remarks>
    [DisallowMultipleComponent]
    public sealed class ChildSpawner : MonoBehaviour
    {
        private static readonly Dictionary<GameObject, GameObject> SpawnedPrefabs = new();

        [FormerlySerializedAs("dontDestroyOnLoad")]
        [SerializeField]
        internal bool _dontDestroyOnLoad = true;

        [SerializeField]
        internal ChildSpawnMethod _spawnMethod = ChildSpawnMethod.Start;

        /// <summary>
        /// Prefabs that are spawned in all environments where the component executes.
        /// </summary>
        [SerializeField]
        internal GameObject[] _prefabs = Array.Empty<GameObject>();

        /// <summary>
        /// Prefabs spawned when running inside the Unity editor only.
        /// </summary>
        [SerializeField]
        internal GameObject[] _editorOnlyPrefabs = Array.Empty<GameObject>();

        /// <summary>
        /// Prefabs spawned when running in the editor or a development build.
        /// </summary>
        [SerializeField]
        internal GameObject[] _developmentOnlyPrefabs = Array.Empty<GameObject>();

        private readonly Dictionary<GameObject, List<GameObject>> _spawnedPrefabs = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void ClearSpawnedPrefabs()
        {
            SpawnedPrefabs.Clear();
        }

        /// <summary>
        /// Removes Unity's default "(Clone)" suffix from instantiated prefab names.
        /// </summary>
        /// <param name="child">The instantiated child whose name should be cleaned.</param>
        private static void CleanName(GameObject child)
        {
            child.name = child.name.Replace("(Clone)", string.Empty);
        }

        private void Awake()
        {
            if (_spawnMethod.HasFlagNoAlloc(ChildSpawnMethod.Awake))
            {
                Spawn();
            }
        }

        private void OnEnable()
        {
            if (_spawnMethod.HasFlagNoAlloc(ChildSpawnMethod.OnEnabled))
            {
                Spawn();
            }
        }

        private void Start()
        {
            if (_spawnMethod.HasFlagNoAlloc(ChildSpawnMethod.Start))
            {
                Spawn();
            }
        }

        /// <summary>
        /// Checks all prefab arrays for duplicates and logs an error if any are found.
        /// Uses pooled collections to avoid allocations. Null prefabs are skipped.
        /// </summary>
        private void CheckForDuplicatePrefabs()
        {
            GameObject[] prefabs = _prefabs ?? Array.Empty<GameObject>();
            GameObject[] editorOnlyPrefabs = _editorOnlyPrefabs ?? Array.Empty<GameObject>();
            GameObject[] developmentOnlyPrefabs =
                _developmentOnlyPrefabs ?? Array.Empty<GameObject>();

            int totalCount =
                prefabs.Length + editorOnlyPrefabs.Length + developmentOnlyPrefabs.Length;

            if (totalCount == 0)
            {
                return;
            }

            using PooledResource<HashSet<GameObject>> seenLease = Buffers<GameObject>.HashSet.Get(
                out HashSet<GameObject> seen
            );
            using PooledResource<HashSet<GameObject>> duplicatesLease =
                Buffers<GameObject>.HashSet.Get(out HashSet<GameObject> duplicates);

            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null)
                {
                    continue;
                }
                if (!seen.Add(prefab))
                {
                    duplicates.Add(prefab);
                }
            }

            foreach (GameObject prefab in editorOnlyPrefabs)
            {
                if (prefab == null)
                {
                    continue;
                }
                if (!seen.Add(prefab))
                {
                    duplicates.Add(prefab);
                }
            }

            foreach (GameObject prefab in developmentOnlyPrefabs)
            {
                if (prefab == null)
                {
                    continue;
                }
                if (!seen.Add(prefab))
                {
                    duplicates.Add(prefab);
                }
            }

            if (duplicates.Count == 0)
            {
                return;
            }

            using PooledResource<List<string>> namesLease = Buffers<string>.GetList(
                duplicates.Count,
                out List<string> duplicateNames
            );

            foreach (GameObject prefab in duplicates)
            {
                duplicateNames.Add(prefab.name);
            }

            // Skip recoverable authoring mistakes with a warning so valid entries still spawn.
            this.LogWarn($"Duplicate child prefab detected: {string.Join(",", duplicateNames)}");
        }

        /// <summary>
        /// Performs the spawning process for all configured prefab collections, applying naming
        /// suffixes and duplicate checks for each group.
        /// </summary>
        private void Spawn()
        {
            TrySetDontDestroyOnLoad();
            CheckForDuplicatePrefabs();

            int count = 0;
            SpawnGroup(_prefabs, string.Empty, ref count);
#if UNITY_EDITOR
            if (Application.isEditor)
            {
                SpawnGroup(_editorOnlyPrefabs, "EDITOR-ONLY ", ref count);
            }
#endif
            if (Application.isEditor || Debug.isDebugBuild)
            {
                SpawnGroup(_developmentOnlyPrefabs, "DEVELOPMENT-ONLY ", ref count);
            }
        }

        private void SpawnGroup(GameObject[] prefabs, string label, ref int count)
        {
            if (prefabs == null || prefabs.Length == 0)
            {
                return;
            }

            using PooledResource<List<(GameObject prefab, GameObject child)>> spawnedLease =
                Buffers<(GameObject prefab, GameObject child)>.List.Get(
                    out List<(GameObject prefab, GameObject child)> spawned
                );
            foreach (GameObject prefab in prefabs)
            {
                GameObject child = Spawn(prefab);
                if (child == null)
                {
                    continue;
                }
                child.name = $"{child.name} ({label}{count++:00})";
                spawned.Add((prefab, child));
            }

            // Preserve duplicate entries within one nonpersistent group while suppressing later lifecycle calls.
            foreach ((GameObject prefab, GameObject child) entry in spawned)
            {
                if (!_spawnedPrefabs.TryGetValue(entry.prefab, out List<GameObject> instances))
                {
                    instances = new List<GameObject>(1);
                    _spawnedPrefabs.Add(entry.prefab, instances);
                }
                instances.Add(entry.child);
            }
        }

        private bool HasLiveSpawnedInstance(GameObject prefab)
        {
            if (!_spawnedPrefabs.TryGetValue(prefab, out List<GameObject> instances))
            {
                return false;
            }
            foreach (GameObject instance in instances)
            {
                if (instance != null)
                {
                    return true;
                }
            }
            instances.Clear();
            return false;
        }

        /// <summary>
        /// Instantiates <paramref name="prefab"/> when no tracked instance is still alive.
        /// </summary>
        /// <param name="prefab">Prefab to spawn.</param>
        /// <returns>The instantiated child instance, or <c>null</c> if the spawn is skipped.</returns>
        private GameObject Spawn(GameObject prefab)
        {
            if (prefab == null)
            {
                // Skip recoverable authoring mistakes with a warning so valid entries still spawn.
                this.LogWarn($"Unexpectedly null prefab - cannot spawn.");
                return null;
            }

            if (HasLiveSpawnedInstance(prefab))
            {
                return null;
            }

            if (SpawnedPrefabs.TryGetValue(prefab, out GameObject existing))
            {
                if (existing != null)
                {
                    return null;
                }
                SpawnedPrefabs.Remove(prefab);
            }

            GameObject child = Instantiate(prefab, transform);
            if (child == null)
            {
                return null;
            }
            CleanName(child);
            if (
                child.IsDontDestroyOnLoad()
                || gameObject.IsDontDestroyOnLoad()
                || prefab.IsDontDestroyOnLoad()
            )
            {
                SpawnedPrefabs[prefab] = child;
            }
            return child;
        }

        /// <summary>
        /// Applies <see cref="UnityEngine.Object.DontDestroyOnLoad(UnityEngine.Object)"/> when configured to
        /// keep the spawner alive between scene loads.
        /// </summary>
        private void TrySetDontDestroyOnLoad()
        {
            if (_dontDestroyOnLoad && Application.isPlaying && !gameObject.IsDontDestroyOnLoad())
            {
                DontDestroyOnLoad(gameObject);
            }
        }
    }
}
