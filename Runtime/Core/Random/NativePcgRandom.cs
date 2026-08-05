// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;

    /// <summary>
    /// A lightweight PCG-based struct RNG intended for low-overhead use (e.g., Burst/Jobs-friendly contexts).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provides a minimal, non-allocating random number source patterned after PCG. Because it is a <c>struct</c>,
    /// it is copied by value; take care to pass by ref where you want a single advancing sequence. Not wired into
    /// <see cref="IRandom"/>; use this when allocations or interface dispatch must be avoided.
    /// </para>
    /// <para>Pros:</para>
    /// <list type="bullet">
    /// <item><description>No allocations, tiny footprint; suitable for tight inner loops.</description></item>
    /// <item><description>Deterministic with good general-purpose quality.</description></item>
    /// </list>
    /// <para>Cons:</para>
    /// <list type="bullet">
    /// <item><description>Value-type semantics—accidental copying can lead to diverging sequences. The buffered
    /// coin-flip bits are part of that state, so a copy taken mid-buffer replays those flips.</description></item>
    /// <item><description>Not cryptographically secure; limited convenience API.</description></item>
    /// </list>
    /// <para>
    /// Numeric behavior matches <see cref="PcgRandom"/> and <see cref="AbstractRandom"/>: <c>NextFloat</c> and
    /// <c>NextDouble</c> return <c>[0, 1)</c>, <c>NextLong</c> is non-negative, and <c>NextUint(max)</c> is
    /// unbiased. Sequences are not comparable to <see cref="PcgRandom"/>'s for the same seed—this type keeps its
    /// own draw order.
    /// </para>
    /// <para>When to use:</para>
    /// <list type="bullet">
    /// <item><description>Performance-critical contexts (Burst/Jobs) where you control by-ref semantics.</description></item>
    /// </list>
    /// <para>When not to use:</para>
    /// <list type="bullet">
    /// <item><description>General gameplay—prefer <see cref="PRNG.Instance"/> with the richer <see cref="IRandom"/> API.</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// var rng = new NativePcgRandom(seed: 123);
    /// uint u = rng.NextUint();
    /// bool b = rng.NextBool();
    /// float t = rng.NextFloat();
    /// double d = rng.NextDouble();
    /// </code>
    /// </example>
    public struct NativePcgRandom
    {
        private const int MaxRejectionAttempts32 = 1 << 16;

        // internal, matching PcgRandom, so the parity contract below is assertable
        // without reflecting on our own code.
        internal readonly ulong _increment;
        private ulong _state;
        private uint _bitBuffer;
        private int _bitCount;

        /// <summary>
        /// Initializes the generator from a <see cref="Guid"/>.
        /// </summary>
        /// <param name="seed">Seed material; both halves are used.</param>
        public NativePcgRandom(Guid seed)
        {
            (ulong a, ulong b) = RandomUtilities.GuidToUInt64Pair(seed);
            _state = a;
            _increment = NormalizeIncrement(b);
            _bitBuffer = 0;
            _bitCount = 0;
        }

        /// <summary>
        /// Initializes the generator from an integer seed.
        /// </summary>
        /// <param name="seed">Seed value; every value produces a distinct stream.</param>
        public NativePcgRandom(int seed)
        {
            // Start with a nice prime, matching PcgRandom's integer-seeded constructor.
            _increment = NormalizeIncrement(6554638469UL);
            _state = unchecked((ulong)seed);
            _bitBuffer = 0;
            _bitCount = 0;
            _increment = NormalizeIncrement(NextUlong());
        }

        // PCG's LCG step is only full-period when the increment is odd; an even one
        // collapses the sequence. PcgRandom normalizes at every construction site and
        // this type did not, so 75% of integer seeds and half of Guid seeds produced a
        // degenerate stream.
        private static ulong NormalizeIncrement(ulong increment)
        {
            return (increment & 1UL) == 0 ? increment | 1UL : increment;
        }

        /// <summary>
        /// Produces the next 32-bit sample.
        /// </summary>
        /// <returns>A uniformly distributed <see cref="uint"/>.</returns>
        public uint NextUint()
        {
            unchecked
            {
                ulong oldState = _state;
                _state = oldState * 6364136223846793005UL + _increment;
                uint xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
                int rot = (int)(oldState >> 59);
                return (xorShifted >> rot) | (xorShifted << (-rot & 31));
            }
        }

        /// <summary>
        /// Produces a non-negative <see cref="int"/> below <paramref name="max"/>.
        /// </summary>
        /// <param name="max">Exclusive upper bound; must be positive.</param>
        /// <returns>A value in <c>[0, max)</c>.</returns>
        public int Next(int max)
        {
            if (max <= 0)
            {
                throw new ArgumentException($"Max {max} cannot be less-than or equal-to 0");
            }

            return unchecked((int)NextUint(unchecked((uint)max)));
        }

        /// <summary>
        /// Produces a <see cref="uint"/> below <paramref name="max"/> without modulo bias.
        /// </summary>
        /// <param name="max">Exclusive upper bound; must be non-zero.</param>
        /// <returns>A value in <c>[0, max)</c>.</returns>
        public uint NextUint(uint max)
        {
            if (max == 0)
            {
                throw new ArgumentException("Max cannot be zero");
            }

            // Power-of-two fast path
            if ((max & (max - 1)) == 0)
            {
                return NextUint() & (max - 1);
            }

            // Lemire's method (32-bit): take high 32 bits of r*max
            uint r = NextUint();
            ulong m = (ulong)r * max;
            uint lo = (uint)m;
            if (lo < max)
            {
                uint t = unchecked((0u - max) % max);
                int attempts = 0;
                while (lo < t)
                {
                    if (++attempts > MaxRejectionAttempts32)
                    {
                        // Prevent infinite loop: fall back to modulo (small bias) rather than hang
                        return r % max;
                    }
                    r = NextUint();
                    m = (ulong)r * max;
                    lo = (uint)m;
                }
            }
            return (uint)(m >> 32);
        }

        /// <summary>
        /// Produces a non-negative <see cref="long"/>.
        /// </summary>
        /// <returns>A value in <c>[0, long.MaxValue]</c>.</returns>
        public long NextLong()
        {
            unchecked
            {
                return (long)(NextUlong() & 0x7FFFFFFFFFFFFFFF);
            }
        }

        /// <summary>
        /// Produces the next 64-bit sample.
        /// </summary>
        /// <returns>A uniformly distributed <see cref="ulong"/>.</returns>
        public ulong NextUlong()
        {
            uint upper = NextUint();
            uint lower = NextUint();
            return ((ulong)upper << 32) | lower;
        }

        /// <summary>
        /// Produces a fair coin flip.
        /// </summary>
        /// <returns>True or false with equal probability.</returns>
        /// <remarks>
        /// Consumes one bit of a buffered 32-bit sample, so 32 flips cost one PCG step
        /// rather than 32.
        /// </remarks>
        public bool NextBool()
        {
            if (_bitCount == 0)
            {
                _bitBuffer = NextUint();
                _bitCount = 32;
            }
            bool bit = (_bitBuffer & 1u) == 0;
            _bitBuffer >>= 1;
            _bitCount--;
            return bit;
        }

        /// <summary>
        /// Produces a <see cref="float"/> in <c>[0, 1)</c>.
        /// </summary>
        /// <returns>A value that is never 1.</returns>
        public float NextFloat()
        {
            // Use 24 random bits for float mantissa
            return (NextUint() >> 8) * (1f / (1 << 24));
        }

        /// <summary>
        /// Produces a <see cref="double"/> in <c>[0, 1)</c>.
        /// </summary>
        /// <returns>A value that is never 1.</returns>
        public double NextDouble()
        {
            // 53 random bits from a 64-bit sample
            const double scale = 1.0 / 9007199254740992.0; // 2^53
            ulong combined = NextUlong() >> 11;
            return combined * scale;
        }
    }
}
