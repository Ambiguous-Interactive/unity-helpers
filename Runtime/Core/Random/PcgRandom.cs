// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is PCG (Permuted Congruential Generator), by Melissa O'Neill,
// Apache License 2.0, https://www.pcg-random.org/. This is an adaptation of that work; the design is
// the original author's. See docs/project/third-party-notices.md.

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using Helper;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// A high-quality, small-state pseudo-random number generator based on the PCG family.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementation based off of the reference PCG Random, found here: https://www.pcg-random.org/index.html
    /// </para>
    /// <para>
    /// PCG (Permuted Congruential Generator) offers excellent statistical quality with very small state
    /// and extremely fast generation. This implementation uses a 64-bit state with 32-bit outputs and
    /// an increment (stream selector) to avoid overlapping sequences when constructing multiple instances.
    /// </para>
    /// <para>
    /// Pros:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>Fast and allocation-free; suitable for gameplay hot paths.</description>
    /// </item>
    /// <item>
    /// <description>Great statistical quality for games and simulations; passes common PRNG test suites for 32-bit outputs.</description>
    /// </item>
    /// <item>
    /// <description>Deterministic and reproducible across platforms for identical seeds.</description>
    /// </item>
    /// <item>
    /// <description>Small state footprint; trivial to serialize via <see cref="RandomState"/>.</description>
    /// </item>
    /// </list>
    /// <para>
    /// Cons:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>Not cryptographically secure; do not use for security-sensitive tokens or secrets.</description>
    /// </item>
    /// <item>
    /// <description>32-bit outputs; if you need full 64-bit outputs, consider generating two uint values or using a 64-bit variant.</description>
    /// </item>
    /// </list>
    /// <para>
    /// When to use:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>General gameplay randomness, procedural content, Monte Carlo style sampling.</description>
    /// </item>
    /// <item>
    /// <description>Situations requiring deterministic replays by capturing and restoring <see cref="InternalState"/>.</description>
    /// </item>
    /// </list>
    /// <para>
    /// When not to use:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>Cryptographic or adversarial scenarios.</description>
    /// </item>
    /// <item>
    /// <description>When you specifically need UnityEngine.Random’s global state behavior; use <see cref="UnityRandom"/> for parity.</description>
    /// </item>
    /// </list>
    /// <para>
    /// Threading: prefer accessing via <c>ThreadLocalRandom&lt;PcgRandom&gt;.Instance</c> or <see cref="PRNG.Instance"/> (which returns the default PRNG)
    /// to avoid contention and accidental shared-state between threads.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using WallstopStudios.UnityHelpers.Core.Random;
    ///
    /// // Recommended: use the global default PRNG (thread-local instance)
    /// IRandom rng = PRNG.Instance; // currently Xoshiro256StarStar; swap to PcgRandom easily
    /// int value = rng.Next(0, 100);
    /// float probability = rng.NextFloat();
    /// bool coinFlip = rng.NextBool();
    ///
    /// // Deterministic playthrough: capture and restore state
    /// var seeded = new PcgRandom(seed: 123456789L);
    /// RandomState snapshot = seeded.InternalState;
    /// // ... generate values
    /// var replay = new PcgRandom(snapshot);
    /// // replay now yields identical sequence
    ///
    /// // Weighted selection (via extensions):
    /// // var index = rng.NextWeightedIndex(new float[] { 0.1f, 0.3f, 0.6f });
    /// </code>
    /// </example>
    [RandomGeneratorMetadata(
        RandomQuality.Excellent,
        "PCG XSH RR 64/32 variant; passes TestU01 BigCrush and PractRand in published results.",
        "O'Neill 2014",
        "https://www.pcg-random.org/paper.html",
        period: "2^64 (published)"
    )]
    [Serializable]
    [DataContract]
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
    public sealed partial class PcgRandom
        : AbstractRandom,
            IEquatable<PcgRandom>,
            IComparable,
            IComparable<PcgRandom>
    {
        // PCG requires an odd increment, and setting the low bit is what makes any value odd --
        // an already-odd one is its own answer, so no branch distinguishes the two cases.
        private static ulong NormalizeIncrement(ulong increment)
        {
            return increment | 1UL;
        }

        public static PcgRandom Instance => ThreadLocalRandom<PcgRandom>.Instance;

        public override RandomState InternalState => BuildState(_state, _increment);

        [ProtoMember(6)]
        [WProtoMember(6)]
        internal ulong _increment;

        [ProtoMember(7)]
        [WProtoMember(7)]
        internal ulong _state;

        public PcgRandom()
            : this(Guid.NewGuid()) { }

        public PcgRandom(Guid guid)
        {
            (ulong a, ulong b) = RandomUtilities.GuidToUInt64Pair(guid);
            _state = a;
            _increment = NormalizeIncrement(b);
        }

        [JsonConstructor]
        public PcgRandom(RandomState internalState)
        {
            _state = internalState.State1;
            _increment = NormalizeIncrement(internalState.State2);
            RestoreCommonState(internalState);
        }

        public PcgRandom(ulong increment, ulong state)
        {
            _increment = NormalizeIncrement(increment);
            _state = state;
        }

        public PcgRandom(long seed)
        {
            // Start with a nice prime
            _increment = NormalizeIncrement(6554638469UL);
            _state = unchecked((ulong)seed);
            _increment = NormalizeIncrement(NextUlong());
        }

        public override uint NextUint()
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

        protected override void OnAfterDeserialization()
        {
            _increment = NormalizeIncrement(_increment);
        }

        public bool Equals(PcgRandom other)
        {
            if (ReferenceEquals(other, null))
            {
                return false;
            }

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            return _increment == other._increment
                && _state == other._state
                && _cachedGaussian == other._cachedGaussian;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as PcgRandom);
        }

        public int CompareTo(PcgRandom other)
        {
            if (ReferenceEquals(other, null))
            {
                return 1;
            }

            if (_increment == other._increment)
            {
                if (_state == other._state)
                {
                    return 0;
                }

                if (_state < other._state)
                {
                    return -1;
                }

                return 1;
            }

            if (_increment < other._increment)
            {
                return -1;
            }

            if (_cachedGaussian.HasValue != other._cachedGaussian.HasValue)
            {
                return _cachedGaussian.HasValue ? -1 : 1;
            }

            if (!_cachedGaussian.HasValue)
            {
                return 0;
            }

            // ReSharper disable once PossibleInvalidOperationException
            return _cachedGaussian.Value.CompareTo(other._cachedGaussian.Value);
        }

        public int CompareTo(object obj)
        {
            return CompareTo(obj as PcgRandom);
        }

        public override int GetHashCode()
        {
            return Objects.HashCode(_increment, _state, _cachedGaussian);
        }

        public override string ToString()
        {
            return $"{{\"Increment\": {_increment}, \"State\": {_state}}}";
        }

        public override IRandom Copy()
        {
            return new PcgRandom(InternalState);
        }
    }
}
