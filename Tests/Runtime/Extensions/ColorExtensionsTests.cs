// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ColorExtensionsTests : CommonTestBase
    {
        private static readonly object[] NullSpriteSingleCases =
        {
            new object[] { ColorAveragingMethod.LAB, 0f, 0f, 0f, 1f },
            new object[] { ColorAveragingMethod.HSV, 0f, 0f, 0f, 1f },
            new object[] { ColorAveragingMethod.Dominant, 0f, 0f, 0f, 1f },
            new object[] { ColorAveragingMethod.Weighted, 0f, 0f, 0f, 0f },
        };

        private static readonly object[] NullSpriteCollectionCases =
        {
            new object[] { ColorAveragingMethod.LAB, 0f, 0f, 0f, 1f },
            new object[] { ColorAveragingMethod.HSV, 0f, 0f, 0f, 1f },
            new object[] { ColorAveragingMethod.Dominant, 0f, 0f, 0f, 1f },
            new object[] { ColorAveragingMethod.Weighted, 0f, 0f, 0f, 0f },
        };

        private static readonly object[] AllNullSpriteCases =
        {
            new object[] { ColorAveragingMethod.LAB, 0f, 0f, 0f, 1f },
            new object[] { ColorAveragingMethod.HSV, 0f, 0f, 0f, 1f },
            new object[] { ColorAveragingMethod.Dominant, 0f, 0f, 0f, 1f },
            new object[] { ColorAveragingMethod.Weighted, 0f, 0f, 0f, 0f },
        };

        private static readonly object[] EmptyPixelCases =
        {
            new object[] { ColorAveragingMethod.LAB, 0f, 0f, 0f, 1f },
            new object[] { ColorAveragingMethod.HSV, 0f, 0f, 0f, 1f },
            new object[] { ColorAveragingMethod.Dominant, 0f, 0f, 0f, 1f },
            new object[] { ColorAveragingMethod.Weighted, 0f, 0f, 0f, 0f },
        };

        [Test]
        public void ToHexFormatsCorrectly()
        {
            Color color = new(0.1f, 0.2f, 0.3f, 0.4f);
            Assert.AreEqual("#1A334C66", color.ToHex());
            Assert.AreEqual("#1A334C", color.ToHex(includeAlpha: false));
        }

        [Test]
        public void ToHexClampsOutOfRangeComponents()
        {
            Color color = new(1.5f, -0.25f, 2f, -1f);
            Assert.AreEqual("#FF00FF00", color.ToHex());
            Assert.AreEqual("#FF00FF", color.ToHex(includeAlpha: false));
        }

        /// <remarks>
        /// A project that quantizes the same color two different ways has no single answer for what
        /// color it is. Unity's own conversion is the one every other consumer of the color sees, so
        /// this asserts agreement rather than merely asserting that we round.
        /// </remarks>
        [Test]
        public void ToHexAgreesWithUnityColorUtility()
        {
            IRandom random = PRNG.Instance;
            for (int i = 0; i < 4_096; ++i)
            {
                Color color = new(
                    random.NextFloat(),
                    random.NextFloat(),
                    random.NextFloat(),
                    random.NextFloat()
                );
                Assert.AreEqual($"#{ColorUtility.ToHtmlStringRGBA(color)}", color.ToHex());
                Assert.AreEqual(
                    $"#{ColorUtility.ToHtmlStringRGB(color)}",
                    color.ToHex(includeAlpha: false)
                );
            }
        }

        [Test]
        public void ToHexRoundTripsEveryChannelValue()
        {
            for (int channel = 0; channel <= byte.MaxValue; ++channel)
            {
                Color32 original = new((byte)channel, (byte)channel, (byte)channel, (byte)channel);
                string hex = ((Color)original).ToHex();
                Assert.IsTrue(
                    ColorUtility.TryParseHtmlString(hex, out Color parsed),
                    $"{hex} was not parseable."
                );
                Color32 roundTripped = parsed;
                Assert.AreEqual(
                    original.r,
                    roundTripped.r,
                    $"Channel {channel} did not round-trip."
                );
                Assert.AreEqual(original.a, roundTripped.a, $"Alpha {channel} did not round-trip.");
            }
        }

        [Test]
        public void GetAverageColorReturnsSameColorForUniformInput()
        {
            Color input = new(0.25f, 0.5f, 0.75f, 1f);
            Color[] pixels = { input, input, input };

            foreach (ColorAveragingMethod method in Enum.GetValues(typeof(ColorAveragingMethod)))
            {
                Color result = pixels.GetAverageColor(method);
                AssertColorsApproximatelyEqual(input, result, 1e-2f, method.ToString());
            }
        }

        [Test]
        public void GetAverageColorIgnoresTransparentPixels()
        {
            List<Color> pixels = new() { new Color(1f, 0f, 0f, 1f), new Color(0f, 0f, 1f, 0f) };

            Color result = pixels.GetAverageColor(ColorAveragingMethod.Weighted, 0.1f);
            AssertColorsApproximatelyEqual(new Color(1f, 0f, 0f, 1f), result, 1e-3f);
        }

        [TestCaseSource(nameof(EmptyPixelCases))]
        public void GetAverageColorEnumerableEmptyReturnsExpectedDefaults(
            ColorAveragingMethod method,
            float expectedR,
            float expectedG,
            float expectedB,
            float expectedA
        )
        {
            Color expected = new(expectedR, expectedG, expectedB, expectedA);
            Color result = Array.Empty<Color>().GetAverageColor(method);
            AssertColorsApproximatelyEqual(expected, result, 1e-5f, method.ToString());
        }

        [TestCaseSource(nameof(EmptyPixelCases))]
        public void GetAverageColorEnumerableAllFilteredReturnsExpectedDefaults(
            ColorAveragingMethod method,
            float expectedR,
            float expectedG,
            float expectedB,
            float expectedA
        )
        {
            List<Color> pixels = new()
            {
                new Color(1f, 0f, 0f, 0.001f),
                new Color(0f, 1f, 0f, 0.001f),
            };

            Color expected = new(expectedR, expectedG, expectedB, expectedA);
            Color result = pixels.GetAverageColor(method, alphaCutoff: 0.01f);
            AssertColorsApproximatelyEqual(expected, result, 1e-5f, method.ToString());
        }

        [TestCaseSource(nameof(NullSpriteSingleCases))]
        public void GetAverageColorNullSpriteReturnsExpectedDefaults(
            ColorAveragingMethod method,
            float expectedR,
            float expectedG,
            float expectedB,
            float expectedA
        )
        {
            Sprite sprite = null;
            Color expected = new(expectedR, expectedG, expectedB, expectedA);
            Color result = sprite.GetAverageColor(method);
            AssertColorsApproximatelyEqual(expected, result, 1e-4f, method.ToString());
        }

        [Test]
        public void GetAverageColorHashSetLabReturnsInputColor()
        {
            Color expected = new(0.2f, 0.4f, 0.6f, 1f);
            HashSet<Color> pixels = new() { expected };

            Color result = pixels.GetAverageColor(ColorAveragingMethod.LAB);
            AssertColorsApproximatelyEqual(expected, result, 1e-3f);
        }

        [Test]
        public void GetAverageColorWeightedReturnsTransparentWhenAllPixelsFiltered()
        {
            List<Color> pixels = new()
            {
                new Color(1f, 0f, 0f, 0.001f),
                new Color(0f, 1f, 0f, 0.001f),
            };

            Color result = pixels.GetAverageColor(ColorAveragingMethod.Weighted, 0.01f);
            AssertColorsApproximatelyEqual(Color.clear, result, 1e-4f);
        }

        [Test]
        public void GetAverageColorSpritesDominantPicksMostFrequentBucket()
        {
            Sprite sprite = CreateSprite(
                new Color(0.95f, 0.15f, 0.15f, 1f),
                new Color(0.9f, 0.1f, 0.1f, 1f),
                new Color(0.2f, 0.8f, 0.2f, 1f),
                new Color(0.88f, 0.12f, 0.11f, 1f)
            );

            Color result = sprite.GetAverageColor(ColorAveragingMethod.Dominant);

            Assert.Greater(result.r, result.g);
            Assert.Greater(result.r, result.b);
            Assert.Greater(result.r, 0.8f);
            Assert.AreEqual(1f, result.a, 1e-6f);
        }

        [Test]
        public void GetAverageColorSpriteCollectionIgnoresNullEntries()
        {
            Sprite sprite = CreateSprite(
                new Color(0.2f, 0.3f, 0.4f, 1f),
                new Color(0.2f, 0.3f, 0.4f, 1f)
            );
            Sprite[] sprites = { null, sprite, null };

            foreach (ColorAveragingMethod method in Enum.GetValues(typeof(ColorAveragingMethod)))
            {
                Color expected = sprite.GetAverageColor(method);
                Color actual = sprites.GetAverageColor(method);
                AssertColorsApproximatelyEqual(expected, actual, 1e-4f, method.ToString());
            }
        }

        [Test]
        public void GetAverageColorSpriteWeightedHonorsAlphaCutoff()
        {
            Sprite sprite = CreateSprite(new Color(1f, 0f, 0f, 1f), new Color(0f, 1f, 0f, 0.01f));

            Color result = sprite.GetAverageColor(ColorAveragingMethod.Weighted, 0.5f);
            AssertColorsApproximatelyEqual(Color.red, result, 1e-3f);
        }

        [Test]
        public void GetAverageColorSpriteEnumerableEmptyReturnsBlack()
        {
            Sprite[] sprites = Array.Empty<Sprite>();
            Color result = sprites.GetAverageColor();
            AssertColorsApproximatelyEqual(Color.black, result, 1e-6f);
        }

        [Test]
        public void GetAverageColorSpriteEnumerableAllTransparentReturnsBlack()
        {
            Sprite transparent = CreateSprite(
                new Color(0.5f, 0.5f, 0.5f, 0f),
                new Color(0.25f, 0.25f, 0.25f, 0f)
            );

            Color result = new[] { transparent }.GetAverageColor(ColorAveragingMethod.LAB);
            AssertColorsApproximatelyEqual(Color.black, result, 1e-6f);
        }

        [Test]
        public void GetAverageColorSpriteEnumerableIgnoresNullEntries()
        {
            Sprite opaque = CreateSprite(new Color(0.1f, 0.2f, 0.3f, 1f));
            Sprite[] sprites = { null, opaque, null };

            Color result = sprites.GetAverageColor(ColorAveragingMethod.LAB);
            AssertColorsApproximatelyEqual(new Color(0.1f, 0.2f, 0.3f, 1f), result, 1e-2f);
        }

        [Test]
        public void GetAverageColorEnumerableHsvHandlesHueWrapAround()
        {
            List<Color> pixels = new()
            {
                Color.HSVToRGB(0.99f, 1f, 1f),
                Color.HSVToRGB(0.01f, 1f, 1f),
            };

            Color result = pixels.GetAverageColor(ColorAveragingMethod.HSV);
            Color.RGBToHSV(result, out float hue, out float saturation, out float value);

            float circularDistance = Mathf.Min(Mathf.Abs(hue), Mathf.Abs(1f - hue));
            Assert.Less(circularDistance, 0.05f, $"Hue {hue} should wrap near zero");
            Assert.Greater(saturation, 0.9f);
            Assert.Greater(value, 0.9f);
        }

        /// <remarks>
        /// The bucket a saturated channel lands in represents channel 256, which is not a channel,
        /// so the decode used to hand back 1.0039 for a dominant white - a "color" outside the range
        /// its own type declares.
        /// </remarks>
        [Test]
        public void GetAverageColorDominantStaysInsideTheUnitRange()
        {
            foreach (float channel in new[] { 1f, 0.99f, 0.75f, 0.5f, 0f })
            {
                Color input = new(channel, channel, channel, 1f);
                Color[] pixels = { input, input };

                Color result = pixels.GetAverageColor(ColorAveragingMethod.Dominant);

                Assert.LessOrEqual(result.r, 1f, $"{channel} overshot on red.");
                Assert.LessOrEqual(result.g, 1f, $"{channel} overshot on green.");
                Assert.LessOrEqual(result.b, 1f, $"{channel} overshot on blue.");
                Assert.GreaterOrEqual(result.r, 0f, $"{channel} undershot on red.");
            }
        }

        [Test]
        public void GetAverageColorEnumerableDominantTracksMostCommonBucket()
        {
            List<Color> pixels = new()
            {
                new Color(0.9f, 0.2f, 0.2f, 1f),
                new Color(0.88f, 0.18f, 0.21f, 1f),
                new Color(0.12f, 0.85f, 0.2f, 1f),
            };

            Color result = pixels.GetAverageColor(ColorAveragingMethod.Dominant);
            Assert.Greater(result.r, result.g);
            Assert.Greater(result.r, result.b);
        }

        [TestCaseSource(nameof(NullSpriteCollectionCases))]
        public void GetAverageColorSpriteCollectionNullReturnsExpectedDefaults(
            ColorAveragingMethod method,
            float expectedR,
            float expectedG,
            float expectedB,
            float expectedA
        )
        {
            IEnumerable<Sprite> sprites = null;
            Color expected = new(expectedR, expectedG, expectedB, expectedA);
            Color result = sprites.GetAverageColor(method);
            AssertColorsApproximatelyEqual(expected, result, 1e-6f, method.ToString());
        }

        [TestCaseSource(nameof(AllNullSpriteCases))]
        public void GetAverageColorSpriteCollectionAllNullReturnsExpectedDefaults(
            ColorAveragingMethod method,
            float expectedR,
            float expectedG,
            float expectedB,
            float expectedA
        )
        {
            IEnumerable<Sprite> sprites = new Sprite[] { null, null };
            Color expected = new(expectedR, expectedG, expectedB, expectedA);
            Color result = sprites.GetAverageColor(method);
            AssertColorsApproximatelyEqual(expected, result, 1e-6f, method.ToString());
        }

        [Test]
        public void GetComplementProducesOppositeHue()
        {
            Color complement = Color.red.GetComplement();
            Assert.Greater(complement.g, 0.5f);
            Assert.Greater(complement.b, 0.5f);
            Assert.Less(complement.r, 0.5f);
        }

        [Test]
        public void GetComplementAdjustsGrayShades()
        {
            Color complement = new Color(0.12f, 0.13f, 0.11f).GetComplement();
            float maxComponent = Mathf.Max(complement.r, Mathf.Max(complement.g, complement.b));
            float minComponent = Mathf.Min(complement.r, Mathf.Min(complement.g, complement.b));

            Assert.Greater(
                maxComponent - minComponent,
                0.2f,
                "Complement should not remain grayscale."
            );
        }

        [Test]
        public void GetComplementAdjustsLightGrayToYellowish()
        {
            Color complement = new Color(0.92f, 0.91f, 0.9f).GetComplement();

            float max = Mathf.Max(complement.r, Mathf.Max(complement.g, complement.b));
            Assert.AreEqual(
                max,
                complement.b,
                1e-3f,
                "Light gray complements should skew toward cool blue after hue rotation."
            );
            Assert.Greater(max - Mathf.Min(complement.r, complement.g), 0.05f);
        }

        [Test]
        public void GetComplementAppliesVarianceWhenRandomProvided()
        {
            StubRandom random = new();
            random.EnqueueFloat(0.25f);
            random.EnqueueFloat(0.5f);
            random.EnqueueFloat(0.75f);

            Color baseline = Color.blue.GetComplement();
            Color result = Color.blue.GetComplement(random, 0.2f);

            Assert.AreNotEqual(baseline, result);
            Assert.AreEqual(0, random.RemainingSamples);
        }

        [Test]
        public void GetComplementZeroVarianceHandlesZeroComponents()
        {
            StubRandom random = new();
            random.EnqueueFloat(0.5f);
            random.EnqueueFloat(0.5f);
            random.EnqueueFloat(0.5f);

            Color result = Color.green.GetComplement(random, variance: 0f);

            Assert.IsFalse(float.IsNaN(result.r));
            Assert.IsFalse(float.IsNaN(result.g));
            Assert.IsFalse(float.IsNaN(result.b));
            Assert.IsFalse(float.IsInfinity(result.r));
            Assert.IsFalse(float.IsInfinity(result.g));
            Assert.IsFalse(float.IsInfinity(result.b));
        }

        [Test]
        public void GetComplementRandomizedFuzzAlwaysFinite()
        {
            SystemRandom rng = new(123);
            for (int i = 0; i < 256; ++i)
            {
                Color input = new(rng.NextFloat(), rng.NextFloat(), rng.NextFloat(), 1f);
                float variance = i % 2 == 0 ? 0f : rng.NextFloat(0.25f);

                SystemRandom complementRandom = new(1000 + i);
                Color result = input.GetComplement(complementRandom, variance);

                Assert.IsFalse(float.IsNaN(result.r));
                Assert.IsFalse(float.IsNaN(result.g));
                Assert.IsFalse(float.IsNaN(result.b));
                Assert.IsFalse(float.IsInfinity(result.r));
                Assert.IsFalse(float.IsInfinity(result.g));
                Assert.IsFalse(float.IsInfinity(result.b));
                Assert.That(result.r, Is.InRange(0f, 1f));
                Assert.That(result.g, Is.InRange(0f, 1f));
                Assert.That(result.b, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void GetAverageColorLargeSpriteMatchesExpectedMean()
        {
            const int size = 64;
            Color[] pixels = new Color[size * size];
            float totalWeight = 0f;
            float weightedR = 0f;
            float weightedG = 0f;
            float weightedB = 0f;

            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    float r = x / (size - 1f);
                    float g = y / (size - 1f);
                    const float b = 0.25f;
                    Color pixel = new(r, g, b, 1f);
                    int index = y * size + x;
                    pixels[index] = pixel;

                    float weight = (r * 0.299f) + (g * 0.587f) + (b * 0.114f);
                    totalWeight += weight;
                    weightedR += r * weight;
                    weightedG += g * weight;
                    weightedB += b * weight;
                }
            }

            Color expected = new(
                weightedR / totalWeight,
                weightedG / totalWeight,
                weightedB / totalWeight,
                1f
            );

            Sprite sprite = CreateSprite(size, size, pixels);
            Color result = new[] { sprite }.GetAverageColor(ColorAveragingMethod.Weighted);

            AssertColorsApproximatelyEqual(expected, result, 5e-3f);
        }

        private Sprite CreateSprite(params Color[] pixels)
        {
            int width = Mathf.Max(1, pixels.Length);
            return CreateSprite(width, 1, pixels);
        }

        private Sprite CreateSprite(int width, int height, Color[] pixels)
        {
            Texture2D texture = Track(new Texture2D(width, height, TextureFormat.RGBA32, false));
            texture.SetPixels(pixels);
            texture.Apply();
            return Track(
                Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f))
            );
        }

        private static void AssertColorsApproximatelyEqual(
            Color expected,
            Color actual,
            float tolerance,
            string context = null
        )
        {
            Assert.AreEqual(expected.r, actual.r, tolerance, $"{context} R");
            Assert.AreEqual(expected.g, actual.g, tolerance, $"{context} G");
            Assert.AreEqual(expected.b, actual.b, tolerance, $"{context} B");
            Assert.AreEqual(expected.a, actual.a, tolerance, $"{context} A");
        }

        private sealed class StubRandom : AbstractRandom
        {
            private readonly Queue<float> _floatValues = new();

            public int RemainingSamples => _floatValues.Count;

            public void EnqueueFloat(float value)
            {
                _floatValues.Enqueue(value);
            }

            public override float NextFloat()
            {
                if (_floatValues.Count == 0)
                {
                    throw new InvalidOperationException(
                        "No float samples enqueued for StubRandom."
                    );
                }

                return _floatValues.Dequeue();
            }

            public override RandomState InternalState => default;

            public override uint NextUint()
            {
                throw new NotSupportedException("StubRandom does not provide NextUint.");
            }

            public override IRandom Copy()
            {
                StubRandom copy = new();
                foreach (float value in _floatValues)
                {
                    copy.EnqueueFloat(value);
                }
                return copy;
            }
        }
    }
}
