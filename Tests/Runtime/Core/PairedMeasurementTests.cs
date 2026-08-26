// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Text;
    using NUnit.Framework;

    /// <summary>
    /// The protocol's whole claim is that a machine drifting during a measurement cannot be
    /// mistaken for one side being faster, so the test that matters drives a drifting machine and
    /// shows the naive shape getting it wrong and this one getting it right (#573).
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class PairedMeasurementTests
    {
        [Test]
        public void BatchOrderIsCounterbalanced()
        {
            Assert.AreEqual("ABBABAAB", BenchmarkProtocol.BatchOrder());
        }

        [Test]
        public void BothSidesOccupyTheSameMeanPositionInABatch()
        {
            string order = BenchmarkProtocol.BatchOrder();
            double reference = 0;
            double subject = 0;
            int referenceCount = 0;
            int subjectCount = 0;
            for (int index = 0; index < order.Length; index++)
            {
                if (order[index] == 'A')
                {
                    reference += index;
                    referenceCount++;
                }
                else
                {
                    subject += index;
                    subjectCount++;
                }
            }

            Assert.AreEqual(subjectCount, referenceCount, "each side must get the same slot count");
            Assert.AreEqual(
                reference / referenceCount,
                subject / subjectCount,
                1e-9,
                "unequal mean position leaves a linear drift in the ratio"
            );
        }

        [Test]
        public void SlotsRunInTheDeclaredOrder()
        {
            StringBuilder observed = new();
            BenchmarkProtocol.MeasurePaired(
                () =>
                {
                    observed.Append('A');
                    return 1;
                },
                () =>
                {
                    observed.Append('B');
                    return 1;
                }
            );

            Assert.AreEqual(BenchmarkProtocol.BatchOrder(), observed.ToString());
        }

        [Test]
        public void TwoBatchesRunTheOrderTwice()
        {
            StringBuilder observed = new();
            PairedMeasurement measurement = BenchmarkProtocol.MeasurePaired(
                () =>
                {
                    observed.Append('A');
                    return 1;
                },
                () =>
                {
                    observed.Append('B');
                    return 2;
                },
                batches: 2
            );

            Assert.AreEqual(
                BenchmarkProtocol.BatchOrder() + BenchmarkProtocol.BatchOrder(),
                observed.ToString()
            );
            Assert.AreEqual(2 * BenchmarkProtocol.CyclesPerBatch, measurement.Cycles);
        }

        [Test]
        public void APairedMeasurementRecoversTheTrueRatioThroughADrift()
        {
            // A machine that gets 4% slower every slot: eight slots span a 28% decline, which is
            // the shape a long sequential benchmark actually sees.
            const double trueRatio = 2.0;
            const double decayPerSlot = 0.96;

            double drift = 1.0;
            PairedMeasurement paired = BenchmarkProtocol.MeasurePaired(
                () =>
                {
                    double sample = 100.0 * drift;
                    drift *= decayPerSlot;
                    return sample;
                },
                () =>
                {
                    double sample = 100.0 * trueRatio * drift;
                    drift *= decayPerSlot;
                    return sample;
                }
            );

            Assert.IsTrue(paired.IsUsable);
            Assert.AreEqual(
                trueRatio,
                paired.Ratio,
                0.02,
                "counterbalancing must cancel the drift"
            );

            // The red half: the same machine, measured the way the roster is measured today --
            // every reference slot first, then every subject slot.
            drift = 1.0;
            double referenceTotal = 0;
            double subjectTotal = 0;
            for (int slot = 0; slot < BenchmarkProtocol.CyclesPerBatch; slot++)
            {
                referenceTotal += 100.0 * drift;
                drift *= decayPerSlot;
            }

            for (int slot = 0; slot < BenchmarkProtocol.CyclesPerBatch; slot++)
            {
                subjectTotal += 100.0 * trueRatio * drift;
                drift *= decayPerSlot;
            }

            double sequentialRatio = subjectTotal / referenceTotal;
            Assert.Less(
                sequentialRatio,
                trueRatio - 0.2,
                "the sequential shape should be visibly wrong, or this test proves nothing"
            );
        }

        [Test]
        public void TheRatioIsGeometricSoAnEvenPairReportsOne()
        {
            PairedMeasurement measurement = BenchmarkProtocol.Combine(
                new double[] { 100, 100 },
                new double[] { 200, 50 }
            );

            Assert.AreEqual(1.0, measurement.Ratio, 1e-9);
        }

        [TestCase(new double[] { 100, 100, 100, 100 }, 0.0)]
        [TestCase(new double[] { 100, 103, 101, 102 }, 0.03)]
        [TestCase(new double[] { 50, 100 }, 1.0)]
        public void SpreadIsRelativeToTheSlowestCycle(double[] values, double expected)
        {
            Assert.AreEqual(expected, BenchmarkProtocol.Spread(values), 1e-9);
        }

        [Test]
        public void ASeriesThatCannotBeReadIsNeverReportedAsStable()
        {
            Assert.AreEqual(double.PositiveInfinity, BenchmarkProtocol.Spread(null));
            Assert.AreEqual(double.PositiveInfinity, BenchmarkProtocol.Spread(new double[0]));
            Assert.AreEqual(
                double.PositiveInfinity,
                BenchmarkProtocol.Spread(new double[] { 100, 0 }),
                "a zero reading is not a fast cycle"
            );
        }

        [Test]
        public void AStableMeasurementPublishesAndAnUnstableOneDoesNot()
        {
            PairedMeasurement steady = BenchmarkProtocol.Combine(
                new double[] { 100, 101, 100, 101 },
                new double[] { 200, 202, 200, 202 }
            );
            Assert.IsTrue(steady.IsStable(BenchmarkProtocol.DefaultSpreadLimit));
            Assert.AreEqual(2.0, steady.Ratio, 1e-9);

            PairedMeasurement jittery = BenchmarkProtocol.Combine(
                new double[] { 100, 130, 100, 130 },
                new double[] { 200, 260, 200, 260 }
            );
            Assert.AreEqual(2.0, jittery.Ratio, 1e-9, "the ratio is still right");
            Assert.IsFalse(
                jittery.IsStable(BenchmarkProtocol.DefaultSpreadLimit),
                "a 30% swing in the machine is not a publishable comparison"
            );
        }

        [Test]
        public void EverySlotStartsFromASettledHeap()
        {
            // The control runs FIRST and decides whether this platform can be measured at all: on a
            // runtime whose collection counter does not move, asserting the subject would be the
            // absence of a measurement rather than a pass.
            int collections = GC.CollectionCount(0);
            BenchmarkProtocol.Settle();
            if (GC.CollectionCount(0) == collections)
            {
                Assert.Ignore(
                    "GC.CollectionCount(0) does not move here, so Settle cannot be seen."
                );
            }

            collections = GC.CollectionCount(0);
            BenchmarkProtocol.MeasurePaired(() => 1, () => 1);
            Assert.LessOrEqual(
                collections + 8,
                GC.CollectionCount(0),
                "one settle per slot, eight slots per batch"
            );
        }

        [Test]
        public void AnUnusableMeasurementIsNeverStable()
        {
            Assert.IsFalse(PairedMeasurement.Unusable.IsUsable);
            Assert.IsFalse(PairedMeasurement.Unusable.IsStable(double.MaxValue));
        }

        [Test]
        public void MissingOrNonsensicalInputIsRefusedRatherThanThrown()
        {
            Assert.IsFalse(BenchmarkProtocol.MeasurePaired(null, () => 1).IsUsable);
            Assert.IsFalse(BenchmarkProtocol.MeasurePaired(() => 1, null).IsUsable);
            Assert.IsFalse(BenchmarkProtocol.MeasurePaired(() => 1, () => 1, 0).IsUsable);
            Assert.IsFalse(BenchmarkProtocol.Combine(null, new double[] { 1 }).IsUsable);
            Assert.IsFalse(
                BenchmarkProtocol.Combine(new double[] { 1 }, new double[] { 1, 2 }).IsUsable
            );
            Assert.IsFalse(BenchmarkProtocol.Combine(new double[0], new double[0]).IsUsable);
        }

        [TestCase(0d)]
        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void ASlotThatReportedNoThroughputMakesTheWholeComparisonUnusable(double sample)
        {
            PairedMeasurement measurement = BenchmarkProtocol.Combine(
                new double[] { 100, 100 },
                new double[] { 200, sample }
            );

            Assert.IsFalse(
                measurement.IsUsable,
                "a slot that measured nothing must not average into a published ratio"
            );
        }
    }
}
