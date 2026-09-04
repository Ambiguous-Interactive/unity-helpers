// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is SplitMix64, by David Blackman and Sebastiano Vigna, CC0 1.0 Universal
// (Public Domain), https://prng.di.unimi.it/splitmix64.c. This is an adaptation of that work; the
// design is the original authors'. See docs/project/third-party-notices.md.

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// A fast 64-bit SplitMix generator often used as a high-quality seeding/mixing PRNG.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SplitMix64 is widely used to quickly generate well-distributed 64-bit values and as a seed source for
    /// other generators. In this implementation, 32-bit outputs are produced from the mixed 64-bit state.
    /// </para>
    /// <para>Pros:</para>
    /// <list type="bullet">
    /// <item><description>Very fast; great as a hash/mixer and for seed generation.</description></item>
    /// <item><description>Deterministic, portable, and simple.</description></item>
    /// </list>
    /// <para>Cons:</para>
    /// <list type="bullet">
    /// <item><description>Not cryptographically secure.</description></item>
    /// <item><description>If you require 64-bit outputs, prefer consuming the full 64-bit mixed value.</description></item>
    /// </list>
    /// <para>When to use:</para>
    /// <list type="bullet">
    /// <item><description>Producing seeds for other PRNGs; quick hash-like mixing; gameplay randomness.</description></item>
    /// </list>
    /// <para>When not to use:</para>
    /// <list type="bullet">
    /// <item><description>Security-sensitive scenarios.</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// using WallstopStudios.UnityHelpers.Core.Random;
    ///
    /// var seedSource = new SplitMix64(123UL);
    /// // Use to seed another RNG
    /// var seeded = new XoroShiroRandom(seedSource.NextUlong(), seedSource.NextUlong());
    /// int v = seeded.Next(0, 10);
    /// </code>
    /// </example>
    [RandomGeneratorMetadata(
        RandomQuality.VeryGood,
        "Well-known SplitMix64 mixer; passes TestU01 BigCrush and PractRand up to large data sizes in literature.",
        "Vigna 2014",
        "https://prng.di.unimi.it/splitmix64.c",
        period: "2^64 (published)"
    )]
    [Serializable]
    [DataContract]
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
    [WProtoSubtype(typeof(AbstractRandom), 110)]
    public sealed partial class SplitMix64
        : AbstractRandom,
            IEquatable<SplitMix64>,
            IComparable,
            IComparable<SplitMix64>
    {
        public static SplitMix64 Instance => ThreadLocalRandom<SplitMix64>.Instance;

        public override RandomState InternalState => BuildState(_state);

        [ProtoMember(6)]
        [WProtoMember(6)]
        internal ulong _state;

        public SplitMix64()
            : this(Guid.NewGuid()) { }

        public SplitMix64(Guid guid)
            : this(RandomUtilities.GuidToUInt64Pair(guid).First) { }

        public SplitMix64(ulong seed)
        {
            _state = seed;
        }

        [JsonConstructor]
        public SplitMix64(RandomState internalState)
        {
            _state = internalState.State1;
            RestoreCommonState(internalState);
        }

        public override uint NextUint()
        {
            unchecked
            {
                return (uint)NextWord();
            }
        }

        public override ulong NextUlong()
        {
            return NextWord();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong NextWord()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;

                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;

                return z;
            }
        }

        public override IRandom Copy()
        {
            return new SplitMix64(InternalState);
        }

        public bool Equals(SplitMix64 other)
        {
            if (other == null)
            {
                return false;
            }

            return _state == other._state;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SplitMix64);
        }

        public int CompareTo(object obj)
        {
            return CompareTo(obj as SplitMix64);
        }

        public int CompareTo(SplitMix64 other)
        {
            if (other == null)
            {
                return 1;
            }

            return _state.CompareTo(other._state);
        }

        public override int GetHashCode()
        {
            return _state.GetHashCode();
        }

        public override string ToString()
        {
            return $"{{\"State\": {_state}}}";
        }
    }
}
