// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using UnityEngine;

    /// <summary>Immutable raw timings and reproducible paired bootstrap statistics.</summary>
    public sealed class CalibratedBenchmarkMeasurement
    {
        private const int BootstrapRepetitions = 2000;

        /// <summary>Reference slot durations in milliseconds, in observation order.</summary>
        public ReadOnlyCollection<double> ReferenceMilliseconds { get; }

        /// <summary>Subject slot durations in milliseconds, in observation order.</summary>
        public ReadOnlyCollection<double> SubjectMilliseconds { get; }

        /// <summary>Paired log throughput ratios; positive values favor the subject.</summary>
        public ReadOnlyCollection<double> PairedLogRatios { get; }

        /// <summary>Throughput comparison and within-arm dispersion.</summary>
        public PairedMeasurement Comparison { get; }

        /// <summary>Lower endpoint of the 95% paired bootstrap throughput ratio interval.</summary>
        public double RatioLower95 { get; }

        /// <summary>Upper endpoint of the 95% paired bootstrap throughput ratio interval.</summary>
        public double RatioUpper95 { get; }

        /// <summary>Reference timing median, p95, and median absolute deviation.</summary>
        public TimingSummary ReferenceSummary { get; }

        /// <summary>Subject timing median, p95, and median absolute deviation.</summary>
        public TimingSummary SubjectSummary { get; }

        /// <summary>Iterations executed in every retained slot.</summary>
        public int Iterations { get; }

        /// <summary>Replay seed supplied by the workload.</summary>
        public int Seed { get; }

        /// <summary>Measured reference warmup duration.</summary>
        public double ReferenceWarmupMilliseconds { get; }

        /// <summary>Measured subject warmup duration.</summary>
        public double SubjectWarmupMilliseconds { get; }

        /// <summary>Reference executions completed during separate warmup.</summary>
        public int ReferenceWarmupExecutions { get; }

        /// <summary>Subject executions completed during separate warmup.</summary>
        public int SubjectWarmupExecutions { get; }

        /// <summary>Environment facts; unavailable build settings remain explicitly unknown.</summary>
        public ReadOnlyDictionary<string, string> EnvironmentMetadata { get; }

        /// <summary>Whether every retained slot exceeds the predeclared clock-noise floor.</summary>
        public bool HasSufficientTiming { get; }

        /// <summary>Whether timing alone meets the five-percent runtime improvement rule.</summary>
        /// <remarks>Controls, semantics, allocation, retention, and size remain separate required evidence.</remarks>
        public bool HasTimingImprovement =>
            HasSufficientTiming
            && Comparison.IsStable(BenchmarkProtocol.DefaultSpreadLimit)
            && 1.0 / 0.95 <= Comparison.Ratio
            && 1 < RatioLower95;

        /// <summary>Whether the full interval is within the five-percent runtime non-inferiority margin.</summary>
        public bool HasTimingNonInferiority =>
            HasSufficientTiming
            && Comparison.IsStable(BenchmarkProtocol.DefaultSpreadLimit)
            && 1.0 / 1.05 <= RatioLower95;

        internal CalibratedBenchmarkMeasurement(
            double[] referenceMilliseconds,
            double[] subjectMilliseconds,
            int iterations,
            int seed,
            double referenceWarmupMilliseconds,
            double subjectWarmupMilliseconds,
            int referenceWarmupExecutions,
            int subjectWarmupExecutions
        )
        {
            ReferenceMilliseconds = Array.AsReadOnly((double[])referenceMilliseconds.Clone());
            SubjectMilliseconds = Array.AsReadOnly((double[])subjectMilliseconds.Clone());
            Iterations = iterations;
            Seed = seed;
            ReferenceWarmupMilliseconds = referenceWarmupMilliseconds;
            SubjectWarmupMilliseconds = subjectWarmupMilliseconds;
            ReferenceWarmupExecutions = referenceWarmupExecutions;
            SubjectWarmupExecutions = subjectWarmupExecutions;
            ReferenceSummary = new TimingSummary(referenceMilliseconds);
            SubjectSummary = new TimingSummary(subjectMilliseconds);
            double[] referenceThroughputs = new double[referenceMilliseconds.Length];
            double[] subjectThroughputs = new double[subjectMilliseconds.Length];
            double[] logs = new double[referenceMilliseconds.Length];
            bool completeBatches = logs.Length % BenchmarkProtocol.CyclesPerBatch == 0;
            bool sufficient = 32 <= logs.Length && completeBatches;
            for (int index = 0; index < logs.Length; index++)
            {
                double reference = referenceMilliseconds[index];
                double subject = subjectMilliseconds[index];
                sufficient &=
                    BenchmarkProtocol.MinimumSampleMilliseconds <= reference
                    && BenchmarkProtocol.MinimumSampleMilliseconds <= subject
                    && !double.IsInfinity(reference)
                    && !double.IsInfinity(subject);
                referenceThroughputs[index] = 1000.0 * iterations / reference;
                subjectThroughputs[index] = 1000.0 * iterations / subject;
                logs[index] = Math.Log(reference) - Math.Log(subject);
            }
            HasSufficientTiming = sufficient;
            PairedLogRatios = Array.AsReadOnly(logs);
            Comparison = BenchmarkProtocol.Combine(referenceThroughputs, subjectThroughputs);
            if (completeBatches)
            {
                double[] bootstrap = Bootstrap(logs, seed);
                RatioLower95 = Quantile(bootstrap, 0.025);
                RatioUpper95 = Quantile(bootstrap, 0.975);
            }
            else
            {
                RatioLower95 = double.NaN;
                RatioUpper95 = double.NaN;
            }
            EnvironmentMetadata = CaptureEnvironment();
        }

        private static void WriteSamples(
            Utf8JsonWriter writer,
            string name,
            ReadOnlyCollection<double> samples
        )
        {
            writer.WriteStartArray(name);
            foreach (double sample in samples)
            {
                if (double.IsNaN(sample) || double.IsInfinity(sample))
                {
                    writer.WriteNullValue();
                }
                else
                {
                    writer.WriteNumberValue(sample);
                }
            }
            writer.WriteEndArray();
        }

        private static void WriteSummary(Utf8JsonWriter writer, string name, TimingSummary summary)
        {
            writer.WriteStartObject(name);
            WriteFinite(writer, nameof(summary.Median), summary.Median);
            WriteFinite(writer, nameof(summary.P95), summary.P95);
            WriteFinite(
                writer,
                nameof(summary.MedianAbsoluteDeviation),
                summary.MedianAbsoluteDeviation
            );
            writer.WriteEndObject();
        }

        private static void WriteFinite(Utf8JsonWriter writer, string name, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                writer.WriteNull(name);
            }
            else
            {
                writer.WriteNumber(name, value);
            }
        }

        private static double[] Bootstrap(double[] logs, int seed)
        {
            uint state = unchecked((uint)seed) | 1;
            double[] ratios = new double[BootstrapRepetitions];
            for (int repetition = 0; repetition < ratios.Length; repetition++)
            {
                double sum = 0;
                // Preserve correlation inside each counterbalanced batch by resampling whole batches.
                int batches = logs.Length / BenchmarkProtocol.CyclesPerBatch;
                for (int batch = 0; batch < batches; batch++)
                {
                    state ^= state << 13;
                    state ^= state >> 17;
                    state ^= state << 5;
                    int offset = (int)(state % (uint)batches) * BenchmarkProtocol.CyclesPerBatch;
                    for (int cycle = 0; cycle < BenchmarkProtocol.CyclesPerBatch; cycle++)
                    {
                        sum += logs[offset + cycle];
                    }
                }
                ratios[repetition] = Math.Exp(sum / logs.Length);
            }
            Array.Sort(ratios);
            return ratios;
        }

        private static double Quantile(double[] sorted, double fraction)
        {
            double position = (sorted.Length - 1) * fraction;
            int lower = (int)position;
            int upper = Math.Min(lower + 1, sorted.Length - 1);
            return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
        }

        private static ReadOnlyDictionary<string, string> CaptureEnvironment()
        {
#if ENABLE_IL2CPP
            const string backend = "IL2CPP";
#else
            const string backend = "Mono";
#endif
            return new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>
                {
                    ["commit"] = Environment.GetEnvironmentVariable("UH_PERF_COMMIT") ?? "unknown",
                    ["baseCommit"] =
                        Environment.GetEnvironmentVariable("UH_PERF_BASE_COMMIT") ?? "unknown",
                    ["unityVersion"] = Application.unityVersion,
                    ["backend"] = backend,
                    ["codeGeneration"] =
                        Environment.GetEnvironmentVariable("UH_PERF_CODE_GENERATION") ?? "unknown",
                    ["optimization"] =
                        Environment.GetEnvironmentVariable("UH_PERF_OPTIMIZATION") ?? "unknown",
                    ["buildConfiguration"] = Debug.isDebugBuild ? "Development" : "Release",
                    ["operatingSystem"] = SystemInfo.operatingSystem,
                    ["cpu"] = SystemInfo.processorType,
                    ["processBits"] = (IntPtr.Size * 8).ToString(),
                    ["platform"] = Application.platform.ToString(),
                    ["isEditor"] = Application.isEditor.ToString(),
                    ["allocationMetric"] = "unsupported: counter not calibrated",
                    ["retentionMetric"] = "unsupported: workload probe required",
                    ["codeSizeMetric"] = "unsupported: player build report required",
                }
            );
        }

        /// <summary>Writes raw evidence without reflection or runtime serializer generation.</summary>
        public string ToJson()
        {
            using MemoryStream stream = new MemoryStream();
            Utf8JsonWriter writer = new Utf8JsonWriter(stream);
            try
            {
                writer.WriteStartObject();
                WriteSamples(writer, nameof(ReferenceMilliseconds), ReferenceMilliseconds);
                WriteSamples(writer, nameof(SubjectMilliseconds), SubjectMilliseconds);
                WriteSamples(writer, nameof(PairedLogRatios), PairedLogRatios);
                writer.WriteStartObject(nameof(Comparison));
                WriteFinite(writer, nameof(Comparison.Ratio), Comparison.Ratio);
                WriteFinite(writer, nameof(Comparison.ReferenceSpread), Comparison.ReferenceSpread);
                WriteFinite(writer, nameof(Comparison.SubjectSpread), Comparison.SubjectSpread);
                writer.WriteNumber(nameof(Comparison.Cycles), Comparison.Cycles);
                writer.WriteEndObject();
                WriteFinite(writer, nameof(RatioLower95), RatioLower95);
                WriteFinite(writer, nameof(RatioUpper95), RatioUpper95);
                WriteSummary(writer, nameof(ReferenceSummary), ReferenceSummary);
                WriteSummary(writer, nameof(SubjectSummary), SubjectSummary);
                writer.WriteNumber(nameof(Iterations), Iterations);
                writer.WriteNumber(nameof(Seed), Seed);
                WriteFinite(
                    writer,
                    nameof(ReferenceWarmupMilliseconds),
                    ReferenceWarmupMilliseconds
                );
                WriteFinite(writer, nameof(SubjectWarmupMilliseconds), SubjectWarmupMilliseconds);
                writer.WriteNumber(nameof(ReferenceWarmupExecutions), ReferenceWarmupExecutions);
                writer.WriteNumber(nameof(SubjectWarmupExecutions), SubjectWarmupExecutions);
                writer.WriteBoolean(nameof(HasSufficientTiming), HasSufficientTiming);
                writer.WriteBoolean(nameof(HasTimingImprovement), HasTimingImprovement);
                writer.WriteBoolean(nameof(HasTimingNonInferiority), HasTimingNonInferiority);
                writer.WriteStartObject(nameof(EnvironmentMetadata));
                foreach (KeyValuePair<string, string> entry in EnvironmentMetadata)
                {
                    writer.WriteString(entry.Key, entry.Value);
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            finally
            {
                writer.Dispose();
            }
            return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
        }

        /// <summary>Distribution statistics for one arm, in milliseconds.</summary>
        public sealed class TimingSummary
        {
            /// <summary>Median retained duration.</summary>
            public double Median { get; }

            /// <summary>95th percentile retained duration with linear interpolation.</summary>
            public double P95 { get; }

            /// <summary>Median absolute deviation from the median retained duration.</summary>
            public double MedianAbsoluteDeviation { get; }

            internal TimingSummary(double[] samples)
            {
                double[] sorted = (double[])samples.Clone();
                Array.Sort(sorted);
                Median = Quantile(sorted, 0.5);
                P95 = Quantile(sorted, 0.95);
                for (int index = 0; index < sorted.Length; index++)
                {
                    sorted[index] = Math.Abs(sorted[index] - Median);
                }
                Array.Sort(sorted);
                MedianAbsoluteDeviation = Quantile(sorted, 0.5);
            }
        }
    }
}
