// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;

    /// <summary>Measures elapsed time and converts Stopwatch counter ticks without allocating a timer.</summary>
    public static class StopwatchTimestamp
    {
        private static readonly double MillisecondsPerTick = 1000.0 / Stopwatch.Frequency;
        private static readonly double NanosecondsPerTick = 1000000000.0 / Stopwatch.Frequency;

        /// <summary>Returns the signed elapsed time since a Stopwatch timestamp.</summary>
        /// <remarks>Uses the counter's wrapping subtraction; truncates to TimeSpan ticks and saturates at TimeSpan limits.</remarks>
        public static TimeSpan Since(long start)
        {
            long end = Stopwatch.GetTimestamp();
            long elapsed = unchecked(end - start);
            long ticks = ScaleTicks(elapsed, TimeSpan.TicksPerSecond, Stopwatch.Frequency);
            return TimeSpan.FromTicks(ticks);
        }

        /// <summary>Converts signed Stopwatch ticks to milliseconds.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ToMilliseconds(long ticks)
        {
            return ticks * MillisecondsPerTick;
        }

        /// <summary>Converts signed Stopwatch ticks to nanoseconds.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ToNanoseconds(long ticks)
        {
            return ticks * NanosecondsPerTick;
        }

        /// <summary>Converts a duration to Stopwatch ticks, truncating toward zero and saturating at Int64 limits.</summary>
        public static long TicksFrom(TimeSpan duration)
        {
            return ScaleTicks(duration.Ticks, Stopwatch.Frequency, TimeSpan.TicksPerSecond);
        }

        internal static long ScaleTicks(long value, long numerator, long denominator)
        {
            long whole = value / denominator;
            if (long.MaxValue / numerator < whole)
            {
                return long.MaxValue;
            }
            if (whole < long.MinValue / numerator)
            {
                return long.MinValue;
            }

            long integral = whole * numerator;
            long remainder = value % denominator;
            long fractional =
                long.MinValue / numerator <= remainder && remainder <= long.MaxValue / numerator
                    ? remainder * numerator / denominator
                    : (long)((decimal)remainder * numerator / denominator);
            if (0 < fractional && long.MaxValue - fractional < integral)
            {
                return long.MaxValue;
            }
            if (fractional < 0 && integral < long.MinValue - fractional)
            {
                return long.MinValue;
            }
            return integral + fractional;
        }
    }
}
