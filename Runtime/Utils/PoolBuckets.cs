// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using WallstopStudios.UnityHelpers.Core.Extension;
#if !SINGLE_THREADED
    using System.Collections.Concurrent;
#endif

    /// <summary>
    /// The free list for a single size class of an array pool: an array-backed LIFO stack of
    /// idle instances, guarded by a monitor unless <c>SINGLE_THREADED</c> is defined.
    /// </summary>
    /// <typeparam name="T">The pooled instance type, typically an array type such as <c>int[]</c>.</typeparam>
    /// <remarks>
    /// Array-backed rather than a lock-free linked structure because a pool exists to stop
    /// allocating: <see cref="System.Collections.Concurrent.ConcurrentStack{T}"/> allocates a node
    /// per push, which is 32 bytes charged to every single rent-and-return cycle.
    /// </remarks>
    internal sealed class PoolBucket<T>
    {
        private readonly Stack<T> _idle = new();

        /// <summary>
        /// Takes an idle instance if this bucket has one.
        /// </summary>
        /// <param name="instance">The instance taken, or <c>default</c> when the bucket is empty.</param>
        /// <returns><c>true</c> when an instance was taken.</returns>
        internal bool TryRent(out T instance)
        {
#if !SINGLE_THREADED
            lock (_idle)
            {
#endif
            return _idle.TryPop(out instance);
#if !SINGLE_THREADED
            }
#endif
        }

        /// <summary>
        /// Returns an instance to this bucket for reuse.
        /// </summary>
        /// <param name="instance">The instance to make available again.</param>
        internal void Return(T instance)
        {
#if !SINGLE_THREADED
            lock (_idle)
            {
#endif
            _idle.Push(instance);
#if !SINGLE_THREADED
            }
#endif
        }

        /// <summary>
        /// Drops every idle instance, leaving the bucket empty.
        /// </summary>
        internal void Clear()
        {
#if !SINGLE_THREADED
            lock (_idle)
            {
#endif
            _idle.Clear();
#if !SINGLE_THREADED
            }
#endif
        }
    }

    /// <summary>
    /// A sparse map from an exact array size to its <see cref="PoolBucket{T}"/>, for pools whose
    /// requested sizes are arbitrary and unbounded.
    /// </summary>
    /// <typeparam name="T">The pooled instance type.</typeparam>
    internal sealed class PoolBucketMap<T>
    {
#if SINGLE_THREADED
        private readonly Dictionary<int, PoolBucket<T>> _buckets = new();
#else
        private readonly ConcurrentDictionary<int, PoolBucket<T>> _buckets = new();
#endif

        /// <summary>
        /// Gets the bucket for <paramref name="size"/>, creating it on first use.
        /// </summary>
        /// <param name="size">The exact array size the bucket holds.</param>
        /// <returns>The bucket for that size; never <c>null</c>.</returns>
        internal PoolBucket<T> Bucket(int size)
        {
            if (_buckets.TryGetValue(size, out PoolBucket<T> existing))
            {
                return existing;
            }

            // GetOrAdd(key) constructs only on the miss this lookup just proved; the overload taking
            // a value would allocate a bucket on the way in whether or not one is needed.
            return _buckets.GetOrAdd(size);
        }
    }

    /// <summary>
    /// A dense table from an exact array size to its <see cref="PoolBucket{T}"/>, indexed directly
    /// by size so a lookup is a bounds check and a load rather than a hash.
    /// </summary>
    /// <typeparam name="T">The pooled instance type.</typeparam>
    /// <remarks>
    /// Growth publishes a whole new array, so a reader either sees the old table or the new one and
    /// never a half-resized one. The previous implementation grew a shared <see cref="List{T}"/>
    /// under a writer lock while the release path indexed that same list with no lock at all.
    /// </remarks>
    internal sealed class PoolBucketTable<T>
    {
        private const int MinimumCapacity = 4;

        // Array.MaxLength is .NET 6+; Unity 2021.3 compiles against netstandard2.1.
        private const int MaximumCapacity = 0X7FFFFFC7;

        private PoolBucket<T>[] _buckets = Array.Empty<PoolBucket<T>>();
#if !SINGLE_THREADED
        private readonly object _growGate = new();
#endif

        /// <summary>
        /// Gets the bucket for <paramref name="size"/>, creating it and widening the table on first use.
        /// </summary>
        /// <param name="size">The exact array size the bucket holds. Must be non-negative.</param>
        /// <returns>The bucket for that size; never <c>null</c>.</returns>
        internal PoolBucket<T> Bucket(int size)
        {
            PoolBucket<T>[] snapshot = Volatile.Read(ref _buckets);
            if (size < snapshot.Length)
            {
                PoolBucket<T> existing = Volatile.Read(ref snapshot[size]);
                if (existing != null)
                {
                    return existing;
                }
            }

            return CreateBucket(size);
        }

        /// <summary>
        /// Drops every idle instance in every bucket, keeping the table and its buckets.
        /// </summary>
        internal void ClearAll()
        {
            PoolBucket<T>[] snapshot = Volatile.Read(ref _buckets);
            for (int i = 0; i < snapshot.Length; ++i)
            {
                Volatile.Read(ref snapshot[i])?.Clear();
            }
        }

        private PoolBucket<T> CreateBucket(int size)
        {
#if !SINGLE_THREADED
            lock (_growGate)
            {
#endif
            PoolBucket<T>[] snapshot = _buckets;
            if (snapshot.Length <= size)
            {
                long doubled = Math.Max(snapshot.Length * 2L, MinimumCapacity);
                long wanted = Math.Max(doubled, size + 1L);
                PoolBucket<T>[] grown = new PoolBucket<T>[(int)Math.Min(wanted, MaximumCapacity)];
                Array.Copy(snapshot, grown, snapshot.Length);
                snapshot = grown;
                Volatile.Write(ref _buckets, grown);
            }

            PoolBucket<T> bucket = snapshot[size];
            if (bucket == null)
            {
                bucket = new PoolBucket<T>();
                Volatile.Write(ref snapshot[size], bucket);
            }

            return bucket;
#if !SINGLE_THREADED
            }
#endif
        }
    }
}
