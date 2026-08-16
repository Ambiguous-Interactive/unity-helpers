// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ColorContrastTests
    {
        private const float Tolerance = 1e-4f;

        [Test]
        public void RelativeLuminanceMatchesTheWcagAnchors()
        {
            Assert.That(
                ColorContrast.RelativeLuminance(Color.black),
                Is.EqualTo(0f).Within(Tolerance)
            );
            Assert.That(
                ColorContrast.RelativeLuminance(Color.white),
                Is.EqualTo(1f).Within(Tolerance)
            );
            Assert.That(
                ColorContrast.RelativeLuminance(Color.red),
                Is.EqualTo(0.2126f).Within(Tolerance)
            );
            Assert.That(
                ColorContrast.RelativeLuminance(Color.green),
                Is.EqualTo(0.7152f).Within(Tolerance)
            );
            Assert.That(
                ColorContrast.RelativeLuminance(Color.blue),
                Is.EqualTo(0.0722f).Within(Tolerance)
            );
        }

        [Test]
        public void ContrastRatioSpansTheDefinedRangeAndIsSymmetric()
        {
            Assert.That(
                ColorContrast.ContrastRatio(Color.black, Color.white),
                Is.EqualTo(21f).Within(1e-3f)
            );
            Assert.That(
                ColorContrast.ContrastRatio(Color.white, Color.black),
                Is.EqualTo(21f).Within(1e-3f)
            );
            Assert.That(
                ColorContrast.ContrastRatio(Color.magenta, Color.magenta),
                Is.EqualTo(1f).Within(Tolerance)
            );
        }

        /// <remarks>
        /// This is the property the shipped luma threshold violated on 22.9% of the colour cube. It is
        /// stated as "never worse than the alternative" rather than as a threshold, because the
        /// threshold was the bug.
        /// </remarks>
        [Test]
        public void ReadableTextColorIsNeverTheLessReadableChoice()
        {
            for (int r = 0; r < 24; ++r)
            {
                for (int g = 0; g < 24; ++g)
                {
                    for (int b = 0; b < 24; ++b)
                    {
                        Color background = new(r / 23f, g / 23f, b / 23f, 1f);
                        Color text = ColorContrast.ReadableTextColor(background);
                        Color other = text == Color.black ? Color.white : Color.black;

                        Assert.GreaterOrEqual(
                            ColorContrast.ContrastRatio(background, text),
                            ColorContrast.ContrastRatio(background, other),
                            $"{background} got the less readable of black and white."
                        );
                    }
                }
            }
        }

        [Test]
        public void ReadableTextColorPicksBlackOnTheSaturatedGreenLumaGetsWrong()
        {
            // The worst case measured over the 6-bit cube: luma said white at 1.58:1 where black
            // gives 13.32:1.
            Color saturatedGreen = new(0f, 0.937f, 0f, 1f);

            Assert.AreEqual(Color.black, ColorContrast.ReadableTextColor(saturatedGreen));
            Assert.Greater(
                ColorContrast.ContrastRatio(saturatedGreen, Color.black),
                ColorContrast.MinimumReadableRatio
            );
            Assert.Less(
                ColorContrast.ContrastRatio(saturatedGreen, Color.white),
                ColorContrast.MinimumLargeTextRatio
            );
        }

        [TestCase(float.NaN, 0f)]
        [TestCase(float.PositiveInfinity, 1f)]
        [TestCase(float.NegativeInfinity, 0f)]
        [TestCase(-3f, 0f)]
        [TestCase(2.5f, 1f)]
        public void RelativeLuminanceBoundsHostileChannels(float channel, float expected)
        {
            Color hostile = new(channel, channel, channel, 1f);

            Assert.That(
                ColorContrast.RelativeLuminance(hostile),
                Is.EqualTo(expected).Within(Tolerance)
            );
        }

        [Test]
        public void ContrastRatioStaysInsideItsDefinedRangeForHostileInput()
        {
            Color notANumber = new(float.NaN, float.NaN, float.NaN, 1f);
            Color highDynamicRange = new(4f, 4f, 4f, 1f);

            float ratio = ColorContrast.ContrastRatio(notANumber, highDynamicRange);

            Assert.GreaterOrEqual(ratio, 1f);
            Assert.LessOrEqual(ratio, 21f);
        }
    }
}
