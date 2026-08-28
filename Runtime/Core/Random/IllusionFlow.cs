// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is IllusionFlow, by Will Stafford Parsons,
// https://github.com/wstaffordp/illusionflow. This is an adaptation of that work; the design is the
// original author's. The upstream license is not recorded in this repository.
// See docs/project/third-party-notices.md.

/*
    IllusionFlow is a significant enhancement upon the classic XoroShiroRandom discovered by Will Stafford Parsons.
        
    Reference: https://github.com/wstaffordp/illusionflow
    
    Copyright original author: https://github.com/wstaffordp
 */

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// IllusionFlow: a five-word 32-bit generator -- a Weyl counter gating a rotate/xor/add mix, with a carry-in reseed step every 2^32 draws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generator advances a 32-bit Weyl counter and mixes five state words with rotations, exclusive-ors and additions.
    /// <see cref="PRNG.Instance"/> previously defaulted here; it now returns
    /// <see cref="Xoshiro256StarStar"/>, whose published period and reference supersede the
    /// unpublished pedigree at a measured-equal speed. <see cref="IllusionFlow"/> itself is unchanged
    /// and remains the performance baseline in the benchmark docs.
    /// </para>
    /// <para>Pros:</para>
    /// <list type="bullet">
    /// <item><description>Excellent performance; strong general-purpose statistical behavior.</description></item>
    /// <item><description>Deterministic and portable via <see cref="RandomState"/>.</description></item>
    /// </list>
    /// <para>Cons:</para>
    /// <list type="bullet">
    /// <item><description>Not cryptographically secure.</description></item>
    /// <item><description>Newer algorithm—choose established ones if you require historical precedence.</description></item>
    /// </list>
    /// <para>When to use:</para>
    /// <list type="bullet">
    /// <item><description>Default choice for most gameplay and procedural content needs.</description></item>
    /// </list>
    /// <para>When not to use:</para>
    /// <list type="bullet">
    /// <item><description>Cryptographic or adversarial contexts.</description></item>
    /// </list>
    /// <para>
    /// Threading: Prefer <see cref="ThreadLocalRandom{T}.Instance"/> or <see cref="PRNG.Instance"/> to avoid sharing state.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using WallstopStudios.UnityHelpers.Core.Random;
    ///
    /// IRandom rng = PRNG.Instance; // Xoshiro256StarStar by default; construct IllusionFlow directly
    /// int index = rng.Next(0, items.Count);
    /// float gaussian = rng.NextGaussian(mean: 0f, stdDev: 1f);
    ///
    /// // Deterministic snapshot
    /// var state = rng.InternalState;
    /// var replay = new IllusionFlow(state);
    /// </code>
    /// </example>
    [RandomGeneratorMetadata(
        RandomQuality.Excellent,
        "Five-word rotate/xor/add generator driven by a 32-bit Weyl counter. Verified here: PractRand 0.95 clean through 8GB, the depth at which SystemRandom fails. The author reports 64GB; that run cannot be checked -- the upstream repository is offline.",
        "Will Stafford Parsons",
        "", // Original repository wileylooper/illusionflow is offline
        period: "unpublished; 108/160 state bits live (measured)"
    )]
    [Serializable]
    [DataContract]
    // SkipConstructor for the reason its siblings carry it: protobuf omits a member equal to its
    // default, so an all-default state writes a payload naming none of it, and a parameterless
    // constructor seeding from Guid.NewGuid() would invent a stream the save never held.
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
    public sealed partial class IllusionFlow : AbstractRandom
    {
        private const int UintByteCount = sizeof(uint) * 8;
        private const int StatePayloadLength = sizeof(uint);

        public static IllusionFlow Instance => ThreadLocalRandom<IllusionFlow>.Instance;

        public override RandomState InternalState
        {
            get
            {
                ulong stateA = ((ulong)_a << UintByteCount) | _b;
                ulong stateB = ((ulong)_c << UintByteCount) | _d;
                byte[] payload = _payload;
                if (payload == null)
                {
                    payload = new byte[StatePayloadLength];
                    _payload = payload;
                }

                BinaryPrimitives.WriteUInt32LittleEndian(payload, _e);
                return BuildState(stateA, stateB, payload: payload);
            }
        }

        [ProtoMember(6)]
        [WProtoMember(6)]
        private uint _a;

        [ProtoMember(7)]
        [WProtoMember(7)]
        private uint _b;

        [ProtoMember(8)]
        [WProtoMember(8)]
        private uint _c;

        [ProtoMember(9)]
        [WProtoMember(9)]
        private uint _d;

        [ProtoMember(10)]
        [WProtoMember(10)]
        private uint _e;

        // Cached space for RandomState, allocated on first use rather than by an initializer:
        // a formatter is free to allocate an instance no constructor ever ran, and this buffer
        // is not on the wire to be restored.
        private byte[] _payload;

        public IllusionFlow()
            : this(Guid.NewGuid()) { }

        public IllusionFlow(Guid guid, uint? extraSeed = null)
        {
            (uint a, uint b, uint c, uint d) = RandomUtilities.GuidToUInt32Quad(guid);
            _a = a;
            _b = b;
            _b = b;
            _c = c;
            _d = d;
            _e = extraSeed ?? unchecked((uint)guid.GetHashCode());
        }

        [JsonConstructor]
        public IllusionFlow(RandomState internalState)
        {
            unchecked
            {
                _a = (uint)(internalState.State1 >> UintByteCount);
                _b = (uint)internalState.State1;
                _c = (uint)(internalState.State2 >> UintByteCount);
                _d = (uint)internalState.State2;
                uint legacyE = 0;
                bool hasPayload = TryReadStatePayload(internalState, out uint payloadE);
                bool legacyPackedE =
                    !hasPayload && TryExtractLegacyPackedValue(internalState, out legacyE);
                if (hasPayload)
                {
                    _e = payloadE;
                }
                else if (legacyPackedE)
                {
                    _e = legacyE;
                }
                else
                {
                    _e = DeriveFallbackE(internalState.State1, internalState.State2);
                }

                RandomState stateToRestore = legacyPackedE
                    ? StripLegacyGaussian(internalState)
                    : internalState;
                RestoreCommonState(stateToRestore);
            }
        }

        public override uint NextUint()
        {
            unchecked
            {
                uint result = _b + _e;
                ++_a;
                if (_a == 0U)
                {
                    _c += _e;
                    _d ^= _b;
                    _b += _c;
                    _e ^= _d;
                    return result;
                }

                _b = ((_b << 17) | (_b >> 15)) ^ _d;
                _d += 1111111111U;
                _e = (result << 13) | (result >> 19);
                return result;
            }
        }

        public override IRandom Copy()
        {
            return new IllusionFlow(InternalState);
        }

        private static bool TryReadStatePayload(RandomState state, out uint value)
        {
            IReadOnlyList<byte> payload = state.PayloadBytes;
            if (payload is not { Count: >= StatePayloadLength })
            {
                value = 0;
                return false;
            }

            Span<byte> buffer = stackalloc byte[StatePayloadLength];
            for (int i = 0; i < StatePayloadLength; ++i)
            {
                buffer[i] = payload[i];
            }

            value = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
            return true;
        }

        private static bool TryExtractLegacyPackedValue(RandomState state, out uint value)
        {
            double? gaussian = state.Gaussian;
            if (gaussian == null)
            {
                value = 0;
                return false;
            }

            long bits = BitConverter.DoubleToInt64Bits(gaussian.Value);
            if ((bits & unchecked((long)0xFFFFFFFF00000000L)) != 0)
            {
                value = 0;
                return false;
            }

            value = (uint)(bits & 0xFFFFFFFFL);
            return true;
        }

        private static RandomState StripLegacyGaussian(RandomState state)
        {
            return new RandomState(
                state.State1,
                state.State2,
                gaussian: null,
                payload: state.PayloadBytes,
                bitBuffer: state.BitBuffer,
                bitCount: state.BitCount,
                byteBuffer: state.ByteBuffer,
                byteCount: state.ByteCount
            );
        }

        private static uint DeriveFallbackE(ulong state1, ulong state2)
        {
            uint candidate = unchecked((uint)(state1 ^ state2 ^ 0xA5A5A5A5UL));
            return candidate != 0 ? candidate : 0x1F123BB5U;
        }
    }
}
