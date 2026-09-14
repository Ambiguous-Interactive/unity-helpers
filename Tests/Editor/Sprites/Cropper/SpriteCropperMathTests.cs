// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
#if UNITY_EDITOR
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Sprites;

    /// <summary>
    /// PURE crop-geometry tests for <see cref="SpriteCropper.ComputeCrop"/> (alpha-bounded
    /// rect + padding + pivot). These build an in-memory pixel buffer and call the extracted
    /// math directly -- NO texture asset creation/import -- so each case runs in microseconds
    /// instead of the ~6s/case full import round-trip the equivalent integration cases cost.
    /// The dimension/padding/pivot coverage here is identical to what
    /// <c>SpriteCropperAdditionalTests</c> previously verified per-case; that fixture retains
    /// the integration tests that exercise the actual AssetDatabase import/output wiring.
    /// No Unity objects are created, so this fixture does NOT inherit CommonTestBase.
    /// </summary>
    [TestFixture]
    public sealed class SpriteCropperMathTests
    {
        private static Color32[] OpaqueRect(
            int width,
            int height,
            int rectX,
            int rectY,
            int rectW,
            int rectH
        )
        {
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; ++y)
            for (int x = 0; x < width; ++x)
            {
                bool inRect = rectX <= x && x < rectX + rectW && rectY <= y && y < rectY + rectH;
                pixels[y * width + x] = inRect
                    ? new Color32(255, 255, 255, 255)
                    : new Color32(0, 0, 0, 0);
            }
            return pixels;
        }

        [TestCase(1_024, 1_024, false)]
        [TestCase(2_048, 2_048, false)]
        [TestCase(4_096, 2_047, false)]
        [TestCase(32_768, 256, false)]
        [TestCase(4_096, 2_048, true)]
        [TestCase(8_192, 1_024, true)]
        [TestCase(16_384, 512, true)]
        public void ScanParallelizationRequiresEnoughPixelsAndRows(
            int width,
            int height,
            bool expected
        )
        {
            Assert.AreEqual(expected, SpriteCropper.ShouldScanPixelsInParallel(width, height));
        }

        [Test]
        public void ParallelAndSequentialScansProduceIdenticalBounds()
        {
            const int Width = 8;
            const int Height = 6;
            Color32[] thresholdEdges = new Color32[Width * Height];
            thresholdEdges[1 * Width + 1] = new Color32(255, 255, 255, 129);
            thresholdEdges[4 * Width + 6] = new Color32(255, 255, 255, 129);
            thresholdEdges[3 * Width + 3] = new Color32(255, 255, 255, 128);
            Color32[][] scenarios =
            {
                new Color32[Width * Height],
                OpaqueRect(Width, Height, 0, 0, Width, Height),
                thresholdEdges,
            };

            foreach (Color32[] pixels in scenarios)
            {
                SpriteCropper.VisibleBounds sequential =
                    SpriteCropper.FindVisibleBoundsSequentially(pixels, Width, Height, 128);
                SpriteCropper.VisibleBounds parallel = SpriteCropper.FindVisibleBoundsInParallel(
                    pixels,
                    Width,
                    Height,
                    128
                );

                Assert.AreEqual(sequential.HasVisible, parallel.HasVisible);
                Assert.AreEqual(sequential.MinX, parallel.MinX);
                Assert.AreEqual(sequential.MinY, parallel.MinY);
                Assert.AreEqual(sequential.MaxX, parallel.MaxX);
                Assert.AreEqual(sequential.MaxY, parallel.MaxY);
            }
        }

        [TestCase(1, 1, 0, 0, 1, 1, 1, 1)]
        [TestCase(2, 2, 0, 0, 2, 2, 2, 2)]
        [TestCase(100, 100, 25, 25, 50, 50, 50, 50)]
        [TestCase(64, 32, 10, 5, 20, 10, 20, 10)]
        [TestCase(32, 64, 5, 10, 10, 20, 10, 20)]
        [TestCase(256, 256, 0, 0, 1, 1, 1, 1)]
        [TestCase(256, 256, 128, 128, 1, 1, 1, 1)]
        public void CropsToExpectedDimensionsForVariousSizes(
            int srcWidth,
            int srcHeight,
            int opaqueX,
            int opaqueY,
            int opaqueW,
            int opaqueH,
            int expectedWidth,
            int expectedHeight
        )
        {
            Color32[] pixels = OpaqueRect(srcWidth, srcHeight, opaqueX, opaqueY, opaqueW, opaqueH);
            SpriteCropper.CropComputation crop = SpriteCropper.ComputeCrop(
                pixels,
                srcWidth,
                srcHeight,
                0,
                0,
                0,
                0,
                0f,
                new Vector2(0.5f, 0.5f),
                false
            );
            Assert.That(crop.CropWidth, Is.EqualTo(expectedWidth), "crop width");
            Assert.That(crop.CropHeight, Is.EqualTo(expectedHeight), "crop height");
        }

        // ComputeCrop takes top before bottom; these legacy cases list bottom before top.
        [TestCase(0, 0, 0, 0)]
        [TestCase(1, 0, 0, 0)]
        [TestCase(0, 1, 0, 0)]
        [TestCase(0, 0, 1, 0)]
        [TestCase(0, 0, 0, 1)]
        [TestCase(1, 1, 1, 1)]
        [TestCase(5, 5, 5, 5)]
        [TestCase(10, 0, 0, 10)]
        public void AppliesPaddingCorrectly(
            int leftPadding,
            int rightPadding,
            int bottomPadding,
            int topPadding
        )
        {
            Color32[] pixels = OpaqueRect(20, 20, 5, 5, 10, 10);
            SpriteCropper.CropComputation crop = SpriteCropper.ComputeCrop(
                pixels,
                20,
                20,
                leftPadding,
                rightPadding,
                topPadding,
                bottomPadding,
                0f,
                new Vector2(0.5f, 0.5f),
                false
            );
            Assert.That(crop.CropWidth, Is.EqualTo(10 + leftPadding + rightPadding), "crop width");
            Assert.That(
                crop.CropHeight,
                Is.EqualTo(10 + bottomPadding + topPadding),
                "crop height"
            );
        }

        [TestCase("TopLeft", 0, 9, 1, 1)]
        [TestCase("TopRight", 9, 9, 1, 1)]
        [TestCase("BottomLeft", 0, 0, 1, 1)]
        [TestCase("BottomRight", 9, 0, 1, 1)]
        [TestCase("LeftEdge", 0, 0, 1, 10)]
        [TestCase("RightEdge", 9, 0, 1, 10)]
        [TestCase("TopEdge", 0, 9, 10, 1)]
        [TestCase("BottomEdge", 0, 0, 10, 1)]
        public void CropsEdgeContentCorrectly(
            string edgeName,
            int opaqueX,
            int opaqueY,
            int opaqueW,
            int opaqueH
        )
        {
            Color32[] pixels = OpaqueRect(10, 10, opaqueX, opaqueY, opaqueW, opaqueH);
            SpriteCropper.CropComputation crop = SpriteCropper.ComputeCrop(
                pixels,
                10,
                10,
                0,
                0,
                0,
                0,
                0f,
                new Vector2(0.5f, 0.5f),
                false
            );
            Assert.That(crop.CropWidth, Is.EqualTo(opaqueW), $"crop width for edge '{edgeName}'");
            Assert.That(crop.CropHeight, Is.EqualTo(opaqueH), $"crop height for edge '{edgeName}'");
        }

        [Test]
        public void AdjustsPivotForAsymmetricPadding()
        {
            /*
                Cropping shifts the original center and adds left/bottom padding, placing the new pivot at (7,8)
                in a 13x13 image.
            */
            Color32[] pixels = OpaqueRect(20, 20, 5, 5, 10, 10);
            SpriteCropper.CropComputation crop = SpriteCropper.ComputeCrop(
                pixels,
                20,
                20,
                2,
                1,
                0,
                3,
                0f,
                new Vector2(0.5f, 0.5f),
                false
            );
            Assert.That(crop.CropWidth, Is.EqualTo(13), "crop width");
            Assert.That(crop.CropHeight, Is.EqualTo(13), "crop height");
            Assert.That(crop.NewPivot.x, Is.EqualTo(7f / 13f).Within(1e-3f), "pivot x");
            Assert.That(crop.NewPivot.y, Is.EqualTo(8f / 13f).Within(1e-3f), "pivot y");
        }

        [Test]
        public void FullyTransparentImageProducesOneByOneCenterPivot()
        {
            Color32[] pixels = OpaqueRect(8, 8, 0, 0, 0, 0);
            SpriteCropper.CropComputation crop = SpriteCropper.ComputeCrop(
                pixels,
                8,
                8,
                0,
                0,
                0,
                0,
                0f,
                new Vector2(0.5f, 0.5f),
                false
            );
            Assert.That(crop.HasVisible, Is.False, "no visible pixels");
            Assert.That(crop.CropWidth, Is.EqualTo(1), "1px width");
            Assert.That(crop.CropHeight, Is.EqualTo(1), "1px height");
            Assert.That(crop.NewPivot, Is.EqualTo(new Vector2(0.5f, 0.5f)), "center pivot");
        }
    }
#endif
}
