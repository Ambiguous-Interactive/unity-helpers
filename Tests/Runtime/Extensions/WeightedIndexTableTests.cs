// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Random
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestDoubles;

    [TestFixture]
    [Category("Fast")]
    public sealed class WeightedIndexTableTests
    {
        private object _retainedAllocation;

        [TestCase(1)]
        [TestCase(16)]
        [TestCase(17)]
        [TestCase(1024)]
        [TestCase(10001)]
        public void UniformWeightsMatchIndependentIndexOracle(int count)
        {
            double[] weights = new double[count];
            Array.Fill(weights, 1d);
            Assert.That(
                WeightedIndexTable.TryCreate(weights, out WeightedIndexTable table),
                Is.True
            );
            Assert.That(table.Count, Is.EqualTo(count));
            PcgRandom random = new(123u);
            IRandom control = random.Copy();
            for (int draw = 0; draw < 100; ++draw)
            {
                int expected = (int)(control.NextDouble() * count);
                Assert.That(table.Next(random), Is.EqualTo(expected));
            }
        }

        [TestCase(4)]
        [TestCase(17)]
        [TestCase(10001)]
        public void DisabledSlotsNeverWinAtBoundaries(int count)
        {
            double[] weights = new double[count];
            weights[1] = 1d;
            weights[count - 2] = 3d;
            Assert.That(
                WeightedIndexTable.TryCreate(weights, out WeightedIndexTable table),
                Is.True
            );
            EdgeCaseRandom random = new(
                doubleSequence: new[] { 0d, 0.249d, 0.25d, 0.9999999999999999d },
                maxDoubleCalls: 4
            );
            Assert.That(table.Next(random), Is.EqualTo(1));
            Assert.That(table.Next(random), Is.EqualTo(1));
            Assert.That(table.Next(random), Is.EqualTo(count - 2));
            Assert.That(table.Next(random), Is.EqualTo(count - 2));
            Assert.Throws<InvalidOperationException>(() => random.NextDouble());
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void NonfiniteWeightsAreRefused(double weight)
        {
            double[] weights = { 1d, weight };
            Assert.That(
                WeightedIndexTable.TryCreate(weights, out WeightedIndexTable table),
                Is.False
            );
            Assert.That(table, Is.Null);
        }

        [Test]
        public void EmptyAndEntirelyDisabledWeightsAreRefused()
        {
            Assert.That(
                WeightedIndexTable.TryCreate(
                    ReadOnlySpan<double>.Empty,
                    out WeightedIndexTable empty
                ),
                Is.False
            );
            Assert.That(empty, Is.Null);
            double[] weights = { 0d, -1d, -double.MaxValue };
            Assert.That(
                WeightedIndexTable.TryCreate(weights, out WeightedIndexTable disabled),
                Is.False
            );
            Assert.That(disabled, Is.Null);
        }

        [TestCase(double.MaxValue)]
        [TestCase(double.Epsilon)]
        public void ExtremeFiniteWeightsDoNotOverflowOrUnderflowTheTotal(double weight)
        {
            double[] weights = { weight, weight };
            Assert.That(
                WeightedIndexTable.TryCreate(weights, out WeightedIndexTable table),
                Is.True
            );
            EdgeCaseRandom random = new(doubleSequence: new[] { 0d, 0.5d, 0.9999999999999999d });
            Assert.That(table.Next(random), Is.EqualTo(0));
            Assert.That(table.Next(random), Is.EqualTo(1));
            Assert.That(table.Next(random), Is.EqualTo(1));
        }

        [Test]
        public void CreationOwnsItsWeights()
        {
            double[] weights = { 0d, 1d, 0d };
            Assert.That(
                WeightedIndexTable.TryCreate(weights, out WeightedIndexTable table),
                Is.True
            );
            Array.Fill(weights, double.NaN);
            EdgeCaseRandom random = new(doubleFallback: 0.5d);
            Assert.That(table.Next(random), Is.EqualTo(1));
            Assert.That(table.NextByRace(random), Is.EqualTo(1));
        }

        [TestCase(-1d)]
        [TestCase(1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void InvalidUniformAndNullGeneratorReturnFailure(double uniform)
        {
            double[] weights = { 1d, 1d };
            Assert.That(
                WeightedIndexTable.TryCreate(weights, out WeightedIndexTable table),
                Is.True
            );
            EdgeCaseRandom random = new(doubleFallback: uniform);
            Assert.That(table.Next(random), Is.EqualTo(-1));
            Assert.That(table.NextByRace(random), Is.EqualTo(-1));
            Assert.That(table.Next(null), Is.EqualTo(-1));
            Assert.That(table.NextByRace(null), Is.EqualTo(-1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RaceMatchesExistingSamplerAndConsumesDisabledSlotDraws(bool extreme)
        {
            double[] weights = extreme
                ? new[] { 0d, 1d, -3d, double.Epsilon, double.MaxValue, 8d }
                : new[] { 0d, 1d, -3d, 4d, 9d, 8d };
            Assert.That(
                WeightedIndexTable.TryCreate(weights, out WeightedIndexTable table),
                Is.True
            );
            PcgRandom random = new(456u);
            IRandom control = random.Copy();
            for (int draw = 0; draw < 100; ++draw)
            {
                Assert.That(control.TryNextWeightedIndexByRace(weights, out int expected), Is.True);
                Assert.That(table.NextByRace(random), Is.EqualTo(expected));
            }
            Assert.That(random.NextDouble(), Is.EqualTo(control.NextDouble()));
        }

        [TestCase(12)]
        [TestCase(64)]
        [TestCase(1024)]
        public void VariedWeightsMatchIndependentIntegerCumulativeOracle(int count)
        {
            float[] weights = new float[count];
            for (int index = 0; index < count; ++index)
            {
                weights[index] = index + 1;
            }
            Assert.IsTrue(WeightedIndexTable.TryCreate(weights, out WeightedIndexTable table));
            PcgRandom random = new(1176u);
            IRandom control = random.Copy();
            int total = count * (count + 1) / 2;
            for (int draw = 0; draw < 256; ++draw)
            {
                double selectedWeight = control.NextDouble() * total;
                int expected = 0;
                int cumulative = 1;
                while (cumulative <= selectedWeight)
                {
                    ++expected;
                    cumulative += expected + 1;
                }
                Assert.That(table.Next(random), Is.EqualTo(expected));
            }
            Array.Fill(weights, float.NaN);
            Assert.That(table.Next(random), Is.InRange(0, count - 1));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(0f)]
        [TestCase(-1f)]
        public void InvalidFloatWeightsAreRefused(float weight)
        {
            float[] weights = { weight };
            Assert.IsFalse(WeightedIndexTable.TryCreate(weights, out WeightedIndexTable table));
            Assert.That(table, Is.Null);
        }

        [Test]
        public void EmptyFloatWeightsAreRefused()
        {
            Assert.IsFalse(
                WeightedIndexTable.TryCreate(
                    ReadOnlySpan<float>.Empty,
                    out WeightedIndexTable table
                )
            );
            Assert.That(table, Is.Null);
        }

        [Test]
        public void RepeatedDrawsHaveNoProfilerAllocationEvents()
        {
            double[] weights = { 0d, 1d, 2d, 3d };
            Assert.IsTrue(WeightedIndexTable.TryCreate(weights, out WeightedIndexTable table));
            PcgRandom random = new(1176u);
            Action allocate = () => _retainedAllocation = new byte[32];
            long control = ProfilerAllocationCounter.Measure(allocate, 32, 128, 4);
            GC.KeepAlive(_retainedAllocation);
            if (control == 0)
            {
                Assert.Ignore(
                    "The allocation recorder did not observe a retained positive control."
                );
            }
            int sum = 0;
            Action sample = () => sum += table.Next(random) + table.NextByRace(random);
            long measured = ProfilerAllocationCounter.Measure(sample, 128, 256, 16);
            Assert.That(measured, Is.Zero);
            Assert.That(sum, Is.InRange(288, 864));
        }

        [Test]
        public void ZeroRaceSamplesChooseFirstPositiveSlot()
        {
            double[] weights = { 0d, -1d, double.Epsilon, double.MaxValue, 0d };
            Assert.That(
                WeightedIndexTable.TryCreate(weights, out WeightedIndexTable table),
                Is.True
            );
            EdgeCaseRandom random = new(doubleFallback: 0d, maxDoubleCalls: weights.Length);
            Assert.That(table.NextByRace(random), Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => random.NextDouble());
        }
    }
}
