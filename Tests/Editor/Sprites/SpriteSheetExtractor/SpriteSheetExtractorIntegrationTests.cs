// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using Object = UnityEngine.Object;
    using PivotMode = UnityHelpers.Editor.Sprites.PivotMode;

    /// <summary>
    /// Core integration tests for <see cref="SpriteSheetExtractor"/> that perform actual
    /// sprite extraction operations. These tests verify end-to-end functionality that cannot
    /// be validated through golden file metadata alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This test fixture is kept minimal to reduce test execution time. Each test verifies
    /// a specific integration scenario that requires actual file I/O and AssetDatabase operations.
    /// </para>
    /// <para>
    /// For verification of expected sprite counts, naming conventions, and grid configurations,
    /// see <see cref="SpriteSheetExtractorVerificationTests"/>.
    /// </para>
    /// <para>
    /// <strong>Performance optimization:</strong> This fixture uses fixture-level AssetDatabase
    /// batching to minimize overhead. All tests share a common output directory with test-specific
    /// subdirectories, and a single batch scope wraps all test operations.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    [Ignore("Beta feature - SpriteSheetExtractor tests are not reliable")]
    public sealed class SpriteSheetExtractorIntegrationTests : SpriteSheetExtractorTestBase
    {
        private const string RootPath = "Assets/Temp/SpriteSheetExtractorIntegrationTests";

        /// <summary>
        /// Shared output directory created once per fixture for all tests.
        /// </summary>
        private string _sharedOutputDir;

        /// <summary>
        /// Per-test output subdirectory within the shared output directory.
        /// </summary>
        private string _testOutputDir;

        /// <summary>
        /// Tracks whether fixture-level batching has been started.
        /// </summary>
        private IDisposable _fixtureBatchScope;

        /// <inheritdoc />
        protected override string Root => RootPath;

        /// <inheritdoc />
        protected override string OutputDir => RootPath + "/Output";

        /// <inheritdoc />
        protected override string SharedDir => SharedSpriteTestFixtures.GetSharedDirectory();

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            if (Application.isPlaying)
            {
                Assert.Ignore("AssetDatabase access requires edit mode.");
            }

            // The test name gives isolation without AssetDatabase overhead.
            string testName = TestContext.CurrentContext.Test.Name;
            // Sanitize test name for file system (remove invalid chars, limit length)
            testName = SanitizeTestName(testName);
            _testOutputDir = Path.Combine(_sharedOutputDir, testName).SanitizePath();

            // Create directory within the batch scope using the helper method
            EnsureDirectoryWithinBatch(_testOutputDir);

            SpriteSheetExtractor.SuppressUserPrompts = true;
        }

        /// <summary>
        /// Creates a directory within a batch scope and ensures it is registered with the AssetDatabase.
        /// </summary>
        /// <remarks>
        /// <para>
        /// When fixture-level batching is active, AssetDatabase.Refresh() is ineffective because
        /// StartAssetEditing() defers all refresh operations until StopAssetEditing() is called.
        /// This method pauses the batch temporarily to allow the directory to be registered.
        /// </para>
        /// </remarks>
        /// <param name="assetPath">The Unity relative path (e.g., "Assets/...") to the directory to create.</param>
        /// <exception cref="AssertionException">Thrown if the directory cannot be verified via AssetDatabase.</exception>
        private void EnsureDirectoryWithinBatch(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                throw new ArgumentNullException(nameof(assetPath));
            }

            /*
                Pause fixture-level batching so the AssetDatabase can register the new folder.
                Without the pause, AssetDatabase.Refresh() is ineffective: StartAssetEditing()
                defers every refresh until StopAssetEditing().
            */
            using (AssetDatabaseBatchHelper.PauseBatch())
            {
                // Create directory on disk if it doesn't exist
                string fullPath = RelToFull(assetPath);
                if (!Directory.Exists(fullPath))
                {
                    Directory.CreateDirectory(fullPath);
                }

                // A synchronous refresh works here only because the batch scope is paused above.
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                // IsValidFolder is more semantically correct for a folder than LoadAssetAtPath<Object>.
                bool isValidFolder = AssetDatabase.IsValidFolder(assetPath);
                bool existsOnDisk = Directory.Exists(fullPath);

                if (!isValidFolder)
                {
                    StringBuilder diagnostics = new StringBuilder();
                    diagnostics.AppendLine($"Directory verification failed for: {assetPath}");
                    diagnostics.AppendLine($"  - Directory.Exists(fullPath): {existsOnDisk}");
                    diagnostics.AppendLine($"  - Full path: {fullPath}");
                    diagnostics.AppendLine($"  - AssetDatabase.IsValidFolder: {isValidFolder}");
                    diagnostics.AppendLine(
                        $"  - IsCurrentlyBatching: {AssetDatabaseBatchHelper.IsCurrentlyBatching}"
                    );

                    Assert.Fail(diagnostics.ToString());
                }
            }
        }

        /// <summary>
        /// Sanitizes a test name for use as a directory name.
        /// </summary>
        private static string SanitizeTestName(string testName)
        {
            if (string.IsNullOrEmpty(testName))
            {
                return "UnknownTest";
            }

            // Replace invalid path characters
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                testName = testName.Replace(c, '_');
            }

            // Also replace parentheses and quotes which can cause issues
            testName = testName
                .Replace('(', '_')
                .Replace(')', '_')
                .Replace('"', '_')
                .Replace('\'', '_');

            // Limit length to avoid path issues
            if (50 < testName.Length)
            {
                testName = testName.Substring(0, 50);
            }

            return testName;
        }

        /// <summary>
        /// Converts an absolute filesystem path to a Unity relative path.
        /// </summary>
        /// <param name="fullPath">The absolute filesystem path.</param>
        /// <returns>The Unity relative path (e.g., "Assets/...").</returns>
        private static string FullToRel(string fullPath)
        {
            string projectRoot = Application.dataPath.Substring(
                0,
                Application.dataPath.Length - "Assets".Length
            );
            string relativePath = fullPath;
            if (fullPath.StartsWith(projectRoot))
            {
                relativePath = fullPath.Substring(projectRoot.Length);
            }
            return relativePath.Replace('\\', '/');
        }

        private static bool AssetPathExistsInDatabase(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            return !string.IsNullOrEmpty(guid);
        }

        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            DeferAssetCleanupToOneTimeTearDown = true;

            // Created BEFORE fixture-level batching starts, so AssetDatabase registers it and can load it.
            _sharedOutputDir =
                "Assets/TestOutput_Integration_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            EnsureFolderStatic(Root);
            EnsureFolderStatic(_sharedOutputDir);
            TrackFolder(_sharedOutputDir);

            // Without this, AssetDatabase does not register the directory until the batch scope ends.
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // Acquire shared fixtures (these handle their own refresh)
            SharedSpriteTestFixtures.AcquireFixtures();
            Shared2x2Path = SharedSpriteTestFixtures.Shared2x2Path;
            Shared4x4Path = SharedSpriteTestFixtures.Shared4x4Path;
            Shared8x8Path = SharedSpriteTestFixtures.Shared8x8Path;
            SharedSingleModePath = SharedSpriteTestFixtures.SharedSingleModePath;
            SharedWidePath = SharedSpriteTestFixtures.SharedWidePath;
            SharedTallPath = SharedSpriteTestFixtures.SharedTallPath;
            SharedOddPath = SharedSpriteTestFixtures.SharedOddPath;
            SharedLarge512Path = SharedSpriteTestFixtures.SharedLarge512Path;
            SharedNpot100x200Path = SharedSpriteTestFixtures.SharedNpot100x200Path;
            SharedNpot150x75Path = SharedSpriteTestFixtures.SharedNpot150x75Path;
            SharedPrime127Path = SharedSpriteTestFixtures.SharedPrime127Path;
            SharedSmall16x16Path = SharedSpriteTestFixtures.SharedSmall16x16Path;
            SharedBoundary256Path = SharedSpriteTestFixtures.SharedBoundary256Path;
            SharedFixturesCreated = true;

            // After all setup: tests can then defer AssetDatabase work with every directory already registered.
            _fixtureBatchScope = AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: true);
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            SharedSpriteTestFixtures.ReleaseFixtures();

            // End fixture-level batching before cleanup
            if (_fixtureBatchScope != null)
            {
                _fixtureBatchScope.Dispose();
                _fixtureBatchScope = null;
            }

            // Cleanup shared output directory
            if (
                !string.IsNullOrEmpty(_sharedOutputDir)
                && AssetDatabase.IsValidFolder(_sharedOutputDir)
            )
            {
                AssetDatabase.DeleteAsset(_sharedOutputDir);
            }
            _sharedOutputDir = null;

            CleanupDeferredAssetsAndFolders();
            base.OneTimeTearDown();
        }

        /// <summary>
        /// Tests that basic grid extraction creates the expected number of output files.
        /// This is the core end-to-end test for extraction functionality.
        /// </summary>
        [Test]
        public void BasicGridExtractionCreatesExpectedFiles()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._outputDirectory = AssetDatabase.LoadAssetAtPath<Object>(_testOutputDir);
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared2x2Path);
            Assert.IsTrue(entry != null, "Should find shared_2x2 entry");

            DeselectAllEntries(extractor);
            entry._isSelected = true;
            extractor.SelectAll(entry);
            extractor.ExtractSelectedSprites();

            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string[] pngFiles = Directory.GetFiles(
                RelToFull(_testOutputDir),
                "*.png",
                SearchOption.TopDirectoryOnly
            );

            Assert.That(pngFiles.Length, Is.EqualTo(4), "Should extract 4 sprites from 2x2 grid");
        }

        /// <summary>
        /// Tests that extracted sprites have the correct pixel dimensions.
        /// This verifies the actual extraction math is correct.
        /// </summary>
        [Test]
        public void ExtractedSpritesHaveCorrectPixelDimensions()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._outputDirectory = AssetDatabase.LoadAssetAtPath<Object>(_testOutputDir);
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 2;
            extractor._gridRows = 2;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared2x2Path);
            Assert.IsTrue(entry != null, "Should find shared_2x2 entry");

            DeselectAllEntries(extractor);
            entry._isSelected = true;
            extractor.SelectAll(entry);
            extractor.ExtractSelectedSprites();

            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string[] pngFiles = Directory.GetFiles(
                RelToFull(_testOutputDir),
                "*.png",
                SearchOption.TopDirectoryOnly
            );
            Assert.That(pngFiles.Length, Is.GreaterThan(0), "Should extract at least one PNG file");

            // Wrap verification in ExecuteWithImmediateImport to ensure asset is imported
            ExecuteWithImmediateImport(() =>
            {
                // Convert filesystem path to Unity asset path for loading
                string firstExtractedPath = FullToRel(pngFiles[0]);
                Texture2D extracted = AssetDatabase.LoadAssetAtPath<Texture2D>(firstExtractedPath);
                Assert.IsTrue(extracted != null, "Should load extracted texture");

                // 64x64 texture / 2x2 grid = 32x32 sprites
                Assert.That(extracted.width, Is.EqualTo(32), "Extracted width should be 32");
                Assert.That(extracted.height, Is.EqualTo(32), "Extracted height should be 32");
            });
        }

        /// <summary>
        /// Tests that pixel content is correctly transferred during extraction.
        /// This verifies the actual pixel copy operation works.
        /// </summary>
        [Test]
        public void PixelContentIsCorrectlyTransferred()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._outputDirectory = AssetDatabase.LoadAssetAtPath<Object>(_testOutputDir);
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 2;
            extractor._gridRows = 2;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared2x2Path);
            Assert.IsTrue(entry != null, "Should find shared_2x2 entry");

            DeselectAllEntries(extractor);
            entry._isSelected = true;
            extractor.SelectAll(entry);
            extractor.ExtractSelectedSprites();

            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string[] pngFiles = Directory.GetFiles(
                RelToFull(_testOutputDir),
                "*.png",
                SearchOption.TopDirectoryOnly
            );
            Assert.That(pngFiles.Length, Is.GreaterThan(0), "Should extract at least one PNG file");

            // Wrap verification in ExecuteWithImmediateImport to ensure asset is imported
            ExecuteWithImmediateImport(() =>
            {
                // Convert filesystem path to Unity asset path for loading
                string firstExtractedPath = FullToRel(pngFiles[0]);
                Texture2D extracted = AssetDatabase.LoadAssetAtPath<Texture2D>(firstExtractedPath);
                Assert.IsTrue(extracted != null, "Should load extracted texture");

                Color[] pixels = extracted.GetPixels();
                bool hasNonZeroPixels = false;
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (0.01f < pixels[i].a)
                    {
                        hasNonZeroPixels = true;
                        break;
                    }
                }

                Assert.IsTrue(
                    hasNonZeroPixels,
                    "Extracted texture should have non-transparent pixels"
                );
            });
        }

        /// <summary>
        /// Tests that overwrite mode replaces existing files.
        /// This verifies the overwrite logic works correctly.
        /// </summary>
        [Test]
        public void OverwriteModeReplacesExistingFiles()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._outputDirectory = AssetDatabase.LoadAssetAtPath<Object>(_testOutputDir);
            extractor._overwriteExisting = true;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared2x2Path);
            Assert.IsTrue(entry != null, "Should find shared_2x2 entry");

            DeselectAllEntries(extractor);
            entry._isSelected = true;
            if (0 < entry._sprites.Count)
            {
                entry._sprites[0]._isSelected = true;
            }

            // First extraction
            extractor.ExtractSelectedSprites();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string[] firstExtractionFiles = Directory.GetFiles(
                RelToFull(_testOutputDir),
                "*.png",
                SearchOption.TopDirectoryOnly
            );
            Assert.That(
                firstExtractionFiles.Length,
                Is.EqualTo(1),
                "First extraction should create exactly 1 file"
            );

            // Read original file content for comparison
            byte[] originalContent = File.ReadAllBytes(firstExtractionFiles[0]);

            // Modify the file to verify overwrite actually occurs
            byte[] modifiedContent = new byte[originalContent.Length];
            Array.Copy(originalContent, modifiedContent, originalContent.Length);
            // Flip some bytes to make content detectably different
            if (100 < modifiedContent.Length)
            {
                modifiedContent[100] = (byte)(modifiedContent[100] ^ 0xFF);
            }
            File.WriteAllBytes(firstExtractionFiles[0], modifiedContent);

            // Verify file was modified
            byte[] contentAfterModification = File.ReadAllBytes(firstExtractionFiles[0]);
            Assert.That(
                contentAfterModification,
                Is.EqualTo(modifiedContent),
                "File should contain modified content before second extraction"
            );

            // Second extraction (should overwrite with original sprite content)
            Assert.DoesNotThrow(
                () => extractor.ExtractSelectedSprites(),
                "Second extraction should not throw"
            );
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string[] secondExtractionFiles = Directory.GetFiles(
                RelToFull(_testOutputDir),
                "*.png",
                SearchOption.TopDirectoryOnly
            );
            Assert.That(
                secondExtractionFiles.Length,
                Is.EqualTo(1),
                "Second extraction should still have exactly 1 file"
            );

            // Verify file was overwritten (content should match original, not modified)
            byte[] finalContent = File.ReadAllBytes(secondExtractionFiles[0]);
            Assert.That(
                finalContent,
                Is.EqualTo(originalContent),
                "File content should be restored to original after overwrite"
            );
        }

        /// <summary>
        /// Tests that dry run mode does not create any files.
        /// This verifies the dry run flag works correctly.
        /// </summary>
        [Test]
        public void DryRunDoesNotCreateFiles()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._outputDirectory = AssetDatabase.LoadAssetAtPath<Object>(_testOutputDir);
            extractor._dryRun = true;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared2x2Path);
            Assert.IsTrue(entry != null, "Should find shared_2x2 entry");

            DeselectAllEntries(extractor);
            entry._isSelected = true;
            extractor.SelectAll(entry);

            extractor.ExtractSelectedSprites();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string[] extracted = Directory.GetFiles(RelToFull(_testOutputDir), "*.png");
            Assert.That(extracted.Length, Is.EqualTo(0), "Dry run should not create files");
        }

        /// <summary>
        /// Tests that extracted sprites are imported with correct import settings.
        /// This verifies the texture importer configuration.
        /// </summary>
        [Test]
        public void ExtractedSpritesHaveCorrectImportSettings()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._outputDirectory = AssetDatabase.LoadAssetAtPath<Object>(_testOutputDir);
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared2x2Path);
            Assert.IsTrue(entry != null, "Should find shared_2x2 entry");

            DeselectAllEntries(extractor);
            entry._isSelected = true;
            if (0 < entry._sprites.Count)
            {
                entry._sprites[0]._isSelected = true;
            }

            extractor.ExtractSelectedSprites();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string[] pngFiles = Directory.GetFiles(
                RelToFull(_testOutputDir),
                "*.png",
                SearchOption.TopDirectoryOnly
            );
            Assert.That(pngFiles.Length, Is.EqualTo(1), "Should extract exactly 1 sprite");

            // Wrap verification in ExecuteWithImmediateImport to ensure asset is imported
            ExecuteWithImmediateImport(() =>
            {
                // Convert filesystem path to Unity asset path for loading importer
                string extractedPath = FullToRel(pngFiles[0]);
                string fullPath = pngFiles[0];
                TextureImporter importer =
                    AssetImporter.GetAtPath(extractedPath) as TextureImporter;
                Assert.IsTrue(
                    importer != null,
                    $"Should have texture importer for extracted sprite at '{extractedPath}'. "
                        + $"File exists on disk: {File.Exists(fullPath)}, "
                        + $"{nameof(AssetPathExistsInDatabase)}: {AssetPathExistsInDatabase(extractedPath)}"
                );

                Assert.That(
                    importer.textureType,
                    Is.EqualTo(TextureImporterType.Sprite),
                    "Extracted texture should be Sprite type"
                );
                Assert.That(
                    importer.spriteImportMode,
                    Is.EqualTo(SpriteImportMode.Single),
                    "Extracted sprite should be Single mode"
                );
            });
        }

        /// <summary>
        /// Tests that custom pivot mode is applied to extracted sprites.
        /// This verifies pivot configuration is transferred.
        /// </summary>
        [Test]
        public void CustomPivotIsAppliedToExtractedSprites()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._outputDirectory = AssetDatabase.LoadAssetAtPath<Object>(_testOutputDir);
            extractor._pivotMode = PivotMode.Custom;
            extractor._customPivot = new Vector2(0.25f, 0.75f);
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared2x2Path);
            Assert.IsTrue(entry != null, "Should find shared_2x2 entry");

            DeselectAllEntries(extractor);
            entry._isSelected = true;
            if (0 < entry._sprites.Count)
            {
                entry._sprites[0]._isSelected = true;
            }

            extractor.ExtractSelectedSprites();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string[] pngFiles = Directory.GetFiles(
                RelToFull(_testOutputDir),
                "*.png",
                SearchOption.TopDirectoryOnly
            );
            Assert.That(pngFiles.Length, Is.EqualTo(1), "Should extract exactly 1 sprite");

            // Wrap verification in ExecuteWithImmediateImport to ensure asset is imported
            ExecuteWithImmediateImport(() =>
            {
                // Convert filesystem path to Unity asset path for loading importer
                string firstExtractedPath = FullToRel(pngFiles[0]);
                string fullPath = pngFiles[0];
                TextureImporter importer =
                    AssetImporter.GetAtPath(firstExtractedPath) as TextureImporter;
                Assert.IsTrue(
                    importer != null,
                    $"Should have texture importer at '{firstExtractedPath}'. "
                        + $"File exists on disk: {File.Exists(fullPath)}, "
                        + $"{nameof(AssetPathExistsInDatabase)}: {AssetPathExistsInDatabase(firstExtractedPath)}"
                );

                TextureImporterSettings settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);

                Assert.That(
                    settings.spritePivot.x,
                    Is.EqualTo(0.25f).Within(0.01f),
                    "Custom pivot X should be 0.25"
                );
                Assert.That(
                    settings.spritePivot.y,
                    Is.EqualTo(0.75f).Within(0.01f),
                    "Custom pivot Y should be 0.75"
                );
            });
        }

        /// <summary>
        /// Tests that per-sprite pivot override takes precedence over sheet-level and global settings.
        /// This verifies the 3-tier pivot cascade: per-sprite -> per-sheet -> global.
        /// </summary>
        [Test]
        public void PerSpritePivotOverrideIsAppliedToExtractedSprite()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._outputDirectory = AssetDatabase.LoadAssetAtPath<Object>(_testOutputDir);
            extractor._pivotMode = PivotMode.Center;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared2x2Path);
            Assert.IsTrue(entry != null, "Should find shared_2x2 entry");

            DeselectAllEntries(extractor);
            entry._isSelected = true;
            entry._useGlobalSettings = false;
            entry._pivotModeOverride = PivotMode.TopLeft;

            Assert.IsTrue(0 < entry._sprites.Count, "Entry should have sprites");
            SpriteSheetExtractor.SpriteEntryData sprite = entry._sprites[0];
            sprite._isSelected = true;
            sprite._usePivotOverride = true;
            sprite._pivotModeOverride = PivotMode.BottomRight;

            extractor.ExtractSelectedSprites();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string[] pngFiles = Directory.GetFiles(
                RelToFull(_testOutputDir),
                "*.png",
                SearchOption.TopDirectoryOnly
            );
            Assert.That(pngFiles.Length, Is.EqualTo(1), "Should extract exactly 1 sprite");

            // Wrap verification in ExecuteWithImmediateImport to ensure asset is imported
            ExecuteWithImmediateImport(() =>
            {
                string extractedPath = FullToRel(pngFiles[0]);
                string fullPath = pngFiles[0];
                TextureImporter importer =
                    AssetImporter.GetAtPath(extractedPath) as TextureImporter;
                Assert.IsTrue(
                    importer != null,
                    $"Should have texture importer at '{extractedPath}'. "
                        + $"File exists on disk: {File.Exists(fullPath)}, "
                        + $"{nameof(AssetPathExistsInDatabase)}: {AssetPathExistsInDatabase(extractedPath)}"
                );

                TextureImporterSettings settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);

                Assert.That(
                    settings.spritePivot.x,
                    Is.EqualTo(1.0f).Within(0.01f),
                    "Per-sprite BottomRight pivot X should be 1.0"
                );
                Assert.That(
                    settings.spritePivot.y,
                    Is.EqualTo(0.0f).Within(0.01f),
                    "Per-sprite BottomRight pivot Y should be 0.0"
                );
            });
        }

        /// <summary>
        /// Tests that sheet-level pivot override is applied when sprite override is disabled.
        /// This verifies the 3-tier pivot cascade: per-sprite -> per-sheet -> global.
        /// </summary>
        [Test]
        public void SheetPivotOverrideIsAppliedWhenSpriteOverrideDisabled()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._outputDirectory = AssetDatabase.LoadAssetAtPath<Object>(_testOutputDir);
            extractor._pivotMode = PivotMode.Center;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared2x2Path);
            Assert.IsTrue(entry != null, "Should find shared_2x2 entry");

            DeselectAllEntries(extractor);
            entry._isSelected = true;
            entry._useGlobalSettings = false;
            entry._pivotModeOverride = PivotMode.Custom;
            entry._customPivotOverride = new Vector2(0.25f, 0.75f);

            Assert.IsTrue(0 < entry._sprites.Count, "Entry should have sprites");
            SpriteSheetExtractor.SpriteEntryData sprite = entry._sprites[0];
            sprite._isSelected = true;
            sprite._usePivotOverride = false;

            extractor.ExtractSelectedSprites();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string[] pngFiles = Directory.GetFiles(
                RelToFull(_testOutputDir),
                "*.png",
                SearchOption.TopDirectoryOnly
            );
            Assert.That(pngFiles.Length, Is.EqualTo(1), "Should extract exactly 1 sprite");

            // Wrap verification in ExecuteWithImmediateImport to ensure asset is imported
            ExecuteWithImmediateImport(() =>
            {
                string extractedPath = FullToRel(pngFiles[0]);
                string fullPath = pngFiles[0];
                TextureImporter importer =
                    AssetImporter.GetAtPath(extractedPath) as TextureImporter;
                Assert.IsTrue(
                    importer != null,
                    $"Should have texture importer at '{extractedPath}'. "
                        + $"File exists on disk: {File.Exists(fullPath)}, "
                        + $"{nameof(AssetPathExistsInDatabase)}: {AssetPathExistsInDatabase(extractedPath)}"
                );

                TextureImporterSettings settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);

                Assert.That(
                    settings.spritePivot.x,
                    Is.EqualTo(0.25f).Within(0.01f),
                    "Sheet-level custom pivot X should be 0.25"
                );
                Assert.That(
                    settings.spritePivot.y,
                    Is.EqualTo(0.75f).Within(0.01f),
                    "Sheet-level custom pivot Y should be 0.75"
                );
            });
        }

        /// <summary>
        /// Tests that padded grid extraction removes padding correctly.
        /// This verifies the padding calculation in extraction.
        /// </summary>
        [Test]
        public void PaddedGridExtractionRemovesPadding()
        {
            TestContext.WriteLine(
                "Testing padded grid extraction: 64x64 texture, 2x2 grid, 4px padding on all sides"
            );
            TestContext.WriteLine("Expected cell size: 32x32, padding removal: 8px each axis");

            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._outputDirectory = AssetDatabase.LoadAssetAtPath<Object>(_testOutputDir);
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.PaddedGrid;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 2;
            extractor._gridRows = 2;
            extractor._paddingLeft = 4;
            extractor._paddingRight = 4;
            extractor._paddingTop = 4;
            extractor._paddingBottom = 4;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared2x2Path);
            Assert.IsTrue(entry != null, "Should find shared_2x2 entry");

            DeselectAllEntries(extractor);
            entry._isSelected = true;
            if (0 < entry._sprites.Count)
            {
                entry._sprites[0]._isSelected = true;
            }

            extractor.ExtractSelectedSprites();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string[] pngFiles = Directory.GetFiles(
                RelToFull(_testOutputDir),
                "*.png",
                SearchOption.TopDirectoryOnly
            );
            Assert.That(pngFiles.Length, Is.EqualTo(1), "Should extract exactly 1 sprite");

            // Wrap verification in ExecuteWithImmediateImport to ensure asset is imported
            ExecuteWithImmediateImport(() =>
            {
                // Convert filesystem path to Unity asset path for loading texture
                string extractedPath = FullToRel(pngFiles[0]);
                Texture2D extracted = AssetDatabase.LoadAssetAtPath<Texture2D>(extractedPath);
                Assert.IsTrue(extracted != null, "Should load extracted texture");

                // 32x32 cell - 8 padding (4 left + 4 right) = 24x24
                int expectedWidth = 32 - 8;
                int expectedHeight = 32 - 8;

                TestContext.WriteLine(
                    $"Extracted sprite dimensions: {extracted.width}x{extracted.height} (expected {expectedWidth}x{expectedHeight})"
                );

                Assert.That(
                    extracted.width,
                    Is.EqualTo(expectedWidth),
                    "Padded extraction should remove horizontal padding"
                );
                Assert.That(
                    extracted.height,
                    Is.EqualTo(expectedHeight),
                    "Padded extraction should remove vertical padding"
                );
            });
        }

        /// <summary>
        /// Tests that large texture extraction works correctly.
        /// This verifies memory handling for large textures.
        /// </summary>
        [Test]
        public void LargeTextureExtractionWorksCorrectly()
        {
            TestContext.WriteLine(
                "Testing large texture extraction: 512x512 texture with 16x16 grid (256 sprites)"
            );
            TestContext.WriteLine($"Output directory: {_testOutputDir}");

            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._outputDirectory = AssetDatabase.LoadAssetAtPath<Object>(_testOutputDir);
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            // Use SharedLarge512Path (512x512, 16x16 grid = 256 sprites)
            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(
                extractor,
                SharedLarge512Path
            );
            Assert.IsTrue(entry != null, "Should find shared_large_512 entry");

            TestContext.WriteLine($"Source texture path: {SharedLarge512Path}");
            TestContext.WriteLine($"Sprites discovered in entry: {entry._sprites.Count}");

            DeselectAllEntries(extractor);
            entry._isSelected = true;
            extractor.SelectAll(entry);

            extractor.ExtractSelectedSprites();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string[] extracted = Directory.GetFiles(RelToFull(_testOutputDir), "*.png");
            TestContext.WriteLine($"Extracted {extracted.Length} sprites to output directory");

            Assert.That(
                extracted.Length,
                Is.EqualTo(256),
                "Should extract 256 sprites from 16x16 grid"
            );
        }

        /// <summary>
        /// Test case data for NPOT texture extraction tests.
        /// </summary>
        private static IEnumerable<TestCaseData> NpotTextureCases()
        {
            // Format: (width, height, columns, rows, expectedSpriteCount)
            yield return new TestCaseData(100, 100, 2, 2, 4).SetName(
                "NPOT.100x100.Grid2x2.Extracts4Sprites"
            );
            yield return new TestCaseData(100, 200, 2, 4, 8).SetName(
                "NPOT.100x200.Grid2x4.Extracts8Sprites"
            );
            yield return new TestCaseData(150, 75, 3, 1, 3).SetName(
                "NPOT.150x75.Grid3x1.Extracts3Sprites"
            );
            yield return new TestCaseData(127, 127, 1, 1, 1).SetName(
                "NPOT.127x127Prime.Grid1x1.Extracts1Sprite"
            );
            yield return new TestCaseData(3, 3, 1, 1, 1).SetName(
                "NPOT.3x3Minimal.Grid1x1.Extracts1Sprite"
            );
        }

        /// <summary>
        /// Tests that NPOT texture extraction works correctly with various non-power-of-two dimensions.
        /// This verifies handling of non-power-of-two dimensions and correct sprite sizing.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(NpotTextureCases))]
        public void NPOTTextureExtractionWorksCorrectly(
            int width,
            int height,
            int columns,
            int rows,
            int expectedSpriteCount
        )
        {
            TestContext.WriteLine(
                $"Testing NPOT texture extraction: {width}x{height} with {columns}x{rows} grid"
            );
            TestContext.WriteLine($"Expected sprite count: {expectedSpriteCount}");

            string npotPath = CreateSpriteSheet(
                $"npot_test_{width}x{height}_{columns}x{rows}",
                width,
                height,
                columns,
                rows
            );

            TestContext.WriteLine($"Created sprite sheet at: {npotPath}");

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._outputDirectory = AssetDatabase.LoadAssetAtPath<Object>(_testOutputDir);
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = columns;
            extractor._gridRows = rows;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            DeselectAllEntries(extractor);
            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, npotPath);
            Assert.IsTrue(entry != null, $"Should find npot_test entry at path: {npotPath}");

            entry._isSelected = true;
            extractor.SelectAll(entry);

            extractor.ExtractSelectedSprites();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string[] extracted = Directory.GetFiles(RelToFull(_testOutputDir), "*.png");
            TestContext.WriteLine($"Extracted {extracted.Length} sprites");

            Assert.That(
                extracted.Length,
                Is.EqualTo(expectedSpriteCount),
                $"Should extract {expectedSpriteCount} sprites from {width}x{height} texture with {columns}x{rows} grid"
            );

            // Wrap verification in ExecuteWithImmediateImport to ensure asset is imported
            int expectedSpriteWidth = width / columns;
            int expectedSpriteHeight = height / rows;
            ExecuteWithImmediateImport(() =>
            {
                // Verify extracted sprite dimensions
                string firstExtractedPath = FullToRel(extracted[0]);
                Texture2D extractedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    firstExtractedPath
                );
                Assert.IsTrue(extractedTexture != null, "Should load extracted texture");

                TestContext.WriteLine(
                    $"Extracted sprite dimensions: {extractedTexture.width}x{extractedTexture.height} (expected {expectedSpriteWidth}x{expectedSpriteHeight})"
                );

                Assert.That(
                    extractedTexture.width,
                    Is.EqualTo(expectedSpriteWidth),
                    $"Extracted sprite width should be {expectedSpriteWidth}"
                );
                Assert.That(
                    extractedTexture.height,
                    Is.EqualTo(expectedSpriteHeight),
                    $"Extracted sprite height should be {expectedSpriteHeight}"
                );
            });
        }

        /// <summary>
        /// Tests extraction with different texture formats.
        /// This verifies format compatibility.
        /// </summary>
        private static IEnumerable<TestCaseData> TextureFormatCases()
        {
            yield return new TestCaseData(TextureFormat.RGBA32).SetName("Format.RGBA32");
            yield return new TestCaseData(TextureFormat.RGB24).SetName("Format.RGB24");
            yield return new TestCaseData(TextureFormat.ARGB32).SetName("Format.ARGB32");
        }

        [Test]
        [TestCaseSource(nameof(TextureFormatCases))]
        public void ExtractionWorksWithDifferentTextureFormats(TextureFormat format)
        {
            TestContext.WriteLine($"Testing texture format: {format}");
            TestContext.WriteLine("Creating 64x64 test texture with 2x2 grid");

            Texture2D tex = Track(new Texture2D(64, 64, format, false));
            FillTexture(tex, Color.magenta);
            string path = Path.Combine(Root, $"format_{format}_test.png").SanitizePath();
            File.WriteAllBytes(RelToFull(path), tex.EncodeToPNG());
            TrackAssetPath(path);

            // Wrap importer setup in ExecuteWithImmediateImport to ensure asset is imported
            ExecuteWithImmediateImport(() =>
            {
                AssetDatabase.ImportAsset(path);
                // SetupSpriteImporter includes AssetDatabase.Refresh to ensure proper format loading
                SetupSpriteImporter(path, 2, 2);
            });

            TestContext.WriteLine($"Created texture at: {path}");

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._outputDirectory = AssetDatabase.LoadAssetAtPath<Object>(_testOutputDir);
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            // Other textures in the Root directory would otherwise interfere.
            DeselectAllEntries(extractor);
            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, path);
            Assert.IsTrue(entry != null, $"Should find format test entry at path: {path}");

            entry._isSelected = true;
            extractor.SelectAll(entry);

            extractor.ExtractSelectedSprites();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string[] extracted = Directory.GetFiles(RelToFull(_testOutputDir), "*.png");
            TestContext.WriteLine($"Extracted {extracted.Length} sprites with {format} format");

            Assert.That(
                extracted.Length,
                Is.EqualTo(4),
                $"Should extract 4 sprites from 2x2 grid with {format} format"
            );
        }

        /// <summary>
        /// Tests that extraction handles null output directory gracefully.
        /// This verifies defensive programming.
        /// </summary>
        [Test]
        public void NullOutputDirectoryIsHandledGracefully()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._outputDirectory = null;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared2x2Path);
            Assert.IsTrue(entry != null, "Should find shared_2x2 entry");

            DeselectAllEntries(extractor);
            entry._isSelected = true;
            extractor.SelectAll(entry);

            // Expect the error log from the production code
            LogAssert.Expect(LogType.Error, new Regex("Invalid output directory"));

            Assert.DoesNotThrow(
                () => extractor.ExtractSelectedSprites(),
                "Extraction should not throw with null output directory"
            );
        }

        /// <summary>
        /// Tests that extraction handles empty sprite sheet gracefully.
        /// This verifies defensive programming.
        /// </summary>
        [Test]
        public void EmptySpriteSheetIsHandledGracefully()
        {
            Texture2D tex = Track(new Texture2D(64, 64, TextureFormat.RGBA32, false));
            FillTexture(tex, Color.white);
            string path = Path.Combine(Root, "empty_sheet_test.png").SanitizePath();
            File.WriteAllBytes(RelToFull(path), tex.EncodeToPNG());
            TrackAssetPath(path);

            // Wrap importer setup in ExecuteWithImmediateImport to ensure asset is imported
            ExecuteWithImmediateImport(() =>
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsTrue(
                    importer != null,
                    $"TextureImporter should be available after import: {path}"
                );

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                SetSpriteSheet(importer, Array.Empty<SpriteMetaData>());
            });

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._outputDirectory = AssetDatabase.LoadAssetAtPath<Object>(_testOutputDir);
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            // Empty sprite sheets should be handled gracefully without errors
            Assert.DoesNotThrow(
                () => extractor.ExtractSelectedSprites(),
                "Extraction should not throw with empty sprite sheet"
            );

            // Verify no output files were created
            string[] extracted = Directory.GetFiles(RelToFull(_testOutputDir), "*.png");
            Assert.That(
                extracted.Length,
                Is.EqualTo(0),
                "Empty sprite sheet should not create any output files"
            );
        }

        /// <summary>
        /// Tests that <see cref="EnsureDirectoryWithinBatch"/> successfully creates and registers
        /// a directory while inside a fixture-level batch scope.
        /// </summary>
        /// <remarks>
        /// This test verifies that the helper method correctly pauses the batch scope,
        /// creates the directory on disk, refreshes AssetDatabase, and validates the folder
        /// is accessible via <see cref="AssetDatabase.IsValidFolder"/>.
        /// </remarks>
        [Test]
        public void EnsureDirectoryWithinBatchCreatesAndRegistersDirectory()
        {
            string uniqueDirName =
                "EnsureDirectoryTest_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string testDirPath = Path.Combine(_sharedOutputDir, uniqueDirName).SanitizePath();
            string fullPath = RelToFull(testDirPath);

            // Verify the directory does not exist before the test
            Assert.IsFalse(
                Directory.Exists(fullPath),
                "Directory should not exist on disk before calling EnsureDirectoryWithinBatch"
            );
            Assert.IsFalse(
                AssetDatabase.IsValidFolder(testDirPath),
                "Directory should not be registered with AssetDatabase before calling EnsureDirectoryWithinBatch"
            );

            // Verify we are currently in a batch scope (fixture-level batching is active)
            Assert.IsTrue(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                "Fixture-level batching should be active during this test"
            );

            Assert.DoesNotThrow(
                () => EnsureDirectoryWithinBatch(testDirPath),
                "EnsureDirectoryWithinBatch should not throw"
            );

            Assert.IsTrue(
                Directory.Exists(fullPath),
                "Directory should exist on disk after calling EnsureDirectoryWithinBatch"
            );

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(testDirPath),
                "Directory should be registered with AssetDatabase after calling EnsureDirectoryWithinBatch"
            );

            Assert.IsTrue(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                "Fixture-level batching should still be active after EnsureDirectoryWithinBatch completes"
            );
        }

        /// <summary>
        /// Tests that calling <see cref="EnsureDirectoryWithinBatch"/> twice with the same path
        /// works correctly without errors (idempotency).
        /// </summary>
        /// <remarks>
        /// This verifies that the helper method handles the case where a directory already exists
        /// both on disk and in AssetDatabase, ensuring idempotent behavior for repeated calls.
        /// </remarks>
        [Test]
        public void EnsureDirectoryWithinBatchIsIdempotent()
        {
            string uniqueDirName =
                "IdempotencyTest_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string testDirPath = Path.Combine(_sharedOutputDir, uniqueDirName).SanitizePath();
            string fullPath = RelToFull(testDirPath);

            // Verify the directory does not exist before the test
            Assert.IsFalse(
                Directory.Exists(fullPath),
                "Directory should not exist on disk before first call"
            );
            Assert.IsFalse(
                AssetDatabase.IsValidFolder(testDirPath),
                "Directory should not be registered with AssetDatabase before first call"
            );

            Assert.DoesNotThrow(
                () => EnsureDirectoryWithinBatch(testDirPath),
                "First call to EnsureDirectoryWithinBatch should not throw"
            );

            // Verify directory was created
            Assert.IsTrue(
                Directory.Exists(fullPath),
                "Directory should exist on disk after first call"
            );
            Assert.IsTrue(
                AssetDatabase.IsValidFolder(testDirPath),
                "Directory should be registered with AssetDatabase after first call"
            );

            Assert.DoesNotThrow(
                () => EnsureDirectoryWithinBatch(testDirPath),
                "Second call to EnsureDirectoryWithinBatch should not throw (idempotency)"
            );

            Assert.IsTrue(
                Directory.Exists(fullPath),
                "Directory should still exist on disk after second call"
            );
            Assert.IsTrue(
                AssetDatabase.IsValidFolder(testDirPath),
                "Directory should still be registered with AssetDatabase after second call"
            );

            Assert.IsTrue(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                "Fixture-level batching should still be active after idempotent calls"
            );
        }

        /// <summary>
        /// Tests that <see cref="EnsureDirectoryWithinBatch"/> throws <see cref="ArgumentNullException"/>
        /// when passed a null or empty path.
        /// </summary>
        /// <remarks>
        /// This test verifies the guard clause that validates input parameters before performing
        /// any directory creation operations.
        /// </remarks>
        [Test]
        public void EnsureDirectoryWithinBatchThrowsArgumentNullExceptionWhenPathIsNullOrEmpty()
        {
            ArgumentNullException nullException = Assert.Throws<ArgumentNullException>(
                () => EnsureDirectoryWithinBatch(null),
                "EnsureDirectoryWithinBatch should throw ArgumentNullException for null path"
            );
            Assert.That(
                nullException.ParamName,
                Is.EqualTo("assetPath"),
                "Exception should reference the 'assetPath' parameter"
            );

            ArgumentNullException emptyException = Assert.Throws<ArgumentNullException>(
                () => EnsureDirectoryWithinBatch(string.Empty),
                "EnsureDirectoryWithinBatch should throw ArgumentNullException for empty path"
            );
            Assert.That(
                emptyException.ParamName,
                Is.EqualTo("assetPath"),
                "Exception should reference the 'assetPath' parameter"
            );

            Assert.IsTrue(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                "Fixture-level batching should still be active after guard clause throws"
            );
        }
    }
#endif
}
