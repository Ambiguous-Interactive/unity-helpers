// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

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

        private static IEnumerable<TestCaseData> PercentileExtremeCases()
        {
            double[] oppositeExtremes = { -double.MaxValue, double.MaxValue };
            yield return new TestCaseData(oppositeExtremes, 0.0, -double.MaxValue).SetName(
                "Percentile.ExtremeDouble.Minimum"
            );
            yield return new TestCaseData(oppositeExtremes, 0.25, -double.MaxValue / 2.0).SetName(
                "Percentile.ExtremeDouble.LowerQuarter"
            );
            yield return new TestCaseData(oppositeExtremes, 0.5, 0.0).SetName(
                "Percentile.ExtremeDouble.Midpoint"
            );
            yield return new TestCaseData(oppositeExtremes, 0.75, double.MaxValue / 2.0).SetName(
                "Percentile.ExtremeDouble.UpperQuarter"
            );
            yield return new TestCaseData(oppositeExtremes, 1.0, double.MaxValue).SetName(
                "Percentile.ExtremeDouble.Maximum"
            );
            yield return new TestCaseData(
                new double[] { double.MaxValue / 2.0, -double.MaxValue },
                0.5,
                -double.MaxValue / 4.0
            ).SetName("Percentile.ExtremeDouble.UnequalOppositeMagnitudes");
            yield return new TestCaseData(
                new double[] { double.MaxValue, double.MaxValue / 2.0 },
                0.5,
                double.MaxValue * 0.75
            ).SetName("Percentile.ExtremeDouble.PositivePair");
            yield return new TestCaseData(
                new double[] { -double.MaxValue, -double.MaxValue / 2.0 },
                0.5,
                -double.MaxValue * 0.75
            ).SetName("Percentile.ExtremeDouble.NegativePair");
            yield return new TestCaseData(
                new double[] { double.Epsilon, double.Epsilon },
                0.5,
                double.Epsilon
            ).SetName("Percentile.SubnormalDouble.RepeatedValue");
            yield return new TestCaseData(
                new double[] { -2.0 * double.Epsilon, 2.0 * double.Epsilon },
                0.25,
                -double.Epsilon
            ).SetName("Percentile.SubnormalDouble.OppositeSigns");
            yield return new TestCaseData(
                new double[] { double.Epsilon, 3.0 * double.Epsilon },
                0.5,
                2.0 * double.Epsilon
            ).SetName("Percentile.SubnormalDouble.PositivePair");
            yield return new TestCaseData(
                new double[] { double.PositiveInfinity, double.PositiveInfinity },
                0.5,
                double.PositiveInfinity
            ).SetName("Percentile.InfinityDouble.RepeatedPositive");
            yield return new TestCaseData(
                new double[] { double.NegativeInfinity, double.NegativeInfinity },
                0.5,
                double.NegativeInfinity
            ).SetName("Percentile.InfinityDouble.RepeatedNegative");
            yield return new TestCaseData(
                new double[] { double.NegativeInfinity, -1.0 },
                0.5,
                double.NegativeInfinity
            ).SetName("Percentile.InfinityDouble.NegativeWithFinite");
            yield return new TestCaseData(
                new double[] { 1.0, double.PositiveInfinity },
                0.5,
                double.PositiveInfinity
            ).SetName("Percentile.InfinityDouble.PositiveWithFinite");
            yield return new TestCaseData(
                new double[] { double.NegativeInfinity, double.PositiveInfinity },
                0.5,
                double.NaN
            ).SetName("Percentile.InfinityDouble.OppositeSignsUndefined");
            yield return new TestCaseData(
                new double[] { double.NegativeInfinity, double.PositiveInfinity },
                0.0,
                double.NegativeInfinity
            ).SetName("Percentile.InfinityDouble.ExactMinimum");
            yield return new TestCaseData(
                new double[] { double.NegativeInfinity, double.PositiveInfinity },
                1.0,
                double.PositiveInfinity
            ).SetName("Percentile.InfinityDouble.ExactMaximum");
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

        private static IEnumerable<TestCaseData> MeanListScratchCases()
        {
            yield return new TestCaseData(new List<float> { 2f, 3f }, 2.5).SetName(
                "Mean.ListScratch.Float.NoDisposalLease"
            );
            yield return new TestCaseData(new List<double> { 0.5, 0.25 }, 0.375).SetName(
                "Mean.ListScratch.Double.NoDisposalLease"
            );
            yield return new TestCaseData(new List<int> { -3, 4 }, 0.5).SetName(
                "Mean.ListScratch.Int.NoDisposalLease"
            );
            yield return new TestCaseData(new List<long> { -3L, 4L }, 0.5).SetName(
                "Mean.ListScratch.Long.NoDisposalLease"
            );
        }

        private static IEnumerable<TestCaseData> MeanListNumericCases()
        {
            yield return new TestCaseData(
                new List<float> { float.MaxValue, float.MaxValue },
                (double)float.MaxValue
            ).SetName("Mean.ListNumeric.Float.ExtremePair");
            yield return new TestCaseData(
                new List<float> { 16777216f, 1f, -16777216f },
                (double)(1f / 3f)
            ).SetName("Mean.ListNumeric.Float.DoubleAccumulation");
            yield return new TestCaseData(new List<float> { float.NaN, 1f }, double.NaN).SetName(
                "Mean.ListNumeric.Float.NaN"
            );
            yield return new TestCaseData(
                new List<float> { float.PositiveInfinity, float.NegativeInfinity },
                double.NaN
            ).SetName("Mean.ListNumeric.Float.OppositeInfinities");
            yield return new TestCaseData(
                new List<double> { double.MaxValue, double.MaxValue },
                double.PositiveInfinity
            ).SetName("Mean.ListNumeric.Double.OverflowUnchanged");
            yield return new TestCaseData(new List<double> { 1e16, 1.0, -1e16 }, 0.0).SetName(
                "Mean.ListNumeric.Double.AccumulationOrder"
            );
            yield return new TestCaseData(new List<double> { double.NaN, 1.0 }, double.NaN).SetName(
                "Mean.ListNumeric.Double.NaN"
            );
            yield return new TestCaseData(
                new List<double> { double.PositiveInfinity, double.NegativeInfinity },
                double.NaN
            ).SetName("Mean.ListNumeric.Double.OppositeInfinities");
            yield return new TestCaseData(
                new List<int> { int.MaxValue, int.MaxValue },
                (double)int.MaxValue
            ).SetName("Mean.ListNumeric.Int.ExtremePair");
            yield return new TestCaseData(
                new List<int> { int.MinValue, int.MaxValue },
                -0.5
            ).SetName("Mean.ListNumeric.Int.OppositeExtremes");
            yield return new TestCaseData(
                new List<long> { long.MaxValue, long.MaxValue },
                (double)long.MaxValue
            ).SetName("Mean.ListNumeric.Long.ExtremePair");
            yield return new TestCaseData(
                new List<long> { 9007199254740993L, -9007199254740992L },
                0.0
            ).SetName("Mean.ListNumeric.Long.Binary64RoundingUnchanged");
        }

        private static double MeanOfList(IList values)
        {
            if (values is List<float> floats)
            {
                return floats.Mean();
            }
            if (values is List<double> doubles)
            {
                return doubles.Mean();
            }
            if (values is List<int> ints)
            {
                return ints.Mean();
            }
            return ((List<long>)values).Mean();
        }

        private static void AssertMeanListScratchSizes<T>(T positive, T negative)
        {
            /* Nearby lengths share an ArrayPool bucket while their logical tails differ. */
            int[] sizes = { 17, 19, 3, 32, 1, 17 };
            for (int repeat = 0; repeat < 3; ++repeat)
            {
                for (int index = 0; index < sizes.Length; ++index)
                {
                    bool positiveInput = index % 2 == 0;
                    T value = positiveInput ? positive : negative;
                    List<T> values = new List<T>(sizes[index]);
                    for (int item = 0; item < sizes[index]; ++item)
                    {
                        values.Add(value);
                    }
                    double actual = MeanOfList(values);
                    Assert.That(actual, Is.EqualTo(positiveInput ? 11.0 : -3.0));
                    Assert.That(values.Count, Is.EqualTo(sizes[index]));
                    foreach (T item in values)
                    {
                        Assert.That(item, Is.EqualTo(value));
                    }
                }
            }
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

        private static IEnumerable<TestCaseData> ChiSquareReferenceCases()
        {
            yield return new TestCaseData(1.0, 1, 0.31731050786291115).SetName(
                "ChiSquare.OneDegree.NormalSquare"
            );
            yield return new TestCaseData(3.841458820694124, 1, 0.05).SetName(
                "ChiSquare.OneDegree.CriticalValue"
            );
            yield return new TestCaseData(100.0, 1, 1.5239706048320995e-23).SetName(
                "ChiSquare.OneDegree.SmallTail"
            );
            yield return new TestCaseData(1000.0, 1, 1.7958327848007363e-219).SetName(
                "ChiSquare.OneDegree.ExtremeRepresentableTail"
            );
            yield return new TestCaseData(1000.0, 2, 7.124576406741474e-218).SetName(
                "ChiSquare.TwoDegrees.ExponentialTail"
            );
            yield return new TestCaseData(10.0, 3, 0.01856613546304325).SetName(
                "ChiSquare.ThreeDegrees"
            );
            yield return new TestCaseData(10.0, 5, 0.07523524614651217).SetName(
                "ChiSquare.FiveDegrees"
            );
            yield return new TestCaseData(32.0, 32, 0.4667448913877211).SetName(
                "ChiSquare.StirlingBoundary.Center"
            );
            yield return new TestCaseData(33.9, 32, 0.375997951578569).SetName(
                "ChiSquare.SeriesBoundary.Below"
            );
            yield return new TestCaseData(34.0, 32, 0.3714536560753673).SetName(
                "ChiSquare.SeriesBoundary.At"
            );
            yield return new TestCaseData(10000.0, 10000, 0.49811936596618267).SetName(
                "ChiSquare.TenThousandDegrees.Center"
            );
            yield return new TestCaseData(1000000.0, 1000000, 0.4998119368033945).SetName(
                "ChiSquare.MillionDegrees.Center"
            );
            yield return new TestCaseData(1004000.0, 1000000, 0.002363028238683893).SetName(
                "ChiSquare.MillionDegrees.UpperTail"
            );
        }

        private static IEnumerable<TestCaseData> MantelHaenszelReferenceCases()
        {
            yield return new TestCaseData(
                new (int, int, int, int)[] { (1, 9, 11, 3) },
                1.0 / 33.0
            ).SetName("MantelHaenszel.SingleStratum");
            yield return new TestCaseData(
                new (int, int, int, int)[] { (1, 9, 11, 3), (8, 2, 4, 6) },
                101.0 / 181.0
            ).SetName("MantelHaenszel.StratifiedEstimate");
            yield return new TestCaseData(
                new (int, int, int, int)[] { (10, 5, 8, 20), (3, 15, 12, 4), (1, 1, 1, 1) },
                15363.0 / 18931.0
            ).SetName("MantelHaenszel.ThreeUnequalStrata");
            yield return new TestCaseData(
                new (int, int, int, int)[] { (0, 0, 0, 0), (1, 9, 11, 3) },
                1.0 / 33.0
            ).SetName("MantelHaenszel.EmptyStratumIgnored");
            yield return new TestCaseData(
                new (int, int, int, int)[] { (1, 0, 0, 1), (0, 1, 1, 0) },
                1.0
            ).SetName("MantelHaenszel.ComplementaryZeroCells");
            yield return new TestCaseData(new (int, int, int, int)[] { (0, 1, 1, 1) }, 0.0).SetName(
                "MantelHaenszel.ZeroEstimate"
            );
            yield return new TestCaseData(
                new (int, int, int, int)[] { (1, 0, 1, 1) },
                double.PositiveInfinity
            ).SetName("MantelHaenszel.InfiniteEstimate");
            yield return new TestCaseData(
                new (int, int, int, int)[]
                {
                    (int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue),
                },
                1.0
            ).SetName("MantelHaenszel.MaximumCells.TotalDoesNotOverflow");
            yield return new TestCaseData(
                new (int, int, int, int)[] { (int.MaxValue, 1, 1, int.MaxValue) },
                4611686014132420609.0
            ).SetName("MantelHaenszel.MaximumCrossProduct.DoesNotOverflow");
        }

        private static IEnumerable<TestCaseData> InvalidMantelHaenszelCases()
        {
            yield return new TestCaseData((object)null).SetName("MantelHaenszel.Null.Fails");
            yield return new TestCaseData(Array.Empty<(int, int, int, int)>()).SetName(
                "MantelHaenszel.Empty.Fails"
            );
            yield return new TestCaseData(new (int, int, int, int)[] { (0, 0, 0, 0) }).SetName(
                "MantelHaenszel.AllEmpty.Fails"
            );
            yield return new TestCaseData(new (int, int, int, int)[] { (1, 1, 0, 0) }).SetName(
                "MantelHaenszel.UndefinedRatio.Fails"
            );
            yield return new TestCaseData(
                new (int, int, int, int)[] { (1, 1, 1, 1), (-1, 1, 1, 1) }
            ).SetName("MantelHaenszel.NegativeUpperLeft.Fails");
            yield return new TestCaseData(new (int, int, int, int)[] { (1, -1, 1, 1) }).SetName(
                "MantelHaenszel.NegativeUpperRight.Fails"
            );
            yield return new TestCaseData(new (int, int, int, int)[] { (1, 1, -1, 1) }).SetName(
                "MantelHaenszel.NegativeLowerLeft.Fails"
            );
            yield return new TestCaseData(new (int, int, int, int)[] { (1, 1, 1, -1) }).SetName(
                "MantelHaenszel.NegativeLowerRight.Fails"
            );
        }

        [TestCaseSource(nameof(ChiSquareReferenceCases))]
        public void ChiSquareSurvivalMatchesIndependentReference(
            double statistic,
            int degreesOfFreedom,
            double expected
        )
        {
            Assert.IsTrue(
                WallMath.TryChiSquareSurvival(statistic, degreesOfFreedom, out double probability)
            );
            Assert.AreEqual(expected, probability, expected * 2e-12);
        }

        [TestCase(
            2147155967.0000763,
            0.99999971371120872473,
            TestName = "ChiSquare.MaximumDegrees.LowerTailQuadrature"
        )]
        [TestCase(
            2147483647.0,
            0.49999594174926252866,
            TestName = "ChiSquare.MaximumDegrees.CenterQuadrature"
        )]
        [TestCase(
            2147811326.9999237,
            0.00000028701472863699552631,
            TestName = "ChiSquare.MaximumDegrees.UpperTailQuadrature"
        )]
        public void ChiSquareMaximumDegreesMatchesHighPrecisionDensityQuadrature(
            double statistic,
            double expected
        )
        {
            Assert.IsTrue(
                WallMath.TryChiSquareSurvival(statistic, int.MaxValue, out double probability)
            );
            Assert.AreEqual(expected, probability, expected * 2e-12);
        }

        [TestCase(-1.0, 1, TestName = "ChiSquare.NegativeStatistic.Fails")]
        [TestCase(double.NegativeInfinity, 1, TestName = "ChiSquare.NegativeInfinity.Fails")]
        [TestCase(double.NaN, 1, TestName = "ChiSquare.NaN.Fails")]
        [TestCase(1.0, 0, TestName = "ChiSquare.ZeroDegrees.Fails")]
        [TestCase(1.0, -1, TestName = "ChiSquare.NegativeDegrees.Fails")]
        public void ChiSquareInvalidInputClearsOutput(double statistic, int degreesOfFreedom)
        {
            Assert.IsFalse(
                WallMath.TryChiSquareSurvival(statistic, degreesOfFreedom, out double probability)
            );
            Assert.AreEqual(0.0, probability);
        }

        [TestCase(0.0, 1, 1.0, TestName = "ChiSquare.ZeroStatistic.One")]
        [TestCase(double.Epsilon, 1, 1.0, TestName = "ChiSquare.SubnormalStatistic.One")]
        [TestCase(0.0, int.MaxValue, 1.0, TestName = "ChiSquare.ZeroStatistic.MaximumDegrees")]
        [TestCase(double.PositiveInfinity, 1, 0.0, TestName = "ChiSquare.PositiveInfinity.Zero")]
        [TestCase(
            double.MaxValue,
            int.MaxValue,
            0.0,
            TestName = "ChiSquare.MaximumFiniteStatistic.Underflows"
        )]
        [TestCase(2000.0, 2, 0.0, TestName = "ChiSquare.UnrepresentableTail.Underflows")]
        public void ChiSquareBoundaryValuesReturnExpectedProbability(
            double statistic,
            int degreesOfFreedom,
            double expected
        )
        {
            Assert.IsTrue(
                WallMath.TryChiSquareSurvival(statistic, degreesOfFreedom, out double probability)
            );
            Assert.AreEqual(expected, probability);
        }

        [Test]
        public void ChiSquareEvenDegreesMatchesFiniteExponentialSum()
        {
            double[] statistics = { 0.0, 0.1, 1.0, 10.0, 30.0, 100.0, 500.0 };
            for (int degreesOfFreedom = 2; degreesOfFreedom <= 40; degreesOfFreedom += 2)
            {
                foreach (double statistic in statistics)
                {
                    double term = 1.0;
                    double sum = term;
                    for (int order = 1; order < degreesOfFreedom / 2; ++order)
                    {
                        term *= statistic / (2.0 * order);
                        sum += term;
                    }

                    double expected = Math.Exp(-statistic / 2.0) * sum;
                    Assert.IsTrue(
                        WallMath.TryChiSquareSurvival(
                            statistic,
                            degreesOfFreedom,
                            out double probability
                        )
                    );
                    Assert.AreEqual(
                        expected,
                        probability,
                        expected * 2e-12,
                        $"Statistic {statistic}, degrees {degreesOfFreedom}."
                    );
                }
            }
        }

        [TestCaseSource(nameof(MantelHaenszelReferenceCases))]
        public void MantelHaenszelMatchesRationalReference(
            (int, int, int, int)[] strata,
            double expected
        )
        {
            (int, int, int, int)[] original = ((int, int, int, int)[])strata.Clone();
            Assert.IsTrue(WallMath.TryMantelHaenszelOddsRatio(strata, out double oddsRatio));
            if (double.IsPositiveInfinity(expected))
            {
                Assert.AreEqual(expected, oddsRatio);
            }
            else
            {
                Assert.AreEqual(expected, oddsRatio, Math.Max(1e-15, expected * 1e-14));
            }

            CollectionAssert.AreEqual(original, strata);
        }

        [TestCaseSource(nameof(InvalidMantelHaenszelCases))]
        public void MantelHaenszelInvalidInputClearsOutput((int, int, int, int)[] strata)
        {
            Assert.IsFalse(WallMath.TryMantelHaenszelOddsRatio(strata, out double oddsRatio));
            Assert.AreEqual(0.0, oddsRatio);
        }

        [Test]
        public void MantelHaenszelIsInvariantToStratumOrderAndReciprocalUnderColumnSwap()
        {
            (int, int, int, int)[] strata = { (10, 5, 8, 20), (3, 15, 12, 4), (1, 1, 1, 1) };
            (int, int, int, int)[] swapped = { (5, 10, 20, 8), (15, 3, 4, 12), (1, 1, 1, 1) };
            Assert.IsTrue(WallMath.TryMantelHaenszelOddsRatio(strata, out double oddsRatio));
            Assert.IsTrue(WallMath.TryMantelHaenszelOddsRatio(swapped, out double reciprocal));
            Assert.AreEqual(1.0, oddsRatio * reciprocal, 1e-14);
            Array.Reverse(strata);
            Assert.IsTrue(WallMath.TryMantelHaenszelOddsRatio(strata, out double reversed));
            Assert.AreEqual(oddsRatio, reversed, 1e-14);
        }

        [Test]
        public void MantelHaenszelHandlesTenThousandStrataInReusableList()
        {
            List<(int, int, int, int)> strata = new List<(int, int, int, int)>(10000);
            for (int index = 0; index < 5000; ++index)
            {
                strata.Add((1, 9, 11, 3));
                strata.Add((8, 2, 4, 6));
            }

            Assert.IsTrue(WallMath.TryMantelHaenszelOddsRatio(strata, out double oddsRatio));
            Assert.AreEqual(101.0 / 181.0, oddsRatio, 1e-14);
            Assert.AreEqual(10000, strata.Count);
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

        [TestCaseSource(nameof(PercentileExtremeCases))]
        public void PercentileHandlesExtremeMagnitudesAndInfiniteBounds(
            double[] values,
            double percentile,
            double expected
        )
        {
            double[] original = (double[])values.Clone();
            double result = values.Percentile(percentile);
            if (double.IsNaN(expected))
            {
                Assert.IsTrue(double.IsNaN(result));
            }
            else if (double.IsInfinity(expected) || Math.Abs(expected) < 1e-300)
            {
                Assert.AreEqual(expected, result);
            }
            else
            {
                Assert.AreEqual(expected, result, Math.Abs(expected) * 1e-14);
            }

            CollectionAssert.AreEqual(original, values);
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

        [TestCaseSource(nameof(MeanListScratchCases))]
        public void MeanListScratchDoesNotAcquireADisposalLease(IList values, double expected)
        {
            DisposalLease probe = DisposalLeases.Acquire();
            try
            {
                int slot = probe.SlotForTests;
                bool released = probe.TryClaim();
                long before = DisposalLeases.CurrentGeneration(slot);
                /*
                 * A balanced acquire reuses this thread's free-list head; assertions follow the
                 * snapshot so test framework work cannot affect the observed generation.
                 */
                double actual = MeanOfList(values);
                long after = DisposalLeases.CurrentGeneration(slot);

                Assert.That(released, Is.True);
                Assert.That(actual, Is.EqualTo(expected));
                Assert.That(
                    after,
                    Is.EqualTo(before),
                    "A private numeric scratch buffer acquired and claimed a disposal lease."
                );
            }
            finally
            {
                probe.TryClaim();
            }
        }

        [Test]
        public void MeanListScratchGenerationObservationDetectsBalancedLeaseWork()
        {
            DisposalLease probe = DisposalLeases.Acquire();
            DisposalLease balanced = default;
            try
            {
                int slot = probe.SlotForTests;
                bool released = probe.TryClaim();
                long before = DisposalLeases.CurrentGeneration(slot);
                balanced = DisposalLeases.Acquire();
                int reusedSlot = balanced.SlotForTests;
                bool balancedReleased = balanced.TryClaim();
                long after = DisposalLeases.CurrentGeneration(slot);

                Assert.That(released, Is.True);
                Assert.That(balancedReleased, Is.True);
                Assert.That(reusedSlot, Is.EqualTo(slot));
                Assert.That(
                    after,
                    Is.EqualTo(before + 2),
                    "The observation must detect both acquire and claim generation advances."
                );
            }
            finally
            {
                balanced.TryClaim();
                probe.TryClaim();
            }
        }

        [TestCaseSource(nameof(MeanListNumericCases))]
        public void MeanListNumericBehaviorAndSourceContentsArePreserved(
            IList values,
            double expected
        )
        {
            object[] snapshot = new object[values.Count];
            values.CopyTo(snapshot, 0);
            for (int repeat = 0; repeat < 3; ++repeat)
            {
                double actual = MeanOfList(values);
                if (double.IsNaN(expected))
                {
                    Assert.That(double.IsNaN(actual), Is.True);
                }
                else
                {
                    Assert.That(actual, Is.EqualTo(expected));
                }
                CollectionAssert.AreEqual(snapshot, values);
            }
        }

        [TestCase(typeof(float), TestName = "Mean.ListScratch.Float.AlternatingSizes")]
        [TestCase(typeof(double), TestName = "Mean.ListScratch.Double.AlternatingSizes")]
        [TestCase(typeof(int), TestName = "Mean.ListScratch.Int.AlternatingSizes")]
        [TestCase(typeof(long), TestName = "Mean.ListScratch.Long.AlternatingSizes")]
        public void MeanListScratchHandlesRepeatedGrowingAndShrinkingInputs(Type elementType)
        {
            if (elementType == typeof(float))
            {
                AssertMeanListScratchSizes(11f, -3f);
            }
            else if (elementType == typeof(double))
            {
                AssertMeanListScratchSizes(11.0, -3.0);
            }
            else if (elementType == typeof(int))
            {
                AssertMeanListScratchSizes(11, -3);
            }
            else
            {
                AssertMeanListScratchSizes(11L, -3L);
            }
        }

        [TestCase(typeof(float), TestName = "Mean.ListValidation.Float.NullAndEmpty")]
        [TestCase(typeof(double), TestName = "Mean.ListValidation.Double.NullAndEmpty")]
        [TestCase(typeof(int), TestName = "Mean.ListValidation.Int.NullAndEmpty")]
        [TestCase(typeof(long), TestName = "Mean.ListValidation.Long.NullAndEmpty")]
        public void MeanListRejectsNullAndEmptyBeforeRenting(Type elementType)
        {
            if (elementType == typeof(float))
            {
                Assert.Throws<ArgumentNullException>(() => ((List<float>)null).Mean());
                Assert.Throws<ArgumentException>(() => new List<float>().Mean());
            }
            else if (elementType == typeof(double))
            {
                Assert.Throws<ArgumentNullException>(() => ((List<double>)null).Mean());
                Assert.Throws<ArgumentException>(() => new List<double>().Mean());
            }
            else if (elementType == typeof(int))
            {
                Assert.Throws<ArgumentNullException>(() => ((List<int>)null).Mean());
                Assert.Throws<ArgumentException>(() => new List<int>().Mean());
            }
            else
            {
                Assert.Throws<ArgumentNullException>(() => ((List<long>)null).Mean());
                Assert.Throws<ArgumentException>(() => new List<long>().Mean());
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
