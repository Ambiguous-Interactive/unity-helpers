// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// WaveSplat: a one-word chaotic generator that increments by a fixed large constant and emits rotated high bits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reference: Will Stafford Parsons (wileylooper/wavesplat, repository offline). The generator maintains a single 64-bit state, adds a
    /// Weyl-style increment of <c>11,111,111,111,111,111</c> each step, and returns bits shifted by a dynamic amount
    /// derived from the low nibble of the state.
    /// </para>
    /// <para>Pros:</para>
    /// <list type="bullet">
    /// <item><description>Tiny state footprint (one 64-bit word) with excellent cache locality.</description></item>
    /// <item><description>Deterministic and serializable via <see cref="RandomState"/>.</description></item>
    /// </list>
    /// <para>Cons:</para>
    /// <list type="bullet">
    /// <item><description>Not a statistically rigorous generator; treat as a fun chaotic mixer.</description></item>
    /// <item><description>Not cryptographically secure.</description></item>
    /// </list>
    /// <para>When to use:</para>
    /// <list type="bullet">
    /// <item><description>Quick procedural effects where minimal overhead matters.</description></item>
    /// </list>
    /// <para>When not to use:</para>
    /// <list type="bullet">
    /// <item><description>Simulation-grade workloads or anything security sensitive.</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// using WallstopStudios.UnityHelpers.Core.Random;
    ///
    /// WaveSplatRandom rng = new WaveSplatRandom(seed: 0xCAFEBABEUL);
    /// uint value = rng.NextUint();
    /// float normalized = rng.NextFloat();
    /// </code>
    /// </example>
    [RandomGeneratorMetadata(
        RandomQuality.Experimental,
        "Single-word chaotic generator; author notes period 2^64 but provides no formal test results—treat as experimental.",
        "Will Stafford Parsons",
        "" // Original repository wileylooper/wavesplat is offline
    )]
    [Serializable]
    [DataContract]
    [ProtoContract]
    [WProtoContract]
    public sealed partial class WaveSplatRandom : AbstractRandom
    {
        private const ulong Increment = 11_111_111_111_111_111UL;
        private const ulong DefaultGuidSeed = 0x9E3779B97F4A7C15UL;
        private const int ShiftMask = 15;
        private const int ShiftBase = 16;

        public static WaveSplatRandom Instance => ThreadLocalRandom<WaveSplatRandom>.Instance;

        public override RandomState InternalState => BuildState(_state);

        [ProtoMember(6)]
        [WProtoMember(6)]
        private ulong _state;

        public WaveSplatRandom()
            : this(Guid.NewGuid()) { }

        public WaveSplatRandom(Guid guid)
        {
            InitializeFromGuid(guid);
        }

        public WaveSplatRandom(ulong seed)
        {
            _state = seed;
        }

        public WaveSplatRandom(uint seed)
            : this((ulong)seed) { }

        [JsonConstructor]
        public WaveSplatRandom(RandomState internalState)
        {
            _state = internalState.State1;
            RestoreCommonState(internalState);
        }

        public override uint NextUint()
        {
            unchecked
            {
                _state += Increment;
                int shift = (int)((_state & ShiftMask) + ShiftBase);
                ulong value = _state >> shift;
                return (uint)value;
            }
        }

        public override IRandom Copy()
        {
            return new WaveSplatRandom(InternalState);
        }

        private void InitializeFromGuid(Guid guid)
        {
            (ulong seedA, ulong seedB) = RandomUtilities.GuidToUInt64Pair(guid);
            ulong combined = seedA ^ seedB;
            if (combined == 0UL)
            {
                combined = seedA != 0UL ? seedA : DefaultGuidSeed;
            }

            _state = combined;
        }
    }
}
