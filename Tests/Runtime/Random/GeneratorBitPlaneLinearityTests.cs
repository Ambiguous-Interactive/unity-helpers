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
    /// at exactly <c>c</c>. For a linear bit plane the rank is therefore capped by the generator's state
    /// width whatever the seed; for a non-linear one it is a random variable, with deficiency <c>k</c>
    /// occurring at roughly <c>4^-k</c>. Measured separation is wide -- linear planes score 2 to 128,
    /// non-linear ones never dropped below 185 of 192 over four seeds -- so the threshold sits in the empty
    /// band between the two populations.
    ///
    /// The seeds here are compile-time constants, so this fixture measures one fixed stream per generator
    /// and its result is reproducible rather than sampled. That, not seed-independence, is why it cannot
    /// flake: seeding it randomly would turn a structural gate into a statistical one.
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
        private const int UintBitCount = 32;
        private const int UlongBitCount = 64;

        // Any value strictly between the worst non-linear generator (185) and the best linear one (128)
        // separates the two populations; this sits in the middle of that band.
        private const int MinimumBitPlaneRank = 160;

        private static readonly Guid Seed = new("00010203-0405-0607-0809-0a0b0c0d0e0f");
        private const int IntSeed = 0x1BADC0DE;

        // Generators whose output bits are linear by construction. Each is already rated Fair or worse
        // and documents the weakness; they are listed so the exemption is asserted rather than assumed.
        //
        // WDoomRandom used to be here and is not any more. Its bits were linear because its table held
        // bytes and a uint was four of them; the table now holds whole 32-bit words drawn from SplitMix64, and
        // the worst plane measures rank 189 against this fixture's 160 threshold. Its rating stays Poor,
        // because linearity was never the reason for it: the period is 1024 draws, and PractRand 0.95
        // fails it at 8KB.
        //
        // XoroShiroRandom left for a different reason: the linear half is still there, it is simply not
        // returned any more. xoroshiro128+ computes a 64-bit word whose bit 0 is a GF(2) recurrence of
        // order 128, and the generator used to return the low half of it; it now returns the high half,
        // which is the half its authors recommend and which this fixture measures as non-linear.
        private static readonly string[] KnownLinearGenerators =
        {
            nameof(LinearCongruentialGenerator),
            nameof(XorShiftRandom),
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
            // Ratings are ordered best-first, so only ratings strictly weaker than Good are exempt.
            // Testing that way rather than listing the strong ratings keeps Unknown -- a generator whose
            // metadata attribute went missing -- inside the gate rather than silently outside it.
            bool exempt = (int)RandomQuality.Good < (int)metadata.Quality;
            if (exempt)
            {
                Assert.Pass(
                    $"{name} is rated {metadata.QualityLabel}; the quality gate covers Good and better."
                );
                return;
            }

            AssertNoLinearPlane(name, metadata.QualityLabel, factory(), false);
            AssertNoLinearPlane(name, metadata.QualityLabel, factory(), true);
        }

        private static void AssertNoLinearPlane(
            string name,
            string qualityLabel,
            IRandom random,
            bool sixtyFourBit
        )
        {
            int[] ranks = MeasureBitPlaneRanks(random, sixtyFourBit);
            string source = sixtyFourBit ? nameof(IRandom.NextUlong) : nameof(IRandom.NextUint);
            for (int bit = 0; bit < ranks.Length; ++bit)
            {
                Assert.GreaterOrEqual(
                    ranks[bit],
                    MinimumBitPlaneRank,
                    $"{name} is rated {qualityLabel}, but bit {bit} of {source}() satisfies a "
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

                int[] ranks = MeasureBitPlaneRanks(factory(), false);
                int worst = int.MaxValue;
                for (int bit = 0; bit < ranks.Length; ++bit)
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

        // NextUlong() is measured as well as NextUint() because for four generators it is no longer the
        // same bits rearranged. They answer it from one raw 64-bit word, so its high half reaches a caller
        // through NextDouble (the top 53 bits) and NextLong without ever appearing in a NextUint draw.
        private static int[] MeasureBitPlaneRanks(IRandom random, bool sixtyFourBit)
        {
            ulong[] draws = new ulong[2 * Dimension];
            for (int i = 0; i < draws.Length; ++i)
            {
                draws[i] = sixtyFourBit ? random.NextUlong() : random.NextUint();
            }

            int[] ranks = new int[sixtyFourBit ? UlongBitCount : UintBitCount];
            ulong[] rows = new ulong[Dimension * WordsPerRow];
            for (int bit = 0; bit < ranks.Length; ++bit)
            {
                ranks[bit] = BitPlaneRank(draws, bit, rows);
            }

            return ranks;
        }

        // Rank over GF(2) of the Hankel matrix whose row i is bits [i, i + Dimension) of one bit plane.
        private static int BitPlaneRank(ulong[] draws, int bit, ulong[] rows)
        {
            Array.Clear(rows, 0, rows.Length);
            for (int row = 0; row < Dimension; ++row)
            {
                int offset = row * WordsPerRow;
                for (int column = 0; column < Dimension; ++column)
                {
                    if (((draws[row + column] >> bit) & 1UL) != 0UL)
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
