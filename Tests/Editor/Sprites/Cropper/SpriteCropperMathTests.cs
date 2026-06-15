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
                bool inRect = x >= rectX && x < rectX + rectW && y >= rectY && y < rectY + rectH;
                pixels[y * width + x] = inRect
                    ? new Color32(255, 255, 255, 255)
                    : new Color32(0, 0, 0, 0);
            }
            return pixels;
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

        // ComputeCrop arg order is (left, right, top, bottom); the TestCase order matches the
        // old AppliesPaddingCorrectly(left, right, bottom, top) signature, so map carefully.
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
            // 20x20, opaque 10x10 at (5,5), center pivot; pad left=2,right=1,top=0,bottom=3.
            // Crop 13x13; new pivot pixels = (10-5+2, 10-5+3) = (7,8) -> (7/13, 8/13).
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
            Color32[] pixels = OpaqueRect(8, 8, 0, 0, 0, 0); // no opaque pixels
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
