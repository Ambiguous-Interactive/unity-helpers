// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Helper methods for parallel pixel array operations in tests.
    /// Uses threshold-based parallelization to avoid overhead on small arrays.
    /// </summary>
    internal static class ParallelPixelHelpers
    {
        /// <summary>
        /// Minimum pixel count for parallelization (128x128 = 16,384).
        /// Below this threshold, sequential processing is faster.
        /// </summary>
        internal const int ParallelThreshold = 16384;

        /// <summary>
        /// Fills a pixel array with grid-based colors.
        /// Uses parallel processing for arrays >= threshold.
        /// </summary>
        internal static void FillGridPixels(
            Color[] pixels,
            int width,
            int height,
            int gridColumns,
            int gridRows
        )
        {
            int cellWidth = width / gridColumns;
            int cellHeight = height / gridRows;
            int totalCells = gridColumns * gridRows;

            if (ParallelThreshold <= pixels.Length)
            {
                // Parallelize by row for cache-friendly access
                Parallel.For(
                    0,
                    height,
                    y =>
                    {
                        int row = y / cellHeight;
                        int rowStart = y * width;
                        for (int x = 0; x < width; x++)
                        {
                            int col = x / cellWidth;
                            int spriteIndex = row * gridColumns + col;
                            float hue = (float)spriteIndex / totalCells;
                            pixels[rowStart + x] = Color.HSVToRGB(hue, 0.8f, 0.9f);
                        }
                    }
                );
            }
            else
            {
                // Sequential for small arrays
                for (int row = 0; row < gridRows; row++)
                {
                    for (int col = 0; col < gridColumns; col++)
                    {
                        int spriteIndex = row * gridColumns + col;
                        float hue = (float)spriteIndex / totalCells;
                        Color cellColor = Color.HSVToRGB(hue, 0.8f, 0.9f);
                        int startX = col * cellWidth;
                        int startY = row * cellHeight;
                        for (int y = startY; y < startY + cellHeight; y++)
                        {
                            int rowStart = y * width;
                            for (int x = startX; x < startX + cellWidth; x++)
                            {
                                pixels[rowStart + x] = cellColor;
                            }
                        }
                    }
                }
            }
        }

        // NOTE: For solid color fills, ALWAYS use Array.Fill() directly.
        // Array.Fill is highly optimized by the runtime and faster than any loop.
    }

    /// <summary>
    /// Shared base class for <see cref="SpriteSheetExtractor"/> integration tests that require
    /// AssetDatabase operations and shared sprite sheet fixtures.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This base class provides common functionality for creating and managing test sprite sheets,
    /// including shared fixtures that persist across all tests in a fixture and unique assets
    /// for tests that require isolation.
    /// </para>
    /// <para>
    /// Subclasses should define their own Root, OutputDir, and SharedDir constants,
    /// then call the base methods to create and manage fixtures.
    /// </para>
    /// </remarks>
    public abstract class SpriteSheetExtractorTestBase : CommonTestBase
    {
        /// <summary>
        /// Gets the root directory path for this test fixture's assets.
        /// Subclasses must override to provide a unique directory.
        /// </summary>
        protected abstract string Root { get; }

        /// <summary>
        /// Gets the output directory path for extracted sprites.
        /// Subclasses must override to provide a unique directory.
        /// </summary>
        protected abstract string OutputDir { get; }

        /// <summary>
        /// Gets the directory path for shared sprite sheet fixtures.
        /// Subclasses must override to provide a unique directory.
        /// </summary>
        protected abstract string SharedDir { get; }

        /// <summary>
        /// Path to the shared 2x2 sprite sheet fixture.
        /// </summary>
        protected string Shared2x2Path { get; set; }

        /// <summary>
        /// Path to the shared 4x4 sprite sheet fixture.
        /// </summary>
        protected string Shared4x4Path { get; set; }

        /// <summary>
        /// Path to the shared 8x8 sprite sheet fixture.
        /// </summary>
        protected string Shared8x8Path { get; set; }

        /// <summary>
        /// Path to the shared single-mode sprite fixture.
        /// </summary>
        protected string SharedSingleModePath { get; set; }

        /// <summary>
        /// Path to the shared wide aspect ratio sprite sheet fixture.
        /// </summary>
        protected string SharedWidePath { get; set; }

        /// <summary>
        /// Path to the shared tall aspect ratio sprite sheet fixture.
        /// </summary>
        protected string SharedTallPath { get; set; }

        /// <summary>
        /// Path to the shared odd dimensions sprite sheet fixture.
        /// </summary>
        protected string SharedOddPath { get; set; }

        /// <summary>
        /// Path to the shared large 512x512 sprite sheet fixture (16x16 grid, 256 sprites).
        /// </summary>
        protected string SharedLarge512Path { get; set; }

        /// <summary>
        /// Path to the shared NPOT 100x200 sprite sheet fixture (2x4 grid).
        /// </summary>
        protected string SharedNpot100x200Path { get; set; }

        /// <summary>
        /// Path to the shared asymmetric NPOT 150x75 sprite sheet fixture (3x1 grid).
        /// </summary>
        protected string SharedNpot150x75Path { get; set; }

        /// <summary>
        /// Path to the shared prime 127x127 sprite fixture (single mode).
        /// </summary>
        protected string SharedPrime127Path { get; set; }

        /// <summary>
        /// Path to the shared small 16x16 sprite sheet fixture (4x4 grid).
        /// </summary>
        protected string SharedSmall16x16Path { get; set; }

        /// <summary>
        /// Path to the shared boundary 256x256 sprite sheet fixture (4x4 grid).
        /// </summary>
        protected string SharedBoundary256Path { get; set; }

        /// <summary>
        /// Indicates whether shared fixtures have been created.
        /// </summary>
        protected bool SharedFixturesCreated { get; set; }

        // Cached output directory object to avoid repeated LoadAssetAtPath calls
        private Object _cachedOutputDirectory;
        private string _cachedOutputDirectoryPath;

        /// <summary>
        /// Gets or sets the cached output directory object. Automatically invalidates when OutputDir changes.
        /// </summary>
        protected Object GetCachedOutputDirectory()
        {
            // Invalidate cache if the output directory path has changed
            if (_cachedOutputDirectoryPath != OutputDir)
            {
                _cachedOutputDirectory = null;
                _cachedOutputDirectoryPath = OutputDir;
            }

            return _cachedOutputDirectory ??= AssetDatabase.LoadAssetAtPath<Object>(OutputDir);
        }

        /// <summary>
        /// Converts a Unity relative path to an absolute file system path.
        /// </summary>
        /// <param name="relativePath">The Unity relative path (e.g., "Assets/...").</param>
        /// <returns>The absolute file system path.</returns>
        protected static string RelToFull(string relativePath)
        {
            return Path.Combine(
                    Application.dataPath.Substring(
                        0,
                        Application.dataPath.Length - "Assets".Length
                    ),
                    relativePath
                )
                .SanitizePath();
        }

        /// <summary>
        /// Creates a sprite sheet texture with the specified grid configuration.
        /// Each cell is filled with a distinct color based on HSV hue.
        /// </summary>
        /// <param name="name">The name of the sprite sheet file (without extension).</param>
        /// <param name="width">The width of the texture in pixels.</param>
        /// <param name="height">The height of the texture in pixels.</param>
        /// <param name="gridColumns">The number of columns in the grid.</param>
        /// <param name="gridRows">The number of rows in the grid.</param>
        /// <param name="mode">The sprite import mode.</param>
        /// <returns>The Unity relative path to the created asset.</returns>
        protected string CreateSharedSpriteSheet(
            string name,
            int width,
            int height,
            int gridColumns,
            int gridRows,
            SpriteImportMode mode
        )
        {
            int cellWidth = width / gridColumns;
            int cellHeight = height / gridRows;

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false) // UNH-SUPPRESS: Temporary texture for file writing, destroyed immediately after
            {
                alphaIsTransparency = true,
            };

            Color[] pixels = new Color[width * height];
            ParallelPixelHelpers.FillGridPixels(pixels, width, height, gridColumns, gridRows);

            texture.SetPixels(pixels);
            texture.Apply();

            string path = Path.Combine(SharedDir, name + ".png").SanitizePath();
            string fullPath = RelToFull(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());

            Object.DestroyImmediate(texture); // UNH-SUPPRESS: Cleanup temporary texture used only for file writing

            AssetDatabase.ImportAsset(path);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return path;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = mode;
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            if (mode == SpriteImportMode.Multiple)
            {
                SpriteMetaData[] spritesheet = new SpriteMetaData[gridColumns * gridRows];
                for (int row = 0; row < gridRows; row++)
                {
                    for (int col = 0; col < gridColumns; col++)
                    {
                        int index = row * gridColumns + col;
                        spritesheet[index] = new SpriteMetaData
                        {
                            name = $"{name}_sprite_{index}",
                            rect = new Rect(
                                col * cellWidth,
                                row * cellHeight,
                                cellWidth,
                                cellHeight
                            ),
                            alignment = (int)SpriteAlignment.Center,
                            pivot = new Vector2(0.5f, 0.5f),
                            border = Vector4.zero,
                        };
                    }
                }
                SetSpriteSheet(importer, spritesheet);
            }

            // Note: SetSpriteSheet already calls SaveAndReimport internally
            return path;
        }

        /// <summary>
        /// Creates a single-mode sprite with a solid color.
        /// </summary>
        /// <param name="name">The name of the sprite file (without extension).</param>
        /// <param name="width">The width of the texture in pixels.</param>
        /// <param name="height">The height of the texture in pixels.</param>
        /// <param name="color">The fill color for the sprite.</param>
        /// <returns>The Unity relative path to the created asset.</returns>
        protected string CreateSharedSingleModeSprite(
            string name,
            int width,
            int height,
            Color color
        )
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false); // UNH-SUPPRESS: Temporary texture for file writing, destroyed immediately after
            Color[] pixels = new Color[width * height];
            Array.Fill(pixels, color);

            texture.SetPixels(pixels);
            texture.Apply();

            string path = Path.Combine(SharedDir, name + ".png").SanitizePath();
            File.WriteAllBytes(RelToFull(path), texture.EncodeToPNG());

            Object.DestroyImmediate(texture); // UNH-SUPPRESS: Cleanup temporary texture used only for file writing

            AssetDatabase.ImportAsset(path);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return path;
            }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            return path;
        }

        /// <summary>
        /// Sets the sprite sheet metadata on a texture importer.
        /// Uses the Unity 2D Sprite package API when available.
        /// </summary>
        /// <param name="importer">The texture importer to configure.</param>
        /// <param name="spritesheet">The sprite metadata array.</param>
        protected static void SetSpriteSheet(TextureImporter importer, SpriteMetaData[] spritesheet)
        {
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
            importer.SaveAndReimport();
#else
#pragma warning disable CS0618
            importer.spritesheet = spritesheet;
#pragma warning restore CS0618
            importer.SaveAndReimport();
#endif
        }

        /// <summary>
        /// Finds a sprite sheet entry by its asset path.
        /// </summary>
        /// <param name="extractor">The extractor containing discovered sheets.</param>
        /// <param name="assetPath">The asset path to search for.</param>
        /// <returns>The matching entry, or null if not found.</returns>
        protected SpriteSheetExtractor.SpriteSheetEntry FindEntryByPath(
            SpriteSheetExtractor extractor,
            string assetPath
        )
        {
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                if (extractor._discoveredSheets[i]._assetPath == assetPath)
                {
                    return extractor._discoveredSheets[i];
                }
            }
            return null;
        }

        /// <summary>
        /// Deselects all entries and their sprites in the extractor.
        /// Use this before <see cref="SpriteSheetExtractor.SelectAll"/> to ensure test isolation
        /// when working with shared fixtures that contain multiple sprite sheets.
        /// </summary>
        /// <param name="extractor">The extractor containing discovered sheets.</param>
        protected static void DeselectAllEntries(SpriteSheetExtractor extractor)
        {
            if (extractor == null || extractor._discoveredSheets == null)
            {
                return;
            }

            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                SpriteSheetExtractor.SpriteSheetEntry entry = extractor._discoveredSheets[i];
                entry._isSelected = false;
                extractor.SelectNone(entry);
            }
        }

        /// <summary>
        /// Creates an extractor configured to use the Root directory for input.
        /// </summary>
        /// <returns>A tracked SpriteSheetExtractor instance.</returns>
        protected SpriteSheetExtractor CreateExtractor()
        {
            SpriteSheetExtractor extractor = Track(
                ScriptableObject.CreateInstance<SpriteSheetExtractor>()
            );
            extractor._inputDirectories = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };
            extractor._outputDirectory = GetCachedOutputDirectory();
            extractor._overwriteExisting = true;
            return extractor;
        }

        /// <summary>
        /// Creates an extractor configured to use the shared fixtures directory.
        /// </summary>
        /// <returns>A tracked SpriteSheetExtractor instance.</returns>
        protected SpriteSheetExtractor CreateExtractorWithSharedFixtures()
        {
            SpriteSheetExtractor extractor = Track(
                ScriptableObject.CreateInstance<SpriteSheetExtractor>()
            );
            extractor._inputDirectories = new List<Object>
            {
                // Use cached directory object to avoid repeated LoadAssetAtPath calls
                SharedSpriteTestFixtures.SharedDirectoryObject,
            };
            extractor._outputDirectory = GetCachedOutputDirectory();
            extractor._overwriteExisting = true;
            return extractor;
        }

        /// <summary>
        /// Creates a sprite sheet texture with the specified grid configuration in the Root directory.
        /// Each cell is filled with a distinct color based on HSV hue.
        /// </summary>
        /// <param name="name">The name of the sprite sheet file (without extension).</param>
        /// <param name="width">The width of the texture in pixels.</param>
        /// <param name="height">The height of the texture in pixels.</param>
        /// <param name="gridColumns">The number of columns in the grid.</param>
        /// <param name="gridRows">The number of rows in the grid.</param>
        /// <param name="directory">Optional directory path. If null, uses Root.</param>
        /// <returns>The Unity relative path to the created asset.</returns>
        protected string CreateSpriteSheet(
            string name,
            int width,
            int height,
            int gridColumns,
            int gridRows,
            string directory = null
        )
        {
            string targetDir = directory ?? Root;
            int cellWidth = width / gridColumns;
            int cellHeight = height / gridRows;

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false) // UNH-SUPPRESS: Temporary texture for file writing, destroyed immediately after
            {
                alphaIsTransparency = true,
            };

            Color[] pixels = new Color[width * height];
            ParallelPixelHelpers.FillGridPixels(pixels, width, height, gridColumns, gridRows);

            texture.SetPixels(pixels);
            texture.Apply();

            string path = Path.Combine(targetDir, name + ".png").SanitizePath();
            string fullPath = RelToFull(path);
            string pathDirectory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(pathDirectory) && !Directory.Exists(pathDirectory))
            {
                Directory.CreateDirectory(pathDirectory);
            }
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            TrackAssetPath(path);

            Object.DestroyImmediate(texture); // UNH-SUPPRESS: Cleanup temporary texture used only for file writing

            AssetDatabase.ImportAsset(path);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return path;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            SpriteMetaData[] spritesheet = new SpriteMetaData[gridColumns * gridRows];
            for (int row = 0; row < gridRows; row++)
            {
                for (int col = 0; col < gridColumns; col++)
                {
                    int index = row * gridColumns + col;
                    spritesheet[index] = new SpriteMetaData
                    {
                        name = $"{name}_sprite_{index}",
                        rect = new Rect(col * cellWidth, row * cellHeight, cellWidth, cellHeight),
                        alignment = (int)SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                        border = Vector4.zero,
                    };
                }
            }
            SetSpriteSheet(importer, spritesheet);
            // Note: SetSpriteSheet already calls SaveAndReimport internally

            // Force synchronous refresh to ensure the texture is fully loaded with the correct
            // format after reimport. Without this, subsequent texture reads may get stale data
            // or incorrect format information (e.g., DXT1 instead of the configured format).
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            return path;
        }

        /// <summary>
        /// Fills a texture with a solid color.
        /// </summary>
        /// <param name="texture">The texture to fill.</param>
        /// <param name="color">The color to fill the texture with.</param>
        protected static void FillTexture(Texture2D texture, Color color)
        {
            if (texture == null)
            {
                return;
            }

            Color[] pixels = new Color[texture.width * texture.height];
            Array.Fill(pixels, color);
            texture.SetPixels(pixels);
            texture.Apply();
        }

        /// <summary>
        /// Creates all standard shared fixtures for testing.
        /// Call this from <see cref="CommonOneTimeSetUp"/> after ensuring directories exist.
        /// </summary>
        protected void CreateAllSharedFixtures()
        {
            if (SharedFixturesCreated)
            {
                if (
                    string.IsNullOrEmpty(Shared2x2Path)
                    || AssetDatabase.LoadAssetAtPath<Texture2D>(Shared2x2Path) == null
                )
                {
                    SharedFixturesCreated = false;
                }
            }

            if (!SharedFixturesCreated)
            {
                Shared2x2Path = CreateSharedSpriteSheet(
                    "shared_2x2",
                    64,
                    64,
                    2,
                    2,
                    SpriteImportMode.Multiple
                );
                Shared4x4Path = CreateSharedSpriteSheet(
                    "shared_4x4",
                    128,
                    128,
                    4,
                    4,
                    SpriteImportMode.Multiple
                );
                Shared8x8Path = CreateSharedSpriteSheet(
                    "shared_8x8",
                    256,
                    256,
                    8,
                    8,
                    SpriteImportMode.Multiple
                );
                SharedSingleModePath = CreateSharedSingleModeSprite(
                    "shared_single",
                    32,
                    32,
                    Color.red
                );
                SharedWidePath = CreateSharedSpriteSheet(
                    "shared_wide",
                    128,
                    64,
                    4,
                    2,
                    SpriteImportMode.Multiple
                );
                SharedTallPath = CreateSharedSpriteSheet(
                    "shared_tall",
                    64,
                    128,
                    2,
                    4,
                    SpriteImportMode.Multiple
                );
                SharedOddPath = CreateSharedSpriteSheet(
                    "shared_odd",
                    63,
                    63,
                    3,
                    3,
                    SpriteImportMode.Multiple
                );
                SharedFixturesCreated = true;
            }
        }

        /// <summary>
        /// Creates a unique output directory for test isolation.
        /// </summary>
        /// <param name="name">The name of the subdirectory.</param>
        /// <returns>The Unity relative path to the created directory.</returns>
        protected string CreateUniqueOutputDirectory(string name)
        {
            string uniqueOutputDir = $"{Root}/{name}";
            EnsureFolder(uniqueOutputDir);
            TrackFolder(uniqueOutputDir);
            return uniqueOutputDir;
        }

        // Fixture-level shared output directory (created once per fixture)
        private string _fixtureOutputDir;
        private int _fixtureOutputSubdirCounter;

        /// <summary>
        /// Gets or creates a shared output directory for the entire test fixture.
        /// This avoids the overhead of creating and tracking many individual directories.
        /// </summary>
        /// <returns>The Unity relative path to the fixture's shared output directory.</returns>
        /// <remarks>
        /// <para>
        /// This directory is created once per fixture and reused across all tests.
        /// Tests that need unique output should use <see cref="GetFixtureSubdirectory"/> instead.
        /// </para>
        /// </remarks>
        protected string GetFixtureOutputDirectory()
        {
            if (string.IsNullOrEmpty(_fixtureOutputDir))
            {
                _fixtureOutputDir = $"{Root}/Output";
                EnsureFolder(_fixtureOutputDir);
                TrackFolder(_fixtureOutputDir);
            }
            return _fixtureOutputDir;
        }

        /// <summary>
        /// Gets a unique subdirectory within the fixture's shared output directory.
        /// This is more efficient than <see cref="CreateUniqueOutputDirectory"/> because
        /// it doesn't create separate folder hierarchies for each test.
        /// </summary>
        /// <param name="suffix">Optional suffix for the subdirectory name.</param>
        /// <returns>The Unity relative path to the subdirectory.</returns>
        protected string GetFixtureSubdirectory(string suffix = null)
        {
            string basePath = GetFixtureOutputDirectory();
            string subdir = string.IsNullOrEmpty(suffix)
                ? $"{basePath}/sub_{_fixtureOutputSubdirCounter++}"
                : $"{basePath}/{suffix}_{_fixtureOutputSubdirCounter++}";
            EnsureFolder(subdir);
            // Note: We don't need to track subdirectories separately because
            // they will be deleted when the parent directory is deleted
            return subdir;
        }

        /// <summary>
        /// Clears the fixture output directory, removing all extracted sprites.
        /// Call this between tests if you need a clean output directory.
        /// </summary>
        protected void ClearFixtureOutputDirectory()
        {
            if (
                string.IsNullOrEmpty(_fixtureOutputDir)
                || !AssetDatabase.IsValidFolder(_fixtureOutputDir)
            )
            {
                return;
            }

            string fullPath = RelToFull(_fixtureOutputDir);
            if (Directory.Exists(fullPath))
            {
                string[] files = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories);
                using (AssetDatabaseBatchHelper.BeginBatch())
                {
                    foreach (string file in files)
                    {
                        // Convert to Unity path format
                        string relativePath = file.Substring(
                            Application.dataPath.Length - "Assets".Length
                        );
                        relativePath = relativePath.Replace('\\', '/');
                        AssetDatabase.DeleteAsset(relativePath);
                    }
                }
            }
        }

        /// <summary>
        /// Sets up a texture as a sprite sheet with the specified grid.
        /// </summary>
        /// <param name="path">The asset path of the texture.</param>
        /// <param name="gridColumns">The number of columns in the grid.</param>
        /// <param name="gridRows">The number of rows in the grid.</param>
        protected void SetupSpriteImporter(string path, int gridColumns, int gridRows)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                return;
            }

            int cellWidth = texture.width / gridColumns;
            int cellHeight = texture.height / gridRows;

            SpriteMetaData[] spritesheet = new SpriteMetaData[gridColumns * gridRows];
            for (int row = 0; row < gridRows; row++)
            {
                for (int col = 0; col < gridColumns; col++)
                {
                    int index = row * gridColumns + col;
                    spritesheet[index] = new SpriteMetaData
                    {
                        name = $"{Path.GetFileNameWithoutExtension(path)}_sprite_{index}",
                        rect = new Rect(
                            col * cellWidth,
                            (gridRows - 1 - row) * cellHeight, // Unity uses bottom-left origin
                            cellWidth,
                            cellHeight
                        ),
                        alignment = (int)SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                        border = Vector4.zero,
                    };
                }
            }
            SetSpriteSheet(importer, spritesheet);
            // Note: SetSpriteSheet already calls SaveAndReimport internally

            // Force synchronous refresh to ensure the texture is fully loaded with the correct
            // format after reimport. Without this, subsequent texture reads may get stale data
            // or incorrect format information (e.g., DXT1 instead of the configured format).
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>
        /// Sets up sprite sheet metadata on an existing TextureImporter.
        /// </summary>
        /// <param name="importer">The texture importer to configure.</param>
        /// <param name="gridColumns">The number of columns in the grid.</param>
        /// <param name="gridRows">The number of rows in the grid.</param>
        protected void SetupSpritesheet(TextureImporter importer, int gridColumns, int gridRows)
        {
            if (importer == null)
            {
                return;
            }

            string path = importer.assetPath;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                return;
            }

            int cellWidth = texture.width / gridColumns;
            int cellHeight = texture.height / gridRows;

            SpriteMetaData[] spritesheet = new SpriteMetaData[gridColumns * gridRows];
            for (int row = 0; row < gridRows; row++)
            {
                for (int col = 0; col < gridColumns; col++)
                {
                    int index = row * gridColumns + col;
                    spritesheet[index] = new SpriteMetaData
                    {
                        name = $"{Path.GetFileNameWithoutExtension(path)}_sprite_{index}",
                        rect = new Rect(
                            col * cellWidth,
                            (gridRows - 1 - row) * cellHeight,
                            cellWidth,
                            cellHeight
                        ),
                        alignment = (int)SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                        border = Vector4.zero,
                    };
                }
            }
            SetSpriteSheet(importer, spritesheet);
        }

        /// <summary>
        /// Creates multiple sprite sheets in a single batch operation, significantly reducing
        /// AssetDatabase overhead compared to creating them one at a time.
        /// </summary>
        /// <param name="configs">The configurations for the sprite sheets to create.</param>
        /// <param name="directory">Optional directory path. If null, uses Root.</param>
        /// <returns>An array of Unity relative paths to the created assets.</returns>
        /// <remarks>
        /// <para>
        /// This method batches all AssetDatabase operations together, which dramatically improves
        /// performance when creating multiple sprite sheets. It follows a three-phase approach:
        /// </para>
        /// <list type="bullet">
        /// <item><description>Phase 1: Write all PNG files to disk (no imports).</description></item>
        /// <item><description>Phase 2: Batch import all files.</description></item>
        /// <item><description>Phase 3: Batch configure all importers.</description></item>
        /// </list>
        /// </remarks>
        protected string[] CreateSpriteSheetsBatched(
            SpriteSheetConfig[] configs,
            string directory = null
        )
        {
            if (configs == null || configs.Length == 0)
            {
                return System.Array.Empty<string>();
            }

            string targetDir = directory ?? Root;
            string[] paths = new string[configs.Length];

            // Phase 1: Write all PNG files (no imports)
            for (int i = 0; i < configs.Length; i++)
            {
                SpriteSheetConfig config = configs[i];

                Texture2D texture = Track(
                    new Texture2D(config.Width, config.Height, TextureFormat.RGBA32, false)
                    {
                        alphaIsTransparency = true,
                    }
                );

                Color[] pixels = new Color[config.Width * config.Height];
                ParallelPixelHelpers.FillGridPixels(
                    pixels,
                    config.Width,
                    config.Height,
                    config.GridColumns,
                    config.GridRows
                );

                texture.SetPixels(pixels);
                texture.Apply();

                string path = Path.Combine(targetDir, config.Name + ".png").SanitizePath();
                string fullPath = RelToFull(path);
                string pathDirectory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(pathDirectory) && !Directory.Exists(pathDirectory))
                {
                    Directory.CreateDirectory(pathDirectory);
                }
                File.WriteAllBytes(fullPath, texture.EncodeToPNG());
                TrackAssetPath(path);
                paths[i] = path;
            }

            // Phase 2: Batch import all files
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    AssetDatabase.ImportAsset(paths[i]);
                }
            }

            // Phase 3: Batch configure all importers
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    SpriteSheetConfig config = configs[i];
                    int cellWidth = config.Width / config.GridColumns;
                    int cellHeight = config.Height / config.GridRows;

                    TextureImporter importer = AssetImporter.GetAtPath(paths[i]) as TextureImporter;
                    if (importer == null)
                    {
                        continue;
                    }

                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Multiple;
                    importer.isReadable = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;

                    SpriteMetaData[] spritesheet = new SpriteMetaData[
                        config.GridColumns * config.GridRows
                    ];
                    for (int row = 0; row < config.GridRows; row++)
                    {
                        for (int col = 0; col < config.GridColumns; col++)
                        {
                            int index = row * config.GridColumns + col;
                            spritesheet[index] = new SpriteMetaData
                            {
                                name = $"{config.Name}_sprite_{index}",
                                rect = new Rect(
                                    col * cellWidth,
                                    row * cellHeight,
                                    cellWidth,
                                    cellHeight
                                ),
                                alignment = (int)SpriteAlignment.Center,
                                pivot = new Vector2(0.5f, 0.5f),
                                border = Vector4.zero,
                            };
                        }
                    }
                    SetSpriteSheet(importer, spritesheet);
                }
            }

            return paths;
        }

        /// <summary>
        /// Cleans up all shared fixtures.
        /// Call this from <see cref="OneTimeTearDown"/>.
        /// </summary>
        protected void CleanupAllSharedFixtures()
        {
            if (!string.IsNullOrEmpty(Shared2x2Path))
            {
                AssetDatabase.DeleteAsset(Shared2x2Path);
                Shared2x2Path = null;
            }
            if (!string.IsNullOrEmpty(Shared4x4Path))
            {
                AssetDatabase.DeleteAsset(Shared4x4Path);
                Shared4x4Path = null;
            }
            if (!string.IsNullOrEmpty(Shared8x8Path))
            {
                AssetDatabase.DeleteAsset(Shared8x8Path);
                Shared8x8Path = null;
            }
            if (!string.IsNullOrEmpty(SharedSingleModePath))
            {
                AssetDatabase.DeleteAsset(SharedSingleModePath);
                SharedSingleModePath = null;
            }
            if (!string.IsNullOrEmpty(SharedWidePath))
            {
                AssetDatabase.DeleteAsset(SharedWidePath);
                SharedWidePath = null;
            }
            if (!string.IsNullOrEmpty(SharedTallPath))
            {
                AssetDatabase.DeleteAsset(SharedTallPath);
                SharedTallPath = null;
            }
            if (!string.IsNullOrEmpty(SharedOddPath))
            {
                AssetDatabase.DeleteAsset(SharedOddPath);
                SharedOddPath = null;
            }
            SharedFixturesCreated = false;

            if (AssetDatabase.IsValidFolder(SharedDir))
            {
                AssetDatabase.DeleteAsset(SharedDir);
            }
        }

        /// <summary>
        /// Represents the configuration for a sprite sheet to be created in a batch operation.
        /// </summary>
        public readonly struct SpriteSheetConfig
        {
            /// <summary>
            /// The name of the sprite sheet file (without extension).
            /// </summary>
            public readonly string Name;

            /// <summary>
            /// The width of the texture in pixels.
            /// </summary>
            public readonly int Width;

            /// <summary>
            /// The height of the texture in pixels.
            /// </summary>
            public readonly int Height;

            /// <summary>
            /// The number of columns in the sprite grid.
            /// </summary>
            public readonly int GridColumns;

            /// <summary>
            /// The number of rows in the sprite grid.
            /// </summary>
            public readonly int GridRows;

            /// <summary>
            /// Creates a new sprite sheet configuration.
            /// </summary>
            public SpriteSheetConfig(
                string name,
                int width,
                int height,
                int gridColumns,
                int gridRows
            )
            {
                Name = name;
                Width = width;
                Height = height;
                GridColumns = gridColumns;
                GridRows = gridRows;
            }
        }
    }
#endif
}
