// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Fluent builder for constructing <see cref="Cache{TKey,TValue}"/> instances.
    /// This is a value type to avoid allocations during builder construction.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// Cache<string, UserData> cache = CacheBuilder<string, UserData>.NewBuilder()
    ///     .MaximumSize(1000)
    ///     .ExpireAfterWrite(TimeSpan.FromMinutes(5))
    ///     .EvictionPolicy(EvictionPolicy.Lru)
    ///     .RecordStatistics()
    ///     .Build();
    /// ]]></code>
    /// </example>
    /// <typeparam name="TKey">The type of keys in the cache.</typeparam>
    /// <typeparam name="TValue">The type of values in the cache.</typeparam>
    public struct CacheBuilder<TKey, TValue>
    {
        private int _maximumSize;
        private int _initialCapacity;
        private IEqualityComparer<TKey> _keyComparer;
        private long _maximumWeight;
        private Func<TKey, TValue, long> _weigher;
        private float _expireAfterWriteSeconds;
        private float _expireAfterAccessSeconds;
        private Func<TKey, TValue, float> _expireAfter;
        private bool _useJitter;
        private float _jitterMaxSeconds;
        private EvictionPolicy _policy;
        private float _protectedRatio;
        private bool _allowGrowth;
        private float _growthFactor;
        private int _maxGrowthSize;
        private float _thrashThresholdEvictionsPerSecond;
        private Action<TKey, TValue, EvictionReason> _onEviction;
        private bool _transferOwnershipOnRemoval;
        private Action<TKey, TValue> _onGet;
        private Action<TKey, TValue> _onSet;
        private bool _recordStatistics;
        private Func<float> _timeProvider;
        private Func<TKey, TValue> _loader;
        private bool _initialized;

        /// <summary>
        /// Creates a new cache builder with default settings.
        /// </summary>
        /// <returns>A new <see cref="CacheBuilder{TKey,TValue}"/> instance.</returns>
        public static CacheBuilder<TKey, TValue> NewBuilder()
        {
            CacheBuilder<TKey, TValue> builder = default;
            builder._maximumSize = CacheOptions<TKey, TValue>.DefaultMaximumSize;
            builder._expireAfterWriteSeconds = CacheOptions<
                TKey,
                TValue
            >.DefaultExpireAfterWriteSeconds;
            builder._expireAfterAccessSeconds = CacheOptions<
                TKey,
                TValue
            >.DefaultExpireAfterAccessSeconds;
            builder._policy = CacheOptions<TKey, TValue>.DefaultEvictionPolicy;
            builder._protectedRatio = CacheOptions<TKey, TValue>.DefaultProtectedRatio;
            builder._growthFactor = CacheOptions<TKey, TValue>.DefaultGrowthFactor;
            builder._thrashThresholdEvictionsPerSecond = CacheOptions<
                TKey,
                TValue
            >.DefaultThrashThreshold;
            builder._initialized = true;
            return builder;
        }

        /// <summary>
        /// Sets the maximum number of entries the cache can hold.
        /// </summary>
        /// <param name="size">The maximum entry count. Must be positive.</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> MaximumSize(int size)
        {
            EnsureInitialized();
            if (size <= 0)
            {
                size = 1;
            }
            _maximumSize = size;
            return this;
        }

        /// <summary>
        /// Removes the entry-count bound while retaining dynamic internal allocation.
        /// </summary>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> Unbounded()
        {
            EnsureInitialized();
            _maximumSize = CacheOptions<TKey, TValue>.UnboundedMaximumSize;
            if (_initialCapacity <= 0)
            {
                _initialCapacity = CacheOptions<TKey, TValue>.DefaultInitialCapacity;
            }
            return this;
        }

        /// <summary>
        /// Sets the comparer used to match cache keys.
        /// </summary>
        /// <param name="comparer">The comparer, or null to use the default comparer.</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> KeyComparer(IEqualityComparer<TKey> comparer)
        {
            EnsureInitialized();
            _keyComparer = comparer;
            return this;
        }

        /// <summary>
        /// Sets the initial capacity of the cache's internal data structures.
        /// The cache will grow dynamically as needed up to <see cref="MaximumSize"/>.
        /// </summary>
        /// <remarks>
        /// This is useful when you know the approximate number of entries the cache will hold
        /// initially. Setting this appropriately can reduce memory allocations during growth.
        /// If not specified, the cache defaults to using <see cref="MaximumSize"/> as initial capacity.
        /// If an invalid value (zero or negative) is passed, falls back to
        /// <see cref="CacheOptions{TKey,TValue}.DefaultInitialCapacity"/>.
        /// Values are clamped to prevent excessive initial allocations.
        /// </remarks>
        /// <param name="capacity">The initial capacity. Must be positive.</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> InitialCapacity(int capacity)
        {
            EnsureInitialized();
            if (capacity <= 0)
            {
                capacity = CacheOptions<TKey, TValue>.DefaultInitialCapacity;
            }
            _initialCapacity = capacity;
            return this;
        }

        /// <summary>
        /// Sets the maximum total weight of all cache entries.
        /// Requires a weigher to be specified.
        /// </summary>
        /// <param name="weight">The maximum total weight. Must be positive.</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> MaximumWeight(long weight)
        {
            EnsureInitialized();
            if (weight <= 0)
            {
                weight = 1;
            }
            _maximumWeight = weight;
            return this;
        }

        /// <summary>
        /// Sets the function used to compute entry weights.
        /// When specified, <see cref="MaximumWeight"/> is used instead of <see cref="MaximumSize"/>.
        /// </summary>
        /// <param name="weigher">Function that computes the weight of an entry.</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> Weigher(Func<TKey, TValue, long> weigher)
        {
            EnsureInitialized();
            _weigher = weigher;
            return this;
        }

        /// <summary>
        /// Sets the time after which entries expire following creation or update.
        /// </summary>
        /// <param name="seconds">Expiration time in seconds. Non-positive values disable expiration.</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> ExpireAfterWrite(float seconds)
        {
            EnsureInitialized();
            _expireAfterWriteSeconds = seconds;
            return this;
        }

        /// <summary>
        /// Sets the time after which entries expire following creation or update.
        /// </summary>
        /// <param name="duration">Expiration duration.</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> ExpireAfterWrite(TimeSpan duration)
        {
            EnsureInitialized();
            _expireAfterWriteSeconds = (float)duration.TotalSeconds;
            return this;
        }

        /// <summary>
        /// Sets the time after which entries expire following last access (sliding window).
        /// </summary>
        /// <param name="seconds">Expiration time in seconds. Non-positive values disable expiration.</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> ExpireAfterAccess(float seconds)
        {
            EnsureInitialized();
            _expireAfterAccessSeconds = seconds;
            return this;
        }

        /// <summary>
        /// Sets the time after which entries expire following last access (sliding window).
        /// </summary>
        /// <param name="duration">Expiration duration.</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> ExpireAfterAccess(TimeSpan duration)
        {
            EnsureInitialized();
            _expireAfterAccessSeconds = (float)duration.TotalSeconds;
            return this;
        }

        /// <summary>
        /// Sets a function to compute custom per-entry expiration times.
        /// </summary>
        /// <param name="expireAfter">Function that returns expiration time in seconds for an entry.</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> ExpireAfter(Func<TKey, TValue, float> expireAfter)
        {
            EnsureInitialized();
            _expireAfter = expireAfter;
            return this;
        }

        /// <summary>
        /// Enables jitter on expiration times to prevent thundering herd.
        /// </summary>
        /// <param name="maxJitterSeconds">Maximum jitter in seconds. If null, defaults to 10% of TTL.</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> WithJitter(float maxJitterSeconds = 0f)
        {
            EnsureInitialized();
            _useJitter = true;
            if (0f < maxJitterSeconds)
            {
                _jitterMaxSeconds = maxJitterSeconds;
            }
            return this;
        }

        /// <summary>
        /// Sets the eviction policy used when the cache reaches capacity.
        /// </summary>
        /// <param name="policy">The eviction algorithm to use.</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> EvictionPolicy(EvictionPolicy policy)
        {
            EnsureInitialized();
            _policy = policy;
            return this;
        }

        /// <summary>
        /// Sets the protected segment ratio for SLRU eviction policy.
        /// </summary>
        /// <param name="ratio">Value between 0 and 1. Default is 0.8 (80% protected).</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> ProtectedRatio(float ratio)
        {
            EnsureInitialized();
            if (ratio < 0f)
            {
                ratio = 0f;
            }
            else if (1f < ratio)
            {
                ratio = 1f;
            }
            _protectedRatio = ratio;
            return this;
        }

        /// <summary>
        /// Enables dynamic cache growth when thrashing is detected.
        /// Pass factor <= 1 and maxSize = 0 to disable growth.
        /// </summary>
        /// <param name="factor">Growth factor. Default is 1.5x. Values <= 1 disable growth.</param>
        /// <param name="maxSize">Maximum size after growth. 0 means unbounded (if enabled).</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> AllowGrowth(float factor = 1.5f, int maxSize = 0)
        {
            EnsureInitialized();
            // If factor <= 1 and maxSize is 0, disable growth
            if (factor <= 1f && maxSize == 0)
            {
                _allowGrowth = false;
                return this;
            }
            _allowGrowth = true;
            if (1f < factor)
            {
                _growthFactor = factor;
            }
            if (0 < maxSize)
            {
                _maxGrowthSize = maxSize;
            }
            return this;
        }

        /// <summary>
        /// Sets the evictions per second threshold for thrash detection.
        /// </summary>
        /// <param name="evictionsPerSecond">Threshold value.</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> ThrashThreshold(float evictionsPerSecond)
        {
            EnsureInitialized();
            if (0f < evictionsPerSecond)
            {
                _thrashThresholdEvictionsPerSecond = evictionsPerSecond;
            }
            return this;
        }

        /// <summary>
        /// Sets the callback invoked when entries are evicted.
        /// </summary>
        /// <param name="listener">Callback receiving the key, value, and eviction reason.</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> OnEviction(Action<TKey, TValue, EvictionReason> listener)
        {
            EnsureInitialized();
            _onEviction = listener;
            return this;
        }

        /// <summary>
        /// Configures explicit removal to transfer ownership without invoking the eviction callback.
        /// </summary>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> TransferOwnershipOnRemoval()
        {
            EnsureInitialized();
            _transferOwnershipOnRemoval = true;
            return this;
        }

        /// <summary>
        /// Sets the callback invoked when entries are retrieved.
        /// </summary>
        /// <param name="listener">Callback receiving the key and value.</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> OnGet(Action<TKey, TValue> listener)
        {
            EnsureInitialized();
            _onGet = listener;
            return this;
        }

        /// <summary>
        /// Sets the callback invoked when entries are added or updated.
        /// </summary>
        /// <param name="listener">Callback receiving the key and value.</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> OnSet(Action<TKey, TValue> listener)
        {
            EnsureInitialized();
            _onSet = listener;
            return this;
        }

        /// <summary>
        /// Enables statistics recording for the cache.
        /// </summary>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> RecordStatistics()
        {
            EnsureInitialized();
            _recordStatistics = true;
            return this;
        }

        /// <summary>
        /// Sets a custom time provider for the cache.
        /// </summary>
        /// <param name="provider">Function returning the current time in seconds.</param>
        /// <returns>This builder for chaining.</returns>
        public CacheBuilder<TKey, TValue> TimeProvider(Func<float> provider)
        {
            EnsureInitialized();
            _timeProvider = provider;
            return this;
        }

        /// <summary>
        /// Builds the cache with the configured options.
        /// </summary>
        /// <returns>A new <see cref="Cache{TKey,TValue}"/> instance.</returns>
        public Cache<TKey, TValue> Build()
        {
            EnsureInitialized();
            return new Cache<TKey, TValue>(CreateOptions());
        }

        /// <summary>
        /// Builds a loading cache with the configured options.
        /// </summary>
        /// <param name="loader">Factory function to compute values for missing keys.</param>
        /// <returns>A new <see cref="Cache{TKey,TValue}"/> instance with automatic loading.</returns>
        public Cache<TKey, TValue> Build(Func<TKey, TValue> loader)
        {
            EnsureInitialized();
            _loader = loader;
            return new Cache<TKey, TValue>(CreateOptions());
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _maximumSize = CacheOptions<TKey, TValue>.DefaultMaximumSize;
            _expireAfterWriteSeconds = CacheOptions<TKey, TValue>.DefaultExpireAfterWriteSeconds;
            _expireAfterAccessSeconds = CacheOptions<TKey, TValue>.DefaultExpireAfterAccessSeconds;
            _policy = CacheOptions<TKey, TValue>.DefaultEvictionPolicy;
            _protectedRatio = CacheOptions<TKey, TValue>.DefaultProtectedRatio;
            _growthFactor = CacheOptions<TKey, TValue>.DefaultGrowthFactor;
            _thrashThresholdEvictionsPerSecond = CacheOptions<TKey, TValue>.DefaultThrashThreshold;
            _initialized = true;
        }

        private CacheOptions<TKey, TValue> CreateOptions()
        {
            return new CacheOptions<TKey, TValue>
            {
                MaximumSize = _maximumSize,
                InitialCapacity = _initialCapacity,
                KeyComparer = _keyComparer,
                MaximumWeight = _maximumWeight,
                Weigher = _weigher,
                ExpireAfterWriteSeconds = _expireAfterWriteSeconds,
                ExpireAfterAccessSeconds = _expireAfterAccessSeconds,
                ExpireAfter = _expireAfter,
                UseJitter = _useJitter,
                JitterMaxSeconds = _jitterMaxSeconds,
                Policy = _policy,
                ProtectedRatio = _protectedRatio,
                AllowGrowth = _allowGrowth,
                GrowthFactor = _growthFactor,
                MaxGrowthSize = _maxGrowthSize,
                ThrashThresholdEvictionsPerSecond = _thrashThresholdEvictionsPerSecond,
                OnEviction = _onEviction,
                TransferOwnershipOnRemoval = _transferOwnershipOnRemoval,
                OnGet = _onGet,
                OnSet = _onSet,
                RecordStatistics = _recordStatistics,
                TimeProvider = _timeProvider,
                Loader = _loader,
            };
        }
    }
}
