// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>Stores all unsigned 64-bit keys using mixed open addressing.</summary>
    /// <remarks>Uses packed keys and hash-slot indices without value storage. Not thread-safe; iteration is unordered and detects mutation.</remarks>
    public sealed class LongSet : IEnumerable<ulong>
    {
        /// <summary>Gets the largest supported live-key capacity.</summary>
        public const int MaximumCapacity = LongMap<byte>.MaximumCapacity;

        private const int MinimumSlotCount = 16;

        /// <summary>Gets the number of stored keys.</summary>
        public int Count => _count;

        /// <summary>Gets whether no keys are stored.</summary>
        public bool IsEmpty => _count == 0;

        /// <summary>Gets the number of hash slots, twice the live-key capacity.</summary>
        public int Capacity => _slots.Length;

        private ulong[] _keys;
        private int[] _slots;
        private int _mask;
        private int _count;
        private int _version;

        /// <summary>Creates a set with room for eight keys before growth.</summary>
        public LongSet()
            : this(8) { }

        /// <summary>Creates a set with the requested live-key capacity.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The capacity is negative or exceeds MaximumCapacity.</exception>
        public LongSet(int initialCapacity)
        {
            if (initialCapacity < 0 || MaximumCapacity < initialCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }
            int slots = MinimumSlotCount;
            while (slots / 2 < initialCapacity)
            {
                slots *= 2;
            }
            _keys = new ulong[slots / 2];
            _slots = new int[slots];
            _mask = slots - 1;
        }

        /// <summary>Adds a key, returning false when it is already present or maximum capacity is reached.</summary>
        public bool TryAdd(ulong key)
        {
            int slot = FindSlot(key);
            if (_slots[slot] != 0)
            {
                return false;
            }
            int count = _count;
            if (count == _keys.Length)
            {
                if (count == MaximumCapacity)
                {
                    return false;
                }
                Grow();
                slot = FindSlot(key);
            }
            _keys[count] = key;
            _slots[slot] = count + 1;
            _count = count + 1;
            _version = unchecked(_version + 1);
            return true;
        }

        /// <summary>Checks whether the key is present.</summary>
        public bool Contains(ulong key)
        {
            return _slots[FindSlot(key)] != 0;
        }

        /// <summary>Removes a key, returning false when absent.</summary>
        public bool Remove(ulong key)
        {
            int slot = FindSlot(key);
            int entry = _slots[slot] - 1;
            if (entry < 0)
            {
                return false;
            }
            int last = _count - 1;
            if (entry != last)
            {
                ulong movedKey = _keys[last];
                int movedSlot = FindSlot(movedKey);
                _keys[entry] = movedKey;
                _slots[movedSlot] = entry + 1;
            }
            CloseGap(slot);
            _keys[last] = 0;
            _count = last;
            _version = unchecked(_version + 1);
            return true;
        }

        /// <summary>Clears live keys and their probe paths, retaining capacity.</summary>
        public void Clear()
        {
            int count = _count;
            if (count == 0)
            {
                return;
            }
            for (int entry = 0; entry < count; ++entry)
            {
                int slot = (int)(LongMap<byte>.Mix(_keys[entry]) & (ulong)_mask);
                while (_slots[slot] != entry + 1)
                {
                    slot = (slot + 1) & _mask;
                }
                _slots[slot] = 0;
                _keys[entry] = 0;
            }
            _count = 0;
            _version = unchecked(_version + 1);
        }

        /// <summary>Returns a value-type traversal of stored keys.</summary>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        private int FindSlot(ulong key)
        {
            int slot = (int)(LongMap<byte>.Mix(key) & (ulong)_mask);
            while (_slots[slot] != 0 && _keys[_slots[slot] - 1] != key)
            {
                slot = (slot + 1) & _mask;
            }
            return slot;
        }

        private void Grow()
        {
            int slotCount = _slots.Length * 2;
            ulong[] keys = new ulong[slotCount / 2];
            int[] slots = new int[slotCount];
            int mask = slotCount - 1;
            int count = _count;
            Array.Copy(_keys, keys, count);
            for (int entry = 0; entry < count; ++entry)
            {
                int slot = (int)(LongMap<byte>.Mix(keys[entry]) & (ulong)mask);
                while (slots[slot] != 0)
                {
                    slot = (slot + 1) & mask;
                }
                slots[slot] = entry + 1;
            }
            _keys = keys;
            _slots = slots;
            _mask = mask;
        }

        private void CloseGap(int gap)
        {
            int slot = (gap + 1) & _mask;
            while (_slots[slot] != 0)
            {
                int entry = _slots[slot] - 1;
                int home = (int)(LongMap<byte>.Mix(_keys[entry]) & (ulong)_mask);
                if (((gap - home) & _mask) < ((slot - home) & _mask))
                {
                    _slots[gap] = _slots[slot];
                    gap = slot;
                }
                slot = (slot + 1) & _mask;
            }
            _slots[gap] = 0;
        }

        IEnumerator<ulong> IEnumerable<ulong>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>Traverses packed keys and detects set mutations.</summary>
        public struct Enumerator : IEnumerator<ulong>, IEquatable<Enumerator>
        {
            /// <summary>Gets the current key.</summary>
            public ulong Current { get; private set; }

            object IEnumerator.Current => Current;

            private readonly LongSet _owner;
            private readonly int _version;
            private int _index;

            internal Enumerator(LongSet owner)
            {
                _owner = owner;
                _version = owner?._version ?? 0;
                _index = 0;
                Current = default;
            }

            /// <summary>Advances to the next live key.</summary>
            public bool MoveNext()
            {
                ValidateVersion();
                if (_owner != null && _index < _owner._count)
                {
                    Current = _owner._keys[_index++];
                    return true;
                }
                _index = (_owner?._count ?? 0) + 1;
                Current = default;
                return false;
            }

            /// <summary>Restarts an unmodified traversal.</summary>
            public void Reset()
            {
                ValidateVersion();
                _index = 0;
                Current = default;
            }

            /// <summary>Completes traversal without owning set storage.</summary>
            public void Dispose() { }

            /// <summary>Compares traversal states.</summary>
            public bool Equals(Enumerator other)
            {
                return ReferenceEquals(_owner, other._owner)
                    && _version == other._version
                    && _index == other._index;
            }

            /// <summary>Compares traversal states.</summary>
            public override bool Equals(object obj)
            {
                return obj is Enumerator other && Equals(other);
            }

            /// <summary>Hashes the traversal state.</summary>
            public override int GetHashCode()
            {
                return HashCode.Combine(_owner, _version, _index);
            }

            private void ValidateVersion()
            {
                if (_owner != null && _owner._version != _version)
                {
                    throw new InvalidOperationException("The set changed during enumeration.");
                }
            }
        }
    }
}
