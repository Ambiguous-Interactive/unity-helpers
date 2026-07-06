// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestAssets
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Concurrent;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core.TestUtils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Manages shared ScriptableObject test fixtures with reference counting for cross-class fixture sharing.
    /// Provides both pre-committed static assets and dynamically created fixtures.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class provides a centralized way to manage test fixtures that are shared
    /// across multiple test classes. It supports two types of fixtures:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <term>Static Assets</term>
    /// <description>Pre-committed .asset files loaded from disk</description>
    /// </item>
    /// <item>
    /// <term>Dynamic Fixtures</term>
    /// <description>Runtime-generated ScriptableObjects cached by key</description>
    /// </item>
    /// </list>
    /// <para>
    /// Thread safety: Public methods that modify shared state use locks for correctness.
    /// The lazy-loading property getters for cached assets do NOT use locks; this is acceptable
    /// because Unity Editor APIs like AssetDatabase.LoadAssetAtPath must be called from the
    /// main thread anyway.
    /// </para>
    /// </remarks>
    public static class SharedEditorTestFixtures
    {
        private const string StaticAssetsDir =
            "Packages/com.wallstop-studios.unity-helpers/Tests/Editor/TestAssets/ScriptableObjects";

        private const string DynamicAssetsDir = "Assets/Temp/DynamicEditorFixtures";

        private static readonly object Lock = new();
        private static int _referenceCount;
        private static bool _fixturesInitialized;

        private static readonly ConcurrentDictionary<string, DynamicFixture> DynamicFixtures =
            new();

        /// <summary>
        /// Represents a dynamically generated ScriptableObject fixture with its associated metadata.
        /// </summary>
        public sealed class DynamicFixture
        {
            /// <summary>
            /// The asset path of the dynamic fixture (null for non-persisted fixtures).
            /// </summary>
            public string AssetPath { get; internal set; }

            /// <summary>
            /// The cached ScriptableObject instance.
            /// </summary>
            public ScriptableObject Asset { get; internal set; }

            /// <summary>
            /// The type of the ScriptableObject.
            /// </summary>
            public Type AssetType { get; internal set; }

            /// <summary>
            /// Whether this fixture is persisted to disk.
            /// </summary>
            public bool IsPersisted { get; internal set; }
        }

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
        /// Gets a value indicating whether fixtures are currently initialized.
        /// </summary>
        public static bool FixturesInitialized
        {
            get
            {
                lock (Lock)
                {
                    return _fixturesInitialized;
                }
            }
        }

        /// <summary>
        /// Acquires a reference to the shared fixtures. Initializes paths if this is the first call.
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

                if (!_fixturesInitialized)
                {
                    InitializeFixtures();
                    _fixturesInitialized = true;
                }
            }
        }

        /// <summary>
        /// Releases a reference to the shared fixtures. Cleans up when the last reference is released.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Call this method in your test fixture's <c>[OneTimeTearDown]</c> method. The method is
        /// thread-safe and uses reference counting to determine when to reset.
        /// </para>
        /// </remarks>
        public static void ReleaseFixtures()
        {
            lock (Lock)
            {
                _referenceCount--;

                if (_referenceCount <= 0)
                {
                    _referenceCount = 0;
                    CleanupAllFixtures();
                    ReleaseDynamicFixtures();
                    _fixturesInitialized = false;
                }
            }
        }

        /// <summary>
        /// Forces cleanup of fixture references regardless of reference count.
        /// Use this only for emergency cleanup or test infrastructure resets.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Warning:</strong> This method bypasses reference counting and immediately
        /// resets all fixture state. Only use this when you need to ensure a clean state,
        /// such as during global test teardown.
        /// </para>
        /// </remarks>
        public static void ForceCleanup()
        {
            lock (Lock)
            {
                CleanupAllFixtures();
                ReleaseDynamicFixtures();
                _referenceCount = 0;
                _fixturesInitialized = false;
            }
        }

        /// <summary>
        /// Gets the static assets directory path used for fixtures.
        /// </summary>
        /// <returns>The Unity asset path for the static test assets directory.</returns>
        public static string GetSharedDirectory()
        {
            return StaticAssetsDir;
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
        /// Preloads all assets into cache. Call this from assembly-level setup to ensure
        /// all assets are loaded once before any tests run.
        /// </summary>
        /// <remarks>
        /// This method is thread-safe and can be called multiple times safely.
        /// It will only initialize fixtures that aren't already initialized.
        /// </remarks>
        public static void PreloadAllAssets()
        {
            lock (Lock)
            {
                if (!_fixturesInitialized)
                {
                    AcquireFixtures();
                }
            }
        }

        /// <summary>
        /// Releases all cached assets. Call this from assembly-level teardown.
        /// </summary>
        /// <remarks>
        /// This clears all cached references and releases dynamic fixtures.
        /// </remarks>
        public static void ReleaseAllCachedAssets()
        {
            lock (Lock)
            {
                CleanupAllFixtures();
                ReleaseDynamicFixtures();
                _referenceCount = 0;
                _fixturesInitialized = false;
            }
        }

        /// <summary>
        /// Gets or creates a dynamically generated ScriptableObject fixture with the specified key.
        /// Dynamic fixtures are cached and shared across all tests that request the same key.
        /// </summary>
        /// <typeparam name="T">The type of ScriptableObject to create.</typeparam>
        /// <param name="key">A unique key identifying this fixture configuration.</param>
        /// <param name="persist">Whether to persist the fixture to disk (default: false).</param>
        /// <returns>The dynamic fixture containing the ScriptableObject and metadata.</returns>
        /// <remarks>
        /// <para>
        /// This method creates ScriptableObjects on-demand and caches them for reuse.
        /// The fixture is created once and shared across all tests that need the same key.
        /// </para>
        /// <para>
        /// Non-persisted fixtures are faster but won't survive Unity domain reloads.
        /// Persisted fixtures are saved to disk and can be loaded after domain reload.
        /// </para>
        /// </remarks>
        public static DynamicFixture GetOrCreateDynamic<T>(string key, bool persist = false)
            where T : ScriptableObject
        {
            string fullKey = typeof(T).FullName + "::" + key;
            return DynamicFixtures.GetOrAdd(fullKey, _ => CreateDynamicFixture<T>(key, persist));
        }

        /// <summary>
        /// Gets or creates a dynamically generated ScriptableObject fixture with the specified key.
        /// Dynamic fixtures are cached and shared across all tests that request the same key.
        /// </summary>
        /// <param name="type">The type of ScriptableObject to create.</param>
        /// <param name="key">A unique key identifying this fixture configuration.</param>
        /// <param name="persist">Whether to persist the fixture to disk (default: false).</param>
        /// <returns>The dynamic fixture containing the ScriptableObject and metadata.</returns>
        public static DynamicFixture GetOrCreateDynamic(Type type, string key, bool persist = false)
        {
            if (type == null || !typeof(ScriptableObject).IsAssignableFrom(type))
            {
                return null;
            }

            string fullKey = type.FullName + "::" + key;
            return DynamicFixtures.GetOrAdd(
                fullKey,
                _ => CreateDynamicFixtureByType(type, key, persist)
            );
        }

        /// <summary>
        /// Releases all dynamic fixtures and cleans up their assets.
        /// </summary>
        /// <remarks>
        /// This method deletes all dynamically generated assets from the project
        /// and destroys non-persisted ScriptableObject instances.
        /// It should be called during test fixture teardown.
        /// </remarks>
        public static void ReleaseDynamicFixtures()
        {
            if (DynamicFixtures.IsEmpty)
            {
                return;
            }

            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: true))
            {
                foreach (
                    System.Collections.Generic.KeyValuePair<
                        string,
                        DynamicFixture
                    > kvp in DynamicFixtures
                )
                {
                    DynamicFixture fixture = kvp.Value;
                    if (fixture == null)
                    {
                        continue;
                    }

                    if (
                        fixture.IsPersisted
                        && !string.IsNullOrEmpty(fixture.AssetPath)
                        && AssetDatabase.LoadAssetAtPath<Object>(fixture.AssetPath) != null
                    )
                    {
                        AssetDatabase.DeleteAsset(fixture.AssetPath);
                    }
                    else if (fixture.Asset != null)
                    {
                        Object.DestroyImmediate(fixture.Asset);
                    }

                    fixture.Asset = null;
                }
            }

            DynamicFixtures.Clear();

            if (AssetDatabase.IsValidFolder(DynamicAssetsDir))
            {
                string[] remainingAssets = AssetDatabase.FindAssets("", new[] { DynamicAssetsDir });
                if (remainingAssets.Length == 0)
                {
                    AssetDatabase.DeleteAsset(DynamicAssetsDir);
                }
            }

            // Clean up "Temp N" duplicates after deleting dynamic assets and folders.
            TempFolderCleanupUtility.CleanupTempDuplicatesWithRetry();
        }

        /// <summary>
        /// Loads a static asset from the shared directory.
        /// </summary>
        /// <typeparam name="T">The type of asset to load.</typeparam>
        /// <param name="fileName">The file name (without path) of the asset.</param>
        /// <returns>The loaded asset, or null if not found.</returns>
        public static T LoadStaticAsset<T>(string fileName)
            where T : Object
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            string path = StaticAssetsDir + "/" + fileName;
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static void InitializeFixtures()
        {
            // Verify the static assets directory exists
            if (!AssetDatabase.IsValidFolder(StaticAssetsDir))
            {
                // The directory doesn't exist yet - that's okay for dynamic-only usage
                return;
            }
        }

        private static void CleanupAllFixtures()
        {
            // Clear any cached static asset references here if needed in the future
        }

        private static DynamicFixture CreateDynamicFixture<T>(string key, bool persist)
            where T : ScriptableObject
        {
            T instance = ScriptableObject.CreateInstance<T>();
            DynamicFixture fixture = new()
            {
                Asset = instance,
                AssetType = typeof(T),
                IsPersisted = persist,
            };

            if (persist)
            {
                EnsureDynamicAssetsDirectory();
                string sanitizedKey = SanitizeKey(key);
                string typeName = typeof(T).Name;
                string assetPath = $"{DynamicAssetsDir}/dynamic_{typeName}_{sanitizedKey}.asset";
                AssetDatabase.CreateAsset(instance, assetPath);
                fixture.AssetPath = assetPath;
            }

            return fixture;
        }

        private static DynamicFixture CreateDynamicFixtureByType(
            Type type,
            string key,
            bool persist
        )
        {
            ScriptableObject instance = ScriptableObject.CreateInstance(type);
            DynamicFixture fixture = new()
            {
                Asset = instance,
                AssetType = type,
                IsPersisted = persist,
            };

            if (persist)
            {
                EnsureDynamicAssetsDirectory();
                string sanitizedKey = SanitizeKey(key);
                string typeName = type.Name;
                string assetPath = $"{DynamicAssetsDir}/dynamic_{typeName}_{sanitizedKey}.asset";
                AssetDatabase.CreateAsset(instance, assetPath);
                fixture.AssetPath = assetPath;
            }

            return fixture;
        }

        private static string SanitizeKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "default";
            }

            return key.Replace(" ", "_").Replace("/", "_").Replace("\\", "_").Replace(":", "_");
        }

        private static void EnsureDynamicAssetsDirectory()
        {
            // Clean up any leftover "Temp N" folders before creating directories.
            TempFolderCleanupUtility.CleanupTempDuplicates();
            AssetDatabaseBatchHelper.EnsureAssetFolder(DynamicAssetsDir);
        }
    }
#endif
}
