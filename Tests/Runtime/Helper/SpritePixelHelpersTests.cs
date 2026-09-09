// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SpritePixelHelpersTests : CommonTestBase
    {
        private static readonly Regex InsideTextureErrorPattern = new(
            @"\[Sprite\].*ExtractSpriteRect requires a sprite rect inside its texture",
            RegexOptions.Compiled
        );

        private static IEnumerable<TestCaseData> RotationCases()
        {
            yield return new TestCaseData(
                2,
                3,
                true,
                new[]
                {
                    NewColor(1, 0),
                    NewColor(1, 1),
                    NewColor(1, 2),
                    NewColor(0, 0),
                    NewColor(0, 1),
                    NewColor(0, 2),
                }
            ).SetName("RotateTexture90.Clockwise.TwoByThree");

            yield return new TestCaseData(
                2,
                3,
                false,
                new[]
                {
                    NewColor(0, 2),
                    NewColor(0, 1),
                    NewColor(0, 0),
                    NewColor(1, 2),
                    NewColor(1, 1),
                    NewColor(1, 0),
                }
            ).SetName("RotateTexture90.Counterclockwise.TwoByThree");

            yield return new TestCaseData(
                3,
                2,
                true,
                new[]
                {
                    NewColor(2, 0),
                    NewColor(2, 1),
                    NewColor(1, 0),
                    NewColor(1, 1),
                    NewColor(0, 0),
                    NewColor(0, 1),
                }
            ).SetName("RotateTexture90.Clockwise.ThreeByTwo");

            yield return new TestCaseData(
                3,
                2,
                false,
                new[]
                {
                    NewColor(0, 1),
                    NewColor(0, 0),
                    NewColor(1, 1),
                    NewColor(1, 0),
                    NewColor(2, 1),
                    NewColor(2, 0),
                }
            ).SetName("RotateTexture90.Counterclockwise.ThreeByTwo");

            yield return new TestCaseData(
                2,
                2,
                true,
                new[] { NewColor(1, 0), NewColor(1, 1), NewColor(0, 0), NewColor(0, 1) }
            ).SetName("RotateTexture90.Clockwise.TwoByTwo");

            yield return new TestCaseData(
                4,
                1,
                true,
                new[] { NewColor(3, 0), NewColor(2, 0), NewColor(1, 0), NewColor(0, 0) }
            ).SetName("RotateTexture90.Clockwise.FourByOne");

            yield return new TestCaseData(
                1,
                4,
                true,
                new[] { NewColor(0, 0), NewColor(0, 1), NewColor(0, 2), NewColor(0, 3) }
            ).SetName("RotateTexture90.Clockwise.OneByFour");
        }

        private static Color32[] BuildSourcePixels(int width, int height)
        {
            Color32[] pixels = new Color32[width * height];
            int index = 0;
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    pixels[index] = NewColor(x, y);
                    ++index;
                }
            }
            return pixels;
        }

        private static Color32 NewColor(int x, int y)
        {
            return new Color32((byte)(8 * x + 1), (byte)(8 * y + 2), 7, 255);
        }

        [Test]
        [TestCaseSource(nameof(RotationCases))]
        public void RotateTexture90ProducesExpectedPixelGrid(
            int width,
            int height,
            bool clockwise,
            Color32[] expected
        )
        {
            Texture2D source = CreateTexture(width, height);
            Texture2D rotated = Track(source.RotateTexture90(clockwise));
            Assert.IsTrue(rotated != null);
            Assert.AreEqual(height, rotated.width);
            Assert.AreEqual(width, rotated.height);
            Assert.AreEqual(source.format, rotated.format);
            CollectionAssert.AreEqual(expected, rotated.GetPixels32());
            CollectionAssert.AreEqual(BuildSourcePixels(width, height), source.GetPixels32());
        }

        [Test]
        public void RotateTexture180ProducesExpectedPixelGrid()
        {
            const int width = 2;
            const int height = 3;
            Texture2D source = CreateTexture(width, height);
            Texture2D rotated = Track(source.RotateTexture180());
            Assert.AreEqual(width, rotated.width);
            Assert.AreEqual(height, rotated.height);
            Assert.AreEqual(source.format, rotated.format);
            Color32[] original = source.GetPixels32();
            Color32[] flipped = rotated.GetPixels32();
            Assert.AreEqual(original.Length, flipped.Length);
            for (int index = 0; index < original.Length; ++index)
            {
                Assert.AreEqual(
                    original[original.Length - 1 - index],
                    flipped[index],
                    $"Pixel {index} did not match the reversed source."
                );
            }
            CollectionAssert.AreEqual(BuildSourcePixels(width, height), source.GetPixels32());
        }

        [Test]
        public void RotateTexture180TwiceReturnsOriginalPixels()
        {
            Texture2D source = CreateTexture(3, 5);
            Texture2D once = Track(source.RotateTexture180());
            Texture2D twice = Track(once.RotateTexture180());
            CollectionAssert.AreEqual(source.GetPixels32(), twice.GetPixels32());
        }

        [Test]
        public void RotatingQuarterTurnsComposesToHalfTurn()
        {
            Texture2D source = CreateTexture(2, 4);
            Texture2D clockwiseTwice = Track(
                Track(source.RotateTexture90(true)).RotateTexture90(true)
            );
            Texture2D halfTurn = Track(source.RotateTexture180());
            CollectionAssert.AreEqual(halfTurn.GetPixels32(), clockwiseTwice.GetPixels32());
        }

        [Test]
        public void RotateTexture90ReturnsNullForNullTexture()
        {
            Assert.IsTrue(((Texture2D)null).RotateTexture90(true) == null);
        }

        [Test]
        public void RotateTexture180ReturnsNullForNullTexture()
        {
            Assert.IsTrue(((Texture2D)null).RotateTexture180() == null);
        }

        [Test]
        public void ExtractSpriteRectReturnsSpritePixels()
        {
            Texture2D source = CreateTexture(4, 4);
            Rect region = new Rect(1, 2, 2, 2);
            Sprite sprite = Track(Sprite.Create(source, region, new Vector2(0.5f, 0.5f)));
            Texture2D extracted = Track(sprite.ExtractSpriteRect());
            Assert.IsTrue(extracted != null);
            Assert.AreEqual(2, extracted.width);
            Assert.AreEqual(2, extracted.height);
            Assert.AreEqual(source.format, extracted.format);
            Color32[] allPixels = source.GetPixels32();
            Color32[] expectedRegion = new Color32[4];
            int index = 0;
            for (int y = 2; y < 4; ++y)
            {
                for (int x = 1; x < 3; ++x)
                {
                    expectedRegion[index] = allPixels[y * source.width + x];
                    ++index;
                }
            }
            CollectionAssert.AreEqual(expectedRegion, extracted.GetPixels32());
        }

        [Test]
        public void ExtractSpriteRectReturnsNullForNullSprite()
        {
            Assert.IsTrue(((Sprite)null).ExtractSpriteRect() == null);
        }

        [Test]
        public void ExtractSpriteRectRejectsNegativeOrigin()
        {
            Texture2D source = CreateTexture(4, 4);
            Sprite sprite = Track(
                Sprite.Create(source, new Rect(-1, 0, 2, 2), new Vector2(0.5f, 0.5f))
            );
            LogAssert.Expect(LogType.Error, InsideTextureErrorPattern);
            Assert.IsTrue(sprite.ExtractSpriteRect() == null);
        }

        [Test]
        public void ExtractSpriteRectRejectsRectBeyondTexture()
        {
            Texture2D source = CreateTexture(4, 4);
            Sprite sprite = Track(
                Sprite.Create(source, new Rect(-4, -4, 8, 8), new Vector2(0.5f, 0.5f))
            );
            LogAssert.Expect(LogType.Error, InsideTextureErrorPattern);
            Assert.IsTrue(sprite.ExtractSpriteRect() == null);
        }

        [Test]
        public void ExtractSpriteRectExpandsFractionalRectToWholePixels()
        {
            const int size = 5;
            Texture2D source = CreateTexture(size, size);
            Sprite sprite = Track(
                Sprite.Create(source, new Rect(1.5f, 1.5f, 3.5f, 3.5f), new Vector2(0.5f, 0.5f))
            );
            Texture2D extracted = Track(sprite.ExtractSpriteRect());
            Assert.IsTrue(extracted != null);
            Assert.AreEqual(4, extracted.width);
            Assert.AreEqual(4, extracted.height);
            Color32[] allPixels = source.GetPixels32();
            Color32[] expectedRegion = new Color32[16];
            int index = 0;
            for (int y = 1; y < size; ++y)
            {
                for (int x = 1; x < size; ++x)
                {
                    expectedRegion[index] = allPixels[y * size + x];
                    ++index;
                }
            }
            CollectionAssert.AreEqual(expectedRegion, extracted.GetPixels32());
        }

        [Test]
        public void ExtractSpriteRectAcceptsRectTouchingFarEdge()
        {
            Texture2D source = CreateTexture(4, 4);
            Sprite sprite = Track(
                Sprite.Create(source, new Rect(2, 2, 2, 2), new Vector2(0.5f, 0.5f))
            );
            Texture2D extracted = Track(sprite.ExtractSpriteRect());
            Assert.IsTrue(extracted != null);
            Color32[] allPixels = source.GetPixels32();
            Color32[] expectedRegion = new Color32[4];
            int index = 0;
            for (int y = 2; y < 4; ++y)
            {
                for (int x = 2; x < 4; ++x)
                {
                    expectedRegion[index] = allPixels[y * source.width + x];
                    ++index;
                }
            }
            CollectionAssert.AreEqual(expectedRegion, extracted.GetPixels32());
        }

        private Texture2D CreateTexture(int width, int height)
        {
            Color32[] pixels = BuildSourcePixels(width, height);
            Texture2D texture = Track(new Texture2D(width, height, TextureFormat.RGBA32, false));
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
