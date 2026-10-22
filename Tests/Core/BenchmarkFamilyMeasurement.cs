// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Collections.Generic;

    /// <summary>Combines complete workload families with equal family weights and batch bootstrap intervals.</summary>
    public sealed class BenchmarkFamilyMeasurement
    {
        private const int BootstrapRepetitions = 2000;

        /// <summary>Equally weighted geometric mean of family throughput ratios.</summary>
        public double Ratio { get; }

        /// <summary>Lower endpoint of the 95% stratified batch bootstrap interval.</summary>
        public double RatioLower95 { get; }

        /// <summary>Upper endpoint of the 95% stratified batch bootstrap interval.</summary>
        public double RatioUpper95 { get; }

        /// <summary>Whether every workload has stable, sufficient, non-diagnostic timing.</summary>
        public bool IsConclusive { get; }

        /// <summary>Whether every workload satisfies the existing five-percent runtime margin.</summary>
        public bool HasTimingNonInferiority { get; }

        /// <summary>Number of complete workload families.</summary>
        public int FamilyCount { get; }

        /// <summary>Total workloads, each weighted equally within its own family.</summary>
        public int WorkloadCount { get; }

        /// <summary>Whether timing meets the twofold target and every workload's regression guard.</summary>
        public bool MeetsTwofoldTimingTarget =>
            IsConclusive && HasTimingNonInferiority && 2 <= RatioLower95;

        private BenchmarkFamilyMeasurement(
            IReadOnlyList<IReadOnlyList<CalibratedBenchmarkMeasurement>> families,
            int seed
        )
        {
            FamilyCount = families.Count;
            bool conclusive = true;
            bool nonInferior = true;
            double mean = 0;
            foreach (IReadOnlyList<CalibratedBenchmarkMeasurement> family in families)
            {
                WorkloadCount += family.Count;
                double familyMean = 0;
                foreach (CalibratedBenchmarkMeasurement workload in family)
                {
                    conclusive &=
                        workload.SlotDiagnostics == null
                        && workload.HasSufficientTiming
                        && workload.Comparison.IsStable(BenchmarkProtocol.DefaultSpreadLimit);
                    nonInferior &= workload.HasTimingNonInferiority;
                    double logSum = 0;
                    foreach (double log in workload.PairedLogRatios)
                    {
                        logSum += log;
                    }
                    familyMean += logSum / workload.PairedLogRatios.Count;
                }
                mean += familyMean / family.Count;
            }
            Ratio = Math.Exp(mean / FamilyCount);
            IsConclusive = conclusive;
            HasTimingNonInferiority = nonInferior;
            uint state = unchecked((uint)seed) | 1;
            double[] bootstrap = new double[BootstrapRepetitions];
            for (int repetition = 0; repetition < bootstrap.Length; ++repetition)
            {
                double aggregate = 0;
                foreach (IReadOnlyList<CalibratedBenchmarkMeasurement> family in families)
                {
                    double familySum = 0;
                    foreach (CalibratedBenchmarkMeasurement workload in family)
                    {
                        double sum = 0;
                        int batchCount =
                            workload.PairedLogRatios.Count / BenchmarkProtocol.CyclesPerBatch;
                        for (int batch = 0; batch < batchCount; ++batch)
                        {
                            state ^= state << 13;
                            state ^= state >> 17;
                            state ^= state << 5;
                            int offset =
                                (int)(state % (uint)batchCount) * BenchmarkProtocol.CyclesPerBatch;
                            for (int cycle = 0; cycle < BenchmarkProtocol.CyclesPerBatch; ++cycle)
                            {
                                sum += workload.PairedLogRatios[offset + cycle];
                            }
                        }
                        familySum += sum / workload.PairedLogRatios.Count;
                    }
                    aggregate += familySum / family.Count;
                }
                bootstrap[repetition] = Math.Exp(aggregate / FamilyCount);
            }
            Array.Sort(bootstrap);
            RatioLower95 = Quantile(bootstrap, 0.025);
            RatioUpper95 = Quantile(bootstrap, 0.975);
        }

        /// <summary>Refuses absent workloads or a changed sampling plan before combining families.</summary>
        public static BenchmarkFamilyMeasurement Combine(
            IReadOnlyList<IReadOnlyList<CalibratedBenchmarkMeasurement>> families,
            IReadOnlyList<int> expectedWorkloadCounts,
            int seed
        )
        {
            if (
                families == null
                || expectedWorkloadCounts == null
                || families.Count == 0
                || families.Count != expectedWorkloadCounts.Count
            )
            {
                return null;
            }
            HashSet<CalibratedBenchmarkMeasurement> observed =
                new HashSet<CalibratedBenchmarkMeasurement>();
            for (int familyIndex = 0; familyIndex < families.Count; ++familyIndex)
            {
                IReadOnlyList<CalibratedBenchmarkMeasurement> family = families[familyIndex];
                if (
                    family == null
                    || family.Count == 0
                    || family.Count != expectedWorkloadCounts[familyIndex]
                )
                {
                    return null;
                }
                foreach (CalibratedBenchmarkMeasurement workload in family)
                {
                    if (
                        workload == null
                        || !observed.Add(workload)
                        || workload.PairedLogRatios.Count
                            != BenchmarkProtocol.CalibratedBatches
                                * BenchmarkProtocol.CyclesPerBatch
                    )
                    {
                        return null;
                    }
                }
            }
            return new BenchmarkFamilyMeasurement(families, seed);
        }

        private static double Quantile(double[] sorted, double fraction)
        {
            double position = (sorted.Length - 1) * fraction;
            int lower = (int)position;
            int upper = Math.Min(lower + 1, sorted.Length - 1);
            return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
        }
    }
}
