// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
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
