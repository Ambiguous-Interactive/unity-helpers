// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is wyhash/wyrand, by Wang Yi, The Unlicense (Public Domain),
// https://github.com/wangyi-fudan/wyhash, by way of the .NET port cocowalla/wyhash-dotnet
// (MIT License), https://github.com/cocowalla/wyhash-dotnet. This is an adaptation of that work; the
// design is the original author's. See docs/project/third-party-notices.md.

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// A wyhash-inspired PRNG variant (WyRandom) leveraging multiply-mix operations for speed and good distribution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reference implementation: https://github.com/cocowalla/wyhash-dotnet/blob/master/src/WyHash/WyRng.cs
    /// </para>
    /// <para>
    /// Designed around 64-bit multiply-and-mix steps, this generator is fast and suitable for general-purpose
    /// randomness and hashing-like use cases. It is not a cryptographic hash nor a CSPRNG.
    /// </para>
    /// <para>Pros:</para>
    /// <list type="bullet">
    /// <item><description>Fast and simple; good distribution for typical gameplay uses.</description></item>
    /// <item><description>Deterministic across platforms.</description></item>
    /// </list>
    /// <para>Cons:</para>
    /// <list type="bullet">
    /// <item><description>Not cryptographically secure.</description></item>
    /// <item><description>Less widely standardized than PCG/Xoroshiro.</description></item>
    /// </list>
    /// <para>When to use:</para>
    /// <list type="bullet">
    /// <item><description>General gameplay RNG, weight selection, shuffles, seed generation.</description></item>
    /// </list>
    /// <para>When not to use:</para>
    /// <list type="bullet">
    /// <item><description>Security-sensitive contexts.</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// var rng = new WyRandom(Guid.NewGuid());
    /// uint u = rng.NextUint();
    /// var color = rng.NextColor(); // via RandomExtensions
    /// </code>
    /// </example>
    [RandomGeneratorMetadata(
        RandomQuality.VeryGood,
        "Wyhash-based generator; published testing shows it clears BigCrush/PractRand with wide seed coverage.",
        "Wang Yi 2019",
        "https://github.com/wangyi-fudan/wyhash",
        period: "2^64 (published)"
    )]
    [Serializable]
    [DataContract]
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
    [WProtoSubtype(typeof(AbstractRandom), 103)]
    public sealed partial class WyRandom : AbstractRandom
    {
        private const ulong Prime0 = 0xa0761d6478bd642f;
        private const ulong Prime1 = 0xe7037ed1a0b428db;

        public static WyRandom Instance => ThreadLocalRandom<WyRandom>.Instance;

        public override RandomState InternalState => BuildState(_state);

        [ProtoMember(6)]
        [WProtoMember(6)]
        private ulong _state;

        public WyRandom()
            : this(Guid.NewGuid()) { }

        public WyRandom(Guid guid)
        {
            _state = RandomUtilities.GuidToUInt64Pair(guid).First;
        }

        [JsonConstructor]
        public WyRandom(RandomState internalState)
        {
            _state = internalState.State1;
            RestoreCommonState(internalState);
        }

        public WyRandom(ulong state)
        {
            _state = state;
        }

        public override uint NextUint()
        {
            unchecked
            {
                return (uint)NextWord();
            }
        }

        public override ulong NextUlong()
        {
            return NextWord();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong NextWord()
        {
            unchecked
            {
                _state += Prime0;
                return Mum(_state ^ Prime1, _state);
            }
        }

        /// <summary>
        /// Perform a MUM (MUltiply and Mix) operation. Multiplies 2 unsigned 64-bit integers, then combines the
        /// hi and lo bits of the resulting 128-bit integer using XOR
        /// </summary>
        /// <param name="x">First 64-bit integer</param>
        /// <param name="y">Second 64-bit integer</param>
        /// <returns>Result of the MUM (MUltiply and Mix) operation</returns>
        private static ulong Mum(ulong x, ulong y)
        {
            (ulong hi, ulong lo) = Multiply64(x, y);
            return hi ^ lo;
        }

        /// <summary>
        /// Multiplies 2 unsigned 64-bit integers, returning the result in 2 ulongs representing the hi and lo bits
        /// of the resulting 128-bit integer
        ///
        /// Source: https://stackoverflow.com/a/51587262/25758, but with a faster lo calculation
        /// </summary>
        /// <remarks>
        /// <para>
        /// <seealso cref="System.Numerics.BigInteger"/> can perform multiplication on large integers, but it's
        /// comparatively slow, and an equivalent method allocates around 360B/call
        /// </para>
        /// <para>
        /// A BMI2 wide-multiply branch lived here behind <c>NETCOREAPP3_0_OR_GREATER</c>, which no Unity
        /// player defines -- so it never ran, and its pointer argument was the only reason the shipped
        /// runtime assembly needed <c>allowUnsafeCode</c>
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/637">#637</see>).
        /// The schoolbook product below is what every supported player has always executed.
        /// </para>
        /// </remarks>
        /// <param name="x">First 64-bit integer</param>
        /// <param name="y">Second 64-bit integer</param>
        /// <returns>Product of <paramref name="x"/> and <paramref name="y"/></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [SuppressMessage("ReSharper", "JoinDeclarationAndInitializer")]
        private static (ulong Hi, ulong Lo) Multiply64(ulong x, ulong y)
        {
            ulong hi;
            ulong lo;

            lo = x * y;

            ulong x0 = (uint)x;
            ulong x1 = x >> 32;

            ulong y0 = (uint)y;
            ulong y1 = y >> 32;

            ulong p11 = x1 * y1;
            ulong p01 = x0 * y1;
            ulong p10 = x1 * y0;
            ulong p00 = x0 * y0;

            // 64-bit product + two 32-bit values
            ulong middle = p10 + (p00 >> 32) + (uint)p01;

            // 64-bit product + two 32-bit values
            hi = p11 + (middle >> 32) + (p01 >> 32);

            return (hi, lo);
        }

        public override IRandom Copy()
        {
            return new WyRandom(_state);
        }
    }
}
