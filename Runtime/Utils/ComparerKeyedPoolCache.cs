// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A bounded, least-recently-used cache of pools keyed by a caller-supplied comparer instance.
    /// </summary>
    /// <remarks>
    /// The key is a strong reference to the caller's comparer, and a Unity comparer is routinely a
    /// <c>MonoBehaviour</c>, a <c>ScriptableObject</c>, or a closure capturing one. An unbounded
    /// cache on a static generic type therefore keeps every comparer a game ever built alive for
    /// the process, surviving scene unload and -- with Domain Reload disabled -- every play
    /// session. Evicting the least recently used entry costs one pool construction on the next miss
    /// and nothing else, which is why a bound is the right answer here rather than a weak key: the
    /// pool's own factory closes over the comparer, so a weak-keyed table whose value reaches its
    /// key would never collect anything.
    /// </remarks>
    /// <typeparam name="TComparer">The comparer type used as the cache key.</typeparam>
    /// <typeparam name="TPool">The pool type cached per comparer.</typeparam>
    internal sealed class ComparerKeyedPoolCache<TComparer, TPool>
        where TComparer : class
        where TPool : class
    {
        private readonly Dictionary<TComparer, Entry> _entries = new();
        private readonly LinkedList<TComparer> _accessOrder = new();
#if !SINGLE_THREADED
        private readonly object _lock = new();
#endif

        /// <summary>
        /// The number of comparers this cache currently holds.
        /// </summary>
        internal int Count
        {
#if SINGLE_THREADED
            get { return _entries.Count; }
#else
            get
            {
                lock (_lock)
                {
                    return _entries.Count;
                }
            }
#endif
        }

        /// <summary>
        /// Returns the pool cached for <paramref name="comparer"/>, creating and caching one when
        /// none exists. Evicts the least recently used entry when the cache is at its bound.
        /// </summary>
        internal TPool GetOrAdd(TComparer comparer, Func<TComparer, TPool> factory)
        {
            if (TryTouch(comparer, out TPool cached))
            {
                return cached;
            }

            /*
                Built outside the lock. A pool registers itself with GlobalPoolRegistry, whose purge
                callbacks run consumer code that may ask this cache for a pool, so constructing
                under this monitor would order two locks against each other for nothing. Losing the
                race below discards the loser, which is what ConcurrentDictionary.GetOrAdd did here
                before.
            */
            TPool created = factory(comparer);

#if !SINGLE_THREADED
            lock (_lock)
#endif
            {
                if (_entries.TryGetValue(comparer, out Entry raced))
                {
                    MoveToMostRecent(raced.node);
                    return raced.pool;
                }

                int maxEntries = Buffers.ComparerPoolMaxDistinctEntries;
                while (0 < maxEntries && maxEntries <= _entries.Count && _accessOrder.First != null)
                {
                    TComparer evicted = _accessOrder.First.Value;
                    _accessOrder.RemoveFirst();
                    _ = _entries.Remove(evicted);
                }

                LinkedListNode<TComparer> node = _accessOrder.AddLast(comparer);
                _entries[comparer] = new Entry(created, node);
                return created;
            }
        }

        private bool TryTouch(TComparer comparer, out TPool pool)
        {
#if !SINGLE_THREADED
            lock (_lock)
#endif
            {
                if (!_entries.TryGetValue(comparer, out Entry existing))
                {
                    pool = null;
                    return false;
                }

                MoveToMostRecent(existing.node);
                pool = existing.pool;
                return true;
            }
        }

        private void MoveToMostRecent(LinkedListNode<TComparer> node)
        {
            if (node.List == null)
            {
                return;
            }

            _accessOrder.Remove(node);
            _accessOrder.AddLast(node);
        }

        /// <summary>
        /// Indicates whether a pool is cached for <paramref name="comparer"/>.
        /// </summary>
        internal bool Contains(TComparer comparer)
        {
#if !SINGLE_THREADED
            lock (_lock)
#endif
            {
                return _entries.ContainsKey(comparer);
            }
        }

        /// <summary>
        /// Removes the pool cached for <paramref name="comparer"/> and hands it to the caller.
        /// </summary>
        internal bool TryRemove(TComparer comparer, out TPool pool)
        {
#if !SINGLE_THREADED
            lock (_lock)
#endif
            {
                if (!_entries.TryGetValue(comparer, out Entry existing))
                {
                    pool = null;
                    return false;
                }

                _ = _entries.Remove(comparer);
                if (existing.node.List != null)
                {
                    _accessOrder.Remove(existing.node);
                }

                pool = existing.pool;
                return true;
            }
        }

        /// <summary>
        /// Drops every cached pool without disposing it, releasing the comparers this cache roots.
        /// </summary>
        internal void Clear()
        {
#if !SINGLE_THREADED
            lock (_lock)
#endif
            {
                _entries.Clear();
                _accessOrder.Clear();
            }
        }

        private readonly struct Entry
        {
            public readonly TPool pool;
            public readonly LinkedListNode<TComparer> node;

            public Entry(TPool pool, LinkedListNode<TComparer> node)
            {
                this.pool = pool;
                this.node = node;
            }
        }
    }
}
