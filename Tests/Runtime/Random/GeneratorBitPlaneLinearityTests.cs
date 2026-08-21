// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    /// <summary>
    /// Measures, per output bit position, whether a generator's bit stream satisfies a short linear
    /// recurrence over GF(2) -- the defect that lets an observer predict every future value of that bit.
    /// </summary>
    /// <remarks>
    /// A sequence with linear complexity <c>c</c> makes any Hankel matrix wider than <c>c</c> rank-deficient
    /// at exactly <c>c</c>, so the rank is an exact structural quantity rather than a statistic: the same
    /// generator returns the same rank for every seed, and the test cannot flake. Measured separation is
    /// wide -- linear bit planes score 2 to 128, non-linear ones never drop below 185 across four seeds --
    /// so the threshold sits in the empty band between them.
    ///
    /// This exists because <c>AbstractRandom.NextBool()</c> reads bit 0 and <c>Next(0, powerOfTwo)</c> masks
    /// the low bits: a generator whose low bits are linear hands out predictable coin flips.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class GeneratorBitPlaneLinearityTests
    {
        private const int Dimension = 192;
        private const int WordsPerRow = Dimension / 64;
        private const int OutputBitCount = 32;

        // Any value strictly between the worst non-linear generator (185) and the best linear one (128)
        // separates the two populations; this sits in the middle of that band.
        private const int MinimumBitPlaneRank = 160;

        private static readonly Guid Seed = new("00010203-0405-0607-0809-0a0b0c0d0e0f");
        private const int IntSeed = 0x1BADC0DE;

        // Generators whose output bits are linear by construction. Each is already rated Fair or worse
        // and documents the weakness; they are listed so the exemption is asserted rather than assumed.
        private static readonly string[] KnownLinearGenerators =
        {
            nameof(LinearCongruentialGenerator),
            nameof(XorShiftRandom),
            nameof(WDoomRandom),
            nameof(XoroShiroRandom),
        };

        private static IEnumerable<TestCaseData> EveryGenerator()
        {
            foreach (KeyValuePair<string, Func<IRandom>> entry in Factories())
            {
                yield return new TestCaseData(entry.Key, entry.Value).SetName(
                    $"BitPlanes({entry.Key})"
                );
            }
        }

        private static IEnumerable<KeyValuePair<string, Func<IRandom>>> Factories()
        {
            yield return Entry(nameof(BlastCircuitRandom), () => new BlastCircuitRandom(Seed));
            yield return Entry(nameof(DotNetRandom), () => new DotNetRandom(Seed));
            yield return Entry(nameof(FlurryBurstRandom), () => new FlurryBurstRandom(Seed));
            yield return Entry(nameof(IllusionFlow), () => new IllusionFlow(Seed));
            yield return Entry(
                nameof(LinearCongruentialGenerator),
                () => new LinearCongruentialGenerator(Seed)
            );
            yield return Entry(nameof(PcgRandom), () => new PcgRandom(Seed));
            yield return Entry(nameof(PhotonSpinRandom), () => new PhotonSpinRandom(Seed));
            yield return Entry(nameof(RomuDuo), () => new RomuDuo(Seed));
            yield return Entry(nameof(SplitMix64), () => new SplitMix64(Seed));
            yield return Entry(nameof(SquirrelRandom), () => new SquirrelRandom(IntSeed));
            yield return Entry(nameof(StormDropRandom), () => new StormDropRandom(Seed));
            yield return Entry(nameof(SystemRandom), () => new SystemRandom(IntSeed));
            yield return Entry(nameof(WaveSplatRandom), () => new WaveSplatRandom(Seed));
            yield return Entry(nameof(WDoomRandom), () => new WDoomRandom(IntSeed));
            yield return Entry(nameof(WyRandom), () => new WyRandom(Seed));
            yield return Entry(nameof(XoroShiroRandom), () => new XoroShiroRandom(Seed));
            yield return Entry(nameof(XorShiftRandom), () => new XorShiftRandom(Seed));
            yield return Entry(nameof(Xoshiro128StarStar), () => new Xoshiro128StarStar(Seed));
            yield return Entry(nameof(Xoshiro256StarStar), () => new Xoshiro256StarStar(Seed));
        }

        private static KeyValuePair<string, Func<IRandom>> Entry(string name, Func<IRandom> factory)
        {
            return new KeyValuePair<string, Func<IRandom>>(name, factory);
        }

        [Test]
        [TestCaseSource(nameof(EveryGenerator))]
        public void GeneratorsRatedGoodOrBetterHaveNoLinearOutputBit(
            string name,
            Func<IRandom> factory
        )
        {
            IRandom random = factory();
            RandomGeneratorMetadata metadata = RandomGeneratorMetadataRegistry.Snapshot(random);
            bool claimsQuality =
                metadata.Quality == RandomQuality.Excellent
                || metadata.Quality == RandomQuality.VeryGood
                || metadata.Quality == RandomQuality.Good;

            if (!claimsQuality)
            {
                Assert.Pass(
                    $"{name} is rated {metadata.QualityLabel}; the quality gate covers Good and better."
                );
            }

            int[] ranks = MeasureBitPlaneRanks(factory());
            for (int bit = 0; bit < OutputBitCount; ++bit)
            {
                Assert.GreaterOrEqual(
                    ranks[bit],
                    MinimumBitPlaneRank,
                    $"{name} is rated {metadata.QualityLabel}, but bit {bit} of its output satisfies a "
                        + $"linear recurrence of order {ranks[bit]} over GF(2): {ranks[bit]} of {Dimension} "
                        + "independent rows. Every future value of that bit is predictable from that many "
                        + "observations. Either return a scrambled half of the word, or lower the rating "
                        + "and document the weakness."
                );
            }
        }

        [Test]
        public void KnownLinearGeneratorsAreStillLinearAndStillRatedBelowGood()
        {
            Dictionary<string, Func<IRandom>> factories = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Func<IRandom>> entry in Factories())
            {
                factories[entry.Key] = entry.Value;
            }

            foreach (string name in KnownLinearGenerators)
            {
                Assert.IsTrue(factories.TryGetValue(name, out Func<IRandom> factory), name);

                RandomGeneratorMetadata metadata = RandomGeneratorMetadataRegistry.Snapshot(
                    factory()
                );
                Assert.Greater(
                    (int)metadata.Quality,
                    (int)RandomQuality.Good,
                    $"{name} has a linear output bit, so it must stay rated below Good. Ratings are "
                        + "ordered best-first, so a larger value is a weaker rating."
                );

                int[] ranks = MeasureBitPlaneRanks(factory());
                int worst = int.MaxValue;
                for (int bit = 0; bit < OutputBitCount; ++bit)
                {
                    worst = Math.Min(worst, ranks[bit]);
                }

                Assert.Less(
                    worst,
                    MinimumBitPlaneRank,
                    $"{name} no longer has a linear output bit (worst plane rank {worst}). If that is "
                        + "deliberate, remove it from the known-linear list and raise its quality rating."
                );
            }
        }

        private static int[] MeasureBitPlaneRanks(IRandom random)
        {
            uint[] draws = new uint[2 * Dimension];
            for (int i = 0; i < draws.Length; ++i)
            {
                draws[i] = random.NextUint();
            }

            int[] ranks = new int[OutputBitCount];
            ulong[] rows = new ulong[Dimension * WordsPerRow];
            for (int bit = 0; bit < OutputBitCount; ++bit)
            {
                ranks[bit] = BitPlaneRank(draws, bit, rows);
            }

            return ranks;
        }

        // Rank over GF(2) of the Hankel matrix whose row i is bits [i, i + Dimension) of one bit plane.
        private static int BitPlaneRank(uint[] draws, int bit, ulong[] rows)
        {
            Array.Clear(rows, 0, rows.Length);
            for (int row = 0; row < Dimension; ++row)
            {
                int offset = row * WordsPerRow;
                for (int column = 0; column < Dimension; ++column)
                {
                    if (((draws[row + column] >> bit) & 1U) != 0U)
                    {
                        rows[offset + (column >> 6)] |= 1UL << (column & 63);
                    }
                }
            }

            int rank = 0;
            for (int column = 0; column < Dimension && rank < Dimension; ++column)
            {
                int word = column >> 6;
                ulong mask = 1UL << (column & 63);

                int pivot = -1;
                for (int row = rank; row < Dimension; ++row)
                {
                    if ((rows[(row * WordsPerRow) + word] & mask) != 0UL)
                    {
                        pivot = row;
                        break;
                    }
                }

                if (pivot < 0)
                {
                    continue;
                }

                if (pivot != rank)
                {
                    for (int w = 0; w < WordsPerRow; ++w)
                    {
                        (rows[(pivot * WordsPerRow) + w], rows[(rank * WordsPerRow) + w]) = (
                            rows[(rank * WordsPerRow) + w],
                            rows[(pivot * WordsPerRow) + w]
                        );
                    }
                }

                for (int row = 0; row < Dimension; ++row)
                {
                    if (row == rank || (rows[(row * WordsPerRow) + word] & mask) == 0UL)
                    {
                        continue;
                    }

                    for (int w = 0; w < WordsPerRow; ++w)
                    {
                        rows[(row * WordsPerRow) + w] ^= rows[(rank * WordsPerRow) + w];
                    }
                }

                ++rank;
            }

            return rank;
        }
    }
}
