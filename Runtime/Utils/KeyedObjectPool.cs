// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using UnityEngine;

    /// <summary>Owns one tracked Unity object pool per key and routes returns to their owner.</summary>
    /// <remarks>Use on the Unity main thread. Disposal reaches idle and checked-out items. Keys must keep stable equality and hashing.</remarks>
    public sealed class KeyedObjectPool<TKey, T> : IDisposable
        where T : UnityEngine.Object
    {
        /// <summary>Gets the number of keys that have created a pool.</summary>
        public int PoolCount => _pools.Count;

        /// <summary>Gets whether this owner has been disposed.</summary>
        public bool IsDisposed => _disposed;

        private readonly Func<TKey, T> _producer;
        private readonly Action<T> _onTake;
        private readonly Action<T> _onRelease;
        private readonly Action<T> _onDestroy;
        private readonly int _maxIdleCount;
        private readonly Dictionary<TKey, TrackedObjectPool<T>> _pools;
        private readonly ConditionalWeakTable<T, TrackedObjectPool<T>> _owners = new();
        private bool _disposed;

        /// <summary>Creates a keyed owner with a producer and lifecycle callbacks shared by its pools.</summary>
        /// <param name="producer">Creates a fresh object for the requested key.</param>
        /// <param name="onTake">Runs when an item is checked out.</param>
        /// <param name="onRelease">Runs when a live checked-out item returns.</param>
        /// <param name="onDestroy">Retires live items on disposal, eviction, or failed checkout; null leaves destruction to the caller.</param>
        /// <param name="maxIdleCount">Maximum idle items per key; zero or less means unbounded.</param>
        /// <param name="comparer">Key equality policy, or the default comparer.</param>
        public KeyedObjectPool(
            Func<TKey, T> producer,
            Action<T> onTake = null,
            Action<T> onRelease = null,
            Action<T> onDestroy = null,
            int maxIdleCount = 0,
            IEqualityComparer<TKey> comparer = null
        )
        {
            _producer = producer;
            _onTake = onTake;
            _onRelease = onRelease;
            _onDestroy = onDestroy;
            _maxIdleCount = maxIdleCount;
            _pools = new Dictionary<TKey, TrackedObjectPool<T>>(comparer);
        }

        /// <summary>Takes an item for a non-null key, returning false when it cannot be produced.</summary>
        /// <remarks>A producer that returns an already-owned object is refused. Same-key recursive production is refused. Callback failures follow TrackedObjectPool's logged failure policy.</remarks>
        public bool TryTake(TKey key, out T item)
        {
            if (_disposed || key is null || _producer == null)
            {
                item = null;
                return false;
            }
            TrackedObjectPool<T> pool;
            try
            {
                if (!_pools.TryGetValue(key, out pool))
                {
                    pool = CreatePool(key);
                    _pools.Add(key, pool);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                item = null;
                return false;
            }
            if (_disposed)
            {
                pool.Dispose();
                _pools.Clear();
                item = null;
                return false;
            }
            return pool.TryTake(out item);
        }

        /// <summary>Returns an item to its original pool; foreign, duplicate and post-disposal returns fail.</summary>
        public bool Release(T item)
        {
            if (
                _disposed
                || ReferenceEquals(item, null)
                || !_owners.TryGetValue(item, out TrackedObjectPool<T> pool)
            )
            {
                return false;
            }
            return pool.Release(item);
        }

        /// <summary>Disposes every child pool, reaching its idle and checked-out objects once.</summary>
        /// <remarks>Disposal is latched before callbacks run, so reentrant takes, returns, and disposal cannot mutate the child-pool collection.</remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            foreach (TrackedObjectPool<T> pool in _pools.Values)
            {
                pool.Dispose();
            }
            _pools.Clear();
            _owners.Clear();
        }

        private TrackedObjectPool<T> CreatePool(TKey key)
        {
            TrackedObjectPool<T> pool = null;
            bool producing = false;
            pool = new TrackedObjectPool<T>(
                producer: () =>
                {
                    if (producing)
                    {
                        return null;
                    }
                    producing = true;
                    try
                    {
                        T item = _producer(key);
                        if (item == null || _owners.TryGetValue(item, out _))
                        {
                            return null;
                        }
                        _owners.Add(item, pool);
                        return item;
                    }
                    finally
                    {
                        producing = false;
                    }
                },
                onTake: _onTake,
                onRelease: _onRelease,
                onDestroy: item =>
                {
                    try
                    {
                        _onDestroy?.Invoke(item);
                    }
                    finally
                    {
                        _owners.Remove(item);
                    }
                },
                maxIdleCount: _maxIdleCount
            );
            return pool;
        }
    }
}
