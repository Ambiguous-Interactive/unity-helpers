// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Fast")]
    public sealed class SpriteArtBoundsTests : CommonTestBase
    {
        private static IEnumerable<TestCaseData> InvalidMasks()
        {
            yield return new TestCaseData(default(SpriteAlphaMask)).SetName(
                "ArtMask.Default.Refused"
            );
            foreach (int width in new[] { -1, 0, 2, int.MaxValue })
            {
                yield return new TestCaseData(
                    new SpriteAlphaMask(new[] { true }, width, 1, Vector2.zero, Vector2.one)
                ).SetName($"ArtMask.Width.{width}.Refused");
            }
            foreach (int height in new[] { -1, 0, 2, int.MaxValue })
            {
                yield return new TestCaseData(
                    new SpriteAlphaMask(new[] { true }, 1, height, Vector2.zero, Vector2.one)
                ).SetName($"ArtMask.Height.{height}.Refused");
            }
            foreach (
                float value in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity }
            )
            {
                yield return new TestCaseData(
                    new SpriteAlphaMask(new[] { true }, 1, 1, new Vector2(value, 0), Vector2.one)
                ).SetName($"ArtMask.PivotX.{value}.Refused");
                yield return new TestCaseData(
                    new SpriteAlphaMask(new[] { true }, 1, 1, new Vector2(0, value), Vector2.one)
                ).SetName($"ArtMask.PivotY.{value}.Refused");
            }
            foreach (
                float value in new[]
                {
                    -1f,
                    0f,
                    float.NaN,
                    float.NegativeInfinity,
                    float.PositiveInfinity,
                }
            )
            {
                yield return new TestCaseData(
                    new SpriteAlphaMask(new[] { true }, 1, 1, Vector2.zero, new Vector2(value, 1))
                ).SetName($"ArtMask.UnitsX.{value}.Refused");
                yield return new TestCaseData(
                    new SpriteAlphaMask(new[] { true }, 1, 1, Vector2.zero, new Vector2(1, value))
                ).SetName($"ArtMask.UnitsY.{value}.Refused");
            }
            yield return new TestCaseData(
                new SpriteAlphaMask(
                    new[] { true },
                    1,
                    1,
                    Vector2.zero,
                    new Vector2(float.Epsilon, 1)
                )
            ).SetName("ArtMask.Underflow.Refused");
            yield return new TestCaseData(
                new SpriteAlphaMask(
                    new[] { true },
                    1,
                    1,
                    new Vector2(float.MaxValue, 0),
                    Vector2.one
                )
            ).SetName("ArtMask.LostPrecision.Refused");
            yield return new TestCaseData(
                new SpriteAlphaMask(
                    new[] { true },
                    1,
                    1,
                    new Vector2(float.MaxValue, 0),
                    new Vector2(float.MaxValue, 1)
                )
            ).SetName("ArtMask.Overflow.Refused");
        }

        private static IEnumerable<TestCaseData> PixelBoundaryCases()
        {
            foreach (int width in new[] { 1, 7, 32 })
            {
                foreach (int pattern in new[] { 0, 1, 2, 3, 4, 5 })
                {
                    foreach (byte threshold in new byte[] { 0, 1, 128, 255 })
                    {
                        yield return new TestCaseData(width, pattern, threshold).SetName(
                            $"PixelBounds.Width{width}.Pattern{pattern}.Threshold{threshold}"
                        );
                    }
                }
            }
        }

        [TestCaseSource(nameof(PixelBoundaryCases))]
        public void PixelBoundsMatchEveryVisiblePixel(int width, int pattern, byte threshold)
        {
            const int height = 9;
            const float pixelsPerUnit = 17f;
            Color32[] pixels = new Color32[width * height];
            bool[] visible = new bool[pixels.Length];
            int minimumX = width;
            int minimumY = height;
            int maximumX = -1;
            int maximumY = -1;
            for (int row = 0; row < height; ++row)
            {
                for (int column = 0; column < width; ++column)
                {
                    byte alpha = pattern switch
                    {
                        0 => 0,
                        1 => 255,
                        2 => column == width / 2 ? (byte)255 : (byte)0,
                        3 => (row == 0 || row == height - 1) && (column == 0 || column == width - 1)
                            ? (byte)128
                            : (byte)0,
                        4 => (column + row) % 2 == 0 ? (byte)255 : (byte)0,
                        _ => (byte)((column * 41 + row * 17) % 256),
                    };
                    int index = row * width + column;
                    pixels[index] = new Color32(0, 0, 0, alpha);
                    visible[index] = alpha != 0 && threshold <= alpha;
                    if (visible[index])
                    {
                        minimumX = Math.Min(minimumX, column);
                        minimumY = Math.Min(minimumY, row);
                        maximumX = Math.Max(maximumX, column);
                        maximumY = Math.Max(maximumY, row);
                    }
                }
            }

            Texture2D texture = Track(new Texture2D(width, height, TextureFormat.RGBA32, false));
            texture.SetPixels32(pixels);
            texture.Apply();
            Rect rectangle = new(0, 0, width, height);
            Vector2 normalizedPivot = new(0.25f, 0.75f);
            Sprite sprite = Track(
                Sprite.Create(texture, rectangle, normalizedPivot, pixelsPerUnit)
            );
            Vector2 pivot = sprite.pivot;
            Vector2 units = Vector2.one / pixelsPerUnit;
            SpriteAlphaMask mask = new(visible, width, height, pivot, units);
            bool expected = 0 <= maximumX;
            Assert.AreEqual(
                expected,
                SpriteArtBounds.TryGetLocalArtBounds(
                    mask,
                    out Bounds maskBounds,
                    out string maskError
                ),
                maskError
            );
            Assert.AreEqual(
                expected,
                SpriteArtBounds.TryGetLocalArtBounds(
                    sprite,
                    out Bounds spriteBounds,
                    out string spriteError,
                    threshold
                ),
                spriteError
            );
            if (!expected)
            {
                Assert.AreEqual(default(Bounds), maskBounds);
                Assert.AreEqual(default(Bounds), spriteBounds);
                return;
            }

            Vector3 expectedMinimum = new(
                (float)((minimumX - (double)pivot.x) * units.x),
                (float)((minimumY - (double)pivot.y) * units.y),
                0
            );
            Vector3 expectedMaximum = new(
                (float)((maximumX + 1 - (double)pivot.x) * units.x),
                (float)((maximumY + 1 - (double)pivot.y) * units.y),
                0
            );
            Assert.That(Vector3.Distance(expectedMinimum, maskBounds.min), Is.LessThan(0.00001f));
            Assert.That(Vector3.Distance(expectedMaximum, maskBounds.max), Is.LessThan(0.00001f));
            Assert.AreEqual(maskBounds, spriteBounds);
        }

        [TestCaseSource(nameof(InvalidMasks))]
        public void InvalidMasksFailWithEmptyBoundsAndAReason(SpriteAlphaMask mask)
        {
            Assert.IsFalse(
                SpriteArtBounds.TryGetLocalArtBounds(mask, out Bounds bounds, out string error)
            );
            Assert.AreEqual(default(Bounds), bounds);
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
        }

        [Test]
        public void MaskBoundsExcludeTransparentBordersAndIncludeWholePixelCells()
        {
            bool[] pixels = new bool[20];
            pixels[6] = true;
            pixels[13] = true;
            SpriteAlphaMask mask = new(pixels, 5, 4, new Vector2(2f, 1.5f), new Vector2(0.5f, 2f));
            Assert.IsTrue(
                SpriteArtBounds.TryGetLocalArtBounds(mask, out Bounds bounds, out string error),
                error
            );
            Assert.AreEqual(new Vector3(-0.5f, -1f, 0f), bounds.min);
            Assert.AreEqual(new Vector3(1f, 3f, 0f), bounds.max);
            Assert.IsTrue(error == null);
            Assert.IsTrue(pixels[6]);
            Assert.IsTrue(pixels[13]);
        }

        [Test]
        public void FullyTransparentMaskIsAReadableEmptyResult()
        {
            SpriteAlphaMask mask = new(new bool[6], 3, 2, Vector2.zero, Vector2.one);
            Assert.IsFalse(
                SpriteArtBounds.TryGetLocalArtBounds(mask, out Bounds bounds, out string error)
            );
            Assert.AreEqual(default(Bounds), bounds);
            StringAssert.Contains("transparent", error);
        }

        [Test]
        public void DirectBoundsAvoidAllocatingAnIntermediateAlphaMask()
        {
            const int iterations = 32;
            const int capacity = 512;
            const int warmup = 8;
            Texture2D texture = Track(new Texture2D(32, 16, TextureFormat.RGBA32, false));
            texture.SetPixel(17, 5, Color.white);
            texture.Apply();
            Sprite sprite = Track(
                Sprite.Create(texture, new Rect(4, 2, 24, 12), new Vector2(0.4f, 0.75f), 13f)
            );

            Bounds reference = default;
            Bounds direct = default;
            int referenceSuccesses = 0;
            int directSuccesses = 0;
            Action withMask = () =>
            {
                if (
                    SpriteMaskReader.TryRead(sprite, out SpriteAlphaMask mask, out _)
                    && SpriteArtBounds.TryGetLocalArtBounds(mask, out reference, out _)
                )
                {
                    ++referenceSuccesses;
                }
            };
            Action withoutMask = () =>
            {
                if (SpriteArtBounds.TryGetLocalArtBounds(sprite, out direct, out _))
                {
                    ++directSuccesses;
                }
            };

            long referenceEvents = ProfilerAllocationCounter.Measure(
                withMask,
                iterations,
                capacity,
                warmup
            );
            long directEvents = ProfilerAllocationCounter.Measure(
                withoutMask,
                iterations,
                capacity,
                warmup
            );
            Assert.AreEqual(iterations + warmup, referenceSuccesses);
            Assert.AreEqual(referenceSuccesses, directSuccesses);
            Assert.AreEqual(reference, direct);
            if (referenceEvents == 0)
            {
                Assert.Ignore("The profiler did not observe the explicit mask and pixel arrays.");
            }

            TestContext.WriteLine(
                $"Allocation events with a mask: {referenceEvents}; direct: {directEvents}."
            );
            Assert.That(directEvents, Is.LessThan(referenceEvents));
        }

        [TestCase(0, 1.5f)]
        [TestCase(1, 1.5f)]
        [TestCase(128, 0.5f)]
        [TestCase(255, 0.5f)]
        public void SpriteBoundsUseOnlyTheirOwnRectanglePivotAndAlphaThreshold(
            int threshold,
            float width
        )
        {
            Texture2D texture = Track(new Texture2D(6, 4, TextureFormat.RGBA32, false));
            Color32[] pixels = new Color32[24];
            pixels[0] = new Color32(255, 255, 255, 255);
            pixels[7] = new Color32(255, 255, 255, 1);
            pixels[15] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            Sprite sprite = Track(
                Sprite.Create(texture, new Rect(1, 1, 4, 3), new Vector2(0.25f, 0.5f), 2f)
            );
            Assert.IsTrue(
                SpriteArtBounds.TryGetLocalArtBounds(
                    sprite,
                    out Bounds bounds,
                    out string error,
                    (byte)threshold
                ),
                error
            );
            Assert.That(bounds.size.x, Is.EqualTo(width).Within(0.0001f));
            Assert.That(bounds.max.x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(bounds.max.y, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.IsTrue(texture.isReadable);
        }

        [Test]
        public void MissingDestroyedAndUnreadableSpritesFailWithoutChangingTheTexture()
        {
            Assert.IsFalse(
                SpriteArtBounds.TryGetLocalArtBounds(
                    (Sprite)null,
                    out Bounds bounds,
                    out string error
                )
            );
            Assert.AreEqual(default(Bounds), bounds);
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
            Texture2D texture = Track(new Texture2D(2, 2));
            Sprite sprite = Track(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero));
            texture.Apply(false, true);
            Assert.IsFalse(SpriteArtBounds.TryGetLocalArtBounds(sprite, out bounds, out error));
            Assert.IsFalse(texture.isReadable);
            Assert.AreEqual(default(Bounds), bounds);
            DestroyTrackedObjects();
            Assert.IsFalse(SpriteArtBounds.TryGetLocalArtBounds(sprite, out bounds, out error));
            Assert.AreEqual(default(Bounds), bounds);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void RendererFlipsMirrorAroundTheOriginAndPreserveExtents(bool flipX, bool flipY)
        {
            Bounds original = new(new Vector3(3, -2, 5), new Vector3(4, 6, 0));
            Bounds drawn = SpriteArtBounds.AsDrawn(original, flipX, flipY);
            Assert.AreEqual(new Vector3(flipX ? -3 : 3, flipY ? 2 : -2, 5), drawn.center);
            Assert.AreEqual(original.size, drawn.size);
            Assert.AreEqual(original, SpriteArtBounds.AsDrawn(drawn, flipX, flipY));
        }

        [TestCase(0f, 1f, 1f)]
        [TestCase(35f, 2f, 0.5f)]
        [TestCase(90f, -2f, 3f)]
        [TestCase(215f, -1f, -1f)]
        public void LowestCornerIncludesRotationScaleAndTranslation(
            float angle,
            float scaleX,
            float scaleY
        )
        {
            Bounds bounds = new(new Vector3(1, 2, 3), new Vector3(4, 6, 0));
            Matrix4x4 matrix = Matrix4x4.TRS(
                new Vector3(7, -5, 9),
                Quaternion.Euler(0, 0, angle),
                new Vector3(scaleX, scaleY, 1)
            );
            float expected = Mathf.Min(
                Mathf.Min(
                    matrix.MultiplyPoint3x4(new Vector3(-1, -1, 3)).y,
                    matrix.MultiplyPoint3x4(new Vector3(3, -1, 3)).y
                ),
                Mathf.Min(
                    matrix.MultiplyPoint3x4(new Vector3(-1, 5, 3)).y,
                    matrix.MultiplyPoint3x4(new Vector3(3, 5, 3)).y
                )
            );
            Assert.That(
                SpriteArtBounds.LowestCornerY(matrix, bounds),
                Is.EqualTo(expected).Within(0.0001f)
            );
        }
    }
}
