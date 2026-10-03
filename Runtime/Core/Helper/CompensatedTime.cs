// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Retains rounding residuals when comparing float clocks and durations.
    /// </summary>
    internal static class CompensatedTime
    {
        private const double FloatOverflowMidpoint = 3.4028235677973366e38d;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Sum(double left, double right, out double high, out double low)
        {
            double total = left + right;
            if (double.IsNaN(total) || double.IsInfinity(total))
            {
                high = total;
                low = 0d;
                return;
            }
            double roundedRight = total - left;
            double remainder = (left - (total - roundedRight)) + (right - roundedRight);
            high = total;
            low = remainder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool HasElapsed(
            float startedAt,
            float currentTime,
            double duration,
            double durationRemainder,
            bool inclusive
        )
        {
            Sum(currentTime, -(double)startedAt, out double elapsed, out double elapsedRemainder);
            if (double.IsNaN(duration) || double.IsNaN(elapsed))
            {
                return false;
            }
            if (duration < elapsed)
            {
                return true;
            }
            if (elapsed < duration)
            {
                return false;
            }
            return inclusive
                ? durationRemainder <= elapsedRemainder
                : durationRemainder < elapsedRemainder;
        }

        internal static float RemainingDuration(float startedAt, float duration, float currentTime)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration))
            {
                return duration < 0f ? 0f : duration;
            }
            Sum(currentTime, -(double)startedAt, out double elapsed, out double elapsedRemainder);
            Sum(duration, -elapsed, out double remaining, out double remainingRemainder);
            Sum(remaining, remainingRemainder - elapsedRemainder, out double high, out double low);
            return RoundNonnegative(high, low);
        }

        private static float RoundNonnegative(double high, double low)
        {
            if (high < 0d || (high == 0d && low < 0d))
            {
                return 0f;
            }
            int roundedBits = BitConverter.SingleToInt32Bits((float)high);
            float rounded = BitConverter.Int32BitsToSingle(roundedBits);
            if (low == 0d || double.IsNaN(high) || double.IsInfinity(high))
            {
                return rounded;
            }
            if (float.IsPositiveInfinity(rounded))
            {
                return high == FloatOverflowMidpoint && low < 0d ? float.MaxValue : rounded;
            }
            if (rounded == 0f && low < 0d)
            {
                return rounded;
            }
            int bits = rounded == 0f ? 0 : roundedBits;
            int adjacentBits = 0d < low ? bits + 1 : bits - 1;
            float adjacent = BitConverter.Int32BitsToSingle(adjacentBits);
            double midpoint = ((double)rounded + adjacent) * 0.5d;
            return high == midpoint ? adjacent : rounded;
        }
    }
}
