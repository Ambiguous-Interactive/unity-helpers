// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Diagnostics;
    using System.Threading;

    /// <summary>Supplies UTC wall time and monotonic timestamps independently of Unity time.</summary>
    public abstract class WallTimeProvider
    {
        /// <summary>The system clock backed by UTC wall time and the Stopwatch counter.</summary>
        public static WallTimeProvider System { get; } = new SystemClock();

        /// <summary>The shared provider for callers that do not receive one explicitly.</summary>
        /// <remarks>Assign null to restore the system clock. Capture this property once per operation.</remarks>
        public static WallTimeProvider Current
        {
            get => Volatile.Read(ref _current);
            set => Volatile.Write(ref _current, value ?? System);
        }

        private static WallTimeProvider _current = System;

        /// <summary>The positive number of counter increments per second.</summary>
        public abstract long TimestampFrequency { get; }

        /// <summary>Reads the current wall time with a zero UTC offset.</summary>
        public abstract DateTimeOffset GetUtcNow();

        /// <summary>Reads the monotonic counter for measuring elapsed time.</summary>
        public abstract long GetTimestamp();

        /// <summary>Converts the signed difference between two timestamps from this provider.</summary>
        /// <remarks>Subtraction wraps at the counter boundary. The result truncates to TimeSpan ticks
        /// and saturates at the TimeSpan limits. A provider with an invalid frequency returns zero.</remarks>
        public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
        {
            long frequency = TimestampFrequency;
            if (frequency <= 0)
            {
                return TimeSpan.Zero;
            }

            long elapsed = unchecked(endingTimestamp - startingTimestamp);
            long ticks =
                frequency == TimeSpan.TicksPerSecond
                    ? elapsed
                    : StopwatchTimestamp.ScaleTicks(elapsed, TimeSpan.TicksPerSecond, frequency);
            return TimeSpan.FromTicks(ticks);
        }

        /// <summary>Reads the current counter once and returns the elapsed time since the start.</summary>
        public TimeSpan GetElapsedTime(long startingTimestamp)
        {
            return GetElapsedTime(startingTimestamp, GetTimestamp());
        }

        private sealed class SystemClock : WallTimeProvider
        {
            public override long TimestampFrequency => Stopwatch.Frequency;

            public override DateTimeOffset GetUtcNow()
            {
                return DateTimeOffset.UtcNow;
            }

            public override long GetTimestamp()
            {
                return Stopwatch.GetTimestamp();
            }
        }
    }
}
