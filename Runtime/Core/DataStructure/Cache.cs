// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Threading;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Random;
#if !SINGLE_THREADED
    using System.Collections.Concurrent;
#endif

    /// <summary>
    /// A high-performance, configurable cache with multiple eviction policies,
    /// time-based expiration, dynamic sizing, and comprehensive callbacks.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// // Simple LRU cache
    /// Cache<string, UserData> cache = CacheBuilder<string, UserData>.NewBuilder()
    ///     .MaximumSize(1000)
    ///     .ExpireAfterWrite(TimeSpan.FromMinutes(5))
    ///     .Build();
    ///
    /// cache.Set("user1", userData);
    /// if (cache.TryGet("user1", out UserData data))
    /// {
    ///     // Use cached data
    /// }
    ///
    /// // Loading cache
    /// Cache<int, ExpensiveResult> loadingCache = CacheBuilder<int, ExpensiveResult>.NewBuilder()
    ///     .MaximumSize(100)
    ///     .Build(key => ComputeExpensiveResult(key));
    ///
    /// ExpensiveResult result = loadingCache.GetOrAdd(42, null);
    /// ]]></code>
    /// </example>
    /// <typeparam name="TKey">The type of keys in the cache.</typeparam>
    /// <typeparam name="TValue">The type of values in the cache.</typeparam>
    public sealed class Cache<TKey, TValue> : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct CacheEntry
        {
            public TKey Key;
            public TValue Value;
            public float WriteTime;
            public float AccessTime;
            public float ExpirationTime;
            public int Frequency;
            public int PrevIndex;
            public int NextIndex;
            public byte SegmentIndex;
            public bool IsAlive;
            public long Weight;
        }

        private const int InvalidIndex = -1;
        private const int ProbationSegment = 0;
        private const int ProtectedSegment = 1;
        private const float ThrashWindowSeconds = 1f;

        private readonly CacheOptions<TKey, TValue> _options;
        private readonly Func<float> _timeProvider;

        // Lazy initialization to avoid triggering PRNG static initialization during Cache construction,
        // which can cause deadlocks during Unity's "Open Project: Open Scene" phase.
        private IRandom _random;
        private IRandom Random => _random ??= PRNG.Instance;
        private ReaderWriterLockSlim _lock;

#if SINGLE_THREADED
        private readonly Dictionary<TKey, int> _keyToIndex;
#else
        private readonly ConcurrentDictionary<TKey, int> _keyToIndex;
