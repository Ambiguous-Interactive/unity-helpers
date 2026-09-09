// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Allocation-free, IL2CPP-safe bit manipulation helpers. Every method is a pure function of
    /// its inputs and uses only portable integer arithmetic, so results are identical across the
    /// editor, Mono and IL2CPP players on every supported platform.
    /// </summary>
    /// <remarks>
    /// Signed overloads interpret their argument as the two's-complement bit pattern, matching
    /// the unsigned interpretation used by .NET's <c>System.Numerics.BitOperations</c>; negative
    /// inputs are the unsigned rebitcast.
    /// </remarks>
    public static class BitOps
    {
        /// <summary>
        /// Counts the set bits (population count) of a <see cref="ulong"/> value.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns>The number of bits set to one, from 0 through 64.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopCount(ulong value)
        {
            /*
                SWAR pairwise reduction: fold adjacent bits, pairs, nibbles, then bytes; the final
                multiply sums the eight byte counts into the top byte.
            */
            value -= (value >> 1) & 0x5555555555555555UL;
            value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
            value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((value * 0x0101010101010101UL) >> 56);
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
            return PopCount((ulong)value);
        }

        /// <summary>
        /// Counts the set bits (population count) of an <see cref="int"/> value.
        /// </summary>
        /// <param name="value">The value to inspect; the bit pattern is interpreted as unsigned.</param>
        /// <returns>The number of bits set to one, from 0 through 32.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopCount(int value)
        {
            return PopCount((ulong)(uint)value);
        }

        /// <summary>
        /// Counts the trailing (least significant) zero bits of a <see cref="ulong"/> value.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns>The index of the least significant set bit, or 64 when <paramref name="value"/> is zero.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TrailingZeroCount(ulong value)
        {
            /*
                Isolating the lowest set bit and decrementing leaves exactly that many lower bits
                set; zero wraps to all ones, which PopCount scores as 64.
            */
            return PopCount((value & (0UL - value)) - 1UL);
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
            return PopCount((value & (0U - value)) - 1U);
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2(uint value)
        {
            int log = 0;
            if (0x0001_0000U <= value)
            {
                value >>= 16;
                log = 16;
            }

            if (0x0000_0100U <= value)
            {
                value >>= 8;
                log += 8;
            }

            if (0x0000_0010U <= value)
            {
                value >>= 4;
                log += 4;
            }

            if (0x0000_0004U <= value)
            {
                value >>= 2;
                log += 2;
            }

            return log + (int)(value >> 1);
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2(ulong value)
        {
            int log = 0;
            if (0x1_0000_0000UL <= value)
            {
                value >>= 32;
                log = 32;
            }

            return log + Log2((uint)value);
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
