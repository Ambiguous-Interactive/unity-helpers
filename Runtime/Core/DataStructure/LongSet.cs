// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>Stores all unsigned 64-bit keys using mixed open addressing.</summary>
    /// <remarks>Shares the packed-entry map implementation. Not thread-safe; iteration is unordered and detects mutation.</remarks>
    public sealed class LongSet : IEnumerable<ulong>
    {
        /// <summary>Gets the number of stored keys.</summary>
        public int Count => _map.Count;

        /// <summary>Gets whether no keys are stored.</summary>
        public bool IsEmpty => _map.IsEmpty;

        /// <summary>Gets the number of hash slots.</summary>
        public int Capacity => _map.Capacity;

        private readonly LongMap<byte> _map;

        /// <summary>Creates a set with room for eight keys before growth.</summary>
        public LongSet()
            : this(8) { }

        /// <summary>Creates a set with the requested live-key capacity.</summary>
        public LongSet(int initialCapacity)
        {
            _map = new LongMap<byte>(initialCapacity);
        }

        /// <summary>Adds a key, returning false when it is already present or maximum capacity is reached.</summary>
        public bool TryAdd(ulong key)
        {
            return _map.TryAdd(key, 0);
        }

        /// <summary>Checks whether the key is present.</summary>
        public bool Contains(ulong key)
        {
            return _map.ContainsKey(key);
        }

        /// <summary>Removes a key, returning false when absent.</summary>
        public bool Remove(ulong key)
        {
            return _map.Remove(key, out _);
        }

        /// <summary>Clears keys in O(Count), retaining capacity.</summary>
        public void Clear()
        {
            _map.Clear();
        }

        /// <summary>Returns a value-type traversal of stored keys.</summary>
        public LongMap<byte>.KeyEnumerator GetEnumerator()
        {
            return _map.Keys.GetEnumerator();
        }

        IEnumerator<ulong> IEnumerable<ulong>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
