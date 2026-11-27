// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Runtime.CompilerServices;
#if NETCOREAPP3_0_OR_GREATER
    using System.Numerics;
#endif

    /// <summary>
    /// Allocation-free bit manipulation helpers with identical results across .NET and Unity.
    /// </summary>
    /// <remarks>
    /// Signed overloads interpret their argument as the two's-complement bit pattern, matching
    /// unsigned overloads. Builds targeting .NET Core 3.0 or later use
    /// <c>System.Numerics.BitOperations</c> for population count, trailing zero count and floor
    /// log2. Unity uses portable implementations for all three.
    /// </remarks>
    public static class BitOps
    {
        private static ReadOnlySpan<byte> TrailingZeroIndices
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                new byte[]
                {
                    0,
                    1,
                    48,
                    2,
                    57,
                    49,
                    28,
                    3,
                    61,
                    58,
                    50,
                    42,
                    38,
                    29,
                    17,
                    4,
                    62,
                    55,
                    59,
                    36,
                    53,
                    51,
                    43,
                    22,
                    45,
                    39,
                    33,
                    30,
                    24,
                    18,
                    12,
                    5,
                    63,
                    47,
                    56,
                    27,
                    60,
                    41,
                    37,
                    16,
                    54,
                    35,
                    52,
                    21,
                    44,
                    32,
                    23,
                    11,
                    46,
                    26,
                    40,
                    15,
                    34,
                    20,
                    31,
                    10,
                    25,
                    14,
                    19,
                    9,
                    13,
                    8,
                    7,
                    6,
                };
        }

        private static ReadOnlySpan<byte> Log2UintIndices
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                new byte[]
                {
                    0,
                    9,
                    1,
                    10,
                    13,
                    21,
                    2,
                    29,
                    11,
                    14,
                    16,
                    18,
                    22,
                    25,
                    3,
                    30,
                    8,
                    12,
                    20,
                    28,
                    15,
                    17,
                    24,
                    7,
                    19,
                    27,
                    23,
                    6,
                    26,
                    5,
                    4,
                    31,
                };
        }

        private static ReadOnlySpan<byte> Log2UlongIndices
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                new byte[]
                {
                    0,
                    47,
                    1,
                    56,
                    48,
                    27,
                    2,
                    60,
                    57,
                    49,
                    41,
                    37,
                    28,
                    16,
                    3,
                    61,
                    54,
                    58,
                    35,
                    52,
                    50,
                    42,
                    21,
                    44,
                    38,
                    32,
                    29,
                    23,
                    17,
                    11,
                    4,
                    62,
                    46,
                    55,
                    26,
                    59,
                    40,
                    36,
                    15,
                    53,
                    34,
                    51,
                    20,
                    43,
                    31,
                    22,
                    10,
                    45,
                    25,
                    39,
                    14,
                    33,
                    19,
                    30,
                    9,
                    24,
                    13,
                    18,
                    8,
                    12,
                    7,
                    6,
                    5,
                    63,
                };
        }

#if ENABLE_MONO
        private static readonly byte[] MonoTrailingZeroIndices = TrailingZeroIndices.ToArray();
        private static readonly byte[] MonoLog2UintIndices = Log2UintIndices.ToArray();
        private static readonly byte[] MonoLog2UlongIndices = Log2UlongIndices.ToArray();
