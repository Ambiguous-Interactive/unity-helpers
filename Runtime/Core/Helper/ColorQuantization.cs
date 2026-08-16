// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System.Runtime.CompilerServices;
    using UnityEngine;

    /// <summary>
    /// The one place 8-bit color channels are converted to and from normalized floats.
    /// </summary>
    /// <remarks>
    /// Three operations look interchangeable and are not. Mixing them is the defect this type exists
    /// to prevent, and it is the mistake called out in
    /// <see href="https://30fps.net/pages/255-vs-256-division/">Should you normalize RGB values by 255 or 256?</see>:
    /// "one should never mix the encode and decode steps of the two quantizers. That's just broken code."
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="ToNormalized(byte)"/> decodes, dividing by 255 so that 0 maps to 0 and 255 maps to 1.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="ToByte(float)"/> encodes, rounding to the nearest representable channel. Truncating
    /// instead doubles the mean quantization error and makes 255 unreachable for every input short of
    /// exactly 1.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="ToThresholdByte(float)"/> is neither. It answers "which bytes satisfy
    /// <c>channel / 255f &lt;= cutoff</c>", and only flooring reproduces that comparison exactly -
    /// rounding or ceiling misclassifies the channel sitting on the boundary.
    /// </description>
    /// </item>
    /// </list>
    /// Thread Safety: thread-safe; pure functions over value types.
    /// Allocations: none.
    /// </remarks>
    public static class ColorQuantization
    {
        /// <summary>
        /// The number of distinct steps between two adjacent 8-bit channel values, as a normalized float.
        /// </summary>
        public const float ChannelStep = 1f / 255f;

        /// <summary>
        /// Decodes an 8-bit color channel to its normalized [0, 1] float value.
        /// </summary>
        /// <param name="channel">The 8-bit channel value.</param>
        /// <returns>The channel divided by 255, so 0 maps to 0 and 255 maps to 1.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToNormalized(byte channel)
        {
            return channel * ChannelStep;
        }

        /// <summary>
        /// Encodes a normalized [0, 1] float to the nearest 8-bit color channel.
        /// </summary>
        /// <param name="normalized">The normalized channel value. Values outside [0, 1] are clamped, and NaN encodes to 0.</param>
        /// <returns>The nearest 8-bit channel, matching Unity's own <see cref="Color"/> to <see cref="Color32"/> conversion.</returns>
        /// <remarks>
        /// Edge Cases: NaN clamps to 0 because <see cref="Mathf.Clamp01(float)"/> returns 0 for it, and
        /// infinities clamp to 0 and 255. An unclamped cast of an out-of-range float to <see cref="byte"/>
        /// is undefined in C#, so the clamp is load-bearing rather than defensive.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ToByte(float normalized)
        {
            return (byte)Mathf.RoundToInt(Mathf.Clamp01(normalized) * 255f);
        }

        /// <summary>
        /// Converts a normalized cutoff into the largest 8-bit channel that still satisfies
        /// <c>channel / 255f &lt;= cutoff</c>.
        /// </summary>
        /// <param name="cutoff">The normalized cutoff. Values outside [0, 1] are clamped, and NaN clamps to 0.</param>
        /// <returns>The inclusive upper bound, so <c>channel &lt;= result</c> is exactly the float comparison.</returns>
        /// <remarks>
        /// Use this - never <see cref="ToByte(float)"/> - when comparing stored <see cref="Color32"/>
        /// channels against a float cutoff. Rounding the cutoff instead misclassifies one channel value
        /// at nearly every cutoff, which is how two callers of the same cutoff disagree about which
        /// pixels are transparent.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ToThresholdByte(float cutoff)
        {
            return (byte)Mathf.FloorToInt(Mathf.Clamp01(cutoff) * 255f);
        }
    }
}
