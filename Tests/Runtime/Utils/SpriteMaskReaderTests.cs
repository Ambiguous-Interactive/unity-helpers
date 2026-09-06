// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Fast")]
    public sealed class SpriteMaskReaderTests : CommonTestBase
    {
        [TestCase(0, true, true)]
        [TestCase(1, true, true)]
        [TestCase(128, true, false)]
        [TestCase(255, false, false)]
        public void ReadsSpriteRectangleThresholdPivotAndPixelSize(
            int threshold,
            bool first,
            bool second
        )
        {
            Texture2D texture = Track(new Texture2D(4, 2, TextureFormat.RGBA32, false));
            Color32[] pixels = new Color32[8];
            pixels[1] = new Color32(255, 255, 255, 128);
            pixels[2] = new Color32(255, 255, 255, 1);
            pixels[6] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            Sprite sprite = Track(
                Sprite.Create(texture, new Rect(1, 0, 2, 2), new Vector2(0.25f, 0.75f), 2)
            );
            Assert.IsTrue(
                SpriteMaskReader.TryRead(
                    sprite,
                    out SpriteAlphaMask mask,
                    out string error,
                    (byte)threshold
                ),
                error
            );
            Assert.AreEqual(2, mask.Width);
            Assert.AreEqual(2, mask.Height);
            Assert.AreEqual(new Vector2(0.5f, 1.5f), mask.Pivot);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), mask.UnitsPerPixel);
            CollectionAssert.AreEqual(new[] { first, second, false, true }, mask.Pixels);
            Assert.IsTrue(texture.isReadable);
        }

        [Test]
        public void NonReadableRuntimeSpriteReturnsExplicitFailure()
        {
            Texture2D texture = Track(new Texture2D(2, 2));
            Sprite sprite = Track(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero));
            texture.Apply(false, true);
            Assert.IsFalse(
                SpriteMaskReader.TryRead(sprite, out SpriteAlphaMask mask, out string error)
            );
            Assert.IsTrue(mask.Pixels == null);
            StringAssert.Contains("readable", error);
            Assert.IsFalse(texture.isReadable);
        }

        [Test]
        public void NullAndDestroyedSpritesReturnExplicitFailure()
        {
            Assert.IsFalse(
                SpriteMaskReader.TryRead(null, out SpriteAlphaMask mask, out string error)
            );
            Assert.IsTrue(mask.Pixels == null);
            Assert.IsFalse(string.IsNullOrEmpty(error));
            Texture2D texture = Track(new Texture2D(2, 2));
            Sprite sprite = Track(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero));
            UnityEngine.Object.DestroyImmediate(sprite); // UNH-SUPPRESS: Exercises destroyed sprite handling.
            Assert.IsFalse(SpriteMaskReader.TryRead(sprite, out mask, out error));
            Assert.IsTrue(mask.Pixels == null);
            Assert.IsFalse(string.IsNullOrEmpty(error));
        }
    }
}
