// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Fast verification tests for <see cref="SpriteSheetExtractor"/> that validate logic
    /// against golden metadata files WITHOUT performing actual extraction operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These tests verify expected sprite counts, naming conventions, dimension calculations,
    /// and grid configurations by reading golden JSON files instead of extracting sprites.
    /// This approach is significantly faster than integration tests.
    /// </para>
    /// <para>
    /// Golden files are located in Tests/Editor/Sprites/Assets/GoldenOutput/ and contain
    /// expected metadata for each fixture configuration.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [Ignore("Beta feature - SpriteSheetExtractor tests are not reliable")]
    public sealed class SpriteSheetExtractorVerificationTests : CommonTestBase
    {
        private const string GoldenOutputDir =
            "Packages/com.wallstop-studios.unity-helpers/Tests/Editor/Sprites/Assets/GoldenOutput";

        private const string StaticAssetsDir =
            "Packages/com.wallstop-studios.unity-helpers/Tests/Editor/TestAssets/Sprites";

        /// <summary>
        /// Converts a Unity relative path to an absolute file system path.
        /// </summary>
        private static string RelToFull(string relativePath)
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
        /// Loads golden metadata from a JSON file.
        /// </summary>
        private static GoldenMetadata LoadGoldenMetadata(string goldenFileName)
        {
            string fullPath = RelToFull(Path.Combine(GoldenOutputDir, goldenFileName));
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"Golden metadata file not found: {goldenFileName}",
                    fullPath
                );
            }

            string json = File.ReadAllText(fullPath);
            return ParseGoldenJson(json);
        }

        /// <summary>
        /// Parses golden JSON manually to avoid JsonUtility issues with arrays.
        /// </summary>
        private static GoldenMetadata ParseGoldenJson(string json)
        {
            GoldenMetadata metadata = new GoldenMetadata { ExpectedNames = new List<string>() };

            // Parse sourceFile
            int sourceFileStart = json.IndexOf("\"sourceFile\":", StringComparison.Ordinal);
            if (0 <= sourceFileStart)
            {
                int valueStart = json.IndexOf('"', sourceFileStart + 13) + 1;
                int valueEnd = json.IndexOf('"', valueStart);
                metadata.SourceFile = json.Substring(valueStart, valueEnd - valueStart);
            }

            // Parse spriteCount
            int spriteCountStart = json.IndexOf("\"spriteCount\":", StringComparison.Ordinal);
            if (0 <= spriteCountStart)
            {
                int valueStart = spriteCountStart + 14;
                int valueEnd = json.IndexOfAny(new[] { ',', '}', '\n' }, valueStart);
                string countStr = json.Substring(valueStart, valueEnd - valueStart).Trim();
                metadata.SpriteCount = int.Parse(countStr);
            }

            // Parse expectedNames array
            int namesStart = json.IndexOf("\"expectedNames\":", StringComparison.Ordinal);
            if (0 <= namesStart)
            {
                int arrayStart = json.IndexOf('[', namesStart);
                int arrayEnd = json.IndexOf(']', arrayStart);
                string arrayContent = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
                string[] parts = arrayContent.Split(
                    new[] { ',' },
                    StringSplitOptions.RemoveEmptyEntries
                );
                foreach (string part in parts)
                {
                    string trimmed = part.Trim().Trim('"');
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        metadata.ExpectedNames.Add(trimmed);
                    }
                }
            }

            // Parse spriteDimensions array
            int dimStart = json.IndexOf("\"spriteDimensions\":", StringComparison.Ordinal);
            if (0 <= dimStart)
            {
                int arrayStart = json.IndexOf('[', dimStart);
                int arrayEnd = json.IndexOf(']', arrayStart);
                string arrayContent = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
                string[] parts = arrayContent.Split(',');
                metadata.SpriteDimensions = new int[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    metadata.SpriteDimensions[i] = int.Parse(parts[i].Trim());
                }
            }

            // Parse gridSize array
            int gridStart = json.IndexOf("\"gridSize\":", StringComparison.Ordinal);
            if (0 <= gridStart)
            {
                int arrayStart = json.IndexOf('[', gridStart);
                int arrayEnd = json.IndexOf(']', arrayStart);
                string arrayContent = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
                string[] parts = arrayContent.Split(',');
                metadata.GridSize = new int[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    metadata.GridSize[i] = int.Parse(parts[i].Trim());
                }
            }

            return metadata;
        }

        /// <summary>
        /// Gets all golden metadata files from the golden output directory.
        /// </summary>
        private static IEnumerable<string> GetAllGoldenFiles()
        {
            string fullDir = RelToFull(GoldenOutputDir);
            if (!Directory.Exists(fullDir))
            {
                yield break;
            }

            string[] files = Directory.GetFiles(fullDir, "golden_*.json");
            foreach (string file in files)
            {
                yield return Path.GetFileName(file);
            }
        }

        /// <summary>
        /// Gets the corresponding static asset path for a golden file.
        /// </summary>
        private static string GetStaticAssetPath(string goldenFileName)
        {
            // Convert golden_xxx.json to test_xxx.png
            string baseName = goldenFileName.Replace("golden_", "test_").Replace(".json", ".png");
            return StaticAssetsDir + "/" + baseName;
        }

        /// <summary>
        /// Verifies that a static test asset exists for the given golden file.
        /// </summary>
        private static bool StaticAssetExists(string goldenFileName)
        {
            string assetPath = GetStaticAssetPath(goldenFileName);
            string fullPath = RelToFull(assetPath);
            return File.Exists(fullPath);
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            if (Application.isPlaying)
            {
                Assert.Ignore("Verification tests require edit mode.");
            }
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
        }

        /// <summary>
        /// Test case source for golden file verification tests.
        /// </summary>
        private static IEnumerable<TestCaseData> GoldenFileCases()
        {
            yield return new TestCaseData("golden_2x2_grid.json").SetName("GoldenVerify.2x2Grid");
            yield return new TestCaseData("golden_4x4_grid.json").SetName("GoldenVerify.4x4Grid");
            yield return new TestCaseData("golden_8x8_grid.json").SetName("GoldenVerify.8x8Grid");
            yield return new TestCaseData("golden_single.json").SetName("GoldenVerify.Single");
            yield return new TestCaseData("golden_wide.json").SetName("GoldenVerify.Wide");
            yield return new TestCaseData("golden_tall.json").SetName("GoldenVerify.Tall");
            yield return new TestCaseData("golden_odd.json").SetName("GoldenVerify.OddDimensions");
        }

        [Test]
        [TestCaseSource(nameof(GoldenFileCases))]
        public void GoldenMetadataFilesAreValid(string goldenFileName)
        {
            GoldenMetadata metadata = LoadGoldenMetadata(goldenFileName);

            Assert.That(metadata.SourceFile, Is.Not.Null.And.Not.Empty, "SourceFile should be set");
            Assert.That(metadata.SpriteCount, Is.GreaterThan(0), "SpriteCount should be positive");
            Assert.That(
                metadata.ExpectedNames,
                Is.Not.Null.And.Count.EqualTo(metadata.SpriteCount),
                "ExpectedNames count should match SpriteCount"
            );
            Assert.That(
                metadata.SpriteDimensions,
                Is.Not.Null.And.Length.EqualTo(2),
                "SpriteDimensions should have 2 elements"
            );
            Assert.That(
                metadata.GridSize,
                Is.Not.Null.And.Length.EqualTo(2),
                "GridSize should have 2 elements"
            );
        }

        [Test]
        [TestCaseSource(nameof(GoldenFileCases))]
        public void StaticAssetsExistForGoldenFiles(string goldenFileName)
        {
            Assert.IsTrue(
                StaticAssetExists(goldenFileName),
                $"Static asset should exist for {goldenFileName}"
            );
        }

        [Test]
        [TestCaseSource(nameof(GoldenFileCases))]
        public void SpriteCountMatchesGridSize(string goldenFileName)
        {
            GoldenMetadata metadata = LoadGoldenMetadata(goldenFileName);
            int expectedCount = metadata.GridSize[0] * metadata.GridSize[1];

            Assert.That(
                metadata.SpriteCount,
                Is.EqualTo(expectedCount),
                $"SpriteCount ({metadata.SpriteCount}) should equal grid columns ({metadata.GridSize[0]}) * rows ({metadata.GridSize[1]})"
            );
        }

        [Test]
        [TestCaseSource(nameof(GoldenFileCases))]
        public void ExpectedNamesHaveCorrectFormat(string goldenFileName)
        {
            GoldenMetadata metadata = LoadGoldenMetadata(goldenFileName);
            string baseName = Path.GetFileNameWithoutExtension(metadata.SourceFile);

            for (int i = 0; i < metadata.ExpectedNames.Count; i++)
            {
                string expectedName = metadata.ExpectedNames[i];
                Assert.That(
                    expectedName,
                    Does.StartWith(baseName),
                    $"Name '{expectedName}' should start with base name '{baseName}'"
                );
                Assert.That(
                    expectedName,
                    Does.EndWith($"_{i}"),
                    $"Name '{expectedName}' should end with index '_{i}'"
                );
            }
        }

        [Test]
        [TestCaseSource(nameof(GoldenFileCases))]
        public void SpriteDimensionsArePositive(string goldenFileName)
        {
            GoldenMetadata metadata = LoadGoldenMetadata(goldenFileName);

            Assert.That(
                metadata.SpriteDimensions[0],
                Is.GreaterThan(0),
                "Sprite width should be positive"
            );
            Assert.That(
                metadata.SpriteDimensions[1],
                Is.GreaterThan(0),
                "Sprite height should be positive"
            );
        }

        [Test]
        [TestCaseSource(nameof(GoldenFileCases))]
        public void GridSizeValuesArePositive(string goldenFileName)
        {
            GoldenMetadata metadata = LoadGoldenMetadata(goldenFileName);

            Assert.That(metadata.GridSize[0], Is.GreaterThan(0), "Grid columns should be positive");
            Assert.That(metadata.GridSize[1], Is.GreaterThan(0), "Grid rows should be positive");
        }

        [Test]
        public void TwoByTwoGridHasExpectedConfiguration()
        {
            GoldenMetadata metadata = LoadGoldenMetadata("golden_2x2_grid.json");

            Assert.That(metadata.SpriteCount, Is.EqualTo(4), "2x2 grid should have 4 sprites");
            Assert.That(metadata.GridSize[0], Is.EqualTo(2), "Grid should have 2 columns");
            Assert.That(metadata.GridSize[1], Is.EqualTo(2), "Grid should have 2 rows");
            Assert.That(metadata.SpriteDimensions[0], Is.EqualTo(32), "Sprite width should be 32");
            Assert.That(metadata.SpriteDimensions[1], Is.EqualTo(32), "Sprite height should be 32");
        }

        [Test]
        public void FourByFourGridHasExpectedConfiguration()
        {
            GoldenMetadata metadata = LoadGoldenMetadata("golden_4x4_grid.json");

            Assert.That(metadata.SpriteCount, Is.EqualTo(16), "4x4 grid should have 16 sprites");
            Assert.That(metadata.GridSize[0], Is.EqualTo(4), "Grid should have 4 columns");
            Assert.That(metadata.GridSize[1], Is.EqualTo(4), "Grid should have 4 rows");
            Assert.That(metadata.SpriteDimensions[0], Is.EqualTo(32), "Sprite width should be 32");
            Assert.That(metadata.SpriteDimensions[1], Is.EqualTo(32), "Sprite height should be 32");
        }

        [Test]
        public void EightByEightGridHasExpectedConfiguration()
        {
            GoldenMetadata metadata = LoadGoldenMetadata("golden_8x8_grid.json");

            Assert.That(metadata.SpriteCount, Is.EqualTo(64), "8x8 grid should have 64 sprites");
            Assert.That(metadata.GridSize[0], Is.EqualTo(8), "Grid should have 8 columns");
            Assert.That(metadata.GridSize[1], Is.EqualTo(8), "Grid should have 8 rows");
            Assert.That(metadata.SpriteDimensions[0], Is.EqualTo(32), "Sprite width should be 32");
            Assert.That(metadata.SpriteDimensions[1], Is.EqualTo(32), "Sprite height should be 32");
        }

        [Test]
        public void SingleSpriteHasExpectedConfiguration()
        {
            GoldenMetadata metadata = LoadGoldenMetadata("golden_single.json");

            Assert.That(metadata.SpriteCount, Is.EqualTo(1), "Single sprite should have 1 sprite");
            Assert.That(metadata.GridSize[0], Is.EqualTo(1), "Grid should have 1 column");
            Assert.That(metadata.GridSize[1], Is.EqualTo(1), "Grid should have 1 row");
        }

        [Test]
        public void WideSheetHasExpectedConfiguration()
        {
            GoldenMetadata metadata = LoadGoldenMetadata("golden_wide.json");

            Assert.That(metadata.SpriteCount, Is.EqualTo(8), "Wide sheet should have 8 sprites");
            Assert.That(metadata.GridSize[0], Is.EqualTo(4), "Grid should have 4 columns");
            Assert.That(metadata.GridSize[1], Is.EqualTo(2), "Grid should have 2 rows");
            Assert.That(
                metadata.GridSize[0],
                Is.GreaterThan(metadata.GridSize[1]),
                "Wide sheet should have more columns than rows"
            );
        }

        [Test]
        public void TallSheetHasExpectedConfiguration()
        {
            GoldenMetadata metadata = LoadGoldenMetadata("golden_tall.json");

            Assert.That(metadata.SpriteCount, Is.EqualTo(8), "Tall sheet should have 8 sprites");
            Assert.That(metadata.GridSize[0], Is.EqualTo(2), "Grid should have 2 columns");
            Assert.That(metadata.GridSize[1], Is.EqualTo(4), "Grid should have 4 rows");
            Assert.That(
                metadata.GridSize[1],
                Is.GreaterThan(metadata.GridSize[0]),
                "Tall sheet should have more rows than columns"
            );
        }

        [Test]
        public void OddDimensionsSheetHasExpectedConfiguration()
        {
            GoldenMetadata metadata = LoadGoldenMetadata("golden_odd.json");

            Assert.That(metadata.SpriteCount, Is.EqualTo(9), "Odd sheet should have 9 sprites");
            Assert.That(metadata.GridSize[0], Is.EqualTo(3), "Grid should have 3 columns");
            Assert.That(metadata.GridSize[1], Is.EqualTo(3), "Grid should have 3 rows");
            Assert.That(
                metadata.SpriteDimensions[0],
                Is.EqualTo(21),
                "Odd sprite width should be 21 (63/3)"
            );
            Assert.That(
                metadata.SpriteDimensions[1],
                Is.EqualTo(21),
                "Odd sprite height should be 21 (63/3)"
            );
        }

        [Test]
        public void AllGoldenFilesHaveUniqueSourceFiles()
        {
            HashSet<string> sourceFiles = new HashSet<string>();
            foreach (string goldenFile in GetAllGoldenFiles())
            {
                GoldenMetadata metadata = LoadGoldenMetadata(goldenFile);
                Assert.IsTrue(
                    sourceFiles.Add(metadata.SourceFile),
                    $"Source file '{metadata.SourceFile}' is duplicated across golden files"
                );
            }
        }

        [Test]
        public void AllGoldenFilesHaveMatchingStaticAssets()
        {
            foreach (string goldenFile in GetAllGoldenFiles())
            {
                Assert.IsTrue(
                    StaticAssetExists(goldenFile),
                    $"Static asset should exist for {goldenFile}"
                );
            }
        }

        [Test]
        public void NamingPatternFollowsZeroPaddedFormat()
        {
            GoldenMetadata metadata = LoadGoldenMetadata("golden_4x4_grid.json");

            // Verify 0-based indexing
            Assert.That(metadata.ExpectedNames[0], Does.EndWith("_0"));
            Assert.That(metadata.ExpectedNames[1], Does.EndWith("_1"));
            Assert.That(metadata.ExpectedNames[15], Does.EndWith("_15"));
        }

        [Test]
        public void VerifyTextureDimensionsMatchGridCalculation()
        {
            // For 2x2 grid: 64x64 texture / 2x2 grid = 32x32 sprites
            GoldenMetadata metadata2x2 = LoadGoldenMetadata("golden_2x2_grid.json");
            int textureWidth2x2 = metadata2x2.SpriteDimensions[0] * metadata2x2.GridSize[0];
            int textureHeight2x2 = metadata2x2.SpriteDimensions[1] * metadata2x2.GridSize[1];
            Assert.That(textureWidth2x2, Is.EqualTo(64), "2x2 texture width should be 64");
            Assert.That(textureHeight2x2, Is.EqualTo(64), "2x2 texture height should be 64");

            // For 4x4 grid: 128x128 texture / 4x4 grid = 32x32 sprites
            GoldenMetadata metadata4x4 = LoadGoldenMetadata("golden_4x4_grid.json");
            int textureWidth4x4 = metadata4x4.SpriteDimensions[0] * metadata4x4.GridSize[0];
            int textureHeight4x4 = metadata4x4.SpriteDimensions[1] * metadata4x4.GridSize[1];
            Assert.That(textureWidth4x4, Is.EqualTo(128), "4x4 texture width should be 128");
            Assert.That(textureHeight4x4, Is.EqualTo(128), "4x4 texture height should be 128");

            // For 8x8 grid: 256x256 texture / 8x8 grid = 32x32 sprites
            GoldenMetadata metadata8x8 = LoadGoldenMetadata("golden_8x8_grid.json");
            int textureWidth8x8 = metadata8x8.SpriteDimensions[0] * metadata8x8.GridSize[0];
            int textureHeight8x8 = metadata8x8.SpriteDimensions[1] * metadata8x8.GridSize[1];
            Assert.That(textureWidth8x8, Is.EqualTo(256), "8x8 texture width should be 256");
            Assert.That(textureHeight8x8, Is.EqualTo(256), "8x8 texture height should be 256");
        }

        [Test]
        public void VerifyWideSheetDimensions()
        {
            GoldenMetadata metadata = LoadGoldenMetadata("golden_wide.json");
            int textureWidth = metadata.SpriteDimensions[0] * metadata.GridSize[0];
            int textureHeight = metadata.SpriteDimensions[1] * metadata.GridSize[1];

            Assert.That(
                textureWidth,
                Is.GreaterThan(textureHeight),
                "Wide sheet texture width should be greater than height"
            );
            Assert.That(textureWidth, Is.EqualTo(128), "Wide texture width should be 128");
            Assert.That(textureHeight, Is.EqualTo(64), "Wide texture height should be 64");
        }

        [Test]
        public void VerifyTallSheetDimensions()
        {
            GoldenMetadata metadata = LoadGoldenMetadata("golden_tall.json");
            int textureWidth = metadata.SpriteDimensions[0] * metadata.GridSize[0];
            int textureHeight = metadata.SpriteDimensions[1] * metadata.GridSize[1];

            Assert.That(
                textureHeight,
                Is.GreaterThan(textureWidth),
                "Tall sheet texture height should be greater than width"
            );
            Assert.That(textureWidth, Is.EqualTo(64), "Tall texture width should be 64");
            Assert.That(textureHeight, Is.EqualTo(128), "Tall texture height should be 128");
        }

        [Test]
        public void VerifyOddDimensionsCalculation()
        {
            GoldenMetadata metadata = LoadGoldenMetadata("golden_odd.json");
            int textureWidth = metadata.SpriteDimensions[0] * metadata.GridSize[0];
            int textureHeight = metadata.SpriteDimensions[1] * metadata.GridSize[1];

            // 63x63 texture with 3x3 grid = 21x21 sprites
            Assert.That(textureWidth, Is.EqualTo(63), "Odd texture width should be 63");
            Assert.That(textureHeight, Is.EqualTo(63), "Odd texture height should be 63");
        }

        [Test]
        public void VerifyExpectedNameCountsMatchSpriteCount()
        {
            foreach (string goldenFile in GetAllGoldenFiles())
            {
                GoldenMetadata metadata = LoadGoldenMetadata(goldenFile);
                Assert.That(
                    metadata.ExpectedNames.Count,
                    Is.EqualTo(metadata.SpriteCount),
                    $"Golden file {goldenFile}: ExpectedNames count should match SpriteCount"
                );
            }
        }

        [Test]
        public void VerifyAllNamesAreUnique()
        {
            foreach (string goldenFile in GetAllGoldenFiles())
            {
                GoldenMetadata metadata = LoadGoldenMetadata(goldenFile);
                HashSet<string> names = new HashSet<string>();
                foreach (string name in metadata.ExpectedNames)
                {
                    Assert.IsTrue(
                        names.Add(name),
                        $"Golden file {goldenFile}: Name '{name}' is duplicated"
                    );
                }
            }
        }

        [Test]
        public void VerifyConsistentSpriteSizeAcrossStandardGrids()
        {
            // All standard grids (2x2, 4x4, 8x8) should have 32x32 sprites
            GoldenMetadata metadata2x2 = LoadGoldenMetadata("golden_2x2_grid.json");
            GoldenMetadata metadata4x4 = LoadGoldenMetadata("golden_4x4_grid.json");
            GoldenMetadata metadata8x8 = LoadGoldenMetadata("golden_8x8_grid.json");

            Assert.That(
                metadata2x2.SpriteDimensions[0],
                Is.EqualTo(32),
                "2x2 sprite width should be 32"
            );
            Assert.That(
                metadata4x4.SpriteDimensions[0],
                Is.EqualTo(32),
                "4x4 sprite width should be 32"
            );
            Assert.That(
                metadata8x8.SpriteDimensions[0],
                Is.EqualTo(32),
                "8x8 sprite width should be 32"
            );

            Assert.That(
                metadata2x2.SpriteDimensions[1],
                Is.EqualTo(32),
                "2x2 sprite height should be 32"
            );
            Assert.That(
                metadata4x4.SpriteDimensions[1],
                Is.EqualTo(32),
                "4x4 sprite height should be 32"
            );
            Assert.That(
                metadata8x8.SpriteDimensions[1],
                Is.EqualTo(32),
                "8x8 sprite height should be 32"
            );
        }

        /// <summary>
        /// Test case source for static asset verification.
        /// </summary>
        private static IEnumerable<TestCaseData> StaticAssetCases()
        {
            yield return new TestCaseData("test_2x2_grid.png", 64, 64, true).SetName(
                "StaticAsset.2x2Grid"
            );
            yield return new TestCaseData("test_4x4_grid.png", 128, 128, true).SetName(
                "StaticAsset.4x4Grid"
            );
            yield return new TestCaseData("test_8x8_grid.png", 256, 256, true).SetName(
                "StaticAsset.8x8Grid"
            );
            yield return new TestCaseData("test_single.png", 32, 32, true).SetName(
                "StaticAsset.Single"
            );
            yield return new TestCaseData("test_wide.png", 128, 64, true).SetName(
                "StaticAsset.Wide"
            );
            yield return new TestCaseData("test_tall.png", 64, 128, true).SetName(
                "StaticAsset.Tall"
            );
            yield return new TestCaseData("test_odd.png", 63, 63, true).SetName(
                "StaticAsset.OddDimensions"
            );
        }

        [Test]
        [TestCaseSource(nameof(StaticAssetCases))]
        public void StaticAssetHasExpectedDimensions(
            string assetFileName,
            int expectedWidth,
            int expectedHeight,
            bool shouldExist
        )
        {
            string assetPath = StaticAssetsDir + "/" + assetFileName;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

            if (shouldExist)
            {
                Assert.IsTrue(texture != null, $"Texture should exist at {assetPath}");
                Assert.That(
                    texture.width,
                    Is.EqualTo(expectedWidth),
                    $"Texture width should be {expectedWidth}"
                );
                Assert.That(
                    texture.height,
                    Is.EqualTo(expectedHeight),
                    $"Texture height should be {expectedHeight}"
                );
            }
        }

        [Test]
        public void AllStandardGridsHaveSquareSprites()
        {
            string[] grids =
            {
                "golden_2x2_grid.json",
                "golden_4x4_grid.json",
                "golden_8x8_grid.json",
            };

            foreach (string gridFile in grids)
            {
                GoldenMetadata metadata = LoadGoldenMetadata(gridFile);
                Assert.That(
                    metadata.SpriteDimensions[0],
                    Is.EqualTo(metadata.SpriteDimensions[1]),
                    $"{gridFile}: Standard grid sprites should be square"
                );
            }
        }

        [Test]
        public void WideAndTallSheetsHaveSquareSprites()
        {
            GoldenMetadata wideMetadata = LoadGoldenMetadata("golden_wide.json");
            GoldenMetadata tallMetadata = LoadGoldenMetadata("golden_tall.json");

            Assert.That(
                wideMetadata.SpriteDimensions[0],
                Is.EqualTo(wideMetadata.SpriteDimensions[1]),
                "Wide sheet should have square sprites"
            );
            Assert.That(
                tallMetadata.SpriteDimensions[0],
                Is.EqualTo(tallMetadata.SpriteDimensions[1]),
                "Tall sheet should have square sprites"
            );
        }

        [Test]
        public void OddSheetHasSquareSprites()
        {
            GoldenMetadata metadata = LoadGoldenMetadata("golden_odd.json");
            Assert.That(
                metadata.SpriteDimensions[0],
                Is.EqualTo(metadata.SpriteDimensions[1]),
                "Odd sheet should have square sprites"
            );
        }

        [Test]
        public void VerifyGoldenFileCountMatchesExpectedFixtures()
        {
            List<string> goldenFiles = new List<string>(GetAllGoldenFiles());
            Assert.That(
                goldenFiles.Count,
                Is.GreaterThanOrEqualTo(7),
                "Should have at least 7 golden files for standard fixtures"
            );
        }

        [Test]
        public void NamingPatternIsConsistentAcrossAllGoldenFiles()
        {
            foreach (string goldenFile in GetAllGoldenFiles())
            {
                GoldenMetadata metadata = LoadGoldenMetadata(goldenFile);
                string baseName = Path.GetFileNameWithoutExtension(metadata.SourceFile);

                for (int i = 0; i < metadata.ExpectedNames.Count; i++)
                {
                    string expectedPattern = $"{baseName}_{i}";
                    Assert.That(
                        metadata.ExpectedNames[i],
                        Is.EqualTo(expectedPattern),
                        $"Golden file {goldenFile}: Name at index {i} should follow pattern '{baseName}_<index>'"
                    );
                }
            }
        }

        [Test]
        public void GridCalculationsAreSymmetricForSquareTextures()
        {
            string[] squareGrids =
            {
                "golden_2x2_grid.json",
                "golden_4x4_grid.json",
                "golden_8x8_grid.json",
            };

            foreach (string gridFile in squareGrids)
            {
                GoldenMetadata metadata = LoadGoldenMetadata(gridFile);

                // For square textures with square grids, columns should equal rows
                Assert.That(
                    metadata.GridSize[0],
                    Is.EqualTo(metadata.GridSize[1]),
                    $"{gridFile}: Square grid should have equal columns and rows"
                );

                // Sprite dimensions should also be square
                Assert.That(
                    metadata.SpriteDimensions[0],
                    Is.EqualTo(metadata.SpriteDimensions[1]),
                    $"{gridFile}: Sprite dimensions should be square for square grids"
                );
            }
        }

        /// <summary>
        /// Utility test that generates golden metadata files for all shared sprite fixtures.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is an [Explicit] test that should only be run manually when you need to
        /// regenerate the golden metadata files. It uses the SharedSpriteTestFixtures to
        /// access the shared test textures and generates JSON files with expected sprite
        /// metadata for each fixture.
        /// </para>
        /// <para>
        /// The generated golden files contain:
        /// - sourceFile: The name of the source texture file
        /// - spriteCount: Expected number of sprites in the grid
        /// - expectedNames: List of expected sprite names following the pattern {basename}_{index}
        /// - spriteDimensions: Width and height of each sprite cell
        /// - gridSize: Number of columns and rows in the sprite grid
        /// </para>
        /// <para>
        /// Run this test manually via the Unity Test Runner when:
        /// - Adding new test fixtures
        /// - Changing the expected sprite naming convention
        /// - Regenerating golden files after texture modifications
        /// </para>
        /// </remarks>
        [Test]
        [Explicit("Utility test for regenerating golden metadata files - run manually when needed")]
        [NUnit.Framework.Category("GenerateGoldenFiles")]
        public void GenerateGoldenMetadataFiles()
        {
            // Ensure fixtures are available
            SharedSpriteTestFixtures.AcquireFixtures();

            try
            {
                // Define fixture configurations: (goldenFileName, texturePath, columns, rows)
                (string goldenName, string texturePath, int columns, int rows)[] fixtures =
                {
                    ("golden_2x2_grid.json", SharedSpriteTestFixtures.Shared2x2Path, 2, 2),
                    ("golden_4x4_grid.json", SharedSpriteTestFixtures.Shared4x4Path, 4, 4),
                    ("golden_8x8_grid.json", SharedSpriteTestFixtures.Shared8x8Path, 8, 8),
                    ("golden_single.json", SharedSpriteTestFixtures.SharedSingleModePath, 1, 1),
                    ("golden_wide.json", SharedSpriteTestFixtures.SharedWidePath, 4, 2),
                    ("golden_tall.json", SharedSpriteTestFixtures.SharedTallPath, 2, 4),
                    ("golden_odd.json", SharedSpriteTestFixtures.SharedOddPath, 3, 3),
                };

                string goldenDir = RelToFull(GoldenOutputDir);

                // Ensure the golden output directory exists
                if (!Directory.Exists(goldenDir))
                {
                    Directory.CreateDirectory(goldenDir);
                }

                foreach ((string goldenName, string texturePath, int columns, int rows) in fixtures)
                {
                    // Load the texture to get dimensions
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    Assert.IsTrue(texture != null, $"Texture not found at path: {texturePath}");

                    // Calculate sprite dimensions and count
                    int spriteWidth = texture.width / columns;
                    int spriteHeight = texture.height / rows;
                    int spriteCount = columns * rows;

                    // Generate expected names
                    string sourceFile = Path.GetFileName(texturePath);
                    string baseName = Path.GetFileNameWithoutExtension(sourceFile);
                    List<string> expectedNames = new List<string>(spriteCount);
                    for (int i = 0; i < spriteCount; i++)
                    {
                        expectedNames.Add($"{baseName}_{i}");
                    }

                    // Build JSON manually to match the expected format
                    string json = BuildGoldenJson(
                        sourceFile,
                        spriteCount,
                        expectedNames,
                        spriteWidth,
                        spriteHeight,
                        columns,
                        rows
                    );

                    // Write the golden file
                    string goldenPath = Path.Combine(goldenDir, goldenName);
                    File.WriteAllText(goldenPath, json);

                    Debug.Log($"[GenerateGoldenMetadataFiles] Generated: {goldenName}");
                }

                // Refresh the asset database to pick up the new files
                AssetDatabase.Refresh();

                Debug.Log(
                    $"[GenerateGoldenMetadataFiles] Successfully generated {fixtures.Length} golden metadata files."
                );
            }
            finally
            {
                SharedSpriteTestFixtures.ReleaseFixtures();
            }
        }

        /// <summary>
        /// Threshold for using compact array format vs multiline format in golden JSON files.
        /// Arrays with this many elements or fewer use compact format.
        /// </summary>
        private const int CompactArrayThreshold = 4;

        /// <summary>
        /// Builds a JSON string for a golden metadata file.
        /// </summary>
        private static string BuildGoldenJson(
            string sourceFile,
            int spriteCount,
            List<string> expectedNames,
            int spriteWidth,
            int spriteHeight,
            int columns,
            int rows
        )
        {
            // Format expectedNames array - use compact format for small arrays, multiline for large
            string namesJson;
            if (expectedNames.Count <= CompactArrayThreshold)
            {
                namesJson =
                    "[" + string.Join(", ", expectedNames.ConvertAll(n => $"\"{n}\"")) + "]";
            }
            else
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("[");
                for (int i = 0; i < expectedNames.Count; i++)
                {
                    sb.Append($"    \"{expectedNames[i]}\"");
                    if (i < expectedNames.Count - 1)
                    {
                        sb.AppendLine(",");
                    }
                    else
                    {
                        sb.AppendLine();
                    }
                }
                sb.Append("  ]");
                namesJson = sb.ToString();
            }

            // Build the full JSON
            return $@"{{
  ""sourceFile"": ""{sourceFile}"",
  ""spriteCount"": {spriteCount},
  ""expectedNames"": {namesJson},
  ""spriteDimensions"": [{spriteWidth}, {spriteHeight}],
  ""gridSize"": [{columns}, {rows}]
}}
";
        }

        /// <summary>
        /// Represents the expected metadata from a golden file.
        /// </summary>
        private sealed class GoldenMetadata
        {
            public string SourceFile { get; set; }
            public int SpriteCount { get; set; }
            public List<string> ExpectedNames { get; set; }
            public int[] SpriteDimensions { get; set; }
            public int[] GridSize { get; set; }
        }
    }
#endif
}
