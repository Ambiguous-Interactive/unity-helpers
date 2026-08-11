// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// A classic, extremely fast XorShift PRNG with small state and modest quality.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XorShift generators are known for their simplicity and speed. This variant operates on a 32-bit state and
    /// produces 32-bit outputs. It is suitable for lightweight, cosmetic randomness where maximum statistical
    /// rigor is not required.
    /// </para>
    /// <para>Pros:</para>
    /// <list type="bullet">
    /// <item><description>Very fast; tiny state footprint.</description></item>
    /// <item><description>Deterministic and easy to serialize/restore.</description></item>
    /// </list>
    /// <para>Cons:</para>
    /// <list type="bullet">
    /// <item><description>Lower statistical quality than newer generators; can fail some modern test batteries.</description></item>
    /// <item><description>Not cryptographically secure.</description></item>
    /// </list>
    /// <para>When to use:</para>
    /// <list type="bullet">
    /// <item><description>Effects, particles, jitter, or any light randomness in hot loops.</description></item>
    /// <item><description>Short-lived simulations where ultimate quality is not required.</description></item>
    /// </list>
    /// <para>When not to use:</para>
    /// <list type="bullet">
    /// <item><description>Simulations or systems sensitive to subtle bias.</description></item>
    /// <item><description>Security-sensitive contexts.</description></item>
    /// </list>
    /// <para>
    /// Threading: Use <c>ThreadLocalRandom&lt;XorShiftRandom&gt;.Instance</c> or <see cref="PRNG.Instance"/> to avoid shared-state across threads.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using WallstopStudios.UnityHelpers.Core.Random;
    ///
    /// var rng = new XorShiftRandom(state: 42);
    /// int damage = rng.Next(10, 20);
    /// Vector3 pos = rng.NextVector3(-5f, 5f); // via RandomExtensions
    ///
    /// // Thread-local access for parallel systems
    /// var fast = XorShiftRandom.Instance; // per-thread instance
    /// </code>
    /// </example>
    [RandomGeneratorMetadata(
        RandomQuality.Fair,
        "Classic 32-bit xorshift; known to fail portions of TestU01 and PractRand, acceptable for lightweight effects only.",
        "Marsaglia 2003",
        "https://doi.org/10.18637/jss.v008.i14"
    )]
    [Serializable]
    [DataContract]
    [ProtoContract]
    [WProtoContract]
    public sealed partial class XorShiftRandom : AbstractRandom
    {
        public static XorShiftRandom Instance => ThreadLocalRandom<XorShiftRandom>.Instance;

        public override RandomState InternalState => BuildState(_state);

        private const uint DefaultState = 2463534242U;

        [ProtoMember(6)]
        [WProtoMember(6)]
        private uint _state;

        private static uint NormalizeState(uint state)
        {
            return state != 0 ? state : DefaultState;
        }

        public XorShiftRandom()
            : this(Guid.NewGuid()) { }

        public XorShiftRandom(int state)
        {
            _state = NormalizeState(unchecked((uint)state));
        }

        public XorShiftRandom(Guid seed)
        {
            _state = NormalizeState(unchecked((uint)seed.GetHashCode()));
        }

        [JsonConstructor]
        public XorShiftRandom(RandomState internalState)
        {
            _state = NormalizeState(unchecked((uint)internalState.State1));
            RestoreCommonState(internalState);
        }

        public override uint NextUint()
        {
            _state = NormalizeState(_state);
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }

        public override IRandom Copy()
        {
            return new XorShiftRandom(InternalState);
        }
    }
}
