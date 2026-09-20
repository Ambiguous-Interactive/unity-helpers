// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools
{
#if UNITY_EDITOR
    using System;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Tools;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ImageBlurToolTests : CommonTestBase
    {
        private const float Tolerance = 1e-4f;

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(Tolerance));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(Tolerance));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(Tolerance));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(Tolerance));
        }

        private static int CountTemporaryTextures()
        {
            int count = 0;
            Texture2D[] textures = Resources.FindObjectsOfTypeAll<Texture2D>();
            foreach (Texture2D texture in textures)
            {
                if (
                    texture != null
                    && string.Equals(
                        texture.name,
                        ImageBlurTool.TemporaryTextureName,
                        System.StringComparison.Ordinal
                    )
                )
                {
                    count++;
                }
            }
            return count;
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void KernelHasExpectedLengthAndNormalizes(int radius)
        {
            float[] kernel = ImageBlurTool.KernelForTests(radius);
            Assert.NotNull(kernel);
            Assert.AreEqual(radius * 2 + 1, kernel.Length);
            float sum = 0f;
            foreach (float kernelElement in kernel)
            {
                sum += kernelElement;
            }
            Assert.That(sum, Is.InRange(0.999f, 1.001f));
        }

        [TestCase(8, 8, 8, false)]
        [TestCase(16, 16, 16, true)]
        [TestCase(1, 4_096, 1, false)]
        [TestCase(1, 4_096, 4_096, true)]
        [TestCase(int.MaxValue, int.MaxValue, int.MaxValue, true)]
        public void BlurParallelismRequiresEnoughPixelsAndPartitions(
            int width,
            int height,
            int partitionCount,
            bool expected
        )
        {
            Assert.That(
                ImageBlurTool.ShouldBlurInParallel(width, height, partitionCount),
                Is.EqualTo(expected)
            );
        }

        [Test]
        public void DirectApiBlursWithoutWindowAndReturnsOwnedTexture()
        {
            Texture2D source = Track(new Texture2D(8, 8, TextureFormat.RGBA32, false));
            source.SetPixel(4, 4, Color.white);
            source.Apply();

            bool success = ImageBlurAPI.TryBlur(source, 2, out Texture2D blurred, out string error);

            Assert.IsTrue(success, error);
            Assert.IsTrue(error == null);
            Assert.IsTrue(blurred != null);
            Track(blurred);
            Assert.AreEqual(source.width, blurred.width);
            Assert.AreEqual(source.height, blurred.height);
            Assert.That(blurred.GetPixel(4, 4).r, Is.LessThan(1f));
        }

        [TestCase(0)]
        [TestCase(201)]
        public void DirectApiRejectsInvalidRadius(int radius)
        {
            Texture2D source = Track(new Texture2D(2, 2));

            Assert.IsFalse(
                ImageBlurAPI.TryBlur(source, radius, out Texture2D blurred, out string error)
            );
            Assert.IsTrue(blurred == null);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void DirectApiRejectsMissingOrUnreadableSource()
        {
            Assert.IsFalse(
                ImageBlurAPI.TryBlur(null, 1, out Texture2D missing, out string missingError)
            );
            Assert.IsTrue(missing == null);
            Assert.IsNotEmpty(missingError);

            Texture2D source = Track(new Texture2D(2, 2));
            source.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            Assert.IsFalse(
                ImageBlurAPI.TryBlur(source, 1, out Texture2D blurred, out string error)
            );
            Assert.IsTrue(blurred == null);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void BlurredTextureMatchesInputDimensions()
        {
            Texture2D tex = Track(new Texture2D(8, 8, TextureFormat.RGBA32, false));
            for (int y = 0; y < tex.height; y++)
            {
                for (int x = 0; x < tex.width; x++)
                {
                    tex.SetPixel(x, y, Color.white);
                }
            }
            tex.Apply();

            Texture2D blurred = Track(ImageBlurTool.BlurredForTests(tex, 2));
            Assert.IsTrue(blurred != null);
            Assert.AreEqual(tex.width, blurred.width);
            Assert.AreEqual(tex.height, blurred.height);
        }

        [Test]
        public void FailedBlurDoesNotLeakTemporaryTexture()
        {
            Texture2D source = Track(new Texture2D(8, 8, TextureFormat.RGBA32, false));
            source.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            Assert.IsFalse(source.isReadable);
            int temporaryTextureCount = CountTemporaryTextures();

            Assert.Throws<ArgumentException>(() => ImageBlurTool.BlurredForTests(source, 2));

            Assert.That(CountTemporaryTextures(), Is.EqualTo(temporaryTextureCount));
        }

        /// <summary>
        /// Pins alpha-weighted blurring: weighting straight color gave the transparent green half
        /// of the kernel's mass, so an image containing only red blurred to a yellow edge.
        /// </summary>
        [Test]
        public void BlurDoesNotBleedColorFromInvisibleNeighbors()
        {
            Color[] pixels = new Color[8];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = i < 4 ? new Color(1f, 0f, 0f, 1f) : new Color(0f, 1f, 0f, 0f);
            }

            Color[] blurred = Blur(8, 1, pixels, 3);

            for (int i = 0; i < blurred.Length; i++)
            {
                if (blurred[i].a <= Tolerance)
                {
                    continue;
                }

                Assert.That(blurred[i].g, Is.EqualTo(0f).Within(Tolerance), $"pixel {i} is tinted");
            }
        }

        [Test]
        public void BlurPreservesAFullyTransparentSourceColor()
        {
            Color transparentWhite = new(1f, 1f, 1f, 0f);
            Color[] pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = transparentWhite;
            }

            Color[] blurred = Blur(4, 4, pixels, 2);

            foreach (UnityEngine.Color blurredElement in blurred)
            {
                AssertColor(blurredElement, transparentWhite);
            }
        }

        [Test]
        public void SequentialAndParallelBlurProduceEquivalentPixels()
        {
            Color[] pixels = new Color[256];
            for (int i = 0; i < pixels.Length; i++)
            {
                float alpha = i % 5 == 0 ? 0f : (i % 11) / 10f;
                pixels[i] = new Color((i % 3) / 2f, (i % 7) / 6f, (i % 13) / 12f, alpha);
            }

            Color[] sequential = Blur(16, 16, pixels, 3, false);
            Color[] parallel = Blur(16, 16, pixels, 3, true);

            Assert.That(parallel.Length, Is.EqualTo(sequential.Length));
            for (int i = 0; i < parallel.Length; i++)
            {
                Assert.That(parallel[i], Is.EqualTo(sequential[i]), $"pixel {i}");
            }
        }

        [TestCase(1)]
        [TestCase(3)]
        public void BlurOfUniformImageIsUniform(int radius)
        {
            Color uniform = new(0.2f, 0.4f, 0.8f, 0.5f);
            Color[] pixels = new Color[36];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = uniform;
            }

            Color[] blurred = Blur(6, 6, pixels, radius);

            foreach (UnityEngine.Color blurredElement in blurred)
            {
                AssertColor(blurredElement, uniform);
            }
        }

        private Color[] Blur(int width, int height, Color[] pixels, int radius)
        {
            Texture2D source = Track(new Texture2D(width, height, TextureFormat.RGBAFloat, false));
            source.SetPixels(pixels);
            source.Apply();
            Texture2D blurred = Track(ImageBlurTool.BlurredForTests(source, radius));
            return blurred.GetPixels();
        }

        private Color[] Blur(int width, int height, Color[] pixels, int radius, bool runInParallel)
        {
            Texture2D source = Track(new Texture2D(width, height, TextureFormat.RGBAFloat, false));
            source.SetPixels(pixels);
            source.Apply();
            Texture2D blurred = Track(ImageBlurTool.BlurredForTests(source, radius, runInParallel));
            return blurred.GetPixels();
        }
    }
#endif
}
