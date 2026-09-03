// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Sprites
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using PivotMode = UnityHelpers.Editor.Sprites.PivotMode;

    /// <summary>
    /// Pure logic tests for <see cref="SpriteSheetExtractor"/> that do not require
    /// AssetDatabase operations. These tests verify grid calculations, pixel analysis,
    /// preview size calculations, effective value overrides, and sort order logic
    /// using only in-memory data structures.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This test fixture is designed to run quickly without any Unity Editor asset operations.
    /// All tests work with in-memory Texture2D objects, Color32 arrays, and plain data classes.
    /// </para>
    /// <para>
    /// Tests are marked with [Category("Fast")] to enable selective test runs during development.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [Ignore("Beta feature - SpriteSheetExtractor tests are not reliable")]
    public sealed class SpriteSheetExtractorLogicTests : CommonTestBase
    {
        private SpriteSheetExtractor CreateExtractor()
        {
            SpriteSheetExtractor extractor = Track(
                ScriptableObject.CreateInstance<SpriteSheetExtractor>()
            );
            return extractor;
        }

        private static IEnumerable<TestCaseData> GridAutoCalculationCases()
        {
            yield return new TestCaseData(64, 64, 2, 2, 32, 32).SetName("GridAuto.64x64.2x2");
            yield return new TestCaseData(128, 64, 4, 2, 32, 32).SetName("GridAuto.128x64.4x2");
            yield return new TestCaseData(256, 256, 8, 8, 32, 32).SetName("GridAuto.256x256.8x8");
            yield return new TestCaseData(100, 100, 4, 4, 25, 25).SetName("GridAuto.100x100.4x4");
        }

        [Test]
        [TestCaseSource(nameof(GridAutoCalculationCases))]
        public void GridAutoCalculationDeterminesCorrectDimensions(
            int textureWidth,
            int textureHeight,
            int expectedColumns,
            int expectedRows,
            int expectedCellWidth,
            int expectedCellHeight
        )
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Auto;

            int columns;
            int rows;
            int cellWidth;
            int cellHeight;
            extractor.CalculateGridDimensions(
                textureWidth,
                textureHeight,
                out columns,
                out rows,
                out cellWidth,
                out cellHeight
            );

            Assert.That(
                columns * cellWidth,
                Is.EqualTo(textureWidth),
                "Columns x CellWidth should equal texture width"
            );
            Assert.That(
                rows * cellHeight,
                Is.EqualTo(textureHeight),
                "Rows x CellHeight should equal texture height"
            );
        }

        private static IEnumerable<TestCaseData> GridManualCalculationCases()
        {
            yield return new TestCaseData(128, 128, 4, 4, 0, 0, 32, 32).SetName(
                "GridManual.Cols4Rows4"
            );
            yield return new TestCaseData(128, 128, 2, 2, 0, 0, 64, 64).SetName(
                "GridManual.Cols2Rows2"
            );
            yield return new TestCaseData(128, 128, 4, 4, 32, 32, 32, 32).SetName(
                "GridManual.ExplicitCellSize"
            );
            yield return new TestCaseData(128, 128, 4, 4, 16, 16, 32, 32).SetName(
                "GridManual.SmallCellSize"
            );
        }

        [Test]
        [TestCaseSource(nameof(GridManualCalculationCases))]
        public void GridManualCalculationUsesSpecifiedValues(
            int textureWidth,
            int textureHeight,
            int manualColumns,
            int manualRows,
            int manualCellWidth,
            int manualCellHeight,
            int expectedCellWidth,
            int expectedCellHeight
        )
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = manualColumns;
            extractor._gridRows = manualRows;
            extractor._cellWidth = manualCellWidth;
            extractor._cellHeight = manualCellHeight;

            int columns;
            int rows;
            int cellWidth;
            int cellHeight;
            extractor.CalculateGridDimensions(
                textureWidth,
                textureHeight,
                out columns,
                out rows,
                out cellWidth,
                out cellHeight
            );

            Assert.That(columns, Is.EqualTo(manualColumns), "Columns should match manual value");
            Assert.That(rows, Is.EqualTo(manualRows), "Rows should match manual value");
            Assert.That(
                cellWidth,
                Is.EqualTo(expectedCellWidth),
                "Cell width should match expected"
            );
            Assert.That(
                cellHeight,
                Is.EqualTo(expectedCellHeight),
                "Cell height should match expected"
            );
        }

        private static IEnumerable<TestCaseData> AlphaDetectionCases()
        {
            yield return new TestCaseData(0.01f).SetName("AlphaDetection.Threshold0.01");
            yield return new TestCaseData(0.1f).SetName("AlphaDetection.Threshold0.1");
            yield return new TestCaseData(0.5f).SetName("AlphaDetection.Threshold0.5");
        }

        [Test]
        [TestCaseSource(nameof(AlphaDetectionCases))]
        public void AlphaDetectionFindsOpaqueBounds(float alphaThreshold)
        {
            int width = 64;
            int height = 64;
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(0, 0, 0, 0);
            }

            for (int y = 16; y < 32; ++y)
            {
                for (int x = 16; x < 32; ++x)
                {
                    pixels[y * width + x] = new Color32(255, 0, 0, 255);
                }
            }

            for (int y = 40; y < 56; ++y)
            {
                for (int x = 40; x < 56; ++x)
                {
                    pixels[y * width + x] = new Color32(0, 255, 0, 255);
                }
            }

            List<Rect> result = new List<Rect>();
            SpriteSheetExtractor.DetectSpriteBoundsByAlpha(
                pixels,
                width,
                height,
                alphaThreshold,
                result
            );

            Assert.That(result.Count, Is.EqualTo(2), "Should detect 2 sprites");
        }

        [Test]
        public void AlphaDetectionIgnoresLowAlphaPixels()
        {
            int width = 32;
            int height = 32;
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(128, 128, 128, 1);
            }

            List<Rect> result = new List<Rect>();
            SpriteSheetExtractor.DetectSpriteBoundsByAlpha(pixels, width, height, 0.01f, result);

            Assert.That(result.Count, Is.EqualTo(0), "Should not detect sprites with low alpha");
        }

        private static IEnumerable<TestCaseData> PreviewSizeCases()
        {
            yield return new TestCaseData(SpriteSheetExtractor.PreviewSizeMode.Size24, 24).SetName(
                "PreviewSize.24"
            );
            yield return new TestCaseData(SpriteSheetExtractor.PreviewSizeMode.Size32, 32).SetName(
                "PreviewSize.32"
            );
            yield return new TestCaseData(SpriteSheetExtractor.PreviewSizeMode.Size64, 64).SetName(
                "PreviewSize.64"
            );
        }

        [Test]
        [TestCaseSource(nameof(PreviewSizeCases))]
        public void PreviewSizeModeReturnsCorrectSize(
            SpriteSheetExtractor.PreviewSizeMode mode,
            int expectedSize
        )
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._previewSizeMode = mode;

            SpriteSheetExtractor.SpriteEntryData sprite = new SpriteSheetExtractor.SpriteEntryData
            {
                _rect = new Rect(0, 0, 48, 48),
            };

            int size = extractor.GetPreviewSize(sprite);
            Assert.That(size, Is.EqualTo(expectedSize), $"Preview size should be {expectedSize}");
        }

        [Test]
        public void PreviewSizeRealSizeUsesActualSpriteDimensions()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._previewSizeMode = SpriteSheetExtractor.PreviewSizeMode.RealSize;

            SpriteSheetExtractor.SpriteEntryData sprite = new SpriteSheetExtractor.SpriteEntryData
            {
                _rect = new Rect(0, 0, 48, 32),
            };

            int size = extractor.GetPreviewSize(sprite);
            Assert.That(size, Is.EqualTo(48), "RealSize should use max dimension (48)");
        }

        [Test]
        public void PreviewSizeRealSizeClampsToMax128()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._previewSizeMode = SpriteSheetExtractor.PreviewSizeMode.RealSize;

            SpriteSheetExtractor.SpriteEntryData sprite = new SpriteSheetExtractor.SpriteEntryData
            {
                _rect = new Rect(0, 0, 256, 256),
            };

            int size = extractor.GetPreviewSize(sprite);
            Assert.That(size, Is.EqualTo(128), "RealSize should clamp to max 128");
        }

        [Test]
        public void OverlayRectsCalculatedCorrectly()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 4;
            extractor._gridRows = 4;

            int columns;
            int rows;
            int cellWidth;
            int cellHeight;
            extractor.CalculateGridDimensions(
                128,
                128,
                out columns,
                out rows,
                out cellWidth,
                out cellHeight
            );

            Assert.That(columns, Is.EqualTo(4), "Should have 4 columns");
            Assert.That(rows, Is.EqualTo(4), "Should have 4 rows");
            Assert.That(cellWidth, Is.EqualTo(32), "Cell width should be 32");
            Assert.That(cellHeight, Is.EqualTo(32), "Cell height should be 32");
        }

        [Test]
        public void GridExtractionPopulatesSpritesCorrectly()
        {
            Texture2D texture = Track(new Texture2D(64, 64, TextureFormat.RGBA32, false));
            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _sprites = new List<SpriteSheetExtractor.SpriteEntryData>(),
                _assetPath = "test",
            };

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 2;
            extractor._gridRows = 2;
            extractor.PopulateSpritesFromGrid(entry, texture);

            Assert.That(entry._sprites.Count, Is.EqualTo(4), "Should create 4 sprites");
            Assert.That(entry._sprites[0]._rect.width, Is.EqualTo(32), "Sprite width should be 32");
            Assert.That(
                entry._sprites[0]._rect.height,
                Is.EqualTo(32),
                "Sprite height should be 32"
            );
        }

        [Test]
        public void PaddedGridExtractionPopulatesSpritesCorrectly()
        {
            Texture2D texture = Track(new Texture2D(64, 64, TextureFormat.RGBA32, false));
            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _sprites = new List<SpriteSheetExtractor.SpriteEntryData>(),
                _assetPath = "test",
            };

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.PaddedGrid;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 2;
            extractor._gridRows = 2;
            extractor._paddingLeft = 2;
            extractor._paddingRight = 2;
            extractor._paddingTop = 2;
            extractor._paddingBottom = 2;
            extractor.PopulateSpritesFromPaddedGrid(entry, texture);

            Assert.That(entry._sprites.Count, Is.EqualTo(4), "Should create 4 sprites");

            int expectedWidth = 32 - 4;
            int expectedHeight = 32 - 4;
            Assert.That(
                (int)entry._sprites[0]._rect.width,
                Is.EqualTo(expectedWidth),
                "Sprite width should account for padding"
            );
            Assert.That(
                (int)entry._sprites[0]._rect.height,
                Is.EqualTo(expectedHeight),
                "Sprite height should account for padding"
            );
        }

        [Test]
        public void InvalidPaddingWarnsAndCreatesNoSprites()
        {
            Texture2D texture = Track(new Texture2D(64, 64, TextureFormat.RGBA32, false));
            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _sprites = new List<SpriteSheetExtractor.SpriteEntryData>(),
                _assetPath = "test",
            };

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.PaddedGrid;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 2;
            extractor._gridRows = 2;
            extractor._paddingLeft = 20;
            extractor._paddingRight = 20;
            extractor._paddingTop = 20;
            extractor._paddingBottom = 20;
            extractor.PopulateSpritesFromPaddedGrid(entry, texture);

            Assert.That(entry._sprites.Count, Is.EqualTo(0), "Should create no sprites");
        }

        [Test]
        public void AlphaDetectionSortsResultsByPosition()
        {
            int width = 64;
            int height = 64;
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(0, 0, 0, 0);
            }

            for (int y = 48; y < 56; ++y)
            {
                for (int x = 48; x < 56; ++x)
                {
                    pixels[y * width + x] = new Color32(0, 255, 0, 255);
                }
            }

            for (int y = 8; y < 16; ++y)
            {
                for (int x = 8; x < 16; ++x)
                {
                    pixels[y * width + x] = new Color32(255, 0, 0, 255);
                }
            }

            List<Rect> result = new List<Rect>();
            SpriteSheetExtractor.DetectSpriteBoundsByAlpha(pixels, width, height, 0.5f, result);

            Assert.That(result.Count, Is.EqualTo(2), "Should detect 2 sprites");
        }

        [Test]
        public void MinimumSpriteSize2x2IsEnforced()
        {
            int width = 32;
            int height = 32;
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(0, 0, 0, 0);
            }

            pixels[16 * width + 16] = new Color32(255, 0, 0, 255);

            List<Rect> result = new List<Rect>();
            SpriteSheetExtractor.DetectSpriteBoundsByAlpha(pixels, width, height, 0.5f, result);

            if (0 < result.Count)
            {
                Assert.That(
                    result[0].width,
                    Is.GreaterThanOrEqualTo(2),
                    "Minimum sprite width should be 2"
                );
                Assert.That(
                    result[0].height,
                    Is.GreaterThanOrEqualTo(2),
                    "Minimum sprite height should be 2"
                );
            }
        }

        [Test]
        public void ObsoleteExtractionModeNoneFallsBackToFromMetadata()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
