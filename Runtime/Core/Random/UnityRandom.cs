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
        "https://docs.unity3d.com/ScriptReference/Random.html"
    )]
    [Serializable]
    [DataContract]
    [ProtoContract]
    [WProtoContract]
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

        // The engine's position, as text. Written just before serialization rather than kept up to
        // date, because that global moves without this object being told; read back on the way in and
        // applied. It is a field rather than a computed property so that the serializers reach it the
        // same way they reach every other generator's state.
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
            // Clone from full InternalState to preserve reservoirs and cached values. The engine
            // position it carries is the one being read at this instant, so writing it back is the
            // identity -- a copy does not move the stream it is copying.
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
            // The protobuf path fills the field directly rather than through a constructor, so this
            // is where a proto payload's position reaches the engine.
            ApplyEngineState();
        }

        private static string CaptureEngineState()
        {
            // JsonUtility rather than the four fields it happens to have today: Random.State's
            // layout is private, and a text round trip through public API cannot be wrong about a
            // field count Unity is free to change.
            return UnityEngine.JsonUtility.ToJson(UnityEngine.Random.state);
        }

        private static byte[] EncodeEngineState(string engineState)
        {
            return string.IsNullOrEmpty(engineState) ? null : Encoding.UTF8.GetBytes(engineState);
        }

        private static string DecodeEngineState(RandomState internalState)
        {
            byte[] payload = internalState._payload;
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
                // Malformed text. The engine stays where it is and the rest of the generator still
                // works, which is what a save file written by a different version needs.
                return;
            }

            // JsonUtility throws only on text that is not JSON at all. Well-formed JSON that is not
            // an engine state -- a payload from another field, another version, or an attacker --
            // parses to a ZEROED state, and assigning that is the worst outcome available: an
            // all-zero xorshift state emits zeros forever. Re-serializing what was parsed and
            // comparing it to the payload refuses exactly those, and does it without this package
            // knowing how many fields Random.State has.
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

        // What a state of nothing serializes to, computed once from the type itself rather than
        // written out, because a literal would be a claim about a field count.
        private static readonly string ZeroedEngineState = UnityEngine.JsonUtility.ToJson(
            default(UnityEngine.Random.State)
        );
    }
}
