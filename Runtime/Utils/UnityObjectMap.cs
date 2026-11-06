// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>Stores values by managed Unity object identity with explicit destroyed-key cleanup.</summary>
    /// <remarks>Use this map on Unity's main thread. Null managed keys are refused. Destroyed keys remain accessible until pruned. Auto-pruning holds maps weakly and is disabled on each play-session reset; enable it again when initializing the session.</remarks>
    public sealed class UnityObjectMap<TKey, TValue>
        : IDisposable,
            IUnityObjectMapLifecycle,
            IEnumerable<KeyValuePair<TKey, TValue>>
        where TKey : UnityEngine.Object
    {
        /// <summary>Gets the stored entry count, including keys awaiting pruning.</summary>
        public int Count => _entries.Count;

        /// <summary>Gets or sets whether scene unloads prune destroyed keys.</summary>
        public bool AutoPruneOnSceneUnload
        {
            get => _autoPruneOnSceneUnload;
            set
            {
                if (_disposed || _autoPruneOnSceneUnload == value)
                {
                    return;
                }
                _autoPruneOnSceneUnload = value;
                if (value)
                {
                    UnityObjectMapLifecycle.Register(this);
                }
                else
                {
                    UnityObjectMapLifecycle.Unregister(this);
                }
            }
        }

        private readonly Dictionary<TKey, TValue> _entries = new(
            ReferenceIdentityComparer<TKey>.Instance
        );
        private bool _autoPruneOnSceneUnload;
        private bool _disposed;
        private ulong _version;

        /// <summary>Creates an empty map, optionally pruning on scene unload.</summary>
        public UnityObjectMap(bool autoPruneOnSceneUnload = false)
        {
            AutoPruneOnSceneUnload = autoPruneOnSceneUnload;
        }

        /// <summary>Stores or replaces a value, returning false for null keys or a disposed map.</summary>
        public bool TrySet(TKey key, TValue value)
        {
            if (_disposed || ReferenceEquals(key, null))
            {
                return false;
            }
            _entries[key] = value;
            ++_version;
            return true;
        }

        /// <summary>Gets a live key's value or creates and stores a value once for this call.</summary>
        /// <remarks>
        /// Null keys, null factories and disposed maps return default without invoking the factory.
        /// Destroyed keys are treated as misses and their stored values are replaced; they remain prunable.
        /// Factory exceptions propagate without storing a result. Use a static factory to avoid captures.
        /// </remarks>
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
        {
            if (factory == null)
            {
                return default;
            }
            return GetOrAdd(key, static (item, create) => create(item), factory);
        }

        /// <summary>Gets or creates a value with caller state without allocating a closure.</summary>
        /// <remarks>
        /// Uses the same null, disposal and destroyed-key rules as the stateless overload.
        /// A live value inserted by the factory takes precedence; disposal during the factory returns default.
        /// Factory exceptions propagate without storing a result.
        /// </remarks>
        public TValue GetOrAdd<TState>(TKey key, Func<TKey, TState, TValue> factory, TState state)
        {
            if (_disposed || ReferenceEquals(key, null) || factory == null)
            {
                return default;
            }
            if (key != null && _entries.TryGetValue(key, out TValue existing))
            {
                return existing;
            }
            TValue created = factory(key, state);
            if (_disposed)
            {
                return default;
            }
            if (key != null && _entries.TryGetValue(key, out existing))
            {
                return existing;
            }
            TrySet(key, created);
            return created;
        }

        /// <summary>Enumerates live keys and values without allocating.</summary>
        /// <remarks>Destroyed keys are skipped without pruning; mutations invalidate the enumerator.</remarks>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        /// <summary>Finds a value by managed reference, even after its Unity object is destroyed.</summary>
        public bool TryGetValue(TKey key, out TValue value)
        {
            if (!_disposed && !ReferenceEquals(key, null))
            {
                return _entries.TryGetValue(key, out value);
            }
            value = default;
            return false;
        }

        /// <summary>Returns whether the exact managed key is stored, including destroyed keys.</summary>
        public bool ContainsKey(TKey key)
        {
            return !_disposed && !ReferenceEquals(key, null) && _entries.ContainsKey(key);
        }

        /// <summary>Removes the exact managed key, including a destroyed key.</summary>
        public bool Remove(TKey key)
        {
            if (!_disposed && !ReferenceEquals(key, null) && _entries.Remove(key))
            {
                ++_version;
                return true;
            }
            return false;
        }

        /// <summary>Removes all stored keys and values.</summary>
        public void Clear()
        {
            _entries.Clear();
            ++_version;
        }

        /// <summary>Removes only destroyed Unity keys and returns their count.</summary>
        public int PruneDestroyed()
        {
            if (_disposed || _entries.Count == 0)
            {
                return 0;
            }
            using PooledResource<List<TKey>> lease = Buffers<TKey>.List.Get(
                out List<TKey> destroyed
            );
            foreach (TKey key in _entries.Keys)
            {
                if (key == null)
                {
                    destroyed.Add(key);
                }
            }
            foreach (TKey key in destroyed)
            {
                Remove(key);
            }
            return destroyed.Count;
        }

        /// <summary>Unregisters lifecycle cleanup and releases all stored references.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            AutoPruneOnSceneUnload = false;
            _disposed = true;
            Clear();
        }

        void IUnityObjectMapLifecycle.PruneDestroyedKeys()
        {
            PruneDestroyed();
        }

        void IUnityObjectMapLifecycle.ResetForPlaySession()
        {
            _autoPruneOnSceneUnload = false;
            Clear();
        }

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<
            KeyValuePair<TKey, TValue>
        >.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>Walks live Unity keys while detecting map mutations.</summary>
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>, IEquatable<Enumerator>
        {
            /// <inheritdoc />
            public KeyValuePair<TKey, TValue> Current => _current;

            object IEnumerator.Current => _current;

            private readonly UnityObjectMap<TKey, TValue> _map;
            private readonly ulong _version;
            private Dictionary<TKey, TValue>.Enumerator _entries;
            private int _position;
            private KeyValuePair<TKey, TValue> _current;

            internal Enumerator(UnityObjectMap<TKey, TValue> map)
            {
                _map = map;
                _version = map._version;
                _entries = map._entries.GetEnumerator();
                _position = 0;
                _current = default;
            }

            /// <inheritdoc />
            public bool MoveNext()
            {
                ValidateVersion();
                if (_map != null)
                {
                    while (_entries.MoveNext())
                    {
                        ++_position;
                        KeyValuePair<TKey, TValue> pair = _entries.Current;
                        if (pair.Key != null)
                        {
                            _current = pair;
                            return true;
                        }
                    }
                }
                _current = default;
                return false;
            }

            /// <inheritdoc />
            public void Reset()
            {
                ValidateVersion();
                if (_map != null)
                {
                    _entries = _map._entries.GetEnumerator();
                }
                _position = 0;
                _current = default;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                _entries.Dispose();
            }

            /// <summary>Compares the map, version, position and current pair.</summary>
            public bool Equals(Enumerator other)
            {
                return ReferenceEquals(_map, other._map)
                    && _version == other._version
                    && _position == other._position
                    && ReferenceEquals(_current.Key, other._current.Key)
                    && EqualityComparer<TValue>.Default.Equals(
                        _current.Value,
                        other._current.Value
                    );
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                return obj is Enumerator other && Equals(other);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                int keyHash = ReferenceIdentityComparer<TKey>.Instance.GetHashCode(_current.Key);
                int valueHash = EqualityComparer<TValue>.Default.GetHashCode(_current.Value);
                return Objects.HashCode(_map, _version, _position, keyHash, valueHash);
            }

            private void ValidateVersion()
            {
                if (_map != null && _version != _map._version)
                {
                    throw new InvalidOperationException("The map changed during enumeration.");
                }
            }
        }
    }
}
