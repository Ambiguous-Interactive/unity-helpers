// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is xoshiro256** 1.0, by David Blackman and Sebastiano Vigna,
// CC0 1.0 Universal (Public Domain), https://prng.di.unimi.it/xoshiro256starstar.c. This is an
// adaptation of that work; the design is the original authors'.
// See docs/project/third-party-notices.md.

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using Extension;
    using Helper;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// A 256-bit state xoshiro256** generator: the authors' all-purpose 64-bit generator, with no weak
    /// output bit and a native 64-bit word that <see cref="NextUlong"/> returns in a single state advance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other 64-bit generator in this package computes a full 64-bit word, returns half of it from
    /// <c>NextUint()</c>, and is then asked for two words to rebuild one in the inherited
    /// <c>NextUlong()</c>. This type overrides <see cref="NextUlong"/> directly, so <c>NextUlong</c>,
    /// <c>NextLong</c> and <c>NextDouble</c> each cost one advance rather than two.
    /// </para>
    /// <para>
    /// The `**` scrambler multiplies, rotates and multiplies again, so unlike the `+` scramblers no output
    /// bit is weaker than any other and single-bit consumers such as <see cref="AbstractRandom.NextBool"/>
    /// are as sound as the full word.
    /// </para>
    /// <para>Pros:</para>
    /// <list type="bullet">
    /// <item><description>All output bits are equally strong; safe for <c>NextBool</c> and low-bit masks.</description></item>
    /// <item><description>Native 64-bit word: one advance per <c>NextUlong</c>, <c>NextLong</c> or <c>NextDouble</c>.</description></item>
    /// <item><description>Period 2^256-1; state large enough for heavily parallel world generation.</description></item>
    /// <item><description>Deterministic and reproducible across platforms.</description></item>
    /// </list>
    /// <para>Cons:</para>
    /// <list type="bullet">
    /// <item><description>Not cryptographically secure.</description></item>
    /// <item><description><c>NextUint</c> returns the upper half of a 64-bit word and discards the lower half; prefer <see cref="Xoshiro128StarStar"/> for 32-bit-dominated workloads and 32-bit targets.</description></item>
    /// <item><description>Reading <see cref="InternalState"/> maintains a 16-byte payload for the two state words that do not fit the two serialized state slots.</description></item>
    /// </list>
    /// <para>When to use:</para>
    /// <list type="bullet">
    /// <item><description>General-purpose gameplay and procedural generation, especially <c>NextDouble</c>-heavy sampling.</description></item>
    /// </list>
    /// <para>When not to use:</para>
    /// <list type="bullet">
    /// <item><description>Security or adversarial contexts.</description></item>
    /// </list>
    /// <para>
    /// Threading: Prefer <c>ThreadLocalRandom&lt;Xoshiro256StarStar&gt;.Instance</c> to avoid sharing state across threads.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using WallstopStudios.UnityHelpers.Core.Random;
    ///
    /// IRandom rng = new Xoshiro256StarStar(Guid.NewGuid());
    /// double sample = rng.NextDouble(); // one state advance, not two
    ///
    /// // Save/restore for deterministic replays
    /// RandomState state = rng.InternalState;
    /// IRandom replay = new Xoshiro256StarStar(state);
    /// </code>
    /// </example>
    [RandomGeneratorMetadata(
        RandomQuality.Excellent,
        "xoshiro256** 1.0; the ** scrambler leaves no weak bit, and the native 64-bit word means NextUlong costs one state advance instead of the two every other 64-bit generator here needs.",
        "Blackman & Vigna 2018",
        "https://prng.di.unimi.it/xoshiro256starstar.c",
        period: "2^256-1 (published)"
    )]
    [Serializable]
    [DataContract]
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
    public sealed partial class Xoshiro256StarStar
        : AbstractRandom,
            IEquatable<Xoshiro256StarStar>,
            IComparable,
            IComparable<Xoshiro256StarStar>
    {
        private const int UlongByteCount = sizeof(ulong);
        private const int StatePayloadLength = UlongByteCount * 2;

        public static Xoshiro256StarStar Instance => ThreadLocalRandom<Xoshiro256StarStar>.Instance;

        public override RandomState InternalState
        {
            get
            {
                byte[] payload = _payload;
                if (payload == null)
                {
                    payload = new byte[StatePayloadLength];
                    _payload = payload;
                }

                BinaryPrimitives.WriteUInt64LittleEndian(payload, _s2);
                BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(UlongByteCount), _s3);
                return BuildState(_s0, _s1, payload: payload);
            }
        }

        [ProtoMember(6)]
        [WProtoMember(6)]
        internal ulong _s0;

        [ProtoMember(7)]
        [WProtoMember(7)]
        internal ulong _s1;

        [ProtoMember(8)]
        [WProtoMember(8)]
        internal ulong _s2;

        [ProtoMember(9)]
        [WProtoMember(9)]
        internal ulong _s3;

        // Scratch for InternalState only; never part of the generator's value or its serialized form.
        private byte[] _payload;

        private void EnsureNonZeroState()
        {
            if ((_s0 | _s1 | _s2 | _s3) == 0)
            {
                _s0 = 0x9E3779B97F4A7C15UL;
                _s1 = 0xBF58476D1CE4E5B9UL;
                _s2 = 0x94D049BB133111EBUL;
                _s3 = 0xD1B54A32D192ED03UL;
            }
        }

        public Xoshiro256StarStar()
            : this(Guid.NewGuid()) { }

        public Xoshiro256StarStar(Guid guid)
        {
            (ulong a, ulong b) = RandomUtilities.GuidToUInt64Pair(guid);
            // A Guid carries 128 bits and the state needs 256, so the two remaining words come from the
            // authors' recommended SplitMix64 expansion rather than from repeating the seed.
            _s0 = a;
            _s1 = b;
            _s2 = SplitMix64Next(ref a);
            _s3 = SplitMix64Next(ref b);
            EnsureNonZeroState();
        }

        public Xoshiro256StarStar(ulong seed0, ulong seed1, ulong seed2, ulong seed3)
        {
            _s0 = seed0;
            _s1 = seed1;
            _s2 = seed2;
            _s3 = seed3;
            EnsureNonZeroState();
        }

        [JsonConstructor]
        public Xoshiro256StarStar(RandomState internalState)
        {
            _s0 = internalState.State1;
            _s1 = internalState.State2;
            if (!TryReadStatePayload(internalState.PayloadBytes, out _s2, out _s3))
            {
                ulong seed = internalState.State1 ^ internalState.State2;
                _s2 = SplitMix64Next(ref seed);
                _s3 = SplitMix64Next(ref seed);
            }

            RestoreCommonState(internalState);
            EnsureNonZeroState();
        }

        protected override void OnAfterDeserialization()
        {
            EnsureNonZeroState();
        }

        public override ulong NextUlong()
        {
            return NextWord();
        }

        public override uint NextUint()
        {
            unchecked
            {
                return (uint)(NextWord() >> 32);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong NextWord()
        {
            unchecked
            {
                ulong result = Rotl(_s1 * 5UL, 7) * 9UL;

                ulong t = _s1 << 17;
                _s2 ^= _s0;
                _s3 ^= _s1;
                _s1 ^= _s2;
                _s0 ^= _s3;
                _s2 ^= t;
                _s3 = Rotl(_s3, 45);

                return result;
            }
        }

        public override IRandom Copy()
        {
            return new Xoshiro256StarStar(InternalState);
        }

        private static bool TryReadStatePayload(
            IReadOnlyList<byte> payload,
            out ulong s2,
            out ulong s3
        )
        {
            if (payload is not { Count: >= StatePayloadLength })
            {
                s2 = 0;
                s3 = 0;
                return false;
            }

            Span<byte> buffer = stackalloc byte[StatePayloadLength];
            for (int i = 0; i < StatePayloadLength; ++i)
            {
                buffer[i] = payload[i];
            }

            s2 = BinaryPrimitives.ReadUInt64LittleEndian(buffer);
            s3 = BinaryPrimitives.ReadUInt64LittleEndian(buffer[UlongByteCount..]);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong SplitMix64Next(ref ulong state)
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                ulong z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Rotl(ulong x, int k)
        {
            return (x << k) | (x >> (64 - k));
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Xoshiro256StarStar);
        }

        public bool Equals(Xoshiro256StarStar other)
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
            return CompareTo(obj as Xoshiro256StarStar);
        }

        public int CompareTo(Xoshiro256StarStar other)
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
