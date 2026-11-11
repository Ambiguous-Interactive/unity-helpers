// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;

    /// <summary>Maps all 64-bit unsigned keys using mixed open addressing and packed live entries.</summary>
    /// <remarks>Not thread-safe. Enumeration is O(Count), has no ordering guarantee and fails on mutation. Removal and clear release stored value references.</remarks>
    public sealed class LongMap<TValue> : IReadOnlyDictionary<ulong, TValue>
    {
        /// <summary>Gets the largest supported live-entry capacity.</summary>
        public const int MaximumCapacity = 1 << 29;

        private const int MinimumSlotCount = 16;

        /// <summary>Gets the number of stored entries.</summary>
        public int Count => _count;

        /// <summary>Gets whether no entries are stored.</summary>
        public bool IsEmpty => _count == 0;

        /// <summary>Gets the number of hash slots, twice the live-entry capacity.</summary>
        public int Capacity => _slots.Length;

        /// <summary>Gets or sets an entry; a missing read throws KeyNotFoundException.</summary>
        public TValue this[ulong key]
        {
            get
            {
                if (TryGet(key, out TValue value))
                {
                    return value;
                }
                throw new KeyNotFoundException($"No value is stored under {key}.");
            }
            set
            {
                if (!TrySet(key, value))
                {
                    throw new InvalidOperationException("The map reached its maximum capacity.");
                }
            }
        }

        /// <summary>Gets a value-type view of stored keys.</summary>
        public KeyView Keys => new(this);

        /// <summary>Gets a value-type view of stored values.</summary>
        public ValueView Values => new(this);

        IEnumerable<ulong> IReadOnlyDictionary<ulong, TValue>.Keys => Keys;
        IEnumerable<TValue> IReadOnlyDictionary<ulong, TValue>.Values => Values;

        private ulong[] _keys;
        private TValue[] _values;
        private int[] _slots;
        private int[] _entrySlots;
        private int _mask;
        private int _count;
        private int _version;

        /// <summary>Creates a map with room for eight entries before growth.</summary>
        public LongMap()
            : this(8) { }

        /// <summary>Creates a map with room for at least the requested live-entry count.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The requested capacity is negative or exceeds MaximumCapacity.</exception>
        public LongMap(int initialCapacity)
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
            _values = new TValue[slots / 2];
            _entrySlots = new int[slots / 2];
            _slots = new int[slots];
            _mask = slots - 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong Mix(ulong key)
        {
            unchecked
            {
                key = (key ^ (key >> 30)) * 0xBF58476D1CE4E5B9UL;
                key = (key ^ (key >> 27)) * 0x94D049BB133111EBUL;
                return key ^ (key >> 31);
            }
        }

        /// <summary>Inserts or replaces a value, returning false only at the maximum live-entry capacity.</summary>
        public bool TrySet(ulong key, TValue value)
        {
            return Insert(key, value, overwrite: true);
        }

        /// <summary>Inserts an absent key, returning false for an existing key or a full maximum-capacity map.</summary>
        public bool TryAdd(ulong key, TValue value)
        {
            return Insert(key, value, overwrite: false);
        }

        /// <summary>Reads a value, returning false and default for an absent key.</summary>
        public bool TryGet(ulong key, out TValue value)
        {
            int entry = _slots[FindSlot(key)] - 1;
            if (entry < 0)
            {
                value = default;
                return false;
            }
            value = _values[entry];
            return true;
        }

        /// <summary>Checks whether the key is present.</summary>
        public bool ContainsKey(ulong key)
        {
            return _slots[FindSlot(key)] != 0;
        }

        /// <summary>Removes a key, returning its value or default when absent.</summary>
        public bool Remove(ulong key, out TValue value)
        {
            int slot = FindSlot(key);
            int entry = _slots[slot] - 1;
            if (entry < 0)
            {
                value = default;
                return false;
            }
            TValue removed = _values[entry];
            int last = _count - 1;
            if (entry != last)
            {
                _keys[entry] = _keys[last];
                _values[entry] = _values[last];
                int movedSlot = _entrySlots[last];
                _slots[movedSlot] = entry + 1;
                _entrySlots[entry] = movedSlot;
            }
            _keys[last] = 0;
            _values[last] = default;
            _entrySlots[last] = 0;
            CloseGap(slot);
            --_count;
            _version = unchecked(_version + 1);
            value = removed;
            return true;
        }

        /// <summary>Removes all entries in O(Count), retaining capacity for reuse.</summary>
        public void Clear()
        {
            if (_count == 0)
            {
                return;
            }
            for (int entry = 0; entry < _count; ++entry)
            {
                _slots[_entrySlots[entry]] = 0;
                _entrySlots[entry] = 0;
                _keys[entry] = 0;
                _values[entry] = default;
            }
            _count = 0;
            _version = unchecked(_version + 1);
        }

        /// <summary>Returns a value-type enumerator of live entries.</summary>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        private int FindSlot(ulong key)
        {
            int slot = (int)(Mix(key) & (ulong)_mask);
            while (_slots[slot] != 0 && _keys[_slots[slot] - 1] != key)
            {
                slot = (slot + 1) & _mask;
            }
            return slot;
        }

        private bool Insert(ulong key, TValue value, bool overwrite)
        {
            int slot = FindSlot(key);
            int entry = _slots[slot] - 1;
            if (0 <= entry)
            {
                if (!overwrite)
                {
                    return false;
                }
                _values[entry] = value;
                _version = unchecked(_version + 1);
                return true;
            }
            if (_count == _keys.Length)
            {
                if (_count == MaximumCapacity)
                {
                    return false;
                }
                Grow();
                slot = FindSlot(key);
            }
            _keys[_count] = key;
            _values[_count] = value;
            _entrySlots[_count] = slot;
            _slots[slot] = _count + 1;
            ++_count;
            _version = unchecked(_version + 1);
            return true;
        }

        private void Grow()
        {
            int slotCount = _slots.Length * 2;
            ulong[] keys = new ulong[slotCount / 2];
            TValue[] values = new TValue[slotCount / 2];
            int[] entrySlots = new int[slotCount / 2];
            int[] slots = new int[slotCount];
            int mask = slotCount - 1;
            Array.Copy(_keys, keys, _count);
            Array.Copy(_values, values, _count);
            for (int entry = 0; entry < _count; ++entry)
            {
                int slot = (int)(Mix(keys[entry]) & (ulong)mask);
                while (slots[slot] != 0)
                {
                    slot = (slot + 1) & mask;
                }
                slots[slot] = entry + 1;
                entrySlots[entry] = slot;
            }
            _keys = keys;
            _values = values;
            _entrySlots = entrySlots;
            _slots = slots;
            _mask = mask;
        }

        private void CloseGap(int gap)
        {
            int slot = (gap + 1) & _mask;
            while (_slots[slot] != 0)
            {
                int entry = _slots[slot] - 1;
                int home = (int)(Mix(_keys[entry]) & (ulong)_mask);
                if (((gap - home) & _mask) < ((slot - home) & _mask))
                {
                    _slots[gap] = _slots[slot];
                    _entrySlots[entry] = gap;
                    gap = slot;
                }
                slot = (slot + 1) & _mask;
            }
            _slots[gap] = 0;
        }

        IEnumerator<KeyValuePair<ulong, TValue>> IEnumerable<
            KeyValuePair<ulong, TValue>
        >.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        bool IReadOnlyDictionary<ulong, TValue>.TryGetValue(ulong key, out TValue value)
        {
            return TryGet(key, out value);
        }

        /// <summary>Traverses packed live entries and detects map mutations.</summary>
        public struct Enumerator : IEnumerator<KeyValuePair<ulong, TValue>>, IEquatable<Enumerator>
        {
            /// <summary>Gets the current pair.</summary>
            public KeyValuePair<ulong, TValue> Current { get; private set; }
            object IEnumerator.Current => Current;

            private readonly LongMap<TValue> _owner;
            private readonly int _version;
            private int _index;

            internal Enumerator(LongMap<TValue> owner)
            {
                _owner = owner;
                _version = owner?._version ?? 0;
                _index = 0;
                Current = default;
            }

            /// <summary>Advances to the next live entry.</summary>
            public bool MoveNext()
            {
                ValidateVersion();
                if (_owner != null && _index < _owner._count)
                {
                    Current = new KeyValuePair<ulong, TValue>(
                        _owner._keys[_index],
                        _owner._values[_index]
                    );
                    ++_index;
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

            /// <summary>Completes the traversal without owning map storage.</summary>
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
                    throw new InvalidOperationException("The map changed during enumeration.");
                }
            }
        }

        /// <summary>Provides a value-type view of the map's keys.</summary>
        public readonly struct KeyView : IEnumerable<ulong>, IEquatable<KeyView>
        {
            private readonly LongMap<TValue> _owner;

            internal KeyView(LongMap<TValue> owner)
            {
                _owner = owner;
            }

            /// <summary>Returns a value-type traversal.</summary>
            public KeyEnumerator GetEnumerator()
            {
                return new KeyEnumerator(new Enumerator(_owner));
            }

            /// <summary>Compares views by their owner.</summary>
            public bool Equals(KeyView other)
            {
                return ReferenceEquals(_owner, other._owner);
            }

            /// <summary>Compares views by their owner.</summary>
            public override bool Equals(object obj)
            {
                return obj is KeyView other && Equals(other);
            }

            /// <summary>Hashes the owner identity.</summary>
            public override int GetHashCode()
            {
                return _owner?.GetHashCode() ?? 0;
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

        /// <summary>Traverses live keys and detects mutations.</summary>
        public struct KeyEnumerator : IEnumerator<ulong>, IEquatable<KeyEnumerator>
        {
            /// <summary>Gets the current key.</summary>
            public ulong Current => _inner.Current.Key;
            object IEnumerator.Current => Current;

            private Enumerator _inner;

            internal KeyEnumerator(Enumerator inner)
            {
                _inner = inner;
            }

            /// <summary>Advances to the next key.</summary>
            public bool MoveNext()
            {
                return _inner.MoveNext();
            }

            /// <summary>Restarts an unmodified traversal.</summary>
            public void Reset()
            {
                _inner.Reset();
            }

            /// <summary>Completes the traversal without owning storage.</summary>
            public void Dispose()
            {
                _inner.Dispose();
            }

            /// <summary>Compares traversal states.</summary>
            public bool Equals(KeyEnumerator other)
            {
                return _inner.Equals(other._inner);
            }

            /// <summary>Compares traversal states.</summary>
            public override bool Equals(object obj)
            {
                return obj is KeyEnumerator other && Equals(other);
            }

            /// <summary>Hashes the traversal state.</summary>
            public override int GetHashCode()
            {
                return _inner.GetHashCode();
            }
        }

        /// <summary>Provides a value-type view of the map's values.</summary>
        public readonly struct ValueView : IEnumerable<TValue>, IEquatable<ValueView>
        {
            private readonly LongMap<TValue> _owner;

            internal ValueView(LongMap<TValue> owner)
            {
                _owner = owner;
            }

            /// <summary>Returns a value-type traversal.</summary>
            public ValueEnumerator GetEnumerator()
            {
                return new ValueEnumerator(new Enumerator(_owner));
            }

            /// <summary>Compares views by their owner.</summary>
            public bool Equals(ValueView other)
            {
                return ReferenceEquals(_owner, other._owner);
            }

            /// <summary>Compares views by their owner.</summary>
            public override bool Equals(object obj)
            {
                return obj is ValueView other && Equals(other);
            }

            /// <summary>Hashes the owner identity.</summary>
            public override int GetHashCode()
            {
                return _owner?.GetHashCode() ?? 0;
            }

            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()
            {
                return GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        /// <summary>Traverses live values and detects mutations.</summary>
        public struct ValueEnumerator : IEnumerator<TValue>, IEquatable<ValueEnumerator>
        {
            /// <summary>Gets the current value.</summary>
            public TValue Current => _inner.Current.Value;
            object IEnumerator.Current => Current;

            private Enumerator _inner;

            internal ValueEnumerator(Enumerator inner)
            {
                _inner = inner;
            }

            /// <summary>Advances to the next value.</summary>
            public bool MoveNext()
            {
                return _inner.MoveNext();
            }

            /// <summary>Restarts an unmodified traversal.</summary>
            public void Reset()
            {
                _inner.Reset();
            }

            /// <summary>Completes the traversal without owning storage.</summary>
            public void Dispose()
            {
                _inner.Dispose();
            }

            /// <summary>Compares traversal states.</summary>
            public bool Equals(ValueEnumerator other)
            {
                return _inner.Equals(other._inner);
            }

            /// <summary>Compares traversal states.</summary>
            public override bool Equals(object obj)
            {
                return obj is ValueEnumerator other && Equals(other);
            }

            /// <summary>Hashes the traversal state.</summary>
            public override int GetHashCode()
            {
                return _inner.GetHashCode();
            }
        }
    }
}
