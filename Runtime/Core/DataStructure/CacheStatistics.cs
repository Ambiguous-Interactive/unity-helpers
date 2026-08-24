// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Runtime.CompilerServices;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// Immutable snapshot of cache performance statistics.
    /// </summary>
    /// <remarks>
    /// Statistics are only recorded when <see cref="CacheOptions{TKey,TValue}.RecordStatistics"/> is enabled.
    /// Use <see cref="Cache{TKey,TValue}.GetStatistics"/> to retrieve the current snapshot.
    /// </remarks>
    public readonly struct CacheStatistics : IEquatable<CacheStatistics>
    {
        /// <summary>
        /// The number of times a requested key was found in the cache.
        /// </summary>
        public long HitCount { get; }

        /// <summary>
        /// The number of times a requested key was not found in the cache.
        /// </summary>
        public long MissCount { get; }

        /// <summary>
        /// The total number of entries evicted from the cache (for any reason).
        /// </summary>
        public long EvictionCount { get; }

        /// <summary>
        /// The number of times the cache loaded a value using the factory function.
        /// </summary>
        public long LoadCount { get; }

        /// <summary>
        /// The number of entries that were evicted due to TTL expiration.
        /// </summary>
        public long ExpiredCount { get; }

        /// <summary>
        /// The current number of entries in the cache.
        /// </summary>
        public int CurrentSize { get; }

        /// <summary>
        /// The maximum number of entries the cache has held at any point.
        /// </summary>
        public int PeakSize { get; }

        /// <summary>
        /// The number of times the cache has grown due to thrash detection.
        /// </summary>
        public int GrowthEvents { get; }

        /// <summary>
        /// The cache hit rate as a value between 0.0 and 1.0.
        /// Returns 0.0 if no requests have been made.
        /// </summary>
        public double HitRate
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                long total = HitCount + MissCount;
                return 0 < total ? HitCount / (double)total : 0.0;
            }
        }

        /// <summary>
        /// The cache miss rate as a value between 0.0 and 1.0.
        /// Returns 0.0 if no requests have been made.
        /// </summary>
        public double MissRate
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => 1.0 - HitRate;
        }

        /// <summary>
        /// Creates a new statistics snapshot.
        /// </summary>
        public CacheStatistics(
            long hitCount,
            long missCount,
            long evictionCount,
            long loadCount,
            long expiredCount,
            int currentSize,
            int peakSize,
            int growthEvents
        )
        {
            HitCount = hitCount;
            MissCount = missCount;
            EvictionCount = evictionCount;
            LoadCount = loadCount;
            ExpiredCount = expiredCount;
            CurrentSize = currentSize;
            PeakSize = peakSize;
            GrowthEvents = growthEvents;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(CacheStatistics other)
        {
            return HitCount == other.HitCount
                && MissCount == other.MissCount
                && EvictionCount == other.EvictionCount
                && LoadCount == other.LoadCount
                && ExpiredCount == other.ExpiredCount
                && CurrentSize == other.CurrentSize
                && PeakSize == other.PeakSize
                && GrowthEvents == other.GrowthEvents;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is CacheStatistics other && Equals(other);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            return Objects.HashCode(
                HitCount,
                MissCount,
                EvictionCount,
                LoadCount,
                ExpiredCount,
                CurrentSize,
                PeakSize,
                GrowthEvents
            );
        }

        /// <summary>
        /// Equality operator.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(CacheStatistics left, CacheStatistics right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(CacheStatistics left, CacheStatistics right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"CacheStatistics(Hits={HitCount}, Misses={MissCount}, Evictions={EvictionCount}, "
                + $"Size={CurrentSize}, Peak={PeakSize}, HitRate={HitRate:P2})";
        }
    }
}
