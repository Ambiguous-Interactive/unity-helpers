// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.TestDoubles;
    using Math = System.Math;

    [TestFixture]
    public sealed class WallMathDistributionTests
    {
        private static double[] OracleRanks(double[] values)
        {
            double[] ranks = new double[values.Length];
            for (int index = 0; index < values.Length; ++index)
            {
                int lower = 0;
                int equal = 0;
                foreach (double other in values)
                {
                    if (other < values[index])
                    {
                        ++lower;
                    }
                    if (other == values[index])
                    {
                        ++equal;
                    }
                }
                ranks[index] = lower + (equal + 1d) * 0.5d;
            }
            return ranks;
        }

        private static double OracleSpearman(double[] x, double[] y)
        {
            double[] left = OracleRanks(x);
            double[] right = OracleRanks(y);
            double numerator = 0d;
            double leftSquares = 0d;
            double rightSquares = 0d;
            for (int first = 0; first < x.Length; ++first)
            {
                for (int second = first + 1; second < x.Length; ++second)
                {
                    double dx = left[first] - left[second];
                    double dy = right[first] - right[second];
                    numerator += dx * dy;
                    leftSquares += dx * dx;
                    rightSquares += dy * dy;
                }
            }
            return numerator / Math.Sqrt(leftSquares * rightSquares);
        }

        private static double OracleKendall(double[] x, double[] y)
        {
            long numerator = 0;
            long xPairs = 0;
            long yPairs = 0;
            for (int first = 0; first < x.Length; ++first)
            {
                for (int second = first + 1; second < x.Length; ++second)
                {
                    int dx = x[first].CompareTo(x[second]);
                    int dy = y[first].CompareTo(y[second]);
                    numerator += dx * dy;
                    if (dx != 0)
                    {
                        ++xPairs;
                    }
                    if (dy != 0)
                    {
                        ++yPairs;
                    }
                }
            }
            return numerator / Math.Sqrt((double)xPairs * yPairs);
        }

        private static void AssertNumber(double expected, double actual)
        {
            if (double.IsNaN(expected))
            {
                Assert.IsTrue(double.IsNaN(actual));
            }
            else
            {
                Assert.AreEqual(expected, actual, 1e-12);
            }
        }

        [TestCase(-1000d)]
        [TestCase(-1d)]
        [TestCase(0d)]
        [TestCase(1000d)]
        [TestCase(double.MaxValue)]
        public void LogSumExpIsStableUnderLargeOffsets(double offset)
        {
            double result = WallMath.LogSumExp(new[] { offset, offset });
            Assert.AreEqual(offset + Math.Log(2d), result, 1e-12);
        }

        [Test]
        public void LogSumExpHasExplicitEmptyAndInfinityPolicies()
        {
            Assert.AreEqual(
                double.NegativeInfinity,
                WallMath.LogSumExp(ReadOnlySpan<double>.Empty)
            );
            Assert.AreEqual(
                double.NegativeInfinity,
                WallMath.LogSumExp(new[] { double.NegativeInfinity })
            );
            Assert.AreEqual(
                double.PositiveInfinity,
                WallMath.LogSumExp(new[] { double.PositiveInfinity, 1d })
            );
            Assert.IsTrue(
                double.IsNaN(WallMath.LogSumExp(new[] { double.PositiveInfinity, double.NaN }))
            );
            Assert.AreEqual(3d, WallMath.LogSumExp(new[] { double.NegativeInfinity, 3d }));
        }

        [TestCase(0.01d)]
        [TestCase(1d)]
        [TestCase(100d)]
        public void SoftmaxMatchesIndependentRatios(double temperature)
        {
            double[] logits = { -2d, 0d, 1d, 4d };
            double[] probabilities = (double[])logits.Clone();
            Assert.IsTrue(WallMath.SoftmaxInPlace(probabilities, temperature));
            double sum = 0d;
            foreach (double value in probabilities)
            {
                sum += value;
            }
            Assert.AreEqual(1d, sum, 1e-14);
            for (int index = 0; index < logits.Length; ++index)
            {
                Assert.AreEqual(
                    Math.Exp((logits[index] - logits[3]) / temperature),
                    probabilities[index] / probabilities[3],
                    1e-12
                );
            }
        }

        [Test]
        public void NegativeInfinityHasZeroWeightWithNegativeScoresAndTinyTemperature()
        {
            double[] values = { double.NegativeInfinity, -1d, -1d };
            Assert.IsTrue(WallMath.SoftmaxInPlace(values, double.Epsilon));
            CollectionAssert.AreEqual(new[] { 0d, 0.5d, 0.5d }, values);
        }

        [Test]
        public void SoftmaxScalesBeforeAnExtremeDifferenceCanOverflow()
        {
            double[] values = { -double.MaxValue, double.MaxValue };
            Assert.IsTrue(WallMath.SoftmaxInPlace(values, double.MaxValue));
            Assert.AreEqual(1d / (1d + Math.Exp(2d)), values[0], 1e-14);
            Assert.AreEqual(1d - values[0], values[1], 1e-14);
        }

        [Test]
        public void SoftmaxHandlesSubnormalTemperatureAndInfiniteTies()
        {
            double[] values = { -1d, 0d, 0d };
            Assert.IsTrue(WallMath.SoftmaxInPlace(values, double.Epsilon));
            CollectionAssert.AreEqual(new[] { 0d, 0.5d, 0.5d }, values);
            double[] infinite =
            {
                double.PositiveInfinity,
                double.NegativeInfinity,
                4d,
                double.PositiveInfinity,
            };
            Assert.IsTrue(WallMath.SoftmaxInPlace(infinite));
            CollectionAssert.AreEqual(new[] { 0.5d, 0d, 0d, 0.5d }, infinite);
        }

        [TestCase(0d)]
        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void InvalidTemperatureLeavesInputUntouched(double temperature)
        {
            double[] values = { 1d, 2d };
            Assert.IsFalse(WallMath.SoftmaxInPlace(values, temperature));
            CollectionAssert.AreEqual(new[] { 1d, 2d }, values);
        }

        [Test]
        public void InvalidSoftmaxDataFailsWithoutWrites()
        {
            Assert.IsFalse(WallMath.SoftmaxInPlace(Span<double>.Empty));
            double[] nan = { 1d, double.NaN, 2d };
            Assert.IsFalse(WallMath.SoftmaxInPlace(nan));
            CollectionAssert.AreEqual(new[] { 1d, double.NaN, 2d }, nan);
            double[] negativeInfinity = { double.NegativeInfinity, double.NegativeInfinity };
            Assert.IsFalse(WallMath.SoftmaxInPlace(negativeInfinity));
            CollectionAssert.AreEqual(
                new[] { double.NegativeInfinity, double.NegativeInfinity },
                negativeInfinity
            );
        }

        [TestCase(-1000d, 0d, 0d)]
        [TestCase(0d, 0.5d, 0.6931471805599453d)]
        [TestCase(1000d, 1d, 1000d)]
        [TestCase(double.NegativeInfinity, 0d, 0d)]
        [TestCase(double.PositiveInfinity, 1d, double.PositiveInfinity)]
        public void SigmoidAndSoftplusStayStable(double x, double sigmoid, double softplus)
        {
            Assert.AreEqual(sigmoid, WallMath.Sigmoid(x));
            Assert.AreEqual(softplus, WallMath.Softplus(x));
        }

        [Test]
        public void SoftplusRetainsSmallNegativeTailAndPropagatesNaN()
        {
            double expected = Math.Exp(-35d);
            Assert.AreEqual(expected, WallMath.Softplus(-35d), expected * 1e-14);
            Assert.IsTrue(double.IsNaN(WallMath.Sigmoid(double.NaN)));
            Assert.IsTrue(double.IsNaN(WallMath.Softplus(double.NaN)));
        }

        [TestCase(0d, 1d, 1d)]
        [TestCase(0.25d, 1.75d, 1d)]
        [TestCase(0.5d, 2.5d, 2d)]
        [TestCase(1d, 4d, 4d)]
        public void SortedPercentilesUseTheirStatedRankPolicy(
            double percentile,
            double interpolated,
            double nearest
        )
        {
            double[] values = { 1d, 2d, 3d, 4d };
            Assert.AreEqual(interpolated, WallMath.PercentileOfSorted(values, percentile));
            Assert.AreEqual(nearest, WallMath.PercentileNearestRank(values, percentile));
            Assert.IsTrue(WallMath.TryMedian(values, out double median));
            Assert.AreEqual(2.5d, median);
            CollectionAssert.AreEqual(new[] { 1d, 2d, 3d, 4d }, values);
        }

        [TestCase(-0.1d)]
        [TestCase(1.1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void InvalidPercentileIsNonthrowing(double percentile)
        {
            Assert.IsTrue(double.IsNaN(WallMath.PercentileOfSorted(new[] { 1d }, percentile)));
            Assert.IsTrue(double.IsNaN(WallMath.PercentileNearestRank(new[] { 1d }, percentile)));
        }

        [Test]
        public void SortedStatisticsHandleExtremeValuesAndInvalidData()
        {
            Assert.IsTrue(
                WallMath.TryMedian(new[] { -double.MaxValue, double.MaxValue }, out double zero)
            );
            Assert.AreEqual(0d, zero);
            Assert.IsTrue(
                WallMath.TryMedian(new[] { double.Epsilon, double.Epsilon }, out double tiny)
            );
            Assert.AreEqual(double.Epsilon, tiny);
            Assert.AreEqual(
                double.PositiveInfinity,
                WallMath.PercentileOfSorted(new[] { 1d, double.PositiveInfinity }, 0.5d)
            );
            Assert.IsFalse(
                WallMath.TryMedian(
                    new[] { double.NegativeInfinity, double.PositiveInfinity },
                    out double undefined
                )
            );
            Assert.IsTrue(double.IsNaN(undefined));
            Assert.IsFalse(WallMath.TryMedian(ReadOnlySpan<double>.Empty, out _));
            Assert.IsFalse(WallMath.TryMedian(new[] { 2d, 1d }, out _));
            Assert.IsFalse(WallMath.TryMedian(new[] { 1d, double.NaN }, out _));
        }

        [Test]
        public void RanksPreserveOrderTiesAndDestinationTail()
        {
            double[] values = { 4d, 1d, 1d, 3d };
            double[] ranks = { -1d, -1d, -1d, -1d, 99d };
            Assert.IsTrue(WallMath.AverageRanks(values, ranks));
            CollectionAssert.AreEqual(new[] { 4d, 1.5d, 1.5d, 3d, 99d }, ranks);
            CollectionAssert.AreEqual(new[] { 4d, 1d, 1d, 3d }, values);
            Assert.IsTrue(WallMath.AverageRanks(values, values));
            CollectionAssert.AreEqual(new[] { 4d, 1.5d, 1.5d, 3d }, values);
        }

        [Test]
        public void InvalidRankRequestsLeaveDestinationUntouched()
        {
            double[] output = { 99d, 98d };
            Assert.IsFalse(WallMath.AverageRanks(new[] { 1d, 2d, 3d }, output));
            Assert.IsFalse(WallMath.AverageRanks(new[] { 1d, double.NaN }, output));
            CollectionAssert.AreEqual(new[] { 99d, 98d }, output);
            Assert.IsTrue(WallMath.AverageRanks(ReadOnlySpan<double>.Empty, Span<double>.Empty));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(int.MaxValue)]
        public void CorrelationsAndRanksMatchIndependentPairOracles(int seed)
        {
            System.Random random = new(seed);
            for (int trial = 0; trial < 64; ++trial)
            {
                int count = random.Next(2, 20);
                double[] x = new double[count];
                double[] y = new double[count];
                for (int index = 0; index < count; ++index)
                {
                    x[index] = random.Next(-2, 3);
                    y[index] = random.Next(-2, 3);
                }
                double[] originalX = (double[])x.Clone();
                double[] originalY = (double[])y.Clone();
                double[] ranks = new double[count];
                Assert.IsTrue(WallMath.AverageRanks(x, ranks));
                CollectionAssert.AreEqual(OracleRanks(x), ranks);
                AssertNumber(OracleSpearman(x, y), WallMath.Spearman(x, y));
                AssertNumber(OracleKendall(x, y), WallMath.KendallTauB(x, y));
                CollectionAssert.AreEqual(originalX, x);
                CollectionAssert.AreEqual(originalY, y);
            }
        }

        [Test]
        public void CorrelationsRejectUndefinedInputsAndAcceptInfiniteRanks()
        {
            double[][] invalid = { Array.Empty<double>(), new[] { 1d }, new[] { 1d, double.NaN } };
            foreach (double[] values in invalid)
            {
                Assert.IsTrue(double.IsNaN(WallMath.Spearman(values, values)));
                Assert.IsTrue(double.IsNaN(WallMath.KendallTauB(values, values)));
            }
            double[] constant = { 2d, 2d, 2d };
            double[] ordered = { 1d, 2d, 3d };
            Assert.IsTrue(double.IsNaN(WallMath.Spearman(constant, ordered)));
            Assert.IsTrue(double.IsNaN(WallMath.KendallTauB(constant, ordered)));
            Assert.IsTrue(double.IsNaN(WallMath.Spearman(constant, ordered.AsSpan(0, 2))));
            Assert.IsTrue(double.IsNaN(WallMath.KendallTauB(constant, ordered.AsSpan(0, 2))));
            double[] infinities = { double.NegativeInfinity, 0d, double.PositiveInfinity };
            Assert.AreEqual(1d, WallMath.Spearman(infinities, ordered));
            Assert.AreEqual(-1d, WallMath.KendallTauB(infinities, new[] { 3d, 2d, 1d }));
        }

        [TestCase(0d, 0)]
        [TestCase(0.499d, 0)]
        [TestCase(0.5d, 1)]
        [TestCase(0.999d, 1)]
        public void SoftmaxSamplingConsumesExactlyOneDrawAndPreservesScratchTail(
            double draw,
            int expected
        )
        {
            EdgeCaseRandom random = new(doubleSequence: new[] { draw, 0.123d });
            double[] logits = { 1000d, 1000d };
            double[] scratch = { -1d, -1d, 99d };
            Assert.IsTrue(random.TryNextSoftmaxIndex(logits, scratch, out int index));
            Assert.AreEqual(expected, index);
            Assert.AreEqual(0.123d, random.NextDouble());
            CollectionAssert.AreEqual(new[] { 0.5d, 0.5d, 99d }, scratch);
            CollectionAssert.AreEqual(new[] { 1000d, 1000d }, logits);
        }

        [Test]
        public void InvalidSoftmaxSamplingConsumesNoDraw()
        {
            EdgeCaseRandom random = new(maxDoubleCalls: 0);
            Assert.IsFalse(
                random.TryNextSoftmaxIndex(new[] { double.NaN }, new double[1], out int index)
            );
            Assert.AreEqual(0, index);
            Assert.IsFalse(random.TryNextSoftmaxIndex(new[] { 1d }, Span<double>.Empty, out _));
            Assert.IsFalse(
                random.TryNextSoftmaxIndex(ReadOnlySpan<double>.Empty, Span<double>.Empty, out _)
            );
            Assert.IsFalse(random.TryNextSoftmaxIndex(new[] { 1d }, new double[1], out _, 0d));
            Assert.IsFalse(((IRandom)null).TryNextSoftmaxIndex(new[] { 1d }, new double[1], out _));
        }

        [Test]
        public void LargeOrderedCorrelationRetainsExactResults()
        {
            double[] values = new double[10001];
            for (int index = 0; index < values.Length; ++index)
            {
                values[index] = index;
            }
            Assert.AreEqual(1d, WallMath.Spearman(values, values));
            Assert.AreEqual(1d, WallMath.KendallTauB(values, values));
            Assert.IsTrue(WallMath.TryMedian(values, out double median));
            Assert.AreEqual(5000d, median);
        }
    }
}
