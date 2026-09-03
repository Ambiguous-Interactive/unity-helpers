// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Runtime.CompilerServices;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// Tracks the rolling high-water mark (peak value) within a configurable time window.
    /// Used by intelligent pool purging to determine typical usage patterns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Samples are aggregated into a fixed ring of time buckets allocated once at construction, so
    /// recording is O(1) and never allocates however many samples arrive. Peak and average are
    /// exact over the samples the ring still holds; only <em>expiry</em> is bucket-granular, and a
    /// sample leaves the window up to two bucket durations late rather than early.
    /// </para>
    /// <para>
    /// Thread safety: All operations are protected by a lock for multi-threaded environments.
    /// </para>
    /// <para>
    /// Expiry only runs forward. A read whose time is not newer than the newest recorded sample
    /// answers from the last sweep, and a sample older than the ring covers is dropped rather than
    /// recorded, because its slot belongs to a live bucket. Both are decisions about a clock that
    /// went backwards; the production time source does not.
    /// </para>
    /// </remarks>
    internal sealed class RollingHighWaterMark
    {
        /// <summary>
        /// Number of buckets spanning the configured window.
        /// </summary>
        private const int WindowBucketCount = 64;

        /// <summary>
        /// Ring length. Two buckets beyond the window so a sample at the far edge of the window is
        /// retained rather than evicted a fraction of a bucket early.
        /// </summary>
        private const int RingLength = WindowBucketCount + 2;

        private readonly Bucket[] _buckets = new Bucket[RingLength];
        private readonly object _lock = new object();
        private float _windowSeconds;
        private float _bucketSeconds;
        private long _newestEpoch;
        private bool _hasEpoch;
        private int _cachedPeak;
        private long _runningSum;
        private long _sampleCount;

        /// <summary>
        /// Gets or sets the rolling window duration in seconds. Changing it discards the samples
        /// recorded against the previous bucket duration.
        /// </summary>
        public float WindowSeconds
        {
            get
            {
                lock (_lock)
                {
                    return _windowSeconds;
                }
            }
            set
            {
                lock (_lock)
                {
                    float sanitized =
                        0f < value ? value : PoolPurgeSettings.DefaultRollingWindowSeconds;
                    if (sanitized == _windowSeconds)
                    {
                        return;
                    }

                    _windowSeconds = sanitized;
                    _bucketSeconds = sanitized / WindowBucketCount;
                    ClearCore();
                }
            }
        }

        /// <summary>
        /// Gets the current peak value within the rolling window.
        /// </summary>
        public int Peak
        {
            get
            {
                lock (_lock)
                {
                    return _cachedPeak;
                }
            }
        }

        /// <summary>
        /// Gets the number of samples currently inside the window.
        /// </summary>
        internal int SampleCount
        {
            get
            {
                lock (_lock)
                {
                    return _sampleCount <= int.MaxValue ? (int)_sampleCount : int.MaxValue;
                }
            }
        }

        /// <summary>
        /// Creates a new rolling high-water mark tracker.
        /// </summary>
        /// <param name="windowSeconds">The rolling window duration in seconds.</param>
        public RollingHighWaterMark(float windowSeconds)
        {
            _windowSeconds =
                0f < windowSeconds ? windowSeconds : PoolPurgeSettings.DefaultRollingWindowSeconds;
            _bucketSeconds = _windowSeconds / WindowBucketCount;
            _cachedPeak = 0;
        }

        /// <summary>
        /// Records a new sample value at the specified time.
        /// </summary>
        /// <param name="currentTime">The current time.</param>
        /// <param name="value">The value to record.</param>
        public void Record(float currentTime, int value)
        {
            lock (_lock)
            {
                RecordCore(currentTime, value);
            }
        }

        /// <summary>
        /// Gets the current peak value within the rolling window, expiring aged buckets.
        /// </summary>
        /// <param name="currentTime">The current time.</param>
        /// <returns>The peak value within the window.</returns>
        public int GetPeak(float currentTime)
        {
            lock (_lock)
            {
                AdvanceTo(EpochFor(currentTime));
                return _cachedPeak;
            }
        }

        /// <summary>
        /// Records a new sample and returns the average in a single lock acquisition.
        /// This avoids the overhead of acquiring the lock twice when both operations are needed.
        /// </summary>
        /// <param name="currentTime">The current time.</param>
        /// <param name="value">The value to record.</param>
        /// <returns>The average value within the window after recording, or 0 if no samples exist.</returns>
        public float RecordAndGetAverage(float currentTime, int value)
        {
            lock (_lock)
            {
                RecordCore(currentTime, value);
                return AverageCore();
            }
        }

        /// <summary>
        /// Gets the average value within the rolling window.
        /// </summary>
        /// <param name="currentTime">The current time.</param>
        /// <returns>The average value, or 0 if no samples exist.</returns>
        public float GetAverage(float currentTime)
        {
            lock (_lock)
            {
                AdvanceTo(EpochFor(currentTime));
                return AverageCore();
            }
        }

        /// <summary>
        /// Clears all recorded samples and resets the peak.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                ClearCore();
            }
        }

        private void RecordCore(float currentTime, int value)
        {
            long epoch = EpochFor(currentTime);
            AdvanceTo(epoch);

            if (epoch < OldestLiveEpoch(_newestEpoch))
            {
                if (_sampleCount != 0)
                {
                    /*
                        A sample older than the ring covers has no slot: its index aliases a bucket
                        a live epoch already owns, and writing there would silently replace that
                        bucket's contribution to the running totals. It is outside the window, so
                        it is dropped rather than recorded. Only an out-of-order clock produces one.
                    */
                    return;
                }

                /*
                    Nothing is live, so there is nothing to protect and the epoch being compared
                    against came from a clock reading rather than from data -- a query at an absurd
                    time, or a read that arrived before the first record. Re-seed on this sample
                    instead of refusing every sample from here on.
                */
                ClearCore();
                _hasEpoch = true;
                _newestEpoch = epoch;
            }

            int index = (int)epoch.PositiveMod(RingLength);
            if (_buckets[index].Occupied && _buckets[index].Epoch == epoch)
            {
                if (_buckets[index].Peak < value)
                {
                    _buckets[index].Peak = value;
                }
                _buckets[index].Sum += value;
                _buckets[index].Count++;
            }
            else
            {
                /*
                    Every live epoch is within RingLength - 1 of the newest, so live epochs never
                    share a slot; AdvanceTo has already emptied whatever expired. This slot is
                    therefore free.
                */
                _buckets[index].Occupied = true;
                _buckets[index].Epoch = epoch;
                _buckets[index].Peak = value;
                _buckets[index].Sum = value;
                _buckets[index].Count = 1;
            }

            _runningSum += value;
            _sampleCount++;
            if (_cachedPeak < value)
            {
                _cachedPeak = value;
            }
        }

        private float AverageCore()
        {
            return _sampleCount == 0 ? 0f : (float)((double)_runningSum / _sampleCount);
        }

        private void AdvanceTo(long epoch)
        {
            if (!_hasEpoch)
            {
                _hasEpoch = true;
                _newestEpoch = epoch;
                return;
            }

            if (epoch <= _newestEpoch)
            {
                return;
            }

            _newestEpoch = epoch;
            long oldestLiveEpoch = OldestLiveEpoch(epoch);
            bool expiredAny = false;
            for (int index = 0; index < RingLength; ++index)
            {
                if (!_buckets[index].Occupied || oldestLiveEpoch <= _buckets[index].Epoch)
                {
                    continue;
                }

                _buckets[index] = default;
                expiredAny = true;
            }

            if (!expiredAny)
            {
                return;
            }

            _runningSum = 0;
            _sampleCount = 0;
            _cachedPeak = 0;
            for (int index = 0; index < RingLength; ++index)
            {
                if (!_buckets[index].Occupied)
                {
                    continue;
                }

                _runningSum += _buckets[index].Sum;
                _sampleCount += _buckets[index].Count;
                if (_cachedPeak < _buckets[index].Peak)
                {
                    _cachedPeak = _buckets[index].Peak;
                }
            }
        }

        private static long OldestLiveEpoch(long newestEpoch)
        {
            return newestEpoch < long.MinValue + (RingLength - 1)
                ? long.MinValue
                : newestEpoch - (RingLength - 1);
        }

        private long EpochFor(float currentTime)
        {
            /*
                A denormal window divides to a bucket duration of zero, and 0f / 0f is NaN, whose
                conversion to long is undefined rather than merely wrong. Answering with the newest
                epoch keeps every sample in one bucket, which is what a window that small means.
            */
            if (float.IsNaN(currentTime) || float.IsInfinity(currentTime) || _bucketSeconds <= 0f)
            {
                return _newestEpoch;
            }

            double epoch = Math.Floor(currentTime / _bucketSeconds);
            if (epoch <= long.MinValue)
            {
                return long.MinValue;
            }

            return long.MaxValue <= epoch ? long.MaxValue : (long)epoch;
        }

        private void ClearCore()
        {
            Array.Clear(_buckets, 0, RingLength);
            _runningSum = 0;
            _sampleCount = 0;
            _cachedPeak = 0;
            _hasEpoch = false;
            _newestEpoch = 0;
        }

        private struct Bucket
        {
            public long Epoch;
            public long Sum;
            public int Peak;
            public int Count;
            public bool Occupied;
        }
    }

    /// <summary>
    /// Immutable snapshot of all purge-related parameters computed in a single lock acquisition.
    /// Used to reduce lock contention on the hot purge path.
    /// </summary>
    internal readonly struct PurgeParameters
    {
        public readonly float EffectiveIdleTimeout;
        public readonly int EffectiveMinRetainCount;
        public readonly int ComfortableSize;
        public readonly bool InHysteresis;

        public PurgeParameters(
            float effectiveIdleTimeout,
            int effectiveMinRetainCount,
            int comfortableSize,
            bool inHysteresis
        )
        {
            EffectiveIdleTimeout = effectiveIdleTimeout;
            EffectiveMinRetainCount = effectiveMinRetainCount;
            ComfortableSize = comfortableSize;
            InHysteresis = inHysteresis;
        }
    }

    /// <summary>
    /// Tracks usage statistics for intelligent pool purging.
    /// </summary>
    /// <remarks>
    /// This class maintains concurrent rental tracking, spike detection,
    /// and access frequency metrics to enable hysteresis-based purge protection
    /// and frequency-informed purge decisions.
    /// </remarks>
    internal sealed class PoolUsageTracker
    {
        /// <summary>
        /// Number of seconds per minute for time conversions.
        /// </summary>
        private const float SecondsPerMinute = 60f;

        /// <summary>
        /// Default duration in seconds for frequency tracking window (1 minute).
        /// </summary>
        private const float DefaultFrequencyWindowSeconds = 60f;

        /// <summary>
        /// Buffer cap applied under medium memory pressure (no additional buffer).
        /// </summary>
        private const float MediumPressureBufferCap = 1.0f;

        /// <summary>
        /// Buffer cap applied under low memory pressure (modest additional buffer).
        /// </summary>
        private const float LowPressureBufferCap = 1.5f;

        /// <summary>
        /// Multiplier for high-frequency pools to increase buffer size.
        /// High-frequency pools get 50% extra buffer.
        /// </summary>
        private const float HighFrequencyBufferBoost = 1.5f;

        /// <summary>
        /// Threshold for rentals-per-minute to be considered high frequency.
        /// Pools with 10+ rentals per minute are high-frequency.
        /// </summary>
        private const float HighFrequencyThreshold = 10f;

        /// <summary>
        /// Multiplier for low-frequency pools idle timeout reduction.
        /// Low-frequency pools purge 50% faster.
        /// </summary>
        private const float LowFrequencyTimeoutMultiplier = 0.5f;

        /// <summary>
        /// Threshold for rentals-per-minute to be considered low frequency.
        /// Pools with at most 1 rental per minute are low-frequency.
        /// </summary>
        private const float LowFrequencyThreshold = 1f;

        /// <summary>
        /// Threshold in minutes for unused pool aggressive purge.
        /// Pools with no access for 5+ minutes are candidates for aggressive purge.
        /// </summary>
        private const float UnusedPoolThresholdMinutes = 5f;

        private readonly RollingHighWaterMark _rollingHighWaterMark;
        private readonly object _lock = new object();

        private int _currentlyRented;
        private int _peakConcurrentRentals;
        private float _lastRentalTime;
        private float _lastReturnTime;
        private float _lastSpikeTime;
        private float _hysteresisSeconds;
        private float _spikeThresholdMultiplier;
        private float _bufferMultiplier;

        private int _rentalCountThisWindow;
        private float _windowStartTime;
        private float _cachedRentalsPerMinute;
        private long _totalRentalCount;
        private double _totalInterRentalTimeSeconds;
        private int _interRentalCount;
        private float _previousRentalTime;

        /// <summary>
        /// Gets the current number of items rented from the pool.
        /// </summary>
        public int CurrentlyRented
        {
            get
            {
                lock (_lock)
                {
                    return _currentlyRented;
                }
            }
        }

        /// <summary>
        /// Gets the all-time peak of concurrent rentals.
        /// </summary>
        public int PeakConcurrentRentals
        {
            get
            {
                lock (_lock)
                {
                    return _peakConcurrentRentals;
                }
            }
        }

        /// <summary>
        /// Gets the time of the last rental.
        /// </summary>
        public float LastRentalTime
        {
            get
            {
                lock (_lock)
                {
                    return _lastRentalTime;
                }
            }
        }

        /// <summary>
        /// Gets the time of the last return.
        /// </summary>
        public float LastReturnTime
        {
            get
            {
                lock (_lock)
                {
                    return _lastReturnTime;
                }
            }
        }

        /// <summary>
        /// Gets the time of the most recent access (rent or return).
        /// </summary>
        public float LastAccessTime
        {
            get
            {
                lock (_lock)
                {
                    return _lastReturnTime < _lastRentalTime ? _lastRentalTime : _lastReturnTime;
                }
            }
        }

        /// <summary>
        /// Gets the current rentals-per-minute rate based on the rolling frequency window.
        /// </summary>
        public float RentalsPerMinute
        {
            get
            {
                lock (_lock)
                {
                    return _cachedRentalsPerMinute;
                }
            }
        }

        /// <summary>
        /// Gets the average time between consecutive rentals in seconds.
        /// This represents the inter-arrival time between rental operations, not the duration items are held.
        /// Returns 0 if fewer than two rentals have occurred.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This metric tracks the time between consecutive rent calls, which can be accurately measured
        /// without per-item state tracking. For concurrent pools with overlapping rentals, this provides
        /// a useful measure of rental frequency (inverse of rentals-per-second).
        /// </para>
        /// <para>
        /// Example: If rentals occur at t=0, t=1, t=3, the average inter-rental time is (1 + 2) / 2 = 1.5 seconds.
        /// </para>
        /// </remarks>
        public float AverageInterRentalTimeSeconds
        {
            get
            {
                lock (_lock)
                {
                    if (_interRentalCount == 0)
                    {
                        return 0f;
                    }
                    return (float)(_totalInterRentalTimeSeconds / _interRentalCount);
                }
            }
        }

        /// <summary>
        /// Gets the total number of rentals since pool creation.
        /// </summary>
        public long TotalRentalCount
        {
            get
            {
                lock (_lock)
                {
                    return _totalRentalCount;
                }
            }
        }

        /// <summary>
        /// Gets or sets the hysteresis duration in seconds.
        /// </summary>
        public float HysteresisSeconds
        {
            get
            {
                lock (_lock)
                {
                    return _hysteresisSeconds;
                }
            }
            set
            {
                lock (_lock)
                {
                    _hysteresisSeconds = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the spike threshold multiplier.
        /// </summary>
        public float SpikeThresholdMultiplier
        {
            get
            {
                lock (_lock)
                {
                    return _spikeThresholdMultiplier;
                }
            }
            set
            {
                lock (_lock)
                {
                    _spikeThresholdMultiplier = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the buffer multiplier.
        /// </summary>
        public float BufferMultiplier
        {
            get
            {
                lock (_lock)
                {
                    return _bufferMultiplier;
                }
            }
            set
            {
                lock (_lock)
                {
                    _bufferMultiplier = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the rolling window duration in seconds.
        /// </summary>
        public float RollingWindowSeconds
        {
            get => _rollingHighWaterMark.WindowSeconds;
            set => _rollingHighWaterMark.WindowSeconds = value;
        }

        /// <summary>
        /// Creates a new pool usage tracker.
        /// </summary>
        /// <param name="rollingWindowSeconds">The rolling window duration for high-water mark tracking.</param>
        /// <param name="hysteresisSeconds">The hysteresis duration after spikes.</param>
        /// <param name="spikeThresholdMultiplier">The multiplier for spike detection.</param>
        /// <param name="bufferMultiplier">The buffer multiplier for comfortable size calculation.</param>
        public PoolUsageTracker(
            float rollingWindowSeconds,
            float hysteresisSeconds,
            float spikeThresholdMultiplier,
            float bufferMultiplier
        )
        {
            _rollingHighWaterMark = new RollingHighWaterMark(rollingWindowSeconds);
            _hysteresisSeconds = hysteresisSeconds;
            _spikeThresholdMultiplier = spikeThresholdMultiplier;
            _bufferMultiplier = bufferMultiplier;
        }

        /// <summary>
        /// Records a rental operation.
        /// </summary>
        /// <param name="currentTime">The current time.</param>
        public void RecordRent(float currentTime)
        {
            lock (_lock)
            {
                _currentlyRented++;
                _totalRentalCount++;

                if (0f < _previousRentalTime && _previousRentalTime <= currentTime)
                {
                    float interRentalTime = currentTime - _previousRentalTime;
                    _totalInterRentalTimeSeconds += interRentalTime;
                    _interRentalCount++;
                }

                _previousRentalTime = currentTime;
                _lastRentalTime = currentTime;

                if (_peakConcurrentRentals < _currentlyRented)
                {
                    _peakConcurrentRentals = _currentlyRented;
                }

                float average = _rollingHighWaterMark.RecordAndGetAverage(
                    currentTime,
                    _currentlyRented
                );
                if (
                    0f < _spikeThresholdMultiplier
                    && average * _spikeThresholdMultiplier < _currentlyRented
                )
                {
                    _lastSpikeTime = currentTime;
                }

                UpdateFrequencyTracking(currentTime);
            }
        }

        /// <summary>
        /// Records a return operation.
        /// </summary>
        /// <param name="currentTime">The current time.</param>
        public void RecordReturn(float currentTime)
        {
            lock (_lock)
            {
                if (0 < _currentlyRented)
                {
                    _currentlyRented--;
                }

                _lastReturnTime = currentTime;
                _rollingHighWaterMark.Record(currentTime, _currentlyRented);
            }
        }

        /// <summary>
        /// Removes a returned item from the active rental count when return processing failed
        /// before a trustworthy timestamp was available.
        /// </summary>
        public void RecordRetiredReturn()
        {
            lock (_lock)
            {
                if (0 < _currentlyRented)
                {
                    _currentlyRented--;
                }
            }
        }

        /// <summary>
        /// Gets the rolling high-water mark (peak within the rolling window).
        /// </summary>
        /// <param name="currentTime">The current time.</param>
        /// <returns>The peak concurrent rentals within the rolling window.</returns>
        public int GetRollingHighWaterMark(float currentTime)
        {
            return _rollingHighWaterMark.GetPeak(currentTime);
        }

        /// <summary>
        /// Gets the rolling average of concurrent rentals.
        /// </summary>
        /// <param name="currentTime">The current time.</param>
        /// <returns>The average concurrent rentals within the rolling window.</returns>
        public float GetRollingAverage(float currentTime)
        {
            return _rollingHighWaterMark.GetAverage(currentTime);
        }

        /// <summary>
        /// Calculates the "comfortable" pool size based on usage patterns.
        /// </summary>
        /// <param name="currentTime">The current time.</param>
        /// <param name="effectiveMinRetainCount">The effective minimum retain count (already accounts for warm/min logic).</param>
        /// <returns>The comfortable pool size.</returns>
        public int GetComfortableSize(float currentTime, int effectiveMinRetainCount)
        {
            return GetComfortableSize(
                currentTime,
                effectiveMinRetainCount,
                MemoryPressureLevel.None
            );
        }

        /// <summary>
        /// Calculates the "comfortable" pool size based on usage patterns and memory pressure.
        /// </summary>
        /// <param name="currentTime">The current time.</param>
        /// <param name="effectiveMinRetainCount">The effective minimum retain count (already accounts for warm/min logic).</param>
        /// <param name="pressureLevel">The current memory pressure level.</param>
        /// <returns>The comfortable pool size, adjusted for memory pressure.</returns>
        /// <remarks>
        /// <para>
        /// Memory pressure affects the buffer multiplier used:
        /// <list type="bullet">
        ///   <item><description><see cref="MemoryPressureLevel.None"/>: Uses configured buffer multiplier</description></item>
        ///   <item><description><see cref="MemoryPressureLevel.Low"/>: Caps buffer at 1.5x</description></item>
        ///   <item><description><see cref="MemoryPressureLevel.Medium"/>: Caps buffer at 1.0x</description></item>
        ///   <item><description><see cref="MemoryPressureLevel.High"/> and above: Returns effective min retain count</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        public int GetComfortableSize(
            float currentTime,
            int effectiveMinRetainCount,
            MemoryPressureLevel pressureLevel
        )
        {
            if (MemoryPressureLevel.High <= pressureLevel)
            {
                return effectiveMinRetainCount;
            }

            int rollingPeak = _rollingHighWaterMark.GetPeak(currentTime);
            float buffer;
            float rentalsPerMin;
            float lastAccess;
            lock (_lock)
            {
                buffer = _bufferMultiplier;
                rentalsPerMin = _cachedRentalsPerMinute;
                lastAccess = _lastReturnTime < _lastRentalTime ? _lastRentalTime : _lastReturnTime;
            }

            if (HighFrequencyThreshold <= rentalsPerMin)
            {
                buffer *= HighFrequencyBufferBoost;
            }

            if (pressureLevel == MemoryPressureLevel.Medium)
            {
                buffer = MediumPressureBufferCap < buffer ? MediumPressureBufferCap : buffer;
            }
            else if (pressureLevel == MemoryPressureLevel.Low)
            {
                buffer = LowPressureBufferCap < buffer ? LowPressureBufferCap : buffer;
            }

            float unusedThresholdSeconds = UnusedPoolThresholdMinutes * SecondsPerMinute;
            if (0f < lastAccess && unusedThresholdSeconds <= (currentTime - lastAccess))
            {
                return effectiveMinRetainCount;
            }

            int bufferedSize = (int)(rollingPeak * buffer);
            return effectiveMinRetainCount < bufferedSize ? bufferedSize : effectiveMinRetainCount;
        }

        /// <summary>
        /// Computes all purge-related parameters in a single lock acquisition to reduce contention.
        /// This combines the logic of <see cref="GetFrequencyAdjustedIdleTimeout"/>,
        /// <see cref="GetEffectiveMinRetainCount(float,float,int,int,MemoryPressureLevel)"/>,
        /// <see cref="IsInHysteresisPeriod"/>, and <see cref="GetComfortableSize(float,int,MemoryPressureLevel)"/>.
        /// </summary>
        public PurgeParameters GetPurgeParameters(
            float currentTime,
            float baseIdleTimeoutSeconds,
            int minRetainCount,
            int warmRetainCount,
            bool useIntelligent,
            MemoryPressureLevel pressureLevel
        )
        {
            lock (_lock)
            {
                // Frequency-adjusted idle timeout
                float effectiveIdleTimeout = baseIdleTimeoutSeconds;
                if (
                    _cachedRentalsPerMinute <= LowFrequencyThreshold
                    && 0f < _cachedRentalsPerMinute
                    && 0 < _totalRentalCount
                )
                {
                    effectiveIdleTimeout = baseIdleTimeoutSeconds * LowFrequencyTimeoutMultiplier;
                }

                // Effective min retain count
                int effectiveMinRetain = minRetainCount;
                if (pressureLevel < MemoryPressureLevel.Medium)
                {
                    bool isActive =
                        0f < baseIdleTimeoutSeconds
                        && (currentTime - _lastRentalTime) < baseIdleTimeoutSeconds;
                    int warmFloor = isActive ? warmRetainCount : 0;
                    effectiveMinRetain = minRetainCount < warmFloor ? warmFloor : minRetainCount;
                }

                // Comfortable size
                int comfortableSize;
                if (!useIntelligent)
                {
                    comfortableSize = effectiveMinRetain;
                }
                else if (MemoryPressureLevel.High <= pressureLevel)
                {
                    comfortableSize = effectiveMinRetain;
                }
                else
                {
                    int rollingPeak = _rollingHighWaterMark.GetPeak(currentTime);
                    float buffer = _bufferMultiplier;

                    if (HighFrequencyThreshold <= _cachedRentalsPerMinute)
                    {
                        buffer *= HighFrequencyBufferBoost;
                    }

                    if (pressureLevel == MemoryPressureLevel.Medium)
                    {
                        buffer =
                            MediumPressureBufferCap < buffer ? MediumPressureBufferCap : buffer;
                    }
                    else if (pressureLevel == MemoryPressureLevel.Low)
                    {
                        buffer = LowPressureBufferCap < buffer ? LowPressureBufferCap : buffer;
                    }

                    float unusedThresholdSeconds = UnusedPoolThresholdMinutes * SecondsPerMinute;
                    float lastAccess =
                        _lastReturnTime < _lastRentalTime ? _lastRentalTime : _lastReturnTime;
                    if (0f < lastAccess && unusedThresholdSeconds <= (currentTime - lastAccess))
                    {
                        comfortableSize = effectiveMinRetain;
                    }
                    else
                    {
                        int bufferedSize = (int)(rollingPeak * buffer);
                        comfortableSize =
                            effectiveMinRetain < bufferedSize ? bufferedSize : effectiveMinRetain;
                    }
                }

                // Hysteresis check
                bool inHysteresis = false;
                if (useIntelligent)
                {
                    bool ignoreForPressure = MemoryPressureLevel.High <= pressureLevel;
                    if (
                        !ignoreForPressure
                        && 0f < _lastSpikeTime
                        && currentTime - _lastSpikeTime < _hysteresisSeconds
                    )
                    {
                        inHysteresis = true;
                    }
                }

                return new PurgeParameters(
                    effectiveIdleTimeout,
                    effectiveMinRetain,
                    comfortableSize,
                    inHysteresis
                );
            }
        }

        /// <summary>
        /// Calculates the effective minimum retain count based on whether the pool is active.
        /// Active pools use WarmRetainCount, idle pools use MinRetainCount.
        /// </summary>
        /// <param name="currentTime">The current time.</param>
        /// <param name="idleTimeoutSeconds">The idle timeout in seconds.</param>
        /// <param name="minRetainCount">The absolute floor (MinRetainCount).</param>
        /// <param name="warmRetainCount">The warm retain count for active pools.</param>
        /// <returns>The effective minimum retain count.</returns>
        public int GetEffectiveMinRetainCount(
            float currentTime,
            float idleTimeoutSeconds,
            int minRetainCount,
            int warmRetainCount
        )
        {
            return GetEffectiveMinRetainCount(
                currentTime,
                idleTimeoutSeconds,
                minRetainCount,
                warmRetainCount,
                MemoryPressureLevel.None
            );
        }

        /// <summary>
        /// Calculates the effective minimum retain count based on pool activity and memory pressure.
        /// </summary>
        /// <param name="currentTime">The current time.</param>
        /// <param name="idleTimeoutSeconds">The idle timeout in seconds.</param>
        /// <param name="minRetainCount">The absolute floor (MinRetainCount).</param>
        /// <param name="warmRetainCount">The warm retain count for active pools.</param>
        /// <param name="pressureLevel">The current memory pressure level.</param>
        /// <returns>The effective minimum retain count, adjusted for memory pressure.</returns>
        /// <remarks>
        /// <para>
        /// Memory pressure affects warm retain count:
        /// <list type="bullet">
        ///   <item><description><see cref="MemoryPressureLevel.None"/> and <see cref="MemoryPressureLevel.Low"/>: Uses full warm retain count for active pools</description></item>
        ///   <item><description><see cref="MemoryPressureLevel.Medium"/> and above: Ignores warm retain count, returns min retain count</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        public int GetEffectiveMinRetainCount(
            float currentTime,
            float idleTimeoutSeconds,
            int minRetainCount,
            int warmRetainCount,
            MemoryPressureLevel pressureLevel
        )
        {
            if (MemoryPressureLevel.Medium <= pressureLevel)
            {
                return minRetainCount;
            }

            float lastRental;
            lock (_lock)
            {
                lastRental = _lastRentalTime;
            }

            bool isActive =
                0f < idleTimeoutSeconds && (currentTime - lastRental) < idleTimeoutSeconds;
            int warmFloor = isActive ? warmRetainCount : 0;
            return minRetainCount < warmFloor ? warmFloor : minRetainCount;
        }

        /// <summary>
        /// Checks if purging should be suppressed due to recent spike activity.
        /// </summary>
        /// <param name="currentTime">The current time.</param>
        /// <returns><c>true</c> if purging should be suppressed; otherwise, <c>false</c>.</returns>
        public bool IsInHysteresisPeriod(float currentTime)
        {
            lock (_lock)
            {
                if (_lastSpikeTime <= 0f)
                {
                    return false;
                }

                return currentTime - _lastSpikeTime < _hysteresisSeconds;
            }
        }

        /// <summary>
        /// Checks if this pool is high-frequency (many rentals per minute).
        /// High-frequency pools benefit from larger buffers.
        /// </summary>
        /// <returns><c>true</c> if the pool is high-frequency; otherwise, <c>false</c>.</returns>
        public bool IsHighFrequency()
        {
            lock (_lock)
            {
                return HighFrequencyThreshold <= _cachedRentalsPerMinute;
            }
        }

        /// <summary>
        /// Checks if this pool is low-frequency (few rentals per minute).
        /// Low-frequency pools can be purged more aggressively.
        /// </summary>
        /// <returns><c>true</c> if the pool is low-frequency; otherwise, <c>false</c>.</returns>
        public bool IsLowFrequency()
        {
            lock (_lock)
            {
                return _cachedRentalsPerMinute <= LowFrequencyThreshold && 0 < _totalRentalCount;
            }
        }

        /// <summary>
        /// Checks if the pool has been unused for an extended period.
        /// </summary>
        /// <param name="currentTime">The current time.</param>
        /// <returns><c>true</c> if the pool is unused; otherwise, <c>false</c>.</returns>
        public bool IsUnused(float currentTime)
        {
            lock (_lock)
            {
                float lastAccess =
                    _lastReturnTime < _lastRentalTime ? _lastRentalTime : _lastReturnTime;
                if (lastAccess <= 0f)
                {
                    return false;
                }

                float unusedThresholdSeconds = UnusedPoolThresholdMinutes * SecondsPerMinute;
                return unusedThresholdSeconds <= (currentTime - lastAccess);
            }
        }

        /// <summary>
        /// Gets the effective buffer multiplier adjusted for frequency.
        /// High-frequency pools get a larger buffer, low-frequency pools get standard buffer.
        /// </summary>
        /// <returns>The adjusted buffer multiplier.</returns>
        public float GetFrequencyAdjustedBufferMultiplier()
        {
            lock (_lock)
            {
                if (HighFrequencyThreshold <= _cachedRentalsPerMinute)
                {
                    return _bufferMultiplier * HighFrequencyBufferBoost;
                }

                return _bufferMultiplier;
            }
        }

        /// <summary>
        /// Gets the effective idle timeout adjusted for frequency.
        /// Low-frequency pools have shorter effective timeout for faster purging.
        /// </summary>
        /// <param name="baseIdleTimeoutSeconds">The base idle timeout in seconds.</param>
        /// <returns>The adjusted idle timeout in seconds.</returns>
        public float GetFrequencyAdjustedIdleTimeout(float baseIdleTimeoutSeconds)
        {
            lock (_lock)
            {
                if (
                    _cachedRentalsPerMinute <= LowFrequencyThreshold
                    && 0f < _cachedRentalsPerMinute
                    && 0 < _totalRentalCount
                )
                {
                    return baseIdleTimeoutSeconds * LowFrequencyTimeoutMultiplier;
                }

                return baseIdleTimeoutSeconds;
            }
        }

        /// <summary>
        /// Gets a snapshot of the current frequency statistics.
        /// </summary>
        /// <param name="currentTime">The current time.</param>
        /// <returns>A snapshot of frequency metrics.</returns>
        public PoolFrequencyStatistics GetFrequencyStatistics(float currentTime)
        {
            lock (_lock)
            {
                UpdateFrequencyTrackingLocked(currentTime);

                float lastAccess =
                    _lastReturnTime < _lastRentalTime ? _lastRentalTime : _lastReturnTime;
                float averageInterRentalTime =
                    0 < _interRentalCount
                        ? (float)(_totalInterRentalTimeSeconds / _interRentalCount)
                        : 0f;

                return new PoolFrequencyStatistics(
                    rentalsPerMinute: _cachedRentalsPerMinute,
                    averageInterRentalTimeSeconds: averageInterRentalTime,
                    lastAccessTime: lastAccess,
                    totalRentalCount: _totalRentalCount,
                    isHighFrequency: HighFrequencyThreshold <= _cachedRentalsPerMinute,
                    isLowFrequency: _cachedRentalsPerMinute <= LowFrequencyThreshold
                        && 0 < _totalRentalCount,
                    isUnused: 0f < lastAccess
                        && UnusedPoolThresholdMinutes * SecondsPerMinute
                            <= (currentTime - lastAccess)
                );
            }
        }

        /// <summary>
        /// Clears all tracking data.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _currentlyRented = 0;
                _peakConcurrentRentals = 0;
                _lastRentalTime = 0f;
                _lastReturnTime = 0f;
                _lastSpikeTime = 0f;
                _rentalCountThisWindow = 0;
                _windowStartTime = 0f;
                _cachedRentalsPerMinute = 0f;
                _totalRentalCount = 0;
                _totalInterRentalTimeSeconds = 0;
                _interRentalCount = 0;
                _previousRentalTime = 0f;
                _rollingHighWaterMark.Clear();
            }
        }

        private void UpdateFrequencyTracking(float currentTime)
        {
            UpdateFrequencyTrackingLocked(currentTime, incrementRental: true);
        }

        /// <summary>
        /// Updates frequency tracking metrics. Must be called while holding the lock.
        /// </summary>
        /// <param name="currentTime">The current time.</param>
        /// <param name="incrementRental">
        /// When <c>true</c>, increments the rental count for this window (used by actual rental operations).
        /// When <c>false</c>, only updates time-based calculations without counting a rental
        /// (used by read-only statistics queries like <see cref="GetFrequencyStatistics"/>).
        /// </param>
        private void UpdateFrequencyTrackingLocked(float currentTime, bool incrementRental = false)
        {
            if (incrementRental)
            {
                _rentalCountThisWindow++;
            }

            if (_windowStartTime <= 0f)
            {
                _windowStartTime = currentTime;
            }

            float windowElapsed = currentTime - _windowStartTime;

            if (DefaultFrequencyWindowSeconds <= windowElapsed)
            {
                if (0f < windowElapsed)
                {
                    _cachedRentalsPerMinute =
                        _rentalCountThisWindow * (SecondsPerMinute / DefaultFrequencyWindowSeconds);
                }

                _rentalCountThisWindow = incrementRental ? 1 : 0;
                _windowStartTime = currentTime;
            }
            else if (0f < windowElapsed)
            {
                float estimatedMinuteRate =
                    _rentalCountThisWindow * (SecondsPerMinute / windowElapsed);
                _cachedRentalsPerMinute = estimatedMinuteRate;
            }
        }
    }

    /// <summary>
    /// Immutable snapshot of pool frequency statistics for debugging and monitoring.
    /// </summary>
    public readonly struct PoolFrequencyStatistics : IEquatable<PoolFrequencyStatistics>
    {
        /// <summary>
        /// Gets the current rentals-per-minute rate.
        /// </summary>
        public float RentalsPerMinute { get; }

        /// <summary>
        /// Gets the average time between consecutive rentals in seconds.
        /// This represents the inter-arrival time between rental operations, not the duration items are held.
        /// Returns 0 if fewer than two rentals have occurred.
        /// </summary>
        public float AverageInterRentalTimeSeconds { get; }

        /// <summary>
        /// Gets the time of the most recent access (rent or return).
        /// </summary>
        public float LastAccessTime { get; }

        /// <summary>
        /// Gets the total number of rentals since pool creation.
        /// </summary>
        public long TotalRentalCount { get; }

        /// <summary>
        /// Gets whether this pool is considered high-frequency.
        /// </summary>
        public bool IsHighFrequency { get; }

        /// <summary>
        /// Gets whether this pool is considered low-frequency.
        /// </summary>
        public bool IsLowFrequency { get; }

        /// <summary>
        /// Gets whether this pool is considered unused.
        /// </summary>
        public bool IsUnused { get; }

        /// <summary>
        /// Creates a new frequency statistics snapshot.
        /// </summary>
        public PoolFrequencyStatistics(
            float rentalsPerMinute,
            float averageInterRentalTimeSeconds,
            float lastAccessTime,
            long totalRentalCount,
            bool isHighFrequency,
            bool isLowFrequency,
            bool isUnused
        )
        {
            RentalsPerMinute = rentalsPerMinute;
            AverageInterRentalTimeSeconds = averageInterRentalTimeSeconds;
            LastAccessTime = lastAccessTime;
            TotalRentalCount = totalRentalCount;
            IsHighFrequency = isHighFrequency;
            IsLowFrequency = isLowFrequency;
            IsUnused = isUnused;
        }

        /// <summary>
        /// Determines whether this snapshot equals another. Every member, the three float rates
        /// included, is compared exactly, so every pair this reports equal also shares a hash code.
        /// </summary>
        /// <param name="other">The other snapshot to compare.</param>
        /// <returns><c>true</c> when every member matches exactly.</returns>
        public bool Equals(PoolFrequencyStatistics other)
        {
            return RentalsPerMinute.Equals(other.RentalsPerMinute)
                && AverageInterRentalTimeSeconds.Equals(other.AverageInterRentalTimeSeconds)
                && LastAccessTime.Equals(other.LastAccessTime)
                && TotalRentalCount == other.TotalRentalCount
                && IsHighFrequency == other.IsHighFrequency
                && IsLowFrequency == other.IsLowFrequency
                && IsUnused == other.IsUnused;
        }

        /// <summary>
        /// Determines whether this snapshot's three float rates each sit within
        /// <paramref name="tolerance"/> of another's, with every other member matching exactly.
        /// </summary>
        /// <param name="other">The other snapshot to compare.</param>
        /// <param name="tolerance">Maximum permitted difference per rate, and the whole of it: nothing relative to the magnitudes is added. Must be finite and non-negative.</param>
        /// <returns>
        /// <c>true</c> when the rates agree within <paramref name="tolerance"/> and the remaining
        /// members match; <c>false</c> when <paramref name="tolerance"/> is negative, infinite, or
        /// not a number. A non-finite rate compares exactly, so two identical infinite rates are
        /// approximately equal and this stays reflexive for every snapshot.
        /// </returns>
        public bool ApproximatelyEquals(PoolFrequencyStatistics other, float tolerance)
        {
            if (float.IsNaN(tolerance) || float.IsInfinity(tolerance) || tolerance < 0f)
            {
                return false;
            }

            return WallMath.WithinTolerance(RentalsPerMinute, other.RentalsPerMinute, tolerance)
                && WallMath.WithinTolerance(
                    AverageInterRentalTimeSeconds,
                    other.AverageInterRentalTimeSeconds,
                    tolerance
                )
                && WallMath.WithinTolerance(LastAccessTime, other.LastAccessTime, tolerance)
                && TotalRentalCount == other.TotalRentalCount
                && IsHighFrequency == other.IsHighFrequency
                && IsLowFrequency == other.IsLowFrequency
                && IsUnused == other.IsUnused;
        }

        /// <summary>
        /// Determines whether this snapshot equals another object. Only another
        /// <see cref="PoolFrequencyStatistics"/> can be equal to a snapshot.
        /// </summary>
        /// <param name="obj">The object to compare.</param>
        /// <returns><c>true</c> when <paramref name="obj"/> is an equal snapshot.</returns>
        public override bool Equals(object obj)
        {
            return obj is PoolFrequencyStatistics other && Equals(other);
        }

        /// <summary>
        /// Returns a hash derived from exactly the members <see cref="Equals(PoolFrequencyStatistics)"/>
        /// compares.
        /// </summary>
        /// <returns>A hash code for this snapshot.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            return Objects.HashCode(
                RentalsPerMinute,
                AverageInterRentalTimeSeconds,
                LastAccessTime,
                TotalRentalCount,
                IsHighFrequency,
                IsLowFrequency,
                IsUnused
            );
        }

        /// <summary>
        /// Determines whether two <see cref="PoolFrequencyStatistics"/> instances are equal.
        /// </summary>
        public static bool operator ==(PoolFrequencyStatistics left, PoolFrequencyStatistics right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two <see cref="PoolFrequencyStatistics"/> instances are not equal.
        /// </summary>
        public static bool operator !=(PoolFrequencyStatistics left, PoolFrequencyStatistics right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"PoolFrequencyStatistics(RentalsPerMin={RentalsPerMinute:F2}, "
                + $"AvgInterRentalTime={AverageInterRentalTimeSeconds:F3}s, "
                + $"LastAccess={LastAccessTime:F2}s, Total={TotalRentalCount}, "
                + $"High={IsHighFrequency}, Low={IsLowFrequency}, Unused={IsUnused})";
        }
    }
}
