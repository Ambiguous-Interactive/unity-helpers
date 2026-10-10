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
    /// Benchmark this map against <c>Dictionary&lt;int,int&gt;</c> for the caller's key distribution
    /// and miss rate. Earlier editor Mono measurements used the previous low-bit slot selection
    /// and do not establish a margin for this implementation. A miss walks occupied slots until
    /// it reaches an untouched slot; shared-low-bit keys therefore need their own controls.
    /// </para>
    /// <para>
    /// Keys are
    /// compared as raw integers rather than routed through <see cref="IEqualityComparer{T}"/>.
    /// Table length stays a power of two, so slot position is one multiply and one
    /// shift instead of a division. Growth doubles the table only when live entries reach the load limit.
    /// Removal closes probe-chain gaps in place without allocating replacement storage.
    /// </para>
    /// <para>
    /// The table stores keys verbatim beside their values, and the two lowest key values remain
    /// reserved; see <see cref="MinimumAllowedKey"/>. Include miss-heavy
    /// workloads when benchmarking against <see cref="Dictionary{TKey, TValue}"/>.
    /// </para>
    /// </remarks>
    public sealed class IntMap<TValue>
        : IReadOnlyDictionary<int, TValue>,
            IEnumerable<KeyValuePair<int, TValue>>
    {
        /// <summary>The smallest key a caller may store; everything lower is reserved.</summary>
        public const int MinimumAllowedKey = int.MinValue + 2;

        private const int DefaultInitialCapacity = 16;
        private const int MinimumTablePower = 3;
        private const int MaximumTablePower = 30;

        // Fibonacci hashing uses the high product bits so shared low key bits do not form a cluster.
        private const uint KeyMultiplier = 0x9E37_79B9u;

        private const int EmptySlot = int.MinValue;

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

        /// <summary>Gets the stored keys, in table order.</summary>
        /// <remarks>
        /// Returns the concrete <see cref="KeyView"/> rather than <see cref="IEnumerable{T}"/> so
        /// typed <c>foreach</c> binds the struct enumerator directly and allocates nothing; the
        /// <c>IReadOnlyDictionary</c> surface reaches this same view through its explicit
        /// interface implementation.
        /// </remarks>
        public KeyView Keys => new KeyView(this);

        /// <summary>Gets the stored values, in table order.</summary>
        /// <remarks>
        /// Returns the concrete <see cref="ValueView"/> for the same reason <see cref="Keys"/>
        /// does: typed <c>foreach</c> must reach the struct enumerator without boxing.
        /// </remarks>
        public ValueView Values => new ValueView(this);

        IEnumerable<int> IReadOnlyDictionary<int, TValue>.Keys => Keys;

        IEnumerable<TValue> IReadOnlyDictionary<int, TValue>.Values => Values;

        private int[] _keys;
        private TValue[] _values;
        private int _mask;
        private int _shift;
        private int _count;
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
        /// table would require division instead of a shift to choose the starting slot.
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

            int power = SmallestSufficientPower(initialCapacity);
            if ((1L << power) / 2 < initialCapacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialCapacity),
                    "Requested capacity exceeds what an int-keyed table can address."
                );
            }

            Rebuild(power);
        }

        private static uint Mix(int key)
        {
            return unchecked((uint)key * KeyMultiplier);
        }

        private static int SmallestSufficientPower(int capacityHint)
        {
            // Bound the power-of-two search before a 32-bit shift can wrap.
            int power = MinimumTablePower;
            while (
                power < MaximumTablePower
                && 0 < (long)capacityHint
                && (long)(1 << power) / 2 <= (long)capacityHint
            )
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

        /// <summary>
        /// Stores <paramref name="value"/> under <paramref name="key"/>, replacing any prior value.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <returns>
        /// <c>true</c> when written; <c>false</c> when the key is reserved, in which case
        /// nothing was stored.
        /// </returns>
        /// <remarks>Replacing an existing value preserves the table storage, even at the occupancy limit.</remarks>
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
        /// Removal shifts displaced entries whose probe paths cross the deleted slot, closing
        /// the gap without allocating or rebuilding the table. Other keys remain reachable,
        /// including probe chains that wrap around the end of the table.
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
            CloseGap(slot);
            --_count;
            ++_version;
            value = removed;
            return true;
        }

        /// <summary>
        /// Reports whether <paramref name="key"/> is currently stored.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns><c>false</c> when absent or below <see cref="MinimumAllowedKey"/>.</returns>
        public bool ContainsKey(int key)
        {
            return TryGet(key, out _);
        }

        /// <summary>Drops every entry, keeping the current table.</summary>
        /// <remarks>
        /// The keys array holds slot-state markers even while idle, so an array-wide zero fill would
        /// read back as tens of thousands of live key-zero entries. Every slot goes back to
        /// <see cref="EmptySlot"/> explicitly.
        /// </remarks>
        public void Clear()
        {
            if (_count != 0)
            {
                int keysLength = _keys.Length;
                for (int index = 0; index < keysLength; ++index)
                {
                    _keys[index] = EmptySlot;
                }

                Array.Clear(_values, 0, _values.Length);
            }

            _count = 0;
            ++_version;
        }

        /// <inheritdoc />
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        private void SetInternal(int key, TValue value)
        {
            int slot = SlotFor(Mix(key));
            while (true)
            {
                int stored = _keys[slot];
                if (stored == key)
                {
                    _values[slot] = value;
                    ++_version;
                    return;
                }

                if (stored == EmptySlot)
                {
                    if (_keys.Length <= _count * 2)
                    {
                        Resize();
                        SetInternal(key, value);
                        return;
                    }

                    _keys[slot] = key;
                    _values[slot] = value;
                    ++_count;
                    ++_version;
                    return;
                }

                slot = NextSlot(slot);
            }
        }

        private void CloseGap(int gap)
        {
            int slot = NextSlot(gap);
            while (_keys[slot] != EmptySlot)
            {
                int home = SlotFor(Mix(_keys[slot]));
                if (((gap - home) & _mask) < ((slot - home) & _mask))
                {
                    _keys[gap] = _keys[slot];
                    _values[gap] = _values[slot];
                    gap = slot;
                }
                slot = NextSlot(slot);
            }

            _keys[gap] = EmptySlot;
            _values[gap] = default(TValue);
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
            int currentPower = PowerOf(_keys.Length);
            int nextPower = currentPower + 1;

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
            _shift = 32 - power;
            _count = 0;
            ++_version;
            for (int index = 0; index < capacity; ++index)
            {
                _keys[index] = EmptySlot;
            }

            if (oldKeys == null)
            {
                return;
            }

            int oldKeysLength = oldKeys.Length;
            for (int index = 0; index < oldKeysLength; ++index)
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
            return (int)(hash >> _shift);
        }

        private int NextSlot(int slot)
        {
            return (slot + 1) & _mask;
        }

        bool IReadOnlyDictionary<int, TValue>.TryGetValue(int key, out TValue value)
        {
            return TryGet(key, out value);
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

        /// <summary>Enumerates live key-value pairs in table order.</summary>
        public struct Enumerator : IEnumerator<KeyValuePair<int, TValue>>
        {
            /// <inheritdoc />
            public KeyValuePair<int, TValue> Current => _current;

            object IEnumerator.Current => _current;

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
            public bool MoveNext()
            {
                if (_version != _map._version)
                {
                    throw new InvalidOperationException("The map changed during enumeration.");
                }

                int keysLength = _map._keys.Length;
                while (_slot < keysLength)
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
                if (_version != _map._version)
                {
                    throw new InvalidOperationException("The map changed during enumeration.");
                }

                _slot = 0;
                _current = default(KeyValuePair<int, TValue>);
            }

            /// <inheritdoc />
            public void Dispose() { }
        }

        /// <summary>A live-keys view whose typed enumerator is a struct.</summary>
        /// <remarks>
        /// Like <see cref="Enumerator"/>, the walk fails fast on a version change: a resize swaps
        /// the slot storage, and an iterator that kept the old index would skip or repeat entries
        /// against new arrays instead of reporting the mutation.
        /// </remarks>
        public readonly struct KeyView : IEnumerable<int>
        {
            private readonly IntMap<TValue> _map;

            internal KeyView(IntMap<TValue> map)
            {
                _map = map;
            }

            /// <summary>Returns the struct enumerator; the typed foreach path allocates nothing.</summary>
            public KeyEnumerator GetEnumerator()
            {
                return new KeyEnumerator(_map);
            }

            IEnumerator<int> IEnumerable<int>.GetEnumerator()
            {
                return GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        /// <summary>A live-values view whose typed enumerator is a struct.</summary>
        /// <remarks>
        /// Shares <see cref="KeyView"/>'s fail-fast contract and its no-boxing rationale.
        /// </remarks>
        public readonly struct ValueView : IEnumerable<TValue>
        {
            private readonly IntMap<TValue> _map;

            internal ValueView(IntMap<TValue> map)
            {
                _map = map;
            }

            /// <summary>Returns the struct enumerator; the typed foreach path allocates nothing.</summary>
            public ValueEnumerator GetEnumerator()
            {
                return new ValueEnumerator(_map);
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

        /// <summary>Enumerates live keys; fails fast when the map changes mid-walk.</summary>
        public struct KeyEnumerator : IEnumerator<int>
        {
            /// <inheritdoc />
            public int Current => _current;

            object IEnumerator.Current => _current;

            private readonly IntMap<TValue> _map;
            private readonly ulong _version;
            private int _slot;
            private int _current;

            internal KeyEnumerator(IntMap<TValue> map)
            {
                _map = map;
                _version = map._version;
                _slot = 0;
                _current = 0;
            }

            /// <inheritdoc />
            public bool MoveNext()
            {
                if (_version != _map._version)
                {
                    throw new InvalidOperationException("The map changed during enumeration.");
                }

                int keysLength = _map._keys.Length;
                while (_slot < keysLength)
                {
                    int stored = _map._keys[_slot];
                    ++_slot;
                    if (MinimumAllowedKey <= stored)
                    {
                        _current = stored;
                        return true;
                    }
                }

                return false;
            }

            /// <inheritdoc />
            public void Reset()
            {
                if (_version != _map._version)
                {
                    throw new InvalidOperationException("The map changed during enumeration.");
                }

                _slot = 0;
                _current = 0;
            }

            /// <inheritdoc />
            public void Dispose() { }
        }

        /// <summary>Enumerates live values; fails fast when the map changes mid-walk.</summary>
        public struct ValueEnumerator : IEnumerator<TValue>
        {
            /// <inheritdoc />
            public TValue Current => _current;

            object IEnumerator.Current => _current;

            private readonly IntMap<TValue> _map;
            private readonly ulong _version;
            private int _slot;
            private TValue _current;

            internal ValueEnumerator(IntMap<TValue> map)
            {
                _map = map;
                _version = map._version;
                _slot = 0;
                _current = default(TValue);
            }

            /// <inheritdoc />
            public bool MoveNext()
            {
                if (_version != _map._version)
                {
                    throw new InvalidOperationException("The map changed during enumeration.");
                }

                int keysLength = _map._keys.Length;
                while (_slot < keysLength)
                {
                    int stored = _map._keys[_slot];
                    ++_slot;
                    if (MinimumAllowedKey <= stored)
                    {
                        _current = _map._values[_slot - 1];
                        return true;
                    }
                }

                return false;
            }

            /// <inheritdoc />
            public void Reset()
            {
                if (_version != _map._version)
                {
                    throw new InvalidOperationException("The map changed during enumeration.");
                }

                _slot = 0;
                _current = default(TValue);
            }

            /// <inheritdoc />
            public void Dispose() { }
        }
    }
}
