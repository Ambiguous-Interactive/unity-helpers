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
        public void ApplyPixelBufferCopiesOnlyTheRequestedPooledPrefix()
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
        }
    }
#endif
}