#endif
        private CacheEntry[] _entries;
        private int _count;
        private int _capacity;
        private long _currentWeight;

        private int _lruHead;
        private int _lruTail;
        private int _fifoHead;
        private int _fifoTail;

        private int _probationHead;
        private int _probationTail;
        private int _protectedHead;
        private int _protectedTail;
        private int _probationCount;
        private int _protectedCount;
        private int _protectedCapacity;

        private int _freeListHead;

        private long _hitCount;
        private long _missCount;
        private long _evictionCount;
        private long _loadCount;
        private long _expiredCount;
        private int _peakSize;
        private int _growthEvents;

        private float _lastEvictionTime;
        private int _recentEvictionCount;

        private bool _disposed;

        /// <summary>
        /// Gets the current number of entries in the cache.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Gets the total weight of all entries when using weighted caching.
        /// </summary>
        public long Size => _options.Weigher != null ? _currentWeight : _count;

        /// <summary>
        /// Gets the configured maximum number of entries the cache can hold.
        /// The cache may start smaller and grow up to this limit.
        /// </summary>
        public int MaximumSize => _options.MaximumSize;

        /// <summary>
        /// Gets the current internal capacity of the cache.
        /// This may be smaller than <see cref="MaximumSize"/> if the cache hasn't grown yet.
        /// </summary>
        public int Capacity => _capacity;

        /// <summary>
        /// Gets an enumerable of all keys currently in the cache.
        /// Note: This property allocates a state machine on each access. For zero-allocation
        /// enumeration, use <see cref="GetKeys(List{TKey})"/> instead.
        /// </summary>
        public IEnumerable<TKey> Keys
        {
            get
            {
                float currentTime = _timeProvider();
                for (int i = 0; i < _entries.Length; i++)
                {
                    if (_entries[i].IsAlive && !IsExpired(i, currentTime))
                    {
                        yield return _entries[i].Key;
                    }
                }
            }
        }

        /// <summary>
        /// Populates a list with all keys currently in the cache.
        /// This method is zero-allocation if the list has sufficient capacity.
        /// </summary>
        /// <param name="keys">The list to populate with keys. Will be cleared before populating.</param>
        public void GetKeys(List<TKey> keys)
        {
            if (keys == null || _disposed)
            {
                return;
            }

            keys.Clear();
            float currentTime = _timeProvider();
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].IsAlive && !IsExpired(i, currentTime))
                {
                    keys.Add(_entries[i].Key);
                }
            }
        }

        /// <summary>
        /// Creates a new cache with the specified options.
        /// </summary>
        /// <param name="options">Cache configuration options.</param>
        public Cache(CacheOptions<TKey, TValue> options)
        {
            if (options.MaximumSize <= 0)
            {
                options.MaximumSize = CacheOptions<TKey, TValue>.DefaultMaximumSize;
            }

            if (options.ProtectedRatio <= 0f)
            {
                options.ProtectedRatio = CacheOptions<TKey, TValue>.DefaultProtectedRatio;
            }

            if (options.GrowthFactor <= 1f)
            {
                options.GrowthFactor = CacheOptions<TKey, TValue>.DefaultGrowthFactor;
            }

            if (options.ThrashThresholdEvictionsPerSecond <= 0f)
            {
                options.ThrashThresholdEvictionsPerSecond = CacheOptions<
                    TKey,
                    TValue
                >.DefaultThrashThreshold;
            }

#pragma warning disable CS0618 // Type or member is obsolete
            if (options.Policy == EvictionPolicy.None)
            {
                options.Policy = CacheOptions<TKey, TValue>.DefaultEvictionPolicy;
            }
#pragma warning restore CS0618

            _options = options;
            _timeProvider = options.TimeProvider ?? DefaultTimeProvider;
            // Note: _random is now lazy-initialized via the Random property to avoid
            // triggering PRNG static initialization during Cache construction.

            // Determine initial capacity for internal data structures.
            // Use InitialCapacity if specified, otherwise default to MaximumSize to ensure the cache
            // can hold the expected number of items without requiring growth.
            // Clamp to prevent excessive initial allocations while respecting MaximumSize as upper bound.
            int requestedInitialCapacity =
                options.InitialCapacity > 0 ? options.InitialCapacity : options.MaximumSize;

            // Clamp to reasonable bounds: at least 1, at most min(MaxReasonableInitialCapacity, MaximumSize)
            int maxInitialCapacity = Math.Min(
                CacheOptions<TKey, TValue>.MaxReasonableInitialCapacity,
                options.MaximumSize
            );
            int initialCapacity = Math.Max(
                1,
                Math.Min(requestedInitialCapacity, maxInitialCapacity)
            );
            _capacity = initialCapacity;

#if SINGLE_THREADED
            _keyToIndex = new Dictionary<TKey, int>(initialCapacity);
#else
            _keyToIndex = new ConcurrentDictionary<TKey, int>(
                Environment.ProcessorCount,
                initialCapacity
            );
#endif

            _lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
            _entries = new CacheEntry[initialCapacity];

            InitializeFreeList();
            InitializeLinkedLists();
        }

        // Use Stopwatch for timing instead of Time.realtimeSinceStartup to avoid
        // hanging during Unity's early initialization (e.g., during "Open Scene").
        // Time.realtimeSinceStartup can block or behave unexpectedly when accessed
        // during static initialization before Unity is fully loaded.
        private static readonly System.Diagnostics.Stopwatch Stopwatch =
            System.Diagnostics.Stopwatch.StartNew();

        private static float DefaultTimeProvider()
        {
            return (float)Stopwatch.Elapsed.TotalSeconds;
        }

        private void InitializeFreeList()
        {
            _freeListHead = 0;
            for (int i = 0; i < _entries.Length - 1; i++)
            {
                _entries[i].NextIndex = i + 1;
            }
            _entries[_entries.Length - 1].NextIndex = InvalidIndex;
        }

        private void InitializeLinkedLists()
        {
            _lruHead = InvalidIndex;
            _lruTail = InvalidIndex;
            _fifoHead = InvalidIndex;
            _fifoTail = InvalidIndex;
            _probationHead = InvalidIndex;
            _probationTail = InvalidIndex;
            _protectedHead = InvalidIndex;
            _protectedTail = InvalidIndex;
            _protectedCapacity = (int)(_capacity * _options.ProtectedRatio);
        }

        /// <summary>
        /// Attempts to get a value from the cache.
        /// </summary>
        /// <param name="key">The key to look up.</param>
        /// <param name="value">The cached value if found.</param>
        /// <returns>True if the key was found and not expired, false otherwise.</returns>
        public bool TryGet(TKey key, out TValue value)
        {
            if (_disposed)
            {
                value = default;
                return false;
            }

            _lock.EnterWriteLock();
            try
            {
                return TryGetUnlocked(key, out value);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private bool TryGetUnlocked(TKey key, out TValue value)
        {
            if (key == null || _disposed)
            {
                value = default;
                return false;
            }

            if (!_keyToIndex.TryGetValue(key, out int index))
            {
                RecordMiss();
                value = default;
                return false;
            }

            float currentTime = _timeProvider();

            if (!_entries[index].IsAlive || IsExpired(index, currentTime))
            {
                EvictEntry(index, EvictionReason.Expired);
                RecordMiss();
                RecordExpired();
                value = default;
                return false;
            }

            value = _entries[index].Value;
            OnAccess(index, currentTime);
            RecordHit();
            InvokeOnGet(key, value);

            return true;
        }

        /// <summary>
        /// Gets a value from the cache, computing it if not present.
        /// </summary>
        /// <param name="key">The key to look up.</param>
        /// <param name="factory">Factory function to compute the value. If null, uses the default loader.</param>
        /// <returns>The cached or computed value.</returns>
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
        {
            if (key == null || _disposed)
            {
                return default;
            }

            _lock.EnterWriteLock();
            try
            {
                if (TryGetUnlocked(key, out TValue value))
                {
                    return value;
                }

                Func<TKey, TValue> actualFactory = factory ?? _options.Loader;
                if (actualFactory == null)
                {
                    return default;
                }

                TValue newValue;
                try
                {
                    newValue = actualFactory(key);
                    RecordLoad();
                }
                catch
                {
                    return default;
                }

                SetUnlocked(key, newValue);
                return newValue;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Adds or updates an entry in the cache.
        /// </summary>
        /// <param name="key">The key of the entry.</param>
        /// <param name="value">The value to cache.</param>
        public void Set(TKey key, TValue value)
        {
            Set(key, value, null);
        }

        /// <summary>
        /// Adds or updates an entry in the cache with a custom TTL.
        /// </summary>
        /// <param name="key">The key of the entry.</param>
        /// <param name="value">The value to cache.</param>
        /// <param name="ttlSeconds">Custom TTL in seconds. If null, uses default expiration.</param>
        public void Set(TKey key, TValue value, float? ttlSeconds)
        {
            if (key == null || _disposed)
            {
                return;
            }

            _lock.EnterWriteLock();
            try
            {
                SetUnlocked(key, value, ttlSeconds);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private void SetUnlocked(TKey key, TValue value, float? ttlSeconds = null)
        {
            float currentTime = _timeProvider();

            if (_keyToIndex.TryGetValue(key, out int existingIndex))
            {
                TValue oldValue = _entries[existingIndex].Value;
                long oldWeight = _options.Weigher != null ? _entries[existingIndex].Weight : 0;

                _entries[existingIndex].Value = value;
                _entries[existingIndex].WriteTime = currentTime;
                _entries[existingIndex].AccessTime = currentTime;
                _entries[existingIndex].ExpirationTime = ComputeExpirationTime(
                    key,
                    value,
                    currentTime,
                    ttlSeconds
                );

                if (_options.Weigher != null)
                {
                    long newWeight = _options.Weigher(key, value);
                    _entries[existingIndex].Weight = newWeight;
                    _currentWeight = _currentWeight - oldWeight + newWeight;
                }

                OnAccess(existingIndex, currentTime);
                InvokeOnEviction(key, oldValue, EvictionReason.Replaced);
                InvokeOnSet(key, value);
                return;
            }

            long weight = _options.Weigher != null ? _options.Weigher(key, value) : 0;

            // Evict until we have room for the new weight
            while (
                _options.Weigher != null
                && _currentWeight + weight > _options.MaximumWeight
                && _count > 0
            )
            {
                if (_options.AllowGrowth && ShouldGrow())
                {
                    Grow();
                    break;
                }

                EvictOne(EvictionReason.Capacity);
            }

            // For non-weighted caches, use standard capacity check
            if (_options.Weigher == null)
            {
                EnsureCapacity();
            }

            int newIndex = AllocateEntry();
            if (newIndex == InvalidIndex)
            {
                return;
            }

            _entries[newIndex] = new CacheEntry
            {
                Key = key,
                Value = value,
                WriteTime = currentTime,
                AccessTime = currentTime,
                ExpirationTime = ComputeExpirationTime(key, value, currentTime, ttlSeconds),
                Frequency = 1,
                PrevIndex = InvalidIndex,
                NextIndex = InvalidIndex,
                SegmentIndex = ProbationSegment,
                IsAlive = true,
                Weight = weight,
            };

#if SINGLE_THREADED
            _keyToIndex[key] = newIndex;
#else
            _keyToIndex.TryAdd(key, newIndex);
#endif

            _count++;
            _currentWeight += weight;

            if (_count > _peakSize)
            {
                _peakSize = _count;
            }

            AddToEvictionList(newIndex);
            InvokeOnSet(key, value);
        }

        /// <summary>
        /// Attempts to remove an entry from the cache.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        /// <returns>True if the entry was removed, false if not found.</returns>
        public bool TryRemove(TKey key)
        {
            return TryRemove(key, out _);
        }

        /// <summary>
        /// Attempts to remove an entry from the cache, returning the removed value.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        /// <param name="value">The removed value if found.</param>
        /// <returns>True if the entry was removed, false if not found.</returns>
        public bool TryRemove(TKey key, out TValue value)
        {
            if (key == null || _disposed)
            {
                value = default;
                return false;
            }

            _lock.EnterWriteLock();
            try
            {
                if (!_keyToIndex.TryGetValue(key, out int index))
                {
                    value = default;
                    return false;
                }

                value = _entries[index].Value;
                EvictEntry(index, EvictionReason.Explicit);
                return true;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes all entries from the cache.
        /// </summary>
        public void Clear()
        {
            if (_disposed)
            {
                return;
            }

            _lock.EnterWriteLock();
            try
            {
                for (int i = 0; i < _entries.Length; i++)
                {
                    if (_entries[i].IsAlive)
                    {
                        InvokeOnEviction(
                            _entries[i].Key,
                            _entries[i].Value,
                            EvictionReason.Explicit
                        );
                        _entries[i] = default;
                    }
                }

                _keyToIndex.Clear();
                _count = 0;
                _currentWeight = 0;
                _probationCount = 0;
                _protectedCount = 0;

                InitializeFreeList();
                InitializeLinkedLists();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Checks if the cache contains the specified key.
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <returns>True if the key exists and is not expired.</returns>
        public bool ContainsKey(TKey key)
        {
            if (key == null || _disposed)
            {
                return false;
            }

            _lock.EnterReadLock();
            try
            {
                if (!_keyToIndex.TryGetValue(key, out int index))
                {
                    return false;
                }

                if (!_entries[index].IsAlive || IsExpired(index, _timeProvider()))
                {
                    return false;
                }

                return true;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Gets multiple values from the cache.
        /// Note: This overload allocates an enumerator. For zero-allocation enumeration,
        /// use <see cref="GetAll(IList{TKey}, IDictionary{TKey, TValue})"/> instead.
        /// </summary>
        /// <param name="keys">The keys to look up.</param>
        /// <param name="results">Dictionary to receive the found key-value pairs.</param>
        public void GetAll(IEnumerable<TKey> keys, IDictionary<TKey, TValue> results)
        {
            if (keys == null || results == null || _disposed)
            {
                return;
            }

            foreach (TKey key in keys)
            {
                if (TryGet(key, out TValue value))
                {
                    results[key] = value;
                }
            }
        }

        /// <summary>
        /// Gets multiple values from the cache using indexed access.
        /// This overload is zero-allocation.
        /// </summary>
        /// <param name="keys">The keys to look up.</param>
        /// <param name="results">Dictionary to receive the found key-value pairs.</param>
        public void GetAll(IList<TKey> keys, IDictionary<TKey, TValue> results)
        {
            if (keys == null || results == null || _disposed)
            {
                return;
            }

            for (int i = 0; i < keys.Count; i++)
            {
                TKey key = keys[i];
                if (TryGet(key, out TValue value))
                {
                    results[key] = value;
                }
            }
        }

        /// <summary>
        /// Adds or updates multiple entries in the cache.
        /// Note: This overload allocates an enumerator. For zero-allocation enumeration,
        /// use <see cref="SetAll(IList{KeyValuePair{TKey, TValue}})"/> instead.
        /// </summary>
        /// <param name="entries">The entries to add or update.</param>
        public void SetAll(IEnumerable<KeyValuePair<TKey, TValue>> entries)
        {
            if (entries == null || _disposed)
            {
                return;
            }

            foreach (KeyValuePair<TKey, TValue> entry in entries)
            {
                Set(entry.Key, entry.Value);
            }
        }

        /// <summary>
        /// Adds or updates multiple entries in the cache using indexed access.
        /// This overload is zero-allocation.
        /// </summary>
        /// <param name="entries">The entries to add or update.</param>
        public void SetAll(IList<KeyValuePair<TKey, TValue>> entries)
        {
            if (entries == null || _disposed)
            {
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                KeyValuePair<TKey, TValue> entry = entries[i];
                Set(entry.Key, entry.Value);
            }
        }

        /// <summary>
        /// Gets the current cache statistics.
        /// </summary>
        /// <returns>A snapshot of cache statistics.</returns>
        public CacheStatistics GetStatistics()
        {
            return new CacheStatistics(
                hitCount: Interlocked.Read(ref _hitCount),
                missCount: Interlocked.Read(ref _missCount),
                evictionCount: Interlocked.Read(ref _evictionCount),
                loadCount: Interlocked.Read(ref _loadCount),
                expiredCount: Interlocked.Read(ref _expiredCount),
                currentSize: _count,
                peakSize: _peakSize,
                growthEvents: _growthEvents
            );
        }

        /// <summary>
        /// Forces an expiration scan to remove expired entries.
        /// </summary>
        public void CleanUp()
        {
            if (_disposed)
            {
                return;
            }

            _lock.EnterWriteLock();
            try
            {
                float currentTime = _timeProvider();
                for (int i = 0; i < _entries.Length; i++)
                {
                    if (_entries[i].IsAlive && IsExpired(i, currentTime))
                    {
                        EvictEntry(i, EvictionReason.Expired);
                        RecordExpired();
                    }
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Forces eviction of a percentage of entries.
        /// </summary>
        /// <param name="percentage">Percentage of entries to evict (0.0 to 1.0).</param>
        public void Compact(float percentage)
        {
            if (_disposed || percentage <= 0f)
            {
                return;
            }

            if (percentage > 1f)
            {
                percentage = 1f;
            }

            _lock.EnterWriteLock();
            try
            {
                int toEvict = (int)(_count * percentage);
                for (int i = 0; i < toEvict && _count > 0; i++)
                {
                    EvictOne(EvictionReason.Capacity);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Resizes the cache to a new capacity.
        /// </summary>
        /// <param name="newCapacity">The new maximum capacity.</param>
        public void Resize(int newCapacity)
        {
            if (_disposed || newCapacity <= 0)
            {
                return;
            }

            _lock.EnterWriteLock();
            try
            {
                while (_count > newCapacity)
                {
                    EvictOne(EvictionReason.Capacity);
                }

                _capacity = newCapacity;
                _protectedCapacity = (int)(_capacity * _options.ProtectedRatio);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Clear();
            _disposed = true;
            _lock.Dispose();
            _lock = null;
        }

        private float ComputeExpirationTime(
            TKey key,
            TValue value,
            float currentTime,
            float? explicitTtl
        )
        {
            float ttl;

            if (explicitTtl.HasValue && explicitTtl.Value > 0f)
            {
                ttl = explicitTtl.Value;
            }
            else if (_options.ExpireAfter != null)
            {
                ttl = _options.ExpireAfter(key, value);
            }
            else if (_options.ExpireAfterWriteSeconds > 0f)
            {
                ttl = _options.ExpireAfterWriteSeconds;
            }
            else
            {
                return float.MaxValue;
            }

            if (_options.UseJitter && ttl > 0f)
            {
                float maxJitter =
                    _options.JitterMaxSeconds > 0f ? _options.JitterMaxSeconds : ttl * 0.1f;
                ttl += Random.NextFloat(0f, maxJitter);
            }

            return currentTime + ttl;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsExpired(int index, float currentTime)
        {
            if (_entries[index].ExpirationTime <= currentTime)
            {
                return true;
            }

            if (_options.ExpireAfterAccessSeconds > 0f)
            {
                float slidingExpiration =
                    _entries[index].AccessTime + _options.ExpireAfterAccessSeconds;
                if (slidingExpiration <= currentTime)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnAccess(int index, float currentTime)
        {
            _entries[index].AccessTime = currentTime;

            if (_options.ExpireAfterAccessSeconds > 0f)
            {
                _entries[index].ExpirationTime = currentTime + _options.ExpireAfterAccessSeconds;
            }

            switch (_options.Policy)
            {
                case EvictionPolicy.Lru:
                    MoveToLruHead(index);
                    break;
                case EvictionPolicy.Slru:
                    PromoteInSlru(index);
                    break;
                case EvictionPolicy.Lfu:
                    _entries[index].Frequency++;
                    break;
                case EvictionPolicy.Fifo:
                case EvictionPolicy.Random:
                    break;
            }
        }

        private void EnsureCapacity()
        {
            EnsureCapacityForWeight(0);
        }

        private void EnsureCapacityForWeight(long incomingWeight)
        {
            // First, grow internal array if needed and possible (before eviction check)
            // We need to grow if count has reached capacity AND we haven't reached MaximumSize yet
            if (_options.Weigher == null && _count >= _capacity && _capacity < _options.MaximumSize)
            {
                GrowTowardsMaximumSize();
            }

            // For weighted caches, evict based on weight
            if (_options.Weigher != null)
            {
                if (_currentWeight + incomingWeight > _options.MaximumWeight)
                {
                    if (_options.AllowGrowth && ShouldGrow())
                    {
                        Grow();
                    }
                    else
                    {
                        EvictOne(EvictionReason.Capacity);
                    }
                }
                return;
            }

            // For non-weighted caches, evict based on count vs MaximumSize
            // Only evict if we've reached the maximum allowed size (not just internal capacity)
            if (_count >= _options.MaximumSize)
            {
                if (_options.AllowGrowth && ShouldGrow())
                {
                    Grow();
                }
                else
                {
                    EvictOne(EvictionReason.Capacity);
                }
            }
        }

        private void GrowTowardsMaximumSize()
        {
            int newCapacity = Math.Min(
                (int)(_capacity * _options.GrowthFactor),
                _options.MaximumSize
            );
            if (newCapacity <= _capacity)
            {
                newCapacity = Math.Min(_capacity + 1, _options.MaximumSize);
            }

            if (newCapacity <= _capacity)
            {
                return;
            }

            CacheEntry[] newEntries = new CacheEntry[newCapacity];
            Array.Copy(_entries, newEntries, _entries.Length);

            int oldLength = _entries.Length;
            _entries = newEntries;

            for (int i = oldLength; i < newCapacity - 1; i++)
            {
                _entries[i].NextIndex = i + 1;
            }
            _entries[newCapacity - 1].NextIndex = _freeListHead;
            _freeListHead = oldLength;

            _capacity = newCapacity;
            _protectedCapacity = (int)(_capacity * _options.ProtectedRatio);
        }

        private bool ShouldGrow()
        {
            if (_options.MaxGrowthSize > 0 && _capacity >= _options.MaxGrowthSize)
            {
                return false;
            }

            float currentTime = _timeProvider();
            if (currentTime - _lastEvictionTime > ThrashWindowSeconds)
            {
                _recentEvictionCount = 0;
                _lastEvictionTime = currentTime;
            }

            float evictionsPerSecond =
                _recentEvictionCount / Math.Max(0.001f, currentTime - _lastEvictionTime);
            return evictionsPerSecond >= _options.ThrashThresholdEvictionsPerSecond;
        }

        private void Grow()
        {
            int newCapacity = (int)(_capacity * _options.GrowthFactor);
            if (_options.MaxGrowthSize > 0)
            {
                newCapacity = Math.Min(newCapacity, _options.MaxGrowthSize);
            }

            if (newCapacity <= _capacity)
            {
                return;
            }

            CacheEntry[] newEntries = new CacheEntry[newCapacity];
            Array.Copy(_entries, newEntries, _entries.Length);

            int oldLength = _entries.Length;
            _entries = newEntries;

            for (int i = oldLength; i < newCapacity - 1; i++)
            {
                _entries[i].NextIndex = i + 1;
            }
            _entries[newCapacity - 1].NextIndex = _freeListHead;
            _freeListHead = oldLength;

            _capacity = newCapacity;
            _protectedCapacity = (int)(_capacity * _options.ProtectedRatio);
            _growthEvents++;
            _recentEvictionCount = 0;
        }

        private int AllocateEntry()
        {
            if (_freeListHead == InvalidIndex)
            {
                return InvalidIndex;
            }

            int index = _freeListHead;
            _freeListHead = _entries[index].NextIndex;
            return index;
        }

        private void FreeEntry(int index)
        {
            _entries[index] = default;
            _entries[index].NextIndex = _freeListHead;
            _freeListHead = index;
        }

        private void AddToEvictionList(int index)
        {
            switch (_options.Policy)
            {
                case EvictionPolicy.Lru:
                    AddToLruHead(index);
                    break;
                case EvictionPolicy.Slru:
                    AddToProbation(index);
                    break;
                case EvictionPolicy.Lfu:
                    AddToLruHead(index);
                    break;
                case EvictionPolicy.Fifo:
                    AddToFifoTail(index);
                    break;
                case EvictionPolicy.Random:
                    break;
            }
        }

        private void RemoveFromEvictionList(int index)
        {
            switch (_options.Policy)
            {
                case EvictionPolicy.Lru:
                case EvictionPolicy.Lfu:
                    RemoveFromLru(index);
                    break;
                case EvictionPolicy.Slru:
                    RemoveFromSlru(index);
                    break;
                case EvictionPolicy.Fifo:
                    RemoveFromFifo(index);
                    break;
                case EvictionPolicy.Random:
                    break;
            }
        }

        private void EvictOne(EvictionReason reason)
        {
            int victim = SelectVictim();
            if (victim != InvalidIndex)
            {
                EvictEntry(victim, reason);
                _recentEvictionCount++;
            }
        }

        private int SelectVictim()
        {
            float currentTime = _timeProvider();
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].IsAlive && IsExpired(i, currentTime))
                {
                    return i;
                }
            }

            switch (_options.Policy)
            {
                case EvictionPolicy.Lru:
                    return _lruTail;
                case EvictionPolicy.Slru:
                    return _probationTail != InvalidIndex ? _probationTail : _protectedTail;
                case EvictionPolicy.Lfu:
                    return SelectLfuVictim();
                case EvictionPolicy.Fifo:
                    return _fifoHead;
                case EvictionPolicy.Random:
                    return SelectRandomVictim();
                default:
                    return InvalidIndex;
            }
        }

        private int SelectLfuVictim()
        {
            int victim = InvalidIndex;
            int minFrequency = int.MaxValue;
            float oldestAccess = float.MaxValue;

            for (int i = 0; i < _entries.Length; i++)
            {
                if (!_entries[i].IsAlive)
                {
                    continue;
                }

                if (
                    _entries[i].Frequency < minFrequency
                    || (
                        _entries[i].Frequency == minFrequency
                        && _entries[i].AccessTime < oldestAccess
                    )
                )
                {
                    minFrequency = _entries[i].Frequency;
                    oldestAccess = _entries[i].AccessTime;
                    victim = i;
                }
            }

            return victim;
        }

        private int SelectRandomVictim()
        {
            if (_count == 0)
            {
                return InvalidIndex;
            }

            int targetCount = Random.Next(0, _count);
            int current = 0;

            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].IsAlive)
                {
                    if (current == targetCount)
                    {
                        return i;
                    }
                    current++;
                }
            }

            return InvalidIndex;
        }

        private void EvictEntry(int index, EvictionReason reason)
        {
            if (!_entries[index].IsAlive)
            {
                return;
            }

            TKey key = _entries[index].Key;
            TValue value = _entries[index].Value;
            long weight = _entries[index].Weight;
            byte segmentIndex = _entries[index].SegmentIndex;

#if SINGLE_THREADED
            _keyToIndex.Remove(key);
#else
            _keyToIndex.TryRemove(key, out _);
#endif

            RemoveFromEvictionList(index);
            FreeEntry(index);

            _count--;
            _currentWeight -= weight;

            if (_options.Policy == EvictionPolicy.Slru)
            {
                if (segmentIndex == ProbationSegment)
                {
                    _probationCount--;
                }
                else
                {
                    _protectedCount--;
                }
            }

            RecordEviction();
            InvokeOnEviction(key, value, reason);
        }

        private void AddToLruHead(int index)
        {
            _entries[index].PrevIndex = InvalidIndex;
            _entries[index].NextIndex = _lruHead;

            if (_lruHead != InvalidIndex)
            {
                _entries[_lruHead].PrevIndex = index;
            }
            _lruHead = index;

            if (_lruTail == InvalidIndex)
            {
                _lruTail = index;
            }
        }

        private void MoveToLruHead(int index)
        {
            if (index == _lruHead)
            {
                return;
            }

            RemoveFromLru(index);
            AddToLruHead(index);
        }

        private void RemoveFromLru(int index)
        {
            int prev = _entries[index].PrevIndex;
            int next = _entries[index].NextIndex;

            if (prev != InvalidIndex)
            {
                _entries[prev].NextIndex = next;
            }
            else
            {
                _lruHead = next;
            }

            if (next != InvalidIndex)
            {
                _entries[next].PrevIndex = prev;
            }
            else
            {
                _lruTail = prev;
            }

            _entries[index].PrevIndex = InvalidIndex;
            _entries[index].NextIndex = InvalidIndex;
        }

        private void AddToFifoTail(int index)
        {
            _entries[index].PrevIndex = _fifoTail;
            _entries[index].NextIndex = InvalidIndex;

            if (_fifoTail != InvalidIndex)
            {
                _entries[_fifoTail].NextIndex = index;
            }
            _fifoTail = index;

            if (_fifoHead == InvalidIndex)
            {
                _fifoHead = index;
            }
        }

        private void RemoveFromFifo(int index)
        {
            int prev = _entries[index].PrevIndex;
            int next = _entries[index].NextIndex;

            if (prev != InvalidIndex)
            {
                _entries[prev].NextIndex = next;
            }
            else
            {
                _fifoHead = next;
            }

            if (next != InvalidIndex)
            {
                _entries[next].PrevIndex = prev;
            }
            else
            {
                _fifoTail = prev;
            }

            _entries[index].PrevIndex = InvalidIndex;
            _entries[index].NextIndex = InvalidIndex;
        }

        private void AddToProbation(int index)
        {
            _entries[index].SegmentIndex = ProbationSegment;
            _entries[index].PrevIndex = InvalidIndex;
            _entries[index].NextIndex = _probationHead;

            if (_probationHead != InvalidIndex)
            {
                _entries[_probationHead].PrevIndex = index;
            }
            _probationHead = index;

            if (_probationTail == InvalidIndex)
            {
                _probationTail = index;
            }

            _probationCount++;
        }

        private void PromoteInSlru(int index)
        {
            if (_entries[index].SegmentIndex == ProtectedSegment)
            {
                MoveToProtectedHead(index);
                return;
            }

            RemoveFromProbation(index);
            AddToProtected(index);

            if (_protectedCount > _protectedCapacity)
            {
                DemoteFromProtected();
            }
        }

        private void AddToProtected(int index)
        {
            _entries[index].SegmentIndex = ProtectedSegment;
            _entries[index].PrevIndex = InvalidIndex;
            _entries[index].NextIndex = _protectedHead;

            if (_protectedHead != InvalidIndex)
            {
                _entries[_protectedHead].PrevIndex = index;
            }
            _protectedHead = index;

            if (_protectedTail == InvalidIndex)
            {
                _protectedTail = index;
            }

            _protectedCount++;
        }

        private void MoveToProtectedHead(int index)
        {
            if (index == _protectedHead)
            {
                return;
            }

            int prev = _entries[index].PrevIndex;
            int next = _entries[index].NextIndex;

            if (prev != InvalidIndex)
            {
                _entries[prev].NextIndex = next;
            }

            if (next != InvalidIndex)
            {
                _entries[next].PrevIndex = prev;
            }
            else
            {
                _protectedTail = prev;
            }

            _entries[index].PrevIndex = InvalidIndex;
            _entries[index].NextIndex = _protectedHead;

            if (_protectedHead != InvalidIndex)
            {
                _entries[_protectedHead].PrevIndex = index;
            }
            _protectedHead = index;
        }

        private void DemoteFromProtected()
        {
            if (_protectedTail == InvalidIndex)
            {
                return;
            }

            int demoted = _protectedTail;
            RemoveFromProtected(demoted);
            AddToProbation(demoted);
        }

        private void RemoveFromProbation(int index)
        {
            int prev = _entries[index].PrevIndex;
            int next = _entries[index].NextIndex;

            if (prev != InvalidIndex)
            {
                _entries[prev].NextIndex = next;
            }
            else
            {
                _probationHead = next;
            }

            if (next != InvalidIndex)
            {
                _entries[next].PrevIndex = prev;
            }
            else
            {
                _probationTail = prev;
            }

            _entries[index].PrevIndex = InvalidIndex;
            _entries[index].NextIndex = InvalidIndex;
            _probationCount--;
        }

        private void RemoveFromProtected(int index)
        {
            int prev = _entries[index].PrevIndex;
            int next = _entries[index].NextIndex;

            if (prev != InvalidIndex)
            {
                _entries[prev].NextIndex = next;
            }
            else
            {
                _protectedHead = next;
            }

            if (next != InvalidIndex)
            {
                _entries[next].PrevIndex = prev;
            }
            else
            {
                _protectedTail = prev;
            }

            _entries[index].PrevIndex = InvalidIndex;
            _entries[index].NextIndex = InvalidIndex;
            _protectedCount--;
        }

        private void RemoveFromSlru(int index)
        {
            if (_entries[index].SegmentIndex == ProbationSegment)
            {
                RemoveFromProbation(index);
            }
            else
            {
                RemoveFromProtected(index);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordHit()
        {
            if (_options.RecordStatistics)
            {
                Interlocked.Increment(ref _hitCount);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordMiss()
        {
            if (_options.RecordStatistics)
            {
                Interlocked.Increment(ref _missCount);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordEviction()
        {
            if (_options.RecordStatistics)
            {
                Interlocked.Increment(ref _evictionCount);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordLoad()
        {
            if (_options.RecordStatistics)
            {
                Interlocked.Increment(ref _loadCount);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordExpired()
        {
            if (_options.RecordStatistics)
            {
                Interlocked.Increment(ref _expiredCount);
            }
        }

        private void InvokeOnEviction(TKey key, TValue value, EvictionReason reason)
        {
            if (_options.OnEviction == null)
            {
                return;
            }

            try
            {
                _options.OnEviction(key, value, reason);
            }
            catch
            {
                // Swallow exceptions from callbacks
            }
        }

        private void InvokeOnGet(TKey key, TValue value)
        {
            if (_options.OnGet == null)
            {
                return;
            }

            try
            {
                _options.OnGet(key, value);
            }
            catch
            {
                // Swallow exceptions from callbacks
            }
        }

        private void InvokeOnSet(TKey key, TValue value)
        {
            if (_options.OnSet == null)
            {
                return;
            }

            try
            {
                _options.OnSet(key, value);
            }
            catch
            {
                // Swallow exceptions from callbacks
            }
        }
    }
}
