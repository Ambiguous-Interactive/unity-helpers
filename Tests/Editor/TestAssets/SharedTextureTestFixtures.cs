// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestAssets
{
#if UNITY_EDITOR
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core.TestUtils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Manages shared texture test fixtures with lazy-loading and reference counting.
    /// Pre-committed static PNG assets are loaded once and cached for all tests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class provides a centralized way to manage texture test fixtures that are shared
    /// across multiple test classes. Instead of generating fixtures at runtime, it uses
    /// pre-committed static PNG assets with proper TextureImporter settings.
    /// </para>
    /// <para>
    /// Thread safety: Public methods that modify shared state (AcquireFixtures, ReleaseFixtures,
    /// PreloadAllAssets, etc.) use locks for correctness. The lazy-loading property getters for
    /// cached textures and importers do NOT use locks; this is acceptable because Unity Editor
    /// APIs like AssetDatabase.LoadAssetAtPath must be called from the main thread anyway.
    /// </para>
    /// </remarks>
    public static class SharedTextureTestFixtures
    {
        private const string StaticAssetsDir =
            "Packages/com.wallstop-studios.unity-helpers/Tests/Editor/TestAssets/Textures";
        private const string DynamicAssetsDir = "Assets/Temp/DynamicTextureFixtures";

        private static readonly object Lock = new();
        private static int _referenceCount;
        private static bool _fixturesVerified;

        // Cached textures - loaded once on first access (base textures)
        private static Texture2D _cached300x100;
        private static Texture2D _cached128x128;
        private static Texture2D _cached256x256;
        private static Texture2D _cached64x64;
        private static Texture2D _cached384x10;
        private static Texture2D _cached512x512;

        // Cached textures - extended set
        private static Texture2D _cached1x1;
        private static Texture2D _cached2x2;
        private static Texture2D _cached32x32;
        private static Texture2D _cached1024x1024;
        private static Texture2D _cached2048x2048;
        private static Texture2D _cached4096x4096;
        private static Texture2D _cached257x64;
        private static Texture2D _cached255x255;
        private static Texture2D _cached513x400;
        private static Texture2D _cached511x511;
        private static Texture2D _cached1x512;
        private static Texture2D _cached512x1;
        private static Texture2D _cached100x200;
        private static Texture2D _cached400x240;
        private static Texture2D _cached450x254;

        // Cached importers - loaded once on first access (base textures)
        private static TextureImporter _cached300x100Importer;
        private static TextureImporter _cached128x128Importer;
        private static TextureImporter _cached256x256Importer;
        private static TextureImporter _cached64x64Importer;
        private static TextureImporter _cached384x10Importer;
        private static TextureImporter _cached512x512Importer;

        // Cached importers - extended set
        private static TextureImporter _cached1x1Importer;
        private static TextureImporter _cached2x2Importer;
        private static TextureImporter _cached32x32Importer;
        private static TextureImporter _cached1024x1024Importer;
        private static TextureImporter _cached2048x2048Importer;
        private static TextureImporter _cached4096x4096Importer;
        private static TextureImporter _cached257x64Importer;
        private static TextureImporter _cached255x255Importer;
        private static TextureImporter _cached513x400Importer;
        private static TextureImporter _cached511x511Importer;
        private static TextureImporter _cached1x512Importer;
        private static TextureImporter _cached512x1Importer;
        private static TextureImporter _cached100x200Importer;
        private static TextureImporter _cached400x240Importer;
        private static TextureImporter _cached450x254Importer;

        // Dynamic fixtures - runtime-generated textures cached by key
        private static readonly ConcurrentDictionary<
            string,
            DynamicTextureFixture
        > DynamicFixtures = new();

        // Dimension-based fixtures - runtime-generated textures cached by dimensions
        private static readonly ConcurrentDictionary<
            string,
            DynamicTextureFixture
        > DimensionFixtures = new();

        /// <summary>
        /// Common power-of-two texture dimensions for general testing.
        /// Includes: 1x1, 2x2, 4x4, 8x8, 16x16, 32x32, 64x64, 128x128, 256x256, 512x512.
        /// </summary>
        public static readonly IReadOnlyList<(int width, int height)> CommonPowerOfTwoDimensions =
            new[]
            {
                (1, 1),
                (2, 2),
                (4, 4),
                (8, 8),
                (16, 16),
                (32, 32),
                (64, 64),
                (128, 128),
                (256, 256),
                (512, 512),
            };

        /// <summary>
        /// Common non-power-of-two texture dimensions for NPOT testing.
        /// Includes: 3x3, 100x200, 150x75, 127x127, 255x255, 257x64, 300x100, 384x10.
        /// </summary>
        public static readonly IReadOnlyList<(
            int width,
            int height
        )> CommonNonPowerOfTwoDimensions = new[]
        {
            (3, 3),
            (100, 100),
            (100, 200),
            (150, 75),
            (127, 127),
            (255, 255),
            (257, 64),
            (300, 100),
            (384, 10),
        };

        /// <summary>
        /// Common extreme aspect ratio texture dimensions for edge case testing.
        /// Includes: 1x512, 512x1, 4x2, 2x4, 1x64, 64x1.
        /// </summary>
        public static readonly IReadOnlyList<(int width, int height)> CommonExtremeAspectRatios =
            new[] { (1, 512), (512, 1), (4, 2), (2, 4), (1, 64), (64, 1) };

        /// <summary>
        /// Common small texture dimensions frequently used in tests.
        /// Includes: 2x2, 3x3, 4x4, 8x8, 16x16, 32x32, 64x64.
        /// </summary>
        public static readonly IReadOnlyList<(int width, int height)> CommonSmallDimensions = new[]
        {
            (2, 2),
            (3, 3),
            (4, 4),
            (8, 8),
            (16, 16),
            (32, 32),
            (64, 64),
        };

        /// <summary>
        /// All commonly used texture dimensions combined for comprehensive test coverage.
        /// Suitable for use with <see cref="PrecreateDimensionsForTests"/>.
        /// </summary>
        public static readonly IReadOnlyList<(int width, int height)> AllCommonDimensions = new[]
        {
            // Small POT
            (1, 1),
            (2, 2),
            (4, 4),
            (8, 8),
            (16, 16),
            (32, 32),
            (64, 64),
            // Medium POT
            (128, 128),
            (256, 256),
            (512, 512),
            // Small NPOT
            (3, 3),
            (100, 100),
            (100, 200),
            (150, 75),
            (127, 127),
            // Edge cases
            (255, 255),
            (257, 64),
            (300, 100),
            (384, 10),
            // Extreme ratios
            (1, 512),
            (512, 1),
            (4, 2),
            (2, 4),
        };

        /// <summary>
        /// Represents a dynamically generated texture fixture with its associated metadata.
        /// </summary>
        public sealed class DynamicTextureFixture
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
        }

        // Path properties for base textures

        /// <summary>
        /// Path to the shared 300x100 magenta texture fixture (non-POT wide texture).
        /// </summary>
        public static string Solid300x100Path => $"{StaticAssetsDir}/solid_300x100_magenta.png";

        /// <summary>
        /// Path to the shared 128x128 white texture fixture (POT square texture).
        /// </summary>
        public static string Solid128x128Path => $"{StaticAssetsDir}/solid_128x128_white.png";

        /// <summary>
        /// Path to the shared 256x256 cyan texture fixture (POT square texture).
        /// </summary>
        public static string Solid256x256Path => $"{StaticAssetsDir}/solid_256x256_cyan.png";

        /// <summary>
        /// Path to the shared 64x64 red texture fixture (small POT texture).
        /// </summary>
        public static string Solid64x64Path => $"{StaticAssetsDir}/solid_64x64_red.png";

        /// <summary>
        /// Path to the shared 384x10 blue texture fixture (wide non-POT texture).
        /// </summary>
        public static string Solid384x10Path => $"{StaticAssetsDir}/solid_384x10_blue.png";

        /// <summary>
        /// Path to the shared 512x512 gray texture fixture (large POT texture).
        /// </summary>
        public static string Solid512x512Path => $"{StaticAssetsDir}/solid_512x512_gray.png";

        // Path properties for extended textures

        /// <summary>
        /// Path to the shared 1x1 texture fixture (minimum size).
        /// </summary>
        public static string Solid1x1Path => $"{StaticAssetsDir}/solid_1x1_black.png";

        /// <summary>
        /// Path to the shared 2x2 texture fixture (minimum POT).
        /// </summary>
        public static string Solid2x2Path => $"{StaticAssetsDir}/solid_2x2_black.png";

        /// <summary>
        /// Path to the shared 32x32 texture fixture (small POT).
        /// </summary>
        public static string Solid32x32Path => $"{StaticAssetsDir}/solid_32x32_black.png";

        /// <summary>
        /// Path to the shared 1024x1024 texture fixture (large POT).
        /// </summary>
        public static string Solid1024x1024Path => $"{StaticAssetsDir}/solid_1024x1024_black.png";

        /// <summary>
        /// Path to the shared 2048x2048 texture fixture (extra large POT).
        /// </summary>
        public static string Solid2048x2048Path => $"{StaticAssetsDir}/solid_2048x2048_black.png";

        /// <summary>
        /// Path to the shared 4096x4096 texture fixture (maximum common POT).
        /// </summary>
        public static string Solid4096x4096Path => $"{StaticAssetsDir}/solid_4096x4096_black.png";

        /// <summary>
        /// Path to the shared 257x64 texture fixture (non-POT wide, one over boundary).
        /// </summary>
        public static string Solid257x64Path => $"{StaticAssetsDir}/solid_257x64_gray.png";

        /// <summary>
        /// Path to the shared 255x255 texture fixture (non-POT square, one under boundary).
        /// </summary>
        public static string Solid255x255Path => $"{StaticAssetsDir}/solid_255x255_gray.png";

        /// <summary>
        /// Path to the shared 513x400 texture fixture (non-POT, one over boundary).
        /// </summary>
        public static string Solid513x400Path => $"{StaticAssetsDir}/solid_513x400_gray.png";

        /// <summary>
        /// Path to the shared 511x511 texture fixture (non-POT square, one under boundary).
        /// </summary>
        public static string Solid511x511Path => $"{StaticAssetsDir}/solid_511x511_gray.png";

        /// <summary>
        /// Path to the shared 1x512 texture fixture (extreme aspect ratio vertical).
        /// </summary>
        public static string Solid1x512Path => $"{StaticAssetsDir}/solid_1x512_yellow.png";

        /// <summary>
        /// Path to the shared 512x1 texture fixture (extreme aspect ratio horizontal).
        /// </summary>
        public static string Solid512x1Path => $"{StaticAssetsDir}/solid_512x1_yellow.png";

        /// <summary>
        /// Path to the shared 100x200 texture fixture (non-POT portrait).
        /// </summary>
        public static string Solid100x200Path => $"{StaticAssetsDir}/solid_100x200_green.png";

        /// <summary>
        /// Path to the shared 400x240 texture fixture (non-POT wide).
        /// </summary>
        public static string Solid400x240Path => $"{StaticAssetsDir}/solid_400x240_blue.png";

        /// <summary>
        /// Path to the shared 450x254 texture fixture (non-POT wide).
        /// </summary>
        public static string Solid450x254Path => $"{StaticAssetsDir}/solid_450x254_blue.png";

        // Lazy-loading properties for base textures

        /// <summary>
        /// Gets the cached 300x100 magenta texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid300x100 =>
            _cached300x100 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid300x100Path);

        /// <summary>
        /// Gets the cached 128x128 white texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid128x128 =>
            _cached128x128 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid128x128Path);

        /// <summary>
        /// Gets the cached 256x256 cyan texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid256x256 =>
            _cached256x256 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid256x256Path);

        /// <summary>
        /// Gets the cached 64x64 red texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid64x64 =>
            _cached64x64 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid64x64Path);

        /// <summary>
        /// Gets the cached 384x10 blue texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid384x10 =>
            _cached384x10 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid384x10Path);

        /// <summary>
        /// Gets the cached 512x512 gray texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid512x512 =>
            _cached512x512 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid512x512Path);

        // Lazy-loading properties for extended textures

        /// <summary>
        /// Gets the cached 1x1 white texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid1x1 =>
            _cached1x1 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid1x1Path);

        /// <summary>
        /// Gets the cached 2x2 white texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid2x2 =>
            _cached2x2 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid2x2Path);

        /// <summary>
        /// Gets the cached 32x32 white texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid32x32 =>
            _cached32x32 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid32x32Path);

        /// <summary>
        /// Gets the cached 1024x1024 white texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid1024x1024 =>
            _cached1024x1024 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid1024x1024Path);

        /// <summary>
        /// Gets the cached 2048x2048 white texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid2048x2048 =>
            _cached2048x2048 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid2048x2048Path);

        /// <summary>
        /// Gets the cached 4096x4096 white texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid4096x4096 =>
            _cached4096x4096 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid4096x4096Path);

        /// <summary>
        /// Gets the cached 257x64 white texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid257x64 =>
            _cached257x64 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid257x64Path);

        /// <summary>
        /// Gets the cached 255x255 white texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid255x255 =>
            _cached255x255 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid255x255Path);

        /// <summary>
        /// Gets the cached 513x400 white texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid513x400 =>
            _cached513x400 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid513x400Path);

        /// <summary>
        /// Gets the cached 511x511 white texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid511x511 =>
            _cached511x511 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid511x511Path);

        /// <summary>
        /// Gets the cached 1x512 white texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid1x512 =>
            _cached1x512 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid1x512Path);

        /// <summary>
        /// Gets the cached 512x1 white texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid512x1 =>
            _cached512x1 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid512x1Path);

        /// <summary>
        /// Gets the cached 100x200 white texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid100x200 =>
            _cached100x200 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid100x200Path);

        /// <summary>
        /// Gets the cached 400x240 white texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid400x240 =>
            _cached400x240 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid400x240Path);

        /// <summary>
        /// Gets the cached 450x254 white texture, loading it on first access.
        /// </summary>
        public static Texture2D Solid450x254 =>
            _cached450x254 ??= AssetDatabase.LoadAssetAtPath<Texture2D>(Solid450x254Path);

        // Lazy-loading properties for base texture importers

        /// <summary>
        /// Gets the cached 300x100 magenta texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid300x100Importer =>
            _cached300x100Importer ??= AssetImporter.GetAtPath(Solid300x100Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 128x128 white texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid128x128Importer =>
            _cached128x128Importer ??= AssetImporter.GetAtPath(Solid128x128Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 256x256 cyan texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid256x256Importer =>
            _cached256x256Importer ??= AssetImporter.GetAtPath(Solid256x256Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 64x64 red texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid64x64Importer =>
            _cached64x64Importer ??= AssetImporter.GetAtPath(Solid64x64Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 384x10 blue texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid384x10Importer =>
            _cached384x10Importer ??= AssetImporter.GetAtPath(Solid384x10Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 512x512 gray texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid512x512Importer =>
            _cached512x512Importer ??= AssetImporter.GetAtPath(Solid512x512Path) as TextureImporter;

        // Lazy-loading properties for extended texture importers

        /// <summary>
        /// Gets the cached 1x1 white texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid1x1Importer =>
            _cached1x1Importer ??= AssetImporter.GetAtPath(Solid1x1Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 2x2 white texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid2x2Importer =>
            _cached2x2Importer ??= AssetImporter.GetAtPath(Solid2x2Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 32x32 white texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid32x32Importer =>
            _cached32x32Importer ??= AssetImporter.GetAtPath(Solid32x32Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 1024x1024 white texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid1024x1024Importer =>
            _cached1024x1024Importer ??=
                AssetImporter.GetAtPath(Solid1024x1024Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 2048x2048 white texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid2048x2048Importer =>
            _cached2048x2048Importer ??=
                AssetImporter.GetAtPath(Solid2048x2048Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 4096x4096 white texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid4096x4096Importer =>
            _cached4096x4096Importer ??=
                AssetImporter.GetAtPath(Solid4096x4096Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 257x64 white texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid257x64Importer =>
            _cached257x64Importer ??= AssetImporter.GetAtPath(Solid257x64Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 255x255 white texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid255x255Importer =>
            _cached255x255Importer ??= AssetImporter.GetAtPath(Solid255x255Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 513x400 white texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid513x400Importer =>
            _cached513x400Importer ??= AssetImporter.GetAtPath(Solid513x400Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 511x511 white texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid511x511Importer =>
            _cached511x511Importer ??= AssetImporter.GetAtPath(Solid511x511Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 1x512 white texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid1x512Importer =>
            _cached1x512Importer ??= AssetImporter.GetAtPath(Solid1x512Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 512x1 white texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid512x1Importer =>
            _cached512x1Importer ??= AssetImporter.GetAtPath(Solid512x1Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 100x200 white texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid100x200Importer =>
            _cached100x200Importer ??= AssetImporter.GetAtPath(Solid100x200Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 400x240 white texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid400x240Importer =>
            _cached400x240Importer ??= AssetImporter.GetAtPath(Solid400x240Path) as TextureImporter;

        /// <summary>
        /// Gets the cached 450x254 white texture importer, loading it on first access.
        /// </summary>
        public static TextureImporter Solid450x254Importer =>
            _cached450x254Importer ??= AssetImporter.GetAtPath(Solid450x254Path) as TextureImporter;

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
                // Force-load all base textures into cache by accessing the properties
                _ = Solid300x100;
                _ = Solid128x128;
                _ = Solid256x256;
                _ = Solid64x64;
                _ = Solid384x10;
                _ = Solid512x512;

                // Force-load all extended textures into cache
                _ = Solid1x1;
                _ = Solid2x2;
                _ = Solid32x32;
                _ = Solid1024x1024;
                _ = Solid2048x2048;
                _ = Solid4096x4096;
                _ = Solid257x64;
                _ = Solid255x255;
                _ = Solid513x400;
                _ = Solid511x511;
                _ = Solid1x512;
                _ = Solid512x1;
                _ = Solid100x200;
                _ = Solid400x240;
                _ = Solid450x254;

                // Force-load all base importers into cache
                _ = Solid300x100Importer;
                _ = Solid128x128Importer;
                _ = Solid256x256Importer;
                _ = Solid64x64Importer;
                _ = Solid384x10Importer;
                _ = Solid512x512Importer;

                // Force-load all extended importers into cache
                _ = Solid1x1Importer;
                _ = Solid2x2Importer;
                _ = Solid32x32Importer;
                _ = Solid1024x1024Importer;
                _ = Solid2048x2048Importer;
                _ = Solid4096x4096Importer;
                _ = Solid257x64Importer;
                _ = Solid255x255Importer;
                _ = Solid513x400Importer;
                _ = Solid511x511Importer;
                _ = Solid1x512Importer;
                _ = Solid512x1Importer;
                _ = Solid100x200Importer;
                _ = Solid400x240Importer;
                _ = Solid450x254Importer;

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
                // Clear cached base textures
                _cached300x100 = null;
                _cached128x128 = null;
                _cached256x256 = null;
                _cached64x64 = null;
                _cached384x10 = null;
                _cached512x512 = null;

                // Clear cached extended textures
                _cached1x1 = null;
                _cached2x2 = null;
                _cached32x32 = null;
                _cached1024x1024 = null;
                _cached2048x2048 = null;
                _cached4096x4096 = null;
                _cached257x64 = null;
                _cached255x255 = null;
                _cached513x400 = null;
                _cached511x511 = null;
                _cached1x512 = null;
                _cached512x1 = null;
                _cached100x200 = null;
                _cached400x240 = null;
                _cached450x254 = null;

                // Clear cached base importers
                _cached300x100Importer = null;
                _cached128x128Importer = null;
                _cached256x256Importer = null;
                _cached64x64Importer = null;
                _cached384x10Importer = null;
                _cached512x512Importer = null;

                // Clear cached extended importers
                _cached1x1Importer = null;
                _cached2x2Importer = null;
                _cached32x32Importer = null;
                _cached1024x1024Importer = null;
                _cached2048x2048Importer = null;
                _cached4096x4096Importer = null;
                _cached257x64Importer = null;
                _cached255x255Importer = null;
                _cached513x400Importer = null;
                _cached511x511Importer = null;
                _cached1x512Importer = null;
                _cached512x1Importer = null;
                _cached100x200Importer = null;
                _cached400x240Importer = null;
                _cached450x254Importer = null;

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
        /// Clones a texture for tests that need to modify importer settings.
        /// The cloned texture is cached and will be cleaned up when fixtures are released.
        /// </summary>
        /// <param name="sourceTexturePath">The path to the source texture to clone.</param>
        /// <param name="testKey">A unique key identifying this test's clone.</param>
        /// <returns>A dynamic texture fixture containing the cloned texture.</returns>
        /// <remarks>
        /// <para>
        /// Use this method when a test needs to modify TextureImporter settings.
        /// The cloned texture is created in Assets/Temp/ and automatically cleaned up.
        /// </para>
        /// </remarks>
        public static DynamicTextureFixture CloneTextureForTest(
            string sourceTexturePath,
            string testKey
        )
        {
            string cacheKey = $"{testKey}_{Path.GetFileName(sourceTexturePath)}";
            return DynamicFixtures.GetOrAdd(
                cacheKey,
                _ =>
                {
                    EnsureDynamicDirectory();
                    string fileName = Path.GetFileName(sourceTexturePath);
                    string destPath = $"{DynamicAssetsDir}/{testKey}_{fileName}".SanitizePath();
                    AssetDatabase.CopyAsset(sourceTexturePath, destPath);
                    AssetDatabaseBatchHelper.RefreshIfNotBatching();
                    return new DynamicTextureFixture
                    {
                        AssetPath = destPath,
                        Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(destPath),
                        Importer = AssetImporter.GetAtPath(destPath) as TextureImporter,
                    };
                }
            );
        }

        /// <summary>
        /// Creates or retrieves a dynamic texture fixture with exact dimensions.
        /// Cached for reuse within the same test run.
        /// </summary>
        /// <param name="width">The width of the texture in pixels.</param>
        /// <param name="height">The height of the texture in pixels.</param>
        /// <param name="testKey">A unique key identifying this test's fixture.</param>
        /// <returns>A dynamic texture fixture with the specified dimensions.</returns>
        public static DynamicTextureFixture GetOrCreateFixture(
            int width,
            int height,
            string testKey
        )
        {
            string cacheKey = $"{width}x{height}_{testKey}";
            return DimensionFixtures.GetOrAdd(
                cacheKey,
                _ => CreateDynamicFixtureForDimension(width, height, testKey)
            );
        }

        /// <summary>
        /// Pre-creates texture fixtures for all specified dimensions in a single batch operation.
        /// Call this from OneTimeSetUp to batch texture creation.
        /// </summary>
        /// <param name="dimensions">The collection of (width, height) tuples to pre-create.</param>
        public static void PrecreateDimensionsForTests(
            IEnumerable<(int width, int height)> dimensions
        )
        {
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                HashSet<(int, int)> unique = new HashSet<(int, int)>(dimensions);
                foreach ((int w, int h) in unique)
                {
                    GetOrCreateFixture(w, h, "precreated");
                }
            }
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

        private static DynamicTextureFixture CreateDynamicFixtureForDimension(
            int width,
            int height,
            string testKey
        )
        {
            EnsureDynamicDirectory();
            string fileName = $"dynamic_{width}x{height}_{testKey}.png";
            string destPath = $"{DynamicAssetsDir}/{fileName}".SanitizePath();

            // Create the texture
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                Color[] pixels = new Color[width * height];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = Color.white;
                }
                texture.SetPixels(pixels);
                texture.Apply();

                byte[] data = texture.EncodeToPNG();
                string fullPath = Path.GetFullPath(
                    destPath.Replace(
                        "Assets/",
                        Application.dataPath.Substring(
                            0,
                            Application.dataPath.Length - "Assets".Length
                        ) + "Assets/"
                    )
                );
                File.WriteAllBytes(fullPath, data);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            return new DynamicTextureFixture
            {
                AssetPath = destPath,
                Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(destPath),
                Importer = AssetImporter.GetAtPath(destPath) as TextureImporter,
            };
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
            bool hasDynamicFixtures = !DynamicFixtures.IsEmpty;
            bool hasDimensionFixtures = !DimensionFixtures.IsEmpty;

            if (!hasDynamicFixtures && !hasDimensionFixtures)
            {
                return;
            }

            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                // Release regular dynamic fixtures
                foreach (KeyValuePair<string, DynamicTextureFixture> kvp in DynamicFixtures)
                {
                    DynamicTextureFixture fixture = kvp.Value;
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

                // Release dimension-based fixtures
                foreach (KeyValuePair<string, DynamicTextureFixture> kvp in DimensionFixtures)
                {
                    DynamicTextureFixture fixture = kvp.Value;
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
            DimensionFixtures.Clear();

            // Clean up "Temp N" duplicates AFTER the batch scope ends.
            // The batch scope's Refresh() may create new duplicates, so we must clean up after.
            TempFolderCleanupUtility.CleanupTempDuplicatesWithRetry();
        }

        private static void VerifyFixtures()
        {
            // Verify base textures
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid300x100Path) != null,
                $"Missing texture fixture: {Solid300x100Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid128x128Path) != null,
                $"Missing texture fixture: {Solid128x128Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid256x256Path) != null,
                $"Missing texture fixture: {Solid256x256Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid64x64Path) != null,
                $"Missing texture fixture: {Solid64x64Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid384x10Path) != null,
                $"Missing texture fixture: {Solid384x10Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid512x512Path) != null,
                $"Missing texture fixture: {Solid512x512Path}"
            );

            // Verify extended textures
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid1x1Path) != null,
                $"Missing texture fixture: {Solid1x1Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid2x2Path) != null,
                $"Missing texture fixture: {Solid2x2Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid32x32Path) != null,
                $"Missing texture fixture: {Solid32x32Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid1024x1024Path) != null,
                $"Missing texture fixture: {Solid1024x1024Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid2048x2048Path) != null,
                $"Missing texture fixture: {Solid2048x2048Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid4096x4096Path) != null,
                $"Missing texture fixture: {Solid4096x4096Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid257x64Path) != null,
                $"Missing texture fixture: {Solid257x64Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid255x255Path) != null,
                $"Missing texture fixture: {Solid255x255Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid513x400Path) != null,
                $"Missing texture fixture: {Solid513x400Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid511x511Path) != null,
                $"Missing texture fixture: {Solid511x511Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid1x512Path) != null,
                $"Missing texture fixture: {Solid1x512Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid512x1Path) != null,
                $"Missing texture fixture: {Solid512x1Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid100x200Path) != null,
                $"Missing texture fixture: {Solid100x200Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid400x240Path) != null,
                $"Missing texture fixture: {Solid400x240Path}"
            );
            Debug.Assert(
                AssetDatabase.LoadAssetAtPath<Texture2D>(Solid450x254Path) != null,
                $"Missing texture fixture: {Solid450x254Path}"
            );
        }
    }
#endif
}
