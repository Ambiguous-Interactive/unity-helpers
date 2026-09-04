// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// Immutable value-type snapshot of a <see cref="BitSet"/> that exposes read-only bit access without allocating.
    /// Ideal for safely sharing flag data across threads, event callbacks, or history buffers without accidental mutations.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// BitSet runtimeState = GetRuntimeFlags();
    /// ImmutableBitSet snapshot = runtimeState.ToImmutable();
    /// if (snapshot.TryGet(flagIndex, out bool enabled) && enabled)
    /// {
    ///     ActivateFeature();
    /// }
    /// ]]></code>
    /// </example>
    [Serializable]
#pragma warning disable WPROTO030 // Served through ImmutableBitSetSurrogate by both serializers.
    [ProtoContract(IgnoreListHandling = true)]
#pragma warning restore WPROTO030
    public readonly struct ImmutableBitSet : IEquatable<ImmutableBitSet>
    {
        private const int BitsPerLongShift = 6; // log2(64)
        private const int BitsPerLongMask = 63; // 64 - 1

        [ProtoMember(1)]
        private readonly ulong[] _bits;

        [ProtoMember(2)]
        private readonly int _capacity;

        /// <summary>
        /// Gets the current capacity (number of bits stored).
        /// </summary>
        public int Capacity => _capacity;

        /// <summary>
        /// Gets the number of bits in this bit set.
        /// </summary>
        public int Count => _capacity;

        /// <summary>
        /// Gets the bit at the specified index.
        /// Returns false for indices beyond capacity.
        /// </summary>
        public bool this[int index] => TryGet(index, out bool value) && value;

        /// <summary>
        /// Constructs an immutable bit set with the specified capacity and bit data.
        /// </summary>
        /// <remarks>
        /// The capacity is lowered to what <paramref name="bits"/> can hold. A payload states the
        /// capacity and delivers the words as separate members, so the two can disagree; every read
        /// indexes <c>bits</c> from an index the capacity admitted, and a capacity larger than the
        /// words delivered turns <see cref="TryGet"/> and <see cref="All"/> into throwing members.
        /// Truncating loses nothing, because no bit exists past the last delivered word.
        /// </remarks>
        internal ImmutableBitSet(ulong[] bits, int capacity)
        {
            _bits = bits ?? Array.Empty<ulong>();
            long deliverableBits = (long)_bits.Length << BitsPerLongShift;
            _capacity =
                capacity <= 0 ? 0
                : deliverableBits < capacity ? (int)deliverableBits
                : capacity;
        }

        /// <summary>
        /// Gets a copy of the internal bits array for serialization purposes.
        /// </summary>
        internal ulong[] GetBitsArrayCopy()
        {
            if (_bits == null)
            {
                return Array.Empty<ulong>();
            }
            ulong[] copy = new ulong[_bits.Length];
            Array.Copy(_bits, copy, _bits.Length);
            return copy;
        }

        /// <summary>
        /// Attempts to get the bit at the specified index.
        /// Returns false if the index is out of range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(int index, out bool value)
        {
            if (index < 0 || _capacity <= index || _bits == null)
            {
                value = false;
                return false;
            }
            int arrayIndex = index >> BitsPerLongShift;
            int bitIndex = index & BitsPerLongMask;
            value = (_bits[arrayIndex] & (1UL << bitIndex)) != 0;
            return true;
        }

        /// <summary>
        /// Counts the number of bits set to 1.
        /// </summary>
        public int CountSetBits()
        {
            if (_bits == null)
            {
                return 0;
            }
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
            if (_bits == null)
            {
                return false;
            }
            return Array.Exists(_bits, bit => bit != 0);
        }

        public List<bool> ToList()
        {
            List<bool> result = new();
            foreach (bool bit in this)
            {
                result.Add(bit);
            }

            return result;
        }

        public List<bool> ToList(List<bool> result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            result.Clear();
            foreach (bool bit in this)
            {
                result.Add(bit);
            }

            return result;
        }

        public IEnumerable<bool> AsEnumerable()
        {
            foreach (bool value in this)
            {
                yield return value;
            }
        }

        public bool[] ToArray()
        {
            bool[] result = null;
            _ = ToArray(ref result);
            return result;
        }

        public int ToArray(ref bool[] result)
        {
            int count = Count;
            if (result == null || result.Length < count)
            {
                result = new bool[count];
            }

            int index = 0;
            foreach (bool value in this)
            {
                result[index++] = value;
            }
            return count;
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
            if (_capacity <= 0 || _bits == null)
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
        /// Converts this immutable bit set to a mutable BitSet.
        /// Creates a new BitSet with a copy of the bit data.
        /// </summary>
        public BitSet ToBitSet()
        {
            BitSet result = new(0 < _capacity ? _capacity : 64);
            if (_bits == null || _capacity <= 0)
            {
                return result;
            }

            // Copy bits from this immutable set to the new mutable set
            for (int i = 0; i < _capacity; i++)
            {
                if (TryGet(i, out bool value) && value)
                {
                    result.TrySet(i);
                }
            }
            return result;
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
        /// </summary>
        public BitEnumerator GetEnumerator()
        {
            return new BitEnumerator(this);
        }

        /// <summary>
        /// Checks if this ImmutableBitSet is equal to another.
        /// </summary>
        /// <remarks>
        /// Compared through spans, which read a null word array as empty. Only
        /// <c>default(ImmutableBitSet)</c> carries null -- the constructor normalizes -- so a
        /// zero-initialized set equals the empty one every round trip produces, exactly as
        /// <see cref="GetHashCode"/> has always hashed them.
        /// </remarks>
        public bool Equals(ImmutableBitSet other)
        {
            if (_capacity != other._capacity)
            {
                return false;
            }

            return _bits.AsSpan().SequenceEqual(other._bits);
        }

        public override bool Equals(object obj)
        {
            return obj is ImmutableBitSet other && Equals(other);
        }

        public override int GetHashCode()
        {
            int hash = Objects.SpanHashCode<ulong>(_bits.AsSpan());
            return Objects.HashCode(_capacity, hash);
        }

        public static bool operator ==(ImmutableBitSet left, ImmutableBitSet right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ImmutableBitSet left, ImmutableBitSet right)
        {
            return !(left == right);
        }

        public struct BitEnumerator : IEnumerator<bool>
        {
            private readonly ImmutableBitSet _bitSet;
            private int _index;
            private bool _current;

            internal BitEnumerator(ImmutableBitSet bitSet)
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
