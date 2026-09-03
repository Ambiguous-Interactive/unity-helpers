// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A bounded, least-recently-used cache shared by the package's static key-to-value caches.
    /// </summary>
    /// <remarks>
    /// Every caller here holds a strong reference to a key a game varies at runtime -- a comparer
    /// that is routinely a <c>MonoBehaviour</c>, a <c>ScriptableObject</c>, or a closure capturing
    /// one; a string built from gameplay. An unbounded cache on a static type therefore keeps every
    /// key a game ever produced alive for the process, surviving scene unload and -- with Domain
    /// Reload disabled -- every play session. A bound is the right answer rather than a weak key
    /// because a value routinely reaches its own key: a pool's factory closes over the comparer, so
    /// a weak-keyed table would never collect anything. Eviction is therefore only correct where
    /// re-creating the value is equivalent to keeping it, which every caller must establish for
    /// itself.
    /// </remarks>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value cached per key.</typeparam>
    internal sealed class BoundedLruCache<TKey, TValue>
        where TKey : class
        where TValue : class
    {
        private readonly Dictionary<TKey, Entry> _entries = new();
        private readonly LinkedList<TKey> _accessOrder = new();
        private readonly Func<int> _maxEntries;
#if !SINGLE_THREADED
        private readonly object _lock = new();
#endif

        /// <summary>
        /// Creates a cache whose bound is read from <paramref name="maxEntries"/> on every insert.
        /// </summary>
        /// <param name="maxEntries">
        /// Supplies the live bound. A value of 0 or less, or a null supplier, removes the bound.
        /// It is read per insert rather than captured so a consumer can retune it at runtime.
        /// </param>
        internal BoundedLruCache(Func<int> maxEntries)
        {
            _maxEntries = maxEntries;
        }

        /// <summary>
        /// The number of keys this cache currently holds.
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
        /// Returns the value cached for <paramref name="key"/>, creating and caching one when none
        /// exists. Evicts the least recently used entry when the cache is at its bound.
        /// </summary>
        internal TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
        {
            if (TryTouch(key, out TValue cached))
            {
                return cached;
            }

            /*
                Built outside the lock, so the factory may take locks of its own: a pool registers
                itself with GlobalPoolRegistry, whose purge callbacks run consumer code that may ask
                this cache for a pool, and constructing under this monitor would order two locks
                against each other for nothing. The cost is that a race constructs twice and
                discards the loser, which is what ConcurrentDictionary.GetOrAdd did here before, and
                which every caller must be able to afford.
            */
            TValue created = factory(key);

#if !SINGLE_THREADED
            lock (_lock)
#endif
            {
                if (_entries.TryGetValue(key, out Entry raced))
                {
                    MoveToMostRecent(raced.node);
                    return raced.value;
                }

                int maxEntries = _maxEntries == null ? 0 : _maxEntries();
                while (0 < maxEntries && maxEntries <= _entries.Count && _accessOrder.First != null)
                {
                    TKey evicted = _accessOrder.First.Value;
                    _accessOrder.RemoveFirst();
                    _ = _entries.Remove(evicted);
                }

                LinkedListNode<TKey> node = _accessOrder.AddLast(key);
                _entries[key] = new Entry(created, node);
                return created;
            }
        }

        private bool TryTouch(TKey key, out TValue value)
        {
#if !SINGLE_THREADED
            lock (_lock)
#endif
            {
                if (!_entries.TryGetValue(key, out Entry existing))
                {
                    value = null;
                    return false;
                }

                MoveToMostRecent(existing.node);
                value = existing.value;
                return true;
            }
        }

        private void MoveToMostRecent(LinkedListNode<TKey> node)
        {
            if (node.List == null)
            {
                return;
            }

            _accessOrder.Remove(node);
            _accessOrder.AddLast(node);
        }

        /// <summary>
        /// Indicates whether a value is cached for <paramref name="key"/>.
        /// </summary>
        internal bool Contains(TKey key)
        {
#if !SINGLE_THREADED
            lock (_lock)
#endif
            {
                return _entries.ContainsKey(key);
            }
        }

        /// <summary>
        /// Removes the value cached for <paramref name="key"/> and hands it to the caller.
        /// </summary>
        internal bool TryRemove(TKey key, out TValue value)
        {
#if !SINGLE_THREADED
            lock (_lock)
#endif
            {
                if (!_entries.TryGetValue(key, out Entry existing))
                {
                    value = null;
                    return false;
                }

                _ = _entries.Remove(key);
                if (existing.node.List != null)
                {
                    _accessOrder.Remove(existing.node);
                }

                value = existing.value;
                return true;
            }
        }

        /// <summary>
        /// Drops every cached value without disposing it, releasing the keys this cache roots.
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
            public readonly TValue value;
            public readonly LinkedListNode<TKey> node;

            public Entry(TValue value, LinkedListNode<TKey> node)
            {
                this.value = value;
                this.node = node;
            }
        }
    }
}
