// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System.Runtime.CompilerServices;
    using UnityEngine;

    /// <summary>
    /// The one place a resampler decides which source pixel a destination pixel came from, and how
    /// two colors of different opacity are allowed to mix.
    /// </summary>
    /// <remarks>
    /// Both halves exist because every resampler in this package got them wrong the same way.
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// A destination pixel covers a <em>range</em> of the source, and the sample belongs at the middle
    /// of that range. Mapping index to index instead - <c>destination * ratio</c> - shifts the image by
    /// half a destination texel toward the origin, so a symmetric image stops downscaling symmetrically
    /// and the far edge is never reached.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Interpolating straight (non-premultiplied) color drags an invisible pixel's RGB into a visible
    /// result. A fully transparent green texel beside an opaque red one produces a yellow edge, because
    /// a channel that nothing can see still got half the weight. Premultiplying first weights each
    /// color by its own opacity, which is what "blend" means.
    /// </description>
    /// </item>
    /// </list>
    /// Thread Safety: thread-safe; pure functions over value types.
    /// Allocations: none.
    /// </remarks>
    public static class TextureResampling
    {
        /// <summary>
        /// Maps a destination pixel index onto the continuous source coordinate its center samples,
        /// measured in source pixel centers and clamped into range.
        /// </summary>
        /// <param name="destinationIndex">The destination pixel index along one axis.</param>
        /// <param name="ratio">Source size divided by destination size along the same axis.</param>
        /// <param name="maxSourceIndex">The largest valid source index, that is, source size minus one.</param>
        /// <returns>
        /// A coordinate in <c>[0, maxSourceIndex]</c>. Its integer part is the first of the two source
        /// pixels to blend and its fractional part is the blend weight toward the second.
        /// </returns>
        /// <remarks>
        /// Edge Cases: a non-positive or NaN coordinate clamps to 0, so a destination pixel whose center
        /// falls outside the source (which happens at both borders of any upscale) repeats the border
        /// pixel rather than reading past it. A non-positive <paramref name="maxSourceIndex"/> always
        /// yields 0, which is the only valid index into a one-pixel axis.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float BilinearSourceCoordinate(
            int destinationIndex,
            float ratio,
            int maxSourceIndex
        )
        {
            float coordinate = ((destinationIndex + 0.5f) * ratio) - 0.5f;
            if (!(coordinate > 0f))
            {
                return 0f;
            }

            return coordinate < maxSourceIndex ? coordinate : Mathf.Max(maxSourceIndex, 0);
        }

        /// <summary>
        /// Maps a destination pixel index onto the single source pixel its center falls inside.
        /// </summary>
        /// <param name="destinationIndex">The destination pixel index along one axis.</param>
        /// <param name="ratio">Source size divided by destination size along the same axis.</param>
        /// <param name="maxSourceIndex">The largest valid source index, that is, source size minus one.</param>
        /// <returns>A source index in <c>[0, maxSourceIndex]</c>.</returns>
        /// <remarks>
        /// This is the nearest-neighbor counterpart of <see cref="BilinearSourceCoordinate"/> and shares
        /// its alignment, so point and bilinear scaling of the same texture no longer disagree about
        /// where the image sits. Edge Cases: as above, NaN and out-of-range coordinates clamp.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int NearestSourceIndex(int destinationIndex, float ratio, int maxSourceIndex)
        {
            float center = (destinationIndex + 0.5f) * ratio;
            if (!(center > 0f))
            {
                return 0;
            }

            int index = (int)center;
            return index < maxSourceIndex ? index : Mathf.Max(maxSourceIndex, 0);
        }

        /// <summary>
        /// Scales a color's RGB by its own alpha, so that it carries only the light it actually
        /// contributes to a blend.
        /// </summary>
        /// <param name="color">The straight (non-premultiplied) color.</param>
        /// <returns>The premultiplied color. Alpha is unchanged.</returns>
        /// <remarks>
        /// A fully opaque color is returned bit-for-bit, because multiplying by exactly 1 is exact in
        /// IEEE 754. Any filter over an opaque image is therefore unaffected by premultiplying.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color Premultiply(Color color)
        {
            return new Color(color.r * color.a, color.g * color.a, color.b * color.a, color.a);
        }

        /// <summary>
        /// Recovers a straight color from a filtered premultiplied one.
        /// </summary>
        /// <param name="premultiplied">The premultiplied result of a filter.</param>
        /// <param name="transparentFallback">
        /// The color to take RGB from when the filtered alpha carries no information. Pass the same
        /// filter's straight-color result, which is what the RGB of an invisible pixel meant before it
        /// was premultiplied.
        /// </param>
        /// <returns>The straight color, with <paramref name="premultiplied"/>'s alpha.</returns>
        /// <remarks>
        /// Edge Cases: alpha at or below zero (and NaN) cannot be divided out - every contributing pixel
        /// was invisible, so their premultiplied RGB is all zero and the quotient would be
        /// <c>0 / 0</c>. Falling back keeps a fully transparent image's RGB intact through a resample
        /// instead of flattening it to black, which matters when RGB carries data rather than color.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color Unpremultiply(Color premultiplied, Color transparentFallback)
        {
            float alpha = premultiplied.a;
            if (!(alpha > 0f))
            {
                return new Color(
                    transparentFallback.r,
                    transparentFallback.g,
                    transparentFallback.b,
                    alpha
                );
            }

            if (alpha == 1f)
            {
                return premultiplied;
            }

            float inverse = 1f / alpha;
            return new Color(
                premultiplied.r * inverse,
                premultiplied.g * inverse,
                premultiplied.b * inverse,
                alpha
            );
        }
    }
}
