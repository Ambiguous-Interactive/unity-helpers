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
        }
    }
#endif
}