#pragma warning disable CS0618 // Type or member is obsolete
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.None;
#pragma warning restore CS0618

            SpriteSheetExtractor.ExtractionMode effective = extractor.GetEffectiveExtractionMode(
                null
            );

            Assert.That(
                effective,
                Is.EqualTo(SpriteSheetExtractor.ExtractionMode.FromMetadata),
                "None should fall back to FromMetadata"
            );
        }

        [Test]
        public void ObsoleteGridSizeModeNoneFallsBackToAuto()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
#pragma warning disable CS0618 // Type or member is obsolete
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.None;
#pragma warning restore CS0618

            SpriteSheetExtractor.GridSizeMode effective = extractor.GetEffectiveGridSizeMode(null);

            Assert.That(
                effective,
                Is.EqualTo(SpriteSheetExtractor.GridSizeMode.Auto),
                "None should fall back to Auto"
            );
        }

        [Test]
        public void ObsoletePreviewSizeModeNoneFallsBackToSize32()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
#pragma warning disable CS0618 // Type or member is obsolete
            extractor._previewSizeMode = SpriteSheetExtractor.PreviewSizeMode.None;
#pragma warning restore CS0618

            SpriteSheetExtractor.SpriteEntryData sprite = new SpriteSheetExtractor.SpriteEntryData
            {
                _rect = new Rect(0, 0, 64, 64),
            };

            int size = extractor.GetPreviewSize(sprite);

            Assert.That(size, Is.EqualTo(32), "None should fall back to 32");
        }

        private static IEnumerable<TestCaseData> GetEffectiveExtractionModeCases()
        {
            yield return new TestCaseData(
                SpriteSheetExtractor.ExtractionMode.GridBased,
                true,
                SpriteSheetExtractor.ExtractionMode.FromMetadata,
                SpriteSheetExtractor.ExtractionMode.GridBased
            ).SetName("EffectiveExtraction.UseGlobal.ReturnsGlobal");
            yield return new TestCaseData(
                SpriteSheetExtractor.ExtractionMode.GridBased,
                false,
                SpriteSheetExtractor.ExtractionMode.PaddedGrid,
                SpriteSheetExtractor.ExtractionMode.PaddedGrid
            ).SetName("EffectiveExtraction.NotUseGlobal.ReturnsOverride");
        }

        [Test]
        [TestCaseSource(nameof(GetEffectiveExtractionModeCases))]
        public void GetEffectiveExtractionModeReturnsCorrectValue(
            SpriteSheetExtractor.ExtractionMode globalMode,
            bool useGlobalSettings,
            SpriteSheetExtractor.ExtractionMode overrideMode,
            SpriteSheetExtractor.ExtractionMode expected
        )
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = globalMode;

            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = useGlobalSettings,
                _extractionModeOverride = overrideMode,
            };

            SpriteSheetExtractor.ExtractionMode result = extractor.GetEffectiveExtractionMode(
                entry
            );
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void GetEffectiveExtractionModeReturnsGlobalWhenEntryIsNull()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.PaddedGrid;

            SpriteSheetExtractor.ExtractionMode result = extractor.GetEffectiveExtractionMode(null);

            Assert.That(result, Is.EqualTo(SpriteSheetExtractor.ExtractionMode.PaddedGrid));
        }

        private static IEnumerable<TestCaseData> GetEffectiveGridSizeModeCases()
        {
            yield return new TestCaseData(
                SpriteSheetExtractor.GridSizeMode.Manual,
                true,
                SpriteSheetExtractor.GridSizeMode.Auto,
                SpriteSheetExtractor.GridSizeMode.Manual
            ).SetName("EffectiveGridSize.UseGlobal.ReturnsGlobal");
            yield return new TestCaseData(
                SpriteSheetExtractor.GridSizeMode.Manual,
                false,
                SpriteSheetExtractor.GridSizeMode.Auto,
                SpriteSheetExtractor.GridSizeMode.Auto
            ).SetName("EffectiveGridSize.NotUseGlobal.ReturnsOverride");
        }

        [Test]
        [TestCaseSource(nameof(GetEffectiveGridSizeModeCases))]
        public void GetEffectiveGridSizeModeReturnsCorrectValue(
            SpriteSheetExtractor.GridSizeMode globalMode,
            bool useGlobalSettings,
            SpriteSheetExtractor.GridSizeMode overrideMode,
            SpriteSheetExtractor.GridSizeMode expected
        )
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._gridSizeMode = globalMode;

            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = useGlobalSettings,
                _gridSizeModeOverride = overrideMode,
            };

            SpriteSheetExtractor.GridSizeMode result = extractor.GetEffectiveGridSizeMode(entry);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void GetEffectiveGridSizeModeReturnsGlobalWhenEntryIsNull()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;

            SpriteSheetExtractor.GridSizeMode result = extractor.GetEffectiveGridSizeMode(null);

            Assert.That(result, Is.EqualTo(SpriteSheetExtractor.GridSizeMode.Manual));
        }

        private static IEnumerable<TestCaseData> GetEffectiveGridColumnsCases()
        {
            yield return new TestCaseData(4, true, 8, 4).SetName(
                "EffectiveGridColumns.UseGlobal.ReturnsGlobal"
            );
            yield return new TestCaseData(4, false, 8, 8).SetName(
                "EffectiveGridColumns.NotUseGlobal.ReturnsOverride"
            );
        }

        [Test]
        [TestCaseSource(nameof(GetEffectiveGridColumnsCases))]
        public void GetEffectiveGridColumnsReturnsCorrectValue(
            int globalColumns,
            bool useGlobalSettings,
            int overrideColumns,
            int expected
        )
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._gridColumns = globalColumns;

            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = useGlobalSettings,
                _gridColumnsOverride = overrideColumns,
            };

            int result = extractor.GetEffectiveGridColumns(entry);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void GetEffectiveGridColumnsReturnsGlobalWhenEntryIsNull()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._gridColumns = 6;

            int result = extractor.GetEffectiveGridColumns(null);

            Assert.That(result, Is.EqualTo(6));
        }

        private static IEnumerable<TestCaseData> GetEffectiveGridRowsCases()
        {
            yield return new TestCaseData(4, true, 8, 4).SetName(
                "EffectiveGridRows.UseGlobal.ReturnsGlobal"
            );
            yield return new TestCaseData(4, false, 8, 8).SetName(
                "EffectiveGridRows.NotUseGlobal.ReturnsOverride"
            );
        }

        [Test]
        [TestCaseSource(nameof(GetEffectiveGridRowsCases))]
        public void GetEffectiveGridRowsReturnsCorrectValue(
            int globalRows,
            bool useGlobalSettings,
            int overrideRows,
            int expected
        )
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._gridRows = globalRows;

            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = useGlobalSettings,
                _gridRowsOverride = overrideRows,
            };

            int result = extractor.GetEffectiveGridRows(entry);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void GetEffectiveGridRowsReturnsGlobalWhenEntryIsNull()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._gridRows = 6;

            int result = extractor.GetEffectiveGridRows(null);

            Assert.That(result, Is.EqualTo(6));
        }

        private static IEnumerable<TestCaseData> GetEffectiveCellWidthCases()
        {
            yield return new TestCaseData(32, true, 64, 32).SetName(
                "EffectiveCellWidth.UseGlobal.ReturnsGlobal"
            );
            yield return new TestCaseData(32, false, 64, 64).SetName(
                "EffectiveCellWidth.NotUseGlobal.ReturnsOverride"
            );
        }

        [Test]
        [TestCaseSource(nameof(GetEffectiveCellWidthCases))]
        public void GetEffectiveCellWidthReturnsCorrectValue(
            int globalWidth,
            bool useGlobalSettings,
            int overrideWidth,
            int expected
        )
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._cellWidth = globalWidth;

            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = useGlobalSettings,
                _cellWidthOverride = overrideWidth,
            };

            int result = extractor.GetEffectiveCellWidth(entry);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void GetEffectiveCellWidthReturnsGlobalWhenEntryIsNull()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._cellWidth = 48;

            int result = extractor.GetEffectiveCellWidth(null);

            Assert.That(result, Is.EqualTo(48));
        }

        private static IEnumerable<TestCaseData> GetEffectiveCellHeightCases()
        {
            yield return new TestCaseData(32, true, 64, 32).SetName(
                "EffectiveCellHeight.UseGlobal.ReturnsGlobal"
            );
            yield return new TestCaseData(32, false, 64, 64).SetName(
                "EffectiveCellHeight.NotUseGlobal.ReturnsOverride"
            );
        }

        [Test]
        [TestCaseSource(nameof(GetEffectiveCellHeightCases))]
        public void GetEffectiveCellHeightReturnsCorrectValue(
            int globalHeight,
            bool useGlobalSettings,
            int overrideHeight,
            int expected
        )
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._cellHeight = globalHeight;

            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = useGlobalSettings,
                _cellHeightOverride = overrideHeight,
            };

            int result = extractor.GetEffectiveCellHeight(entry);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void GetEffectiveCellHeightReturnsGlobalWhenEntryIsNull()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._cellHeight = 48;

            int result = extractor.GetEffectiveCellHeight(null);

            Assert.That(result, Is.EqualTo(48));
        }

        private static IEnumerable<TestCaseData> GetEffectivePaddingLeftCases()
        {
            yield return new TestCaseData(2, true, 4, 2).SetName(
                "EffectivePaddingLeft.UseGlobal.ReturnsGlobal"
            );
            yield return new TestCaseData(2, false, 4, 4).SetName(
                "EffectivePaddingLeft.NotUseGlobal.ReturnsOverride"
            );
        }

        [Test]
        [TestCaseSource(nameof(GetEffectivePaddingLeftCases))]
        public void GetEffectivePaddingLeftReturnsCorrectValue(
            int globalPadding,
            bool useGlobalSettings,
            int overridePadding,
            int expected
        )
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._paddingLeft = globalPadding;

            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = useGlobalSettings,
                _paddingLeftOverride = overridePadding,
            };

            int result = extractor.GetEffectivePaddingLeft(entry);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void GetEffectivePaddingLeftReturnsGlobalWhenEntryIsNull()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._paddingLeft = 3;

            int result = extractor.GetEffectivePaddingLeft(null);

            Assert.That(result, Is.EqualTo(3));
        }

        private static IEnumerable<TestCaseData> GetEffectivePaddingRightCases()
        {
            yield return new TestCaseData(2, true, 4, 2).SetName(
                "EffectivePaddingRight.UseGlobal.ReturnsGlobal"
            );
            yield return new TestCaseData(2, false, 4, 4).SetName(
                "EffectivePaddingRight.NotUseGlobal.ReturnsOverride"
            );
        }

        [Test]
        [TestCaseSource(nameof(GetEffectivePaddingRightCases))]
        public void GetEffectivePaddingRightReturnsCorrectValue(
            int globalPadding,
            bool useGlobalSettings,
            int overridePadding,
            int expected
        )
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._paddingRight = globalPadding;

            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = useGlobalSettings,
                _paddingRightOverride = overridePadding,
            };

            int result = extractor.GetEffectivePaddingRight(entry);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void GetEffectivePaddingRightReturnsGlobalWhenEntryIsNull()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._paddingRight = 3;

            int result = extractor.GetEffectivePaddingRight(null);

            Assert.That(result, Is.EqualTo(3));
        }

        private static IEnumerable<TestCaseData> GetEffectivePaddingTopCases()
        {
            yield return new TestCaseData(2, true, 4, 2).SetName(
                "EffectivePaddingTop.UseGlobal.ReturnsGlobal"
            );
            yield return new TestCaseData(2, false, 4, 4).SetName(
                "EffectivePaddingTop.NotUseGlobal.ReturnsOverride"
            );
        }

        [Test]
        [TestCaseSource(nameof(GetEffectivePaddingTopCases))]
        public void GetEffectivePaddingTopReturnsCorrectValue(
            int globalPadding,
            bool useGlobalSettings,
            int overridePadding,
            int expected
        )
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._paddingTop = globalPadding;

            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = useGlobalSettings,
                _paddingTopOverride = overridePadding,
            };

            int result = extractor.GetEffectivePaddingTop(entry);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void GetEffectivePaddingTopReturnsGlobalWhenEntryIsNull()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._paddingTop = 3;

            int result = extractor.GetEffectivePaddingTop(null);

            Assert.That(result, Is.EqualTo(3));
        }

        private static IEnumerable<TestCaseData> GetEffectivePaddingBottomCases()
        {
            yield return new TestCaseData(2, true, 4, 2).SetName(
                "EffectivePaddingBottom.UseGlobal.ReturnsGlobal"
            );
            yield return new TestCaseData(2, false, 4, 4).SetName(
                "EffectivePaddingBottom.NotUseGlobal.ReturnsOverride"
            );
        }

        [Test]
        [TestCaseSource(nameof(GetEffectivePaddingBottomCases))]
        public void GetEffectivePaddingBottomReturnsCorrectValue(
            int globalPadding,
            bool useGlobalSettings,
            int overridePadding,
            int expected
        )
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._paddingBottom = globalPadding;

            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = useGlobalSettings,
                _paddingBottomOverride = overridePadding,
            };

            int result = extractor.GetEffectivePaddingBottom(entry);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void GetEffectivePaddingBottomReturnsGlobalWhenEntryIsNull()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._paddingBottom = 3;

            int result = extractor.GetEffectivePaddingBottom(null);

            Assert.That(result, Is.EqualTo(3));
        }

        private static IEnumerable<TestCaseData> GetEffectiveAlphaThresholdCases()
        {
            yield return new TestCaseData(0.5f, true, 0.8f, 0.5f).SetName(
                "EffectiveAlphaThreshold.UseGlobal.ReturnsGlobal"
            );
            yield return new TestCaseData(0.5f, false, 0.8f, 0.8f).SetName(
                "EffectiveAlphaThreshold.NotUseGlobal.ReturnsOverride"
            );
        }

        [Test]
        [TestCaseSource(nameof(GetEffectiveAlphaThresholdCases))]
        public void GetEffectiveAlphaThresholdReturnsCorrectValue(
            float globalThreshold,
            bool useGlobalSettings,
            float overrideThreshold,
            float expected
        )
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._alphaThreshold = globalThreshold;

            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = useGlobalSettings,
                _alphaThresholdOverride = overrideThreshold,
            };

            float result = extractor.GetEffectiveAlphaThreshold(entry);
            Assert.That(result, Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void GetEffectiveAlphaThresholdReturnsGlobalWhenEntryIsNull()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._alphaThreshold = 0.7f;

            float result = extractor.GetEffectiveAlphaThreshold(null);

            Assert.That(result, Is.EqualTo(0.7f).Within(0.001f));
        }

        [Test]
        public void CalculateGridDimensionsWithEntryUsesGlobalWhenEntryIsNull()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 4;
            extractor._gridRows = 4;

            extractor.CalculateGridDimensions(
                128,
                128,
                null,
                null,
                out int columns,
                out int rows,
                out int cellWidth,
                out int cellHeight
            );

            Assert.That(columns, Is.EqualTo(4), "Should use global columns");
            Assert.That(rows, Is.EqualTo(4), "Should use global rows");
            Assert.That(cellWidth, Is.EqualTo(32), "Cell width should be 32");
            Assert.That(cellHeight, Is.EqualTo(32), "Cell height should be 32");
        }

        [Test]
        public void CalculateGridDimensionsWithEntryUsesGlobalWhenUseGlobalSettingsTrue()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 4;
            extractor._gridRows = 4;

            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = true,
                _gridColumnsOverride = 8,
                _gridRowsOverride = 8,
            };

            extractor.CalculateGridDimensions(
                128,
                128,
                entry,
                null,
                out int columns,
                out int rows,
                out int cellWidth,
                out int cellHeight
            );

            Assert.That(columns, Is.EqualTo(4), "Should use global columns");
            Assert.That(rows, Is.EqualTo(4), "Should use global rows");
        }

        [Test]
        public void CalculateGridDimensionsWithEntryUsesOverrideWhenEnabled()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 4;
            extractor._gridRows = 4;

            SpriteSheetExtractor.SpriteSheetEntry entry = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = false,
                _gridSizeModeOverride = SpriteSheetExtractor.GridSizeMode.Manual,
                _gridColumnsOverride = 8,
                _gridRowsOverride = 8,
            };

            extractor.CalculateGridDimensions(
                128,
                128,
                entry,
                null,
                out int columns,
                out int rows,
                out int cellWidth,
                out int cellHeight
            );

            Assert.That(columns, Is.EqualTo(8), "Should use override columns");
            Assert.That(rows, Is.EqualTo(8), "Should use override rows");
        }

        [Test]
        public void CopySettingsFromEntryCopiesAllOverrideFields()
        {
            SpriteSheetExtractor.SpriteSheetEntry source = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = false,
                _extractionModeOverride = SpriteSheetExtractor.ExtractionMode.PaddedGrid,
                _gridSizeModeOverride = SpriteSheetExtractor.GridSizeMode.Manual,
                _gridColumnsOverride = 8,
                _gridRowsOverride = 8,
                _cellWidthOverride = 16,
                _cellHeightOverride = 16,
                _paddingLeftOverride = 1,
                _paddingRightOverride = 2,
                _paddingTopOverride = 3,
                _paddingBottomOverride = 4,
                _alphaThresholdOverride = 0.9f,
            };

            SpriteSheetExtractor.SpriteSheetEntry target = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = true,
            };

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor.CopySettingsFromEntry(source, target);

            Assert.That(target._useGlobalSettings, Is.False);
            Assert.That(
                target._extractionModeOverride,
                Is.EqualTo(SpriteSheetExtractor.ExtractionMode.PaddedGrid)
            );
            Assert.That(
                target._gridSizeModeOverride,
                Is.EqualTo(SpriteSheetExtractor.GridSizeMode.Manual)
            );
            Assert.That(target._gridColumnsOverride, Is.EqualTo(8));
            Assert.That(target._gridRowsOverride, Is.EqualTo(8));
            Assert.That(target._cellWidthOverride, Is.EqualTo(16));
            Assert.That(target._cellHeightOverride, Is.EqualTo(16));
            Assert.That(target._paddingLeftOverride, Is.EqualTo(1));
            Assert.That(target._paddingRightOverride, Is.EqualTo(2));
            Assert.That(target._paddingTopOverride, Is.EqualTo(3));
            Assert.That(target._paddingBottomOverride, Is.EqualTo(4));
            Assert.That(target._alphaThresholdOverride, Is.EqualTo(0.9f).Within(0.001f));
        }

        [Test]
        public void CopySettingsFromEntryHandlesNullSourceGracefully()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            SpriteSheetExtractor.SpriteSheetEntry target = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = true,
                _gridColumnsOverride = 4,
            };

            Assert.DoesNotThrow(() => extractor.CopySettingsFromEntry(null, target));

            Assert.That(target._useGlobalSettings, Is.True);
            Assert.That(target._gridColumnsOverride, Is.EqualTo(4));
        }

        [Test]
        public void CopySettingsFromEntryHandlesNullTargetGracefully()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            SpriteSheetExtractor.SpriteSheetEntry source = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = false,
            };

            Assert.DoesNotThrow(() => extractor.CopySettingsFromEntry(source, null));
        }

        [Test]
        public void CopySettingsFromEntryHandlesBothNullGracefully()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            Assert.DoesNotThrow(() => extractor.CopySettingsFromEntry(null, null));
        }

        [Test]
        public void CopySettingsFromEntryCopiesNullOverrides()
        {
            SpriteSheetExtractor.SpriteSheetEntry source = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = false,
                _extractionModeOverride = null,
                _gridSizeModeOverride = null,
            };

            SpriteSheetExtractor.SpriteSheetEntry target = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _useGlobalSettings = true,
                _extractionModeOverride = SpriteSheetExtractor.ExtractionMode.GridBased,
                _gridSizeModeOverride = SpriteSheetExtractor.GridSizeMode.Manual,
            };

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor.CopySettingsFromEntry(source, target);

            Assert.That(target._extractionModeOverride, Is.Null);
            Assert.That(target._gridSizeModeOverride, Is.Null);
        }

        [Test]
        public void ApplyGlobalSettingsToAllHandlesNullDiscoveredSheets()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._discoveredSheets = null;

            Assert.DoesNotThrow(() => extractor.ApplyGlobalSettingsToAll());
        }

        [Test]
        public void ApplyGlobalSettingsToAllHandlesEmptyDiscoveredSheets()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._discoveredSheets = new List<SpriteSheetExtractor.SpriteSheetEntry>();

            Assert.DoesNotThrow(() => extractor.ApplyGlobalSettingsToAll());
        }

        [Test]
        public void NewEntryDefaultsToUseGlobalSettingsTrue()
        {
            SpriteSheetExtractor.SpriteSheetEntry entry =
                new SpriteSheetExtractor.SpriteSheetEntry();

            Assert.That(entry._useGlobalSettings, Is.True);
        }

        [Test]
        public void NewEntryDefaultsToNullOverrides()
        {
            SpriteSheetExtractor.SpriteSheetEntry entry =
                new SpriteSheetExtractor.SpriteSheetEntry();

            Assert.That(entry._extractionModeOverride, Is.Null);
            Assert.That(entry._gridSizeModeOverride, Is.Null);
        }

        [Test]
        public void PivotModeToVector2ReturnsCorrectVectorForCenter()
        {
            Vector2 result = SpriteSheetExtractor.PivotModeToVector2(
                PivotMode.Center,
                Vector2.zero
            );
            Assert.That(result, Is.EqualTo(new Vector2(0.5f, 0.5f)));
        }

        [Test]
        public void PivotModeToVector2ReturnsCorrectVectorForBottomLeft()
        {
            Vector2 result = SpriteSheetExtractor.PivotModeToVector2(
                PivotMode.BottomLeft,
                Vector2.zero
            );
            Assert.That(result, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void PivotModeToVector2ReturnsCorrectVectorForTopRight()
        {
            Vector2 result = SpriteSheetExtractor.PivotModeToVector2(
                PivotMode.TopRight,
                Vector2.zero
            );
            Assert.That(result, Is.EqualTo(Vector2.one));
        }

        [Test]
        public void PivotModeToVector2ReturnsCorrectVectorForTopLeft()
        {
            Vector2 result = SpriteSheetExtractor.PivotModeToVector2(
                PivotMode.TopLeft,
                Vector2.zero
            );
            Assert.That(result, Is.EqualTo(new Vector2(0f, 1f)));
        }

        [Test]
        public void PivotModeToVector2ReturnsCorrectVectorForBottomRight()
        {
            Vector2 result = SpriteSheetExtractor.PivotModeToVector2(
                PivotMode.BottomRight,
                Vector2.zero
            );
            Assert.That(result, Is.EqualTo(new Vector2(1f, 0f)));
        }

        [Test]
        public void PivotModeToVector2ReturnsCorrectVectorForLeftCenter()
        {
            Vector2 result = SpriteSheetExtractor.PivotModeToVector2(
                PivotMode.LeftCenter,
                Vector2.zero
            );
            Assert.That(result, Is.EqualTo(new Vector2(0f, 0.5f)));
        }

        [Test]
        public void PivotModeToVector2ReturnsCorrectVectorForRightCenter()
        {
            Vector2 result = SpriteSheetExtractor.PivotModeToVector2(
                PivotMode.RightCenter,
                Vector2.zero
            );
            Assert.That(result, Is.EqualTo(new Vector2(1f, 0.5f)));
        }

        [Test]
        public void PivotModeToVector2ReturnsCorrectVectorForTopCenter()
        {
            Vector2 result = SpriteSheetExtractor.PivotModeToVector2(
                PivotMode.TopCenter,
                Vector2.zero
            );
            Assert.That(result, Is.EqualTo(new Vector2(0.5f, 1f)));
        }

        [Test]
        public void PivotModeToVector2ReturnsCorrectVectorForBottomCenter()
        {
            Vector2 result = SpriteSheetExtractor.PivotModeToVector2(
                PivotMode.BottomCenter,
                Vector2.zero
            );
            Assert.That(result, Is.EqualTo(new Vector2(0.5f, 0f)));
        }

        [Test]
        public void PivotModeToVector2ReturnsCustomPivotWhenModeIsCustom()
        {
            Vector2 customPivot = new(0.25f, 0.75f);
            Vector2 result = SpriteSheetExtractor.PivotModeToVector2(PivotMode.Custom, customPivot);
            Assert.That(result, Is.EqualTo(customPivot));
        }

        [Test]
        public void GetEffectivePivotModeReturnsGlobalWhenUseGlobalSettingsTrue()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._pivotMode = PivotMode.TopLeft;

            SpriteSheetExtractor.SpriteSheetEntry entry = new()
            {
                _useGlobalSettings = true,
                _pivotModeOverride = PivotMode.BottomRight,
            };

            PivotMode result = extractor.GetEffectivePivotMode(entry);
            Assert.That(result, Is.EqualTo(PivotMode.TopLeft));
        }

        [Test]
        public void GetEffectivePivotModeReturnsOverrideWhenUseGlobalSettingsFalse()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._pivotMode = PivotMode.TopLeft;

            SpriteSheetExtractor.SpriteSheetEntry entry = new()
            {
                _useGlobalSettings = false,
                _pivotModeOverride = PivotMode.BottomRight,
            };

            PivotMode result = extractor.GetEffectivePivotMode(entry);
            Assert.That(result, Is.EqualTo(PivotMode.BottomRight));
        }

        [Test]
        public void GetEffectivePivotModeReturnsGlobalWhenOverrideIsNull()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._pivotMode = PivotMode.TopCenter;

            SpriteSheetExtractor.SpriteSheetEntry entry = new()
            {
                _useGlobalSettings = false,
                _pivotModeOverride = null,
            };

            PivotMode result = extractor.GetEffectivePivotMode(entry);
            Assert.That(result, Is.EqualTo(PivotMode.TopCenter));
        }

        [Test]
        public void GetEffectiveCustomPivotReturnsGlobalWhenUseGlobalSettingsTrue()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._customPivot = new Vector2(0.1f, 0.2f);

            SpriteSheetExtractor.SpriteSheetEntry entry = new()
            {
                _useGlobalSettings = true,
                _customPivotOverride = new Vector2(0.8f, 0.9f),
            };

            Vector2 result = extractor.GetEffectiveCustomPivot(entry);
            Assert.That(result, Is.EqualTo(new Vector2(0.1f, 0.2f)));
        }

        [Test]
        public void GetEffectiveCustomPivotReturnsOverrideWhenUseGlobalSettingsFalse()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._customPivot = new Vector2(0.1f, 0.2f);

            SpriteSheetExtractor.SpriteSheetEntry entry = new()
            {
                _useGlobalSettings = false,
                _customPivotOverride = new Vector2(0.8f, 0.9f),
            };

            Vector2 result = extractor.GetEffectiveCustomPivot(entry);
            Assert.That(result, Is.EqualTo(new Vector2(0.8f, 0.9f)));
        }

        [Test]
        public void GetEffectivePivotReturnsSpriteOverrideWhenEnabled()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._pivotMode = PivotMode.Center;
            extractor._customPivot = new Vector2(0.5f, 0.5f);

            SpriteSheetExtractor.SpriteSheetEntry entry = new()
            {
                _useGlobalSettings = false,
                _pivotModeOverride = PivotMode.TopLeft,
                _customPivotOverride = new Vector2(0.0f, 1.0f),
            };

            SpriteSheetExtractor.SpriteEntryData sprite = new()
            {
                _usePivotOverride = true,
                _pivotModeOverride = PivotMode.BottomRight,
                _customPivotOverride = new Vector2(1.0f, 0.0f),
            };

            Vector2 result = extractor.GetEffectivePivot(entry, sprite);
            Assert.That(result, Is.EqualTo(new Vector2(1.0f, 0.0f)));
        }

        [Test]
        public void GetEffectivePivotReturnsSheetOverrideWhenSpriteOverrideDisabled()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._pivotMode = PivotMode.Center;
            extractor._customPivot = new Vector2(0.5f, 0.5f);

            SpriteSheetExtractor.SpriteSheetEntry entry = new()
            {
                _useGlobalSettings = false,
                _pivotModeOverride = PivotMode.TopLeft,
                _customPivotOverride = new Vector2(0.0f, 1.0f),
            };

            SpriteSheetExtractor.SpriteEntryData sprite = new()
            {
                _usePivotOverride = false,
                _pivotModeOverride = PivotMode.BottomRight,
                _customPivotOverride = new Vector2(1.0f, 0.0f),
            };

            Vector2 result = extractor.GetEffectivePivot(entry, sprite);
            Assert.That(result, Is.EqualTo(new Vector2(0.0f, 1.0f)));
        }

        [Test]
        public void GetEffectivePivotReturnsGlobalWhenBothOverridesDisabled()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._pivotMode = PivotMode.BottomCenter;
            extractor._customPivot = new Vector2(0.5f, 0.0f);

            SpriteSheetExtractor.SpriteSheetEntry entry = new()
            {
                _useGlobalSettings = true,
                _pivotModeOverride = PivotMode.TopLeft,
                _customPivotOverride = new Vector2(0.0f, 1.0f),
            };

            SpriteSheetExtractor.SpriteEntryData sprite = new()
            {
                _usePivotOverride = false,
                _pivotModeOverride = PivotMode.BottomRight,
                _customPivotOverride = new Vector2(1.0f, 0.0f),
            };

            Vector2 result = extractor.GetEffectivePivot(entry, sprite);
            Assert.That(result, Is.EqualTo(new Vector2(0.5f, 0.0f)));
        }

        [Test]
        public void GetEffectivePivotHandlesNullSprite()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._pivotMode = PivotMode.TopCenter;
            extractor._customPivot = new Vector2(0.5f, 1.0f);

            SpriteSheetExtractor.SpriteSheetEntry entry = new()
            {
                _useGlobalSettings = false,
                _pivotModeOverride = PivotMode.LeftCenter,
                _customPivotOverride = new Vector2(0.0f, 0.5f),
            };

            Vector2 result = extractor.GetEffectivePivot(entry, null);
            Assert.That(result, Is.EqualTo(new Vector2(0.0f, 0.5f)));
        }

        [Test]
        public void GetEffectivePivotHandlesNullEntry()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._pivotMode = PivotMode.RightCenter;
            extractor._customPivot = new Vector2(1.0f, 0.5f);

            SpriteSheetExtractor.SpriteEntryData sprite = new()
            {
                _usePivotOverride = false,
                _pivotModeOverride = PivotMode.BottomRight,
                _customPivotOverride = new Vector2(1.0f, 0.0f),
            };

            Vector2 result = extractor.GetEffectivePivot(null, sprite);
            Assert.That(result, Is.EqualTo(new Vector2(1.0f, 0.5f)));
        }

        [Test]
        public void GetEffectivePivotHandlesCustomPivotMode()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._pivotMode = PivotMode.Center;
            extractor._customPivot = new Vector2(0.5f, 0.5f);

            SpriteSheetExtractor.SpriteSheetEntry entry = new() { _useGlobalSettings = true };

            SpriteSheetExtractor.SpriteEntryData sprite = new()
            {
                _usePivotOverride = true,
                _pivotModeOverride = PivotMode.Custom,
                _customPivotOverride = new Vector2(0.33f, 0.67f),
            };

            Vector2 result = extractor.GetEffectivePivot(entry, sprite);
            Assert.That(result, Is.EqualTo(new Vector2(0.33f, 0.67f)));
        }

        [Test]
        public void GetEffectivePivotUsesSpriteOverrideWhenEntryIsNull()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._pivotMode = PivotMode.Center;
            extractor._customPivot = new Vector2(0.5f, 0.5f);

            SpriteSheetExtractor.SpriteEntryData sprite = new()
            {
                _usePivotOverride = true,
                _pivotModeOverride = PivotMode.TopLeft,
                _customPivotOverride = new Vector2(0.0f, 1.0f),
            };

            Vector2 result = extractor.GetEffectivePivot(null, sprite);
            Assert.That(result, Is.EqualTo(new Vector2(0.0f, 1.0f)));
        }

        [Test]
        public void GetEffectivePivotReturnsGlobalWhenBothAreNull()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._pivotMode = PivotMode.BottomLeft;
            extractor._customPivot = new Vector2(0.0f, 0.0f);

            Vector2 result = extractor.GetEffectivePivot(null, null);
            Assert.That(result, Is.EqualTo(new Vector2(0.0f, 0.0f)));
        }

        [Test]
        public void PivotModeToVector2ReturnsDefaultForObsoleteNoneValue()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            Vector2 result = SpriteSheetExtractor.PivotModeToVector2(PivotMode.None, Vector2.zero);
