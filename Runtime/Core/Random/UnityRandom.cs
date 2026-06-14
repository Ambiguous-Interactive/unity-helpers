// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using ProtoBuf;

    /// <summary>
    /// An adapter over <c>UnityEngine.Random</c> exposing the <see cref="IRandom"/> interface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses Unity's global random state. If constructed with a seed, it initializes the global state via
    /// <c>UnityEngine.Random.InitState</c>. Without a seed, it reads from whatever global state Unity maintains.
    /// </para>
    /// <para>Pros:</para>
    /// <list type="bullet">
    /// <item><description>Parity with Unity's <c>Random</c> for projects relying on its behavior.</description></item>
    /// <item><description>Easy substitution of Unity's RNG with the unified <see cref="IRandom"/> interface.</description></item>
    /// </list>
    /// <para>Cons:</para>
    /// <list type="bullet">
    /// <item><description>Global shared state; can be modified by other code calling <c>UnityEngine.Random</c>.</description></item>
    /// <item><description>Not thread-safe and generally slower than high-performance PRNGs.</description></item>
    /// <item><description>Determinism depends on controlling Unity's global state elsewhere in your project.</description></item>
    /// </list>
    /// <para>When to use:</para>
    /// <list type="bullet">
    /// <item><description>When you must preserve Unity.Random behavior or interact with code that depends on it.</description></item>
    /// </list>
    /// <para>When not to use:</para>
    /// <list type="bullet">
    /// <item><description>General-purpose gameplay randomness—prefer <see cref="PRNG.Instance"/> or a concrete PRNG like PCG.</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// using WallstopStudios.UnityHelpers.Core.Random;
    ///
    /// // Explicitly seed Unity's global RNG
    /// var unityRng = new UnityRandom(seed: 2024);
    /// int roll = unityRng.Next(1, 7);
    ///
    /// // Note: calling UnityEngine.Random elsewhere will affect this sequence.
    /// </code>
    /// </example>
    [RandomGeneratorMetadata(
        RandomQuality.Fair,
        "Mirrors UnityEngine.Random (Xorshift196 + additive); suitable for legacy compatibility but not high-stakes simulation.",
        "UnityEngine.Random",
        "https://docs.unity3d.com/ScriptReference/Random.html"
    )]
    [Serializable]
    [DataContract]
    [ProtoContract]
    public sealed class UnityRandom : AbstractRandom
    {
        public static readonly UnityRandom Instance = new();

        public override RandomState InternalState
        {
            get
            {
                unchecked
                {
                    return new RandomState(
                        (ulong)(_seed ?? 0),
                        gaussian: _seed != null ? 0.0 : null,
                        payload: null,
                        bitBuffer: _bitBuffer,
                        bitCount: _bitCount,
                        byteBuffer: _byteBuffer,
                        byteCount: _byteCount
                    );
                }
            }
        }

        [ProtoMember(6)]
        private readonly int? _seed;

        public UnityRandom()
            : this(null) { }

        public UnityRandom(int? seed)
        {
            if (seed != null)
            {
                _seed = seed.Value;
                UnityEngine.Random.InitState(seed.Value);
            }
        }

        [JsonConstructor]
        public UnityRandom(RandomState internalState)
        {
            unchecked
            {
                _seed = internalState.Gaussian != null ? (int)internalState.State1 : null;
                RestoreCommonState(internalState);
            }
        }

        public override uint NextUint()
        {
            return unchecked((uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue));
        }

        public override IRandom Copy()
        {
            // Clone from full InternalState to preserve reservoirs and cached values
            return new UnityRandom(InternalState);
        }
    }
}
