// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    /// An int-keyed open-addressing hash map built for read-mostly lookups, where a
    /// <see cref="Dictionary{TKey, TValue}"/> pays for an interface it never needed.
    /// </summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <remarks>
    /// <para>
    /// Measured against <c>Dictionary&lt;int,int&gt;</c> on Unity 6000.4.6f1 editor Mono,
    /// counterbalanced ABBABAAB with a settled heap per slot and both sides calling TryGetValue:
    /// hit-heavy lookups ran 1.97x-2.19x faster depending on entry count, easing toward 1.26x as
    /// the miss rate climbed toward fifty percent. A miss walks occupied slots until it reaches an
    /// untouched slot, so linear probing spends on every miss whatever it saved on the hits.
    /// </para>
    /// <para>
    /// Two structural choices carry the win, so neither is negotiable inside this type. Keys are
    /// compared as raw integers rather than routed through <see cref="IEqualityComparer{T}"/> --
    /// Unity's Mono does not devirtualize that interface call away, and removing it is part of what
    /// is being beaten. Table length stays a power of two, so slot position is one multiply and one
    /// AND instead of a division, and doubling is the only size change there is.
    /// </para>
    /// <para>
    /// The table stores keys verbatim beside their values, and the two lowest key values name the
    /// slot states instead of callers' data; see <see cref="MinimumAllowedKey"/>. A workload whose
    /// misses outnumber its hits should stay on <see cref="Dictionary{TKey, TValue}"/>.
    /// </para>
    /// </remarks>
    public sealed class IntMap<TValue>
        : IEnumerable<KeyValuePair<int, TValue>>,
            IReadOnlyCollection<KeyValuePair<int, TValue>>
    {
        /// <summary>The smallest key a caller may store; everything lower names a slot state.</summary>
        public const int MinimumAllowedKey = int.MinValue + 2;

        private const int DefaultInitialCapacity = 16;
        private const int MinimumTablePower = 3;
        private const int MaximumTablePower = 30;

        // Golden-ratio multiplier. Consecutive integer ids land far apart before masking, which is
        // what keeps a clustered id range from turning its neighborhood into one long probe run.
        private const uint KeyMultiplier = 0x9E37_79B9u;

        // Slot states rather than caller data. Stored keys are live iff MinimumAllowedKey <= them.
        private const int EmptySlot = int.MinValue;
        private const int TombstoneSlot = int.MinValue + 1;

        private int[] _keys;
        private TValue[] _values;
        private int _mask;
        private int _count;
        private int _tombstones;
        private ulong _version;

        /// <summary>
        /// Initializes an empty map sized for sixteen live entries before its first resize.
        /// </summary>
        public IntMap()
            : this(DefaultInitialCapacity) { }

        /// <summary>
        /// Initializes an empty map able to hold <paramref name="initialCapacity"/> entries without
        /// resizing.
        /// </summary>
        /// <param name="initialCapacity">How many live entries to make room for.</param>
        /// <remarks>
        /// Powers of two are honored directly; anything else rounds up, because a non-power-of-two
        /// table would hand the mask's job back to division -- the cost this map exists to shed.
        /// </remarks>
        public IntMap(int initialCapacity)
        {
            if (initialCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialCapacity),
                    "Capacity must be positive."
                );
            }

            Rebuild(SmallestSufficientPower(initialCapacity));
        }

        /// <summary>Gets how many live entries the map holds.</summary>
        public int Count => _count;

        /// <summary>Gets whether the map holds no entries.</summary>
        public bool IsEmpty => _count == 0;

        /// <summary>Gets the number of slots in the underlying table.</summary>
        public int Capacity => _keys.Length;

        /// <summary>
        /// Gets the value stored under <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <exception cref="KeyNotFoundException">Thrown when the key is absent.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="key"/> falls below <see cref="MinimumAllowedKey"/>.
        /// </exception>
        public TValue this[int key]
        {
            get
            {
                Validate(key);
                int slot = FindSlot(key);
                if (_keys[slot] == key)
                {
                    return _values[slot];
                }

                throw new KeyNotFoundException($"No value is stored under {key}.");
            }
            set => SetInternal(Validate(key), value);
        }

        /// <summary>
        /// Stores <paramref name="value"/> under <paramref name="key"/>, replacing any prior value.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <returns>
        /// <c>true</c> when written; <c>false</c> when the key is a reserved marker, in which case
        /// nothing was stored.
        /// </returns>
        public bool TrySet(int key, TValue value)
        {
            if (key < MinimumAllowedKey)
            {
                return false;
            }

            SetInternal(key, value);
            return true;
        }

        /// <summary>
        /// Gets the value stored under <paramref name="key"/>, if present.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">Receives the stored value, or the type's default on absence.</param>
        /// <returns><c>true</c> when the key is present.</returns>
        public bool TryGet(int key, out TValue value)
        {
            if (key < MinimumAllowedKey || _count == 0)
            {
                value = default(TValue);
                return false;
            }

            int slot = FindSlot(key);
            if (_keys[slot] == key)
            {
                value = _values[slot];
                return true;
            }

            value = default(TValue);
            return false;
        }

        /// <summary>
        /// Removes <paramref name="key"/> and its value.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">Receives the removed value, or the type's default on absence.</param>
        /// <returns><c>true</c> when the key was present and is now gone.</returns>
        /// <remarks>
        /// A probe chain reads slots until an empty one proves the key absent, so removal marks the
        /// slot as a tombstone rather than releasing it: closing the gap would sever the tail of its
        /// own chain. Tombstones join live entries against the load factor, so a workload dominated
        /// by removals keeps probing pay for its churn -- and periodic growth compacts them away.
        /// </remarks>
        public bool Remove(int key, out TValue value)
        {
            if (key < MinimumAllowedKey || _count == 0)
            {
                value = default(TValue);
                return false;
            }

            int slot = FindSlot(key);
            if (_keys[slot] != key)
            {
                value = default(TValue);
                return false;
            }

            TValue removed = _values[slot];
            _values[slot] = default(TValue);
            _keys[slot] = TombstoneSlot;
            --_count;
            ++_tombstones;
            ++_version;
            value = removed;
            return true;
        }

        /// <summary>Drops every entry, keeping the current table.</summary>
        public void Clear()
        {
            if (_count != 0 || _tombstones != 0)
            {
                Array.Clear(_keys, 0, _keys.Length);
                Array.Clear(_values, 0, _values.Length);
            }

            _count = 0;
            _tombstones = 0;
            ++_version;
        }

        /// <inheritdoc />
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator<KeyValuePair<int, TValue>> IEnumerable<
            KeyValuePair<int, TValue>
        >.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private void SetInternal(int key, TValue value)
        {
            // The load factor is one half: once live entries plus tombstones reach half the table,
            // grow before inserting so a full sweep never becomes the norm on any path.
            if (_keys.Length <= (_count + _tombstones) * 2)
            {
                Resize();
            }

            uint hash = Mix(key);

            int slot = SlotFor(hash);
            int reuseSlot = -1;
            while (true)
            {
                int stored = _keys[slot];
                if (stored == key)
                {
                    _values[slot] = value;
                    ++_version;
                    return;
                }

                if (stored == TombstoneSlot)
                {
                    if (reuseSlot < 0)
                    {
                        reuseSlot = slot;
                    }
                }
                else if (stored == EmptySlot)
                {
                    Fill(slot, key, value, reuseSlot);
                    return;
                }

                slot = NextSlot(slot);
            }
        }

        private void Fill(int emptySlot, int key, TValue value, int reuseSlot)
        {
            int target = reuseSlot < 0 ? emptySlot : reuseSlot;
            if (reuseSlot < 0)
            {
                ++_count;
            }
            else
            {
                --_tombstones;
                ++_count;
            }

            _keys[target] = key;
            _values[target] = value;
            ++_version;
        }

        private int FindSlot(int key)
        {
            int slot = SlotFor(Mix(key));
            while (true)
            {
                int stored = _keys[slot];
                if (stored == EmptySlot || stored == key)
                {
                    return slot;
                }

                slot = NextSlot(slot);
            }
        }

        private void Resize()
        {
            // Growth stays monotone: rehashing survivors into a same-size table whenever a few
            // tombstones appear makes the resize cost data-dependent, and doubling until survivors
            // rest under the load factor clears tombstones wholesale with one rule and no knobs.
            int nextPower = SmallestSufficientPower(_count);
            int currentPower = PowerOf(_keys.Length);
            if (nextPower <= currentPower)
            {
                nextPower = currentPower + 1;
            }

            if (MaximumTablePower < nextPower)
            {
                throw new InvalidOperationException("This map cannot grow past its largest table.");
            }

            Rebuild(nextPower);
        }

        private void Rebuild(int power)
        {
            int capacity = 1 << power;
            int[] oldKeys = _keys;
            TValue[] oldValues = _values;

            _keys = new int[capacity];
            _values = new TValue[capacity];
            _mask = capacity - 1;
            _count = 0;
            _tombstones = 0;
            ++_version;
            for (int index = 0; index < capacity; ++index)
            {
                _keys[index] = EmptySlot;
            }

            if (oldKeys == null)
            {
                return;
            }

            for (int index = 0; index < oldKeys.Length; ++index)
            {
                int stored = oldKeys[index];
                if (MinimumAllowedKey <= stored)
                {
                    int slot = SlotFor(Mix(stored));
                    while (_keys[slot] != EmptySlot)
                    {
                        slot = NextSlot(slot);
                    }

                    _keys[slot] = stored;
                    _values[slot] = oldValues[index];
                    ++_count;
                }
            }
        }

        private int Validate(int key)
        {
            if (key < MinimumAllowedKey)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(key),
                    $"{key} falls below {nameof(MinimumAllowedKey)}, the smallest storable key."
                );
            }

            return key;
        }

        private int SlotFor(uint hash)
        {
            return (int)(hash & _mask);
        }

        private int NextSlot(int slot)
        {
            return (slot + 1) & _mask;
        }

        private static uint Mix(int key)
        {
            return unchecked((uint)key * KeyMultiplier);
        }

        private static int SmallestSufficientPower(int capacityHint)
        {
            // The smallest power whose half exceeds the hint, so the hint itself rests below the
            // load factor and the first insert never triggers a resize.
            int power = MinimumTablePower;
            while (0 < capacityHint && (long)(1 << power) / 2 <= (long)capacityHint)
            {
                ++power;
            }

            return power;
        }

        private static int PowerOf(int capacity)
        {
            int power = MinimumTablePower;
            while (1 << power < capacity)
            {
                ++power;
            }

            return power;
        }

        /// <summary>Enumerates live key-value pairs in table order.</summary>
        public struct Enumerator : IEnumerator<KeyValuePair<int, TValue>>
        {
            private readonly IntMap<TValue> _map;
            private readonly ulong _version;
            private int _slot;
            private KeyValuePair<int, TValue> _current;

            internal Enumerator(IntMap<TValue> map)
            {
                _map = map;
                _version = map._version;
                _slot = 0;
                _current = default(KeyValuePair<int, TValue>);
            }

            /// <inheritdoc />
            public KeyValuePair<int, TValue> Current => _current;

            object IEnumerator.Current => _current;

            /// <inheritdoc />
            public bool MoveNext()
            {
                if (_version != _map._version)
                {
                    throw new InvalidOperationException("The map changed during enumeration.");
                }

                while (_slot < _map._keys.Length)
                {
                    int stored = _map._keys[_slot];
                    ++_slot;
                    if (MinimumAllowedKey <= stored)
                    {
                        _current = new KeyValuePair<int, TValue>(stored, _map._values[_slot - 1]);
                        return true;
                    }
                }

                return false;
            }

            /// <inheritdoc />
            public void Reset()
            {
                _slot = 0;
                _current = default(KeyValuePair<int, TValue>);
            }

            /// <inheritdoc />
            public void Dispose() { }
        }
    }
}
