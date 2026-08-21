// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is BlastCircuit, by Will Stafford Parsons (wileylooper/blastcircuit, the
// upstream repository is offline). This is an adaptation of that work; the design is the original
// author's. The upstream license is not recorded in this repository.
// See docs/project/third-party-notices.md.

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// BlastCircuit: a four-word ARX-style generator with rotational feedback.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reference: Will Stafford Parsons (wileylooper/blastcircuit, repository offline). The generator keeps four 64-bit state words, mixes them
    /// using xor, rotation, and Weyl-style increments, and produces a 64-bit <c>mix</c> value each round (we emit the
    /// lower 32 bits in <see cref="NextUint"/> to match the framework API).
    /// </para>
    /// <para>Pros:</para>
    /// <list type="bullet">
    /// <item><description>Moderate state (256 bits) with lively dynamics and excellent bulk generation speed.</description></item>
    /// <item><description>Supports deterministic capture with <see cref="RandomState"/>.</description></item>
    /// </list>
    /// <para>Cons:</para>
    /// <list type="bullet">
    /// <item><description>Not designed for cryptography.</description></item>
    /// <item><description>First few outputs after seeding may be correlated—discard them when mirroring the C reference.</description></item>
    /// </list>
    /// <para>When to use:</para>
    /// <list type="bullet">
    /// <item><description>Procedural content where you want energetic bit diffusion from a compact state.</description></item>
    /// </list>
    /// <para>When not to use:</para>
    /// <list type="bullet">
    /// <item><description>Security-sensitive systems or scenarios requiring strict statistical certification.</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// using WallstopStudios.UnityHelpers.Core.Random;
    ///
    /// BlastCircuitRandom rng = new BlastCircuitRandom(Guid.NewGuid());
    /// ulong wide = rng.NextUlong(); // 64-bit output via AbstractRandom
    /// int range = rng.Next(0, 100);
    /// </code>
    /// </example>
    [RandomGeneratorMetadata(
        RandomQuality.Good,
        "Chaotic ARX mixer rather than a proven statistically optimal generator. Verified here: PractRand 0.95 clean through 8GB, the depth at which SystemRandom fails.",
        "Will Stafford Parsons",
        "" // Original repository wileylooper/blastcircuit is offline
    )]
    [Serializable]
    [DataContract]
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
    public sealed partial class BlastCircuitRandom : AbstractRandom
    {
        private const ulong Increment = 111_111_111_111_111UL;
        private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;
        private const int PayloadByteCount = sizeof(ulong) * 2;

        public static BlastCircuitRandom Instance => ThreadLocalRandom<BlastCircuitRandom>.Instance;

        public override RandomState InternalState
        {
            get
            {
                byte[] payload = new byte[PayloadByteCount];
                BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(0, sizeof(ulong)), _c);
                BinaryPrimitives.WriteUInt64LittleEndian(
                    payload.AsSpan(sizeof(ulong), sizeof(ulong)),
                    _d
                );
                return BuildState(_a, _b, payload);
            }
        }

        [ProtoMember(6)]
        [WProtoMember(6)]
        private ulong _a;

        [ProtoMember(7)]
        [WProtoMember(7)]
        private ulong _b;

        [ProtoMember(8)]
        [WProtoMember(8)]
        private ulong _c;

        [ProtoMember(9)]
        [WProtoMember(9)]
        private ulong _d;

        public BlastCircuitRandom()
            : this(Guid.NewGuid()) { }

        public BlastCircuitRandom(Guid guid)
        {
            InitializeFromGuid(guid);
        }

        public BlastCircuitRandom(ulong seed)
        {
            InitializeFromScalar(seed);
        }

        public BlastCircuitRandom(ulong seedA, ulong seedB, ulong seedC, ulong seedD)
        {
            SetState(seedA, seedB, seedC, seedD);
        }

        [JsonConstructor]
        public BlastCircuitRandom(RandomState internalState)
        {
            _a = internalState.State1;
            _b = internalState.State2;
            (ulong payloadC, ulong payloadD) = ReadPayload(internalState.PayloadBytes);
            _c = payloadC;
            _d = payloadD;
            RestoreCommonState(internalState);
        }

        public override uint NextUint()
        {
            unchecked
            {
                ulong mix = _a ^ _b;

                _a += Increment;
                _b = (_b >> 3) + _c;
                _c = _d;
                _d = RotateLeft(_d, 21) + mix;

                return (uint)mix;
            }
        }

        public override IRandom Copy()
        {
            return new BlastCircuitRandom(InternalState);
        }

        private void InitializeFromGuid(Guid guid)
        {
            (ulong seed0, ulong seed1) = RandomUtilities.GuidToUInt64Pair(guid);
            ulong mixed0 = Mix64(seed0);
            ulong mixed1 = Mix64(seed0 + GoldenGamma);
            ulong mixed2 = Mix64(seed1);
            ulong mixed3 = Mix64(seed1 + GoldenGamma);
            SetState(mixed0, mixed1, mixed2, mixed3);
        }

        private void InitializeFromScalar(ulong seed)
        {
            unchecked
            {
                ulong baseSeed = Mix64(seed);
                ulong bSeed = Mix64(seed + GoldenGamma);
                ulong cSeed = Mix64(seed + (GoldenGamma * 2));
                ulong dSeed = Mix64(seed + (GoldenGamma * 3));
                SetState(baseSeed, bSeed, cSeed, dSeed);
            }
        }

        private void SetState(ulong a, ulong b, ulong c, ulong d)
        {
            _a = a;
            _b = b;
            _c = c;
            _d = d;
        }

        private static (ulong, ulong) ReadPayload(IReadOnlyList<byte> payload)
        {
            if (payload == null || payload.Count < PayloadByteCount)
            {
                return (0UL, 0UL);
            }

            if (payload is byte[] payloadArray)
            {
                ulong cValue = BinaryPrimitives.ReadUInt64LittleEndian(
                    payloadArray.AsSpan(0, sizeof(ulong))
                );
                ulong dValue = BinaryPrimitives.ReadUInt64LittleEndian(
                    payloadArray.AsSpan(sizeof(ulong), sizeof(ulong))
                );
                return (cValue, dValue);
            }

            Span<byte> buffer = stackalloc byte[PayloadByteCount];
            for (int i = 0; i < PayloadByteCount; ++i)
            {
                buffer[i] = payload[i];
            }

            ulong c = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(0, sizeof(ulong)));
            ulong d = BinaryPrimitives.ReadUInt64LittleEndian(
                buffer.Slice(sizeof(ulong), sizeof(ulong))
            );
            return (c, d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mix64(ulong value)
        {
            unchecked
            {
                value += GoldenGamma;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotateLeft(ulong value, int count)
        {
            return (value << count) | (value >> (64 - count));
        }
    }
}
