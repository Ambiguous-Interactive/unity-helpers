// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is sfc64 (Small Fast Chaotic), by Chris Doty-Humphrey, as distributed
// with the PractRand test suite and under MIT license in Melissa E. O'Neill's reference adaptation
// (https://gist.github.com/imneme/f1f7821f07cf76504a97f6537c818083). This is an adaptation of that
// work; the design is the original author's. Seeding follows the canonical formula: the counter
// starts at 1 and the generator is advanced twelve times before its first draw.
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
    /// An sfc64 generator: three 64-bit state words plus a draw counter, a current-generation
    /// generator that pairs very high statistical quality with a very small hot path (one add, one
    /// rotate, two shifts and an xor per 64-bit draw).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The embedded counter increments on every draw, so the full state cannot repeat before 2^64
    /// draws; the warm-up seeding discipline escapes the degenerate all-zero corner. Like
    /// <see cref="Xoshiro256StarStar"/>, this type overrides <see cref="NextUlong"/> to answer a
    /// 64-bit draw with a single state advance rather than composing one from two 32-bit halves.
    /// </para>
    /// <para>
    /// The output word is an arithmetic sum, so its low bits are weaker than its high bits (the low
    /// bits of an add never see the carries). <see cref="NextUint"/> therefore returns the upper
    /// half of the word, where every mixed bit arrives; the full word is still available through
    /// <see cref="NextUlong"/>, exactly as the published generator produces it.
    /// </para>
    /// <para>Pros:</para>
    /// <list type="bullet">
    /// <item><description>Very high statistical quality with an extremely short hot path.</description></item>
    /// <item><description>Native 64-bit word: one advance per <c>NextUlong</c>, <c>NextLong</c> or <c>NextDouble</c>.</description></item>
    /// <item><description>The counter alone guarantees at least 2^64 draws before the state can repeat.</description></item>
    /// <item><description>Deterministic and reproducible across platforms.</description></item>
    /// </list>
    /// <para>Cons:</para>
    /// <list type="bullet">
    /// <item><description>Not cryptographically secure.</description></item>
    /// <item><description>No formal period is published; the guarantee above comes from the counter, not from a proven cycle length.</description></item>
    /// <item><description><c>NextUint</c> returns the upper half of a 64-bit word and discards the lower half.</description></item>
    /// <item><description>Reading <see cref="InternalState"/> maintains a 16-byte payload for the two state words that do not fit the two serialized state slots.</description></item>
    /// </list>
    /// <para>When to use:</para>
    /// <list type="bullet">
    /// <item><description>General-purpose gameplay and procedural generation where a published pedigree and a tiny hot path both matter.</description></item>
    /// </list>
    /// <para>When not to use:</para>
    /// <list type="bullet">
    /// <item><description>Security or adversarial contexts.</description></item>
    /// </list>
    /// <para>
    /// Threading: Prefer <c>ThreadLocalRandom&lt;Sfc64Random&gt;.Instance</c> to avoid sharing state across threads.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using WallstopStudios.UnityHelpers.Core.Random;
    ///
    /// IRandom rng = new Sfc64Random(Guid.NewGuid());
    /// double sample = rng.NextDouble(); // one state advance, not two
    ///
    /// // Save/restore for deterministic replays
    /// RandomState state = rng.InternalState;
    /// IRandom replay = new Sfc64Random(state);
    /// </code>
    /// </example>
    [RandomGeneratorMetadata(
        RandomQuality.VeryGood,
        "sfc64 (Small Fast Chaotic): three 64-bit words plus a draw counter, seeded by the canonical twelve-draw warm-up. NextUint returns the upper half of the output word, where every mixed bit arrives.",
        "O'Neill 2018 (Doty-Humphrey's SFC)",
        "https://gist.github.com/imneme/f1f7821f07cf76504a97f6537c818083",
        period: "unpublished; counter forbids a repeat before 2^64 draws; 204/256 state bits live (measured)"
    )]
    [Serializable]
    [DataContract]
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
    [WProtoSubtype(typeof(AbstractRandom), 120)]
    public sealed partial class Sfc64Random
        : AbstractRandom,
            IEquatable<Sfc64Random>,
            IComparable,
            IComparable<Sfc64Random>
    {
        private const int UlongByteCount = sizeof(ulong);
        private const int StatePayloadLength = UlongByteCount * 2;
        private const int WarmupDraws = 12;
        private const ulong InitialCounter = 1UL;

        public static Sfc64Random Instance => ThreadLocalRandom<Sfc64Random>.Instance;

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

                BinaryPrimitives.WriteUInt64LittleEndian(payload, _c);
                BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(UlongByteCount), _counter);
                return BuildState(_a, _b, payload: payload);
            }
        }

        [ProtoMember(6)]
        [WProtoMember(6)]
        internal ulong _a;

        [ProtoMember(7)]
        [WProtoMember(7)]
        internal ulong _b;

        [ProtoMember(8)]
        [WProtoMember(8)]
        internal ulong _c;

        [ProtoMember(9)]
        [WProtoMember(9)]
        internal ulong _counter;

        // Scratch for InternalState only; never part of the generator's value or its serialized form.
        private byte[] _payload;

        private void EnsureNonZeroState()
        {
            if ((_a | _b | _c) == 0 && _counter == 0)
            {
                _a = 0x9E3779B97F4A7C15UL;
                _b = 0xBF58476D1CE4E5B9UL;
                _c = 0x94D049BB133111EBUL;
                _counter = InitialCounter;
            }
        }

        public Sfc64Random()
            : this(Guid.NewGuid()) { }

        public Sfc64Random(Guid guid)
        {
            (ulong a, ulong b) = RandomUtilities.GuidToUInt64Pair(guid);
            // A Guid carries 128 bits and the state needs 192, so the remaining word comes from the
            // authors' recommended SplitMix64 expansion rather than from repeating a seed word.
            ulong seed = b;
            Seed(a, b, SplitMix64Next(ref seed));
        }

        public Sfc64Random(ulong seed0, ulong seed1, ulong seed2)
        {
            Seed(seed0, seed1, seed2);
        }

        [JsonConstructor]
        public Sfc64Random(RandomState internalState)
        {
            _a = internalState.State1;
            _b = internalState.State2;
            if (!TryReadStatePayload(internalState.PayloadBytes, out _c, out _counter))
            {
                // The same recovery path the seeding discipline uses: derive the missing word and
                // warm up, because a cold half-state is exactly the corner the warm-up exists for.
                ulong seed = internalState.State1 ^ internalState.State2;
                _c = SplitMix64Next(ref seed);
                _counter = InitialCounter;
                Warmup();
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

        public override IRandom Copy()
        {
            return new Sfc64Random(InternalState);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Seed(ulong seed0, ulong seed1, ulong seed2)
        {
            _a = seed0;
            _b = seed1;
            _c = seed2;
            _counter = InitialCounter;
            Warmup();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Warmup()
        {
            for (int i = 0; i < WarmupDraws; ++i)
            {
                NextWord();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong NextWord()
        {
            unchecked
            {
                ulong result = _a + _b + _counter++;

                _a = _b ^ (_b >> 11);
                _b = _c + (_c << 3);
                _c = Rotl(_c, 24) + result;

                return result;
            }
        }

        private static bool TryReadStatePayload(
            IReadOnlyList<byte> payload,
            out ulong c,
            out ulong counter
        )
        {
            if (payload is not { Count: >= StatePayloadLength })
            {
                c = 0;
                counter = 0;
                return false;
            }

            Span<byte> buffer = stackalloc byte[StatePayloadLength];
            for (int i = 0; i < StatePayloadLength; ++i)
            {
                buffer[i] = payload[i];
            }

            c = BinaryPrimitives.ReadUInt64LittleEndian(buffer);
            counter = BinaryPrimitives.ReadUInt64LittleEndian(buffer[UlongByteCount..]);
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
            return Equals(obj as Sfc64Random);
        }

        public bool Equals(Sfc64Random other)
        {
            if (other == null)
            {
                return false;
            }

            return _a == other._a && _b == other._b && _c == other._c && _counter == other._counter;
        }

        public override int GetHashCode()
        {
            return Objects.HashCode(_a, _b, _c, _counter);
        }

        public override string ToString()
        {
            return this.ToJson();
        }

        public int CompareTo(object obj)
        {
            return CompareTo(obj as Sfc64Random);
        }

        public int CompareTo(Sfc64Random other)
        {
            if (other == null)
            {
                return 1;
            }

            int comparison = _a.CompareTo(other._a);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = _b.CompareTo(other._b);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = _c.CompareTo(other._c);
            if (comparison != 0)
            {
                return comparison;
            }

            return _counter.CompareTo(other._counter);
        }
    }
}
