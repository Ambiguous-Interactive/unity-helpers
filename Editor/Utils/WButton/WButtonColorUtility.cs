// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils.WButton
{
#if UNITY_EDITOR
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    internal static class WButtonColorUtility
    {
        private const float GoldenRatio = 0.61803398875f;
        private const float DefaultSaturation = 0.65f;
        private const float DefaultValue = 0.9f;

        // ShiftValue relies on ActiveDarkenAmount being the larger of the two.
        private const float ActiveDarkenAmount = 0.12f;
        private const float HoverDarkenAmount = 0.06f;

        internal static Color SuggestPaletteColor(int index)
        {
            float hue = Mathf.Repeat(0.05f + (index * GoldenRatio), 1f);
            Color color = Color.HSVToRGB(hue, DefaultSaturation, DefaultValue);
            color.a = 1f;
            return color;
        }

        internal static Color GetReadableTextColor(Color background)
        {
            return ColorContrast.ReadableTextColor(background);
        }

        internal static Color GetHoverColor(Color baseColor)
        {
            return ShiftValue(baseColor, HoverDarkenAmount);
        }

        internal static Color GetActiveColor(Color baseColor)
        {
            return ShiftValue(baseColor, ActiveDarkenAmount);
        }

        /// <remarks>
        /// Darkens, except where darkening has nowhere to go. A button below the requested amount
        /// clamps to black for every state, so a near-black one gave the same color for rest, hover and
        /// press - no feedback at all. Lightening there keeps the states distinct and keeps their
        /// relative order: press always moves further than hover.
        /// The direction is decided by the <em>larger</em> of the two shifts rather than by this call's
        /// own amount, so hover and press always move the same way. Testing each against its own amount
        /// instead would make a color between them darken on hover and lighten on press.
        /// </remarks>
        private static Color ShiftValue(Color color, float amount)
        {
            Color.RGBToHSV(color, out float hue, out float saturation, out float value);
            saturation = Clamp01OrZero(saturation);
            value = Clamp01OrZero(value);
            value = Clamp01OrZero(ActiveDarkenAmount <= value ? value - amount : value + amount);
            Color adjusted = Color.HSVToRGB(hue, saturation, value);
            adjusted.a = color.a;
            return adjusted;
        }

        /// <remarks>
        /// Saturation above 1 - which <see cref="Color.RGBToHSV"/> reports for an out-of-gamut input -
        /// makes <see cref="Color.HSVToRGB"/> emit negative channels, so the result has to be bounded
        /// before the conversion rather than after it. <see cref="Mathf.Clamp01(float)"/> returns NaN
        /// for NaN because every comparison against NaN is false; ordering the NaN test first makes 0
        /// the answer by construction.
        /// </remarks>
        private static float Clamp01OrZero(float value)
        {
            if (!(0f < value))
            {
                return 0f;
            }

            return 1f <= value ? 1f : value;
        }
    }
#endif
}