#endif

        /// <summary>Enumerates set-bit indices from least to most significant without allocating.</summary>
        public static SetBitEnumerator SetBits(ulong mask)
        {
            return new SetBitEnumerator(mask);
        }

        /// <summary>Returns the zero-based ranked set-bit index, or -1 for an invalid rank.</summary>
        public static int SelectBit(ulong mask, int rank)
        {
            if (rank < 0 || 64 <= rank)
            {
                return -1;
            }
            while (0 < rank && mask != 0)
            {
                mask &= mask - 1;
                --rank;
            }
            return mask == 0 ? -1 : TrailingZeroCount(mask);
        }

        /// <summary>
        /// Counts the set bits (population count) of a <see cref="ulong"/> value.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns>The number of bits set to one, from 0 through 64.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopCount(ulong value)
        {
#if NETCOREAPP3_0_OR_GREATER
            return BitOperations.PopCount(value);
#else
            /*
                SWAR pairwise reduction: fold adjacent bits, pairs, nibbles, then bytes; the final
                multiply sums the eight byte counts into the top byte.
            */
            value -= (value >> 1) & 0x5555555555555555UL;
            value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
            value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((value * 0x0101010101010101UL) >> 56);
#endif
        }

        /// <summary>
        /// Counts the set bits (population count) of a <see cref="long"/> value.
        /// </summary>
        /// <param name="value">The value to inspect; the bit pattern is interpreted as unsigned.</param>
        /// <returns>The number of bits set to one, from 0 through 64.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopCount(long value)
        {
            return PopCount((ulong)value);
        }

        /// <summary>
        /// Counts the set bits (population count) of a <see cref="uint"/> value.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns>The number of bits set to one, from 0 through 32.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopCount(uint value)
        {
#if NETCOREAPP3_0_OR_GREATER
            return BitOperations.PopCount(value);
#else
            return PopCount((ulong)value);
#endif
        }

        /// <summary>
        /// Counts the set bits (population count) of an <see cref="int"/> value.
        /// </summary>
        /// <param name="value">The value to inspect; the bit pattern is interpreted as unsigned.</param>
        /// <returns>The number of bits set to one, from 0 through 32.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopCount(int value)
        {
            return PopCount((uint)value);
        }

        /// <summary>
        /// Counts the trailing (least significant) zero bits of a <see cref="ulong"/> value.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns>The index of the least significant set bit, or 64 when <paramref name="value"/> is zero.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TrailingZeroCount(ulong value)
        {
#if NETCOREAPP3_0_OR_GREATER
            return BitOperations.TrailingZeroCount(value);
#else
            if (value == 0)
            {
                return 64;
            }
            ulong isolated = value & unchecked(0UL - value);
            int index = (int)(unchecked(isolated * 0x03F79D71B4CB0A89UL) >> 58);
#if ENABLE_MONO
            return MonoTrailingZeroIndices[index];
#else
            return TrailingZeroIndices[index];
#endif
#endif
        }

        /// <summary>
        /// Counts the trailing (least significant) zero bits of a <see cref="long"/> value.
        /// </summary>
        /// <param name="value">The value to inspect; the bit pattern is interpreted as unsigned.</param>
        /// <returns>The index of the least significant set bit, or 64 when <paramref name="value"/> is zero.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TrailingZeroCount(long value)
        {
            return TrailingZeroCount((ulong)value);
        }

        /// <summary>
        /// Counts the trailing (least significant) zero bits of a <see cref="uint"/> value.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns>The index of the least significant set bit, or 32 when <paramref name="value"/> is zero.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TrailingZeroCount(uint value)
        {
#if NETCOREAPP3_0_OR_GREATER
            return BitOperations.TrailingZeroCount(value);
#else
            return value == 0 ? 32 : TrailingZeroCount((ulong)value);
#endif
        }

        /// <summary>
        /// Counts the trailing (least significant) zero bits of an <see cref="int"/> value.
        /// </summary>
        /// <param name="value">The value to inspect; the bit pattern is interpreted as unsigned.</param>
        /// <returns>The index of the least significant set bit, or 32 when <paramref name="value"/> is zero.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TrailingZeroCount(int value)
        {
            return TrailingZeroCount((uint)value);
        }

        /// <summary>
        /// Computes the floor base-two logarithm of a <see cref="uint"/> value: the zero-based
        /// index of its most significant set bit.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns>The floor of log2(<paramref name="value"/>); 0 when <paramref name="value"/> is 0 or 1.</returns>
        /// <remarks>Uses the hardware intrinsic on .NET Core 3.0 or later, and portable de Bruijn indexing elsewhere.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2(uint value)
        {
#if NETCOREAPP3_0_OR_GREATER
            return BitOperations.Log2(value);
#else
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            int index = (int)(unchecked(value * 0x07C4ACDDU) >> 27);
#if ENABLE_MONO
            return MonoLog2UintIndices[index];
#else
            return Log2UintIndices[index];
#endif
#endif
        }

        /// <summary>
        /// Computes the floor base-two logarithm of an <see cref="int"/> value: the zero-based
        /// index of its most significant set bit.
        /// </summary>
        /// <param name="value">The value to inspect; the bit pattern is interpreted as unsigned.</param>
        /// <returns>The floor of log2 of the unsigned bit pattern; 0 when the pattern is 0 or 1.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2(int value)
        {
            return Log2((uint)value);
        }

        /// <summary>
        /// Computes the floor base-two logarithm of a <see cref="ulong"/> value: the zero-based
        /// index of its most significant set bit.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns>The floor of log2(<paramref name="value"/>); 0 when <paramref name="value"/> is 0 or 1.</returns>
        /// <remarks>Uses the hardware intrinsic on .NET Core 3.0 or later, and portable de Bruijn indexing elsewhere.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2(ulong value)
        {
#if NETCOREAPP3_0_OR_GREATER
            return BitOperations.Log2(value);
#else
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value |= value >> 32;
            int index = (int)(unchecked(value * 0x03F79D71B4CB0A89UL) >> 58);
#if ENABLE_MONO
            return MonoLog2UlongIndices[index];
#else
            return Log2UlongIndices[index];
#endif
#endif
        }

        /// <summary>
        /// Computes the floor base-two logarithm of a <see cref="long"/> value: the zero-based
        /// index of its most significant set bit.
        /// </summary>
        /// <param name="value">The value to inspect; the bit pattern is interpreted as unsigned.</param>
        /// <returns>The floor of log2 of the unsigned bit pattern; 0 when the pattern is 0 or 1.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2(long value)
        {
            return Log2((ulong)value);
        }

        /// <summary>
        /// Counts the leading (most significant) zero bits of a <see cref="uint"/> value.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns>The number of zero bits before the most significant set bit, or 32 when <paramref name="value"/> is zero.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LeadingZeroCount(uint value)
        {
#if NETCOREAPP3_0_OR_GREATER
            return BitOperations.LeadingZeroCount(value);
#else
            return value == 0U ? 32 : 31 - Log2(value);
#endif
        }

        /// <summary>
        /// Counts the leading (most significant) zero bits of an <see cref="int"/> value.
        /// </summary>
        /// <param name="value">The value to inspect; the bit pattern is interpreted as unsigned.</param>
        /// <returns>The number of zero bits before the most significant set bit, or 32 when the bit pattern is zero.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LeadingZeroCount(int value)
        {
            return LeadingZeroCount((uint)value);
        }

        /// <summary>
        /// Counts the leading (most significant) zero bits of a <see cref="ulong"/> value.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns>The number of zero bits before the most significant set bit, or 64 when <paramref name="value"/> is zero.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LeadingZeroCount(ulong value)
        {
#if NETCOREAPP3_0_OR_GREATER
            return BitOperations.LeadingZeroCount(value);
#else
            return value == 0UL ? 64 : 63 - Log2(value);
#endif
        }

        /// <summary>
        /// Counts the leading (most significant) zero bits of a <see cref="long"/> value.
        /// </summary>
        /// <param name="value">The value to inspect; the bit pattern is interpreted as unsigned.</param>
        /// <returns>The number of zero bits before the most significant set bit, or 64 when the bit pattern is zero.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LeadingZeroCount(long value)
        {
            return LeadingZeroCount((ulong)value);
        }

        /// <summary>
        /// Isolates the most significant set bit of a <see cref="ulong"/> value.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns>A value with only the highest set bit of <paramref name="value"/> set; 0 when <paramref name="value"/> is 0.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong HighestSetBit(ulong value)
        {
            return 0UL == value ? 0UL : 1UL << Log2(value);
        }

        /// <summary>
        /// Isolates the most significant set bit of a <see cref="long"/> value.
        /// </summary>
        /// <param name="value">The value to inspect; the bit pattern is interpreted as unsigned.</param>
        /// <returns>A value with only the highest set bit set; 0 when the bit pattern is 0.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long HighestSetBit(long value)
        {
            return unchecked((long)HighestSetBit((ulong)value));
        }

        /// <summary>
        /// Isolates the most significant set bit of a <see cref="uint"/> value.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns>A value with only the highest set bit of <paramref name="value"/> set; 0 when <paramref name="value"/> is 0.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint HighestSetBit(uint value)
        {
            return 0U == value ? 0U : 1U << Log2(value);
        }

        /// <summary>
        /// Isolates the most significant set bit of an <see cref="int"/> value.
        /// </summary>
        /// <param name="value">The value to inspect; the bit pattern is interpreted as unsigned.</param>
        /// <returns>A value with only the highest set bit set; 0 when the bit pattern is 0.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int HighestSetBit(int value)
        {
            return unchecked((int)HighestSetBit((uint)value));
        }

        /// <summary>
        /// Determines whether a <see cref="ulong"/> value is a non-zero power of two.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns>True when exactly one bit of <paramref name="value"/> is set; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPowerOfTwo(ulong value)
        {
            return 0UL != value && (value & (value - 1UL)) == 0UL;
        }

        /// <summary>
        /// Determines whether a <see cref="long"/> value is a non-zero power of two.
        /// </summary>
        /// <param name="value">The value to inspect; the bit pattern is interpreted as unsigned.</param>
        /// <returns>True when exactly one bit of the bit pattern is set; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPowerOfTwo(long value)
        {
            return IsPowerOfTwo((ulong)value);
        }

        /// <summary>
        /// Determines whether a <see cref="uint"/> value is a non-zero power of two.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns>True when exactly one bit of <paramref name="value"/> is set; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPowerOfTwo(uint value)
        {
            return 0U != value && (value & (value - 1U)) == 0U;
        }

        /// <summary>
        /// Determines whether an <see cref="int"/> value is a non-zero power of two.
        /// </summary>
        /// <param name="value">The value to inspect; the bit pattern is interpreted as unsigned.</param>
        /// <returns>True when exactly one bit of the bit pattern is set; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPowerOfTwo(int value)
        {
            return IsPowerOfTwo((uint)value);
        }

        /// <summary>
        /// Rotates the bits of a <see cref="uint"/> value left by an offset.
        /// </summary>
        /// <param name="value">The value whose bits are rotated.</param>
        /// <param name="offset">The number of positions to rotate; positive and negative offsets and multiples of 32 are accepted.</param>
        /// <returns><paramref name="value"/> rotated left by <paramref name="offset"/> modulo 32 positions.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint RotateLeft(uint value, int offset)
        {
#if NETCOREAPP3_0_OR_GREATER
            return BitOperations.RotateLeft(value, offset);
#else
            int shift = offset & 31;
            return (value << shift) | (value >> ((32 - shift) & 31));
#endif
        }

        /// <summary>
        /// Rotates the bits of an <see cref="int"/> value left by an offset.
        /// </summary>
        /// <param name="value">The value whose bits are rotated; the bit pattern is interpreted as unsigned.</param>
        /// <param name="offset">The number of positions to rotate.</param>
        /// <returns>The rotated bit pattern.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RotateLeft(int value, int offset)
        {
            return unchecked((int)RotateLeft((uint)value, offset));
        }

        /// <summary>
        /// Rotates the bits of a <see cref="ulong"/> value left by an offset.
        /// </summary>
        /// <param name="value">The value whose bits are rotated.</param>
        /// <param name="offset">The number of positions to rotate; positive and negative offsets and multiples of 64 are accepted.</param>
        /// <returns><paramref name="value"/> rotated left by <paramref name="offset"/> modulo 64 positions.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong RotateLeft(ulong value, int offset)
        {
#if NETCOREAPP3_0_OR_GREATER
            return BitOperations.RotateLeft(value, offset);
#else
            int shift = offset & 63;
            return (value << shift) | (value >> ((64 - shift) & 63));
#endif
        }

        /// <summary>
        /// Rotates the bits of a <see cref="long"/> value left by an offset.
        /// </summary>
        /// <param name="value">The value whose bits are rotated; the bit pattern is interpreted as unsigned.</param>
        /// <param name="offset">The number of positions to rotate.</param>
        /// <returns>The rotated bit pattern.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long RotateLeft(long value, int offset)
        {
            return unchecked((long)RotateLeft((ulong)value, offset));
        }

        /// <summary>
        /// Rotates the bits of a <see cref="uint"/> value right by an offset.
        /// </summary>
        /// <param name="value">The value whose bits are rotated.</param>
        /// <param name="offset">The number of positions to rotate; positive and negative offsets and multiples of 32 are accepted.</param>
        /// <returns><paramref name="value"/> rotated right by <paramref name="offset"/> modulo 32 positions.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint RotateRight(uint value, int offset)
        {
#if NETCOREAPP3_0_OR_GREATER
            return BitOperations.RotateRight(value, offset);
#else
            int shift = offset & 31;
            return (value >> shift) | (value << ((32 - shift) & 31));
#endif
        }

        /// <summary>
        /// Rotates the bits of an <see cref="int"/> value right by an offset.
        /// </summary>
        /// <param name="value">The value whose bits are rotated; the bit pattern is interpreted as unsigned.</param>
        /// <param name="offset">The number of positions to rotate.</param>
        /// <returns>The rotated bit pattern.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RotateRight(int value, int offset)
        {
            return unchecked((int)RotateRight((uint)value, offset));
        }

        /// <summary>
        /// Rotates the bits of a <see cref="ulong"/> value right by an offset.
        /// </summary>
        /// <param name="value">The value whose bits are rotated.</param>
        /// <param name="offset">The number of positions to rotate; positive and negative offsets and multiples of 64 are accepted.</param>
        /// <returns><paramref name="value"/> rotated right by <paramref name="offset"/> modulo 64 positions.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong RotateRight(ulong value, int offset)
        {
#if NETCOREAPP3_0_OR_GREATER
            return BitOperations.RotateRight(value, offset);
#else
            int shift = offset & 63;
            return (value >> shift) | (value << ((64 - shift) & 63));
#endif
        }

        /// <summary>
        /// Rotates the bits of a <see cref="long"/> value right by an offset.
        /// </summary>
        /// <param name="value">The value whose bits are rotated; the bit pattern is interpreted as unsigned.</param>
        /// <param name="offset">The number of positions to rotate.</param>
        /// <returns>The rotated bit pattern.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long RotateRight(long value, int offset)
        {
            return unchecked((long)RotateRight((ulong)value, offset));
        }

        /// <summary>
        /// Computes the smallest power of two that is greater than or equal to a <see cref="uint"/>
        /// value, for capacity and buffer sizing.
        /// </summary>
        /// <param name="value">The lower bound to round up.</param>
        /// <returns>The smallest power of two &gt;= <paramref name="value"/>; 1 when <paramref name="value"/> is 0 or 1.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="value"/> exceeds the largest representable power of two
        /// (<see cref="uint.MaxValue"/> / 2 + 1), because no exact result exists.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint NextPowerOfTwo(uint value)
        {
            if (value <= 1U)
            {
                return 1U;
            }

            if (0x8000_0000U < value)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    $"No power of two representable in uint is greater than or equal to {value}."
                );
            }

            return 1U << (Log2(value - 1U) + 1);
        }

        /// <summary>
        /// Computes the smallest power of two that is greater than or equal to an <see cref="int"/>
        /// value, for capacity and buffer sizing.
        /// </summary>
        /// <param name="value">The lower bound to round up.</param>
        /// <returns>The smallest power of two &gt;= <paramref name="value"/>; 1 when <paramref name="value"/> is 0 or 1.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="value"/> is negative, or exceeds 2^30, because no exact
        /// non-negative result exists.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int NextPowerOfTwo(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "A negative value has no power-of-two ceiling."
                );
            }

            if (value <= 1)
            {
                return 1;
            }

            if (0x4000_0000 < value)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    $"No power of two representable in int is greater than or equal to {value}."
                );
            }

            return 1 << (Log2(value - 1) + 1);
        }

        /// <summary>
        /// Computes the smallest power of two that is greater than or equal to a <see cref="ulong"/>
        /// value, for capacity and buffer sizing.
        /// </summary>
        /// <param name="value">The lower bound to round up.</param>
        /// <returns>The smallest power of two &gt;= <paramref name="value"/>; 1 when <paramref name="value"/> is 0 or 1.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="value"/> exceeds 2^63, because no exact result exists.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong NextPowerOfTwo(ulong value)
        {
            if (value <= 1UL)
            {
                return 1UL;
            }

            if (0x8000_0000_0000_0000UL < value)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    $"No power of two representable in ulong is greater than or equal to {value}."
                );
            }

            return 1UL << (Log2(value - 1UL) + 1);
        }

        /// <summary>
        /// Computes the smallest power of two that is greater than or equal to a <see cref="long"/>
        /// value, for capacity and buffer sizing.
        /// </summary>
        /// <param name="value">The lower bound to round up.</param>
        /// <returns>The smallest power of two &gt;= <paramref name="value"/>; 1 when <paramref name="value"/> is 0 or 1.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="value"/> is negative, or exceeds 2^62, because no exact
        /// non-negative result exists.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long NextPowerOfTwo(long value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "A negative value has no power-of-two ceiling."
                );
            }

            if (value <= 1L)
            {
                return 1L;
            }

            if (0x4000_0000_0000_0000L < value)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    $"No power of two representable in long is greater than or equal to {value}."
                );
            }

            return 1L << (Log2(value - 1L) + 1);
        }
    }
}
