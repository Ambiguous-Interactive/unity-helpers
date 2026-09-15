// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class WallMathStatisticsTests
    {
        private static readonly Random Rng = new Random(742);

        private static IEnumerable<TestCaseData> MedianFloatCases()
        {
            yield return new TestCaseData(new float[] { 3f }, 3f).SetName(
                "Median.Float.SingleElement"
            );
            yield return new TestCaseData(new float[] { 5f, 1f, 3f }, 3f).SetName(
                "Median.Float.OddCount.SortedMiddle"
            );
            yield return new TestCaseData(new float[] { 4f, 1f, 3f, 2f }, 2.5f).SetName(
                "Median.Float.EvenCount.AveragesMiddlePair"
            );
            yield return new TestCaseData(new float[] { -1f, -5f, -3f }, -3f).SetName(
                "Median.Float.NegativeValues"
            );
            yield return new TestCaseData(new float[] { 1f, 2f, 3f, 4f, 5f, 6f }, 3.5f).SetName(
                "Median.Float.EvenCount.SixElements"
            );
            yield return new TestCaseData(
                new float[] { float.MaxValue, -float.MaxValue },
                0f
            ).SetName("Median.Float.ExtremePair.DoesNotOverflow");
        }

        private static IEnumerable<TestCaseData> MedianIntegralCases()
        {
            yield return new TestCaseData(new int[] { 7 }, 7.0).SetName("Median.Int.SingleElement");
            yield return new TestCaseData(new int[] { 1, 2, 3, 4 }, 2.5).SetName(
                "Median.Int.EvenCount.HalfStep"
            );
            yield return new TestCaseData(new int[] { 9, -9 }, 0.0).SetName(
                "Median.Int.SymmetricPair"
            );
            yield return new TestCaseData(
                new long[] { long.MaxValue - 1, long.MaxValue },
                long.MaxValue - 0.5
            ).SetName("Median.Long.ExtremePair.DoesNotOverflow");
            yield return new TestCaseData(new long[] { -3L, 5L }, 1.0).SetName(
                "Median.Long.MixedSigns"
            );
        }

        private static IEnumerable<TestCaseData> PercentileCases()
        {
            float[] data = { 1f, 2f, 3f, 4f, 5f };
            yield return new TestCaseData(data, 0f, 1.0).SetName("Percentile.Minimum");
            yield return new TestCaseData(data, 1f, 5.0).SetName("Percentile.Maximum");
            yield return new TestCaseData(data, 0.5f, 3.0).SetName("Percentile.Median");
            yield return new TestCaseData(data, 0.25f, 2.0).SetName(
                "Percentile.ExactRank.NoInterpolation"
            );
            yield return new TestCaseData(data, 0.3f, 2.2).SetName(
                "Percentile.BetweenRanks.Interpolates"
            );
            yield return new TestCaseData(new int[] { 10, 20, 30, 40 }, 0.25, 17.5).SetName(
                "Percentile.Int.InterpolatedHalfStep"
            );
            yield return new TestCaseData(new long[] { 10L, 20L }, 0.5, 15.0).SetName(
                "Percentile.Long.EvenCount"
            );
            yield return new TestCaseData(new double[] { 2.5, 0.5, 1.5 }, 0.5, 1.5).SetName(
                "Percentile.Double.OddCount"
            );
        }

        private static IEnumerable<TestCaseData> MeanCases()
        {
            yield return new TestCaseData(new float[] { 2f, 3f }, 2.5f).SetName(
                "Mean.Float.HalfValue"
            );
            yield return new TestCaseData(new float[] { 5f }, 5f).SetName(
                "Mean.Float.SingleElement"
            );
            yield return new TestCaseData(
                new float[] { float.MaxValue, float.MaxValue },
                float.MaxValue
            ).SetName("Mean.Float.ExtremePair.DoesNotOverflow");
            yield return new TestCaseData(
                new int[] { int.MaxValue, int.MaxValue },
                (double)int.MaxValue
            ).SetName("Mean.Int.ExtremePair.DoesNotOverflow");
            yield return new TestCaseData(new int[] { -3, 4 }, 0.5).SetName("Mean.Int.MixedSigns");
            yield return new TestCaseData(
                new long[] { long.MaxValue, long.MaxValue },
                (double)long.MaxValue
            ).SetName("Mean.Long.ExtremePair.DoesNotOverflow");
            yield return new TestCaseData(new double[] { 0.5, 0.25 }, 0.375).SetName(
                "Mean.Double.Exact"
            );
        }

        private static IEnumerable<TestCaseData> StandardDeviationCases()
        {
            yield return new TestCaseData(
                new float[] { 2f, 4f, 4f, 4f, 5f, 5f, 7f, 9f },
                false,
                2.0
            ).SetName("StandardDeviation.Population.ClassicExample");
            yield return new TestCaseData(
                new float[] { 2f, 4f, 4f, 4f, 5f, 5f, 7f, 9f },
                true,
                2.138089935299395
            ).SetName("StandardDeviation.Sample.ClassicExample");
            yield return new TestCaseData(new float[] { 42f }, false, 0.0).SetName(
                "StandardDeviation.Population.SingleElement"
            );
            yield return new TestCaseData(new double[] { 2.0, 4.0 }, true, Math.Sqrt(2.0)).SetName(
                "StandardDeviation.Sample.TwoElements"
            );
            yield return new TestCaseData(new double[] { -3.0, 3.0 }, false, 3.0).SetName(
                "StandardDeviation.Population.Symmetric"
            );
        }

        private static IEnumerable<TestCaseData> InvalidPercentileCases()
        {
            yield return new TestCaseData(float.NaN).SetName("InvalidPercentile.NaN");
            yield return new TestCaseData(-0.0001f).SetName("InvalidPercentile.BelowRange");
            yield return new TestCaseData(1.0001f).SetName("InvalidPercentile.AboveRange");
        }

        private static double BinomialProbabilityAtMost(int successes, int trials, double chance)
        {
            double failureChance = 1.0 - chance;
            double probability = Math.Pow(failureChance, trials);
            double total = probability;
            for (int observed = 0; observed < successes; ++observed)
            {
                probability *= (trials - observed) * chance / ((observed + 1) * failureChance);
                total += probability;
            }

            return total;
        }

        private static double Combination(int count, int selection)
        {
            selection = Math.Min(selection, count - selection);
            double result = 1.0;
            for (int i = 1; i <= selection; ++i)
            {
                result *= (double)(count - selection + i) / i;
            }

            return result;
        }

        private static double FisherExactReference(
            int upperLeft,
            int upperRight,
            int lowerLeft,
            int lowerRight
        )
        {
            int firstRow = upperLeft + upperRight;
            int secondRow = lowerLeft + lowerRight;
            int firstColumn = upperLeft + lowerLeft;
            int total = firstRow + secondRow;
            int minimum = Math.Max(0, firstColumn - secondRow);
            int maximum = Math.Min(firstRow, firstColumn);
            double divisor = Combination(total, firstColumn);
            double observed =
                Combination(firstRow, upperLeft)
                * Combination(secondRow, firstColumn - upperLeft)
                / divisor;
            double result = 0.0;
            for (int candidate = minimum; candidate <= maximum; ++candidate)
            {
                double probability =
                    Combination(firstRow, candidate)
                    * Combination(secondRow, firstColumn - candidate)
                    / divisor;
                if (probability <= observed * (1.0 + 1e-12))
                {
                    result += probability;
                }
            }

            return Math.Min(1.0, result);
        }

        [Test]
        public void ClopperPearsonRejectsInvalidInputs()
        {
            (int successes, int trials, double confidenceLevel)[] invalidInputs =
            {
                (-1, 10, 0.95),
                (11, 10, 0.95),
                (0, 0, 0.95),
                (0, -1, 0.95),
                (5, 10, 0.0),
                (5, 10, 1.0),
                (5, 10, double.NaN),
                (5, 10, double.PositiveInfinity),
            };

            foreach (
                (int successes, int trials, double confidenceLevel) invalidInput in invalidInputs
            )
            {
                bool success = WallMath.TryClopperPearsonInterval(
                    invalidInput.successes,
                    invalidInput.trials,
                    invalidInput.confidenceLevel,
                    out double lowerBound,
                    out double upperBound
                );

                Assert.IsFalse(success, $"Expected {invalidInput} to fail.");
                Assert.AreEqual(0.0, lowerBound, $"Expected {invalidInput} to clear lower.");
                Assert.AreEqual(0.0, upperBound, $"Expected {invalidInput} to clear upper.");
            }
        }

        [Test]
        public void ClopperPearsonUsesExactBoundaryFormulas()
        {
            const int trials = 10;
            const double confidenceLevel = 0.95;
            double tail = (1.0 - confidenceLevel) / 2.0;

            Assert.IsTrue(
                WallMath.TryClopperPearsonInterval(
                    0,
                    trials,
                    confidenceLevel,
                    out double zeroLower,
                    out double zeroUpper
                )
            );
            Assert.AreEqual(0.0, zeroLower);
            Assert.AreEqual(1.0 - Math.Pow(tail, 1.0 / trials), zeroUpper, 1e-12);

            Assert.IsTrue(
                WallMath.TryClopperPearsonInterval(
                    trials,
                    trials,
                    confidenceLevel,
                    out double allLower,
                    out double allUpper
                )
            );
            Assert.AreEqual(Math.Pow(tail, 1.0 / trials), allLower, 1e-12);
            Assert.AreEqual(1.0, allUpper);

            double extremeConfidence = BitConverter.Int64BitsToDouble(
                BitConverter.DoubleToInt64Bits(1.0) - 1
            );
            double extremeTail = (1.0 - extremeConfidence) / 2.0;
            Assert.IsTrue(
                WallMath.TryClopperPearsonInterval(
                    0,
                    trials,
                    extremeConfidence,
                    out double extremeLower,
                    out double extremeUpper
                )
            );
            Assert.AreEqual(0.0, extremeLower);
            Assert.AreEqual(1.0 - Math.Pow(extremeTail, 1.0 / trials), extremeUpper, 1e-12);

            Assert.IsTrue(
                WallMath.TryClopperPearsonInterval(
                    1,
                    int.MaxValue,
                    extremeConfidence,
                    out double rareLower,
                    out double rareUpper
                )
            );
            double rareLowerApproximation = extremeTail / int.MaxValue;
            Assert.That(rareLower / rareLowerApproximation, Is.InRange(0.999, 1.001));
            Assert.That(rareUpper, Is.InRange(rareLower, 1.0));
        }

        [Test]
        public void ClopperPearsonInteriorBoundsSatisfyDefiningTails()
        {
            const int successes = 3;
            const int trials = 10;
            const double confidenceLevel = 0.95;
            double tail = (1.0 - confidenceLevel) / 2.0;

            Assert.IsTrue(
                WallMath.TryClopperPearsonInterval(
                    successes,
                    trials,
                    confidenceLevel,
                    out double lowerBound,
                    out double upperBound
                )
            );

            double lowerTail = 1.0 - BinomialProbabilityAtMost(successes - 1, trials, lowerBound);
            double upperTail = BinomialProbabilityAtMost(successes, trials, upperBound);
            Assert.AreEqual(tail, lowerTail, 1e-10);
            Assert.AreEqual(tail, upperTail, 1e-10);
            Assert.That(lowerBound, Is.LessThan((double)successes / trials));
            Assert.That((double)successes / trials, Is.LessThan(upperBound));
        }

        [Test]
        public void ClopperPearsonIsSymmetricAndWidensWithConfidence()
        {
            Assert.IsTrue(
                WallMath.TryClopperPearsonInterval(
                    7,
                    20,
                    0.9,
                    out double lower90,
                    out double upper90
                )
            );
            Assert.IsTrue(
                WallMath.TryClopperPearsonInterval(
                    7,
                    20,
                    0.99,
                    out double lower99,
                    out double upper99
                )
            );
            Assert.IsTrue(
                WallMath.TryClopperPearsonInterval(
                    13,
                    20,
                    0.9,
                    out double reflectedLower,
                    out double reflectedUpper
                )
            );

            Assert.That(lower99, Is.LessThan(lower90));
            Assert.That(upper90, Is.LessThan(upper99));
            Assert.AreEqual(1.0 - upper90, reflectedLower, 1e-12);
            Assert.AreEqual(1.0 - lower90, reflectedUpper, 1e-12);
        }

        [Test]
        public void ClopperPearsonHandlesLargeCountsWithBoundedFiniteOutput()
        {
            Assert.IsTrue(
                WallMath.TryClopperPearsonInterval(
                    3000,
                    10000,
                    0.999,
                    out double lowerBound,
                    out double upperBound
                )
            );

            Assert.That(lowerBound, Is.InRange(0.0, 0.3));
            Assert.That(upperBound, Is.InRange(0.3, 1.0));
            Assert.IsFalse(double.IsNaN(lowerBound));
            Assert.IsFalse(double.IsNaN(upperBound));

            Assert.IsTrue(
                WallMath.TryClopperPearsonInterval(
                    500_000,
                    1_000_000,
                    0.95,
                    out double balancedLower,
                    out double balancedUpper
                )
            );
            Assert.That(balancedLower, Is.InRange(0.49, 0.5));
            Assert.That(balancedUpper, Is.InRange(0.5, 0.51));
        }

        [Test]
        public void ExactSignTestMatchesExhaustiveBinomialReference()
        {
            for (int positiveDifferences = 0; positiveDifferences <= 32; ++positiveDifferences)
            {
                for (
                    int negativeDifferences = 0;
                    negativeDifferences <= 32 - positiveDifferences;
                    ++negativeDifferences
                )
                {
                    int trials = positiveDifferences + negativeDifferences;
                    if (trials == 0)
                    {
                        continue;
                    }

                    int smallerCount = Math.Min(positiveDifferences, negativeDifferences);
                    double expected = Math.Min(
                        1.0,
                        2.0 * BinomialProbabilityAtMost(smallerCount, trials, 0.5)
                    );

                    Assert.IsTrue(
                        WallMath.TryExactSignTest(
                            positiveDifferences,
                            negativeDifferences,
                            out double pValue
                        )
                    );
                    Assert.AreEqual(
                        expected,
                        pValue,
                        1e-12,
                        $"Unexpected p-value for {positiveDifferences} positive and {negativeDifferences} negative differences."
                    );
                }
            }
        }

        [Test]
        public void ExactSignTestRejectsInvalidCountsAndClearsOutput()
        {
            (int positiveDifferences, int negativeDifferences)[] invalidInputs =
            {
                (-1, 1),
                (1, -1),
                (0, 0),
                (int.MaxValue, 1),
            };

            foreach (
                (int positiveDifferences, int negativeDifferences) invalidInput in invalidInputs
            )
            {
                Assert.IsFalse(
                    WallMath.TryExactSignTest(
                        invalidInput.positiveDifferences,
                        invalidInput.negativeDifferences,
                        out double pValue
                    )
                );
                Assert.AreEqual(0.0, pValue);
            }
        }

        [Test]
        public void ExactSignTestHandlesLargeAndSymmetricCounts()
        {
            Assert.IsTrue(WallMath.TryExactSignTest(500_000, 500_000, out double balanced));
            Assert.AreEqual(1.0, balanced);

            Assert.IsTrue(
                WallMath.TryExactSignTest(
                    1_073_741_823,
                    1_073_741_824,
                    out double maximumOddBalanced
                )
            );
            Assert.AreEqual(1.0, maximumOddBalanced);

            Assert.IsTrue(WallMath.TryExactSignTest(0, 1_000, out double extremeTail));
            Assert.AreEqual(Math.Pow(0.5, 999), extremeTail);

            Assert.IsTrue(WallMath.TryExactSignTest(0, 10_000, out double underflowedTail));
            Assert.AreEqual(0.0, underflowedTail);

            Assert.IsTrue(WallMath.TryExactSignTest(8, 2, out double forward));
            Assert.IsTrue(WallMath.TryExactSignTest(2, 8, out double reflected));
            Assert.AreEqual(0.109375, forward, 1e-12);
            Assert.AreEqual(forward, reflected);

            Assert.IsTrue(WallMath.TryExactSignTest(499_999, 500_001, out double nearBalanced));
            /* For 2m trials split m - 1 to m + 1, the doubled tail is one minus
             * the central binomial probability C(2m, m) / 2^(2m). */
            Assert.AreEqual(0.9992021156392126, nearBalanced, 5e-10);
        }

        [Test]
        public void FisherExactMatchesKnownTables()
        {
            Assert.IsTrue(WallMath.TryFisherExactTest(1, 9, 11, 3, out double first));
            Assert.AreEqual(0.0027594561852200836, first, 1e-14);

            Assert.IsTrue(WallMath.TryFisherExactTest(8, 2, 1, 5, out double second));
            Assert.AreEqual(0.03496503496503496, second, 1e-14);

            Assert.IsTrue(WallMath.TryFisherExactTest(0, 5, 0, 7, out double degenerate));
            Assert.AreEqual(1.0, degenerate);
        }

        [Test]
        public void FisherExactIncludesTheObservedModeForBalancedLargeTables()
        {
            Assert.IsTrue(WallMath.TryFisherExactTest(50, 1000, 50, 1000, out double pValue));
            Assert.AreEqual(1.0, pValue, 1e-14);

            Assert.IsTrue(
                WallMath.TryFisherExactTest(int.MaxValue, 0, 0, 0, out double narrowPValue)
            );
            Assert.AreEqual(1.0, narrowPValue);
        }

        [Test]
        public void FisherExactMatchesExhaustiveSmallTableReference()
        {
            for (int upperLeft = 0; upperLeft <= 4; ++upperLeft)
            {
                for (int upperRight = 0; upperRight <= 4; ++upperRight)
                {
                    for (int lowerLeft = 0; lowerLeft <= 4; ++lowerLeft)
                    {
                        for (int lowerRight = 0; lowerRight <= 4; ++lowerRight)
                        {
                            if (upperLeft + upperRight + lowerLeft + lowerRight == 0)
                            {
                                continue;
                            }

                            double expected = FisherExactReference(
                                upperLeft,
                                upperRight,
                                lowerLeft,
                                lowerRight
                            );
                            Assert.IsTrue(
                                WallMath.TryFisherExactTest(
                                    upperLeft,
                                    upperRight,
                                    lowerLeft,
                                    lowerRight,
                                    out double actual
                                )
                            );
                            Assert.AreEqual(
                                expected,
                                actual,
                                1e-12,
                                $"Unexpected table ({upperLeft}, {upperRight}, {lowerLeft}, {lowerRight})."
                            );
                        }
                    }
                }
            }
        }

        [Test]
        public void FisherExactIsInvariantUnderTableReflections()
        {
            Assert.IsTrue(WallMath.TryFisherExactTest(3, 7, 8, 2, out double original));
            (int upperLeft, int upperRight, int lowerLeft, int lowerRight)[] reflections =
            {
                (8, 2, 3, 7),
                (7, 3, 2, 8),
                (2, 8, 7, 3),
                (3, 8, 7, 2),
                (8, 3, 2, 7),
                (7, 2, 3, 8),
                (2, 7, 8, 3),
            };
            foreach (
                (
                    int upperLeft,
                    int upperRight,
                    int lowerLeft,
                    int lowerRight
                ) reflection in reflections
            )
            {
                Assert.IsTrue(
                    WallMath.TryFisherExactTest(
                        reflection.upperLeft,
                        reflection.upperRight,
                        reflection.lowerLeft,
                        reflection.lowerRight,
                        out double reflected
                    )
                );
                Assert.AreEqual(original, reflected, 1e-12);
            }
        }

        [Test]
        public void FisherExactRejectsInvalidOrUnboundedTablesAndClearsOutput()
        {
            (int upperLeft, int upperRight, int lowerLeft, int lowerRight)[] invalidInputs =
            {
                (-1, 0, 0, 0),
                (0, -1, 0, 0),
                (0, 0, -1, 0),
                (0, 0, 0, -1),
                (0, 0, 0, 0),
                (int.MaxValue, 1, 0, 0),
                (600_000, 600_000, 600_000, 600_000),
            };

            foreach (
                (
                    int upperLeft,
                    int upperRight,
                    int lowerLeft,
                    int lowerRight
                ) invalidInput in invalidInputs
            )
            {
                Assert.IsFalse(
                    WallMath.TryFisherExactTest(
                        invalidInput.upperLeft,
                        invalidInput.upperRight,
                        invalidInput.lowerLeft,
                        invalidInput.lowerRight,
                        out double pValue
                    )
                );
                Assert.AreEqual(0.0, pValue);
            }
        }

        [Test]
        [TestCaseSource(nameof(MedianFloatCases))]
        public void MedianFloatMatchesExpected(float[] values, float expected)
        {
            Assert.AreEqual(expected, values.Median(), 1e-4f);
        }

        [Test]
        [TestCaseSource(nameof(MedianIntegralCases))]
        public void MedianIntegralMatchesExpected(IList values, double expected)
        {
            if (values is int[] ints)
            {
                Assert.AreEqual(expected, ints.Median(), 1e-9);
            }
            else
            {
                Assert.AreEqual(expected, ((long[])values).Median(), 1e-9);
            }
        }

        [Test]
        [TestCaseSource(nameof(PercentileCases))]
        public void PercentileMatchesExpected(IList values, double percentile, double expected)
        {
            if (values is float[] floats)
            {
                Assert.AreEqual(expected, floats.Percentile((float)percentile), 1e-4f);
            }
            else if (values is int[] ints)
            {
                Assert.AreEqual(expected, ints.Percentile(percentile), 1e-9);
            }
            else if (values is double[] doubles)
            {
                Assert.AreEqual(expected, doubles.Percentile(percentile), 1e-9);
            }
            else
            {
                Assert.AreEqual(expected, ((long[])values).Percentile(percentile), 1e-9);
            }
        }

        [Test]
        [TestCaseSource(nameof(MeanCases))]
        public void MeanMatchesExpected(IList values, double expected)
        {
            if (values is float[] floats)
            {
                Assert.AreEqual(expected, floats.Mean(), 1e-4f);
            }
            else if (values is int[] ints)
            {
                Assert.AreEqual(expected, ints.Mean(), 1e-9);
            }
            else if (values is double[] doubles)
            {
                Assert.AreEqual(expected, doubles.Mean(), 1e-12);
            }
            else
            {
                Assert.AreEqual(expected, ((long[])values).Mean(), 1e-6);
            }
        }

        [Test]
        [TestCaseSource(nameof(StandardDeviationCases))]
        public void StandardDeviationMatchesExpected(IList values, bool sample, double expected)
        {
            if (values is float[] floats)
            {
                Assert.AreEqual(expected, floats.StandardDeviation(sample), 1e-4f);
            }
            else
            {
                Assert.AreEqual(expected, ((double[])values).StandardDeviation(sample), 1e-9);
            }
        }

        [Test]
        [TestCaseSource(nameof(InvalidPercentileCases))]
        public void PercentileRejectsInvalidPercentile(float percentile)
        {
            float[] values = { 1f, 2f, 3f };
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = values.Percentile(percentile));
        }

        [Test]
        public void MedianThrowsOnNull()
        {
            Assert.Throws<ArgumentNullException>(() => _ = ((float[])null).Median());
            Assert.Throws<ArgumentNullException>(() => _ = ((int[])null).Median());
            Assert.Throws<ArgumentNullException>(() => _ = ((double[])null).Mean());
            Assert.Throws<ArgumentNullException>(() => _ = ((long[])null).Mean());
        }

        [Test]
        public void StatisticsThrowOnEmpty()
        {
            Assert.Throws<ArgumentException>(() => _ = Array.Empty<float>().Median());
            Assert.Throws<ArgumentException>(() => _ = Array.Empty<double>().Median());
            Assert.Throws<ArgumentException>(() => _ = Array.Empty<int>().Percentile(0.5));
            Assert.Throws<ArgumentException>(() => _ = Array.Empty<long>().Percentile(0.5));
            Assert.Throws<ArgumentException>(() => _ = Array.Empty<float>().Mean());
            Assert.Throws<ArgumentException>(() => _ = Array.Empty<double>().Mean());
            Assert.Throws<ArgumentException>(() => _ = Array.Empty<int>().Mean());
            Assert.Throws<ArgumentException>(() => _ = Array.Empty<long>().Mean());
            Assert.Throws<ArgumentException>(() => _ = Array.Empty<float>().StandardDeviation());
            Assert.Throws<ArgumentException>(() =>
                _ = Array.Empty<double>().StandardDeviation(sample: true)
            );
        }

        [Test]
        public void SampleStandardDeviationRequiresTwoElements()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _ = new float[] { 1f }.StandardDeviation(sample: true)
            );
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _ = new double[] { 1.0 }.StandardDeviation(sample: true)
            );
            Assert.DoesNotThrow(() => _ = new float[] { 1f }.StandardDeviation());
        }

        [Test]
        public void StatisticsDoNotMutateTheCallerList()
        {
            List<float> values = new List<float> { 3f, 1f, 2f };
            float[] snapshot = values.ToArray();
            _ = values.Median();
            _ = values.Percentile(0.75f);
            _ = values.Mean();
            _ = values.StandardDeviation();
            CollectionAssert.AreEqual(snapshot, values);
        }

        [Test]
        public void MedianAgreesWithReferenceAcrossSeededSizes()
        {
            for (int count = 1; count <= 40; ++count)
            {
                float[] values = new float[count];
                for (int i = 0; i < count; ++i)
                {
                    values[i] = Rng.Next(-1000, 1000) + (float)Rng.NextDouble();
                }

                float[] sorted = (float[])values.Clone();
                Array.Sort(sorted);
                double expected =
                    count % 2 == 1
                        ? sorted[count / 2]
                        : sorted[count / 2 - 1] / 2.0 + sorted[count / 2] / 2.0;

                Assert.AreEqual(
                    expected,
                    values.Median(),
                    1e-3f,
                    $"Median diverged at count {count}."
                );
            }
        }

        [Test]
        public void PercentileAgreesWithReferenceAcrossSeededSizes()
        {
            for (int count = 1; count <= 25; ++count)
            {
                double[] values = new double[count];
                for (int i = 0; i < count; ++i)
                {
                    values[i] = Rng.NextDouble() * 200.0 - 100.0;
                }

                double percentile = Rng.NextDouble();
                double[] sorted = (double[])values.Clone();
                Array.Sort(sorted);
                double rank = percentile * (count - 1);
                int lower = (int)rank;
                int upper = Math.Min(lower + 1, count - 1);
                double expected = sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);

                Assert.AreEqual(
                    expected,
                    values.Percentile(percentile),
                    1e-9,
                    $"Percentile diverged at count {count}."
                );
            }
        }
    }
}
