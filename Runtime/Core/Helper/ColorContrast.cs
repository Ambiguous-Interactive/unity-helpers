// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using UnityEngine;

    /// <summary>
    /// Answers "can this be read against that?" the way
    /// <see href="https://www.w3.org/TR/WCAG21/#contrast-minimum">WCAG</see> defines it, rather than
    /// by how bright a color looks.
    /// </summary>
    /// <remarks>
    /// The distinction is the whole point of this type. <em>Luma</em> - the familiar
    /// <c>0.299r + 0.587g + 0.114b</c> - weights gamma-encoded channels and is a fine estimate of
    /// perceived brightness. It is not contrast. Contrast is a ratio between two colors' <em>relative
    /// luminance</em>, which is measured on linearized channels with different weights, and the two
    /// disagree most on the saturated greens and cyans a button palette is full of.
    /// Thread Safety: thread-safe; pure functions over value types.
    /// Allocations: none.
    /// </remarks>
    public static class ColorContrast
    {
        /// <summary>
        /// The lowest contrast ratio WCAG 2.1 accepts for normal-size body text (level AA).
        /// </summary>
        public const float MinimumReadableRatio = 4.5f;

        /// <summary>
        /// The lowest contrast ratio WCAG 2.1 accepts for large text and user interface components.
        /// </summary>
        public const float MinimumLargeTextRatio = 3f;

        private const float LuminanceOffset = 0.05f;

        /// <summary>
        /// Computes a color's WCAG relative luminance, the linear-light quantity contrast is defined on.
        /// </summary>
        /// <param name="color">The color. Alpha is ignored, because contrast is defined between two composited colors.</param>
        /// <returns>The relative luminance, in <c>[0, 1]</c>. Black is 0 and white is 1.</returns>
        /// <remarks>
        /// Edge Cases: each channel is bounded before it is linearized, so an out-of-gamut or HDR color
        /// saturates rather than producing a luminance outside <c>[0, 1]</c>, and a NaN channel
        /// contributes 0 instead of poisoning the sum.
        /// </remarks>
        public static float RelativeLuminance(Color color)
        {
            return (0.2126f * Linearize(color.r))
                + (0.7152f * Linearize(color.g))
                + (0.0722f * Linearize(color.b));
        }

        /// <summary>
        /// Computes the WCAG contrast ratio between two colors.
        /// </summary>
        /// <param name="first">One color.</param>
        /// <param name="second">The other color. Order does not matter.</param>
        /// <returns>
        /// A ratio in <c>[1, 21]</c>. 1 is two identical colors and 21 is black against white.
        /// Compare against <see cref="MinimumReadableRatio"/> or <see cref="MinimumLargeTextRatio"/>.
        /// </returns>
        public static float ContrastRatio(Color first, Color second)
        {
            float firstLuminance = RelativeLuminance(first);
            float secondLuminance = RelativeLuminance(second);
            float lighter = Mathf.Max(firstLuminance, secondLuminance);
            float darker = Mathf.Min(firstLuminance, secondLuminance);
            return (lighter + LuminanceOffset) / (darker + LuminanceOffset);
        }

        /// <summary>
        /// Picks whichever of black or white text is more readable on <paramref name="background"/>.
        /// </summary>
        /// <param name="background">The color the text sits on.</param>
        /// <returns><see cref="Color.black"/> or <see cref="Color.white"/>, whichever has the higher contrast ratio.</returns>
        /// <remarks>
        /// There is no threshold to tune here: the two candidate ratios are computed and the larger
        /// wins. A luma threshold picks the less readable of the two on a large share of the color
        /// cube, worst of all on saturated greens, where it can choose white at 1.58:1 over black at
        /// 13.32:1. Ties go to black, which is the choice at the crossover luminance of about 0.1791.
        /// </remarks>
        public static Color ReadableTextColor(Color background)
        {
            float luminance = RelativeLuminance(background);
            float againstBlack = (luminance + LuminanceOffset) / LuminanceOffset;
            float againstWhite = (1f + LuminanceOffset) / (luminance + LuminanceOffset);
            return againstWhite <= againstBlack ? Color.black : Color.white;
        }

        private static float Linearize(float channel)
        {
            if (!(0f < channel))
            {
                return 0f;
            }

            if (1f <= channel)
            {
                return 1f;
            }

            return channel <= 0.03928f
                ? channel / 12.92f
                : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
        }
    }
}
