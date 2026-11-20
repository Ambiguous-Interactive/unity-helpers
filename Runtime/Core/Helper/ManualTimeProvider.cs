// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;

    /// <summary>A clock with manually controlled UTC wall time and elapsed time.</summary>
    public sealed class ManualTimeProvider : WallTimeProvider
    {
        /// <inheritdoc />
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        private readonly object _gate = new();
        private DateTimeOffset _utcNow;
        private long _timestamp;

        /// <summary>Starts at the Unix epoch with a zero timestamp.</summary>
        public ManualTimeProvider()
            : this(DateTimeOffset.UnixEpoch) { }

        /// <summary>Starts at the supplied instant with a zero timestamp.</summary>
        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow.ToUniversalTime();
        }

        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }

        /// <inheritdoc />
        public override long GetTimestamp()
        {
            lock (_gate)
            {
                return _timestamp;
            }
        }

        /// <summary>Sets the wall clock without moving the monotonic counter.</summary>
        public void SetUtcNow(DateTimeOffset utcNow)
        {
            lock (_gate)
            {
                _utcNow = utcNow.ToUniversalTime();
            }
        }

        /// <summary>Advances both clocks by the specified nonnegative duration.</summary>
        /// <returns>True on success. If either clock would exceed its bounds, both remain unchanged.</returns>
        public bool TryAdvance(TimeSpan duration)
        {
            long ticks = duration.Ticks;
            if (ticks < 0)
            {
                return false;
            }

            lock (_gate)
            {
                if (
                    DateTimeOffset.MaxValue.Ticks - _utcNow.Ticks < ticks
                    || long.MaxValue - _timestamp < ticks
                )
                {
                    return false;
                }

                _utcNow = _utcNow.AddTicks(ticks);
                _timestamp += ticks;
                return true;
            }
        }
    }
}
