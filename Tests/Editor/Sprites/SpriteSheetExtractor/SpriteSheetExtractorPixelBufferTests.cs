// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Sprites
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Integration")]
    public sealed class SpriteSheetExtractorPixelBufferTests : CommonTestBase
    {
        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            CleanupTrackedFoldersAndAssets();
        }

        [Test]
        public void PooledPixelBuffersPreservePreviewAndExtractionOutput()
        {
            Texture2D texture = Track(new Texture2D(3, 1, TextureFormat.RGBA32, false));
            Color32[] oversizedBuffer =
            {
                new(1, 2, 3, 255),
                new(4, 5, 6, 255),
                new(7, 8, 9, 255),
                new(200, 201, 202, 255),
            };

            SpriteSheetExtractor.ApplyPixelBuffer(texture, 3, 1, oversizedBuffer);

            Color32[] expected = { oversizedBuffer[0], oversizedBuffer[1], oversizedBuffer[2] };
            CollectionAssert.AreEqual(expected, texture.GetPixels32());

            const string outputDirectory = "Assets/Temp/SpriteSheetExtractorPixelBufferTests";
            EnsureFolder(outputDirectory);

            Color32[] sourcePixels =
            {
                new(10, 11, 12, 255),
                new(20, 21, 22, 255),
                new(30, 31, 32, 255),
                new(40, 41, 42, 255),
                new(50, 51, 52, 255),
                new(60, 61, 62, 255),
                new(70, 71, 72, 255),
                new(80, 81, 82, 255),
            };
            Texture2D source = Track(new Texture2D(4, 2, TextureFormat.RGBA32, false));
            source.SetPixels32(sourcePixels);
            source.Apply();

            SpriteSheetExtractor extractor = Track(
                ScriptableObject.CreateInstance<SpriteSheetExtractor>()
            );
            extractor._namingPrefix = "pooled";
            extractor._overwriteExisting = true;
            extractor._preserveImportSettings = false;
            extractor._previewSizeMode = SpriteSheetExtractor.PreviewSizeMode.RealSize;

            SpriteSheetExtractor.SpriteSheetEntry sheet = new()
            {
                _assetPath = "Assets/pooled-source.png",
                _texture = source,
            };
            SpriteSheetExtractor.SpriteEntryData sprite = new()
            {
                _originalName = "region",
                _rect = new Rect(1, 0, 3, 2),
            };
            Color32[] expectedRegion =
            {
                sourcePixels[1],
                sourcePixels[2],
                sourcePixels[3],
                sourcePixels[5],
                sourcePixels[6],
                sourcePixels[7],
            };

            Texture2D preview = Track(extractor.GeneratePreviewTexture(sourcePixels, 4, 2, sprite));
            CollectionAssert.AreEqual(expectedRegion, preview.GetPixels32());

            string immediatePath = Path.Combine(outputDirectory, "pooled_000.png");
            string deferredPath = Path.Combine(outputDirectory, "pooled_001.png");
            TrackAssetPath(immediatePath);
            TrackAssetPath(deferredPath);

            bool extracted = extractor.ExtractSpriteImmediately(sheet, sprite, outputDirectory, 0);
            string deferredResult = extractor.ExtractSpriteWithoutImport(
                sheet,
                sprite,
                outputDirectory,
                1,
                new List<SpriteSheetExtractor.PendingImportSettings>()
            );

            Assert.That(extracted, Is.True);
            Assert.That(deferredResult, Is.EqualTo(deferredPath));
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            byte[] immediateBytes = File.ReadAllBytes(Path.Combine(projectRoot, immediatePath));
            byte[] deferredBytes = File.ReadAllBytes(Path.Combine(projectRoot, deferredPath));
            CollectionAssert.AreEqual(immediateBytes, deferredBytes);

            Texture2D decoded = Track(new Texture2D(2, 2, TextureFormat.RGBA32, false));
            Assert.That(ImageConversion.LoadImage(decoded, immediateBytes), Is.True);
            CollectionAssert.AreEqual(expectedRegion, decoded.GetPixels32());
        }
    }
#endif
}
