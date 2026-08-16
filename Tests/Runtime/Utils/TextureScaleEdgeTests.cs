// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class TextureScaleEdgeTests : CommonTestBase
    {
        [Test]
        public void PointScaleToOneByOneSamplesTheCenterPixel()
        {
            Texture2D texture = Track(new Texture2D(3, 3, TextureFormat.RGBA32, false, false));
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[9]
            {
                new(1, 0, 0, 1),
                new(0, 1, 0, 1),
                new(0, 0, 1, 1),
                new(1, 1, 0, 1),
                new(0, 1, 1, 1),
                new(1, 0, 1, 1),
                new(0.2f, 0.3f, 0.4f, 1),
                new(0.5f, 0.6f, 0.7f, 1),
                new(0.9f, 1f, 0.1f, 1),
            };
            texture.SetPixels(pixels);
            texture.Apply(false, false);

            TextureScale.Point(texture, 1, 1);

            Assert.AreEqual(1, texture.width);
            Assert.AreEqual(1, texture.height);

            // The one destination pixel covers the whole source, so its center is the source's center.
            // Sampling the corner instead is the same half-texel bias that shifted bilinear output.
            Color expected = pixels[4];
            Color c = texture.GetPixels()[0];
            Assert.That(c.r, Is.EqualTo(expected.r).Within(1e-5f));
            Assert.That(c.g, Is.EqualTo(expected.g).Within(1e-5f));
            Assert.That(c.b, Is.EqualTo(expected.b).Within(1e-5f));
            Assert.That(c.a, Is.EqualTo(expected.a).Within(1e-5f));
        }

        [Test]
        public void BilinearScaleToOneRowInterpolatesHorizontally()
        {
            Texture2D texture = Track(new Texture2D(4, 2, TextureFormat.RGBA32, false, false));
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[8]
            {
                new(0, 0, 0, 1),
                new(1, 0, 0, 1),
                new(0, 1, 0, 1),
                new(0, 0, 1, 1),
                new(1, 1, 1, 1),
                new(0.5f, 0.5f, 0.5f, 1),
                new(0.25f, 0.75f, 0.25f, 1),
                new(0.75f, 0.25f, 0.75f, 1),
            };
            texture.SetPixels(pixels);
            texture.Apply(false, false);

            TextureScale.Bilinear(texture, 2, 1);

            Assert.AreEqual(2, texture.width);
            Assert.AreEqual(1, texture.height);
            Color[] result = texture.GetPixels();
            Assert.AreEqual(2, result.Length);
            Assert.That(result[0].a, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(result[1].a, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void ScalingToSameSizeLeavesPixelsEquivalent()
        {
            Texture2D texture = Track(new Texture2D(2, 2, TextureFormat.RGBA32, false, false));
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            Color[] pixels = new Color[4]
            {
                new(0.1f, 0.2f, 0.3f, 0.4f),
                new(0.5f, 0.6f, 0.7f, 0.8f),
                new(0.9f, 1f, 0.2f, 0.3f),
                new(0.4f, 0.3f, 0.2f, 0.1f),
            };
            texture.SetPixels(pixels);
            texture.Apply(false, false);

            // Snapshot values as reported by the texture after Apply()
            Color[] before = texture.GetPixels();

            TextureScale.Point(texture, 2, 2);
            Color[] afterPoint = texture.GetPixels();
            for (int i = 0; i < before.Length; i++)
            {
                Assert.That(afterPoint[i].r, Is.EqualTo(before[i].r).Within(1e-5f));
                Assert.That(afterPoint[i].g, Is.EqualTo(before[i].g).Within(1e-5f));
                Assert.That(afterPoint[i].b, Is.EqualTo(before[i].b).Within(1e-5f));
                Assert.That(afterPoint[i].a, Is.EqualTo(before[i].a).Within(1e-5f));
            }

            TextureScale.Bilinear(texture, 2, 2);
            Color[] afterBilinear = texture.GetPixels();
            for (int i = 0; i < before.Length; i++)
            {
                Assert.That(afterBilinear[i].r, Is.EqualTo(before[i].r).Within(1e-5f));
                Assert.That(afterBilinear[i].g, Is.EqualTo(before[i].g).Within(1e-5f));
                Assert.That(afterBilinear[i].b, Is.EqualTo(before[i].b).Within(1e-5f));
                Assert.That(afterBilinear[i].a, Is.EqualTo(before[i].a).Within(1e-5f));
            }
        }
    }
}
