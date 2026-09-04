// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    /// <summary>
    /// NativePcgRandom is a struct and therefore cannot inherit the IRandom suite in
    /// RandomTestBase. These cover the numeric contracts that suite enforces for every
    /// other generator, each of which this type had drifted away from.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class NativePcgRandomTests
    {
        private const int DistributionSamples = 200_000;

        private static IEnumerable<int> Seeds()
        {
            yield return int.MinValue;
            yield return -1;
            yield return 0;
            yield return 1;
            yield return 12345;
            yield return int.MaxValue;
        }

        // PCG's LCG step satisfies Hull-Dobell -- and is therefore full-period -- only
        // when the increment is odd. Neither constructor normalized one: measured 7482
        // of 10000 integer seeds and 5003 of 10000 Guid seeds landed on an even
        // increment, i.e. a shortened period. A statistical check does not catch this
        // (the output permutation reads the high state bits), so assert the structural
        // property directly.
        [Test]
        [TestCaseSource(nameof(Seeds))]
        public void IntegerSeedsProduceAnOddIncrement(int seed)
        {
            NativePcgRandom random = new(seed);
            Assert.AreEqual(
                1UL,
                random._increment & 1UL,
                $"Seed {seed} produced an even increment, which breaks PCG's full period."
            );
        }

        [Test]
        public void GuidSeedsProduceAnOddIncrement()
        {
            // Every Guid whose eighth byte is even used to yield an even increment.
            byte[] bytes = new byte[16];
            for (int i = 0; i < 256; i++)
            {
                bytes[8] = (byte)i;
                NativePcgRandom random = new(new Guid(bytes));
                Assert.AreEqual(
                    1UL,
                    random._increment & 1UL,
                    $"Guid with low increment byte {i} produced an even increment."
                );
            }
        }

        [Test]
        [TestCaseSource(nameof(Seeds))]
        public void IntegerSeedsProduceUniformOutput(int seed)
        {
            NativePcgRandom random = new(seed);
            AssertUniformLowBits(ref random, $"seed {seed}");
        }

        [Test]
        public void GuidSeedsProduceUniformOutput()
        {
            NativePcgRandom random = new(new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff"));
            AssertUniformLowBits(ref random, "guid seed");
        }

        // The shipped constant was 5.960465E-008F rather than 1f/(1<<24), which is
        // larger than the true scale: mantissas 16777214 and 16777215 both rounded to
        // exactly 1.0f. `(int)(NextFloat() * count)` therefore returned `count` about
        // once every 8.4 million draws.
        [Test]
        public void NextFloatStaysBelowOne()
        {
            NativePcgRandom random = new(12345);
            float maximum = float.MinValue;
            for (int i = 0; i < DistributionSamples; i++)
            {
                float value = random.NextFloat();
                Assert.GreaterOrEqual(value, 0f, "NextFloat returned a negative value.");
                Assert.Less(value, 1f, "NextFloat returned 1 or greater.");
                if (maximum < value)
                {
                    maximum = value;
                }
            }

            Assert.Greater(maximum, 0.99f, "NextFloat never approached its upper bound.");
        }

        // Exhaustive rather than sampled: 2^24 is every value the 24-bit mantissa can
        // take, so this cannot miss the two that used to round to 1 -- a sampled test
        // has roughly a 1-in-40 chance of drawing either. It multiplies by the
        // production constant, not a re-derived one, or it would only be asserting
        // that C# can evaluate 1f/(1<<24).
        [Test]
        public void NextFloatScaleIsExactForEveryMantissa()
        {
            const int MantissaCount = 1 << 24;
            for (int mantissa = 0; mantissa < MantissaCount; mantissa++)
            {
                float value = mantissa * NativePcgRandom.FloatScale;
                if (1f <= value)
                {
                    Assert.Fail($"Mantissa {mantissa} scales to {value:R}, which is not below 1.");
                }
            }
        }

        [Test]
        public void NextDoubleStaysBelowOne()
        {
            NativePcgRandom random = new(54321);
            double maximum = double.MinValue;
            for (int i = 0; i < DistributionSamples; i++)
            {
                double value = random.NextDouble();
                Assert.GreaterOrEqual(value, 0.0, "NextDouble returned a negative value.");
                Assert.Less(value, 1.0, "NextDouble returned 1 or greater.");
                if (maximum < value)
                {
                    maximum = value;
                }
            }

            Assert.Greater(maximum, 0.99, "NextDouble never approached its upper bound.");
        }

        // AbstractRandom.NextLong masks the sign bit; this type returned the raw 64
        // bits, so half its results were negative and `NextLong() % count` produced
        // negative indices.
        [Test]
        public void NextLongIsNeverNegative()
        {
            NativePcgRandom random = new(777);
            for (int i = 0; i < DistributionSamples; i++)
            {
                Assert.GreaterOrEqual(random.NextLong(), 0L, "NextLong returned a negative value.");
            }
        }

        [Test]
        public void NextBoolIsFair()
        {
            NativePcgRandom random = new(999);
            int trueCount = 0;
            for (int i = 0; i < DistributionSamples; i++)
            {
                if (random.NextBool())
                {
                    trueCount++;
                }
            }

            double probability = (double)trueCount / DistributionSamples;
            Assert.That(
                probability,
                Is.EqualTo(0.5).Within(0.01),
                $"NextBool produced true {probability:P2} of the time."
            );
        }

        // The old implementation spent a full 32-bit PCG step per flip. Buffering the
        // bits is the difference between 1 step per flip and 1 per 32, and this asserts
        // it structurally: a generator that has issued 32 flips must be exactly one
        // step ahead, not 32.
        [Test]
        public void ThirtyTwoCoinFlipsConsumeOneSample()
        {
            NativePcgRandom flipper = new(4242);
            NativePcgRandom reference = new(4242);

            for (int i = 0; i < 32; i++)
            {
                flipper.NextBool();
            }

            reference.NextUint();
            Assert.AreEqual(
                reference.NextUint(),
                flipper.NextUint(),
                "32 coin flips did not consume exactly one 32-bit sample."
            );
        }

        // A zero bound divided by zero inside the old rejection loop.
        [Test]
        public void BoundedNextRejectsNonPositiveBounds()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                NativePcgRandom random = new(1);
                random.NextUint(0);
            });
            Assert.Throws<ArgumentException>(() =>
            {
                NativePcgRandom random = new(1);
                random.Next(0);
            });
            Assert.Throws<ArgumentException>(() =>
            {
                NativePcgRandom random = new(1);
                random.Next(-5);
            });
        }

        [Test]
        [TestCase(2u)]
        [TestCase(3u)]
        [TestCase(7u)]
        [TestCase(16u)]
        [TestCase(100u)]
        public void BoundedNextIsUniformAndInRange(uint exclusiveMax)
        {
            NativePcgRandom random = new(31337);
            int[] counts = new int[exclusiveMax];
            int samples = (int)exclusiveMax * 20_000;
            for (int i = 0; i < samples; i++)
            {
                uint value = random.NextUint(exclusiveMax);
                Assert.Less(value, exclusiveMax, "NextUint(max) returned a value at or above max.");
                counts[value]++;
            }

            double expected = (double)samples / exclusiveMax;
            double chiSquare = 0;
            foreach (int count in counts)
            {
                double delta = count - expected;
                chiSquare += delta * delta / expected;
            }

            // 10x the p=0.001 critical value for the largest bucket count here, so this
            // fails on real bias rather than on an unlucky seed.
            double bound = 10.0 * exclusiveMax;
            Assert.Less(
                chiSquare,
                bound,
                $"NextUint({exclusiveMax}) chi-square {chiSquare:F2} exceeded {bound:F2}."
            );
        }

        /*
            A struct has one more construction site than its constructors: `default`. An unassigned
            field, an element of `new NativePcgRandom[n]`, and `default(T)` in a generic all arrive
            with a zero state and a zero increment, and zero is the LCG's fixed point -- so the
            state never advanced and every surface answered the same value forever. Measured on
            editor 6000.4.6f1 before the fix: NextUint() and NextUint(10) returned 0 on all six
            draws and NextBool() returned True on all eight, which is a stuck source wearing an
            in-range answer (#638).

            256 draws rather than a handful: a stream that passes through state 0 emits 0 twice
            before the permutation has any bits to work with, and NextBool serves 32 flips from
            each sample, so the first 64 flips of THIS stream are True for the ordinary reason
            rather than the broken one.
        */
        [Test]
        public void ADefaultConstructedGeneratorAdvances()
        {
            NativePcgRandom uints = default;
            NativePcgRandom bounded = default;
            NativePcgRandom bools = default;
            NativePcgRandom longs = default;

            HashSet<uint> distinctUints = new();
            HashSet<uint> distinctBounded = new();
            HashSet<bool> distinctBools = new();
            HashSet<long> distinctLongs = new();
            for (int i = 0; i < 256; i++)
            {
                distinctUints.Add(uints.NextUint());
                distinctBounded.Add(bounded.NextUint(10));
                distinctBools.Add(bools.NextBool());
                distinctLongs.Add(longs.NextLong());
            }

            Assert.Less(
                1,
                distinctUints.Count,
                "A default-constructed generator returned one value from NextUint() forever."
            );
            Assert.Less(
                1,
                distinctBounded.Count,
                "A default-constructed generator returned one value from NextUint(10) forever, "
                    + "which is in range and still not a draw."
            );
            Assert.AreEqual(
                2,
                distinctBools.Count,
                "A default-constructed generator returned one value from NextBool() forever."
            );
            Assert.Less(
                1,
                distinctLongs.Count,
                "A default-constructed generator returned one value from NextLong() forever."
            );
        }

        /*
            The control for the fix above: normalizing the increment where it is USED rather than
            only where it is assigned must not move a stream that was already correct. Every
            constructed instance holds an odd increment, so the normalization is the identity for
            all of them -- and these vectors were measured in editor 6000.4.6f1 BEFORE the change,
            so they fail if that reasoning is wrong.
        */
        [Test]
        [TestCase(
            12345,
            new uint[]
            {
                810091774u,
                2058501951u,
                4179075400u,
                1483483657u,
                1065695692u,
                3879941362u,
                728063719u,
                246938095u,
            },
            TestName = "SeededStreamIsUnchanged12345"
        )]
        [TestCase(
            2024,
            new uint[]
            {
                2987123831u,
                46647341u,
                3619409208u,
                634954543u,
                2105492914u,
                1552962672u,
                1204694862u,
                2427746999u,
            },
            TestName = "SeededStreamIsUnchanged2024"
        )]
        [TestCase(
            0,
            new uint[]
            {
                512347103u,
                1946694163u,
                3533213373u,
                978267510u,
                3347358660u,
                4097000983u,
                2473005997u,
                4252041869u,
            },
            TestName = "SeededStreamIsUnchangedZero"
        )]
        [TestCase(
            -1,
            new uint[]
            {
                1612242395u,
                1573221433u,
                3807779709u,
                678677722u,
                540207772u,
                2604697432u,
                1394940986u,
                4009330181u,
            },
            TestName = "SeededStreamIsUnchangedNegativeOne"
        )]
        public void SeededStreamIsUnchanged(int seed, uint[] expected)
        {
            NativePcgRandom random = new(seed);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], random.NextUint(), $"Seed {seed}, draw {i}.");
            }
        }

        [Test]
        public void SameSeedProducesSameSequence()
        {
            NativePcgRandom first = new(2024);
            NativePcgRandom second = new(2024);
            for (int i = 0; i < 1_000; i++)
            {
                Assert.AreEqual(first.NextUint(), second.NextUint());
                Assert.AreEqual(first.NextBool(), second.NextBool());
                Assert.AreEqual(first.NextFloat(), second.NextFloat());
                Assert.AreEqual(first.NextDouble(), second.NextDouble());
                Assert.AreEqual(first.NextLong(), second.NextLong());
            }
        }

        [Test]
        public void DifferentSeedsDiverge()
        {
            NativePcgRandom first = new(1);
            NativePcgRandom second = new(2);
            bool diverged = false;
            for (int i = 0; i < 100; i++)
            {
                if (first.NextUint() != second.NextUint())
                {
                    diverged = true;
                    break;
                }
            }

            Assert.IsTrue(diverged, "Two differently seeded generators produced identical output.");
        }

        private static void AssertUniformLowBits(ref NativePcgRandom random, string label)
        {
            const int Buckets = 8;
            int[] counts = new int[Buckets];
            for (int i = 0; i < DistributionSamples; i++)
            {
                counts[random.NextUint() & (Buckets - 1)]++;
            }

            double expected = (double)DistributionSamples / Buckets;
            double chiSquare = 0;
            for (int i = 0; i < Buckets; i++)
            {
                double delta = counts[i] - expected;
                chiSquare += delta * delta / expected;
            }

            Assert.Less(
                chiSquare,
                50.0,
                $"Low bits from {label} were not uniform (chi-square {chiSquare:F2})."
            );
        }
    }
}
