// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using ProtoBuf;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Compact, dynamically resizable bit set that stores dense boolean flags using a single bit per entry.
    /// Ideal for entity state masks, collision layers, and other scenarios that benefit from memory-efficient O(1) reads and writes.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// BitSet activeLayers = new BitSet();
    /// activeLayers.Set(2);
    /// bool isLayerActive = activeLayers.TryGet(2, out bool value) && value;
    /// ]]></code>
    /// </example>
    [Serializable]
    [ProtoContract(IgnoreListHandling = true)]
    [WProtoContract(IgnoreListHandling = true)]
    public sealed partial class BitSet : IReadOnlyList<bool>
    {
        private const int BitsPerLong = 64;
        private const int BitsPerLongShift = 6;
        private const int BitsPerLongMask = 63;
        private const int DefaultCapacity = 64;

        [SerializeField]
        [ProtoMember(1)]
        [WProtoMember(1)]
        private ulong[] _bits;

        [SerializeField]
        [ProtoMember(2)]
        [WProtoMember(2)]
        private int _capacity;

        public int Count => _capacity;

        /// <summary>
        /// Gets the current capacity (maximum number of bits that can be stored without resizing).
        /// </summary>
        public int Capacity => _capacity;

        /// <summary>
        /// Gets or sets the bit at the specified index.
        /// Automatically expands if setting a bit beyond current capacity and ignores assignments outside the representable index range.
        /// </summary>
        public bool this[int index]
        {
            get => TryGet(index, out bool value) && value;
            set
            {
                if (value)
                {
                    TrySet(index);
                }
                else
                {
                    TryClear(index);
                }
            }
        }

        private BitSet()
        {
            _capacity = 0;
            _bits = Array.Empty<ulong>();
        }

        /// <summary>
        /// Constructs a bit set with the specified initial capacity.
        /// </summary>
        public BitSet(int initialCapacity)
        {
            if (initialCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialCapacity),
                    "Initial capacity must be positive."
                );
            }

            _capacity = initialCapacity;
            int arraySize = WordCountForCapacity(initialCapacity);
            _bits = new ulong[arraySize];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int WordCountForCapacity(int capacity)
        {
            return (int)(((long)capacity + BitsPerLong - 1) >> BitsPerLongShift);
        }

        /// <summary>
        /// Attempts to set the bit at the specified index to 1.
        /// Automatically expands capacity if needed, or returns false when the index cannot be represented.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TrySet(int index)
        {
            if (index < 0 || int.MaxValue <= index)
            {
                return false;
            }

            if (_capacity <= index)
            {
                EnsureCapacity(index + 1);
            }

            int arrayIndex = index >> BitsPerLongShift;
            int bitIndex = index & BitsPerLongMask;
            _bits[arrayIndex] |= 1UL << bitIndex;
            return true;
        }

        /// <summary>
        /// Attempts to clear the bit at the specified index to 0.
        /// Returns false if index is negative or beyond current capacity (does not auto-expand).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryClear(int index)
        {
            if (index < 0 || _capacity <= index)
            {
                return false;
            }

            int arrayIndex = index >> BitsPerLongShift;
            if (_bits == null || _bits.Length <= arrayIndex)
            {
                return false;
            }

            int bitIndex = index & BitsPerLongMask;
            _bits[arrayIndex] &= ~(1UL << bitIndex);
            return true;
        }

        /// <summary>
        /// Attempts to toggle the bit at the specified index.
        /// Automatically expands capacity if needed, or returns false when the index cannot be represented.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryFlip(int index)
        {
            if (index < 0 || int.MaxValue <= index)
            {
                return false;
            }

            if (_capacity <= index)
            {
                EnsureCapacity(index + 1);
            }

            int arrayIndex = index >> BitsPerLongShift;
            int bitIndex = index & BitsPerLongMask;
            _bits[arrayIndex] ^= 1UL << bitIndex;
            return true;
        }

        /// <summary>
        /// Attempts to get the bit at the specified index.
        /// Returns false (out parameter) for indices beyond capacity.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(int index, out bool value)
        {
            if (index < 0 || _capacity <= index)
            {
                value = false;
                return false;
            }

            int arrayIndex = index >> BitsPerLongShift;
            if (_bits == null || _bits.Length <= arrayIndex)
            {
                value = false;
                return false;
            }

            int bitIndex = index & BitsPerLongMask;
            value = (_bits[arrayIndex] & (1UL << bitIndex)) != 0;
            return true;
        }

        // Capacity is an untrusted claim; only delivered words contain readable bits.
        [ProtoAfterDeserialization]
        [WProtoAfterDeserialization]
        private void ClampCapacityToDeliveredWords()
        {
            long delivered = _bits == null ? 0L : (long)_bits.Length << BitsPerLongShift;
            if (_capacity < 0)
            {
                _capacity = 0;
                return;
            }

            if (delivered < _capacity)
            {
                _capacity = (int)delivered;
            }
        }

        /// <summary>
        /// Ensures the bit set can hold at least the specified capacity.
        /// </summary>
        public void EnsureCapacity(int minCapacity)
        {
            if (minCapacity <= _capacity)
            {
                return;
            }

            // Doubling zero or an overflowed capacity cannot make progress.
            int newCapacity = _capacity < 1 ? 1 : _capacity;
            while (newCapacity < minCapacity)
            {
                int grown = newCapacity < 256 ? newCapacity * 2 : newCapacity + (newCapacity / 2);
                if (grown <= newCapacity)
                {
                    newCapacity = minCapacity;
                    break;
                }

                newCapacity = grown;
            }

            Resize(newCapacity);
        }

        /// <summary>
        /// Resizes the bit set to the specified capacity.
        /// If shrinking, bits beyond the new capacity are lost.
        /// </summary>
        public void Resize(int newCapacity)
        {
            if (newCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(newCapacity),
                    "Capacity must be positive."
                );
            }

            if (newCapacity == _capacity)
            {
                return;
            }

            int newArraySize = WordCountForCapacity(newCapacity);
            Array.Resize(ref _bits, newArraySize);
            _capacity = newCapacity;

            int remainingBits = newCapacity & BitsPerLongMask;
            if (remainingBits != 0 && 0 < newArraySize)
            {
                ulong mask = (1UL << remainingBits) - 1;
                _bits[newArraySize - 1] &= mask;
            }
        }

        /// <summary>
        /// Shrinks the capacity to fit the highest set bit, or to a minimum capacity.
        /// </summary>
        public void TrimExcess(int minimumCapacity = DefaultCapacity)
        {
            int highestSetBit = -1;
            for (int i = _capacity - 1; 0 <= i; i--)
            {
                if (TryGet(i, out bool value) && value)
                {
                    highestSetBit = i;
                    break;
                }
            }

            int targetCapacity = Math.Max(minimumCapacity, highestSetBit + 1);
            if (targetCapacity < _capacity)
            {
                Resize(targetCapacity);
            }
        }

        /// <summary>
        /// Sets all bits to 1 within the current capacity.
        /// </summary>
        public void SetAll()
        {
            for (int i = 0; i < _bits.Length; i++)
            {
                _bits[i] = ulong.MaxValue;
            }

            int remainingBits = _capacity & BitsPerLongMask;
            if (remainingBits != 0 && 0 < _bits.Length)
            {
                ulong mask = (1UL << remainingBits) - 1;
                _bits[^1] &= mask;
            }
        }

        /// <summary>
        /// Sets all bits to 0.
        /// </summary>
        public void ClearAll()
        {
            Array.Clear(_bits, 0, _bits.Length);
        }

        /// <summary>
        /// Flips all bits (0 becomes 1, 1 becomes 0) within the current capacity.
        /// </summary>
        public void FlipAll()
        {
            for (int i = 0; i < _bits.Length; i++)
            {
                _bits[i] = ~_bits[i];
            }

            int remainingBits = _capacity & BitsPerLongMask;
            if (remainingBits != 0 && 0 < _bits.Length)
            {
                ulong mask = (1UL << remainingBits) - 1;
                _bits[^1] &= mask;
            }
        }

        /// <summary>
        /// Inverts all bits (same as FlipAll).
        /// </summary>
        public void Not()
        {
            FlipAll();
        }

        /// <summary>
        /// Shifts all bits left by the specified amount.
        /// Bits shifted beyond capacity are lost, zeros fill from the right.
        /// </summary>
        public void LeftShift(int shift)
        {
            if (shift <= 0)
            {
                return;
            }

            if (_capacity <= shift)
            {
                ClearAll();
                return;
            }

            // Read a snapshot so shifted writes cannot overwrite unread source bits.
            using PooledArray<ulong> pooled = SystemArrayPool<ulong>.Get(
                _bits.Length,
                out ulong[] temp
            );
            Array.Copy(_bits, temp, _bits.Length);

            ClearAll();

            for (int i = shift; i < _capacity; i++)
            {
                int sourceIndex = i - shift;
                int sourceWordIndex = sourceIndex >> 6;
                int sourceBitIndex = sourceIndex & 63;
                bool value = (temp[sourceWordIndex] & (1UL << sourceBitIndex)) != 0;
                if (value)
                {
                    TrySet(i);
                }
            }
        }

        /// <summary>
        /// Shifts all bits right by the specified amount.
        /// Bits shifted out are lost, zeros fill from the left.
        /// </summary>
        public void RightShift(int shift)
        {
            if (shift <= 0)
            {
                return;
            }

            if (_capacity <= shift)
            {
                ClearAll();
                return;
            }

            for (int i = 0; i < _capacity - shift; i++)
            {
                if (TryGet(i + shift, out bool value) && value)
                {
                    TrySet(i);
                }
                else
                {
                    TryClear(i);
                }
            }

            for (int i = _capacity - shift; i < _capacity; i++)
            {
                TryClear(i);
            }
        }

        /// <summary>
        /// Counts the number of bits set to 1.
        /// </summary>
        public int CountSetBits()
        {
            int count = 0;
            foreach (ulong bit in _bits)
            {
                count += PopCount(bit);
            }
            return count;
        }

        /// <summary>
        /// Checks if any bits are set to 1.
        /// </summary>
        public bool Any()
        {
            return Array.Exists(_bits, bit => bit != 0);
        }

        /// <summary>
        /// Checks if all bits are set to 0.
        /// </summary>
        public bool None()
        {
            return !Any();
        }

        /// <summary>
        /// Checks if all bits (within capacity) are set to 1.
        /// </summary>
        public bool All()
        {
            if (_capacity <= 0)
            {
                return false;
            }

            int fullSegments = _capacity >> BitsPerLongShift;
            for (int i = 0; i < fullSegments; i++)
            {
                if (_bits[i] != ulong.MaxValue)
                {
                    return false;
                }
            }

            int remainingBits = _capacity & BitsPerLongMask;
            if (remainingBits == 0)
            {
                return true;
            }

            if (_bits.Length <= fullSegments)
            {
                return false;
            }

            ulong mask = (1UL << remainingBits) - 1;
            return (_bits[fullSegments] & mask) == mask;
        }

        /// <summary>
        /// Performs a bitwise AND operation with another BitSet.
        /// Resizes this BitSet to match the other if needed.
        /// </summary>
        public bool TryAnd(BitSet other)
        {
            if (other == null)
            {
                return false;
            }

            if (other._capacity != _capacity)
            {
                Resize(other._capacity);
            }

            for (int i = 0; i < _bits.Length; i++)
            {
                _bits[i] &= other._bits[i];
            }
            return true;
        }

        /// <summary>
        /// Performs a bitwise OR operation with another BitSet.
        /// Resizes this BitSet to match the other if needed.
        /// </summary>
        public bool TryOr(BitSet other)
        {
            if (other == null)
            {
                return false;
            }

            if (_capacity < other._capacity)
            {
                Resize(other._capacity);
            }

            int minLength = Math.Min(_bits.Length, other._bits.Length);
            for (int i = 0; i < minLength; i++)
            {
                _bits[i] |= other._bits[i];
            }
            return true;
        }

        /// <summary>
        /// Performs a bitwise XOR operation with another BitSet.
        /// Resizes this BitSet to match the other if needed.
        /// </summary>
        public bool TryXor(BitSet other)
        {
            if (other == null)
            {
                return false;
            }

            if (_capacity < other._capacity)
            {
                Resize(other._capacity);
            }

            int minLength = Math.Min(_bits.Length, other._bits.Length);
            for (int i = 0; i < minLength; i++)
            {
                _bits[i] ^= other._bits[i];
            }
            return true;
        }

        /// <summary>
        /// Populates the provided list with indices of all set bits.
        /// Clears the list before adding. Returns the same list for chaining.
        /// </summary>
        public List<int> GetSetBits(List<int> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            for (int i = 0; i < _capacity; i++)
            {
                if (TryGet(i, out bool value) && value)
                {
                    results.Add(i);
                }
            }
            return results;
        }

        /// <summary>
        /// Enumerates indices of all set bits (where value is 1).
        /// Use this for efficient iteration over sparse bitsets.
        /// </summary>
        public IEnumerable<int> EnumerateSetIndices()
        {
            for (int i = 0; i < _capacity; i++)
            {
                if (TryGet(i, out bool value) && value)
                {
                    yield return i;
                }
            }
        }

        /// <summary>
        /// Converts this BitSet to an immutable ImmutableBitSet.
        /// Creates a snapshot with a copy of the current bit data.
        /// </summary>
        public ImmutableBitSet ToImmutable()
        {
            ulong[] bitsCopy = new ulong[_bits.Length];
            Array.Copy(_bits, bitsCopy, _bits.Length);
            return new ImmutableBitSet(bitsCopy, _capacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PopCount(ulong value)
        {
            // Brian Kernighan's algorithm
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Returns an enumerator that iterates through all bit values (true/false) in order.
        /// Use foreach to iterate over all bits.
        /// </summary>
        public BitEnumerator GetEnumerator()
        {
            return new BitEnumerator(this);
        }

        IEnumerator<bool> IEnumerable<bool>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public struct BitEnumerator : IEnumerator<bool>
        {
            private readonly BitSet _bitSet;
            private int _index;
            private bool _current;

            internal BitEnumerator(BitSet bitSet)
            {
                _bitSet = bitSet;
                _index = -1;
                _current = false;
            }

            public bool MoveNext()
            {
                if (++_index < _bitSet._capacity)
                {
                    _bitSet.TryGet(_index, out _current);
                    return true;
                }
                _current = false;
                return false;
            }

            public bool Current => _current;

            object IEnumerator.Current => Current;

            public void Reset()
            {
                _index = -1;
                _current = false;
            }

            public void Dispose() { }
        }
    }
}
