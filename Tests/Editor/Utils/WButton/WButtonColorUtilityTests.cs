// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class WButtonColorUtilityTests
    {
        /// <summary>
        /// Comfortably above the largest darken amount, so every colour at or over it has room to
        /// darken without the test having to know the private constants.
        /// </summary>
        private const float DarkenFloor = 0.25f;

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
        /// Anything with room to darken must darken; only a colour already at the floor may go the
        /// other way, which is the sole reason a near-black button has any feedback at all.
        /// </remarks>
        [Test]
        public void HoverAndActiveDarkenEveryColorWithRoomToDarken()
        {
            for (int r = 0; r < 16; ++r)
            {
                for (int g = 0; g < 16; ++g)
                {
                    for (int b = 0; b < 16; ++b)
                    {
                        Color source = new(r / 15f, g / 15f, b / 15f, 1f);
                        if (source.maxColorComponent < DarkenFloor)
                        {
                            continue;
                        }

                        float sourceValue = source.maxColorComponent;

                        Assert.Less(
                            WButtonColorUtility.GetHoverColor(source).maxColorComponent,
                            sourceValue,
                            $"Hover did not darken {source}."
                        );
                        Assert.Less(
                            WButtonColorUtility.GetActiveColor(source).maxColorComponent,
                            sourceValue,
                            $"Active did not darken {source}."
                        );
                    }
                }
            }
        }

        /// <remarks>
        /// Darkening a colour already at the floor clamps it to black, so rest, hover and press used to
        /// render identically and a dark button gave no feedback whatsoever.
        /// </remarks>
        [Test]
        public void HoverAndActiveAreDistinctFromRestAndFromEachOther()
        {
            for (int hue = 0; hue < 12; ++hue)
            {
                for (int value = 0; value <= 100; ++value)
                {
                    Color source = Color.HSVToRGB(hue / 12f, 0.8f, value / 100f);
                    source.a = 1f;

                    Color hover = WButtonColorUtility.GetHoverColor(source);
                    Color active = WButtonColorUtility.GetActiveColor(source);

                    Assert.Greater(
                        Distance(hover, source),
                        0f,
                        $"Hover gave no feedback for {source}."
                    );
                    Assert.Greater(
                        Distance(active, source),
                        Distance(hover, source),
                        $"Press did not move further than hover for {source}."
                    );
                }
            }
        }

        private static float Distance(Color first, Color second)
        {
            return Mathf.Abs(first.r - second.r)
                + Mathf.Abs(first.g - second.g)
                + Mathf.Abs(first.b - second.b);
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

        /// <remarks>
        /// The old rule compared Rec.601 luma against a threshold, which measures brightness rather
        /// than contrast. It
        /// chose the less readable of black and white on 22.9% of the colour cube - worst at
        /// <c>rgb(0, 0.937, 0)</c>, where it picked white at 1.58:1 over black at 13.32:1.
        /// </remarks>
        [Test]
        public void GetReadableTextColorAlwaysPicksTheHigherContrastOption()
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
                        Color other = text == Color.black ? Color.white : Color.black;
                        Assert.GreaterOrEqual(
                            ColorContrast.ContrastRatio(source, text),
                            ColorContrast.ContrastRatio(source, other),
                            $"Readable text for {source} was the less readable choice."
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
