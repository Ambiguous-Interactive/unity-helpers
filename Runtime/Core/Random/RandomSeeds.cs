// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The mixer in this file follows Sebastiano Vigna's SplitMix64 reference implementation,
// CC0 1.0 Universal (Public Domain), https://prng.di.unimi.it/splitmix64.c.

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System.Runtime.CompilerServices;

    /// <summary>Provides deterministic 64-bit mixing and keyed seed derivation.</summary>
    /// <remarks>
    /// Numeric keys are folded in order as
    /// <c>Mix64(state + 0x9E3779B97F4A7C15 * (key + 1))</c>. Text domains use FNV-1a-64 over
    /// replacement-fallback UTF-8 bytes before that fold; null is the empty domain. These
    /// algorithms are part of the reproducibility contract and produce the same result across
    /// supported processes and platforms. They distribute seed bits; they are not cryptographic
    /// hashes and must not be used for secrets or adversarial input.
    /// </remarks>
    /// <example>
    /// <code>
    /// ulong roomSeed = RandomSeeds.Derive(worldSeed, roomIndex, attemptIndex);
    /// IRandom roomRandom = new SplitMix64(roomSeed);
    /// </code>
    /// </example>
    public static class RandomSeeds
    {
        private const ulong Fnv64OffsetBasis = 14695981039346656037UL;
        private const ulong Fnv64Prime = 1099511628211UL;
        private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

        /// <summary>Applies the SplitMix64 finalizer, a bijection over 64-bit values.</summary>
        /// <param name="value">The value to mix.</param>
        /// <returns>The mixed value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Mix64(ulong value)
        {
            unchecked
            {
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }

        /// <summary>Derives a reproducible seed from a root seed and one key.</summary>
        /// <param name="rootSeed">The root seed.</param>
        /// <param name="key">The ordered derivation key.</param>
        /// <returns>A mixed seed for the key.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Derive(ulong rootSeed, ulong key)
        {
            unchecked
            {
                return Mix64(rootSeed + (GoldenGamma * (key + 1UL)));
            }
        }

        /// <summary>Derives a reproducible seed from a root seed and two ordered keys.</summary>
        /// <param name="rootSeed">The root seed.</param>
        /// <param name="key0">The first derivation key.</param>
        /// <param name="key1">The second derivation key.</param>
        /// <returns>A mixed seed for the ordered keys.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Derive(ulong rootSeed, ulong key0, ulong key1)
        {
            return Derive(Derive(rootSeed, key0), key1);
        }

        /// <summary>Derives a reproducible seed from a root seed and three ordered keys.</summary>
        /// <param name="rootSeed">The root seed.</param>
        /// <param name="key0">The first derivation key.</param>
        /// <param name="key1">The second derivation key.</param>
        /// <param name="key2">The third derivation key.</param>
        /// <returns>A mixed seed for the ordered keys.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Derive(ulong rootSeed, ulong key0, ulong key1, ulong key2)
        {
            return Derive(Derive(rootSeed, key0, key1), key2);
        }

        /// <summary>Derives a reproducible seed from a root seed and a UTF-8 domain.</summary>
        /// <param name="rootSeed">The root seed.</param>
        /// <param name="domain">The domain text. Null is treated as an empty domain.</param>
        /// <returns>A mixed seed for the stable domain hash.</returns>
        public static ulong Derive(ulong rootSeed, string domain)
        {
            return Derive(rootSeed, StableUtf8Hash64(domain ?? string.Empty));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong AddByte(ulong hash, byte value)
        {
            unchecked
            {
                return (hash ^ value) * Fnv64Prime;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong AddScalar(ulong hash, uint scalar)
        {
            if (scalar <= 0x7FU)
            {
                return AddByte(hash, (byte)scalar);
            }

            if (scalar <= 0x7FFU)
            {
                hash = AddByte(hash, (byte)(0xC0U | (scalar >> 6)));
                return AddByte(hash, (byte)(0x80U | (scalar & 0x3FU)));
            }

            if (scalar <= 0xFFFFU)
            {
                hash = AddByte(hash, (byte)(0xE0U | (scalar >> 12)));
                hash = AddByte(hash, (byte)(0x80U | ((scalar >> 6) & 0x3FU)));
                return AddByte(hash, (byte)(0x80U | (scalar & 0x3FU)));
            }

            hash = AddByte(hash, (byte)(0xF0U | (scalar >> 18)));
            hash = AddByte(hash, (byte)(0x80U | ((scalar >> 12) & 0x3FU)));
            hash = AddByte(hash, (byte)(0x80U | ((scalar >> 6) & 0x3FU)));
            return AddByte(hash, (byte)(0x80U | (scalar & 0x3FU)));
        }

        private static ulong StableUtf8Hash64(string value)
        {
            ulong hash = Fnv64OffsetBasis;
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                uint scalar = character;
                if (
                    char.IsHighSurrogate(character)
                    && index + 1 < value.Length
                    && char.IsLowSurrogate(value[index + 1])
                )
                {
                    scalar =
                        0x10000U
                        + (((uint)character - 0xD800U) << 10)
                        + ((uint)value[++index] - 0xDC00U);
                }
                else if (char.IsSurrogate(character))
                {
                    scalar = 0xFFFDU;
                }

                hash = AddScalar(hash, scalar);
            }

            return hash;
        }
    }
}
