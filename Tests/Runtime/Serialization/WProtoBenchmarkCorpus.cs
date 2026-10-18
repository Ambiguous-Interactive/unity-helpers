// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using PublicSerializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    /// <summary>Owns the frozen serializer corpus and its calibrated measurement paths.</summary>
    public static class WProtoBenchmarkCorpus
    {
        /// <summary>Version identifying the frozen input corpus.</summary>
        public const string CorpusVersion = "wproto-seven-families-v1";

        /// <summary>Identifies the ReadMemory comparison.</summary>
        public const string ReadMemory = "read-memory";

        /// <summary>Identifies the ReadStream comparison.</summary>
        public const string ReadStream = "read-stream";

        /// <summary>Identifies the ReadFinite comparison.</summary>
        public const string ReadFinite = "read-finite";

        /// <summary>Identifies the ReadDirect comparison.</summary>
        public const string ReadDirect = "read-direct";

        /// <summary>Identifies the ReadPublic comparison.</summary>
        public const string ReadPublic = "read-public";

        /// <summary>Identifies the WriteBuffer comparison.</summary>
        public const string WriteBuffer = "write-buffer";

        /// <summary>Identifies the WriteStream comparison.</summary>
        public const string WriteStream = "write-stream";

        /// <summary>Identifies the WriteArray comparison.</summary>
        public const string WriteArray = "write-array";

        /// <summary>Identifies the WriteDirect comparison.</summary>
        public const string WriteDirect = "write-direct";

        /// <summary>Identifies the WritePublic comparison.</summary>
        public const string WritePublic = "write-public";
        private const int BenchmarkSeed = 343;
        private const double CalibrationTargetMilliseconds = 50;
        private const int CalibrationAttemptCount = 21;
        private const string VerifyOnly = "verify";

        private static readonly int[] CorpusSizes = { 0, 1, 32, 1024 };
        private static readonly ulong[] CorpusVarints =
        {
            0,
            127,
            128,
            1UL << 14,
            1UL << 21,
            1UL << 28,
            1UL << 35,
            1UL << 42,
            1UL << 49,
            1UL << 56,
            ulong.MaxValue,
        };

        /// <summary>Verifies every frozen workload without collecting timings.</summary>
        public static void VerifyFrozenCorpus()
        {
            IReadOnlyList<IReadOnlyList<CalibratedBenchmarkMeasurement>> families = RunFrozenCorpus(
                VerifyOnly
            );
            Assert.AreEqual(7, families.Count);
            Assert.AreEqual(8, families[2].Count);
            Assert.AreEqual(4, families[4].Count);
        }

        /// <summary>Measures all workloads for one preregistered API path.</summary>
        public static void Measure(string operation)
        {
#if ENABLE_IL2CPP
            Assert.Ignore(
                "Third-party corpus comparisons require a supported Mono oracle; AOT qualification uses same-backend baseline/candidate runs."
            );
#endif
#if !WALLSTOP_PROTO
            if (
                string.Equals(operation, ReadPublic, StringComparison.Ordinal)
                || string.Equals(operation, WritePublic, StringComparison.Ordinal)
            )
            {
                Assert.Ignore(
                    "Public facade comparisons require the generated-first WALLSTOP_PROTO configuration."
                );
            }
#endif
            int expectedWorkloads = operation.StartsWith("read-", StringComparison.Ordinal)
                ? 32
                : 28;
            UnityEngine.Debug.Log(
                FormattableString.Invariant(
                    $"WPROTO_CORPUS_PLAN {CorpusVersion} {operation} {{\"SchemaVersion\":1,\"Seed\":{BenchmarkSeed},\"CalibrationTargetMilliseconds\":{CalibrationTargetMilliseconds:R},\"Batches\":{BenchmarkProtocol.CalibratedBatches},\"CyclesPerBatch\":{BenchmarkProtocol.CyclesPerBatch},\"BatchOrder\":\"ABBABAAB\",\"ExpectedWorkloads\":{expectedWorkloads}}}"
                )
            );
            IReadOnlyList<IReadOnlyList<CalibratedBenchmarkMeasurement>> families = RunFrozenCorpus(
                operation
            );
            int numericCases = operation.StartsWith("read-", StringComparison.Ordinal) ? 8 : 4;
            BenchmarkFamilyMeasurement result = BenchmarkFamilyMeasurement.Combine(
                families,
                new[] { 4, 4, numericCases, 4, 4, 4, 4 },
                BenchmarkSeed
            );
            if (result == null)
            {
                UnityEngine.Debug.Log(
                    $"WPROTO_CORPUS_RESULT {CorpusVersion} {operation} inconclusive: incomplete calibrated corpus"
                );
                return;
            }
            UnityEngine.Debug.Log(
                FormattableString.Invariant(
                    $"WPROTO_CORPUS_RESULT {CorpusVersion} {operation} families={result.FamilyCount} workloads={result.WorkloadCount} ratio={result.Ratio:R} lower95={result.RatioLower95:R} upper95={result.RatioUpper95:R} conclusive={result.IsConclusive} nonInferior={result.HasTimingNonInferiority} twofoldTimingTarget={result.MeetsTwofoldTimingTarget}. Timing only; platform, allocation, retention, size and fresh-process qualification remain separate gates."
                )
            );
        }

        private static IReadOnlyList<IReadOnlyList<CalibratedBenchmarkMeasurement>> RunFrozenCorpus(
            string operation
        )
        {
            HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);
            List<CalibratedBenchmarkMeasurement>[] families =
                new List<CalibratedBenchmarkMeasurement>[7];
            for (int index = 0; index < families.Length; ++index)
            {
                families[index] = new List<CalibratedBenchmarkMeasurement>();
            }
            foreach (int size in CorpusSizes)
            {
                ProtoBenchmarkScalars scalars = new ProtoBenchmarkScalars
                {
                    First = size == 0 ? 0UL : 127UL,
                    Second = size < 32 ? 0UL : 128UL,
                    Third = size < 32 ? 0 : 1UL << 14,
                    Fourth = size < 32 ? 0 : 1UL << 21,
                    Fifth = size < 32 ? 0 : 1UL << 28,
                    Sixth = size < 1024 ? 0 : 1UL << 35,
                    Seventh = size < 1024 ? 0 : 1UL << 42,
                    Eighth = size < 1024 ? 0 : 1UL << 49,
                    Ninth = size < 1024 ? 0 : 1UL << 56,
                    Tenth = size < 1024 ? 0 : ulong.MaxValue,
                };
                families[0]
                    .Add(
                        MeasureCorpusCase(
                            identities,
                            operation,
                            "scalar-heavy",
                            size.ToString(CultureInfo.InvariantCulture),
                            scalars,
                            AssertScalars
                        )
                    );
                ProtoBenchmarkValue<string> text = new ProtoBenchmarkValue<string>
                {
                    Value = new string('界', size * 16),
                };
                families[1]
                    .Add(
                        MeasureCorpusCase(
                            identities,
                            operation,
                            "text-heavy",
                            size.ToString(CultureInfo.InvariantCulture),
                            text,
                            (expected, actual) =>
                                Assert.AreEqual(expected.Value, actual.Value ?? string.Empty)
                        )
                    );
                ulong[] values = new ulong[size];
                Dictionary<string, int> map = new Dictionary<string, int>();
                ProtoBenchmarkNode[] nodes = new ProtoBenchmarkNode[size];
                DayOfWeek[] enums = new DayOfWeek[size];
                for (int index = 0; index < size; ++index)
                {
                    values[index] = CorpusVarints[index % CorpusVarints.Length];
                    map.Add("key-" + index.ToString(CultureInfo.InvariantCulture), index * 17);
                    nodes[index] = new ProtoBenchmarkLeaf
                    {
                        Id = index,
                        Label = "leaf-" + index.ToString(CultureInfo.InvariantCulture),
                    };
                    enums[index] = (DayOfWeek)(index % 7);
                }
                ProtoBenchmarkRepeated<ulong> numbers = new ProtoBenchmarkRepeated<ulong>
                {
                    Value = values,
                };
                families[2]
                    .Add(
                        MeasureCorpusCase(
                            identities,
                            operation,
                            "repeated-numeric",
                            size.ToString(CultureInfo.InvariantCulture),
                            numbers,
                            (expected, actual) =>
                                CollectionAssert.AreEqual(
                                    expected.Value,
                                    actual.Value ?? Array.Empty<ulong>()
                                )
                        )
                    );
                if (
                    operation.StartsWith("read-", StringComparison.Ordinal)
                    || string.Equals(operation, VerifyOnly, StringComparison.Ordinal)
                )
                {
                    byte[] unpacked = new byte[size * 11 + 3];
                    WProtoWriter writer = new WProtoWriter(unpacked);
                    foreach (ulong value in values)
                    {
                        Assert.IsTrue(writer.TryWriteTag(1, WProtoWireType.Varint));
                        Assert.IsTrue(writer.TryWriteVarint64(value));
                    }
                    Assert.IsTrue(writer.TryWriteTag(100, WProtoWireType.Varint));
                    Assert.IsTrue(writer.TryWriteVarint64(1));
                    Array.Resize(ref unpacked, writer.Position);
                    families[2]
                        .Add(
                            MeasureCorpusCase(
                                identities,
                                operation,
                                "repeated-numeric",
                                size.ToString(CultureInfo.InvariantCulture) + "-unpacked",
                                numbers,
                                (expected, actual) =>
                                    CollectionAssert.AreEqual(
                                        expected.Value,
                                        actual.Value ?? Array.Empty<ulong>()
                                    ),
                                unpacked
                            )
                        );
                }
                families[3]
                    .Add(
                        MeasureCorpusCase(
                            identities,
                            operation,
                            "maps",
                            size.ToString(CultureInfo.InvariantCulture),
                            new ProtoBenchmarkMap { Value = map },
                            (expected, actual) =>
                                CollectionAssert.AreEquivalent(
                                    expected.Value,
                                    actual.Value ?? new Dictionary<string, int>()
                                )
                        )
                    );
                families[4]
                    .Add(
                        MeasureCorpusCase(
                            identities,
                            operation,
                            "nested-polymorphic",
                            size.ToString(CultureInfo.InvariantCulture),
                            new ProtoBenchmarkRepeated<ProtoBenchmarkNode> { Value = nodes },
                            AssertNodes
                        )
                    );
                families[5]
                    .Add(
                        MeasureCorpusCase(
                            identities,
                            operation,
                            "generic-enum",
                            size.ToString(CultureInfo.InvariantCulture),
                            new ProtoBenchmarkRepeated<DayOfWeek> { Value = enums },
                            (expected, actual) =>
                                CollectionAssert.AreEqual(
                                    expected.Value,
                                    actual.Value ?? Array.Empty<DayOfWeek>()
                                )
                        )
                    );
                families[6]
                    .Add(
                        MeasureCorpusCase(
                            identities,
                            operation,
                            "large-binary",
                            size.ToString(CultureInfo.InvariantCulture),
                            new ProtoBenchmarkValue<byte[]>
                            {
                                Value = MakeBytes(size * 1024, BenchmarkSeed),
                            },
                            (expected, actual) =>
                                CollectionAssert.AreEqual(
                                    expected.Value,
                                    actual.Value ?? Array.Empty<byte>()
                                )
                        )
                    );
            }
            Assert.IsTrue(
                ProtoBenchmarkCorpusIdentity.IsComplete(
                    identities,
                    operation.StartsWith("read-", StringComparison.Ordinal)
                        || string.Equals(operation, VerifyOnly, StringComparison.Ordinal)
                ),
                "Every frozen workload identity must appear exactly once."
            );
            return families;
        }

        private static CalibratedBenchmarkMeasurement MeasureCorpusCase<T>(
            HashSet<string> identities,
            string operation,
            string family,
            string workload,
            T value,
            Action<T, T> assertEqual,
            byte[] readPayload = null
        )
        {
            Assert.IsTrue(
                WProtoFormatterProvider.TryGet(out IWProtoFormatter<T> formatter),
                "The corpus must have a generated formatter before measurement."
            );
            byte[] generatedPayload = WProtoFacade.Serialize(value);
            byte[] input = readPayload ?? generatedPayload;
            byte[] reusable = new byte[Math.Max(1, generatedPayload.Length)];
            ProtoBenchmarkBufferWriter destination = new ProtoBenchmarkBufferWriter(
                Math.Max(1, generatedPayload.Length)
            );
            using MemoryStream outputStream = new MemoryStream();
            using MemoryStream inputStream = new MemoryStream(input, writable: false);
            WProtoReadLimits limits = new WProtoReadLimits(
                2 * 1024 * 1024,
                2 * 1024 * 1024,
                32768,
                32,
                32768
            ).WithTotalFieldCount(32768);
            using SHA256 fingerprint = SHA256.Create();
            string inputHash = Convert.ToBase64String(fingerprint.ComputeHash(input));
            UnityEngine.Debug.Log(
                $"WPROTO_CORPUS_INPUT {CorpusVersion} {operation} {family}/{workload} bytes={input.Length} sha256={inputHash}"
            );
            Assert.IsTrue(
                ProtoBenchmarkCorpusIdentity.TryAccept(
                    identities,
                    family + "/" + workload,
                    input.Length,
                    inputHash
                ),
                "The corpus identity, bytes, or uniqueness changed without a versioned baseline update."
            );
            Action verify = () =>
            {
                Assert.IsTrue(WProtoFacade.TryDeserialize(input, out T generated));
                Assert.IsTrue(generated != null);
                assertEqual(value, generated);
#if !ENABLE_IL2CPP
                T reference = Serializer.Deserialize<T>(new ReadOnlyMemory<byte>(input));
                Assert.IsTrue(reference != null);
                assertEqual(value, reference);
                destination.Rewind();
                Serializer.Serialize(destination, value);
                Assert.IsTrue(
                    WProtoFacade.TryDeserialize(destination.WrittenSpan, out T oracleRoundtrip)
                );
                assertEqual(value, oracleRoundtrip);
#endif
                WProtoReader reader = new WProtoReader(input, limits);
                Assert.IsTrue(formatter.TryRead(ref reader, out T direct));
                Assert.IsFalse(reader.Malformed);
                Assert.AreEqual(input.Length, reader.Position);
                assertEqual(value, direct);
                Assert.AreEqual(generatedPayload.Length, formatter.Measure(in value));
                WProtoWriter writer = new WProtoWriter(reusable);
                Assert.IsTrue(formatter.Write(ref writer, in value));
                Assert.IsFalse(writer.Faulted);
                Assert.AreEqual(generatedPayload.Length, writer.Position);
                CollectionAssert.AreEqual(generatedPayload, writer.Written.ToArray());
                assertEqual(value, PublicSerializer.ProtoDeserialize<T>(input));
                byte[] publicPayload = PublicSerializer.ProtoSerialize(value);
                Assert.IsTrue(WProtoFacade.TryDeserialize(publicPayload, out T publicRoundtrip));
                assertEqual(value, publicRoundtrip);
            };
            verify();
            if (string.Equals(operation, VerifyOnly, StringComparison.Ordinal))
            {
                return null;
            }
            bool reading = operation.StartsWith("read-", StringComparison.Ordinal);
            Func<int, long> referenceWork = iterations =>
            {
                long checksum = 0;
                for (int index = 0; index < iterations; ++index)
                {
                    if (reading)
                    {
                        T restored;
                        if (string.Equals(operation, ReadStream, StringComparison.Ordinal))
                        {
                            inputStream.Position = 0;
                            restored = Serializer.Deserialize<T>(inputStream);
                        }
                        else
                        {
                            restored = Serializer.Deserialize<T>(new ReadOnlyMemory<byte>(input));
                        }
                        GC.KeepAlive(restored);
                        checksum += input.Length;
                    }
                    else if (string.Equals(operation, WriteStream, StringComparison.Ordinal))
                    {
                        outputStream.Position = 0;
                        outputStream.SetLength(0);
                        Serializer.Serialize(outputStream, value);
                        checksum += outputStream.Length;
                    }
                    else
                    {
                        destination.Rewind();
                        Serializer.Serialize(destination, value);
                        if (
                            string.Equals(operation, WriteArray, StringComparison.Ordinal)
                            || string.Equals(operation, WritePublic, StringComparison.Ordinal)
                        )
                        {
                            byte[] returned = destination.WrittenSpan.ToArray();
                            GC.KeepAlive(returned);
                            checksum += returned.Length;
                        }
                        else
                        {
                            checksum += destination.WrittenCount;
                        }
                    }
                }
                return checksum;
            };
            Func<int, long> subjectWork = iterations =>
            {
                long checksum = 0;
                for (int index = 0; index < iterations; ++index)
                {
                    if (reading)
                    {
                        T restored;
                        if (string.Equals(operation, ReadDirect, StringComparison.Ordinal))
                        {
                            WProtoReader reader = new WProtoReader(input);
                            if (
                                !formatter.TryRead(ref reader, out restored)
                                || reader.Malformed
                                || reader.Position != input.Length
                            )
                            {
                                Assert.Fail("Direct read failed during measurement.");
                            }
                        }
                        else if (string.Equals(operation, ReadPublic, StringComparison.Ordinal))
                        {
                            restored = PublicSerializer.ProtoDeserialize<T>(input);
                        }
                        else
                        {
                            restored = string.Equals(
                                operation,
                                ReadFinite,
                                StringComparison.Ordinal
                            )
                                ? WProtoFacade.Deserialize<T>(input, limits)
                                : WProtoFacade.Deserialize<T>(input);
                        }
                        GC.KeepAlive(restored);
                        checksum += input.Length;
                    }
                    else if (string.Equals(operation, WriteDirect, StringComparison.Ordinal))
                    {
                        int measured = formatter.Measure(in value);
                        WProtoWriter writer = new WProtoWriter(reusable);
                        if (
                            !formatter.Write(ref writer, in value)
                            || writer.Faulted
                            || writer.Position != measured
                        )
                        {
                            Assert.Fail("Direct write failed during measurement.");
                        }
                        checksum += writer.Position;
                    }
                    else if (
                        string.Equals(operation, WriteArray, StringComparison.Ordinal)
                        || string.Equals(operation, WritePublic, StringComparison.Ordinal)
                    )
                    {
                        byte[] returned = string.Equals(
                            operation,
                            WritePublic,
                            StringComparison.Ordinal
                        )
                            ? PublicSerializer.ProtoSerialize(value)
                            : WProtoFacade.Serialize(value);
                        GC.KeepAlive(returned);
                        checksum += returned.Length;
                    }
                    else
                    {
                        WProtoWriteResult result = WProtoFacade.Serialize(value, ref reusable);
                        if (!result.Served || result.Resized)
                        {
                            Assert.Fail(
                                "The reusable generated path stopped serving its registered contract."
                            );
                        }
                        checksum += result.Length;
                    }
                }
                return checksum;
            };
            if (identities.Count == 1)
            {
                MeasureSameCodeControl(operation, "reference-same-code", referenceWork, verify);
                MeasureSameCodeControl(operation, "subject-same-code", subjectWork, verify);
            }
            CalibratedBenchmarkMeasurement samples = MeasureWithCalibration(
                operation,
                family + "/" + workload,
                referenceWork,
                subjectWork,
                verify
            );
            UnityEngine.Debug.Log(
                $"WPROTO_CORPUS_SAMPLES {CorpusVersion} {operation} {family} {workload} bytes={input.Length} sha256={inputHash} {(samples == null ? "inconclusive: calibration failed" : samples.ToJson())}"
            );
            return samples;
        }

        private static void MeasureSameCodeControl(
            string operation,
            string control,
            Func<int, long> work,
            Action verify
        )
        {
            CalibratedBenchmarkMeasurement samples = MeasureWithCalibration(
                operation,
                control,
                work,
                work,
                verify
            );
            UnityEngine.Debug.Log(
                $"WPROTO_CORPUS_CONTROL {CorpusVersion} {operation} {control} {(samples == null ? "inconclusive: calibration failed" : samples.ToJson())}"
            );
        }

        private static CalibratedBenchmarkMeasurement MeasureWithCalibration(
            string operation,
            string identity,
            Func<int, long> referenceWork,
            Func<int, long> subjectWork,
            Action verify
        )
        {
            int[] iterations = new int[CalibrationAttemptCount];
            double[] reference = new double[CalibrationAttemptCount];
            double[] subject = new double[CalibrationAttemptCount];
            int observed = 0;
            CalibratedBenchmarkMeasurement samples = BenchmarkProtocol.MeasureCalibrated(
                referenceWork,
                subjectWork,
                verify,
                BenchmarkSeed,
                CalibrationTargetMilliseconds,
                (count, referenceMilliseconds, subjectMilliseconds) =>
                {
                    if (observed >= CalibrationAttemptCount)
                    {
                        Assert.Fail("The calibration attempt plan changed.");
                        return;
                    }
                    iterations[observed] = count;
                    reference[observed] = referenceMilliseconds;
                    subject[observed] = subjectMilliseconds;
                    ++observed;
                }
            );
            using MemoryStream stream = new MemoryStream();
            Utf8JsonWriter writer = new Utf8JsonWriter(stream);
            try
            {
                writer.WriteStartObject();
                writer.WriteNumber("SchemaVersion", 1);
                writer.WriteStartArray("Pairs");
                for (int index = 0; index < observed; ++index)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("Iterations", iterations[index]);
                    writer.WriteNumber("ReferenceMilliseconds", reference[index]);
                    writer.WriteNumber("SubjectMilliseconds", subject[index]);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            finally
            {
                writer.Dispose();
            }
            string record = Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
            UnityEngine.Debug.Log(
                $"WPROTO_CORPUS_CALIBRATION {CorpusVersion} {operation} {identity} {record}"
            );
            return samples;
        }

        private static byte[] MakeBytes(int length, int seed)
        {
            byte[] bytes = new byte[length];
            int state = seed;
            for (int index = 0; index < bytes.Length; ++index)
            {
                state = unchecked(state * 1664525 + 1013904223);
                bytes[index] = (byte)(state >> 24);
            }
            return bytes;
        }

        private static void AssertScalars(
            ProtoBenchmarkScalars expected,
            ProtoBenchmarkScalars actual
        )
        {
            Assert.IsTrue(actual != null);
            Assert.AreEqual(expected.CorpusMarker, actual.CorpusMarker);
            Assert.AreEqual(expected.First, actual.First);
            Assert.AreEqual(expected.Second, actual.Second);
            Assert.AreEqual(expected.Third, actual.Third);
            Assert.AreEqual(expected.Fourth, actual.Fourth);
            Assert.AreEqual(expected.Fifth, actual.Fifth);
            Assert.AreEqual(expected.Sixth, actual.Sixth);
            Assert.AreEqual(expected.Seventh, actual.Seventh);
            Assert.AreEqual(expected.Eighth, actual.Eighth);
            Assert.AreEqual(expected.Ninth, actual.Ninth);
            Assert.AreEqual(expected.Tenth, actual.Tenth);
        }

        private static void AssertNodes(
            ProtoBenchmarkRepeated<ProtoBenchmarkNode> expected,
            ProtoBenchmarkRepeated<ProtoBenchmarkNode> actual
        )
        {
            ProtoBenchmarkNode[] restored = actual.Value ?? Array.Empty<ProtoBenchmarkNode>();
            Assert.AreEqual(expected.Value.Length, restored.Length);
            for (int index = 0; index < restored.Length; ++index)
            {
                Assert.IsInstanceOf<ProtoBenchmarkLeaf>(restored[index]);
                Assert.AreEqual(expected.Value[index].Id, restored[index].Id);
                Assert.AreEqual(
                    ((ProtoBenchmarkLeaf)expected.Value[index]).Label,
                    ((ProtoBenchmarkLeaf)restored[index]).Label
                );
            }
        }
    }
}
