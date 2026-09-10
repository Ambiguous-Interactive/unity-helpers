// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;

    /// <summary>
    /// Configuration options for <see cref="WallstopGenericPool{T}"/> auto-purging behavior.
    /// </summary>
    /// <typeparam name="T">The type of objects managed by the pool.</typeparam>
    /// <remarks>
    /// <para>
    /// Use these options to control memory usage by limiting pool size and purging idle items.
    /// All timing values use seconds as the unit.
    /// </para>
    /// <para>
    /// For intelligent purging that avoids GC churn, enable <see cref="UseIntelligentPurging"/>
    /// and configure the settings via <see cref="PoolPurgeSettings"/> or set properties directly.
    /// Intelligent purging tracks usage patterns and only purges items that are unlikely to be needed.
    /// </para>
    /// <example>
    /// <code><![CDATA[
    /// PoolOptions<MyObject> options = new()
    /// {
    ///     MaxPoolSize = 100,
    ///     IdleTimeoutSeconds = 60f,
    ///     Triggers = PurgeTrigger.OnRent | PurgeTrigger.OnReturn,
    ///     OnPurge = (item, reason) => Debug.Log($"Purged: {reason}")
    /// };
    ///
    /// // Enable intelligent purging
    /// PoolOptions<MyObject> smartOptions = new()
    /// {
    ///     UseIntelligentPurging = true,
    ///     IdleTimeoutSeconds = 300f, // 5 minutes
    ///     BufferMultiplier = 1.5f,
    ///     RollingWindowSeconds = 300f,
    ///     HysteresisSeconds = 60f
    /// };
    /// ]]></code>
    /// </example>
    /// </remarks>
    public sealed class PoolOptions<T>
    {
        /// <summary>
        /// Default value for <see cref="MaxPoolSize"/> (0 = unbounded).
        /// </summary>
        public const int DefaultMaxPoolSize = 0;

        /// <summary>
        /// Default value for <see cref="MinRetainCount"/>.
        /// </summary>
        public const int DefaultMinRetainCount = 0;

        /// <summary>
        /// Default value for <see cref="WarmRetainCount"/>.
        /// </summary>
        public const int DefaultWarmRetainCount = 2;

        /// <summary>
        /// Default value for <see cref="IdleTimeoutSeconds"/> (0 = disabled).
        /// </summary>
        public const float DefaultIdleTimeoutSeconds = 0f;

        /// <summary>
        /// Default value for <see cref="PurgeIntervalSeconds"/>.
        /// </summary>
        public const float DefaultPurgeIntervalSeconds = 60f;

        /// <summary>
        /// Default value for <see cref="BufferMultiplier"/>.
        /// </summary>
        public const float DefaultBufferMultiplier = 2.0f;

        /// <summary>
        /// Default value for <see cref="RollingWindowSeconds"/>.
        /// </summary>
        public const float DefaultRollingWindowSeconds = 300f;

        /// <summary>
        /// Default value for <see cref="HysteresisSeconds"/>.
        /// </summary>
        public const float DefaultHysteresisSeconds = 120f;

        /// <summary>
        /// Default value for <see cref="SpikeThresholdMultiplier"/>.
        /// </summary>
        public const float DefaultSpikeThresholdMultiplier = 2.5f;

        /// <summary>
        /// Default value for <see cref="MaxPurgesPerOperation"/>.
        /// </summary>
        public const int DefaultMaxPurgesPerOperation = 10;

        /// <summary>
        /// Maximum number of items to retain in the pool.
        /// Items exceeding this limit will be purged.
        /// A value of 0 or null means unbounded (no size limit).
        /// </summary>
        public int? MaxPoolSize { get; set; }

        /// <summary>
        /// Minimum number of items to always retain in the pool during purge operations.
        /// This is the absolute floor - purge will never reduce the pool below this count.
        /// Default is 0 (no minimum).
        /// </summary>
        public int MinRetainCount { get; set; } = DefaultMinRetainCount;

        /// <summary>
        /// Warm retain count for active pools.
        /// Active pools (accessed within <see cref="IdleTimeoutSeconds"/>) keep this many items warm
        /// to avoid cold-start allocations. Idle pools purge to <see cref="MinRetainCount"/>.
        /// Effective floor = <c>max(MinRetainCount, isActive ? WarmRetainCount : 0)</c>.
        /// Default is 2.
        /// </summary>
        public int? WarmRetainCount { get; set; }

        /// <summary>
        /// Time in seconds after which an idle item becomes eligible for purging.
        /// A value of 0 or null disables idle timeout purging.
        /// Items are tracked from the time they are returned to the pool.
        /// </summary>
        public float? IdleTimeoutSeconds { get; set; }

        /// <summary>
        /// Interval in seconds between periodic purge checks when <see cref="PurgeTrigger.Periodic"/> is enabled.
        /// Only used when <see cref="Triggers"/> includes <see cref="PurgeTrigger.Periodic"/>.
        /// Default is 60 seconds.
        /// </summary>
        public float? PurgeIntervalSeconds { get; set; }

        /// <summary>
        /// Specifies when automatic purge operations should be triggered.
        /// Default is <see cref="PurgeTrigger.Periodic"/> for time-based cleanup that avoids
        /// per-operation overhead in hot paths.
        /// </summary>
        public PurgeTrigger Triggers { get; set; } = PurgeTrigger.Periodic;

        /// <summary>
        /// Optional callback invoked when an item is purged from the pool.
        /// The callback receives the purged item and the reason for purging.
        /// Use this for cleanup, logging, or resource disposal.
        /// </summary>
        /// <remarks>
        /// Global budget enforcement invokes this callback after releasing the registry lock.
        /// Exceptions thrown by this callback are swallowed to prevent pool corruption.
        /// </remarks>
        public Action<T, PurgeReason> OnPurge { get; set; }

        /// <summary>
        /// Optional function to provide the current time for idle tracking.
        /// If null, uses a Stopwatch-based provider (safe during static initialization).
        /// Useful for testing or custom time sources.
        /// </summary>
        public Func<float> TimeProvider { get; set; }

        /// <summary>
        /// When true, enables intelligent purging that tracks usage patterns to avoid GC churn.
        /// If null, consults <see cref="PoolPurgeSettings"/> for the effective setting.
        /// Default is null (uses global settings, which default to disabled).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Intelligent purging provides the following protections against GC churn:
        /// <list type="bullet">
        ///   <item><description>Tracks rolling high-water mark of concurrent rentals</description></item>
        ///   <item><description>Only purges items that are BOTH idle for <see cref="IdleTimeoutSeconds"/> AND would leave pool above "comfortable size"</description></item>
        ///   <item><description>Applies hysteresis after usage spikes to prevent purge-allocate cycles</description></item>
        ///   <item><description>Never purges if it would bring pool below typical usage level</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        public bool? UseIntelligentPurging { get; set; }

        /// <summary>
        /// Buffer multiplier for calculating "comfortable" pool size.
        /// The comfortable size is <c>max(MinRetainCount, rollingHighWaterMark * BufferMultiplier)</c>.
        /// Default is 1.5 (50% buffer above peak usage).
        /// </summary>
        public float? BufferMultiplier { get; set; }

        /// <summary>
        /// Duration in seconds for the rolling window used to track high-water mark.
        /// Only the peak concurrent rentals within this window are considered.
        /// Default is 300 seconds (5 minutes).
        /// </summary>
        public float? RollingWindowSeconds { get; set; }

        /// <summary>
        /// Hysteresis duration in seconds after a usage spike.
        /// Purging is suppressed for this duration after a spike to prevent purge-allocate cycles.
        /// Default is 60 seconds.
        /// </summary>
        public float? HysteresisSeconds { get; set; }

        /// <summary>
        /// Multiplier to determine what constitutes a "spike" in usage.
        /// A spike is detected when concurrent rentals exceed the rolling average by this factor.
        /// Default is 2.0 (spike is 2x the average).
        /// </summary>
        public float? SpikeThresholdMultiplier { get; set; }

        /// <summary>
        /// Maximum number of items to purge per operation.
        /// Limits GC pressure by spreading large purge operations across multiple calls.
        /// A value of 0 means unlimited (purge all eligible items in one operation).
        /// If null, uses global default from <see cref="PoolPurgeSettings.DefaultGlobalMaxPurgesPerOperation"/>.
        /// Default is null (uses global setting, which defaults to 10).
        /// </summary>
        /// <remarks>
        /// <para>
        /// When set to a positive value, purge operations will process at most this many items
        /// before returning, setting a "pending purges" flag to continue on subsequent operations.
        /// This prevents GC spikes from bulk deallocation.
        /// </para>
        /// <para>
        /// Emergency purges (e.g., <see cref="PurgeReason.MemoryPressure"/>) bypass this limit
        /// to ensure memory is freed immediately when the system is under memory pressure.
        /// </para>
        /// </remarks>
        public int? MaxPurgesPerOperation { get; set; }
    }
}
