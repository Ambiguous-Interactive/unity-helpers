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
        [TestCase(0, 0, 4, 3, false, TextureFormat.RGBA32)]
        [TestCase(0, 0, 4, 3, false, TextureFormat.RGB24)]
        [TestCase(1, 2, 3, 2, false, TextureFormat.RGBA32)]
        [TestCase(1, 2, 3, 2, false, TextureFormat.RGB24)]
        [TestCase(4, 5, 4, 3, true, TextureFormat.RGBA32)]
        [TestCase(4, 5, 4, 3, true, TextureFormat.RGB24)]
        [TestCase(7, 7, 1, 1, true, TextureFormat.RGBA32)]
        [TestCase(7, 7, 1, 1, true, TextureFormat.RGB24)]
        public void ReadableSubregionCopiesOnlyItsBaseMipPixels(
            int x,
            int y,
            int width,
            int height,
            bool mipmaps,
            TextureFormat format
        )
        {
            const int textureSize = 8;
            Texture2D texture = Track(new Texture2D(textureSize, textureSize, format, mipmaps));
            Color32[] original = new Color32[textureSize * textureSize];
            for (int index = 0; index < original.Length; ++index)
            {
                original[index] = new Color32(
                    (byte)(index * 3),
                    (byte)(255 - index),
                    (byte)(index * 2),
                    (byte)(index * 4)
                );
            }
            texture.SetPixels32(original);
            texture.Apply();
            original = texture.GetPixels32();
            Sprite sprite = Track(
                Sprite.Create(texture, new Rect(x, y, width, height), new Vector2(0.25f, 0.75f), 2f)
            );
            Assert.IsTrue(
                SpriteMaskReader.TryReadPixels(
                    sprite,
                    out SpritePixelRegion region,
                    out string error
                ),
                error
            );
            Assert.AreEqual(width * height, region.Pixels.Length);
            Assert.AreEqual(width, region.TextureWidth);
            Assert.AreEqual(new RectInt(0, 0, width, height), region.Region);
            Assert.AreEqual(new Vector2(width * 0.25f, height * 0.75f), region.Pivot);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), region.UnitsPerPixel);
            for (int row = 0; row < height; ++row)
            {
                for (int column = 0; column < width; ++column)
                {
                    Assert.AreEqual(
                        original[(y + row) * textureSize + x + column],
                        region.GetPixel(column, row)
                    );
                }
            }
            CollectionAssert.AreEqual(original, texture.GetPixels32());
            region.Pixels[0] = default;
            CollectionAssert.AreEqual(original, texture.GetPixels32());
        }

        [TestCase(TextureFormat.ARGB32)]
        [TestCase(TextureFormat.RGB24)]
        [TestCase(TextureFormat.Alpha8)]
        public void OtherReadableFormatsKeepTheirConvertedPixelSemantics(TextureFormat format)
        {
            Texture2D texture = Track(new Texture2D(4, 4, format, false));
            Color32[] input = new Color32[16];
            for (int index = 0; index < input.Length; ++index)
            {
                input[index] = new Color32(
                    (byte)(index * 7),
                    (byte)(index * 11),
                    (byte)(index * 13),
                    (byte)(index * 17)
                );
            }
            texture.SetPixels32(input);
            texture.Apply();
            Color32[] expected = texture.GetPixels32();
            Sprite sprite = Track(Sprite.Create(texture, new Rect(1, 1, 2, 2), Vector2.zero));
            Assert.IsTrue(
                SpriteMaskReader.TryReadPixels(
                    sprite,
                    out SpritePixelRegion region,
                    out string error
                ),
                error
            );
            for (int row = 0; row < 2; ++row)
            {
                for (int column = 0; column < 2; ++column)
                {
                    Assert.AreEqual(
                        expected[(row + 1) * 4 + column + 1],
                        region.GetPixel(column, row)
                    );
                }
            }
        }

        [Test]
        public void InkQueryUsesTheSelectedSpriteRectangleAndItsLocalMapping()
        {
            Texture2D texture = Track(new Texture2D(4, 2, TextureFormat.RGBA32, false));
            Color32[] pixels =
            {
                new(0, 0, 0, 255),
                new(255, 255, 255, 255),
                new(255, 255, 255, 255),
                new(0, 0, 0, 255),
                new(0, 0, 0, 255),
                new(255, 255, 255, 255),
                new(255, 255, 255, 255),
                new(0, 0, 0, 255),
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            Sprite sprite = Track(
                Sprite.Create(texture, new Rect(1, 0, 2, 2), new Vector2(0.25f, 0.75f), 2)
            );
            Assert.That(
                SpriteArtBounds.TryGetCentreOfLargestDarkestInkRegion(
                    sprite,
                    1f,
                    out Vector2 centre,
                    out string error
                ),
                Is.True,
                error
            );
            Assert.That(centre, Is.EqualTo(new Vector2(0.25f, -0.25f)));
            Assert.That(error, Is.Null);
            Assert.That(texture.isReadable, Is.True);
        }

        [Test]
        public void InkQueryRefusesMissingDestroyedAndUnreadableInputs()
        {
            Assert.That(
                SpriteArtBounds.TryGetCentreOfLargestDarkestInkRegion(
                    null,
                    0f,
                    out Vector2 centre,
                    out string error
                ),
                Is.False
            );
            Assert.That(centre, Is.EqualTo(Vector2.zero));
            Assert.That(string.IsNullOrWhiteSpace(error), Is.False);
            Texture2D texture = Track(new Texture2D(2, 2));
            Sprite sprite = Track(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero));
            texture.Apply(false, true);
            Assert.That(
                SpriteArtBounds.TryGetCentreOfLargestDarkestInkRegion(
                    sprite,
                    0f,
                    out centre,
                    out error
                ),
                Is.False
            );
            Assert.That(error, Does.Contain("readable"));
            Assert.That(texture.isReadable, Is.False);
            UnityEngine.Object.DestroyImmediate(sprite); // UNH-SUPPRESS UNH001: Destroyed sprite behavior is under test; tracked for teardown.
            Assert.That(
                SpriteArtBounds.TryGetCentreOfLargestDarkestInkRegion(
                    sprite,
                    0f,
                    out centre,
                    out error
                ),
                Is.False
            );
            Assert.That(centre, Is.EqualTo(Vector2.zero));
        }

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
