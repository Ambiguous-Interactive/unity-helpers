// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestAssets
{
#if UNITY_EDITOR
    using System.Collections.Concurrent;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core.TestUtils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Manages shared prefab test fixtures with lazy-loading and reference counting.
    /// Pre-committed static prefabs are loaded once and cached for all tests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class provides a centralized way to manage prefab test fixtures that are shared
    /// across multiple test classes. Instead of generating fixtures at runtime, it uses
    /// pre-committed static prefabs with test handler components attached.
    /// </para>
    /// <para>
    /// Thread safety: Public methods that modify shared state (AcquireFixtures, ReleaseFixtures,
    /// PreloadAllAssets, etc.) use locks for correctness. The lazy-loading property getters for
    /// cached prefabs do NOT use locks; this is acceptable because Unity Editor APIs like
    /// AssetDatabase.LoadAssetAtPath must be called from the main thread anyway.
    /// </para>
    /// </remarks>
    public static class SharedPrefabTestFixtures
    {
        private const string StaticAssetsDir =
            "Packages/com.wallstop-studios.unity-helpers/Tests/Editor/TestAssets/Prefabs";
        private const string DynamicAssetsDir = "Assets/Temp/DynamicPrefabFixtures";

        private static readonly object Lock = new();
        private static int _referenceCount;
        private static bool _fixturesVerified;

        // Cached prefab references - loaded once on first access
        private static GameObject _cachedPrefabHandler;
        private static GameObject _cachedNestedHandler;
        private static GameObject _cachedMultipleHandlers;
        private static GameObject _cachedCombinedHandler;
        private static GameObject _cachedSceneHandler;

        // Dynamic prefabs for tests needing unique instances
        private static readonly ConcurrentDictionary<string, DynamicPrefabFixture> DynamicFixtures =
            new();

        /// <summary>
        /// Represents a dynamically generated prefab fixture with its associated metadata.
        /// </summary>
        public sealed class DynamicPrefabFixture
        {
            /// <summary>
            /// The asset path of the dynamic fixture.
            /// </summary>
            public string AssetPath { get; internal set; }

            /// <summary>
            /// The cached prefab for the dynamic fixture.
            /// </summary>
            public GameObject Prefab { get; internal set; }
        }

        /// <summary>
        /// Path to the shared prefab handler fixture (single TestPrefabAssetChangeHandler component).
        /// </summary>
        public static string PrefabHandlerPath => $"{StaticAssetsDir}/test_prefab_handler.prefab";

        /// <summary>
        /// Path to the shared nested handler fixture (prefab with nested child containing handler).
        /// </summary>
        public static string NestedHandlerPath => $"{StaticAssetsDir}/test_nested_handler.prefab";

        /// <summary>
        /// Path to the shared multiple handlers fixture (prefab with multiple handler components).
        /// </summary>
        public static string MultipleHandlersPath =>
            $"{StaticAssetsDir}/test_multiple_handlers.prefab";

        /// <summary>
        /// Path to the shared combined handler fixture (prefab with combined handler types).
        /// </summary>
        public static string CombinedHandlerPath =>
            $"{StaticAssetsDir}/test_combined_handler.prefab";

        /// <summary>
        /// Path to the shared scene handler fixture (prefab designed for scene instantiation tests).
        /// </summary>
        public static string SceneHandlerPath => $"{StaticAssetsDir}/test_scene_handler.prefab";

        /// <summary>
        /// Gets the cached prefab handler, loading it on first access.
        /// </summary>
        public static GameObject PrefabHandler =>
            _cachedPrefabHandler ??= AssetDatabase.LoadAssetAtPath<GameObject>(PrefabHandlerPath);

        /// <summary>
        /// Gets the cached nested handler prefab, loading it on first access.
        /// </summary>
        public static GameObject NestedHandler =>
            _cachedNestedHandler ??= AssetDatabase.LoadAssetAtPath<GameObject>(NestedHandlerPath);

        /// <summary>
        /// Gets the cached multiple handlers prefab, loading it on first access.
        /// </summary>
        public static GameObject MultipleHandlers =>
            _cachedMultipleHandlers ??= AssetDatabase.LoadAssetAtPath<GameObject>(
                MultipleHandlersPath
            );

        /// <summary>
        /// Gets the cached combined handler prefab, loading it on first access.
        /// </summary>
        public static GameObject CombinedHandler =>
            _cachedCombinedHandler ??= AssetDatabase.LoadAssetAtPath<GameObject>(
                CombinedHandlerPath
            );

        /// <summary>
        /// Gets the cached scene handler prefab, loading it on first access.
        /// </summary>
        public static GameObject SceneHandler =>
            _cachedSceneHandler ??= AssetDatabase.LoadAssetAtPath<GameObject>(SceneHandlerPath);

        /// <summary>
        /// Gets the current reference count for diagnostic purposes.
        /// </summary>
        public static int ReferenceCount
        {
            get
            {
                lock (Lock)
                {
                    return _referenceCount;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether fixtures have been verified.
        /// </summary>
        public static bool FixturesVerified
        {
            get
            {
                lock (Lock)
                {
                    return _fixturesVerified;
                }
            }
        }

        /// <summary>
        /// Acquires a reference to the shared fixtures. Verifies assets if this is the first call.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Call this method in your test fixture's <c>[OneTimeSetUp]</c> method. The method is
        /// thread-safe and uses reference counting to track how many consumers are using the fixtures.
        /// </para>
        /// </remarks>
        public static void AcquireFixtures()
        {
            lock (Lock)
            {
                _referenceCount++;
                if (!_fixturesVerified)
                {
                    VerifyFixtures();
                    _fixturesVerified = true;
                }
            }
        }

        /// <summary>
        /// Releases a reference to the shared fixtures. Releases dynamic fixtures when the last reference is released.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Call this method in your test fixture's <c>[OneTimeTearDown]</c> method. The method is
        /// thread-safe and uses reference counting to determine when to clean up.
        /// </para>
        /// </remarks>
        public static void ReleaseFixtures()
        {
            lock (Lock)
            {
                _referenceCount--;
                if (_referenceCount <= 0)
                {
                    ReleaseDynamicFixtures();
                    _referenceCount = 0;
                }
            }
        }

        /// <summary>
        /// Preloads all assets into cache. Call this from assembly-level setup to ensure
        /// all assets are loaded once before any tests run.
        /// </summary>
        /// <remarks>
        /// This method is thread-safe and can be called multiple times safely.
        /// It will only load assets that aren't already cached.
        /// </remarks>
        public static void PreloadAllAssets()
        {
            lock (Lock)
            {
                // Force-load all prefabs into cache by accessing the properties
                _ = PrefabHandler;
                _ = NestedHandler;
                _ = MultipleHandlers;
                _ = CombinedHandler;
                _ = SceneHandler;

                _fixturesVerified = true;
            }
        }

        /// <summary>
        /// Releases all cached assets. Call this from assembly-level teardown.
        /// </summary>
        /// <remarks>
        /// This clears all cached references but does NOT delete the actual assets
        /// since they are pre-committed static files.
        /// </remarks>
        public static void ReleaseAllCachedAssets()
        {
            lock (Lock)
            {
                // Clear cached prefabs
                _cachedPrefabHandler = null;
                _cachedNestedHandler = null;
                _cachedMultipleHandlers = null;
                _cachedCombinedHandler = null;
                _cachedSceneHandler = null;

                _fixturesVerified = false;
                ReleaseDynamicFixtures();
                _referenceCount = 0;
            }
        }

        /// <summary>
        /// Forces cleanup of fixture references regardless of reference count.
        /// </summary>
        public static void ForceCleanup()
        {
            lock (Lock)
            {
                ReleaseDynamicFixtures();
                _referenceCount = 0;
                _fixturesVerified = false;
            }
        }

        /// <summary>
        /// Gets or creates a dynamic prefab for tests needing unique instances.
        /// The dynamic prefab is cached and will be cleaned up when fixtures are released.
        /// </summary>
        /// <typeparam name="T">The component type to add to the prefab.</typeparam>
        /// <param name="key">A unique key identifying this dynamic prefab.</param>
        /// <returns>A dynamic prefab fixture containing the created prefab.</returns>
        /// <remarks>
        /// <para>
        /// Use this method when a test needs a unique prefab instance that can be modified
        /// without affecting other tests. The prefab is created in Assets/Temp/ and
        /// automatically cleaned up.
        /// </para>
        /// </remarks>
        public static DynamicPrefabFixture GetOrCreateDynamicPrefab<T>(string key)
            where T : Component
        {
            return DynamicFixtures.GetOrAdd(key, _ => CreateDynamicPrefab<T>(key));
        }

        /// <summary>
        /// Gets the directory path for dynamic fixtures.
        /// </summary>
        /// <returns>The Unity asset path for the dynamic fixtures directory.</returns>
        public static string GetDynamicFixturesDirectory()
        {
            return DynamicAssetsDir;
        }

        /// <summary>
        /// Gets the static assets directory path used for fixtures.
        /// </summary>
        /// <returns>The Unity asset path for the static test assets directory.</returns>
        public static string GetSharedDirectory()
        {
            return StaticAssetsDir;
        }

        private static DynamicPrefabFixture CreateDynamicPrefab<T>(string key)
            where T : Component
        {
            EnsureDynamicDirectory();

            string path = $"{DynamicAssetsDir}/{key}.prefab";
            GameObject go = new(typeof(T).Name);
            go.AddComponent<T>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go); // UNH-SUPPRESS: Test infrastructure cleanup after saving prefab
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            return new DynamicPrefabFixture { AssetPath = path, Prefab = prefab };
        }

        private static void EnsureDynamicDirectory()
        {
            // First clean any numbered duplicates from previous failed runs
            // This prevents Unity from creating "Temp 1", "Temp 2", etc.
            TempFolderCleanupUtility.CleanupTempDuplicates();
            AssetDatabaseBatchHelper.EnsureAssetFolder(DynamicAssetsDir);
        }

        private static void ReleaseDynamicFixtures()
        {
            if (DynamicFixtures.IsEmpty)
            {
                return;
            }

            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                foreach (
                    System.Collections.Generic.KeyValuePair<
                        string,
                        DynamicPrefabFixture
                    > kvp in DynamicFixtures
                )
                {
                    DynamicPrefabFixture fixture = kvp.Value;
                    if (
                        !string.IsNullOrEmpty(fixture.AssetPath)
                        && AssetDatabase.LoadAssetAtPath<Object>(fixture.AssetPath) != null
                    )
                    {
                        AssetDatabase.DeleteAsset(fixture.AssetPath);
                    }
                    fixture.Prefab = null;
                }

                // Clean up the dynamic assets directory if it exists and is empty
                if (AssetDatabase.IsValidFolder(DynamicAssetsDir))
                {
                    string[] remainingAssets = AssetDatabase.FindAssets(
                        "",
                        new[] { DynamicAssetsDir }
                    );
                    if (remainingAssets.Length == 0)
                    {
                        AssetDatabase.DeleteAsset(DynamicAssetsDir);
                    }
                }
            }

            DynamicFixtures.Clear();

            // Clean up "Temp N" duplicates AFTER the batch scope ends.
            // The batch scope's Refresh() may create new duplicates, so we must clean up after.
            TempFolderCleanupUtility.CleanupTempDuplicatesWithRetry();
        }

        private static void VerifyFixtures()
        {
            // Use warnings instead of asserts since prefab files may not exist yet.
            // Tests that depend on these prefabs should skip gracefully when assets are missing.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabHandlerPath) == null)
            {
                Debug.LogWarning(
                    $"[SharedPrefabTestFixtures] Missing prefab fixture: {PrefabHandlerPath}"
                );
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(NestedHandlerPath) == null)
            {
                Debug.LogWarning(
                    $"[SharedPrefabTestFixtures] Missing prefab fixture: {NestedHandlerPath}"
                );
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MultipleHandlersPath) == null)
            {
                Debug.LogWarning(
                    $"[SharedPrefabTestFixtures] Missing prefab fixture: {MultipleHandlersPath}"
                );
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CombinedHandlerPath) == null)
            {
                Debug.LogWarning(
                    $"[SharedPrefabTestFixtures] Missing prefab fixture: {CombinedHandlerPath}"
                );
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(SceneHandlerPath) == null)
            {
                Debug.LogWarning(
                    $"[SharedPrefabTestFixtures] Missing prefab fixture: {SceneHandlerPath}"
                );
            }
        }
    }
#endif
}
