// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;

    /// <summary>
    /// Shared helpers for sanitizing probabilistic samples returned by random generators.
    /// </summary>
    public static partial class Helpers
    {
        private static readonly double LargestDoubleLessThanOne = BitConverter.Int64BitsToDouble(
            BitConverter.DoubleToInt64Bits(1d) - 1L
        );
        private static readonly float LargestFloatLessThanOne = BitConverter.Int32BitsToSingle(
            BitConverter.SingleToInt32Bits(1f) - 1
        );

        /// <summary>
        /// Clamps an arbitrary double to the half-open unit interval [0, 1).
        /// Returns 0 for NaN/negative inputs and the largest representable double &lt; 1 for values ≥ 1.
        /// </summary>
        internal static double ClampUnitInterval(double value)
        {
            if (double.IsNaN(value) || double.IsNegativeInfinity(value) || value <= 0d)
            {
                return 0d;
            }

            if (double.IsPositiveInfinity(value) || 1d <= value)
            {
                return LargestDoubleLessThanOne;
            }

            return value;
        }

        /// <summary>
        /// Clamps an arbitrary float to the half-open unit interval [0, 1).
        /// Returns 0 for NaN/negative inputs and the next representable float below 1 for values ≥ 1.
        /// </summary>
        internal static float ClampUnitInterval(float value)
        {
            if (float.IsNaN(value) || float.IsNegativeInfinity(value) || value <= 0f)
            {
                return 0f;
            }

            if (float.IsPositiveInfinity(value) || 1f <= value)
            {
                return LargestFloatLessThanOne;
            }

            return value;
        }
    }
}
