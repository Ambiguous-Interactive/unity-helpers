// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Sprites
{
#if UNITY_EDITOR
    using System.Collections.Concurrent;
    using System.IO;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core.TestUtils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Manages shared sprite test fixtures with reference counting for cross-class fixture sharing.
    /// Fixtures are pre-committed static assets that are verified on the first <see cref="AcquireFixtures"/> call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class provides a centralized way to manage test fixtures that are shared
    /// across multiple test classes. Instead of generating fixtures at runtime, it uses
    /// pre-committed static PNG assets with proper TextureImporter settings.
    /// </para>
    /// <para>
    /// Thread safety: Public methods that modify shared state (AcquireFixtures, ReleaseFixtures,
    /// ForceCleanup, etc.) use locks for correctness. The lazy-loading property getters for
    /// cached textures and importers do NOT use locks; this is acceptable because Unity Editor
    /// APIs like AssetDatabase.LoadAssetAtPath must be called from the main thread anyway.
    /// </para>
    /// </remarks>
    public static class SharedSpriteTestFixtures
    {
        private const string StaticAssetsDir =
            "Packages/com.wallstop-studios.unity-helpers/Tests/Editor/TestAssets/Sprites";

        private static readonly object Lock = new();
        private static int _referenceCount;
        private static bool _fixturesCreated;

        // Cached loaded assets - loaded once on first access, reused for all tests
        private static Texture2D _cached2x2Texture;
        private static Texture2D _cached4x4Texture;
        private static Texture2D _cached8x8Texture;
        private static Texture2D _cachedSingleTexture;
        private static Texture2D _cachedWideTexture;
        private static Texture2D _cachedTallTexture;
        private static Texture2D _cachedOddTexture;
        private static Texture2D _cachedLarge512Texture;
        private static Texture2D _cachedNpot100x200Texture;
        private static Texture2D _cachedNpot150x75Texture;
        private static Texture2D _cachedPrime127Texture;
        private static Texture2D _cachedSmall16x16Texture;
        private static Texture2D _cachedBoundary256Texture;

        // Cached importers - loaded once on first access
        private static TextureImporter _cached2x2Importer;
        private static TextureImporter _cached4x4Importer;
        private static TextureImporter _cached8x8Importer;
        private static TextureImporter _cachedSingleImporter;
        private static TextureImporter _cachedWideImporter;
        private static TextureImporter _cachedTallImporter;
        private static TextureImporter _cachedOddImporter;
        private static TextureImporter _cachedLarge512Importer;
        private static TextureImporter _cachedNpot100x200Importer;
        private static TextureImporter _cachedNpot150x75Importer;
        private static TextureImporter _cachedPrime127Importer;
        private static TextureImporter _cachedSmall16x16Importer;
        private static TextureImporter _cachedBoundary256Importer;

        // Cached directory object for CreateExtractorWithSharedFixtures
        private static Object _cachedDirectoryObject;

        // Dynamic fixtures - runtime-generated textures cached by key
        private static readonly ConcurrentDictionary<string, DynamicFixture> DynamicFixtures =
            new();

        // Directory for dynamic fixtures
        private const string DynamicAssetsDir = "Assets/Temp/DynamicSpriteFixtures";

        /// <summary>
        /// Represents a dynamically generated sprite sheet fixture with its associated metadata.
        /// </summary>
        public sealed class DynamicFixture
        {
            /// <summary>
            /// The asset path of the dynamic fixture.
            /// </summary>
            public string AssetPath { get; internal set; }

            /// <summary>
            /// The cached texture for the dynamic fixture.
            /// </summary>
            public Texture2D Texture { get; internal set; }

            /// <summary>
            /// The cached texture importer for the dynamic fixture.
            /// </summary>
            public TextureImporter Importer { get; internal set; }

            /// <summary>
            /// The width of the texture in pixels.
            /// </summary>
            public int Width { get; internal set; }

            /// <summary>
            /// The height of the texture in pixels.
            /// </summary>
            public int Height { get; internal set; }

            /// <summary>
            /// The number of columns in the sprite grid.
            /// </summary>
            public int Columns { get; internal set; }

            /// <summary>
            /// The number of rows in the sprite grid.
            /// </summary>
            public int Rows { get; internal set; }

            /// <summary>
            /// The texture format used for this fixture.
            /// </summary>
            public TextureFormat Format { get; internal set; }
        }

        /// <summary>
        /// Path to the shared 2x2 sprite sheet fixture (64x64 pixels).
        /// </summary>
        public static string Shared2x2Path { get; private set; }

        /// <summary>
        /// Path to the shared 4x4 sprite sheet fixture (128x128 pixels).
        /// </summary>
        public static string Shared4x4Path { get; private set; }

        /// <summary>
        /// Path to the shared 8x8 sprite sheet fixture (256x256 pixels).
        /// </summary>
        public static string Shared8x8Path { get; private set; }

        /// <summary>
        /// Path to the shared single-mode sprite fixture (32x32 pixels, red color).
        /// </summary>
        public static string SharedSingleModePath { get; private set; }

        /// <summary>
        /// Path to the shared wide aspect ratio sprite sheet fixture (128x64 pixels, 4x2 grid).
        /// </summary>
        public static string SharedWidePath { get; private set; }

        /// <summary>
        /// Path to the shared tall aspect ratio sprite sheet fixture (64x128 pixels, 2x4 grid).
        /// </summary>
        public static string SharedTallPath { get; private set; }

        /// <summary>
        /// Path to the shared odd dimensions sprite sheet fixture (63x63 pixels, 3x3 grid).
        /// </summary>
        public static string SharedOddPath { get; private set; }

        /// <summary>
        /// Path to the shared large sprite sheet fixture (512x512 pixels, 16x16 grid, 256 sprites).
        /// </summary>
        public static string SharedLarge512Path { get; private set; }

        /// <summary>
        /// Path to the shared NPOT sprite sheet fixture (100x200 pixels, 2x4 grid).
        /// </summary>
        public static string SharedNpot100x200Path { get; private set; }

        /// <summary>
        /// Path to the shared asymmetric NPOT sprite sheet fixture (150x75 pixels, 3x1 grid).
        /// </summary>
        public static string SharedNpot150x75Path { get; private set; }

        /// <summary>
        /// Path to the shared prime dimensions sprite fixture (127x127 pixels, single mode).
        /// </summary>
        public static string SharedPrime127Path { get; private set; }

        /// <summary>
        /// Path to the shared small sprite sheet fixture (16x16 pixels, 4x4 grid).
        /// </summary>
        public static string SharedSmall16x16Path { get; private set; }

        /// <summary>
        /// Path to the shared boundary sprite sheet fixture (256x256 pixels, 4x4 grid).
        /// </summary>
        public static string SharedBoundary256Path { get; private set; }

        /// <summary>
        /// Gets the cached 2x2 texture, loading it on first access.
        /// </summary>
        public static Texture2D Shared2x2Texture =>
            _cached2x2Texture ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Shared2x2Path);

        /// <summary>
        /// Gets the cached 4x4 texture, loading it on first access.
        /// </summary>
        public static Texture2D Shared4x4Texture =>
            _cached4x4Texture ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Shared4x4Path);

        /// <summary>
        /// Gets the cached 8x8 texture, loading it on first access.
        /// </summary>
        public static Texture2D Shared8x8Texture =>
            _cached8x8Texture ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Shared8x8Path);

        /// <summary>
        /// Gets the cached single-mode texture, loading it on first access.
        /// </summary>
        public static Texture2D SharedSingleTexture =>
            _cachedSingleTexture ??= AssetDatabase.LoadAssetAtPath<Texture2D>(SharedSingleModePath);

        /// <summary>
        /// Gets the cached wide texture, loading it on first access.
        /// </summary>
        public static Texture2D SharedWideTexture =>
            _cachedWideTexture ??= AssetDatabase.LoadAssetAtPath<Texture2D>(SharedWidePath);

        /// <summary>
        /// Gets the cached tall texture, loading it on first access.
        /// </summary>
        public static Texture2D SharedTallTexture =>
            _cachedTallTexture ??= AssetDatabase.LoadAssetAtPath<Texture2D>(SharedTallPath);

        /// <summary>
        /// Gets the cached odd dimensions texture, loading it on first access.
        /// </summary>
        public static Texture2D SharedOddTexture =>
            _cachedOddTexture ??= AssetDatabase.LoadAssetAtPath<Texture2D>(SharedOddPath);

        /// <summary>
        /// Gets the cached large 512x512 texture, loading it on first access.
        /// </summary>
        public static Texture2D SharedLarge512Texture =>
            _cachedLarge512Texture ??= AssetDatabase.LoadAssetAtPath<Texture2D>(SharedLarge512Path);

        /// <summary>
        /// Gets the cached NPOT 100x200 texture, loading it on first access.
        /// </summary>
        public static Texture2D SharedNpot100x200Texture =>
            _cachedNpot100x200Texture ??= AssetDatabase.LoadAssetAtPath<Texture2D>(
                SharedNpot100x200Path
            );

        /// <summary>
        /// Gets the cached asymmetric NPOT 150x75 texture, loading it on first access.
        /// </summary>
        public static Texture2D SharedNpot150x75Texture =>
            _cachedNpot150x75Texture ??= AssetDatabase.LoadAssetAtPath<Texture2D>(
                SharedNpot150x75Path
            );

        /// <summary>
        /// Gets the cached prime 127x127 texture, loading it on first access.
        /// </summary>
        public static Texture2D SharedPrime127Texture =>
            _cachedPrime127Texture ??= AssetDatabase.LoadAssetAtPath<Texture2D>(SharedPrime127Path);

        /// <summary>
        /// Gets the cached small 16x16 texture, loading it on first access.
        /// </summary>
        public static Texture2D SharedSmall16x16Texture =>
            _cachedSmall16x16Texture ??= AssetDatabase.LoadAssetAtPath<Texture2D>(
                SharedSmall16x16Path
            );

        /// <summary>
        /// Gets the cached boundary 256x256 texture, loading it on first access.
        /// </summary>
        public static Texture2D SharedBoundary256Texture =>
            _cachedBoundary256Texture ??= AssetDatabase.LoadAssetAtPath<Texture2D>(
                SharedBoundary256Path
            );

        /// <summary>
        /// Gets the cached 2x2 texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Shared2x2Importer =>
            _cached2x2Importer ??= AssetImporter.GetAtPath(Shared2x2Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 4x4 texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Shared4x4Importer =>
            _cached4x4Importer ??= AssetImporter.GetAtPath(Shared4x4Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 8x8 texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Shared8x8Importer =>
            _cached8x8Importer ??= AssetImporter.GetAtPath(Shared8x8Path) as TextureImporter;

        /// <summary>
        /// Gets the cached single-mode texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter SharedSingleImporter =>
            _cachedSingleImporter ??=
                AssetImporter.GetAtPath(SharedSingleModePath) as TextureImporter;

        /// <summary>
        /// Gets the cached wide texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter SharedWideImporter =>
            _cachedWideImporter ??= AssetImporter.GetAtPath(SharedWidePath) as TextureImporter;

        /// <summary>
        /// Gets the cached tall texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter SharedTallImporter =>
            _cachedTallImporter ??= AssetImporter.GetAtPath(SharedTallPath) as TextureImporter;

        /// <summary>
        /// Gets the cached odd dimensions texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter SharedOddImporter =>
            _cachedOddImporter ??= AssetImporter.GetAtPath(SharedOddPath) as TextureImporter;

        /// <summary>
        /// Gets the cached large 512x512 texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter SharedLarge512Importer =>
            _cachedLarge512Importer ??=
                AssetImporter.GetAtPath(SharedLarge512Path) as TextureImporter;

        /// <summary>
        /// Gets the cached NPOT 100x200 texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter SharedNpot100x200Importer =>
            _cachedNpot100x200Importer ??=
                AssetImporter.GetAtPath(SharedNpot100x200Path) as TextureImporter;

        /// <summary>
        /// Gets the cached asymmetric NPOT 150x75 texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter SharedNpot150x75Importer =>
            _cachedNpot150x75Importer ??=
                AssetImporter.GetAtPath(SharedNpot150x75Path) as TextureImporter;

        /// <summary>
        /// Gets the cached prime 127x127 texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter SharedPrime127Importer =>
            _cachedPrime127Importer ??=
                AssetImporter.GetAtPath(SharedPrime127Path) as TextureImporter;

        /// <summary>
        /// Gets the cached small 16x16 texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter SharedSmall16x16Importer =>
            _cachedSmall16x16Importer ??=
                AssetImporter.GetAtPath(SharedSmall16x16Path) as TextureImporter;

        /// <summary>
        /// Gets the cached boundary 256x256 texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter SharedBoundary256Importer =>
            _cachedBoundary256Importer ??=
                AssetImporter.GetAtPath(SharedBoundary256Path) as TextureImporter;

        /// <summary>
        /// Gets the cached directory object for the shared fixtures folder.
        /// Used by CreateExtractorWithSharedFixtures to avoid repeated LoadAssetAtPath calls.
        /// </summary>
        public static Object SharedDirectoryObject =>
            _cachedDirectoryObject ??= AssetDatabase.LoadAssetAtPath<Object>(StaticAssetsDir);

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
        /// Gets a value indicating whether fixtures are currently created.
        /// </summary>
        public static bool FixturesCreated
        {
            get
            {
                lock (Lock)
                {
                    return _fixturesCreated;
                }
            }
        }

        /// <summary>
        /// Acquires a reference to the shared fixtures. Verifies and sets up paths if this is the first call.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Call this method in your test fixture's <c>[OneTimeSetUp]</c> method. The method is
        /// thread-safe and uses reference counting to track how many consumers are using the fixtures.
        /// </para>
        /// <para>
        /// Unlike runtime-generated fixtures, these are pre-committed static assets. The method
        /// verifies the assets exist and sets up the path properties.
        /// </para>
        /// </remarks>
        public static void AcquireFixtures()
        {
            lock (Lock)
            {
                _referenceCount++;

                if (_fixturesCreated)
                {
                    // Use cached texture to verify validity instead of calling LoadAssetAtPath
                    if (string.IsNullOrEmpty(Shared2x2Path) || _cached2x2Texture == null)
                    {
                        _fixturesCreated = false;
                    }
                }

                if (!_fixturesCreated)
                {
                    CreateAllFixtures();
                    _fixturesCreated = true;
                }
            }
        }

        /// <summary>
        /// Releases a reference to the shared fixtures. Resets paths when the last reference is released.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Call this method in your test fixture's <c>[OneTimeTearDown]</c> method. The method is
        /// thread-safe and uses reference counting to determine when to reset.
        /// </para>
        /// <para>
        /// Since these are static assets, they are NOT deleted on cleanup - only the path variables
        /// are reset to null.
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
                    _fixturesCreated = false;
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
        /// resets all fixture paths. Only use this when you need to ensure a clean state,
        /// such as during global test teardown.
        /// </para>
        /// <para>
        /// Note: This does NOT delete the static assets - they are pre-committed and remain
        /// in the repository.
        /// </para>
        /// </remarks>
        public static void ForceCleanup()
        {
            lock (Lock)
            {
                CleanupAllFixtures();
                // Also release dynamic fixtures to ensure complete cleanup
                ReleaseDynamicFixtures();
                _referenceCount = 0;
                _fixturesCreated = false;
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
                // Ensure fixtures are acquired first
                if (!_fixturesCreated)
                {
                    AcquireFixtures();
                }

                // Force-load all textures into cache by accessing the properties
                _ = Shared2x2Texture;
                _ = Shared4x4Texture;
                _ = Shared8x8Texture;
                _ = SharedSingleTexture;
                _ = SharedWideTexture;
                _ = SharedTallTexture;
                _ = SharedOddTexture;
                _ = SharedLarge512Texture;
                _ = SharedNpot100x200Texture;
                _ = SharedNpot150x75Texture;
                _ = SharedPrime127Texture;
                _ = SharedSmall16x16Texture;
                _ = SharedBoundary256Texture;

                // Force-load all importers into cache
                _ = Shared2x2Importer;
                _ = Shared4x4Importer;
                _ = Shared8x8Importer;
                _ = SharedSingleImporter;
                _ = SharedWideImporter;
                _ = SharedTallImporter;
                _ = SharedOddImporter;
                _ = SharedLarge512Importer;
                _ = SharedNpot100x200Importer;
                _ = SharedNpot150x75Importer;
                _ = SharedPrime127Importer;
                _ = SharedSmall16x16Importer;
                _ = SharedBoundary256Importer;

                // Force-load directory object
                _ = SharedDirectoryObject;
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
                // Clear cached textures
                _cached2x2Texture = null;
                _cached4x4Texture = null;
                _cached8x8Texture = null;
                _cachedSingleTexture = null;
                _cachedWideTexture = null;
                _cachedTallTexture = null;
                _cachedOddTexture = null;
                _cachedLarge512Texture = null;
                _cachedNpot100x200Texture = null;
                _cachedNpot150x75Texture = null;
                _cachedPrime127Texture = null;
                _cachedSmall16x16Texture = null;
                _cachedBoundary256Texture = null;

                // Clear cached importers
                _cached2x2Importer = null;
                _cached4x4Importer = null;
                _cached8x8Importer = null;
                _cachedSingleImporter = null;
                _cachedWideImporter = null;
                _cachedTallImporter = null;
                _cachedOddImporter = null;
                _cachedLarge512Importer = null;
                _cachedNpot100x200Importer = null;
                _cachedNpot150x75Importer = null;
                _cachedPrime127Importer = null;
                _cachedSmall16x16Importer = null;
                _cachedBoundary256Importer = null;

                // Clear cached directory object
                _cachedDirectoryObject = null;

                // Reset fixture state directly instead of calling ReleaseFixtures() to avoid
                // decrementing reference count (this method is for unconditional cleanup)
                CleanupAllFixtures();
                ReleaseDynamicFixtures();
                _referenceCount = 0;
                _fixturesCreated = false;
            }
        }

        /// <summary>
        /// Gets or creates a dynamically generated sprite sheet fixture with the specified parameters.
        /// Dynamic fixtures are cached and shared across all tests that request the same configuration.
        /// </summary>
        /// <param name="key">A unique key identifying this fixture configuration.</param>
        /// <param name="width">The width of the texture in pixels.</param>
        /// <param name="height">The height of the texture in pixels.</param>
        /// <param name="columns">The number of columns in the sprite grid.</param>
        /// <param name="rows">The number of rows in the sprite grid.</param>
        /// <param name="format">The texture format to use (defaults to RGBA32).</param>
        /// <returns>The dynamic fixture containing the texture, importer, and metadata.</returns>
        /// <remarks>
        /// <para>
        /// This method creates sprite sheet textures on-demand and caches them for reuse.
        /// The fixture is created once and shared across all tests that need the same configuration.
        /// </para>
        /// <para>
        /// The key should be descriptive and unique for the configuration, e.g., "large_2048x2048_4x4"
        /// or "npot_100x200_rgba32".
        /// </para>
        /// </remarks>
        public static DynamicFixture GetOrCreateDynamicFixture(
            string key,
            int width,
            int height,
            int columns,
            int rows,
            TextureFormat format = TextureFormat.RGBA32
        )
        {
            return DynamicFixtures.GetOrAdd(
                key,
                _ => CreateDynamicSpriteSheet(key, width, height, columns, rows, format)
            );
        }

        /// <summary>
        /// Releases all dynamic fixtures and cleans up their assets.
        /// </summary>
        /// <remarks>
        /// This method deletes all dynamically generated assets from the project.
        /// It should be called during test fixture teardown.
        /// </remarks>
        public static void ReleaseDynamicFixtures()
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
                        DynamicFixture
                    > kvp in DynamicFixtures
                )
                {
                    DynamicFixture fixture = kvp.Value;
                    if (
                        !string.IsNullOrEmpty(fixture.AssetPath)
                        && AssetDatabase.LoadAssetAtPath<Object>(fixture.AssetPath) != null
                    )
                    {
                        AssetDatabase.DeleteAsset(fixture.AssetPath);
                    }
                    fixture.Texture = null;
                    fixture.Importer = null;
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

        /// <summary>
        /// Gets the directory path for dynamic fixtures.
        /// </summary>
        /// <returns>The Unity asset path for the dynamic fixtures directory.</returns>
        public static string GetDynamicFixturesDirectory()
        {
            return DynamicAssetsDir;
        }

        /// <summary>
        /// Creates a dynamic sprite sheet with the specified parameters.
        /// </summary>
        private static DynamicFixture CreateDynamicSpriteSheet(
            string key,
            int width,
            int height,
            int columns,
            int rows,
            TextureFormat format
        )
        {
            // Ensure the dynamic assets directory exists
            EnsureDynamicAssetsDirectory();

            int cellWidth = width / columns;
            int cellHeight = height / rows;

            // Create texture with the specified format
            Texture2D texture = new Texture2D(width, height, format, false)
            {
                alphaIsTransparency = true,
            };

            Color[] pixels = new Color[width * height];
            ParallelPixelHelpers.FillGridPixels(pixels, width, height, columns, rows);

            texture.SetPixels(pixels);
            texture.Apply();

            string sanitizedKey = key.Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
            // assetPath: Unity-relative path (e.g., "Assets/Temp/DynamicSpriteFixtures/dynamic_xxx.png")
            // Used for AssetDatabase operations
            string assetPath = $"{DynamicAssetsDir}/dynamic_{sanitizedKey}.png".SanitizePath();
            // fullPath: Absolute file path for File I/O operations
            // Both paths are sanitized to ensure consistent forward slash separators
            string projectRoot = Application.dataPath.Substring(
                0,
                Application.dataPath.Length - "Assets".Length
            );
            string fullPath = Path.Combine(projectRoot, assetPath).SanitizePath();

            // Write the file
            byte[] pngBytes = texture.EncodeToPNG();
            File.WriteAllBytes(fullPath, pngBytes);

            // Destroy the temporary texture
            Object.DestroyImmediate(texture);

            // Import and configure
            AssetDatabase.ImportAsset(assetPath);

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;

                // Set up sprite sheet metadata
                SpriteMetaData[] spritesheet = new SpriteMetaData[columns * rows];
                for (int row = 0; row < rows; row++)
                {
                    for (int col = 0; col < columns; col++)
                    {
                        int index = row * columns + col;
                        spritesheet[index] = new SpriteMetaData
                        {
                            name = $"dynamic_{sanitizedKey}_sprite_{index}",
                            rect = new Rect(
                                col * cellWidth,
                                row * cellHeight,
                                cellWidth,
                                cellHeight
                            ),
                            alignment = (int)SpriteAlignment.Center,
                            pivot = new UnityEngine.Vector2(0.5f, 0.5f),
                            border = UnityEngine.Vector4.zero,
                        };
                    }
                }

                // Use Unity 2D Sprite package API when available
#if UNITY_2D_SPRITE
                UnityEditor.U2D.Sprites.SpriteDataProviderFactories factory = new();
                factory.Init();
                UnityEditor.U2D.Sprites.ISpriteEditorDataProvider dataProvider =
                    factory.GetSpriteEditorDataProviderFromObject(importer);
                dataProvider.InitSpriteEditorDataProvider();

                UnityEditor.U2D.Sprites.SpriteRect[] spriteRects =
                    new UnityEditor.U2D.Sprites.SpriteRect[spritesheet.Length];
                for (int i = 0; i < spritesheet.Length; i++)
                {
                    SpriteMetaData meta = spritesheet[i];
                    spriteRects[i] = new UnityEditor.U2D.Sprites.SpriteRect
                    {
                        name = meta.name,
                        rect = meta.rect,
                        alignment = (SpriteAlignment)meta.alignment,
                        pivot = meta.pivot,
                        border = meta.border,
                        spriteID = GUID.Generate(),
                    };
                }

                dataProvider.SetSpriteRects(spriteRects);
                dataProvider.Apply();
#else
#pragma warning disable CS0618
                importer.spritesheet = spritesheet;
#pragma warning restore CS0618
#endif
                importer.SaveAndReimport();
            }

            // Load the imported texture
            Texture2D loadedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            TextureImporter loadedImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;

            return new DynamicFixture
            {
                AssetPath = assetPath,
                Texture = loadedTexture,
                Importer = loadedImporter,
                Width = width,
                Height = height,
                Columns = columns,
                Rows = rows,
                Format = format,
            };
        }

        /// <summary>
        /// Ensures the dynamic assets directory exists.
        /// </summary>
        private static void EnsureDynamicAssetsDirectory()
        {
            // Clean up any leftover "Temp N" folders before creating directories
            TempFolderCleanupUtility.CleanupTempDuplicates();
            AssetDatabaseBatchHelper.EnsureAssetFolder(DynamicAssetsDir);
        }

        private static void CreateAllFixtures()
        {
            Shared2x2Path = StaticAssetsDir + "/test_2x2_grid.png";
            Shared4x4Path = StaticAssetsDir + "/test_4x4_grid.png";
            Shared8x8Path = StaticAssetsDir + "/test_8x8_grid.png";
            SharedSingleModePath = StaticAssetsDir + "/test_single.png";
            SharedWidePath = StaticAssetsDir + "/test_wide.png";
            SharedTallPath = StaticAssetsDir + "/test_tall.png";
            SharedOddPath = StaticAssetsDir + "/test_odd.png";
            SharedLarge512Path = StaticAssetsDir + "/test_large_512.png";
            SharedNpot100x200Path = StaticAssetsDir + "/test_npot_100x200.png";
            SharedNpot150x75Path = StaticAssetsDir + "/test_npot_150x75.png";
            SharedPrime127Path = StaticAssetsDir + "/test_prime_127.png";
            SharedSmall16x16Path = StaticAssetsDir + "/test_small_16x16.png";
            SharedBoundary256Path = StaticAssetsDir + "/test_boundary_256.png";

            // Verify assets exist and populate the cache simultaneously
            VerifyAndCacheAsset(Shared2x2Path, ref _cached2x2Texture);
            VerifyAndCacheAsset(Shared4x4Path, ref _cached4x4Texture);
            VerifyAndCacheAsset(Shared8x8Path, ref _cached8x8Texture);
            VerifyAndCacheAsset(SharedSingleModePath, ref _cachedSingleTexture);
            VerifyAndCacheAsset(SharedWidePath, ref _cachedWideTexture);
            VerifyAndCacheAsset(SharedTallPath, ref _cachedTallTexture);
            VerifyAndCacheAsset(SharedOddPath, ref _cachedOddTexture);
            VerifyAndCacheAsset(SharedLarge512Path, ref _cachedLarge512Texture);
            VerifyAndCacheAsset(SharedNpot100x200Path, ref _cachedNpot100x200Texture);
            VerifyAndCacheAsset(SharedNpot150x75Path, ref _cachedNpot150x75Texture);
            VerifyAndCacheAsset(SharedPrime127Path, ref _cachedPrime127Texture);
            VerifyAndCacheAsset(SharedSmall16x16Path, ref _cachedSmall16x16Texture);
            VerifyAndCacheAsset(SharedBoundary256Path, ref _cachedBoundary256Texture);
        }

        private static void VerifyAndCacheAsset(string path, ref Texture2D cachedTexture)
        {
            // If already cached, skip the load
            if (cachedTexture != null)
            {
                return;
            }

            cachedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (cachedTexture == null)
            {
                throw new System.InvalidOperationException(
                    $"[SharedSpriteTestFixtures] Static test asset not found: {path}. "
                        + "Ensure the test assets have been properly committed to the repository."
                );
            }
        }

        private static void CleanupAllFixtures()
        {
            // Clear path references
            Shared2x2Path = null;
            Shared4x4Path = null;
            Shared8x8Path = null;
            SharedSingleModePath = null;
            SharedWidePath = null;
            SharedTallPath = null;
            SharedOddPath = null;
            SharedLarge512Path = null;
            SharedNpot100x200Path = null;
            SharedNpot150x75Path = null;
            SharedPrime127Path = null;
            SharedSmall16x16Path = null;
            SharedBoundary256Path = null;

            // Clear cached textures
            _cached2x2Texture = null;
            _cached4x4Texture = null;
            _cached8x8Texture = null;
            _cachedSingleTexture = null;
            _cachedWideTexture = null;
            _cachedTallTexture = null;
            _cachedOddTexture = null;
            _cachedLarge512Texture = null;
            _cachedNpot100x200Texture = null;
            _cachedNpot150x75Texture = null;
            _cachedPrime127Texture = null;
            _cachedSmall16x16Texture = null;
            _cachedBoundary256Texture = null;

            // Clear cached importers
            _cached2x2Importer = null;
            _cached4x4Importer = null;
            _cached8x8Importer = null;
            _cachedSingleImporter = null;
            _cachedWideImporter = null;
            _cachedTallImporter = null;
            _cachedOddImporter = null;
            _cachedLarge512Importer = null;
            _cachedNpot100x200Importer = null;
            _cachedNpot150x75Importer = null;
            _cachedPrime127Importer = null;
            _cachedSmall16x16Importer = null;
            _cachedBoundary256Importer = null;

            // Clear cached directory object
            _cachedDirectoryObject = null;
        }
    }
#endif
}
