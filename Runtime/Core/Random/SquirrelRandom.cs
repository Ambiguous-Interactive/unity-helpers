// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is Squirrel Noise, by Squirrel Eiserloh, presented in his GDC talk
// "Math for Game Programmers: Noise-Based RNG", https://youtu.be/LWFzPP8ZbdU?t=2673. This
// implementation follows the approach presented there; the design is the original author's. The
// upstream license is not recorded in this repository. See docs/project/third-party-notices.md.

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// A hash-based PRNG inspired by Squirrel Eiserloh's "Squirrel Noise" approach for deterministic noise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reference implementation: https://youtu.be/LWFzPP8ZbdU?t=2673
    /// </para>
    /// <para>
    /// Squirrel-style generators are simple, stateless transformations that can produce deterministic pseudo-noise
    /// values from integer coordinates or an advancing internal position. This implementation exposes both a sequential
    /// <see cref="NextUint()"/> and coordinate-based noise via <see cref="NextNoise(int, int)"/> without advancing state.
    /// </para>
    /// <para>Pros:</para>
    /// <list type="bullet">
    /// <item><description>Great for deterministic spatial noise (e.g., tiles, grid sampling) with no stored sequence.</description></item>
    /// <item><description>Fast and simple; ideal for repeatable content by position.</description></item>
    /// </list>
    /// <para>Cons:</para>
    /// <list type="bullet">
    /// <item><description>Statistical properties are good for noise but not designed for all PRNG use-cases.</description></item>
    /// <item><description>Not cryptographically secure.</description></item>
    /// </list>
    /// <para>When to use:</para>
    /// <list type="bullet">
    /// <item><description>Procedural textures, terrain, per-cell randomness where the same input produces the same output.</description></item>
    /// </list>
    /// <para>When not to use:</para>
    /// <list type="bullet">
    /// <item><description>When you need a classic stream-based PRNG for sequences or shuffles; prefer PCG/Xoroshiro/RomuDuo.</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// using WallstopStudios.UnityHelpers.Core.Random;
    ///
    /// var rng = new SquirrelRandom(seed: 12345);
    /// // Deterministic by coordinate (does not advance RNG):
    /// float noise = rng.NextNoise(x: 10, y: 42);
    ///
    /// // Sequential usage:
    /// int val = rng.Next(0, 100);
    /// </code>
    /// </example>
    [RandomGeneratorMetadata(
        RandomQuality.Good,
        "Hash-based generator built on Squirrel3; good equidistribution for table lookups but not extensively tested beyond moderate ranges.",
        "Squirrel Eiserloh",
        "https://youtu.be/LWFzPP8ZbdU?t=2673" // GDC talk on Squirrel noise
    )]
    [Serializable]
    [DataContract]
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
    public sealed partial class SquirrelRandom : AbstractRandom
    {
        private const uint BitNoise1 = 0xB5297A4D;
        private const uint BitNoise2 = 0x68E31DA4;
        private const uint BitNoise3 = 0x1B56C4E9;
        private const int LargePrime = 198491317;

        public static readonly SquirrelRandom Instance = ThreadLocalRandom<SquirrelRandom>.Instance;

        public override RandomState InternalState => BuildState(_position);

        [ProtoMember(6)]
        [WProtoMember(6)]
        private uint _position;

        public SquirrelRandom()
            : this(Guid.NewGuid().GetHashCode()) { }

        public SquirrelRandom(int seed)
        {
            _position = unchecked((uint)seed);
        }

        [JsonConstructor]
        public SquirrelRandom(RandomState internalState)
        {
            _position = unchecked((uint)internalState.State1);
            RestoreCommonState(internalState);
        }

        public override uint NextUint()
        {
            return NextUintInternal(ref _position);
        }

        // Does not advance the RNG
        public float NextNoise(int x, int y)
        {
            return NextNoise(x, y, _position);
        }

        public override IRandom Copy()
        {
            return new SquirrelRandom(InternalState);
        }

        private static uint NextUintInternal(ref uint seed)
        {
            seed *= BitNoise1;
            seed ^= seed >> 8;
            seed += BitNoise2;
            seed ^= seed << 8;
            seed *= BitNoise3;
            seed ^= seed >> 8;
            return seed;
        }

        // https://youtu.be/LWFzPP8ZbdU?t=2906
        private static float NextNoise(int x, uint seed)
        {
            uint result = unchecked((uint)x);
            result *= BitNoise1;
            result += seed;
            result ^= result >> 8;
            result += BitNoise2;
            result ^= result << 8;
            result *= BitNoise3;
            result ^= result >> 8;
            return (result >> 8) * MagicFloat;
        }

        private static float NextNoise(int x, int y, uint seed)
        {
            return NextNoise(x + LargePrime * y, seed);
        }
    }
}
