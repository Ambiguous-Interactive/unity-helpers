// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is xoshiro128** 1.1, by David Blackman and Sebastiano Vigna,
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
    /// A 128-bit state, natively 32-bit xoshiro128** generator whose every output bit passes the
    /// batteries its authors run, making it the safest choice for single-bit and low-bit consumers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike the `+` scramblers, whose lowest bits are linear and are documented by their own authors as
    /// failing linearity tests, the `**` scrambler multiplies, rotates and multiplies again, so no bit of
    /// the result is weaker than any other. That matters here because <see cref="AbstractRandom.NextBool"/>
    /// reads a single bit and <c>Next(0, powerOfTwo)</c> masks the low bits.
    /// </para>
    /// <para>
    /// The generator's native word is 32 bits, which is exactly the width <see cref="NextUint"/> returns, so
    /// a call consumes one state advance and discards nothing.
    /// </para>
    /// <para>Pros:</para>
    /// <list type="bullet">
    /// <item><description>All output bits are equally strong; safe for <c>NextBool</c>, bit masks and low-bit extraction.</description></item>
    /// <item><description>Native 32-bit word; no discarded half, and no 64-bit multiply on 32-bit targets such as WebGL.</description></item>
    /// <item><description>Period 2^128-1 with a 128-bit state that fits the serialized state words exactly.</description></item>
    /// <item><description>Deterministic and reproducible across platforms.</description></item>
    /// </list>
    /// <para>Cons:</para>
    /// <list type="bullet">
    /// <item><description>Not cryptographically secure.</description></item>
    /// <item><description>Two state advances per 64-bit draw; prefer <see cref="Xoshiro256StarStar"/> when <c>NextUlong</c> or <c>NextDouble</c> dominates.</description></item>
    /// </list>
    /// <para>When to use:</para>
    /// <list type="bullet">
    /// <item><description>Gameplay randomness dominated by <c>NextBool</c>, small ranges, shuffles and 32-bit draws.</description></item>
    /// <item><description>WebGL and other 32-bit targets where 64-bit arithmetic is emulated.</description></item>
    /// </list>
    /// <para>When not to use:</para>
    /// <list type="bullet">
    /// <item><description>Security or adversarial contexts.</description></item>
    /// </list>
    /// <para>
    /// Threading: Prefer <c>ThreadLocalRandom&lt;Xoshiro128StarStar&gt;.Instance</c> to avoid sharing state across threads.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using WallstopStudios.UnityHelpers.Core.Random;
    ///
    /// IRandom rng = new Xoshiro128StarStar(Guid.NewGuid());
    /// bool crit = rng.NextBool(); // every bit is strong, unlike the `+` scramblers
    ///
    /// // Save/restore for deterministic replays
    /// RandomState state = rng.InternalState;
    /// IRandom replay = new Xoshiro128StarStar(state);
    /// </code>
    /// </example>
    [RandomGeneratorMetadata(
        RandomQuality.Excellent,
        "xoshiro128** 1.1; the ** scrambler leaves no weak bit, so NextBool and low-bit masks are as strong as the full word. Native 32-bit output, so NextUint discards nothing.",
        "Blackman & Vigna 2018",
        "https://prng.di.unimi.it/xoshiro128starstar.c"
    )]
    [Serializable]
    [DataContract]
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
    public sealed partial class Xoshiro128StarStar
        : AbstractRandom,
            IEquatable<Xoshiro128StarStar>,
            IComparable,
            IComparable<Xoshiro128StarStar>
    {
        private const int UintBitCount = sizeof(uint) * 8;

        public static Xoshiro128StarStar Instance => ThreadLocalRandom<Xoshiro128StarStar>.Instance;

        public override RandomState InternalState =>
            BuildState(((ulong)_s0 << UintBitCount) | _s1, ((ulong)_s2 << UintBitCount) | _s3);

        [ProtoMember(6)]
        [WProtoMember(6)]
        internal uint _s0;

        [ProtoMember(7)]
        [WProtoMember(7)]
        internal uint _s1;

        [ProtoMember(8)]
        [WProtoMember(8)]
        internal uint _s2;

        [ProtoMember(9)]
        [WProtoMember(9)]
        internal uint _s3;

        private void EnsureNonZeroState()
        {
            if ((_s0 | _s1 | _s2 | _s3) == 0)
            {
                _s0 = 0x9E3779B9U;
                _s1 = 0x243F6A88U;
                _s2 = 0xB7E15162U;
                _s3 = 0x85A308D3U;
            }
        }

        public Xoshiro128StarStar()
            : this(Guid.NewGuid()) { }

        public Xoshiro128StarStar(Guid guid)
        {
            (ulong a, ulong b) = RandomUtilities.GuidToUInt64Pair(guid);
            unchecked
            {
                _s0 = (uint)(a >> UintBitCount);
                _s1 = (uint)a;
                _s2 = (uint)(b >> UintBitCount);
                _s3 = (uint)b;
            }
            EnsureNonZeroState();
        }

        public Xoshiro128StarStar(uint seed0, uint seed1, uint seed2, uint seed3)
        {
            _s0 = seed0;
            _s1 = seed1;
            _s2 = seed2;
            _s3 = seed3;
            EnsureNonZeroState();
        }

        [JsonConstructor]
        public Xoshiro128StarStar(RandomState internalState)
        {
            unchecked
            {
                _s0 = (uint)(internalState.State1 >> UintBitCount);
                _s1 = (uint)internalState.State1;
                _s2 = (uint)(internalState.State2 >> UintBitCount);
                _s3 = (uint)internalState.State2;
            }
            RestoreCommonState(internalState);
            EnsureNonZeroState();
        }

        protected override void OnAfterDeserialization()
        {
            EnsureNonZeroState();
        }

        public override uint NextUint()
        {
            unchecked
            {
                uint result = Rotl(_s1 * 5U, 7) * 9U;

                uint t = _s1 << 9;
                _s2 ^= _s0;
                _s3 ^= _s1;
                _s1 ^= _s2;
                _s0 ^= _s3;
                _s2 ^= t;
                _s3 = Rotl(_s3, 11);

                return result;
            }
        }

        public override IRandom Copy()
        {
            return new Xoshiro128StarStar(InternalState);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Rotl(uint x, int k)
        {
            return (x << k) | (x >> (UintBitCount - k));
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Xoshiro128StarStar);
        }

        public bool Equals(Xoshiro128StarStar other)
        {
            if (other == null)
            {
                return false;
            }

            return _s0 == other._s0 && _s1 == other._s1 && _s2 == other._s2 && _s3 == other._s3;
        }

        public override int GetHashCode()
        {
            return Objects.HashCode(_s0, _s1, _s2, _s3);
        }

        public override string ToString()
        {
            return this.ToJson();
        }

        public int CompareTo(object obj)
        {
            return CompareTo(obj as Xoshiro128StarStar);
        }

        public int CompareTo(Xoshiro128StarStar other)
        {
            if (other == null)
            {
                return 1;
            }

            int comparison = _s0.CompareTo(other._s0);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = _s1.CompareTo(other._s1);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = _s2.CompareTo(other._s2);
            if (comparison != 0)
            {
                return comparison;
            }

            return _s3.CompareTo(other._s3);
        }
    }
}
