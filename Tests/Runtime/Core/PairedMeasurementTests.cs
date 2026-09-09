// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Text;
    using System.Text.Json;
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
        private static CalibratedBenchmarkMeasurement CreateSamples(
            double[] reference,
            double[] subject
        )
        {
            return new CalibratedBenchmarkMeasurement(reference, subject, 1, 12345, 100, 100, 3, 3);
        }

        [TestCase(0.90, true, true)]
        [TestCase(0.96, false, true)]
        [TestCase(1.04, false, true)]
        [TestCase(1.06, false, false)]
        public void CalibratedTimingRulesSeparateImprovementAndNonInferiority(
            double subjectDurationRatio,
            bool improvement,
            bool nonInferiority
        )
        {
            double[] reference = new double[32];
            double[] subject = new double[32];
            for (int index = 0; index < reference.Length; index++)
            {
                reference[index] = 100;
                subject[index] = 100 * subjectDurationRatio;
            }
            CalibratedBenchmarkMeasurement measurement = CreateSamples(reference, subject);
            Assert.AreEqual(improvement, measurement.HasTimingImprovement);
            Assert.AreEqual(nonInferiority, measurement.HasTimingNonInferiority);
            Assert.AreEqual(1 / subjectDurationRatio, measurement.Comparison.Ratio, 1e-12);
            Assert.AreEqual(measurement.Comparison.Ratio, measurement.RatioLower95, 1e-12);
            Assert.AreEqual(measurement.Comparison.Ratio, measurement.RatioUpper95, 1e-12);
        }

        [Test]
        public void CalibratedRawSamplesAreImmutableAndDistributionStatisticsAreKnown()
        {
            double[] reference = new double[32];
            double[] subject = new double[32];
            for (int index = 0; index < reference.Length; index++)
            {
                reference[index] = 100 + index;
                subject[index] = reference[index] / 2;
            }
            CalibratedBenchmarkMeasurement measurement = CreateSamples(reference, subject);
            reference[0] = 999;
            subject[0] = 999;
            Assert.AreEqual(100, measurement.ReferenceMilliseconds[0]);
            Assert.AreEqual(50, measurement.SubjectMilliseconds[0]);
            Assert.AreEqual(115.5, measurement.ReferenceSummary.Median);
            Assert.AreEqual(129.45, measurement.ReferenceSummary.P95, 1e-9);
            Assert.AreEqual(8, measurement.ReferenceSummary.MedianAbsoluteDeviation);
            Assert.AreEqual(Math.Log(2), measurement.PairedLogRatios[0], 1e-12);
            Assert.IsFalse(
                measurement.HasTimingImprovement,
                "An unstable control is inconclusive even at twice the throughput."
            );
        }

        [Test]
        public void PairedBootstrapIsReplayableAndIncludesBatchVariation()
        {
            double[] reference = new double[32];
            double[] subject = new double[32];
            for (int index = 0; index < reference.Length; index++)
            {
                reference[index] = 100;
                subject[index] = index < 16 ? 50 : 200;
            }
            CalibratedBenchmarkMeasurement first = CreateSamples(reference, subject);
            CalibratedBenchmarkMeasurement replay = CreateSamples(reference, subject);
            Assert.AreEqual(1, first.Comparison.Ratio, 1e-12);
            Assert.Less(first.RatioLower95, 0.8);
            Assert.Less(1.2, first.RatioUpper95);
            Assert.AreEqual(first.RatioLower95, replay.RatioLower95);
            Assert.AreEqual(first.RatioUpper95, replay.RatioUpper95);
            Assert.IsFalse(first.HasTimingImprovement);
        }

        [TestCase(4, 100)]
        [TestCase(31, 100)]
        [TestCase(32, 1)]
        public void TooFewOrTooShortSamplesCannotEstablishTimingAcceptance(
            int count,
            double milliseconds
        )
        {
            double[] reference = new double[count];
            double[] subject = new double[count];
            for (int index = 0; index < count; index++)
            {
                reference[index] = milliseconds;
                subject[index] = milliseconds / 2;
            }
            CalibratedBenchmarkMeasurement measurement = CreateSamples(reference, subject);
            Assert.IsFalse(measurement.HasSufficientTiming);
            Assert.IsFalse(measurement.HasTimingImprovement);
            Assert.IsFalse(measurement.HasTimingNonInferiority);
        }

        [TestCase(32, JsonValueKind.Number)]
        [TestCase(31, JsonValueKind.Null)]
        public void RawEvidenceJsonRetainsSamplesAndMarksIncompleteIntervals(
            int count,
            JsonValueKind expectedInterval
        )
        {
            double[] reference = new double[count];
            double[] subject = new double[count];
            for (int index = 0; index < count; index++)
            {
                reference[index] = 100;
                subject[index] = 90;
            }
            CalibratedBenchmarkMeasurement measurement = CreateSamples(reference, subject);
            using JsonDocument document = JsonDocument.Parse(measurement.ToJson());
            JsonElement root = document.RootElement;
            Assert.AreEqual(
                count,
                root.GetProperty(nameof(measurement.ReferenceMilliseconds)).GetArrayLength()
            );
            Assert.AreEqual(
                90,
                root.GetProperty(nameof(measurement.SubjectMilliseconds))[0].GetDouble()
            );
            Assert.AreEqual(
                100,
                root.GetProperty(nameof(measurement.ReferenceSummary))
                    .GetProperty(nameof(measurement.ReferenceSummary.Median))
                    .GetDouble()
            );
            Assert.AreEqual(
                expectedInterval,
                root.GetProperty(nameof(measurement.RatioLower95)).ValueKind
            );
            Assert.AreEqual(
                expectedInterval,
                root.GetProperty(nameof(measurement.RatioUpper95)).ValueKind
            );
            Assert.AreEqual(
                measurement.HasSufficientTiming,
                root.GetProperty(nameof(measurement.HasSufficientTiming)).GetBoolean()
            );
            Assert.AreEqual(
                measurement.Seed,
                root.GetProperty(nameof(measurement.Seed)).GetInt32()
            );
            Assert.IsNotEmpty(
                root.GetProperty(nameof(measurement.EnvironmentMetadata))
                    .GetProperty("allocationMetric")
                    .GetString()
            );
        }

        [Test]
        public void CalibratedMeasurementRequiresCorrectnessControl()
        {
            Assert.IsTrue(
                BenchmarkProtocol.MeasureCalibrated(null, iterations => iterations, () => { }, 1)
                    == null
            );
            Assert.IsTrue(
                BenchmarkProtocol.MeasureCalibrated(iterations => iterations, null, () => { }, 1)
                    == null
            );
            Assert.IsTrue(
                BenchmarkProtocol.MeasureCalibrated(
                    iterations => iterations,
                    iterations => iterations,
                    null,
                    1
                ) == null
            );
            Assert.Throws<InvalidOperationException>(() =>
                BenchmarkProtocol.MeasureCalibrated(
                    iterations =>
                        throw new NotSupportedException(
                            "Timing must never run before its correctness control."
                        ),
                    iterations => iterations,
                    () => throw new InvalidOperationException(),
                    1
                )
            );
        }

        [Test]
        public void ExtremeRatiosAndBatchCountsCannotBecomeUsableMeasurements()
        {
            Assert.IsFalse(
                BenchmarkProtocol.MeasurePaired(() => 1, () => 1, int.MaxValue).IsUsable
            );
            Assert.IsFalse(
                BenchmarkProtocol
                    .Combine(new[] { double.Epsilon }, new[] { double.MaxValue })
                    .IsUsable
            );
            Assert.IsFalse(
                BenchmarkProtocol
                    .Combine(new[] { double.MaxValue }, new[] { double.Epsilon })
                    .IsUsable
            );
            Assert.AreEqual(
                2,
                BenchmarkProtocol.Combine(new[] { 1e-300 }, new[] { 2e-300 }).Ratio,
                1e-12
            );
        }

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
            // A 4% slowdown per slot models the drift that biases sequential benchmarks.
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

            // Measuring all reference slots first exposes drift bias.
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
            /*
                The control must prove collection counters work before an unchanged counter can count as a
                subject result.
            */
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
