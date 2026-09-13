// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Sprites
{
#if UNITY_EDITOR
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SpriteSheetExtractorPixelBufferTests : CommonTestBase
    {
        [Test]
        public void PixelBufferOperationsCopyOnlyLogicalPrefixes()
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

            Color32[] source =
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
            Color32 sentinel = new(200, 201, 202, 255);
            Color32[] destination = new Color32[8];
            destination[6] = sentinel;
            destination[7] = sentinel;

            SpriteSheetExtractor.CopyPixelRows(source, 4, 1, 0, 3, 2, destination);

            Color32[] expectedRegion =
            {
                source[1],
                source[2],
                source[3],
                source[5],
                source[6],
                source[7],
                sentinel,
                sentinel,
            };
            CollectionAssert.AreEqual(expectedRegion, destination);

            if (
                SpriteSheetExtractor.ShouldCopyPixelsInParallel(256, 256)
                || SpriteSheetExtractor.ShouldCopyPixelsInParallel(4096, 16)
                || !SpriteSheetExtractor.ShouldCopyPixelsInParallel(2048, 512)
                || SpriteSheetExtractor.ShouldCopyPixelsInParallel(2048, 511)
                || SpriteSheetExtractor.ShouldCopyPixelsInParallel(2047, 512)
                || !SpriteSheetExtractor.ShouldCopyPixelsInParallel(1024, 1024)
                || !SpriteSheetExtractor.ShouldCopyPixelsInParallel(4096, 4096)
                || SpriteCropper.ShouldCopyPixelsInParallel(256, 256)
                || SpriteCropper.ShouldCopyPixelsInParallel(4096, 16)
                || !SpriteCropper.ShouldCopyPixelsInParallel(2048, 512)
                || SpriteCropper.ShouldCopyPixelsInParallel(2048, 511)
                || SpriteCropper.ShouldCopyPixelsInParallel(2047, 512)
                || !SpriteCropper.ShouldCopyPixelsInParallel(1024, 1024)
                || !SpriteCropper.ShouldCopyPixelsInParallel(4096, 4096)
            )
            {
                Assert.Fail("Size-aware pixel-copy parallelism selected an unexpected path.");
            }

            const int largeWidth = 1024;
            const int largeHeight = 1024;
            Color32[] largeSource = new Color32[largeWidth * largeHeight];
            Color32[] largeDestination = new Color32[largeSource.Length];
            for (int index = 0; index < largeSource.Length; ++index)
            {
                largeSource[index] = new Color32(
                    (byte)index,
                    (byte)(index >> 8),
                    (byte)(index >> 16),
                    255
                );
            }

            SpriteSheetExtractor.CopyPixelRows(
                largeSource,
                largeWidth,
                0,
                0,
                largeWidth,
                largeHeight,
                largeDestination
            );

            for (int index = 0; index < largeDestination.Length; ++index)
            {
                if (!largeDestination[index].Equals(largeSource[index]))
                {
                    Assert.Fail($"Parallel pixel copy corrupted index {index}.");
                }
            }

            const int cropSourceSize = 1022;
            Color32[] cropSource = new Color32[cropSourceSize * cropSourceSize];
            for (int index = 0; index < cropSource.Length; ++index)
            {
                cropSource[index] = new Color32(
                    (byte)(index + 1),
                    (byte)((index >> 8) + 1),
                    (byte)((index >> 16) + 1),
                    255
                );
            }

            Color32[] cropDestination = new Color32[largeWidth * largeHeight];
            Color32 cropSentinel = new(201, 202, 203, 255);
            System.Array.Fill(cropDestination, cropSentinel);

            SpriteCropper.CopyCropPixels(
                cropSource,
                cropSourceSize,
                cropSourceSize,
                -1,
                -1,
                cropSourceSize,
                cropSourceSize,
                largeWidth,
                largeHeight,
                cropDestination
            );

            for (int row = 0; row < largeHeight; ++row)
            {
                for (int column = 0; column < largeWidth; ++column)
                {
                    bool padding =
                        0 == row
                        || largeHeight - 1 == row
                        || 0 == column
                        || largeWidth - 1 == column;
                    Color32 actual = cropDestination[row * largeWidth + column];
                    Color32 expectedCrop = padding
                        ? default
                        : cropSource[(row - 1) * cropSourceSize + column - 1];
                    if (!actual.Equals(expectedCrop))
                    {
                        Assert.Fail($"Parallel padded crop corrupted ({column}, {row}).");
                    }
                }
            }
        }
    }
#endif
}