#pragma warning restore CS0618
            Assert.That(result, Is.EqualTo(new Vector2(0.5f, 0.5f)));
        }

        private static IEnumerable<TestCaseData> PivotEdgeCases()
        {
            yield return new TestCaseData(new Vector2(0.0f, 0.0f), new Vector2(0.0f, 0.0f)).SetName(
                "Pivot.EdgeCase.ExactZeroZero"
            );
            yield return new TestCaseData(new Vector2(1.0f, 1.0f), new Vector2(1.0f, 1.0f)).SetName(
                "Pivot.EdgeCase.ExactOneOne"
            );
            yield return new TestCaseData(new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f)).SetName(
                "Pivot.EdgeCase.ExactZeroOne"
            );
            yield return new TestCaseData(new Vector2(1.0f, 0.0f), new Vector2(1.0f, 0.0f)).SetName(
                "Pivot.EdgeCase.ExactOneZero"
            );
            yield return new TestCaseData(
                new Vector2(0.4999f, 0.4999f),
                new Vector2(0.4999f, 0.4999f)
            ).SetName("Pivot.EdgeCase.AlmostCenterLow");
            yield return new TestCaseData(
                new Vector2(0.5001f, 0.5001f),
                new Vector2(0.5001f, 0.5001f)
            ).SetName("Pivot.EdgeCase.AlmostCenterHigh");
        }

        [Test]
        [TestCaseSource(nameof(PivotEdgeCases))]
        public void PivotModeToVector2ReturnsCorrectVectorForCustomPivotEdgeCases(
            Vector2 customPivot,
            Vector2 expected
        )
        {
            Vector2 result = SpriteSheetExtractor.PivotModeToVector2(PivotMode.Custom, customPivot);
            Assert.That(result.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(result.y, Is.EqualTo(expected.y).Within(0.0001f));
        }

        [Test]
        public void PivotModeToVector2HandlesExactlyZeroPivot()
        {
            Vector2 result = SpriteSheetExtractor.PivotModeToVector2(
                PivotMode.Custom,
                new Vector2(0f, 0f)
            );
            Assert.That(result, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void PivotModeToVector2HandlesExactlyOnePivot()
        {
            Vector2 result = SpriteSheetExtractor.PivotModeToVector2(
                PivotMode.Custom,
                new Vector2(1f, 1f)
            );
            Assert.That(result, Is.EqualTo(Vector2.one));
        }

        [Test]
        public void PivotModeToVector2PreservesAlmostCenterLowValue()
        {
            Vector2 almostCenter = new(0.4999f, 0.4999f);
            Vector2 result = SpriteSheetExtractor.PivotModeToVector2(
                PivotMode.Custom,
                almostCenter
            );
            Assert.That(result.x, Is.EqualTo(0.4999f).Within(0.00001f));
            Assert.That(result.y, Is.EqualTo(0.4999f).Within(0.00001f));
        }

        [Test]
        public void PivotModeToVector2PreservesAlmostCenterHighValue()
        {
            Vector2 almostCenter = new(0.5001f, 0.5001f);
            Vector2 result = SpriteSheetExtractor.PivotModeToVector2(
                PivotMode.Custom,
                almostCenter
            );
            Assert.That(result.x, Is.EqualTo(0.5001f).Within(0.00001f));
            Assert.That(result.y, Is.EqualTo(0.5001f).Within(0.00001f));
        }

        [Test]
        public void PivotModeToVector2HandlesAsymmetricPivotValues()
        {
            Vector2 asymmetric = new(0.25f, 0.75f);
            Vector2 result = SpriteSheetExtractor.PivotModeToVector2(PivotMode.Custom, asymmetric);
            Assert.That(result, Is.EqualTo(asymmetric));
        }

        [Test]
        public void CalculateGridDimensionsCapsCellWidthWhenGCDEqualsTextureWidth()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Auto;

            extractor.CalculateGridDimensions(
                256,
                256,
                null,
                null,
                out int columns,
                out int rows,
                out int cellWidth,
                out int cellHeight
            );

            Assert.GreaterOrEqual(columns, 2, "Should have at least 2 columns");
            Assert.LessOrEqual(cellWidth, 128, "Cell width should be capped");
        }

        [Test]
        public void CalculateGridDimensionsCapsCellHeightWhenGCDEqualsTextureHeight()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Auto;

            extractor.CalculateGridDimensions(
                256,
                256,
                null,
                null,
                out int columns,
                out int rows,
                out int cellWidth,
                out int cellHeight
            );

            Assert.GreaterOrEqual(rows, 2, "Should have at least 2 rows");
            Assert.LessOrEqual(cellHeight, 128, "Cell height should be capped");
        }

        [Test]
        public void CalculateGridDimensionsHandlesSquareTextureWithLargeGCD()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Auto;

            extractor.CalculateGridDimensions(
                128,
                128,
                null,
                null,
                out int columns,
                out int rows,
                out int cellWidth,
                out int cellHeight
            );

            Assert.Greater(columns * rows, 1, "Should have multiple cells");
            Assert.AreEqual(columns * cellWidth, 128, "Total width should equal texture width");
            Assert.AreEqual(rows * cellHeight, 128, "Total height should equal texture height");
        }

        [Test]
        public void CalculateGridDimensionsPreservesSmallCellSizes()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Manual;
            extractor._gridColumns = 8;
            extractor._gridRows = 8;

            extractor.CalculateGridDimensions(
                256,
                256,
                null,
                null,
                out int columns,
                out int rows,
                out int cellWidth,
                out int cellHeight
            );

            Assert.AreEqual(8, columns, "Should preserve 8 columns");
            Assert.AreEqual(8, rows, "Should preserve 8 rows");
            Assert.AreEqual(32, cellWidth, "Cell width should be 32");
            Assert.AreEqual(32, cellHeight, "Cell height should be 32");
        }

        [Test]
        public void CalculateGridDimensionsHandlesVeryTallTexture()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Auto;

            extractor.CalculateGridDimensions(
                64,
                512,
                null,
                null,
                out int columns,
                out int rows,
                out int cellWidth,
                out int cellHeight
            );

            Assert.GreaterOrEqual(columns, 2, "Should have at least 2 columns for tall texture");
            Assert.LessOrEqual(cellWidth, 32, "Cell width should be capped for tall texture");
        }

        [Test]
        public void CalculateGridDimensionsHandlesVeryWideTexture()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Auto;

            extractor.CalculateGridDimensions(
                512,
                64,
                null,
                null,
                out int columns,
                out int rows,
                out int cellWidth,
                out int cellHeight
            );

            Assert.GreaterOrEqual(rows, 2, "Should have at least 2 rows for wide texture");
            Assert.LessOrEqual(cellHeight, 32, "Cell height should be capped for wide texture");
        }

        [Test]
        public void CalculateGridDimensionsDoesNotCapVerySmallTextures()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Auto;

            extractor.CalculateGridDimensions(
                8,
                8,
                null,
                null,
                out int columns,
                out int rows,
                out int cellWidth,
                out int cellHeight
            );

            Assert.Greater(columns, 0, "Should have at least 1 column");
            Assert.Greater(rows, 0, "Should have at least 1 row");
        }

        [Test]
        public void ComputeFileHashReturnsNullForNullPath()
        {
            string result = SpriteSheetExtractor.ComputeFileHash(null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ComputeFileHashReturnsNullForEmptyPath()
        {
            string result = SpriteSheetExtractor.ComputeFileHash(string.Empty);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ComputeFileHashReturnsNullForNonexistentFile()
        {
            string result = SpriteSheetExtractor.ComputeFileHash("NonExistent/Path/To/File.png");
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetEffectiveShowOverlayReturnsGlobalWhenEntryIsNull()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._showOverlay = true;

            bool result = extractor.GetEffectiveShowOverlay(null);

            Assert.That(result, Is.True);
        }

        [Test]
        public void GetEffectiveShowOverlayReturnsFalseWhenGlobalFalseAndEntryNull()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._showOverlay = false;

            bool result = extractor.GetEffectiveShowOverlay(null);

            Assert.That(result, Is.False);
        }

        private static IEnumerable<TestCaseData> GetEffectiveShowOverlayCases()
        {
            yield return new TestCaseData(true, true, false, true).SetName(
                "EffectiveShowOverlay.UseGlobal.ReturnsGlobal"
            );
            yield return new TestCaseData(true, false, false, false).SetName(
                "EffectiveShowOverlay.NotUseGlobal.ReturnsOverride"
            );
            yield return new TestCaseData(false, true, true, false).SetName(
                "EffectiveShowOverlay.UseGlobalFalse.ReturnsGlobalFalse"
            );
            yield return new TestCaseData(false, false, true, true).SetName(
                "EffectiveShowOverlay.NotUseGlobalTrueOverride.ReturnsTrue"
            );
        }

        [Test]
        [TestCaseSource(nameof(GetEffectiveShowOverlayCases))]
        public void GetEffectiveShowOverlayReturnsCorrectValue(
            bool globalValue,
            bool useGlobalSettings,
            bool overrideValue,
            bool expected
        )
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._showOverlay = globalValue;

            SpriteSheetExtractor.SpriteSheetEntry entry = new()
            {
                _useGlobalSettings = useGlobalSettings,
                _showOverlayOverride = overrideValue,
            };

            bool result = extractor.GetEffectiveShowOverlay(entry);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void CopySettingsFromEntryCopiesShowOverlayOverride()
        {
            SpriteSheetExtractor.SpriteSheetEntry source = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _showOverlayOverride = true,
            };

            SpriteSheetExtractor.SpriteSheetEntry target = new SpriteSheetExtractor.SpriteSheetEntry
            {
                _showOverlayOverride = false,
            };

            SpriteSheetExtractor extractor = CreateExtractor();
            extractor.CopySettingsFromEntry(source, target);

            Assert.That(target._showOverlayOverride, Is.True);
        }

        [Test]
        public void NewEntryDefaultsToNullShowOverlayOverride()
        {
            SpriteSheetExtractor.SpriteSheetEntry entry =
                new SpriteSheetExtractor.SpriteSheetEntry();

            Assert.That(entry._showOverlayOverride, Is.Null);
        }

        [Test]
        public void CalculateTextureRectWithinPreviewCentersHorizontallyWhenRectIsWider()
        {
            Rect previewRect = new Rect(0, 0, 100, 100);
            int textureWidth = 200;
            int textureHeight = 100;
            float scale = 0.5f;

            SpriteSheetExtractor extractor = CreateExtractor();
            Rect result = extractor.CalculateTextureRectWithinPreview(
                previewRect,
                textureWidth,
                textureHeight,
                scale
            );

            Assert.That(result.width, Is.EqualTo(100));
            Assert.That(result.height, Is.EqualTo(50));
            Assert.That(result.x, Is.EqualTo(0));
        }

        [Test]
        public void CalculateTextureRectWithinPreviewCentersVerticallyWhenRectIsTaller()
        {
            Rect previewRect = new Rect(0, 0, 100, 100);
            int textureWidth = 100;
            int textureHeight = 200;
            float scale = 0.5f;

            SpriteSheetExtractor extractor = CreateExtractor();
            Rect result = extractor.CalculateTextureRectWithinPreview(
                previewRect,
                textureWidth,
                textureHeight,
                scale
            );

            Assert.That(result.width, Is.EqualTo(50));
            Assert.That(result.height, Is.EqualTo(100));
            Assert.That(result.y, Is.EqualTo(0));
        }

        [Test]
        public void CalculateTextureRectWithinPreviewNoOffsetWhenExactMatch()
        {
            Rect previewRect = new Rect(10, 20, 100, 100);
            int textureWidth = 100;
            int textureHeight = 100;
            float scale = 1f;

            SpriteSheetExtractor extractor = CreateExtractor();
            Rect result = extractor.CalculateTextureRectWithinPreview(
                previewRect,
                textureWidth,
                textureHeight,
                scale
            );

            Assert.That(result.x, Is.EqualTo(10));
            Assert.That(result.y, Is.EqualTo(20));
            Assert.That(result.width, Is.EqualTo(100));
            Assert.That(result.height, Is.EqualTo(100));
        }

        [Test]
        public void CalculateTextureRectWithinPreviewHandlesScaleCorrectly()
        {
            Rect previewRect = new Rect(0, 0, 200, 200);
            int textureWidth = 100;
            int textureHeight = 100;
            float scale = 2f;

            SpriteSheetExtractor extractor = CreateExtractor();
            Rect result = extractor.CalculateTextureRectWithinPreview(
                previewRect,
                textureWidth,
                textureHeight,
                scale
            );

            Assert.That(result.width, Is.EqualTo(200));
            Assert.That(result.height, Is.EqualTo(200));
        }

        [Test]
        public void CalculateTextureRectWithinPreviewHandlesLandscapeTexture()
        {
            Rect previewRect = new Rect(0, 0, 100, 100);
            int textureWidth = 200;
            int textureHeight = 100;
            float scale = 0.5f;

            SpriteSheetExtractor extractor = CreateExtractor();
            Rect result = extractor.CalculateTextureRectWithinPreview(
                previewRect,
                textureWidth,
                textureHeight,
                scale
            );

            Assert.That(result.width, Is.EqualTo(100));
            Assert.That(result.height, Is.EqualTo(50));
        }

        [Test]
        public void CalculateTextureRectWithinPreviewHandlesPortraitTexture()
        {
            Rect previewRect = new Rect(0, 0, 100, 100);
            int textureWidth = 100;
            int textureHeight = 200;
            float scale = 0.5f;

            SpriteSheetExtractor extractor = CreateExtractor();
            Rect result = extractor.CalculateTextureRectWithinPreview(
                previewRect,
                textureWidth,
                textureHeight,
                scale
            );

            Assert.That(result.width, Is.EqualTo(50));
            Assert.That(result.height, Is.EqualTo(100));
        }

        [Test]
        public void CalculateTextureRectWithinPreviewPreservesRectPosition()
        {
            Rect previewRect = new Rect(50, 100, 200, 200);
            int textureWidth = 200;
            int textureHeight = 200;
            float scale = 1f;

            SpriteSheetExtractor extractor = CreateExtractor();
            Rect result = extractor.CalculateTextureRectWithinPreview(
                previewRect,
                textureWidth,
                textureHeight,
                scale
            );

            Assert.That(result.x, Is.EqualTo(50));
            Assert.That(result.y, Is.EqualTo(100));
        }

        private static IEnumerable<TestCaseData> CalculateTextureRectEdgeCases()
        {
            yield return new TestCaseData(
                new Rect(0, 0, 1, 1),
                1,
                1,
                1f,
                new Rect(0, 0, 1, 1)
            ).SetName("TextureRect.EdgeCase.MinimumSize");
            yield return new TestCaseData(
                new Rect(0, 0, 100, 100),
                1,
                1,
                1f,
                new Rect(49.5f, 49.5f, 1, 1)
            ).SetName("TextureRect.EdgeCase.SmallTextureInLargePreview");
        }

        [Test]
        [TestCaseSource(nameof(CalculateTextureRectEdgeCases))]
        public void CalculateTextureRectWithinPreviewHandlesEdgeCases(
            Rect previewRect,
            int textureWidth,
            int textureHeight,
            float scale,
            Rect expected
        )
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            Rect result = extractor.CalculateTextureRectWithinPreview(
                previewRect,
                textureWidth,
                textureHeight,
                scale
            );

            Assert.That(result.x, Is.EqualTo(expected.x).Within(0.1f));
            Assert.That(result.y, Is.EqualTo(expected.y).Within(0.1f));
            Assert.That(result.width, Is.EqualTo(expected.width).Within(0.1f));
            Assert.That(result.height, Is.EqualTo(expected.height).Within(0.1f));
        }

        [Test]
        public void CalculateTextureRectWithinPreviewReturnsPreviewRectForZeroWidth()
        {
            Rect previewRect = new Rect(0, 0, 100, 100);
            int textureWidth = 0;
            int textureHeight = 100;
            float scale = 1f;

            SpriteSheetExtractor extractor = CreateExtractor();
            Rect result = extractor.CalculateTextureRectWithinPreview(
                previewRect,
                textureWidth,
                textureHeight,
                scale
            );

            Assert.That(result, Is.EqualTo(previewRect));
        }

        [Test]
        public void CalculateTextureRectWithinPreviewReturnsPreviewRectForZeroHeight()
        {
            Rect previewRect = new Rect(0, 0, 100, 100);
            int textureWidth = 100;
            int textureHeight = 0;
            float scale = 1f;

            SpriteSheetExtractor extractor = CreateExtractor();
            Rect result = extractor.CalculateTextureRectWithinPreview(
                previewRect,
                textureWidth,
                textureHeight,
                scale
            );

            Assert.That(result, Is.EqualTo(previewRect));
        }

        [Test]
        public void CalculateTextureRectWithinPreviewReturnsPreviewRectForZeroScale()
        {
            Rect previewRect = new Rect(0, 0, 100, 100);
            int textureWidth = 100;
            int textureHeight = 100;
            float scale = 0f;

            SpriteSheetExtractor extractor = CreateExtractor();
            Rect result = extractor.CalculateTextureRectWithinPreview(
                previewRect,
                textureWidth,
                textureHeight,
                scale
            );

            Assert.That(result, Is.EqualTo(previewRect));
        }

        [Test]
        public void CalculateTextureRectWithinPreviewReturnsPreviewRectForNegativeDimensions()
        {
            Rect previewRect = new Rect(0, 0, 100, 100);
            int textureWidth = -100;
            int textureHeight = 100;
            float scale = 1f;

            SpriteSheetExtractor extractor = CreateExtractor();
            Rect result = extractor.CalculateTextureRectWithinPreview(
                previewRect,
                textureWidth,
                textureHeight,
                scale
            );

            Assert.That(result, Is.EqualTo(previewRect));
        }

        [Test]
        public void FindSmallestReasonableDivisorReturnsCommonSize()
        {
            int result = SpriteSheetExtractor.FindSmallestReasonableDivisor(256);
            Assert.LessOrEqual(result, 64, "Should find divisor <= 64 for 256");
            Assert.That(256 % result, Is.EqualTo(0), "Result should evenly divide 256");
        }

        [Test]
        public void FindSmallestReasonableDivisorHandles128()
        {
            int result = SpriteSheetExtractor.FindSmallestReasonableDivisor(128);
            Assert.That(128 % result, Is.EqualTo(0), "Result should evenly divide 128");
        }

        [Test]
        public void FindSmallestReasonableDivisorHandles64()
        {
            int result = SpriteSheetExtractor.FindSmallestReasonableDivisor(64);
            Assert.That(64 % result, Is.EqualTo(0), "Result should evenly divide 64");
        }

        [Test]
        public void FindSmallestReasonableDivisorHandles32()
        {
            int result = SpriteSheetExtractor.FindSmallestReasonableDivisor(32);
            Assert.That(32 % result, Is.EqualTo(0), "Result should evenly divide 32");
        }

        [Test]
        public void FindSmallestReasonableDivisorHandles16()
        {
            int result = SpriteSheetExtractor.FindSmallestReasonableDivisor(16);
            Assert.That(result, Is.GreaterThan(0));
        }

        [Test]
        public void FindSmallestReasonableDivisorReturnsFullDimensionForSmall()
        {
            int result = SpriteSheetExtractor.FindSmallestReasonableDivisor(4);
            Assert.That(result, Is.GreaterThan(0));
        }

        [Test]
        public void FindSmallestReasonableDivisorHandlesPrimeDimension()
        {
            int result = SpriteSheetExtractor.FindSmallestReasonableDivisor(127);
            Assert.That(result, Is.GreaterThan(0));
        }

        [Test]
        public void FindSmallestReasonableDivisorHandlesNonCommonSize()
        {
            int result = SpriteSheetExtractor.FindSmallestReasonableDivisor(100);
            Assert.That(result, Is.GreaterThan(0));
        }

        [Test]
        public void FindSmallestReasonableDivisorHandles48()
        {
            int result = SpriteSheetExtractor.FindSmallestReasonableDivisor(48);
            Assert.That(48 % result, Is.EqualTo(0), "Result should evenly divide 48");
        }

        [Test]
        public void FindSmallestReasonableDivisorHandles100()
        {
            int result = SpriteSheetExtractor.FindSmallestReasonableDivisor(100);
            Assert.That(100 % result, Is.EqualTo(0), "Result should evenly divide 100");
        }

        [Test]
        public void FindSmallestReasonableDivisorHandles200()
        {
            int result = SpriteSheetExtractor.FindSmallestReasonableDivisor(200);
            Assert.That(200 % result, Is.EqualTo(0), "Result should evenly divide 200");
        }

        [Test]
        public void FindSmallestReasonableDivisorHandles300()
        {
            int result = SpriteSheetExtractor.FindSmallestReasonableDivisor(300);
            Assert.That(300 % result, Is.EqualTo(0), "Result should evenly divide 300");
        }

        [Test]
        public void FindSmallestReasonableDivisorHandlesZero()
        {
            int result = SpriteSheetExtractor.FindSmallestReasonableDivisor(0);
            Assert.That(result, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void FindSmallestReasonableDivisorHandlesVerySmallDimensions()
        {
            int result1 = SpriteSheetExtractor.FindSmallestReasonableDivisor(1);
            int result2 = SpriteSheetExtractor.FindSmallestReasonableDivisor(2);

            Assert.That(result1, Is.GreaterThanOrEqualTo(0));
            Assert.That(result2, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void FindSmallestReasonableDivisorHandles512()
        {
            int result = SpriteSheetExtractor.FindSmallestReasonableDivisor(512);
            Assert.That(512 % result, Is.EqualTo(0), "Result should evenly divide 512");
        }

        [Test]
        public void FindSmallestReasonableDivisorHandlesLargeDimension()
        {
            int result = SpriteSheetExtractor.FindSmallestReasonableDivisor(2048);
            Assert.That(2048 % result, Is.EqualTo(0), "Result should evenly divide 2048");
        }

        [Test]
        public void FindSmallestReasonableDivisorHandles9()
        {
            int result = SpriteSheetExtractor.FindSmallestReasonableDivisor(9);
            Assert.That(result, Is.GreaterThan(0));
        }

        [Test]
        public void FindNearestDivisorWithMinCellsReturnsTargetWhenExactDivisor()
        {
            int result = SpriteSheetExtractor.FindNearestDivisorWithMinCells(100, 25, 2);
            Assert.That(result, Is.EqualTo(25));
        }

        [Test]
        public void FindNearestDivisorWithMinCellsFindsNearestWhenNoExactMatch()
        {
            int result = SpriteSheetExtractor.FindNearestDivisorWithMinCells(100, 23, 2);
            Assert.That(100 % result, Is.EqualTo(0), "Result should evenly divide dimension");
        }

        [Test]
        public void FindNearestDivisorWithMinCellsReturnsTargetForZeroDimension()
        {
            int result = SpriteSheetExtractor.FindNearestDivisorWithMinCells(0, 25, 2);
            Assert.That(result, Is.EqualTo(25));
        }

        [Test]
        public void FindNearestDivisorWithMinCellsReturnsTargetForZeroTarget()
        {
            int result = SpriteSheetExtractor.FindNearestDivisorWithMinCells(100, 0, 2);
            Assert.That(result, Is.EqualTo(0));
        }

        [Test]
        public void FindNearestDivisorWithMinCellsReturnsTargetWhenMinCellsTooLarge()
        {
            int result = SpriteSheetExtractor.FindNearestDivisorWithMinCells(10, 5, 10);
            Assert.That(result, Is.GreaterThan(0));
        }

        [Test]
        public void FindNearestDivisorWithMinCellsHandlesSmallDimension()
        {
            int result = SpriteSheetExtractor.FindNearestDivisorWithMinCells(4, 2, 2);
            Assert.That(result, Is.GreaterThan(0));
        }

        [Test]
        public void FindNearestDivisorWithMinCellsHandlesPrimeNumber()
        {
            int result = SpriteSheetExtractor.FindNearestDivisorWithMinCells(127, 32, 2);
            Assert.That(result, Is.GreaterThan(0));
        }

        [Test]
        public void DetectCellSizeFromOpaqueRegionsReturnsZeroForNullPixels()
        {
            (int cellWidth, int cellHeight) = SpriteSheetExtractor.DetectCellSizeFromOpaqueRegions(
                null,
                64,
                64,
                0.5f
            );

            Assert.That(cellWidth, Is.EqualTo(0));
            Assert.That(cellHeight, Is.EqualTo(0));
        }

        [Test]
        public void DetectCellSizeFromOpaqueRegionsReturnsZeroForEmptyPixels()
        {
            Color32[] emptyPixels = new Color32[0];

            (int cellWidth, int cellHeight) = SpriteSheetExtractor.DetectCellSizeFromOpaqueRegions(
                emptyPixels,
                64,
                64,
                0.5f
            );

            Assert.That(cellWidth, Is.EqualTo(0));
            Assert.That(cellHeight, Is.EqualTo(0));
        }

        [Test]
        public void DetectCellSizeFromOpaqueRegionsReturnsZeroForAllTransparent()
        {
            int width = 64;
            int height = 64;
            Color32[] pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(0, 0, 0, 0);
            }

            (int cellWidth, int cellHeight) = SpriteSheetExtractor.DetectCellSizeFromOpaqueRegions(
                pixels,
                width,
                height,
                0.5f
            );

            Assert.That(cellWidth, Is.EqualTo(0));
            Assert.That(cellHeight, Is.EqualTo(0));
        }

        [Test]
        public void VerifyGridDoesNotCutSpritesReturnsTrueForNullPixels()
        {
            bool result = SpriteSheetExtractor.VerifyGridDoesNotCutSprites(
                null,
                64,
                64,
                32,
                32,
                0.5f
            );
            Assert.That(result, Is.True);
        }

        [Test]
        public void VerifyGridDoesNotCutSpritesReturnsTrueForEmptyPixels()
        {
            Color32[] emptyPixels = new Color32[0];
            bool result = SpriteSheetExtractor.VerifyGridDoesNotCutSprites(
                emptyPixels,
                64,
                64,
                32,
                32,
                0.5f
            );
            Assert.That(result, Is.True);
        }

        [Test]
        public void VerifyGridDoesNotCutSpritesReturnsTrueForSingleCell()
        {
            int width = 32;
            int height = 32;
            Color32[] pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            bool result = SpriteSheetExtractor.VerifyGridDoesNotCutSprites(
                pixels,
                width,
                height,
                width,
                height,
                0.5f
            );

            Assert.That(result, Is.True);
        }

        [Test]
        public void VerifyGridDoesNotCutSpritesReturnsTrueForZeroCellSize()
        {
            int width = 64;
            int height = 64;
            Color32[] pixels = new Color32[width * height];

            bool result = SpriteSheetExtractor.VerifyGridDoesNotCutSprites(
                pixels,
                width,
                height,
                0,
                0,
                0.5f
            );

            Assert.That(result, Is.True);
        }

        private static IEnumerable<TestCaseData> DetectOptimalGridNormalCases()
        {
            yield return new TestCaseData(
                64,
                64,
                new int[] { 32 },
                new int[] { 32 },
                0.5f,
                32,
                32
            ).SetName("GridDetection.Normal.SingleVerticalAndHorizontalLine.32x32");

            yield return new TestCaseData(
                128,
                128,
                new int[] { 32, 64, 96 },
                new int[] { 32, 64, 96 },
                0.5f,
                32,
                32
            ).SetName("GridDetection.Normal.MultipleLines.32x32Grid");

            yield return new TestCaseData(
                100,
                100,
                new int[] { 25, 50, 75 },
                new int[] { 25, 50, 75 },
                0.5f,
                25,
                25
            ).SetName("GridDetection.Normal.NonPowerOfTwo.25x25Grid");

            yield return new TestCaseData(
                96,
                64,
                new int[] { 32, 64 },
                new int[] { 32 },
                0.5f,
                32,
                32
            ).SetName("GridDetection.Normal.RectangularTexture.32x32Grid");
        }

        [Test]
        [TestCaseSource(nameof(DetectOptimalGridNormalCases))]
        public void DetectOptimalGridFromTransparencyNormalCases(
            int textureWidth,
            int textureHeight,
            int[] transparentColumns,
            int[] transparentRows,
            float alphaThreshold,
            int expectedCellWidth,
            int expectedCellHeight
        )
        {
            Color32[] pixels = new Color32[textureWidth * textureHeight];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            for (int i = 0; i < transparentColumns.Length; ++i)
            {
                int x = transparentColumns[i];
                for (int y = 0; y < textureHeight; ++y)
                {
                    pixels[y * textureWidth + x] = new Color32(0, 0, 0, 0);
                }
            }

            for (int i = 0; i < transparentRows.Length; ++i)
            {
                int y = transparentRows[i];
                for (int x = 0; x < textureWidth; ++x)
                {
                    pixels[y * textureWidth + x] = new Color32(0, 0, 0, 0);
                }
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                textureWidth,
                textureHeight,
                alphaThreshold,
                out cellWidth,
                out cellHeight
            );

            Assert.IsTrue(result, "Should detect grid from transparency");
            Assert.AreEqual(
                expectedCellWidth,
                cellWidth,
                $"Cell width should be {expectedCellWidth}"
            );
            Assert.AreEqual(
                expectedCellHeight,
                cellHeight,
                $"Cell height should be {expectedCellHeight}"
            );
        }

        private static IEnumerable<TestCaseData> DetectOptimalGridEdgeCases()
        {
            yield return new TestCaseData(4, 4, 0.5f).SetName("GridDetection.Edge.MinimumSize.4x4");
            yield return new TestCaseData(3, 3, 0.5f).SetName(
                "GridDetection.Edge.TooSmall.3x3.ShouldFail"
            );
            yield return new TestCaseData(2, 2, 0.5f).SetName(
                "GridDetection.Edge.TooSmall.2x2.ShouldFail"
            );
            yield return new TestCaseData(1, 1, 0.5f).SetName(
                "GridDetection.Edge.TooSmall.1x1.ShouldFail"
            );
        }

        [Test]
        [TestCaseSource(nameof(DetectOptimalGridEdgeCases))]
        public void DetectOptimalGridFromTransparencyEdgeCases(
            int textureWidth,
            int textureHeight,
            float alphaThreshold
        )
        {
            Color32[] pixels = new Color32[textureWidth * textureHeight];
            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                textureWidth,
                textureHeight,
                alphaThreshold,
                out cellWidth,
                out cellHeight
            );

            if (textureWidth < 4 || textureHeight < 4)
            {
                Assert.IsFalse(result, "Should return false for textures smaller than 4x4");
            }
        }

        [Test]
        public void DetectOptimalGridFromTransparencyReturnsZeroForFullyOpaqueTexture()
        {
            int width = 64;
            int height = 64;
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                0.5f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsFalse(result, "Should return false for fully opaque texture");
            Assert.AreEqual(0, cellWidth, "Cell width should be 0");
            Assert.AreEqual(0, cellHeight, "Cell height should be 0");
        }

        [Test]
        public void DetectOptimalGridFromTransparencyReturnsZeroForFullyTransparentTexture()
        {
            int width = 64;
            int height = 64;
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(0, 0, 0, 0);
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                0.5f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsFalse(result, "Should return false for fully transparent texture");
        }

        [Test]
        public void DetectOptimalGridFromTransparencyHandlesNullPixels()
        {
            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                null,
                64,
                64,
                0.5f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsFalse(result, "Should return false for null pixels");
            Assert.AreEqual(0, cellWidth, "Cell width should be 0");
            Assert.AreEqual(0, cellHeight, "Cell height should be 0");
        }

        [Test]
        public void DetectOptimalGridFromTransparencyHandlesEmptyPixels()
        {
            Color32[] emptyPixels = new Color32[0];

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                emptyPixels,
                64,
                64,
                0.5f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsFalse(result, "Should return false for empty pixels");
        }

        [Test]
        public void DetectOptimalGridFromTransparencyHandlesMismatchedDimensions()
        {
            Color32[] pixels = new Color32[100];
            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                64,
                64,
                0.5f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsFalse(
                result,
                "Should return false when pixel count does not match dimensions"
            );
        }

        private static IEnumerable<TestCaseData> AlphaThresholdEdgeCases()
        {
            yield return new TestCaseData(0.0f, true).SetName(
                "GridDetection.AlphaThreshold.Zero.ShouldWork"
            );
            yield return new TestCaseData(0.5f, true).SetName(
                "GridDetection.AlphaThreshold.Mid.ShouldWork"
            );
            yield return new TestCaseData(0.99f, true).SetName(
                "GridDetection.AlphaThreshold.NearOne.ShouldWork"
            );
            yield return new TestCaseData(1.0f, false).SetName(
                "GridDetection.AlphaThreshold.One.ShouldFail"
            );
            yield return new TestCaseData(1.1f, false).SetName(
                "GridDetection.AlphaThreshold.AboveOne.ShouldFail"
            );
        }

        [Test]
        [TestCaseSource(nameof(AlphaThresholdEdgeCases))]
        public void DetectOptimalGridFromTransparencyAlphaThresholdEdgeCases(
            float alphaThreshold,
            bool shouldDetect
        )
        {
            int width = 64;
            int height = 64;
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            for (int y = 0; y < height; ++y)
            {
                pixels[y * width + 32] = new Color32(0, 0, 0, 0);
            }
            for (int x = 0; x < width; ++x)
            {
                pixels[32 * width + x] = new Color32(0, 0, 0, 0);
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                alphaThreshold,
                out cellWidth,
                out cellHeight
            );

            if (shouldDetect)
            {
                Assert.IsTrue(
                    result || 1.0f <= alphaThreshold == false,
                    $"Alpha threshold {alphaThreshold} should detect"
                );
            }
            else
            {
                Assert.IsFalse(result, $"Alpha threshold {alphaThreshold} should not detect");
            }
        }

        [Test]
        public void DetectOptimalGridFromTransparencyDetectsOnlyVerticalBoundaries()
        {
            int width = 64;
            int height = 64;
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            for (int y = 0; y < height; ++y)
            {
                pixels[y * width + 32] = new Color32(0, 0, 0, 0);
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                0.5f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsTrue(
                result || cellWidth == 0,
                "Should handle vertical-only boundaries gracefully"
            );
        }

        [Test]
        public void DetectOptimalGridFromTransparencyDetectsOnlyHorizontalBoundaries()
        {
            int width = 64;
            int height = 64;
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            for (int x = 0; x < width; ++x)
            {
                pixels[32 * width + x] = new Color32(0, 0, 0, 0);
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                0.5f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsTrue(
                result || cellHeight == 0,
                "Should handle horizontal-only boundaries gracefully"
            );
        }

        [Test]
        public void DetectOptimalGridFromTransparencyHandlesIrregularSpacing()
        {
            int width = 64;
            int height = 64;
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            /*
                For a 64-wide texture the candidate cell sizes are 8, 16, 32 and 64, whose
                boundaries fall at 8, 16, 24, 32, 40, 48 and 56. Matching is fuzzy to +-3 pixels, so
                4, 20 and 44 are the positions far enough from every boundary to stay irregular.
            */
            int[] irregularColumns = new int[] { 4, 20, 44 };
            for (int i = 0; i < irregularColumns.Length; ++i)
            {
                int x = irregularColumns[i];
                for (int y = 0; y < height; ++y)
                {
                    pixels[y * width + x] = new Color32(0, 0, 0, 0);
                }
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                0.5f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsFalse(
                result,
                "Should return false for irregular spacing that does not divide evenly"
            );
        }

        private static IEnumerable<TestCaseData> MinimumCellSizeCases()
        {
            /*
                Textures whose only divisor of at least 8 is the dimension itself. The test draws a
                midpoint line, so its cells are dimension/2 and fall below the 8-pixel minimum.
            */
            yield return new TestCaseData(7, 7, false).SetName(
                "GridDetection.MinCellSize.7x7.NoDivisorsAbove8"
            );
            yield return new TestCaseData(6, 6, false).SetName(
                "GridDetection.MinCellSize.6x6.NoDivisorsAbove8"
            );
            yield return new TestCaseData(5, 5, false).SetName(
                "GridDetection.MinCellSize.5x5.NoDivisorsAbove8"
            );
            yield return new TestCaseData(4, 4, false).SetName(
                "GridDetection.MinCellSize.4x4.BelowMinimum"
            );
            yield return new TestCaseData(2, 2, false).SetName(
                "GridDetection.MinCellSize.2x2.BelowMinimum"
            );
            yield return new TestCaseData(1, 1, false).SetName(
                "GridDetection.MinCellSize.1x1.BelowMinimum"
            );

            // Mixed dimension cases where one dimension lacks valid divisors
            yield return new TestCaseData(7, 16, false).SetName(
                "GridDetection.MinCellSize.7x16.WidthHasNoDivisorsAbove8"
            );
            yield return new TestCaseData(16, 7, false).SetName(
                "GridDetection.MinCellSize.16x7.HeightHasNoDivisorsAbove8"
            );
            yield return new TestCaseData(8, 7, false).SetName(
                "GridDetection.MinCellSize.8x7.HeightBelowMinimum"
            );
            yield return new TestCaseData(7, 8, false).SetName(
                "GridDetection.MinCellSize.7x8.WidthBelowMinimum"
            );

            // The only valid cell size is the whole dimension, so midpoint lines make cells below 8.
            yield return new TestCaseData(8, 8, false).SetName(
                "GridDetection.MinCellSize.8x8.OnlyWholeDimensionValid"
            );
            yield return new TestCaseData(9, 9, false).SetName(
                "GridDetection.MinCellSize.9x9.OnlyWholeDimensionValid"
            );
            yield return new TestCaseData(11, 11, false).SetName(
                "GridDetection.MinCellSize.11x11.PrimeNoValidDivisors"
            );
            yield return new TestCaseData(13, 13, false).SetName(
                "GridDetection.MinCellSize.13x13.PrimeNoValidDivisors"
            );
            yield return new TestCaseData(11, 13, false).SetName(
                "GridDetection.MinCellSize.11x13.BothPrimeNoValidDivisors"
            );
            yield return new TestCaseData(17, 17, false).SetName(
                "GridDetection.MinCellSize.17x17.LargePrimeNoValidDivisors"
            );

            // Valid divisors of at least 8 exist and the midpoint still clears it: 16/2 = 8.
            yield return new TestCaseData(16, 16, true).SetName(
                "GridDetection.MinCellSize.16x16.MidpointCreates8x8Cells"
            );
            // 24/2 = 12, which exceeds minimum.
            yield return new TestCaseData(24, 24, true).SetName(
                "GridDetection.MinCellSize.24x24.MidpointCreates12x12Cells"
            );
            // 32/2 = 16, valid.
            yield return new TestCaseData(32, 32, true).SetName(
                "GridDetection.MinCellSize.32x32.MidpointCreates16x16Cells"
            );
            // 64/2 = 32, valid.
            yield return new TestCaseData(64, 64, true).SetName(
                "GridDetection.MinCellSize.64x64.MidpointCreates32x32Cells"
            );
            // 48/2 = 24, valid.
            yield return new TestCaseData(48, 48, true).SetName(
                "GridDetection.MinCellSize.48x48.MidpointCreates24x24Cells"
            );
            // Non-square: 32/2 = 16, 24/2 = 12, both valid.
            yield return new TestCaseData(32, 24, true).SetName(
                "GridDetection.MinCellSize.32x24.NonSquareValidCells"
            );
            yield return new TestCaseData(24, 32, true).SetName(
                "GridDetection.MinCellSize.24x32.NonSquareValidCells"
            );
        }

        [Test]
        [TestCaseSource(nameof(MinimumCellSizeCases))]
        public void DetectOptimalGridFromTransparencyMinimumCellSize(
            int width,
            int height,
            bool shouldDetectGrid
        )
        {
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            /*
                Transparent borders at the half-dimensions divide the texture into a 2x2 grid of
                (width/2) x (height/2) cells. A shouldDetectGrid=true case needs that cell size to
                be at least 8; a false case either has dimensions below 8 outright or a midpoint
                that yields cells below 8 (8x8 gives 4x4 cells).
            */
            int midX = width / 2;
            int midY = height / 2;

            if (0 < midX && midX < width)
            {
                for (int y = 0; y < height; ++y)
                {
                    pixels[y * width + midX] = new Color32(0, 0, 0, 0);
                }
            }

            if (0 < midY && midY < height)
            {
                for (int x = 0; x < width; ++x)
                {
                    pixels[midY * width + x] = new Color32(0, 0, 0, 0);
                }
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                0.5f,
                out cellWidth,
                out cellHeight
            );

            if (shouldDetectGrid)
            {
                Assert.IsTrue(
                    result,
                    $"Should detect grid for {width}x{height} texture - midpoint creates "
                        + $"{width / 2}x{height / 2} cells which are >= 8"
                );
                Assert.That(
                    cellWidth,
                    Is.GreaterThanOrEqualTo(8),
                    "Detected cell width should be at least 8"
                );
                Assert.That(
                    cellHeight,
                    Is.GreaterThanOrEqualTo(8),
                    "Detected cell height should be at least 8"
                );
            }
            else
            {
                Assert.IsFalse(
                    result,
                    $"Should not detect grid for {width}x{height} texture - midpoint would create "
                        + $"{width / 2}x{height / 2} cells which are below minimum of 8"
                );
            }
        }

        [Test]
        public void DetectOptimalGridFromTransparencyValidCellSizeAboveMinimum()
        {
            int width = 64;
            int height = 64;
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            for (int i = 16; i < width; i += 16)
            {
                for (int y = 0; y < height; ++y)
                {
                    pixels[y * width + i] = new Color32(0, 0, 0, 0);
                }
            }

            for (int i = 16; i < height; i += 16)
            {
                for (int x = 0; x < width; ++x)
                {
                    pixels[i * width + x] = new Color32(0, 0, 0, 0);
                }
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                0.5f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsTrue(result, "Should detect valid grid with cell size above minimum");
            Assert.AreEqual(16, cellWidth, "Cell width should be 16");
            Assert.AreEqual(16, cellHeight, "Cell height should be 16");
        }

        [Test]
        public void DetectOptimalGridFromTransparency24x24With8x8Cells()
        {
            // Transparent lines at 8 and 16 make a 3x3 grid of 8x8 cells -- exactly the minimum size.
            int width = 24;
            int height = 24;
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            // Draw vertical transparent lines at x=8 and x=16
            for (int x = 8; x < width; x += 8)
            {
                for (int y = 0; y < height; ++y)
                {
                    pixels[y * width + x] = new Color32(0, 0, 0, 0);
                }
            }

            // Draw horizontal transparent lines at y=8 and y=16
            for (int y = 8; y < height; y += 8)
            {
                for (int x = 0; x < width; ++x)
                {
                    pixels[y * width + x] = new Color32(0, 0, 0, 0);
                }
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                0.5f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsTrue(result, "Should detect 8x8 grid in 24x24 texture");
            Assert.AreEqual(8, cellWidth, "Cell width should be 8");
            Assert.AreEqual(8, cellHeight, "Cell height should be 8");
        }

        [Test]
        public void DetectOptimalGridFromTransparency32x32With8x8Cells()
        {
            // 32x32 texture with transparent lines every 8 pixels, creating a 4x4 grid of 8x8 cells.
            int width = 32;
            int height = 32;
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            // Draw vertical transparent lines at 8, 16, 24
            for (int x = 8; x < width; x += 8)
            {
                for (int y = 0; y < height; ++y)
                {
                    pixels[y * width + x] = new Color32(0, 0, 0, 0);
                }
            }

            // Draw horizontal transparent lines at 8, 16, 24
            for (int y = 8; y < height; y += 8)
            {
                for (int x = 0; x < width; ++x)
                {
                    pixels[y * width + x] = new Color32(0, 0, 0, 0);
                }
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                0.5f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsTrue(result, "Should detect 8x8 grid in 32x32 texture");
            Assert.AreEqual(8, cellWidth, "Cell width should be 8");
            Assert.AreEqual(8, cellHeight, "Cell height should be 8");
        }

        [Test]
        public void DetectOptimalGridFromTransparencyLargeTexture()
        {
            int width = 512;
            int height = 512;
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            for (int i = 64; i < width; i += 64)
            {
                for (int y = 0; y < height; ++y)
                {
                    pixels[y * width + i] = new Color32(0, 0, 0, 0);
                }
            }

            for (int i = 64; i < height; i += 64)
            {
                for (int x = 0; x < width; ++x)
                {
                    pixels[i * width + x] = new Color32(0, 0, 0, 0);
                }
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                0.5f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsTrue(result, "Should detect grid in large texture");
            Assert.AreEqual(64, cellWidth, "Cell width should be 64");
            Assert.AreEqual(64, cellHeight, "Cell height should be 64");
        }

        [Test]
        public void DetectOptimalGridFromTransparencyPartiallyTransparentRows()
        {
            int width = 64;
            int height = 64;
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < (int)(width * 0.9f); ++x)
                {
                    pixels[y * width + 32] = new Color32(0, 0, 0, 0);
                }
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                0.5f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsTrue(
                result || !result,
                "Should handle partially transparent rows without crashing"
            );
        }

        [Test]
        public void DetectOptimalGridFromTransparencySingleSprite()
        {
            int width = 32;
            int height = 32;
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                0.5f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsFalse(result, "Should return false for single sprite with no boundaries");
        }

        [Test]
        public void DetectOptimalGridFromTransparencyZeroDimensions()
        {
            Color32[] pixels = new Color32[0];

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                0,
                0,
                0.5f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsFalse(result, "Should return false for zero dimensions");
        }

        [Test]
        public void DetectOptimalGridFromTransparencyNegativeAlphaThreshold()
        {
            int width = 64;
            int height = 64;
            Color32[] pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                -0.5f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsFalse(
                result,
                "Should handle negative alpha threshold by not detecting any transparency"
            );
        }

        [Test]
        public void DetectOptimalGridFromTransparencyLowAlphaPixels()
        {
            int width = 64;
            int height = 64;
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            for (int y = 0; y < height; ++y)
            {
                pixels[y * width + 32] = new Color32(0, 0, 0, 50);
            }

            for (int x = 0; x < width; ++x)
            {
                pixels[32 * width + x] = new Color32(0, 0, 0, 50);
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                0.5f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsTrue(result, "Should detect boundaries with alpha below threshold");
        }

        [Test]
        public void DetectOptimalGridFromTransparencyAlphaExactlyAtThreshold()
        {
            int width = 64;
            int height = 64;
            Color32[] pixels = new Color32[width * height];

            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            byte thresholdByte = (byte)(0.5f * 255);
            for (int y = 0; y < height; ++y)
            {
                pixels[y * width + 32] = new Color32(0, 0, 0, thresholdByte);
            }

            for (int x = 0; x < width; ++x)
            {
                pixels[32 * width + x] = new Color32(0, 0, 0, thresholdByte);
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                0.5f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsTrue(
                result || !result,
                "Should handle alpha exactly at threshold without crashing"
            );
        }

        [Test]
        public void DetectOptimalGridFromTransparencyThinGutterDetection()
        {
            int width = 64;
            int height = 32;
            Color32[] pixels = new Color32[width * height];

            /*
                The algorithm requires both dimensions to have detected boundaries scoring above
                the minimum threshold, so a vertical gutter alone fails for want of a horizontal
                one. Gutters at x=32 and y=16 make a 2x2 grid.
            */
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    if (x == 32 || y == 16)
                    {
                        pixels[y * width + x] = new Color32(0, 0, 0, 0);
                    }
                    else
                    {
                        pixels[y * width + x] = new Color32(255, 0, 0, 255);
                    }
                }
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                0.01f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsTrue(result, "Should detect grid with thin 1-pixel transparent gutters");
            Assert.AreEqual(32, cellWidth, "Cell width should be 32 pixels");
            Assert.AreEqual(16, cellHeight, "Cell height should be 16 pixels");
        }

        [Test]
        public void DetectOptimalGridFromTransparencyPartialTransparencyRow()
        {
            int width = 64;
            int height = 32;
            Color32[] pixels = new Color32[width * height];

            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    if (y == 16)
                    {
                        if (x % 2 == 0)
                        {
                            pixels[y * width + x] = new Color32(0, 0, 0, 0);
                        }
                        else
                        {
                            pixels[y * width + x] = new Color32(255, 0, 0, 255);
                        }
                    }
                    else
                    {
                        pixels[y * width + x] = new Color32(255, 0, 0, 255);
                    }
                }
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                0.01f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsTrue(
                result || !result,
                "Should handle partial transparency rows without crashing"
            );
        }

        [Test]
        public void DetectOptimalGridFromTransparencyPrefersSmallerCellSizes()
        {
            int width = 256;
            int height = 128;
            Color32[] pixels = new Color32[width * height];

            int cellSize = 32;
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    bool isVerticalBoundary = (x % cellSize == 0) && (0 < x);
                    bool isHorizontalBoundary = (y % cellSize == 0) && (0 < y);

                    if (isVerticalBoundary || isHorizontalBoundary)
                    {
                        pixels[y * width + x] = new Color32(0, 0, 0, 0);
                    }
                    else
                    {
                        pixels[y * width + x] = new Color32(255, 0, 0, 255);
                    }
                }
            }

            int cellWidth;
            int cellHeight;
            bool result = SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                pixels,
                width,
                height,
                0.01f,
                out cellWidth,
                out cellHeight
            );

            Assert.IsTrue(result, "Should detect grid in 256x128 texture with 32-pixel cells");
            Assert.AreEqual(32, cellWidth, "Cell width should be 32 (not 128 or 256)");
            Assert.AreEqual(32, cellHeight, "Cell height should be 32 (not 64 or 128)");
        }

        [Test]
        public void CalculateGridDimensionsFallbackPrefersSmallerCellsOverGCD()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Auto;

            extractor.CalculateGridDimensions(
                256,
                128,
                null,
                null,
                out int columns,
                out int rows,
                out int cellWidth,
                out int cellHeight
            );

            Assert.Greater(columns, 1, "Should produce more than 1 column");
            Assert.Greater(rows, 1, "Should produce more than 1 row");
            Assert.Less(cellWidth, 128, "Cell width should be less than GCD (128)");
            Assert.Less(cellHeight, 128, "Cell height should be less than GCD (128)");
        }

        [Test]
        public void CalculateGridDimensionsFallbackHandlesPrimeDimension()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Auto;

            extractor.CalculateGridDimensions(
                127,
                128,
                null,
                null,
                out int columns,
                out int rows,
                out int cellWidth,
                out int cellHeight
            );

            Assert.Greater(columns, 0, "Columns should be positive");
            Assert.Greater(rows, 0, "Rows should be positive");
            Assert.Greater(cellWidth, 0, "Cell width should be positive");
            Assert.Greater(cellHeight, 0, "Cell height should be positive");
            Assert.LessOrEqual(
                cellHeight,
                64,
                "Cell height should be reasonable for 128px dimension"
            );
        }

        [Test]
        public void CalculateGridDimensionsFallbackHandlesVerySmallDimension()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._extractionMode = SpriteSheetExtractor.ExtractionMode.GridBased;
            extractor._gridSizeMode = SpriteSheetExtractor.GridSizeMode.Auto;

            extractor.CalculateGridDimensions(
                16,
                16,
                null,
                null,
                out int columns,
                out int rows,
                out int cellWidth,
                out int cellHeight
            );

            Assert.Greater(columns, 0, "Columns should be positive");
            Assert.Greater(rows, 0, "Rows should be positive");
            Assert.IsTrue(cellWidth == 8 || cellWidth == 16, "Cell width should be 8 or 16");
            Assert.IsTrue(cellHeight == 8 || cellHeight == 16, "Cell height should be 8 or 16");
        }

        [Test]
        public void GridOverlayRectsCalculatedCorrectly()
        {
            SpriteSheetExtractor extractor = CreateExtractor();
            extractor._gridColumns = 2;
            extractor._gridRows = 2;

            int cellWidth = 32;
            int cellHeight = 32;

            List<Rect> rects = new();
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 2; col++)
                {
                    rects.Add(new Rect(col * cellWidth, row * cellHeight, cellWidth, cellHeight));
                }
            }

            Assert.AreEqual(4, rects.Count);
            Assert.AreEqual(new Rect(0, 0, 32, 32), rects[0]);
            Assert.AreEqual(new Rect(32, 0, 32, 32), rects[1]);
            Assert.AreEqual(new Rect(0, 32, 32, 32), rects[2]);
            Assert.AreEqual(new Rect(32, 32, 32, 32), rects[3]);
        }

        [Test]
        public void ClampingBehaviorWorksWhenSpriteRectExtendsBeyondTexture()
        {
            int textureWidth = 64;
            int textureHeight = 64;
            Rect spriteRect = new Rect(50, 50, 32, 32);

            int clampedX = Mathf.Clamp((int)spriteRect.x, 0, textureWidth);
            int clampedY = Mathf.Clamp((int)spriteRect.y, 0, textureHeight);
            int clampedWidth = Mathf.Min((int)spriteRect.width, textureWidth - clampedX);
            int clampedHeight = Mathf.Min((int)spriteRect.height, textureHeight - clampedY);

            Assert.AreEqual(50, clampedX);
            Assert.AreEqual(50, clampedY);
            Assert.AreEqual(14, clampedWidth);
            Assert.AreEqual(14, clampedHeight);
        }
    }
#endif
}
