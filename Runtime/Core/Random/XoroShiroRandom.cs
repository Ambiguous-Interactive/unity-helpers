// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is xoroshiro/xoshiro, by David Blackman and Sebastiano Vigna,
// CC0 1.0 Universal (Public Domain), https://prng.di.unimi.it/xoshiro128starstar.c. This is an
// adaptation of that work; the design is the original authors'.
// See docs/project/third-party-notices.md.

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using Extension;
    using Helper;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// A fast 128-bit state Xoroshiro-based PRNG with good quality and tiny footprint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Xoroshiro family generators (here in a 64/64 configuration) offer an excellent balance between speed and quality
    /// for real-time applications. This implementation maintains two 64-bit state variables and returns 32-bit outputs.
    /// </para>
    /// <para>Pros:</para>
    /// <list type="bullet">
    /// <item><description>Very fast; suitable for gameplay and procedural generation.</description></item>
    /// <item><description>Good statistical properties for non-crypto use; long period (~2^128−1).</description></item>
    /// <item><description>Deterministic and reproducible across platforms.</description></item>
    /// </list>
    /// <para>Cons:</para>
    /// <list type="bullet">
    /// <item><description>Not cryptographically secure.</description></item>
    /// <item><description>Low bits may show weaker properties in some variants; use full width for mixing.</description></item>
    /// </list>
    /// <para>When to use:</para>
    /// <list type="bullet">
    /// <item><description>General-purpose game randomness, procedural placement, shuffles, noise seeding.</description></item>
    /// </list>
    /// <para>When not to use:</para>
    /// <list type="bullet">
    /// <item><description>Security or adversarial contexts.</description></item>
    /// </list>
    /// <para>
    /// Threading: Prefer <c>ThreadLocalRandom&lt;XoroShiroRandom&gt;.Instance</c> to avoid sharing state across threads.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using WallstopStudios.UnityHelpers.Core.Random;
    ///
    /// IRandom rng = new XoroShiroRandom(Guid.NewGuid());
    /// float t = rng.NextFloat();
    /// Color c = rng.NextColor(); // via RandomExtensions
    ///
    /// // Save/restore for deterministic replays
    /// var state = rng.InternalState;
    /// var replay = new XoroShiroRandom(state);
    /// </code>
    /// </example>
    [RandomGeneratorMetadata(
        RandomQuality.VeryGood,
        "xoshiro128** variant; authors recommend for general-purpose use and report clean BigCrush performance with jump functions.",
        "Blackman & Vigna 2019",
        "https://prng.di.unimi.it/xoshiro128starstar.c"
    )]
    [Serializable]
    [DataContract]
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
    public sealed partial class XoroShiroRandom
        : AbstractRandom,
            IEquatable<XoroShiroRandom>,
            IComparable,
            IComparable<XoroShiroRandom>
    {
        public static XoroShiroRandom Instance => ThreadLocalRandom<XoroShiroRandom>.Instance;

        public override RandomState InternalState => BuildState(_s0, _s1);

        [ProtoMember(6)]
        [WProtoMember(6)]
        internal ulong _s0;

        [ProtoMember(7)]
        [WProtoMember(7)]
        internal ulong _s1;

        private void EnsureNonZeroState()
        {
            if ((_s0 | _s1) == 0)
            {
                _s0 = 0x9E3779B97F4A7C15UL;
                _s1 = 0xD1B54A32D192ED03UL;
            }
        }

        public XoroShiroRandom()
            : this(Guid.NewGuid()) { }

        public XoroShiroRandom(Guid guid)
        {
            (ulong a, ulong b) = RandomUtilities.GuidToUInt64Pair(guid);
            _s0 = a;
            _s1 = b;
            EnsureNonZeroState();
        }

        public XoroShiroRandom(ulong seed1, ulong seed2)
        {
            _s0 = seed1;
            _s1 = seed2;
            EnsureNonZeroState();
        }

        [JsonConstructor]
        public XoroShiroRandom(RandomState internalState)
        {
            _s0 = internalState.State1;
            _s1 = internalState.State2;
            RestoreCommonState(internalState);
            EnsureNonZeroState();
        }

        public override uint NextUint()
        {
            unchecked
            {
                EnsureNonZeroState();
                ulong s0 = _s0;
                ulong s1 = _s1;
                ulong result = s0 + s1;

                s1 ^= s0;
                _s0 = Rotl(s0, 24) ^ s1 ^ (s1 << 16);
                _s1 = Rotl(s1, 37);

                return (uint)result;
            }
        }

        public override IRandom Copy()
        {
            return new XoroShiroRandom(InternalState);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Rotl(ulong x, int k)
        {
            return (x << k) | (x >> (64 - k));
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as XoroShiroRandom);
        }

        public bool Equals(XoroShiroRandom other)
        {
            if (other == null)
            {
                return false;
            }

            return _s0 == other._s0 && _s1 == other._s1;
        }

        public override int GetHashCode()
        {
            return Objects.HashCode(_s0, _s1);
        }

        public override string ToString()
        {
            return this.ToJson();
        }

        public int CompareTo(object obj)
        {
            return CompareTo(obj as XoroShiroRandom);
        }

        public int CompareTo(XoroShiroRandom other)
        {
            if (other == null)
            {
                return -1;
            }

            int comparison = _s0.CompareTo(other._s0);
            if (comparison != 0)
            {
                return comparison;
            }

            return _s1.CompareTo(other._s1);
        }
    }
}
