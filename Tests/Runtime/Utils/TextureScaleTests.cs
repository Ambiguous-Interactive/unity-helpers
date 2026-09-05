// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Threading;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class TextureScaleTests : CommonTestBase
    {
        private const float Tolerance = 5e-5f;

        private TextureTestHelper _textureHelper;

        [SetUp]
        public void SetUp()
        {
            _textureHelper = new TextureTestHelper();
        }

        [TearDown]
        public override void TearDown()
        {
            TextureScale.SliceStartedForTesting = null;
            if (_textureHelper != null)
            {
                _textureHelper.Dispose();
            }

            base.TearDown();
        }

        [TestCase(true, 8, true)]
        [TestCase(true, 1, false)]
        [TestCase(false, 8, true)]
        [TestCase(false, 1, false)]
        public void ScaleReportsASliceFailureWhicheverBranchRanIt(
            bool useBilinear,
            int newHeight,
            bool parallel
        )
        {
            Texture2D texture = _textureHelper.CreateTextureWithFactory(
                8,
                8,
                (x, y) => new Color(x / 8f, y / 8f, 0f, 1f)
            );
            InvalidOperationException injected = new("the slice could not run");

            /*
                Use measured slice counts; single-core execution cannot prove the parallel failure path. Row
                zero belongs to a worker when multiple slices run.
            */
            int sliceStarts = 0;
            TextureScale.SliceStartedForTesting = start =>
            {
                Interlocked.Increment(ref sliceStarts);
                if (start == 0)
                {
                    throw injected;
                }
            };

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
                InvokeScale(texture, 4, newHeight, useBilinear)
            );

            int observedSlices = Volatile.Read(ref sliceStarts);
            if (parallel && observedSlices <= 1)
            {
                Assert.Ignore(
                    "This runner ran one slice, so there is no background slice to fail and a "
                        + "pass here would be the absence of a measurement."
                );
            }

            Assert.AreEqual(
                parallel,
                1 < observedSlices,
                $"Expected the {(parallel ? "parallel" : "single-threaded")} branch, and "
                    + $"{observedSlices} slice(s) ran."
            );
            Assert.AreSame(
                injected,
                thrown,
                "a slice that failed on a worker must reach the caller as the failure it was, not "
                    + "as a silently partial image"
            );
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ScaleThrowsWhenTextureIsNull(bool useBilinear)
        {
            Assert.Throws<ArgumentNullException>(() => InvokeScale(null, 2, 2, useBilinear));
        }

        [TestCase(true, 0)]
        [TestCase(true, -3)]
        [TestCase(false, 0)]
        [TestCase(false, -3)]
        public void ScaleThrowsWhenWidthIsNotPositive(bool useBilinear, int newWidth)
        {
            Texture2D texture = _textureHelper.CreateTextureWithFactory(
                2,
                2,
                (x, y) => new Color(x, y, 0f, 1f)
            );
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                InvokeScale(texture, newWidth, 2, useBilinear)
            );
        }

        [TestCase(true, 0)]
        [TestCase(true, -5)]
        [TestCase(false, 0)]
        [TestCase(false, -5)]
        public void ScaleThrowsWhenHeightIsNotPositive(bool useBilinear, int newHeight)
        {
            Texture2D texture = _textureHelper.CreateTextureWithFactory(
                2,
                2,
                (x, y) => new Color(x, y, 0f, 1f)
            );
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                InvokeScale(texture, 2, newHeight, useBilinear)
            );
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ScaleThrowsWhenTextureIsNotReadable(bool useBilinear)
        {
            Texture2D texture = _textureHelper.CreateTextureWithFactory(
                2,
                2,
                (x, y) => new Color(x, y, 0f, 1f)
            );
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            Assert.Throws<UnityException>(() => InvokeScale(texture, 1, 1, useBilinear));
        }

        [TestCase(5, 4, 3, 2)]
        [TestCase(2, 3, 5, 7)]
        [TestCase(7, 5, 3, 9)]
        public void PointScaleOnlyEmitsColorsPresentInTheSource(
            int sourceWidth,
            int sourceHeight,
            int destWidth,
            int destHeight
        )
        {
            Texture2D texture = _textureHelper.CreateTextureWithFactory(
                sourceWidth,
                sourceHeight,
                (x, y) => new Color(x / 10f, y / 10f, (x + y) / 20f, (x + 1) / 10f),
                TextureFormat.RGBAFloat
            );
            Color[] source = texture.GetPixels();

            TextureScale.Point(texture, destWidth, destHeight);

            Assert.AreEqual(destWidth, texture.width);
            Assert.AreEqual(destHeight, texture.height);
            Color[] actual = texture.GetPixels();
            Assert.AreEqual(destWidth * destHeight, actual.Length);
            for (int i = 0; i < actual.Length; ++i)
            {
                Assert.IsTrue(
                    ContainsColor(source, actual[i]),
                    $"pixel {i} was interpolated rather than sampled"
                );
            }
        }

        [TestCase(2, 3, 2)]
        [TestCase(3, 2, 4)]
        [TestCase(4, 4, 1)]
        public void PointUpscaleByAnIntegerFactorDuplicatesEverySourcePixel(
            int sourceWidth,
            int sourceHeight,
            int factor
        )
        {
            Texture2D texture = _textureHelper.CreateTextureWithFactory(
                sourceWidth,
                sourceHeight,
                (x, y) => new Color(x * 0.25f, y * 0.2f, (x + y) * 0.1f, 1f - (0.1f * x)),
                TextureFormat.RGBAFloat
            );
            Color[] source = texture.GetPixels();
            int destWidth = sourceWidth * factor;

            TextureScale.Point(texture, destWidth, sourceHeight * factor);

            Color[] actual = texture.GetPixels();
            for (int y = 0; y < sourceHeight * factor; ++y)
            {
                for (int x = 0; x < destWidth; ++x)
                {
                    Color expected = source[((y / factor) * sourceWidth) + (x / factor)];
                    AssertColor(actual[(y * destWidth) + x], expected, Tolerance);
                }
            }
        }

        [Test]
        public void PointDownscaleOfSymmetricSourceIsSymmetric()
        {
            // Origin-biased mapping previously broke symmetry when downscaling symmetric input.
            Texture2D texture = _textureHelper.CreateTextureWithFactory(
                9,
                1,
                (x, _) => new Color(Mathf.Min(x, 8 - x) / 4f, 0f, 0f, 1f),
                TextureFormat.RGBAFloat
            );

            TextureScale.Point(texture, 3, 1);

            Color[] actual = texture.GetPixels();
            for (int i = 0; i < actual.Length; ++i)
            {
                AssertColor(actual[i], actual[actual.Length - 1 - i], Tolerance);
            }
        }

        [Test]
        public void PointScalingKeepsTextureReadable()
        {
            Texture2D texture = _textureHelper.CreateTextureWithFactory(
                3,
                3,
                (x, y) => new Color(x, y, 0f, 1f)
            );
            TextureScale.Point(texture, 3, 3);
            Assert.IsTrue(texture.isReadable);
        }

        // Assert resampler invariants instead of duplicating its algorithm and any shared sampling defect.

        [TestCase(4, 3, 2, 2)]
        [TestCase(3, 2, 6, 4)]
        [TestCase(2, 2, 5, 5)]
        [TestCase(7, 5, 3, 9)]
        [TestCase(1, 1, 4, 4)]
        public void BilinearOutputStaysInsideTheSourceRange(
            int sourceWidth,
            int sourceHeight,
            int destWidth,
            int destHeight
        )
        {
            Texture2D texture = CreateRamp(sourceWidth, sourceHeight);
            Color[] source = texture.GetPixels();
            float min = MinChannel(source);
            float max = MaxChannel(source);

            TextureScale.Bilinear(texture, destWidth, destHeight);

            Color[] actual = texture.GetPixels();
            Assert.That(MinChannel(actual), Is.GreaterThanOrEqualTo(min - Tolerance));
            Assert.That(MaxChannel(actual), Is.LessThanOrEqualTo(max + Tolerance));
        }

        [TestCase(4, 1, 8, 1)]
        [TestCase(3, 2, 6, 4)]
        [TestCase(2, 2, 5, 5)]
        [TestCase(5, 3, 5, 3)]
        public void BilinearUpscaleReachesBothSourceExtremes(
            int sourceWidth,
            int sourceHeight,
            int destWidth,
            int destHeight
        )
        {
            Texture2D texture = CreateRamp(sourceWidth, sourceHeight);
            Color[] source = texture.GetPixels();
            float min = MinChannel(source);
            float max = MaxChannel(source);

            TextureScale.Bilinear(texture, destWidth, destHeight);

            // Corner sampling previously left the far source edge unreachable during upscaling.
            Color[] actual = texture.GetPixels();
            Assert.That(MinChannel(actual), Is.EqualTo(min).Within(Tolerance));
            Assert.That(MaxChannel(actual), Is.EqualTo(max).Within(Tolerance));
        }

        [TestCase(4, 3, 2, 2)]
        [TestCase(3, 2, 6, 4)]
        [TestCase(7, 5, 3, 9)]
        [TestCase(1, 4, 6, 1)]
        public void BilinearScaleOfUniformImageIsUniform(
            int sourceWidth,
            int sourceHeight,
            int destWidth,
            int destHeight
        )
        {
            Color uniform = new(0.3f, 0.6f, 0.9f, 0.5f);
            Texture2D texture = _textureHelper.CreateTextureWithFactory(
                sourceWidth,
                sourceHeight,
                (_, _) => uniform,
                TextureFormat.RGBAFloat
            );

            TextureScale.Bilinear(texture, destWidth, destHeight);

            Color[] actual = texture.GetPixels();
            Assert.AreEqual(destWidth * destHeight, actual.Length);
            foreach (UnityEngine.Color actualElement in actual)
            {
                AssertColor(actualElement, uniform, Tolerance);
            }
        }

        [Test]
        public void BilinearDownscaleOfSymmetricSourceIsSymmetric()
        {
            // Checkerboard input distinguishes center sampling from a corner-biased first output pixel.
            Texture2D texture = _textureHelper.CreateTextureWithFactory(
                8,
                1,
                (x, _) => x % 2 == 0 ? Color.white : Color.black,
                TextureFormat.RGBAFloat
            );

            TextureScale.Bilinear(texture, 4, 1);

            Color[] actual = texture.GetPixels();
            for (int i = 0; i < actual.Length; ++i)
            {
                AssertColor(actual[i], actual[actual.Length - 1 - i], Tolerance);
            }
        }

        [Test]
        public void BilinearDoesNotBleedColorFromInvisibleNeighbors()
        {
            // Straight-alpha interpolation previously let transparent green tint visible red.
            Texture2D texture = _textureHelper.CreateTextureWithFactory(
                2,
                1,
                (x, _) => x == 0 ? new Color(1f, 0f, 0f, 1f) : new Color(0f, 1f, 0f, 0f),
                TextureFormat.RGBAFloat
            );

            TextureScale.Bilinear(texture, 4, 1);

            // Preserve invisible RGB as the fallback rather than turning fully transparent images black.
            Color[] actual = texture.GetPixels();
            int visible = 0;
            for (int i = 0; i < actual.Length; ++i)
            {
                if (actual[i].a <= Tolerance)
                {
                    continue;
                }

                ++visible;
                Assert.That(actual[i].g, Is.EqualTo(0f).Within(Tolerance), $"pixel {i} is tinted");
                Assert.That(actual[i].r, Is.EqualTo(1f).Within(Tolerance), $"pixel {i} lost red");
            }

            Assert.Greater(visible, 0, "the fixture asserted nothing");
        }

        [Test]
        public void BilinearPreservesAFullyTransparentSourceColor()
        {
            // Zero alpha cannot be unpremultiplied; retain RGB that may carry non-color data.
            Color transparentWhite = new(1f, 1f, 1f, 0f);
            Texture2D texture = _textureHelper.CreateTextureWithFactory(
                3,
                3,
                (_, _) => transparentWhite,
                TextureFormat.RGBAFloat
            );

            TextureScale.Bilinear(texture, 7, 5);

            Color[] actual = texture.GetPixels();
            foreach (UnityEngine.Color actualElement in actual)
            {
                AssertColor(actualElement, transparentWhite, Tolerance);
            }
        }

        [Test]
        public void BilinearScaleOfOpaqueSourceStaysOpaque()
        {
            Texture2D texture = CreateRamp(5, 4);

            TextureScale.Bilinear(texture, 9, 7);

            Color[] actual = texture.GetPixels();
            foreach (UnityEngine.Color actualElement in actual)
            {
                Assert.That(actualElement.a, Is.EqualTo(1f).Within(Tolerance));
            }
        }

        private static void InvokeScale(Texture2D texture, int width, int height, bool useBilinear)
        {
            if (useBilinear)
            {
                TextureScale.Bilinear(texture, width, height);
            }
            else
            {
                TextureScale.Point(texture, width, height);
            }
        }

        private static bool ContainsColor(Color[] source, Color color)
        {
            foreach (UnityEngine.Color sourceElement in source)
            {
                if (
                    Mathf.Abs(sourceElement.r - color.r) <= Tolerance
                    && Mathf.Abs(sourceElement.g - color.g) <= Tolerance
                    && Mathf.Abs(sourceElement.b - color.b) <= Tolerance
                    && Mathf.Abs(sourceElement.a - color.a) <= Tolerance
                )
                {
                    return true;
                }
            }

            return false;
        }

        private Texture2D CreateRamp(int width, int height)
        {
            int last = Mathf.Max(width * height - 1, 1);
            return _textureHelper.CreateTextureWithFactory(
                width,
                height,
                (x, y) => new Color(((y * width) + x) / (float)last, 0f, 0f, 1f),
                TextureFormat.RGBAFloat
            );
        }

        private static float MinChannel(Color[] pixels)
        {
            float min = float.PositiveInfinity;
            foreach (UnityEngine.Color pixelsElement in pixels)
            {
                min = Mathf.Min(min, pixelsElement.r);
            }

            return min;
        }

        private static float MaxChannel(Color[] pixels)
        {
            float max = float.NegativeInfinity;
            foreach (UnityEngine.Color pixelsElement in pixels)
            {
                max = Mathf.Max(max, pixelsElement.r);
            }

            return max;
        }

        private static void AssertColor(Color actual, Color expected, float tolerance = 1e-5f)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(tolerance));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(tolerance));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(tolerance));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(tolerance));
        }
    }
}
