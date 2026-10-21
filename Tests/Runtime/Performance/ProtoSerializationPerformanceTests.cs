// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Collections;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using NUnit.Framework;
    using ProtoBuf;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Serialization;

    [TestFixture]
    [Category("Performance")]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed partial class ProtoSerializationPerformanceTests
    {
        private const int BenchmarkTimeoutMilliseconds = 600_000;

        private const int BenchmarkSeed = 343;
        private const int ClockControlMilliseconds = 20;
        private const double CalibrationTargetMilliseconds = 50;

        private static SmallMsg MakeSmall(int i)
        {
            return new() { Id = i, Name = "Name_" + i };
        }

        private static MediumMsg MakeMedium(int i, int len)
        {
            return new()
            {
                Id = i,
                Name = new string('x', (i % 17) + 8),
                Values = MakeIntArray(len, seed: i),
            };
        }

        private static LargeMsg MakeLarge(int i, int blobSize, int nestedLen)
        {
            return new()
            {
                Identifier = "2f3a9b4c-8d1f-4cba-8df7-2af00f5c6c1e",
                Description = new string('d', (i % 31) + 64),
                Blob = MakeBytes(blobSize, seed: i),
                Nested = MakeMedium(i, nestedLen),
            };
        }

        private static void RunSerializeBenchmark<T>(
            string label,
            Func<T> factory,
            out int payloadSize
        )
        {
            T sample = factory();
            byte[] buffer = null;
            using MemoryStream protobufNetBuffer = new();
            int expectedLength = WProtoFacade.Serialize(sample, ref buffer).Length;
            byte[] originalBuffer = buffer;
            Action verify = () =>
            {
                WProtoWriteResult generated = WProtoFacade.Serialize(sample, ref buffer);
                Assert.IsTrue(generated.Served);
                Assert.IsFalse(generated.Resized);
                Assert.AreEqual(expectedLength, generated.Length);
                Assert.IsTrue(ReferenceEquals(originalBuffer, buffer));
                protobufNetBuffer.Position = 0;
                protobufNetBuffer.SetLength(0);
                Serializer.Serialize(protobufNetBuffer, sample);
                CollectionAssert.AreEqual(
                    protobufNetBuffer.ToArray(),
                    buffer,
                    "Both writers must emit the same complete payload."
                );
            };
            CalibratedBenchmarkMeasurement samples = BenchmarkProtocol.MeasureCalibrated(
                iterations =>
                {
                    long checksum = 0;
                    for (int i = 0; i < iterations; ++i)
                    {
                        protobufNetBuffer.Position = 0;
                        protobufNetBuffer.SetLength(0);
                        Serializer.Serialize(protobufNetBuffer, sample);
                        checksum += protobufNetBuffer.Length;
                    }
                    return checksum;
                },
                iterations =>
                {
                    long checksum = 0;
                    for (int i = 0; i < iterations; ++i)
                    {
                        checksum += WProtoFacade.Serialize(sample, ref buffer).Length;
                    }
                    return checksum;
                },
                verify,
                BenchmarkSeed,
                CalibrationTargetMilliseconds
            );
            payloadSize = expectedLength;
            ReportWarmSamples(
                nameof(CompareCalibratedSerializeSmallMediumLarge),
                label,
                samples,
                payloadSize
            );
        }

        private static void RunDeserializeBenchmark<T>(string label, T payload)
        {
            byte[] data = WProtoFacade.Serialize(payload);
            using MemoryStream protobufNetBuffer = new(data, writable: false);
            Action verify = () =>
            {
                Assert.IsTrue(WProtoFacade.TryDeserialize(data, out T generated));
                protobufNetBuffer.Position = 0;
                T reference = Serializer.Deserialize<T>(protobufNetBuffer);
                CollectionAssert.AreEqual(data, WProtoFacade.Serialize(generated));
                using MemoryStream rewritten = new();
                Serializer.Serialize(rewritten, reference);
                CollectionAssert.AreEqual(data, rewritten.ToArray());
            };
            CalibratedBenchmarkMeasurement samples = BenchmarkProtocol.MeasureCalibrated(
                iterations =>
                {
                    long checksum = 0;
                    for (int i = 0; i < iterations; ++i)
                    {
                        protobufNetBuffer.Position = 0;
                        T restored = Serializer.Deserialize<T>(protobufNetBuffer);
                        GC.KeepAlive(restored);
                        checksum += protobufNetBuffer.Position;
                    }
                    return checksum;
                },
                iterations =>
                {
                    long checksum = 0;
                    for (int i = 0; i < iterations; ++i)
                    {
                        T restored = WProtoFacade.Deserialize<T>(data);
                        GC.KeepAlive(restored);
                        checksum += data.Length;
                    }
                    return checksum;
                },
                verify,
                BenchmarkSeed,
                CalibrationTargetMilliseconds
            );
            ReportWarmSamples(
                nameof(CompareCalibratedDeserializeSmallMediumLarge),
                label,
                samples,
                data.Length
            );
        }

        private static void RequireObservableClock(string operation = null)
        {
            Stopwatch clock = Stopwatch.StartNew();
            Thread.Sleep(ClockControlMilliseconds);
            clock.Stop();
            double elapsed = clock.Elapsed.TotalMilliseconds;
            UnityEngine.Debug.Log(
                FormattableString.Invariant(
                    $"WPROTO_CLOCK_CONTROL requested={ClockControlMilliseconds}ms measured={elapsed:F4}ms frequency={Stopwatch.Frequency}"
                )
            );
            if (operation != null)
            {
                UnityEngine.Debug.Log(
                    FormattableString.Invariant(
                        $"WPROTO_CORPUS_CLOCK {WProtoBenchmarkCorpus.CorpusVersion} {operation} {{\"RequestedMilliseconds\":{ClockControlMilliseconds},\"ElapsedMilliseconds\":{elapsed:R},\"Frequency\":{Stopwatch.Frequency}}}"
                    )
                );
            }
            if (elapsed < ClockControlMilliseconds / 2.0)
            {
                Assert.Ignore("The timer did not observe the sleep control.");
            }
        }

        private static void ReportWarmSamples(
            string operation,
            string label,
            CalibratedBenchmarkMeasurement samples,
            int payloadSize
        )
        {
            if (samples == null)
            {
                Assert.Ignore("The paired workload could not reach its calibration duration.");
            }
            UnityEngine.Debug.Log($"WPROTO_WARM_SAMPLES {operation} {label} {samples.ToJson()}");
            bool stable =
                samples.HasSufficientTiming
                && samples.Comparison.IsStable(BenchmarkProtocol.DefaultSpreadLimit);
            string ratio = stable
                ? samples.Comparison.Ratio.ToString("F3", CultureInfo.InvariantCulture) + "x"
                : "inconclusive";
            double generatedNanoseconds =
                samples.SubjectSummary.Median * 1_000_000 / samples.Iterations;
            double referenceNanoseconds =
                samples.ReferenceSummary.Median * 1_000_000 / samples.Iterations;
            UnityEngine.Debug.Log(
                $"| {label} | {generatedNanoseconds:F2} | {referenceNanoseconds:F2} | {ratio} | {payloadSize} |"
            );
        }

        private static int[] MakeIntArray(int len, int seed)
        {
            int[] arr = new int[len];
            int x = seed;
            for (int i = 0; i < len; ++i)
            {
                // simple LCG for reproducibility
                x = unchecked(x * 1103515245 + 12345);
                arr[i] = x;
            }
            return arr;
        }

        private static byte[] MakeBytes(int len, int seed)
        {
            byte[] b = new byte[len];
            int x = seed;
            for (int i = 0; i < len; ++i)
            {
                x = unchecked(x * 1664525 + 1013904223);
                b[i] = (byte)(x >> 24);
            }
            return b;
        }

        [UnityTest, Timeout(BenchmarkTimeoutMilliseconds)]
        public IEnumerator CompareCalibratedSerializeSmallMediumLarge()
        {
            yield return null;
            RequireObservableClock();
            UnityEngine.Debug.Log(
                "| Payload | WallstopProto (ns) | protobuf-net (ns) | Speedup | Size (bytes) |"
            );
            UnityEngine.Debug.Log(
                "| ------- | ------------------:| ----------------:| -------:| ------------:|"
            );

            RunSerializeBenchmark("Small", () => MakeSmall(123), out int smallSize);
            yield return null;
            RunSerializeBenchmark("Medium", () => MakeMedium(123, 16), out int medSize);
            yield return null;
            RunSerializeBenchmark("Large", () => MakeLarge(123, 8 * 1024, 64), out int largeSize);
        }

        [UnityTest, Timeout(BenchmarkTimeoutMilliseconds)]
        public IEnumerator CompareCalibratedDeserializeSmallMediumLarge()
        {
            yield return null;
            RequireObservableClock();
            UnityEngine.Debug.Log(
                "| Payload | WallstopProto (ns) | protobuf-net (ns) | Speedup | Size (bytes) |"
            );
            UnityEngine.Debug.Log(
                "| ------- | ------------------:| ----------------:| -------:| ------------:|"
            );

            RunDeserializeBenchmark("Small", MakeSmall(123));
            yield return null;
            RunDeserializeBenchmark("Medium", MakeMedium(123, 16));
            yield return null;
            RunDeserializeBenchmark("Large", MakeLarge(123, 8 * 1024, 64));
        }

        [ProtoContract]
        [WProtoContract]
        internal sealed partial class SmallMsg
        {
            [ProtoMember(1)]
            [WProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            [WProtoMember(2)]
            public string Name { get; set; }
        }

        [ProtoContract]
        [WProtoContract]
        internal sealed partial class MediumMsg
        {
            [ProtoMember(1)]
            [WProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            [WProtoMember(2)]
            public string Name { get; set; }

            [ProtoMember(3)]
            [WProtoMember(3, IsPacked = false)]
            public int[] Values { get; set; }
        }

        [ProtoContract]
        [WProtoContract]
        internal sealed partial class LargeMsg
        {
            [ProtoMember(1)]
            [WProtoMember(1)]
            public string Identifier { get; set; }

            [ProtoMember(2)]
            [WProtoMember(2)]
            public string Description { get; set; }

            [ProtoMember(3)]
            [WProtoMember(3)]
            public byte[] Blob { get; set; }

            [ProtoMember(4)]
            [WProtoMember(4)]
            public MediumMsg Nested { get; set; }
        }
    }
}
