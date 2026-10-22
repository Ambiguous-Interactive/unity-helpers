// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;

    [TestFixture]
    [Category("Fast")]
    public sealed class FamilyMeasurementTests
    {
        private static CalibratedBenchmarkMeasurement Samples(
            double ratio,
            int count = 32,
            bool unstable = false,
            bool diagnostic = false
        )
        {
            double[] reference = new double[count];
            double[] subject = new double[count];
            for (int index = 0; index < count; ++index)
            {
                reference[index] = 40 * ratio;
                subject[index] = unstable && (index & 1) == 0 ? 80 : 40;
            }
            return new CalibratedBenchmarkMeasurement(
                reference,
                subject,
                1,
                343,
                100,
                100,
                3,
                3,
                diagnostic ? new BenchmarkSlotDiagnostics(null, 1000, true) : null
            );
        }

        private static BenchmarkFamilyMeasurement Combine(
            params CalibratedBenchmarkMeasurement[][] families
        )
        {
            int[] counts = new int[families.Length];
            for (int index = 0; index < counts.Length; ++index)
            {
                counts[index] = families[index].Length;
            }
            return BenchmarkFamilyMeasurement.Combine(families, counts, 343);
        }

        [Test]
        public void FamiliesReceiveEqualWeightsDespiteUnequalWorkloadCounts()
        {
            BenchmarkFamilyMeasurement measurement = Combine(
                new[] { Samples(4) },
                new[] { Samples(1), Samples(1), Samples(1) }
            );
            Assert.IsTrue(measurement != null);
            Assert.AreEqual(2, measurement.Ratio, 0.000001);
            Assert.AreEqual(2, measurement.RatioLower95, 0.000001);
            Assert.AreEqual(2, measurement.RatioUpper95, 0.000001);
            Assert.AreEqual(2, measurement.FamilyCount);
            Assert.AreEqual(4, measurement.WorkloadCount);
        }

        [TestCase(1.9, false)]
        [TestCase(2.01, true)]
        [TestCase(3, true)]
        public void TwofoldTargetUsesLowerInterval(double ratio, bool expected)
        {
            BenchmarkFamilyMeasurement measurement = Combine(new[] { Samples(ratio) });
            Assert.IsTrue(measurement != null);
            Assert.AreEqual(expected, measurement.MeetsTwofoldTimingTarget);
        }

        [Test]
        public void AggregateImprovementCannotHideAnIndividualRegression()
        {
            BenchmarkFamilyMeasurement measurement = Combine(new[] { Samples(8), Samples(0.9) });
            Assert.IsTrue(measurement != null);
            Assert.IsTrue(2 < measurement.RatioLower95);
            Assert.IsTrue(measurement.IsConclusive);
            Assert.IsFalse(measurement.HasTimingNonInferiority);
            Assert.IsFalse(measurement.MeetsTwofoldTimingTarget);
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void UnstableOrDiagnosticWorkloadsAreInconclusive(bool unstable, bool diagnostic)
        {
            BenchmarkFamilyMeasurement measurement = Combine(
                new[] { Samples(4, unstable: unstable, diagnostic: diagnostic) }
            );
            Assert.IsTrue(measurement != null);
            Assert.IsFalse(measurement.IsConclusive);
            Assert.IsFalse(measurement.MeetsTwofoldTimingTarget);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(31)]
        [TestCase(36)]
        public void ChangedSamplingPlansCannotQualify(int count)
        {
            CalibratedBenchmarkMeasurement[] family =
                count == 0
                    ? Array.Empty<CalibratedBenchmarkMeasurement>()
                    : new[] { Samples(3, count) };
            Assert.IsTrue(Combine(family) == null);
        }

        [Test]
        public void BelowTimingFloorCannotQualifyDespiteStableSamples()
        {
            BenchmarkFamilyMeasurement measurement = Combine(new[] { Samples(0.1) });
            Assert.IsTrue(measurement != null);
            Assert.IsFalse(measurement.IsConclusive);
            Assert.IsFalse(measurement.MeetsTwofoldTimingTarget);
        }

        [Test]
        public void ReusedMeasurementCannotCountAsIndependentEvidence()
        {
            CalibratedBenchmarkMeasurement workload = Samples(3);
            Assert.IsTrue(Combine(new[] { workload, workload }) == null);
            Assert.IsTrue(Combine(new[] { workload }, new[] { workload }) == null);
        }

        [Test]
        public void MissingFamilyCannotQualify()
        {
            IReadOnlyList<CalibratedBenchmarkMeasurement>[] families = { null };
            Assert.IsTrue(BenchmarkFamilyMeasurement.Combine(families, new[] { 1 }, 343) == null);
        }

        [Test]
        public void MissingWorkloadCannotQualify()
        {
            IReadOnlyList<CalibratedBenchmarkMeasurement>[] families = { new[] { Samples(3) } };
            Assert.IsTrue(BenchmarkFamilyMeasurement.Combine(families, new[] { 2 }, 343) == null);
            Assert.IsTrue(
                BenchmarkFamilyMeasurement.Combine(families, new[] { 1, 1 }, 343) == null
            );
            Assert.IsTrue(BenchmarkFamilyMeasurement.Combine(null, new[] { 1 }, 343) == null);
            Assert.IsTrue(BenchmarkFamilyMeasurement.Combine(families, null, 343) == null);
            Assert.IsTrue(Combine(new CalibratedBenchmarkMeasurement[] { null }) == null);
            Assert.IsTrue(Combine() == null);
        }

        [Test]
        public void BootstrapResamplesWholeBatchesAndReplaysDeterministically()
        {
            double[] reference = new double[32];
            double[] subject = new double[32];
            for (int index = 0; index < reference.Length; ++index)
            {
                reference[index] = 79 + index / BenchmarkProtocol.CyclesPerBatch * 0.3;
                subject[index] = 40;
            }
            CalibratedBenchmarkMeasurement workload = new CalibratedBenchmarkMeasurement(
                reference,
                subject,
                1,
                343,
                100,
                100,
                3,
                3
            );
            BenchmarkFamilyMeasurement first = Combine(new[] { workload });
            BenchmarkFamilyMeasurement replay = Combine(new[] { workload });
            Assert.IsTrue(first != null && replay != null);
            Assert.AreEqual(workload.RatioLower95, first.RatioLower95, 0.000000001);
            Assert.AreEqual(workload.RatioUpper95, first.RatioUpper95, 0.000000001);
            Assert.AreEqual(first.RatioLower95, replay.RatioLower95);
            Assert.AreEqual(first.RatioUpper95, replay.RatioUpper95);
            Assert.IsTrue(first.RatioLower95 < first.RatioUpper95);
            Assert.IsTrue(first.IsConclusive);
            Assert.IsTrue(2 < first.Ratio);
            Assert.IsTrue(first.RatioLower95 < 2);
            Assert.IsFalse(first.MeetsTwofoldTimingTarget);
        }
    }
}
