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

    /// <summary>
    /// Verifies numeric threshold guards with in-memory sprite pixels.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SpriteSheetExtractorNumericGuardTests : CommonTestBase
    {
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void AlphaDetectionRejectsNonfiniteThresholds(float threshold)
        {
            const int size = 32;
            Color32[] pixels = new Color32[size * size];
            for (int y = 4; y < 12; ++y)
            {
                for (int x = 4; x < 12; ++x)
                {
                    pixels[y * size + x] = new Color32(255, 255, 255, 255);
                }
            }

            Assert.IsFalse(
                SpriteSheetExtractor.DetectOptimalGridFromTransparency(
                    pixels,
                    size,
                    size,
                    threshold,
                    out int width,
                    out int height
                )
            );
            Assert.AreEqual(0, width);
            Assert.AreEqual(0, height);
            Assert.AreEqual(
                (0, 0),
                SpriteSheetExtractor.DetectCellSizeFromOpaqueRegions(pixels, size, size, threshold)
            );
            Assert.IsFalse(
                SpriteSheetExtractor.VerifyGridDoesNotCutSprites(
                    pixels,
                    size,
                    size,
                    16,
                    16,
                    threshold
                )
            );
            List<Rect> bounds = new() { new Rect(1f, 1f, 2f, 2f) };
            SpriteSheetExtractor.DetectSpriteBoundsByAlpha(pixels, size, size, threshold, bounds);
            Assert.IsEmpty(bounds);
        }
    }
#endif
}
