// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Runtime.CompilerServices;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// Immutable snapshot of pool performance statistics.
    /// </summary>
    /// <remarks>
    /// Use <see cref="WallstopGenericPool{T}.GetStatistics"/> to retrieve the current snapshot.
    /// Statistics are always recorded regardless of pool configuration.
    /// </remarks>
    public readonly struct PoolStatistics : IEquatable<PoolStatistics>
    {
        /// <summary>
        /// Tolerance for floating-point equality comparisons.
        /// </summary>
        private const float FloatEqualityTolerance = 0.001f;

        /// <summary>
        /// The current number of items in the pool.
        /// </summary>
        public int CurrentSize { get; }

        /// <summary>
        /// The maximum number of items the pool has held at any point.
        /// </summary>
        public int PeakSize { get; }

        /// <summary>
        /// The total number of times items have been rented from the pool.
        /// </summary>
        public long RentCount { get; }

        /// <summary>
        /// The total number of times items have been returned to the pool.
        /// </summary>
        public long ReturnCount { get; }

        /// <summary>
        /// The total number of items purged from the pool for any reason.
        /// </summary>
        public long PurgeCount { get; }

        /// <summary>
        /// The number of items purged due to idle timeout expiration.
        /// </summary>
        public long IdleTimeoutPurges { get; }

        /// <summary>
        /// The number of items purged due to pool capacity being exceeded.
        /// </summary>
        public long CapacityPurges { get; }

        /// <summary>
        /// The number of purge operations that completed fully (purged all eligible items).
        /// </summary>
        public long FullPurgeOperations { get; }

        /// <summary>
        /// The number of purge operations that were partial (hit <c>MaxPurgesPerOperation</c> limit).
        /// Partial purges continue on subsequent Rent/Return/Periodic operations.
        /// </summary>
        public long PartialPurgeOperations { get; }

        /// <summary>
        /// The current rentals-per-minute rate based on the rolling frequency window.
        /// Used for intelligent purge decisions - high-frequency pools keep larger buffers.
        /// </summary>
        public float RentalsPerMinute { get; }

        /// <summary>
        /// The average time between consecutive rentals in seconds.
        /// This represents the inter-arrival time between rental operations, not the duration items are held.
        /// Returns 0 if fewer than two rentals have occurred.
        /// </summary>
        public float AverageInterRentalTimeSeconds { get; }

        /// <summary>
        /// The time of the most recent access (rent or return).
        /// </summary>
        public float LastAccessTime { get; }

        /// <summary>
        /// Whether this pool is considered high-frequency (10+ rentals/minute).
        /// High-frequency pools benefit from larger buffers to avoid GC churn.
        /// </summary>
        public bool IsHighFrequency { get; }

        /// <summary>
        /// Whether this pool is considered low-frequency (at most 1 rental/minute).
        /// Low-frequency pools can be purged more aggressively.
        /// </summary>
        public bool IsLowFrequency { get; }

        /// <summary>
        /// Whether this pool is considered unused (no access in 5+ minutes).
        /// Unused pools are candidates for aggressive purging.
        /// </summary>
        public bool IsUnused { get; }

        /// <summary>
        /// Creates a new statistics snapshot.
        /// </summary>
        /// <param name="currentSize">Current number of items in the pool.</param>
        /// <param name="peakSize">Maximum pool size reached.</param>
        /// <param name="rentCount">Total rent operations.</param>
        /// <param name="returnCount">Total return operations.</param>
        /// <param name="purgeCount">Total purge operations.</param>
        /// <param name="idleTimeoutPurges">Purges due to idle timeout.</param>
        /// <param name="capacityPurges">Purges due to capacity limits.</param>
        /// <param name="fullPurgeOperations">Purge operations that completed fully.</param>
        /// <param name="partialPurgeOperations">Purge operations that were partial (hit max limit).</param>
        /// <param name="rentalsPerMinute">Current rentals-per-minute rate.</param>
        /// <param name="averageInterRentalTimeSeconds">Average time between consecutive rentals in seconds.</param>
        /// <param name="lastAccessTime">Time of most recent access.</param>
        /// <param name="isHighFrequency">Whether this is a high-frequency pool.</param>
        /// <param name="isLowFrequency">Whether this is a low-frequency pool.</param>
        /// <param name="isUnused">Whether this pool is unused.</param>
        public PoolStatistics(
            int currentSize,
            int peakSize,
            long rentCount,
            long returnCount,
            long purgeCount,
            long idleTimeoutPurges,
            long capacityPurges,
            long fullPurgeOperations = 0,
            long partialPurgeOperations = 0,
            float rentalsPerMinute = 0f,
            float averageInterRentalTimeSeconds = 0f,
            float lastAccessTime = 0f,
            bool isHighFrequency = false,
            bool isLowFrequency = false,
            bool isUnused = false
        )
        {
            CurrentSize = currentSize;
            PeakSize = peakSize;
            RentCount = rentCount;
            ReturnCount = returnCount;
            PurgeCount = purgeCount;
            IdleTimeoutPurges = idleTimeoutPurges;
            CapacityPurges = capacityPurges;
            FullPurgeOperations = fullPurgeOperations;
            PartialPurgeOperations = partialPurgeOperations;
            RentalsPerMinute = rentalsPerMinute;
            AverageInterRentalTimeSeconds = averageInterRentalTimeSeconds;
            LastAccessTime = lastAccessTime;
            IsHighFrequency = isHighFrequency;
            IsLowFrequency = isLowFrequency;
            IsUnused = isUnused;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(PoolStatistics other)
        {
            return CurrentSize == other.CurrentSize
                && PeakSize == other.PeakSize
                && RentCount == other.RentCount
                && ReturnCount == other.ReturnCount
                && PurgeCount == other.PurgeCount
                && IdleTimeoutPurges == other.IdleTimeoutPurges
                && CapacityPurges == other.CapacityPurges
                && FullPurgeOperations == other.FullPurgeOperations
                && PartialPurgeOperations == other.PartialPurgeOperations
                && Math.Abs(RentalsPerMinute - other.RentalsPerMinute) < FloatEqualityTolerance
                && Math.Abs(AverageInterRentalTimeSeconds - other.AverageInterRentalTimeSeconds)
                    < FloatEqualityTolerance
                && Math.Abs(LastAccessTime - other.LastAccessTime) < FloatEqualityTolerance
                && IsHighFrequency == other.IsHighFrequency
                && IsLowFrequency == other.IsLowFrequency
                && IsUnused == other.IsUnused;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is PoolStatistics other && Equals(other);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            /*
                Deliberately excludes RentalsPerMinute, AverageInterRentalTimeSeconds and
                LastAccessTime. Equals compares those three within FloatEqualityTolerance, so two
                snapshots that are equal can hold different float bits; hashing them would let equal
                values produce different hash codes and land in different buckets. Only members
                equality compares exactly may contribute.
            */
            return Objects.HashCode(
                CurrentSize,
                PeakSize,
                RentCount,
                ReturnCount,
                PurgeCount,
                IdleTimeoutPurges,
                CapacityPurges,
                FullPurgeOperations,
                PartialPurgeOperations,
                IsHighFrequency,
                IsLowFrequency,
                IsUnused
            );
        }

        /// <summary>
        /// Equality operator.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(PoolStatistics left, PoolStatistics right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(PoolStatistics left, PoolStatistics right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"PoolStatistics(CurrentSize={CurrentSize}, Peak={PeakSize}, Rents={RentCount}, "
                + $"Returns={ReturnCount}, Purges={PurgeCount}, IdleTimeout={IdleTimeoutPurges}, "
                + $"Capacity={CapacityPurges}, FullPurgeOps={FullPurgeOperations}, PartialPurgeOps={PartialPurgeOperations}, "
                + $"RentalsPerMin={RentalsPerMinute:F2}, AvgInterRentalTime={AverageInterRentalTimeSeconds:F3}s, "
                + $"LastAccess={LastAccessTime:F2}s, High={IsHighFrequency}, Low={IsLowFrequency}, Unused={IsUnused})";
        }
    }
}
