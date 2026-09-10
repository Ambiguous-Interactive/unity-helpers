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
