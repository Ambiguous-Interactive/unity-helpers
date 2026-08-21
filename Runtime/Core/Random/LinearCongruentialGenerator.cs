// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is the "quick and dirty" linear congruential generator
// (a = 1664525, c = 1013904223, m = 2^32) popularized by Press et al., "Numerical Recipes in C"
// (ranqd1), after Knuth and H. W. Lewis. This is an independent implementation of a published
// algorithm. See docs/project/third-party-notices.md.

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// A simple Linear Congruential Generator (LCG): extremely fast with low-quality randomness.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LCGs are among the oldest PRNGs. This configuration is fast and compact but exhibits correlations and
    /// shorter periods compared to modern generators. Best suited for cosmetic randomness where quality is not
    /// critical.
    /// </para>
    /// <para>Pros:</para>
    /// <list type="bullet">
    /// <item><description>Blazing fast; trivial implementation.</description></item>
    /// <item><description>Tiny state and deterministic behavior.</description></item>
    /// </list>
    /// <para>Cons:</para>
    /// <list type="bullet">
    /// <item><description>Poor statistical quality vs. PCG/Xoroshiro; noticeable patterns in some uses.</description></item>
    /// <item><description>Not cryptographically secure.</description></item>
    /// </list>
    /// <para>When to use:</para>
    /// <list type="bullet">
    /// <item><description>Cheap visual effects, quick throwaway randomness, prototyping.</description></item>
    /// </list>
    /// <para>When not to use:</para>
    /// <list type="bullet">
    /// <item><description>Gameplay-critical logic, simulations, or fairness-sensitive systems.</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// var rng = new LinearCongruentialGenerator(seed: 12345);
    /// int i = rng.Next(0, 100);
    /// // Prefer PCG or IllusionFlow for production gameplay.
    /// </code>
    /// </example>
    [RandomGeneratorMetadata(
        RandomQuality.Poor,
        "Numerical Recipes 'quick and dirty' LCG (a=1664525, c=1013904223, m=2^32) returning the raw state, so bit k has period only 2^(k+1) -- measured linear complexity of bit k is 2^k+1, and bit 0 simply alternates. Cosmetic use only.",
        "Press et al., Numerical Recipes (ranqd1)",
        ""
    )]
    [Serializable]
    [DataContract]
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
    public sealed partial class LinearCongruentialGenerator : AbstractRandom
    {
        public static LinearCongruentialGenerator Instance =>
            ThreadLocalRandom<LinearCongruentialGenerator>.Instance;

        public override RandomState InternalState => BuildState(_state);

        [ProtoMember(6)]
        [WProtoMember(6)]
        private uint _state;

        public LinearCongruentialGenerator()
            : this(Guid.NewGuid()) { }

        public LinearCongruentialGenerator(int seed)
        {
            _state = unchecked((uint)seed);
        }

        public LinearCongruentialGenerator(Guid seed)
        {
            _state = unchecked((uint)RandomUtilities.GuidToInt32(seed));
        }

        [JsonConstructor]
        public LinearCongruentialGenerator(RandomState internalState)
        {
            _state = unchecked((uint)internalState.State1);
            RestoreCommonState(internalState);
        }

        public override uint NextUint()
        {
            unchecked
            {
                _state = _state * 1664525U + 1013904223U;
            }
            return _state;
        }

        public override IRandom Copy()
        {
            return new LinearCongruentialGenerator(InternalState);
        }
    }
}
