// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>Stores values by managed Unity object identity with explicit destroyed-key cleanup.</summary>
    /// <remarks>Use this map on Unity's main thread. Null managed keys are refused. Destroyed keys remain accessible until pruned. Auto-pruning holds maps weakly and is disabled on each play-session reset; enable it again when initializing the session.</remarks>
    public sealed class UnityObjectMap<TKey, TValue> : IDisposable, IUnityObjectMapLifecycle
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
            return true;
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
            return !_disposed && !ReferenceEquals(key, null) && _entries.Remove(key);
        }

        /// <summary>Removes all stored keys and values.</summary>
        public void Clear()
        {
            _entries.Clear();
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
                _entries.Remove(key);
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
    }
}
