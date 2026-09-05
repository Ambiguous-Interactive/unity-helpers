// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Runtime.Serialization;
    using System.Text;
    using System.Text.Json.Serialization;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

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
    /// <item><description><b>Restoring a snapshot moves the engine.</b> The position this adapter reports
    /// is <c>UnityEngine.Random</c>'s, so restoring it writes <c>UnityEngine.Random.state</c> back --
    /// exactly as <c>new UnityRandom(seed)</c> already calls <c>InitState</c>. Anything else drawing from
    /// <c>UnityEngine.Random</c> is moved with it.</description></item>
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
    /// <remarks>
    /// <para>
    /// <b>Snapshots resume.</b> <see cref="InternalState"/> carries the engine's live position, and a
    /// generator rebuilt from that snapshot writes it back, so a save file reproduces the sequence the
    /// way every other generator here does. The position travels as text produced by
    /// <c>JsonUtility</c> from <c>UnityEngine.Random.State</c>, so this package never has to know how
    /// many fields that struct has.
    /// </para>
    /// </remarks>
    [RandomGeneratorMetadata(
        RandomQuality.Fair,
        "Mirrors UnityEngine.Random, documented by Unity as Xorshift 128; suitable for legacy compatibility but not high-stakes simulation.",
        "UnityEngine.Random",
        "https://docs.unity3d.com/ScriptReference/Random.html",
        period: "2^128-1 (Unity documents Xorshift 128)"
    )]
    [Serializable]
    [DataContract]
    [ProtoContract]
    [WProtoContract]
    [WProtoSubtype(typeof(AbstractRandom), 105)]
    public sealed partial class UnityRandom : AbstractRandom
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
                        payload: EncodeEngineState(CaptureEngineState()),
                        bitBuffer: _bitBuffer,
                        bitCount: _bitCount,
                        byteBuffer: _byteBuffer,
                        byteCount: _byteCount
                    );
                }
            }
        }

        [ProtoMember(6)]
        [WProtoMember(6)]
        private readonly int? _seed;

        // Capture engine state at serialization time because other callers can advance the shared generator.
        [ProtoMember(7)]
        [WProtoMember(7)]
        internal string _engineState;

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
                _engineState = DecodeEngineState(internalState);
                RestoreCommonState(internalState);
                ApplyEngineState();
            }
        }

        public override uint NextUint()
        {
            return unchecked((uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue));
        }

        public override IRandom Copy()
        {
            // Clone the complete state without advancing the shared engine stream.
            return new UnityRandom(InternalState);
        }

        /// <inheritdoc />
        protected override void OnBeforeSerialization()
        {
            _engineState = CaptureEngineState();
        }

        /// <inheritdoc />
        protected override void OnAfterDeserialization()
        {
            // Protobuf bypasses constructors, so apply its restored engine position here.
            ApplyEngineState();
        }

        private static string CaptureEngineState()
        {
            // JsonUtility preserves the public Random.State contract without depending on its private field layout.
            return UnityEngine.JsonUtility.ToJson(UnityEngine.Random.state);
        }

        private static byte[] EncodeEngineState(string engineState)
        {
            return string.IsNullOrEmpty(engineState) ? null : Encoding.UTF8.GetBytes(engineState);
        }

        private static string DecodeEngineState(RandomState internalState)
        {
            byte[] payload = internalState._payload;
            // The JSON state validator rejects decoded text that this encoder could not have produced.
            return payload == null || payload.Length == 0 ? null : Encoding.UTF8.GetString(payload);
        }

        private void ApplyEngineState()
        {
            if (string.IsNullOrEmpty(_engineState))
            {
                return;
            }

            UnityEngine.Random.State parsed;
            try
            {
                parsed = UnityEngine.JsonUtility.FromJson<UnityEngine.Random.State>(_engineState);
            }
            catch (ArgumentException)
            {
                // Invalid state text leaves the engine position unchanged.
                return;
            }

            // Unrelated valid JSON can parse to a zero state; round-trip validation prevents installing a stuck generator.
            string round = UnityEngine.JsonUtility.ToJson(parsed);
            if (
                !string.Equals(round, _engineState, StringComparison.Ordinal)
                || string.Equals(round, ZeroedEngineState, StringComparison.Ordinal)
            )
            {
                return;
            }

            UnityEngine.Random.state = parsed;
        }

        // Derive the zero-state representation without assuming Unity field count.
        private static readonly string ZeroedEngineState = UnityEngine.JsonUtility.ToJson(
            default(UnityEngine.Random.State)
        );
    }
}
