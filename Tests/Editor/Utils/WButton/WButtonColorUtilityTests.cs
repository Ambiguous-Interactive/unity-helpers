// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class WButtonColorUtilityTests
    {
        /// <remarks>
        /// The darkened colour is written straight into a 1x1 RGBA texture that becomes the button
        /// background, so a channel outside [0, 1] is not a rounding curiosity - it is a colour the
        /// texture cannot hold. <c>Color.RGBToHSV</c> reports saturation above 1 for an out-of-gamut
        /// input and <c>Color.HSVToRGB</c> turns that into negative channels.
        /// </remarks>
        [Test]
        public void HoverAndActiveStayInGamutForOutOfGamutInput()
        {
            Color outOfGamut = new(-0.5f, 2f, 0.5f, 1f);

            AssertInGamut(WButtonColorUtility.GetHoverColor(outOfGamut), nameof(outOfGamut));
            AssertInGamut(WButtonColorUtility.GetActiveColor(outOfGamut), nameof(outOfGamut));
        }

        [Test]
        public void HoverAndActiveStayInGamutForNaNInput()
        {
            Color notANumber = new(float.NaN, float.NaN, float.NaN, 1f);

            AssertInGamut(WButtonColorUtility.GetHoverColor(notANumber), nameof(notANumber));
            AssertInGamut(WButtonColorUtility.GetActiveColor(notANumber), nameof(notANumber));
        }

        [Test]
        public void HoverAndActiveStayInGamutForInfiniteInput()
        {
            Color positive = new(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity,
                1f
            );
            Color negative = new(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity,
                1f
            );

            AssertInGamut(WButtonColorUtility.GetHoverColor(positive), nameof(positive));
            AssertInGamut(WButtonColorUtility.GetActiveColor(positive), nameof(positive));
            AssertInGamut(WButtonColorUtility.GetHoverColor(negative), nameof(negative));
            AssertInGamut(WButtonColorUtility.GetActiveColor(negative), nameof(negative));
        }

        /// <remarks>
        /// Alpha is the one channel the darkening must not touch: palette colours come from a colour
        /// field that exposes alpha, and <c>Color.HSVToRGB</c> always returns an opaque colour, so
        /// the explicit copy-back is load-bearing.
        /// </remarks>
        [Test]
        public void HoverAndActivePreserveAlphaAcrossTheColorCube()
        {
            for (int r = 0; r < 16; ++r)
            {
                for (int g = 0; g < 16; ++g)
                {
                    for (int b = 0; b < 16; ++b)
                    {
                        Color source = new(r / 15f, g / 15f, b / 15f, 0.25f);

                        Assert.AreEqual(
                            source.a,
                            WButtonColorUtility.GetHoverColor(source).a,
                            $"Hover changed alpha for {source}."
                        );
                        Assert.AreEqual(
                            source.a,
                            WButtonColorUtility.GetActiveColor(source).a,
                            $"Active changed alpha for {source}."
                        );
                    }
                }
            }
        }

        /// <remarks>
        /// Hover and active are the only thing distinguishing the three interaction states - the
        /// styles are otherwise identical - so a "darken" that brightens would invert the feedback.
        /// </remarks>
        [Test]
        public void HoverAndActiveNeverBrightenAnInGamutColor()
        {
            for (int r = 0; r < 16; ++r)
            {
                for (int g = 0; g < 16; ++g)
                {
                    for (int b = 0; b < 16; ++b)
                    {
                        Color source = new(r / 15f, g / 15f, b / 15f, 1f);
                        float sourceValue = source.maxColorComponent;

                        Assert.LessOrEqual(
                            WButtonColorUtility.GetHoverColor(source).maxColorComponent,
                            sourceValue + 1e-5f,
                            $"Hover brightened {source}."
                        );
                        Assert.LessOrEqual(
                            WButtonColorUtility.GetActiveColor(source).maxColorComponent,
                            sourceValue + 1e-5f,
                            $"Active brightened {source}."
                        );
                    }
                }
            }
        }

        [Test]
        public void SuggestPaletteColorIsOpaqueAndInGamutForAnyIndex()
        {
            int[] indices = { int.MinValue, -1, 0, 1, 2, 63, 255, 1024, int.MaxValue };
            foreach (int index in indices)
            {
                Color suggested = WButtonColorUtility.SuggestPaletteColor(index);
                AssertInGamut(suggested, $"index {index}");
                Assert.AreEqual(1f, suggested.a, $"index {index} was not opaque.");
            }
        }

        [Test]
        public void GetReadableTextColorReturnsAnOpaqueMonochrome()
        {
            for (int r = 0; r < 16; ++r)
            {
                for (int g = 0; g < 16; ++g)
                {
                    for (int b = 0; b < 16; ++b)
                    {
                        Color source = new(r / 15f, g / 15f, b / 15f, 1f);
                        Color text = WButtonColorUtility.GetReadableTextColor(source);

                        Assert.IsTrue(
                            text == Color.black || text == Color.white,
                            $"Readable text for {source} was neither black nor white."
                        );
                    }
                }
            }
        }

        private static void AssertInGamut(Color color, string context)
        {
            Assert.IsFalse(float.IsNaN(color.r), $"{context}: red was NaN.");
            Assert.IsFalse(float.IsNaN(color.g), $"{context}: green was NaN.");
            Assert.IsFalse(float.IsNaN(color.b), $"{context}: blue was NaN.");
            Assert.GreaterOrEqual(color.r, 0f, $"{context}: red below 0.");
            Assert.GreaterOrEqual(color.g, 0f, $"{context}: green below 0.");
            Assert.GreaterOrEqual(color.b, 0f, $"{context}: blue below 0.");
            Assert.LessOrEqual(color.r, 1f, $"{context}: red above 1.");
            Assert.LessOrEqual(color.g, 1f, $"{context}: green above 1.");
            Assert.LessOrEqual(color.b, 1f, $"{context}: blue above 1.");
        }
    }
}
#endif
