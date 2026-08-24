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
        /// <param name="normalized">The normalized channel value. Values outside [0, 1] saturate, and NaN encodes to 0.</param>
        /// <returns>The nearest 8-bit channel, matching Unity's own <see cref="Color"/> to <see cref="Color32"/> conversion.</returns>
        /// <remarks>
        /// Edge Cases: both infinities are ordered against 0 and 1, so they saturate to 0 and 255 through
        /// the two branches below and never reach the cast. NaN is the value that does not:
        /// <see cref="Mathf.Clamp01(float)"/> returns NaN for it, because every comparison against NaN is
        /// false and it falls through both of that method's branches. The remaining cast would then be a
        /// float-to-int conversion of NaN, which C# leaves undefined; it happens to yield 0 here only
        /// because <c>(byte)int.MinValue</c> is 0. Testing <c>!(normalized &gt; 0f)</c> first puts NaN on
        /// the same branch as a negative, making 0 the answer by construction on every architecture.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ToByte(float normalized)
        {
            if (!(0f < normalized))
            {
                return byte.MinValue;
            }

            if (1f <= normalized)
            {
                return byte.MaxValue;
            }

            return (byte)Mathf.RoundToInt(normalized * 255f);
        }

        /// <summary>
        /// Converts a normalized cutoff into the largest 8-bit channel that still satisfies
        /// <c>channel / 255f &lt;= cutoff</c>.
        /// </summary>
        /// <param name="cutoff">The normalized cutoff. Values outside [0, 1] saturate, and NaN yields 0.</param>
        /// <returns>The inclusive upper bound, so <c>channel &lt;= result</c> is exactly the float comparison.</returns>
        /// <remarks>
        /// Use this - never <see cref="ToByte(float)"/> - when comparing stored <see cref="Color32"/>
        /// channels against a float cutoff. Rounding the cutoff instead misclassifies one channel value
        /// at nearly every cutoff, which is how two callers of the same cutoff disagree about which
        /// pixels are transparent.
        /// Edge Cases: NaN yields 0, so nothing but a zero channel falls under a cutoff that is not a
        /// number. See <see cref="ToByte(float)"/> for why the bound is not tested with
        /// <see cref="Mathf.Clamp01(float)"/>.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ToThresholdByte(float cutoff)
        {
            if (!(0f < cutoff))
            {
                return byte.MinValue;
            }

            if (1f <= cutoff)
            {
                return byte.MaxValue;
            }

            return (byte)Mathf.FloorToInt(cutoff * 255f);
        }

        /// <summary>
        /// Reports whether two colors are the same color, meaning they encode to the same 8-bit channels.
        /// </summary>
        /// <param name="left">One color.</param>
        /// <param name="right">The other color.</param>
        /// <returns><see langword="true"/> when every channel encodes to the same byte.</returns>
        /// <remarks>
        /// This is the one definition of "the same color" in the package, and it is the only one that
        /// can back an <see cref="System.Collections.Generic.IEqualityComparer{T}"/>: an absolute
        /// tolerance is not transitive, so no hash can agree with it. It is also the only distinction a
        /// <see cref="Color32"/>, a texture, or a serialized swatch can preserve - two colors that
        /// encode alike are indistinguishable once stored.
        /// Edge Cases: <see cref="ToByte(float)"/> maps NaN to 0, so two NaN channels compare equal.
        /// A comparison written as <c>Mathf.Abs(a - b) &lt; tolerance</c> instead answers <c>false</c>
        /// for NaN, which is how a change notifier reported "changed" on every check and never settled.
        /// </remarks>
        public static bool AreSameColor(Color left, Color right)
        {
            return ToByte(left.r) == ToByte(right.r)
                && ToByte(left.g) == ToByte(right.g)
                && ToByte(left.b) == ToByte(right.b)
                && ToByte(left.a) == ToByte(right.a);
        }
    }
}
