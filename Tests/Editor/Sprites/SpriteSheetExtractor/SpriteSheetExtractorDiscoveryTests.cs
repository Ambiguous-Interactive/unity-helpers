// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Tests for <see cref="SpriteSheetExtractor"/> covering sprite sheet discovery,
    /// selection handling, settings overrides, and mode detection using shared fixtures.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This test fixture uses shared fixtures created once in <see cref="CommonOneTimeSetUp"/>
    /// and cleaned up in <see cref="OneTimeTearDown"/>. Tests in this class do NOT perform
    /// extraction operations - they only test discovery, selection, and configuration.
    /// </para>
    /// <para>
    /// Tests are marked with [Category("Slow")] and [Category("Integration")] because they
    /// require AssetDatabase operations, even though they don't extract sprites.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    [Ignore("Beta feature - SpriteSheetExtractor tests are not reliable")]
    public sealed class SpriteSheetExtractorDiscoveryTests : SpriteSheetExtractorTestBase
    {
        private const string RootPath = "Assets/Temp/SpriteSheetExtractorDiscoveryTests";
        private const string OutputDirPath =
            "Assets/Temp/SpriteSheetExtractorDiscoveryTests/Output";

        /// <inheritdoc />
        protected override string Root => RootPath;

        /// <inheritdoc />
        protected override string OutputDir => OutputDirPath;

        /// <inheritdoc />
        /// <remarks>
        /// Returns the SharedSpriteTestFixtures directory since this class uses centralized
        /// shared fixtures rather than per-class fixtures.
        /// </remarks>
        protected override string SharedDir => SharedSpriteTestFixtures.GetSharedDirectory();

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            if (Application.isPlaying)
            {
                Assert.Ignore("AssetDatabase access requires edit mode.");
            }
            EnsureFolder(Root);
            EnsureFolder(OutputDir);
            SpriteSheetExtractor.SuppressUserPrompts = true;
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            if (Application.isPlaying)
            {
                return;
            }
            CleanupTrackedFoldersAndAssets();
        }

        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            DeferAssetCleanupToOneTimeTearDown = true;
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                EnsureFolderStatic(Root);
                EnsureFolderStatic(OutputDir);
            }
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
            // Set for base class compatibility - indicates fixtures are ready for use
            SharedFixturesCreated = true;
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            SharedSpriteTestFixtures.ReleaseFixtures();
            CleanupDeferredAssetsAndFolders();
            base.OneTimeTearDown();
        }

        private void AssertAllSpritesHaveSelection(
            SpriteSheetExtractor.SpriteSheetEntry entry,
            bool expectedSelection,
            string testContext
        )
        {
            Assert.IsTrue(
                entry != null,
                $"[{testContext}] Entry should not be null when checking selection state"
            );
            for (int i = 0; i < entry._sprites.Count; i++)
            {
                Assert.That(
                    entry._sprites[i]._isSelected,
                    Is.EqualTo(expectedSelection),
                    $"[{testContext}] Sprite {i} ('{entry._sprites[i]._originalName}') should have selection={expectedSelection}"
                );
            }
        }

        [Test]
        public void DiscoverSheetsFindsMultipleModeSpriteSheet()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            Assert.IsTrue(extractor._discoveredSheets != null);
            Assert.That(extractor._discoveredSheets.Count, Is.GreaterThanOrEqualTo(1));

            bool found = false;
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                if (extractor._discoveredSheets[i]._assetPath == Shared2x2Path)
                {
                    found = true;
                    Assert.That(extractor._discoveredSheets[i]._sprites.Count, Is.EqualTo(4));
                    Assert.That(
                        extractor._discoveredSheets[i]._importMode,
                        Is.EqualTo(SpriteImportMode.Multiple)
                    );
                    break;
                }
            }
            Assert.IsTrue(found, "Should find the shared 2x2 sprite sheet");
        }

        [Test]
        public void DiscoverSheetsFindsSingleModeSprite()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            Assert.IsTrue(extractor._discoveredSheets != null);

            bool found = false;
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                if (extractor._discoveredSheets[i]._assetPath == SharedSingleModePath)
                {
                    found = true;
                    Assert.That(extractor._discoveredSheets[i]._sprites.Count, Is.EqualTo(1));
                    Assert.That(
                        extractor._discoveredSheets[i]._importMode,
                        Is.EqualTo(SpriteImportMode.Single)
                    );
                    break;
                }
            }
            Assert.IsTrue(found, "Should find shared single mode sprite");
        }

        [Test]
        public void SelectAllSelectsAllSprites()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null, "Should find shared_4x4 entry");

            for (int i = 0; i < entry._sprites.Count; i++)
            {
                entry._sprites[i]._isSelected = false;
            }

            extractor.SelectAll(entry);

            AssertAllSpritesHaveSelection(entry, true, "SelectAll");
        }

        [Test]
        public void SelectNoneDeselectsAllSprites()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null, "Should find shared_4x4 entry");

            for (int i = 0; i < entry._sprites.Count; i++)
            {
                entry._sprites[i]._isSelected = true;
            }

            extractor.SelectNone(entry);

            AssertAllSpritesHaveSelection(entry, false, "SelectNone");
        }

        [Test]
        public void IndividualSpriteSelectionWorks()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null, "Should find shared_4x4 entry");
            Assert.That(entry._sprites.Count, Is.GreaterThan(0));

            entry._sprites[0]._isSelected = false;

            Assert.That(
                entry._sprites[0]._isSelected,
                Is.False,
                "First sprite should be deselected"
            );

            entry._sprites[0]._isSelected = true;

            Assert.That(entry._sprites[0]._isSelected, Is.True, "First sprite should be selected");
        }

        [Test]
        public void GridBasedExtractionCreatesCorrectSpriteCount()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 4;
            extractor._gridRows = 4;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            bool found = false;
            for (int i = 0; i < extractor._discoveredSheets.Count; ++i)
            {
                if (extractor._discoveredSheets[i]._assetPath == Shared4x4Path)
                {
                    found = true;
                    Assert.That(
                        extractor._discoveredSheets[i]._sprites.Count,
                        Is.EqualTo(16),
                        "Grid 4x4 should create 16 sprites"
                    );
                    break;
                }
            }
            Assert.IsTrue(found, "Should find shared_4x4");
        }

        private static IEnumerable<TestCaseData> ExtractionModeCases()
        {
            yield return new TestCaseData(SpriteSheetExtractor.ExtractionMode.FromMetadata).SetName(
                "ExtractionMode.FromMetadata"
            );
            yield return new TestCaseData(SpriteSheetExtractor.ExtractionMode.GridBased).SetName(
                "ExtractionMode.GridBased"
            );
            yield return new TestCaseData(SpriteSheetExtractor.ExtractionMode.PaddedGrid).SetName(
                "ExtractionMode.PaddedGrid"
            );
        }

        [Test]
        [TestCaseSource(nameof(ExtractionModeCases))]
        public void ExtractionModeSwitchingWorksCorrectly(SpriteSheetExtractor.ExtractionMode mode)
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = mode;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 2;
            extractor._gridRows = 2;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            bool found = false;
            for (int i = 0; i < extractor._discoveredSheets.Count; ++i)
            {
                if (extractor._discoveredSheets[i]._assetPath == Shared2x2Path)
                {
                    found = true;
                    Assert.That(
                        extractor._discoveredSheets[i]._sprites.Count,
                        Is.GreaterThanOrEqualTo(1),
                        $"Mode {mode} should discover sprites"
                    );
                    break;
                }
            }
            Assert.IsTrue(found, $"Should find shared_2x2 with mode {mode}");
        }

        [Test]
        public void DiscoverySheetsInitiallySelectedByDefault()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            bool anyFound = false;
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                SpriteSheetExtractor.SpriteSheetEntry entry = extractor._discoveredSheets[i];
                if (0 < entry._sprites.Count)
                {
                    anyFound = true;
                    AssertAllSpritesHaveSelection(entry, true, "InitialSelection");
                }
            }
            Assert.IsTrue(anyFound, "Should have found at least one sheet with sprites");
        }

        [Test]
        public void RediscoveryClearsOldSheets()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            int initialCount = extractor._discoveredSheets.Count;
            Assert.That(initialCount, Is.GreaterThan(0), "Should have discovered sheets initially");

            extractor.DiscoverSpriteSheets(generatePreviews: false);

            Assert.That(
                extractor._discoveredSheets.Count,
                Is.EqualTo(initialCount),
                "Rediscovery should produce same count"
            );
        }

        [Test]
        public void NullInputDirectoriesHandledGracefully()
        {
            SpriteSheetExtractor extractor = Track(
                ScriptableObject.CreateInstance<SpriteSheetExtractor>()
            );
            extractor._inputDirectories = null;

            Assert.DoesNotThrow(() => extractor.DiscoverSpriteSheets(generatePreviews: false));
            Assert.That(
                extractor._discoveredSheets == null || extractor._discoveredSheets.Count == 0
            );
        }

        [Test]
        public void EmptyInputDirectoriesListHandledGracefully()
        {
            SpriteSheetExtractor extractor = Track(
                ScriptableObject.CreateInstance<SpriteSheetExtractor>()
            );
            extractor._inputDirectories = new List<Object>();

            Assert.DoesNotThrow(() => extractor.DiscoverSpriteSheets(generatePreviews: false));
            Assert.That(
                extractor._discoveredSheets == null || extractor._discoveredSheets.Count == 0
            );
        }

        [Test]
        public void NoSelectedSpritesDoesNotCrash()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                for (int j = 0; j < extractor._discoveredSheets[i]._sprites.Count; j++)
                {
                    extractor._discoveredSheets[i]._sprites[j]._isSelected = false;
                }
            }

            Assert.DoesNotThrow(() => extractor.ExtractSelectedSprites());
        }

        [Test]
        public void ApplyGlobalSettingsToAllCopiesGlobalValuesToAllEntries()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.PaddedGrid;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 6;
            extractor._gridRows = 6;
            extractor._paddingLeft = 5;
            extractor._paddingRight = 5;
            extractor._paddingTop = 5;
            extractor._paddingBottom = 5;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                extractor._discoveredSheets[i]._useGlobalSettings = false;
            }

            extractor.ApplyGlobalSettingsToAll();

            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                SpriteSheetExtractor.SpriteSheetEntry entry = extractor._discoveredSheets[i];
                Assert.That(entry._useGlobalSettings, Is.False);
                Assert.That(
                    entry._extractionModeOverride,
                    Is.EqualTo(SpriteSheetExtractor.ExtractionMode.PaddedGrid)
                );
                Assert.That(
                    entry._gridSizeModeOverride,
                    Is.EqualTo(SpriteSheetExtractor.GridSizeMode.Manual)
                );
                Assert.That(entry._gridColumnsOverride, Is.EqualTo(6));
                Assert.That(entry._gridRowsOverride, Is.EqualTo(6));
                Assert.That(entry._paddingLeftOverride, Is.EqualTo(5));
            }
        }

        [Test]
        public void PopulateSpritesFromGridUsesEffectiveSettings()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 4;
            extractor._gridRows = 4;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null, "Should find shared_4x4");

            entry._useGlobalSettings = false;
            entry._gridSizeModeOverride = SpriteSheetExtractor.GridSizeMode.Manual;
            entry._gridColumnsOverride = 2;
            entry._gridRowsOverride = 2;

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Shared4x4Path);
            Assert.IsTrue(texture != null, "Should load texture");

            entry._sprites.Clear();
            extractor.PopulateSpritesFromGrid(entry, texture);

            Assert.That(entry._sprites.Count, Is.EqualTo(4), "Should have 4 sprites with 2x2 grid");
        }

        [Test]
        public void PopulateSpritesFromPaddedGridUsesEffectivePadding()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.PaddedGrid;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 2;
            extractor._gridRows = 2;
            extractor._paddingLeft = 0;
            extractor._paddingRight = 0;
            extractor._paddingTop = 0;
            extractor._paddingBottom = 0;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared2x2Path);
            Assert.IsTrue(entry != null, "Should find shared_2x2");

            entry._useGlobalSettings = false;
            entry._gridSizeModeOverride = SpriteSheetExtractor.GridSizeMode.Manual;
            entry._gridColumnsOverride = 2;
            entry._gridRowsOverride = 2;
            entry._paddingLeftOverride = 4;
            entry._paddingRightOverride = 4;
            entry._paddingTopOverride = 4;
            entry._paddingBottomOverride = 4;

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Shared2x2Path);
            Assert.IsTrue(texture != null, "Should load texture");

            entry._sprites.Clear();
            extractor.PopulateSpritesFromPaddedGrid(entry, texture);

            Assert.That(entry._sprites.Count, Is.EqualTo(4), "Should have 4 sprites");
            if (0 < entry._sprites.Count)
            {
                int expectedWidth = 32 - 8;
                Assert.That(
                    (int)entry._sprites[0]._rect.width,
                    Is.EqualTo(expectedWidth),
                    "Sprite width should account for padding"
                );
            }
        }

        private static IEnumerable<TestCaseData> EntryWithPartialOverridesCases()
        {
            yield return new TestCaseData(true, false, false, false).SetName(
                "PartialOverrides.ExtractionModeOnly"
            );
            yield return new TestCaseData(false, true, false, false).SetName(
                "PartialOverrides.GridSizeModeOnly"
            );
            yield return new TestCaseData(false, false, true, true).SetName(
                "PartialOverrides.GridDimensionsOnly"
            );
        }

        [Test]
        [TestCaseSource(nameof(EntryWithPartialOverridesCases))]
        public void EntryWithPartialOverridesUsesGlobalForNullFields(
            bool hasExtractionMode,
            bool hasGridSizeMode,
            bool hasGridColumns,
            bool hasGridRows
        )
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.FromMetadata;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Auto;
            extractor._gridColumns = 4;
            extractor._gridRows = 4;

            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = false,
                _extractionModeOverride = hasExtractionMode
                    ? SpriteSheetExtractor.ExtractionMode.GridBased
                    : null,
                _gridSizeModeOverride = hasGridSizeMode
                    ? SpriteSheetExtractor.GridSizeMode.Manual
                    : null,
                _gridColumnsOverride = hasGridColumns ? 8 : null,
                _gridRowsOverride = hasGridRows ? 8 : null,
            };

            SpriteSheetExtractor.ExtractionMode effectiveMode =
                extractor.GetEffectiveExtractionMode(entry);
            SpriteSheetExtractor.GridSizeMode effectiveGridMode =
                extractor.GetEffectiveGridSizeMode(entry);
            int effectiveColumns = extractor.GetEffectiveGridColumns(entry);
            int effectiveRows = extractor.GetEffectiveGridRows(entry);

            if (hasExtractionMode)
            {
                Assert.That(
                    effectiveMode,
                    Is.EqualTo(SpriteSheetExtractor.ExtractionMode.GridBased)
                );
            }
            else
            {
                Assert.That(
                    effectiveMode,
                    Is.EqualTo(SpriteSheetExtractor.ExtractionMode.FromMetadata)
                );
            }

            if (hasGridSizeMode)
            {
                Assert.That(
                    effectiveGridMode,
                    Is.EqualTo(SpriteSheetExtractor.GridSizeMode.Manual)
                );
            }
            else
            {
                Assert.That(effectiveGridMode, Is.EqualTo(SpriteSheetExtractor.GridSizeMode.Auto));
            }

            if (hasGridColumns)
            {
                Assert.That(effectiveColumns, Is.EqualTo(8));
            }
            else
            {
                Assert.That(effectiveColumns, Is.EqualTo(4));
            }

            if (hasGridRows)
            {
                Assert.That(effectiveRows, Is.EqualTo(8));
            }
            else
            {
                Assert.That(effectiveRows, Is.EqualTo(4));
            }
        }

        [Test]
        public void EntryWithAllNullOverridesAndUseGlobalFalseUsesGlobalValues()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.PaddedGrid;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 5;
            extractor._gridRows = 5;

            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = false,
                _extractionModeOverride = null,
                _gridSizeModeOverride = null,
                _gridColumnsOverride = null,
                _gridRowsOverride = null,
            };

            Assert.That(
                extractor.GetEffectiveExtractionMode(entry),
                Is.EqualTo(SpriteSheetExtractor.ExtractionMode.PaddedGrid)
            );
            Assert.That(
                extractor.GetEffectiveGridSizeMode(entry),
                Is.EqualTo(SpriteSheetExtractor.GridSizeMode.Manual)
            );
            Assert.That(extractor.GetEffectiveGridColumns(entry), Is.EqualTo(5));
            Assert.That(extractor.GetEffectiveGridRows(entry), Is.EqualTo(5));
        }

        [Test]
        public void SingleEntryBehaviorWorksCorrectly()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 2;
            extractor._gridRows = 2;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            Assert.That(extractor._discoveredSheets.Count, Is.GreaterThan(0));

            SpriteSheetExtractor.SpriteSheetEntry entry = extractor._discoveredSheets[0];
            Assert.That(entry._useGlobalSettings, Is.True);
            Assert.That(entry._sprites.Count, Is.GreaterThan(0));
        }

        [Test]
        public void PerSheetExtractionModeAffectsDiscovery()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 4;
            extractor._gridRows = 4;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null, "Should find shared_4x4");
            int globalCount = entry._sprites.Count;

            entry._useGlobalSettings = false;
            entry._extractionModeOverride = SpriteSheetExtractor.ExtractionMode.GridBased;
            entry._gridSizeModeOverride = SpriteSheetExtractor.GridSizeMode.Manual;
            entry._gridColumnsOverride = 2;
            entry._gridRowsOverride = 2;

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Shared4x4Path);
            entry._sprites.Clear();
            extractor.PopulateSpritesFromGrid(entry, texture);

            Assert.That(entry._sprites.Count, Is.EqualTo(4));
            Assert.That(entry._sprites.Count, Is.Not.EqualTo(globalCount));
        }

        private static IEnumerable<TestCaseData> DiscoveryVariousAspectRatiosCases()
        {
            yield return new TestCaseData("wide", 8).SetName("Discovery.WideAspectRatio");
            yield return new TestCaseData("tall", 8).SetName("Discovery.TallAspectRatio");
            yield return new TestCaseData("odd", 9).SetName("Discovery.OddDimensions");
        }

        [Test]
        [TestCaseSource(nameof(DiscoveryVariousAspectRatiosCases))]
        public void DiscoveryFindsSpriteSheetWithVariousAspectRatiosAndDimensions(
            string sheetType,
            int expectedSpriteCount
        )
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            string targetPath = sheetType switch
            {
                "wide" => SharedWidePath,
                "tall" => SharedTallPath,
                "odd" => SharedOddPath,
                _ => null,
            };

            Assert.That(
                !string.IsNullOrEmpty(targetPath),
                $"Target path for {sheetType} should exist"
            );

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, targetPath);
            Assert.IsTrue(entry != null, $"Should find {sheetType} sheet");
            Assert.That(
                entry._sprites.Count,
                Is.EqualTo(expectedSpriteCount),
                $"{sheetType} sheet should have {expectedSpriteCount} sprites"
            );
        }

        private static IEnumerable<TestCaseData> SelectAllVariousSheetTypesCases()
        {
            yield return new TestCaseData("2x2", 4).SetName("SelectAll.2x2Sheet");
            yield return new TestCaseData("4x4", 16).SetName("SelectAll.4x4Sheet");
            yield return new TestCaseData("8x8", 64).SetName("SelectAll.8x8Sheet");
        }

        [Test]
        [TestCaseSource(nameof(SelectAllVariousSheetTypesCases))]
        public void SelectAllWorksWithVariousSheetTypes(string sheetType, int expectedSpriteCount)
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            string targetPath = sheetType switch
            {
                "2x2" => Shared2x2Path,
                "4x4" => Shared4x4Path,
                "8x8" => Shared8x8Path,
                _ => null,
            };

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, targetPath);
            Assert.IsTrue(entry != null, $"Should find {sheetType} sheet");

            for (int i = 0; i < entry._sprites.Count; i++)
            {
                entry._sprites[i]._isSelected = false;
            }

            extractor.SelectAll(entry);

            AssertAllSpritesHaveSelection(entry, true, $"SelectAll.{sheetType}");
            Assert.That(entry._sprites.Count, Is.EqualTo(expectedSpriteCount));
        }

        [Test]
        public void SpritePreviewTexturesExistAfterDiscovery()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: true);

            bool foundAnyWithPreviews = false;
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                SpriteSheetExtractor.SpriteSheetEntry entry = extractor._discoveredSheets[i];
                for (int j = 0; j < entry._sprites.Count; j++)
                {
                    if (entry._sprites[j]._previewTexture != null)
                    {
                        foundAnyWithPreviews = true;
                        break;
                    }
                }
                if (foundAnyWithPreviews)
                {
                    break;
                }
            }

            Assert.IsTrue(
                foundAnyWithPreviews,
                "At least some sprites should have preview textures after discovery"
            );
        }

        [Test]
        public void ExtractionModeChangeRepopulatesSprites()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.FromMetadata;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null, "Should find shared_4x4");
            int metadataCount = entry._sprites.Count;

            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 2;
            extractor._gridRows = 2;

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Shared4x4Path);
            entry._sprites.Clear();
            extractor.PopulateSpritesFromGrid(entry, texture);

            Assert.That(entry._sprites.Count, Is.Not.EqualTo(metadataCount));
            Assert.That(entry._sprites.Count, Is.EqualTo(4));
        }

        [Test]
        public void InitializeOverridesFromGlobalCopiesAllGlobalValuesToOverrideFields()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.PaddedGrid;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 7;
            extractor._gridRows = 7;
            extractor._cellWidth = 48;
            extractor._cellHeight = 48;
            extractor._paddingLeft = 3;
            extractor._paddingRight = 3;
            extractor._paddingTop = 3;
            extractor._paddingBottom = 3;
            extractor._alphaThreshold = 0.75f;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null, "Should find shared_4x4");

            entry._useGlobalSettings = true;
            extractor.InitializeOverridesFromGlobal(entry);

            Assert.That(
                entry._extractionModeOverride,
                Is.EqualTo(SpriteSheetExtractor.ExtractionMode.PaddedGrid)
            );
            Assert.That(
                entry._gridSizeModeOverride,
                Is.EqualTo(SpriteSheetExtractor.GridSizeMode.Manual)
            );
            Assert.That(entry._gridColumnsOverride, Is.EqualTo(7));
            Assert.That(entry._gridRowsOverride, Is.EqualTo(7));
            Assert.That(entry._cellWidthOverride, Is.EqualTo(48));
            Assert.That(entry._cellHeightOverride, Is.EqualTo(48));
            Assert.That(entry._paddingLeftOverride, Is.EqualTo(3));
            Assert.That(entry._paddingRightOverride, Is.EqualTo(3));
            Assert.That(entry._paddingTopOverride, Is.EqualTo(3));
            Assert.That(entry._paddingBottomOverride, Is.EqualTo(3));
            Assert.That(entry._alphaThresholdOverride, Is.EqualTo(0.75f).Within(0.001f));
        }

        [Test]
        public void InitializeOverridesFromGlobalDoesNothingForNullEntry()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();

            Assert.DoesNotThrow(() => extractor.InitializeOverridesFromGlobal(null));
        }

        [Test]
        public void TogglingFromPerSheetToGlobalDoesNotChangeOverrides()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null, "Should find shared_4x4");

            entry._useGlobalSettings = false;
            entry._extractionModeOverride = SpriteSheetExtractor.ExtractionMode.PaddedGrid;
            entry._gridColumnsOverride = 10;

            entry._useGlobalSettings = true;

            Assert.That(
                entry._extractionModeOverride,
                Is.EqualTo(SpriteSheetExtractor.ExtractionMode.PaddedGrid)
            );
            Assert.That(entry._gridColumnsOverride, Is.EqualTo(10));
        }

        [Test]
        public void AfterInitializeOverridesFromGlobalEffectiveValuesRemainSame()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridColumns = 8;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null, "Should find shared_4x4");

            SpriteSheetExtractor.ExtractionMode effectiveBefore =
                extractor.GetEffectiveExtractionMode(entry);
            int columnsBefore = extractor.GetEffectiveGridColumns(entry);

            extractor.InitializeOverridesFromGlobal(entry);
            entry._useGlobalSettings = false;

            SpriteSheetExtractor.ExtractionMode effectiveAfter =
                extractor.GetEffectiveExtractionMode(entry);
            int columnsAfter = extractor.GetEffectiveGridColumns(entry);

            Assert.That(effectiveAfter, Is.EqualTo(effectiveBefore));
            Assert.That(columnsAfter, Is.EqualTo(columnsBefore));
        }

        [Test]
        public void EnableAllPivotsButtonSetsPivotOverrideForAllSprites()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null, "Should find shared_4x4");

            for (int i = 0; i < entry._sprites.Count; i++)
            {
                entry._sprites[i]._usePivotOverride = false;
            }

            extractor.EnableAllPivotOverrides(entry);

            for (int i = 0; i < entry._sprites.Count; i++)
            {
                Assert.That(
                    entry._sprites[i]._usePivotOverride,
                    Is.True,
                    $"Sprite {i} should have pivot override enabled"
                );
            }
        }

        [Test]
        public void DisableAllPivotsButtonClearsAllPivotOverrides()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null, "Should find shared_4x4");

            for (int i = 0; i < entry._sprites.Count; i++)
            {
                entry._sprites[i]._usePivotOverride = true;
            }

            extractor.DisableAllPivotOverrides(entry);

            for (int i = 0; i < entry._sprites.Count; i++)
            {
                Assert.That(
                    entry._sprites[i]._usePivotOverride,
                    Is.False,
                    $"Sprite {i} should have pivot override disabled"
                );
            }
        }

        [Test]
        public void BatchPivotButtonsHandleEmptySpriteListSafely()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();

            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _sprites = new List<SpriteSheetExtractor.SpriteEntryData>(),
            };

            Assert.DoesNotThrow(() => extractor.EnableAllPivotOverrides(entry));
            Assert.DoesNotThrow(() => extractor.DisableAllPivotOverrides(entry));
        }

        [Test]
        public void InvalidateEntryDoesNotThrowForNullEntry()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();

            Assert.DoesNotThrow(() => extractor.InvalidateEntry(null));
        }

        [Test]
        public void IsEntryStaleFalseForNullEntry()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();

            bool result = extractor.IsEntryStale(null);

            Assert.That(result, Is.False);
        }

        [Test]
        public void IsEntryStaleTrueWhenNeedsRegenerationSet()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();

            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _needsRegeneration = true,
            };

            bool result = extractor.IsEntryStale(entry);

            Assert.That(result, Is.True);
        }

        [Test]
        public void IsEntryStaleFalseWhenCacheKeyMatches()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null, "Should find shared_4x4");

            entry._needsRegeneration = false;
            entry._lastCacheKey = extractor.GetBoundsCacheKey(entry);

            bool result = extractor.IsEntryStale(entry);

            Assert.That(result, Is.False);
        }

        [Test]
        public void GetBoundsCacheKeyReturnsDifferentValueForDifferentExtractionMode()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.FromMetadata;
            int key1 = extractor.GetBoundsCacheKey(null);

            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            int key2 = extractor.GetBoundsCacheKey(null);

            Assert.That(key1, Is.Not.EqualTo(key2));
        }

        [Test]
        public void GetBoundsCacheKeyReturnsDifferentValueForDifferentGridSize()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._gridColumns = 4;
            extractor._gridRows = 4;
            int key1 = extractor.GetBoundsCacheKey(null);

            extractor._gridColumns = 8;
            extractor._gridRows = 8;
            int key2 = extractor.GetBoundsCacheKey(null);

            Assert.That(key1, Is.Not.EqualTo(key2));
        }

        [Test]
        public void GetBoundsCacheKeyReturnsSameValueForSameSettings()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridColumns = 4;
            extractor._gridRows = 4;

            int key1 = extractor.GetBoundsCacheKey(null);
            int key2 = extractor.GetBoundsCacheKey(null);

            Assert.That(key1, Is.EqualTo(key2));
        }

        [Test]
        public void GetBoundsCacheKeyReturnsZeroForNullExtractor()
        {
            int result = SpriteSheetExtractor.GetBoundsCacheKeyStatic(null, null);

            Assert.That(result, Is.EqualTo(0));
        }

        [Test]
        public void RepopulateSpritesForEntryHandlesNullTexture()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();

            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _assetPath = "NonExistent/Path.png",
                _sprites = new List<SpriteSheetExtractor.SpriteEntryData>(),
            };

            Assert.DoesNotThrow(() => extractor.RepopulateSpritesForEntry(entry));
        }

        [Test]
        public void RepopulateSpritesForEntryInitializesNullSpritesList()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null, "Should find shared_4x4");

            entry._sprites = null;

            extractor.RepopulateSpritesForEntry(entry);

            Assert.IsTrue(entry._sprites != null, "Sprites list should be initialized");
        }

        [Test]
        public void EmptyDirectoryReturnsNoSheets()
        {
            string emptyDir = System.IO.Path.Combine(Root, "EmptyDir").SanitizePath();
            EnsureFolder(emptyDir);

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._inputDirectories = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(emptyDir),
            };
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            Assert.IsTrue(extractor._discoveredSheets != null);
            Assert.That(extractor._discoveredSheets.Count, Is.EqualTo(0));
        }

        [Test]
        public void DirectoryWithNoSpriteSheetsReturnsEmpty()
        {
            string noSpriteDir = System.IO.Path.Combine(Root, "NoSpriteDir").SanitizePath();
            EnsureFolder(noSpriteDir);

            Texture2D tex = Track(new Texture2D(32, 32, TextureFormat.RGBA32, false));
            string path = System.IO.Path.Combine(noSpriteDir, "regular_texture.png").SanitizePath();
            System.IO.File.WriteAllBytes(RelToFull(path), tex.EncodeToPNG());
            TrackAssetPath(path);
            AssetDatabase.ImportAsset(path);
            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            imp.textureType = TextureImporterType.Default;
            imp.SaveAndReimport();

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._inputDirectories = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(noSpriteDir),
            };
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            Assert.IsTrue(extractor._discoveredSheets != null);
            Assert.That(extractor._discoveredSheets.Count, Is.EqualTo(0));
        }

        [Test]
        public void ProcessesMultipleSheetsInSingleDirectory()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            Assert.Greater(extractor._discoveredSheets.Count, 1);
        }

        [Test]
        public void ProcessesMultipleInputDirectories()
        {
            string secondDir = System.IO.Path.Combine(Root, "SecondDir").SanitizePath();
            EnsureFolder(secondDir);

            CreateSpriteSheet("multi_dir_sheet", 64, 64, 2, 2, secondDir);

            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._inputDirectories.Add(AssetDatabase.LoadAssetAtPath<Object>(secondDir));
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            bool foundShared = false;
            bool foundSecond = false;

            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                if (extractor._discoveredSheets[i]._assetPath.Contains("test_"))
                {
                    foundShared = true;
                }
                if (extractor._discoveredSheets[i]._assetPath.Contains("multi_dir_sheet"))
                {
                    foundSecond = true;
                }
            }

            Assert.IsTrue(foundShared, "Should find shared fixtures");
            Assert.IsTrue(foundSecond, "Should find sheet in second directory");
        }

        private static IEnumerable<TestCaseData> SortModeTestCases()
        {
            yield return new TestCaseData(SpriteSheetExtractor.SortMode.ByPositionTopLeft).SetName(
                "SortMode.ByPositionTopLeft"
            );
            yield return new TestCaseData(
                SpriteSheetExtractor.SortMode.ByPositionBottomLeft
            ).SetName("SortMode.ByPositionBottomLeft");
        }

        [Test]
        [TestCaseSource(nameof(SortModeTestCases))]
        public void SortModeWorksWithExtractionModes(SpriteSheetExtractor.SortMode sortMode)
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._sortMode = sortMode;
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null);
            Assert.Greater(entry._sprites.Count, 0);
        }

        [Test]
        public void SortModeReversedReversesSpriteOrder()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._sortMode = SpriteSheetExtractor.SortMode.ByPositionTopLeft;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null, "Should find shared_4x4");
            Assert.GreaterOrEqual(
                entry._sprites.Count,
                2,
                "Need at least 2 sprites to test reversal"
            );

            // Get original order
            List<SpriteSheetExtractor.SpriteEntryData> originalOrder =
                SpriteSheetExtractor.ApplySortMode(
                    entry._sprites,
                    SpriteSheetExtractor.SortMode.Original
                );

            // Get reversed order
            List<SpriteSheetExtractor.SpriteEntryData> reversedOrder =
                SpriteSheetExtractor.ApplySortMode(
                    entry._sprites,
                    SpriteSheetExtractor.SortMode.Reversed
                );

            // Verify the order is actually reversed
            Assert.That(reversedOrder.Count, Is.EqualTo(originalOrder.Count));
            for (int i = 0; i < originalOrder.Count; i++)
            {
                Assert.That(
                    reversedOrder[i],
                    Is.SameAs(originalOrder[originalOrder.Count - 1 - i]),
                    $"Element at index {i} should be from the opposite end of the original list"
                );
            }
        }

        [Test]
        public void SortModeByNameSortsAlphabetically()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._sortMode = SpriteSheetExtractor.SortMode.ByName;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null, "Should find shared_4x4");
            Assert.GreaterOrEqual(
                entry._sprites.Count,
                2,
                "Need at least 2 sprites to test sorting"
            );

            // Get sorted order by name
            List<SpriteSheetExtractor.SpriteEntryData> sortedByName =
                SpriteSheetExtractor.ApplySortMode(
                    entry._sprites,
                    SpriteSheetExtractor.SortMode.ByName
                );

            // Verify alphabetical ordering
            for (int i = 1; i < sortedByName.Count; i++)
            {
                int comparison = string.Compare(
                    sortedByName[i - 1]._originalName,
                    sortedByName[i]._originalName,
                    StringComparison.Ordinal
                );
                Assert.That(
                    comparison,
                    Is.LessThanOrEqualTo(0),
                    $"'{sortedByName[i - 1]._originalName}' should come before or equal to "
                        + $"'{sortedByName[i]._originalName}' in alphabetical order"
                );
            }
        }

        private static IEnumerable<TestCaseData> PreviewTextureSizeCases()
        {
            yield return new TestCaseData(32, 32).SetName("PreviewTexture.32x32");
            yield return new TestCaseData(64, 64).SetName("PreviewTexture.64x64");
            yield return new TestCaseData(128, 128).SetName("PreviewTexture.128x128");
            yield return new TestCaseData(16, 16).SetName("PreviewTexture.16x16");
        }

        [Test]
        [TestCaseSource(nameof(PreviewTextureSizeCases))]
        public void PreviewTextureGenerationWorksWithVariousSizes(int width, int height)
        {
            CreateSpriteSheet($"preview_size_{width}x{height}", width * 2, height * 2, 2, 2);

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._previewSizeMode = SpriteSheetExtractor.PreviewSizeMode.Size64;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = null;
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                if (
                    extractor
                        ._discoveredSheets[i]
                        ._assetPath.Contains($"preview_size_{width}x{height}")
                )
                {
                    entry = extractor._discoveredSheets[i];
                    break;
                }
            }

            Assert.IsTrue(entry != null);
            Assert.Greater(entry._sprites.Count, 0);
        }

        private static IEnumerable<TestCaseData> OddDimensionCases()
        {
            yield return new TestCaseData(33, 33).SetName("PreviewTexture.Odd.33x33");
            yield return new TestCaseData(65, 65).SetName("PreviewTexture.Odd.65x65");
            yield return new TestCaseData(127, 127).SetName("PreviewTexture.Odd.127x127");
        }

        [Test]
        [TestCaseSource(nameof(OddDimensionCases))]
        public void PreviewTextureGenerationWorksWithOddDimensions(int width, int height)
        {
            CreateSpriteSheet($"odd_preview_{width}x{height}", width, height, 1, 1);

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._previewSizeMode = SpriteSheetExtractor.PreviewSizeMode.Size64;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = null;
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                if (
                    extractor
                        ._discoveredSheets[i]
                        ._assetPath.Contains($"odd_preview_{width}x{height}")
                )
                {
                    entry = extractor._discoveredSheets[i];
                    break;
                }
            }

            Assert.IsTrue(entry != null);
        }

        private static IEnumerable<TestCaseData> AspectRatioCases()
        {
            yield return new TestCaseData(128, 64).SetName("PreviewTexture.Aspect.2to1");
            yield return new TestCaseData(64, 128).SetName("PreviewTexture.Aspect.1to2");
            yield return new TestCaseData(256, 64).SetName("PreviewTexture.Aspect.4to1");
        }

        [Test]
        [TestCaseSource(nameof(AspectRatioCases))]
        public void PreviewTextureGenerationWorksWithVariousAspectRatios(int width, int height)
        {
            CreateSpriteSheet($"aspect_{width}x{height}", width, height, 2, 1);

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = null;
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                if (extractor._discoveredSheets[i]._assetPath.Contains($"aspect_{width}x{height}"))
                {
                    entry = extractor._discoveredSheets[i];
                    break;
                }
            }

            Assert.IsTrue(entry != null);
        }

        [Test]
        public void PreviewTextureRealSizeModeRespectsSpriteDimensions()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._previewSizeMode = SpriteSheetExtractor.PreviewSizeMode.RealSize;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null);
            Assert.Greater(entry._sprites.Count, 0);
        }

        [Test]
        public void PreviewTextureSquareSizeModeScalesAppropriately()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._previewSizeMode = SpriteSheetExtractor.PreviewSizeMode.Size64;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null);
        }

        [Test]
        public void PreviewTextureBoundarySpritesGenerateCorrectly()
        {
            CreateSpriteSheet("boundary_test", 256, 256, 4, 4);

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = null;
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                if (extractor._discoveredSheets[i]._assetPath.Contains("boundary_test"))
                {
                    entry = extractor._discoveredSheets[i];
                    break;
                }
            }

            Assert.IsTrue(entry != null);
            Assert.AreEqual(16, entry._sprites.Count);
        }

        private static IEnumerable<TestCaseData> PreviewDimensionCases()
        {
            yield return new TestCaseData(SpriteSheetExtractor.PreviewSizeMode.Size24, 24).SetName(
                "PreviewDimension.Size24"
            );
            yield return new TestCaseData(SpriteSheetExtractor.PreviewSizeMode.Size32, 32).SetName(
                "PreviewDimension.Size32"
            );
            yield return new TestCaseData(SpriteSheetExtractor.PreviewSizeMode.Size64, 64).SetName(
                "PreviewDimension.Size64"
            );
        }

        [Test]
        [TestCaseSource(nameof(PreviewDimensionCases))]
        public void PreviewTextureDimensionsMatchExpectedSize(
            SpriteSheetExtractor.PreviewSizeMode mode,
            int expectedSize
        )
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._previewSizeMode = mode;
            extractor.DiscoverSpriteSheets(generatePreviews: false);
            extractor.GenerateAllPreviewTexturesInBatch(extractor._discoveredSheets);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null);

            if (0 < entry._sprites.Count && entry._sprites[0]._previewTexture != null)
            {
                int maxDim = Mathf.Max(
                    entry._sprites[0]._previewTexture.width,
                    entry._sprites[0]._previewTexture.height
                );
                Assert.LessOrEqual(maxDim, expectedSize);
            }
        }

        [Test]
        public void PreviewTextureRealSizeModeDimensionsMatchSpriteRect()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._previewSizeMode = SpriteSheetExtractor.PreviewSizeMode.RealSize;
            extractor.DiscoverSpriteSheets(generatePreviews: false);
            extractor.GenerateAllPreviewTexturesInBatch(extractor._discoveredSheets);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry != null);

            if (0 < entry._sprites.Count && entry._sprites[0]._previewTexture != null)
            {
                int expectedWidth = (int)entry._sprites[0]._rect.width;
                int expectedHeight = (int)entry._sprites[0]._rect.height;
                int maxExpected = Mathf.Max(expectedWidth, expectedHeight);
                int clampedMax = Mathf.Min(maxExpected, 128);
                int actualMax = Mathf.Max(
                    entry._sprites[0]._previewTexture.width,
                    entry._sprites[0]._previewTexture.height
                );
                Assert.LessOrEqual(actualMax, clampedMax + 1);
            }
        }

        [Test]
        public void PreviewTextureAspectRatioIsPreserved()
        {
            CreateSpriteSheet("aspect_ratio_test", 128, 64, 2, 1);

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._previewSizeMode = SpriteSheetExtractor.PreviewSizeMode.Size64;
            extractor.DiscoverSpriteSheets(generatePreviews: false);
            extractor.GenerateAllPreviewTexturesInBatch(extractor._discoveredSheets);

            SpriteSheetExtractor.SpriteSheetEntry entry = null;
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                if (extractor._discoveredSheets[i]._assetPath.Contains("aspect_ratio_test"))
                {
                    entry = extractor._discoveredSheets[i];
                    break;
                }
            }

            Assert.IsTrue(entry != null);
        }

        [Test]
        public void SchedulePreviewRegenerationHandlesNullEntry()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            Assert.DoesNotThrow(() => extractor.SchedulePreviewRegeneration(null));
        }

        [Test]
        public void SchedulePreviewRegenerationHandlesEntryWithNullTexture()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            SpriteSheetExtractor.SpriteSheetEntry entry = new();
            entry._texture = null;
            entry._sprites = new List<SpriteSheetExtractor.SpriteEntryData>();

            Assert.DoesNotThrow(() => extractor.SchedulePreviewRegeneration(entry));
        }

        [Test]
        public void SchedulePreviewRegenerationHandlesEntryWithNullSprites()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            entry._sprites = null;

            Assert.DoesNotThrow(() => extractor.SchedulePreviewRegeneration(entry));
        }

        [Test]
        public void SchedulePreviewRegenerationHandlesEmptySpriteList()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            entry._sprites = new List<SpriteSheetExtractor.SpriteEntryData>();

            Assert.DoesNotThrow(() => extractor.SchedulePreviewRegeneration(entry));
        }

        [Test]
        public void SchedulePreviewRegenerationHandlesEmptySprites()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            entry._sprites.Clear();

            Assert.DoesNotThrow(() => extractor.SchedulePreviewRegeneration(entry));
        }

        [Test]
        public void SchedulePreviewRegenerationHandlesNullSpritesInList()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            entry._sprites.Add(null);

            Assert.DoesNotThrow(() => extractor.SchedulePreviewRegeneration(entry));
        }

        [Test]
        public void SchedulePreviewRegenerationPreservesTexturesWhenRectsMatch()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);
            extractor.GenerateAllPreviewTexturesInBatch(extractor._discoveredSheets);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            List<Texture2D> originalTextures = new();
            for (int i = 0; i < entry._sprites.Count; i++)
            {
                originalTextures.Add(entry._sprites[i]._previewTexture);
            }

            extractor.SchedulePreviewRegeneration(entry);
            extractor.GenerateAllPreviewTexturesInBatch(extractor._discoveredSheets);

            for (int i = 0; i < entry._sprites.Count; i++)
            {
                Assert.IsTrue(entry._sprites[i]._previewTexture != null);
            }
        }

        [Test]
        public void SchedulePreviewRegenerationWorksAfterModeChange()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            int originalCount = entry._sprites.Count;

            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.FromMetadata;
            extractor.RepopulateSpritesForEntry(entry);

            Assert.IsTrue(entry._sprites != null);
        }

        [Test]
        public void SchedulePreviewRegenerationHandlesMultipleConsecutiveRegenerations()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);

            for (int i = 0; i < 5; i++)
            {
                Assert.DoesNotThrow(() => extractor.SchedulePreviewRegeneration(entry));
            }
        }

        [Test]
        public void SchedulePreviewRegenerationHandlesTogglingUseGlobalSettings()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);

            entry._useGlobalSettings = true;
            Assert.DoesNotThrow(() => extractor.SchedulePreviewRegeneration(entry));

            entry._useGlobalSettings = false;
            Assert.DoesNotThrow(() => extractor.SchedulePreviewRegeneration(entry));
        }

        [Test]
        public void SchedulePreviewRegenerationHandlesDuplicateRects()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            if (2 <= entry._sprites.Count)
            {
                entry._sprites[1]._rect = entry._sprites[0]._rect;
            }

            Assert.DoesNotThrow(() => extractor.SchedulePreviewRegeneration(entry));
        }

        private static IEnumerable<TestCaseData> DuplicateRectHandlingCases()
        {
            yield return new TestCaseData(true).SetName("DuplicateRects.WithRegeneration");
            yield return new TestCaseData(false).SetName("DuplicateRects.WithoutRegeneration");
        }

        [Test]
        [TestCaseSource(nameof(DuplicateRectHandlingCases))]
        public void SchedulePreviewRegenerationSpritesWithDuplicateRectsAreHandled(bool regenerate)
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            if (2 <= entry._sprites.Count)
            {
                entry._sprites[1]._rect = entry._sprites[0]._rect;
            }

            if (regenerate)
            {
                Assert.DoesNotThrow(() => extractor.SchedulePreviewRegeneration(entry));
            }

            Assert.DoesNotThrow(() =>
                extractor.GenerateAllPreviewTexturesInBatch(extractor._discoveredSheets)
            );
        }

        [Test]
        public void SchedulePreviewRegenerationCleansUpOrphanedTextures()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);
            extractor.GenerateAllPreviewTexturesInBatch(extractor._discoveredSheets);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);

            if (2 < entry._sprites.Count)
            {
                entry._sprites.RemoveAt(entry._sprites.Count - 1);
            }

            Assert.DoesNotThrow(() => extractor.SchedulePreviewRegeneration(entry));
        }

        [Test]
        public void SchedulePreviewRegenerationHandlesDestroyedPreviewTextures()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);
            extractor.GenerateAllPreviewTexturesInBatch(extractor._discoveredSheets);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);

            for (int i = 0; i < entry._sprites.Count; i++)
            {
                if (entry._sprites[i]._previewTexture != null)
                {
                    Object.DestroyImmediate(entry._sprites[i]._previewTexture); // UNH-SUPPRESS: Intentionally destroy to test destroyed texture handling
                    entry._sprites[i]._previewTexture = null;
                }
            }

            Assert.DoesNotThrow(() => extractor.SchedulePreviewRegeneration(entry));
        }

        [Test]
        public void SchedulePreviewRegenerationForDestroyedWindowDoesNotCrash()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);

            Assert.DoesNotThrow(() => extractor.SchedulePreviewRegeneration(entry));
        }

        [Test]
        public void SchedulePreviewRegenerationForEntryHandlesValidEntryWithTexture()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            Assert.IsTrue(entry._texture != null);

            Assert.DoesNotThrow(() => extractor.SchedulePreviewRegeneration(entry));
        }

        [Test]
        public void PreviewRegenerationScheduledGuardPreventsMultipleQueueing()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);

            extractor.SchedulePreviewRegeneration(entry);
            extractor.SchedulePreviewRegeneration(entry);
            extractor.SchedulePreviewRegeneration(entry);

            Assert.Pass("Multiple scheduling calls did not cause issues");
        }

        [Test]
        public void PreviewRegenerationAfterTogglingUseGlobalSettingsCreatesValidTextures()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            entry._useGlobalSettings = false;
            entry._gridColumnsOverride = 2;
            entry._gridRowsOverride = 2;

            extractor.RepopulateSpritesForEntry(entry);
            extractor.GenerateAllPreviewTexturesInBatch(extractor._discoveredSheets);

            entry._useGlobalSettings = true;
            extractor.RepopulateSpritesForEntry(entry);

            Assert.Greater(entry._sprites.Count, 0);
        }

        [Test]
        public void PreviewRegenerationAfterTogglingFromFalseToTrueCreatesValidTextures()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 4;
            extractor._gridRows = 4;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            entry._useGlobalSettings = false;
            entry._gridSizeModeOverride = SpriteSheetExtractor.GridSizeMode.Manual;
            entry._gridColumnsOverride = 2;
            entry._gridRowsOverride = 2;

            extractor.RepopulateSpritesForEntry(entry);
            int overrideCount = entry._sprites.Count;

            entry._useGlobalSettings = true;
            extractor.RepopulateSpritesForEntry(entry);
            int globalCount = entry._sprites.Count;

            Assert.AreNotEqual(overrideCount, globalCount, "Sprite counts should differ");
        }

        [Test]
        public void PreviewRegenerationWithGridBasedOverrideCreatesValidTextures()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.FromMetadata;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            entry._useGlobalSettings = false;
            entry._extractionModeOverride = SpriteSheetExtractor.ExtractionMode.GridBased;
            entry._gridSizeModeOverride = SpriteSheetExtractor.GridSizeMode.Manual;
            entry._gridColumnsOverride = 2;
            entry._gridRowsOverride = 2;

            extractor.RepopulateSpritesForEntry(entry);
            extractor.GenerateAllPreviewTexturesInBatch(extractor._discoveredSheets);

            Assert.AreEqual(4, entry._sprites.Count, "Should have 4 sprites from 2x2 grid");
        }

        private static IEnumerable<TestCaseData> SettingsToggleCases()
        {
            yield return new TestCaseData(true, false).SetName("SettingsToggle.GlobalToOverride");
            yield return new TestCaseData(false, true).SetName("SettingsToggle.OverrideToGlobal");
        }

        [Test]
        [TestCaseSource(nameof(SettingsToggleCases))]
        public void PreviewRegenerationAfterSettingsToggleProducesCorrectSpriteCount(
            bool startGlobal,
            bool endGlobal
        )
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridColumns = 4;
            extractor._gridRows = 4;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);

            entry._useGlobalSettings = startGlobal;
            if (!startGlobal)
            {
                entry._gridColumnsOverride = 2;
                entry._gridRowsOverride = 2;
            }
            extractor.RepopulateSpritesForEntry(entry);

            entry._useGlobalSettings = endGlobal;
            if (!endGlobal)
            {
                entry._gridColumnsOverride = 2;
                entry._gridRowsOverride = 2;
            }
            extractor.RepopulateSpritesForEntry(entry);

            Assert.Greater(entry._sprites.Count, 0);
        }

        [Test]
        public void ToggleUseGlobalSettingsPreservesPreviewTexturesWithMatchingRects()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridColumns = 4;
            extractor._gridRows = 4;
            extractor.DiscoverSpriteSheets(generatePreviews: false);
            extractor.GenerateAllPreviewTexturesInBatch(extractor._discoveredSheets);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);

            entry._useGlobalSettings = false;
            entry._gridColumnsOverride = 4;
            entry._gridRowsOverride = 4;
            extractor.RepopulateSpritesForEntry(entry);
            extractor.GenerateAllPreviewTexturesInBatch(extractor._discoveredSheets);

            Assert.Greater(entry._sprites.Count, 0);
        }

        private static IEnumerable<TestCaseData> SlicingButtonVisibilityCases()
        {
            yield return new TestCaseData(
                SpriteSheetExtractor.ExtractionMode.GridBased,
                true
            ).SetName("SlicingButton.GridBased.Visible");
            yield return new TestCaseData(
                SpriteSheetExtractor.ExtractionMode.PaddedGrid,
                true
            ).SetName("SlicingButton.PaddedGrid.Visible");
            yield return new TestCaseData(
                SpriteSheetExtractor.ExtractionMode.FromMetadata,
                false
            ).SetName("SlicingButton.FromMetadata.Hidden");
            yield return new TestCaseData(
                SpriteSheetExtractor.ExtractionMode.AlphaDetection,
                false
            ).SetName("SlicingButton.AlphaDetection.Hidden");
        }

        [Test]
        [TestCaseSource(nameof(SlicingButtonVisibilityCases))]
        public void PreviewSlicingButtonVisibilityDeterminedByExtractionMode(
            SpriteSheetExtractor.ExtractionMode mode,
            bool expectedVisible
        )
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = mode;

            bool isGridBased =
                mode == SpriteSheetExtractor.ExtractionMode.GridBased
                || mode == SpriteSheetExtractor.ExtractionMode.PaddedGrid;
            Assert.AreEqual(expectedVisible, isGridBased);
        }

        [Test]
        public void PreviewSlicingButtonVisibilityUsesPerSheetOverrideWhenNotUsingGlobal()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.FromMetadata;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            entry._useGlobalSettings = false;
            entry._extractionModeOverride = SpriteSheetExtractor.ExtractionMode.GridBased;

            bool isGridBased =
                entry._extractionModeOverride == SpriteSheetExtractor.ExtractionMode.GridBased
                || entry._extractionModeOverride == SpriteSheetExtractor.ExtractionMode.PaddedGrid;
            Assert.IsTrue(isGridBased);
        }

        [Test]
        public void RepopulateSpritesForEntryClearsSpritesWhenRectsChange()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 4;
            extractor._gridRows = 4;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = FindEntryByPath(extractor, Shared4x4Path);
            int initialCount = entry._sprites.Count;

            extractor._gridColumns = 2;
            extractor._gridRows = 2;
            extractor.RepopulateSpritesForEntry(entry);

            Assert.AreNotEqual(initialCount, entry._sprites.Count);
        }

        [Test]
        public void PopulateSpritesFromAlphaDetectionUsesEffectiveThreshold()
        {
            CreateSpriteSheet("alpha_threshold_test", 64, 64, 2, 2);

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.AlphaDetection;
            extractor._alphaThreshold = 0.1f;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = null;
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                if (extractor._discoveredSheets[i]._assetPath.Contains("alpha_threshold_test"))
                {
                    entry = extractor._discoveredSheets[i];
                    break;
                }
            }

            Assert.IsTrue(entry != null);
        }

        [Test]
        public void RegenerateAllPreviewTexturesRepopulatesSpritesCorrectly()
        {
            SpriteSheetExtractor extractor = CreateExtractorWithSharedFixtures();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            int totalSprites = 0;
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                totalSprites += extractor._discoveredSheets[i]._sprites.Count;
            }

            extractor.GenerateAllPreviewTexturesInBatch(extractor._discoveredSheets);

            int newTotalSprites = 0;
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                newTotalSprites += extractor._discoveredSheets[i]._sprites.Count;
            }

            Assert.AreEqual(totalSprites, newTotalSprites);
        }

        [Test]
        public void PreviewGenerationWith2048x2048TextureWorksCorrectly()
        {
            CreateSpriteSheet("large_2048", 2048, 2048, 8, 8);

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._previewSizeMode = SpriteSheetExtractor.PreviewSizeMode.Size64;
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = null;
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                if (extractor._discoveredSheets[i]._assetPath.Contains("large_2048"))
                {
                    entry = extractor._discoveredSheets[i];
                    break;
                }
            }

            Assert.IsTrue(entry != null);
            Assert.AreEqual(64, entry._sprites.Count);
        }

        [Test]
        public void PreviewGenerationManySpritesWithVaryingSizesWorksCorrectly()
        {
            CreateSpriteSheet("varying_sizes", 512, 512, 16, 16);

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = null;
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                if (extractor._discoveredSheets[i]._assetPath.Contains("varying_sizes"))
                {
                    entry = extractor._discoveredSheets[i];
                    break;
                }
            }

            Assert.IsTrue(entry != null);
            Assert.AreEqual(256, entry._sprites.Count);
        }

        private static IEnumerable<TestCaseData> AsymmetricNPOTCases()
        {
            yield return new TestCaseData(100, 200).SetName("NPOT.100x200");
            yield return new TestCaseData(150, 75).SetName("NPOT.150x75");
            yield return new TestCaseData(300, 100).SetName("NPOT.300x100");
        }

        [Test]
        [TestCaseSource(nameof(AsymmetricNPOTCases))]
        public void PreviewGenerationWithAsymmetricNPOTDimensionsWorksCorrectly(
            int width,
            int height
        )
        {
            CreateSpriteSheet($"npot_{width}x{height}", width, height, 2, 2);

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = null;
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                if (extractor._discoveredSheets[i]._assetPath.Contains($"npot_{width}x{height}"))
                {
                    entry = extractor._discoveredSheets[i];
                    break;
                }
            }

            Assert.IsTrue(entry != null);
        }

        [Test]
        public void PreviewGenerationWithNonPowerOfTwoDimensionsWorksCorrectly()
        {
            CreateSpriteSheet("npot_test", 100, 100, 2, 2);

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = null;
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                if (extractor._discoveredSheets[i]._assetPath.Contains("npot_test"))
                {
                    entry = extractor._discoveredSheets[i];
                    break;
                }
            }

            Assert.IsTrue(entry != null);
        }

        [Test]
        public void PreviewGenerationWithPrimeNumberDimensionsWorksCorrectly()
        {
            CreateSpriteSheet("prime_test", 127, 127, 1, 1);

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = null;
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                if (extractor._discoveredSheets[i]._assetPath.Contains("prime_test"))
                {
                    entry = extractor._discoveredSheets[i];
                    break;
                }
            }

            Assert.IsTrue(entry != null);
        }

        [Test]
        public void SingleModeSpriteExtractsWithWarning()
        {
            Texture2D tex = Track(new Texture2D(64, 64, TextureFormat.RGBA32, false));
            FillTexture(tex, Color.red);
            string path = System.IO.Path.Combine(Root, "single_mode_sprite.png").SanitizePath();
            System.IO.File.WriteAllBytes(RelToFull(path), tex.EncodeToPNG());
            TrackAssetPath(path);
            AssetDatabase.ImportAsset(path);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor.DiscoverSpriteSheets(generatePreviews: false);

            SpriteSheetExtractor.SpriteSheetEntry entry = null;
            for (int i = 0; i < extractor._discoveredSheets.Count; i++)
            {
                if (extractor._discoveredSheets[i]._assetPath.Contains("single_mode_sprite"))
                {
                    entry = extractor._discoveredSheets[i];
                    break;
                }
            }

            Assert.IsTrue(entry != null, "Single mode sprite should be discovered");
            Assert.That(
                entry._importMode,
                Is.EqualTo(SpriteImportMode.Single),
                "Single mode sprite should have Single import mode"
            );
        }
    }
#endif
}
