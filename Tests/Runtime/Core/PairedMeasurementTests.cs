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

        private static BenchmarkCpuSetTopology ParseCpuSetTopology(
            byte[] buffer,
            uint reportedBytes,
            ushort groups
        )
        {
            return BenchmarkCpuSetTopology.FromNativeResult(
                buffer,
                true,
                0,
                reportedBytes,
                groups,
                100,
                200,
                "control-host"
            );
        }

        private static byte[] CreateCpuSetRecord(
            int recordSize,
            ushort group,
            byte logicalIndex,
            byte efficiencyClass,
            byte flags
        )
        {
            byte[] buffer = new byte[recordSize];
            WriteCpuSetUInt32(buffer, 0, (uint)recordSize);
            WriteCpuSetUInt32(buffer, 8, uint.MaxValue);
            buffer[12] = (byte)group;
            buffer[13] = (byte)(group >> 8);
            buffer[14] = logicalIndex;
            buffer[15] = 7;
            buffer[18] = efficiencyClass;
            buffer[19] = flags;
            return buffer;
        }

        private static void WriteCpuSetUInt32(byte[] buffer, int offset, uint value)
        {
            for (int index = 0; index < 4; ++index)
            {
                buffer[offset + index] = (byte)(value >> (index * 8));
            }
        }

        [TestCase(0u, "InvalidRecordSize")]
        [TestCase(7u, "InvalidRecordSize")]
        [TestCase(8u, "TruncatedCpuSetRecord")]
        [TestCase(20u, "TruncatedCpuSetRecord")]
        [TestCase(31u, "TruncatedCpuSetRecord")]
        [TestCase(33u, "InvalidRecordSize")]
        [TestCase(uint.MaxValue, "InvalidRecordSize")]
        public void CpuSetTopologyRejectsMalformedRecordSizes(uint recordSize, string expected)
        {
            byte[] buffer = CreateCpuSetRecord(32, 0, 4, 1, 0);
            WriteCpuSetUInt32(buffer, 0, recordSize);
            BenchmarkCpuSetTopology topology = ParseCpuSetTopology(buffer, 32, 1);
            Assert.AreEqual(expected, topology.Availability);
            Assert.IsEmpty(topology.CpuSets);
            Assert.IsFalse(topology.CanMapProcessorNumberEndpoints);
        }

        [TestCase(0, 1u)]
        [TestCase(32, 33u)]
        [TestCase(32, 65_537u)]
        [TestCase(65_537, 32u)]
        public void CpuSetTopologyRejectsUndeliveredOrOversizedBuffers(
            int bufferLength,
            uint reportedBytes
        )
        {
            BenchmarkCpuSetTopology topology = ParseCpuSetTopology(
                new byte[bufferLength],
                reportedBytes,
                1
            );
            Assert.AreEqual("InvalidReturnedLength", topology.Availability);
            Assert.IsEmpty(topology.CpuSets);
            Assert.AreEqual(reportedBytes, topology.ReportedBytes);
        }

        [Test]
        public void CpuSetTopologyRejectsNullBuffersAndTrailingPartialHeaders()
        {
            Assert.AreEqual("InvalidReturnedLength", ParseCpuSetTopology(null, 0, 1).Availability);
            byte[] buffer = new byte[36];
            Array.Copy(CreateCpuSetRecord(32, 0, 4, 1, 0), buffer, 32);
            BenchmarkCpuSetTopology topology = ParseCpuSetTopology(buffer, 36, 1);
            Assert.AreEqual("TruncatedRecordHeader", topology.Availability);
            Assert.IsEmpty(
                topology.CpuSets,
                "A partial response never presents a complete topology."
            );
        }

        [TestCase(122, 65_537u, true, "BufferTooSmall")]
        [TestCase(5, 123u, false, "NativeApiFailed")]
        [TestCase(0, 123u, false, "NativeApiFailed")]
        public void CpuSetTopologyPreservesApiFailureWithoutParsingUndeliveredBytes(
            int error,
            uint reportedBytes,
            bool lengthDefined,
            string expected
        )
        {
            BenchmarkCpuSetTopology topology = BenchmarkCpuSetTopology.FromNativeResult(
                null,
                false,
                error,
                reportedBytes,
                1,
                100,
                200,
                "control-host"
            );
            Assert.AreEqual(expected, topology.Availability);
            Assert.AreEqual(error, topology.NativeError);
            Assert.AreEqual(lengthDefined ? reportedBytes : (uint?)null, topology.ReportedBytes);
            Assert.IsEmpty(topology.CpuSets);
            Assert.IsFalse(topology.CanMapProcessorNumberEndpoints);
        }

        [Test]
        public void CpuSetTopologyCountsUnknownTypesAndAdvancesActualExtendedRecordSize()
        {
            byte[] buffer = new byte[80];
            WriteCpuSetUInt32(buffer, 0, 8);
            WriteCpuSetUInt32(buffer, 4, 99);
            Array.Copy(CreateCpuSetRecord(40, 0, 4, 0, 0x81), 0, buffer, 8, 40);
            Array.Copy(CreateCpuSetRecord(32, 0, 5, 1, 2), 0, buffer, 48, 32);
            BenchmarkCpuSetTopology topology = ParseCpuSetTopology(buffer, 80, 1);
            Array.Clear(buffer, 0, buffer.Length);
            Assert.AreEqual("Available", topology.Availability);
            Assert.AreEqual(1, topology.UnknownRecordTypeCount);
            Assert.AreEqual(2, topology.CpuSets.Count);
            Assert.AreEqual(40u, topology.CpuSets[0].RecordSize);
            Assert.AreEqual(32u, topology.CpuSets[1].RecordSize);
            Assert.AreEqual(0, topology.CpuSets[0].EfficiencyClass);
            Assert.AreEqual(1, topology.CpuSets[1].EfficiencyClass);
            Assert.AreEqual(0x81, topology.CpuSets[0].AllFlags);
            Assert.IsFalse(
                topology.CanMapProcessorNumberEndpoints,
                "Unknown records make complete endpoint mapping unavailable."
            );
        }

        [Test]
        public void CpuSetTopologyReportsEmptyAndUnknownOnlyResponsesExplicitly()
        {
            BenchmarkCpuSetTopology empty = ParseCpuSetTopology(Array.Empty<byte>(), 0, 1);
            Assert.AreEqual("NoCpuSets", empty.Availability);
            Assert.IsFalse(empty.CanMapProcessorNumberEndpoints);
            byte[] unknown = new byte[8];
            WriteCpuSetUInt32(unknown, 0, 8);
            WriteCpuSetUInt32(unknown, 4, 7);
            BenchmarkCpuSetTopology topology = ParseCpuSetTopology(unknown, 8, 1);
            Assert.AreEqual("NoKnownCpuSets", topology.Availability);
            Assert.AreEqual(1, topology.UnknownRecordTypeCount);
            Assert.IsEmpty(topology.CpuSets);
            Assert.IsFalse(topology.CanMapProcessorNumberEndpoints);
        }

        [TestCase(1, 0, 4, true, "Available")]
        [TestCase(2, 0, 4, false, "Available")]
        [TestCase(2, 1, 4, false, "Available")]
        [TestCase(1, 1, 4, false, "InvalidProcessorIndex")]
        [TestCase(1, 0, 64, false, "InvalidProcessorIndex")]
        [TestCase(0, 0, 4, false, "ActiveGroupCountUnavailable")]
        public void CpuSetTopologyMapsOnlyReportedUnambiguousSingleGroupEndpoints(
            int activeGroups,
            int recordGroup,
            int logicalIndex,
            bool canMap,
            string expected
        )
        {
            BenchmarkCpuSetTopology topology = ParseCpuSetTopology(
                CreateCpuSetRecord(32, (ushort)recordGroup, (byte)logicalIndex, 1, 0),
                32,
                (ushort)activeGroups
            );
            Assert.AreEqual(expected, topology.Availability);
            Assert.AreEqual(canMap, topology.CanMapProcessorNumberEndpoints);
            Assert.AreEqual(
                canMap ? (ushort)recordGroup : (ushort?)null,
                topology.ProcessorNumberEndpointGroup
            );
        }

        [Test]
        public void CpuSetTopologyPreservesDuplicateIndicesButRefusesAmbiguousMapping()
        {
            byte[] buffer = new byte[64];
            Array.Copy(CreateCpuSetRecord(32, 0, 4, 0, 1), 0, buffer, 0, 32);
            Array.Copy(CreateCpuSetRecord(32, 0, 4, 1, 2), 0, buffer, 32, 32);
            BenchmarkCpuSetTopology topology = ParseCpuSetTopology(buffer, 64, 1);
            Assert.AreEqual("Available", topology.Availability);
            Assert.AreEqual(2, topology.CpuSets.Count);
            Assert.IsFalse(topology.CanMapProcessorNumberEndpoints);
            Assert.IsFalse(topology.ProcessorNumberEndpointGroup.HasValue);
        }

        [Test]
        public void DiagnosticTopologyJsonPreservesExactHostMetadataAndCannotPassTimingPredicates()
        {
            byte[] buffer = new byte[64];
            Array.Copy(CreateCpuSetRecord(32, 0, 4, 0, 0x81), 0, buffer, 0, 32);
            Array.Copy(CreateCpuSetRecord(32, 0, 5, 1, 2), 0, buffer, 32, 32);
            BenchmarkCpuSetTopology topology = ParseCpuSetTopology(buffer, 64, 1);
            double[] reference = new double[32];
            double[] subject = new double[32];
            for (int index = 0; index < reference.Length; ++index)
            {
                reference[index] = 100;
                subject[index] = 50;
            }
            BenchmarkSlotDiagnostics diagnostics = new(
                new BenchmarkSlotObservation[64],
                1000,
                true,
                topology
            );
            CalibratedBenchmarkMeasurement measurement = new(
                reference,
                subject,
                1,
                12345,
                100,
                100,
                3,
                3,
                diagnostics
            );
            Assert.IsTrue(measurement.HasSufficientTiming);
            Assert.IsFalse(measurement.HasTimingImprovement);
            Assert.IsFalse(measurement.HasTimingNonInferiority);
            using JsonDocument document = JsonDocument.Parse(measurement.ToJson());
            JsonElement serialized = document
                .RootElement.GetProperty(nameof(CalibratedBenchmarkMeasurement.SlotDiagnostics))
                .GetProperty(nameof(BenchmarkSlotDiagnostics.CpuSetTopology));
            Assert.AreEqual(
                1,
                serialized.GetProperty(nameof(BenchmarkCpuSetTopology.SchemaVersion)).GetInt32()
            );
            Assert.IsTrue(
                serialized
                    .GetProperty(nameof(BenchmarkCpuSetTopology.IsDiagnosticOnly))
                    .GetBoolean()
            );
            Assert.AreEqual(
                "control-host",
                serialized.GetProperty(nameof(BenchmarkCpuSetTopology.MachineName)).GetString()
            );
            Assert.AreEqual(
                100,
                serialized
                    .GetProperty(nameof(BenchmarkCpuSetTopology.CaptureStartTimestamp))
                    .GetInt64()
            );
            Assert.AreEqual(
                200,
                serialized
                    .GetProperty(nameof(BenchmarkCpuSetTopology.CaptureEndTimestamp))
                    .GetInt64()
            );
            Assert.AreEqual(
                64u,
                serialized.GetProperty(nameof(BenchmarkCpuSetTopology.ReportedBytes)).GetUInt32()
            );
            Assert.AreEqual(
                1,
                serialized
                    .GetProperty(nameof(BenchmarkCpuSetTopology.ActiveProcessorGroupCount))
                    .GetInt32()
            );
            Assert.AreEqual(
                JsonValueKind.Null,
                serialized.GetProperty(nameof(BenchmarkCpuSetTopology.NativeError)).ValueKind
            );
            Assert.IsTrue(
                serialized
                    .GetProperty(nameof(BenchmarkCpuSetTopology.CanMapProcessorNumberEndpoints))
                    .GetBoolean()
            );
            Assert.AreEqual(
                0,
                serialized
                    .GetProperty(nameof(BenchmarkCpuSetTopology.ProcessorNumberEndpointGroup))
                    .GetInt32()
            );
            JsonElement sets = serialized.GetProperty(nameof(BenchmarkCpuSetTopology.CpuSets));
            Assert.AreEqual(2, sets.GetArrayLength());
            Assert.AreEqual(
                uint.MaxValue,
                sets[0].GetProperty(nameof(BenchmarkCpuSetRecord.Id)).GetUInt32()
            );
            Assert.AreEqual(
                4,
                sets[0].GetProperty(nameof(BenchmarkCpuSetRecord.LogicalProcessorIndex)).GetInt32()
            );
            Assert.AreEqual(
                7,
                sets[0].GetProperty(nameof(BenchmarkCpuSetRecord.CoreIndex)).GetInt32()
            );
            Assert.AreEqual(
                0,
                sets[0].GetProperty(nameof(BenchmarkCpuSetRecord.EfficiencyClass)).GetInt32()
            );
            Assert.AreEqual(
                1,
                sets[1].GetProperty(nameof(BenchmarkCpuSetRecord.EfficiencyClass)).GetInt32()
            );
            Assert.AreEqual(
                0x81,
                sets[0].GetProperty(nameof(BenchmarkCpuSetRecord.AllFlags)).GetInt32()
            );
            CalibratedBenchmarkMeasurement normal = CreateSamples(reference, subject);
            Assert.IsTrue(normal.HasTimingImprovement);
            using JsonDocument normalJson = JsonDocument.Parse(normal.ToJson());
            Assert.IsFalse(
                normalJson.RootElement.TryGetProperty(
                    nameof(CalibratedBenchmarkMeasurement.SlotDiagnostics),
                    out _
                )
            );
        }

        [Test]
        public void CpuSetTopologyRetainsEveryDeliveredRecordAtTheFixedBufferBoundary()
        {
            byte[] buffer = new byte[BenchmarkCpuSetTopology.ScratchBufferBytes];
            const int recordSize = 32;
            int expected = buffer.Length / recordSize;
            for (int index = 0; index < expected; ++index)
            {
                Array.Copy(
                    CreateCpuSetRecord(recordSize, 0, (byte)(index % 64), (byte)(index % 2), 0),
                    0,
                    buffer,
                    index * recordSize,
                    recordSize
                );
            }
            BenchmarkCpuSetTopology topology = ParseCpuSetTopology(buffer, (uint)buffer.Length, 1);
            Assert.AreEqual("Available", topology.Availability);
            Assert.AreEqual(expected, topology.CpuSets.Count);
            Assert.AreEqual(63, topology.CpuSets[expected - 1].LogicalProcessorIndex);
            Assert.AreEqual(1, topology.CpuSets[expected - 1].EfficiencyClass);
            Assert.IsFalse(topology.CanMapProcessorNumberEndpoints);
        }

        [Test]
        public void CpuSetTopologyCaptureReportsActualHostOrExplicitUnavailable()
        {
            BenchmarkCpuSetTopology topology = BenchmarkCpuSetTopology.Capture();
            Assert.IsTrue(topology.IsDiagnosticOnly);
            Assert.LessOrEqual(topology.CaptureStartTimestamp, topology.CaptureEndTimestamp);
            Assert.IsFalse(string.IsNullOrEmpty(topology.Availability));
            if (string.Equals(topology.Availability, "Available", StringComparison.Ordinal))
            {
                Assert.IsFalse(string.IsNullOrEmpty(topology.MachineName));
                Assert.IsNotEmpty(topology.CpuSets);
                Assert.IsTrue(topology.ActiveProcessorGroupCount.HasValue);
                Assert.LessOrEqual(1, topology.ActiveProcessorGroupCount.Value);
                Assert.IsTrue(topology.ReportedBytes.HasValue);
                Assert.LessOrEqual(
                    topology.ReportedBytes.Value,
                    BenchmarkCpuSetTopology.ScratchBufferBytes
                );
                Assert.IsFalse(topology.NativeError.HasValue);
            }
            else
            {
                Assert.IsEmpty(topology.CpuSets);
                Assert.IsFalse(topology.CanMapProcessorNumberEndpoints);
                Assert.IsFalse(topology.ProcessorNumberEndpointGroup.HasValue);
            }
#if !UNITY_EDITOR_WIN && !UNITY_STANDALONE_WIN
            Assert.AreEqual("UnsupportedPlatform", topology.Availability);
#endif
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("0", false)]
        [TestCase("true", false)]
        [TestCase(" 1", false)]
        [TestCase("1 ", false)]
        [TestCase("1", true)]
        public void SlotDiagnosticsRequireTheExactOptInValue(string value, bool expected)
        {
            Assert.AreEqual(expected, BenchmarkProtocol.IsSlotDiagnosticsEnabled(value));
        }

        [Test]
        public void DefaultMeasurementJsonOmitsSlotDiagnosticsEntirely()
        {
            double[] reference = new double[32];
            double[] subject = new double[32];
            for (int index = 0; index < reference.Length; ++index)
            {
                reference[index] = 100;
                subject[index] = 50;
            }
            CalibratedBenchmarkMeasurement measurement = CreateSamples(reference, subject);
            using JsonDocument document = JsonDocument.Parse(measurement.ToJson());
            Assert.IsFalse(
                document.RootElement.TryGetProperty(
                    nameof(CalibratedBenchmarkMeasurement.SlotDiagnostics),
                    out _
                )
            );
            Assert.IsTrue(measurement.SlotDiagnostics == null);
        }

        [Test]
        public void DiagnosticJsonRetainsEveryChronologicalSlotAndExactCounterValues()
        {
            double[] reference = new double[32];
            double[] subject = new double[32];
            for (int index = 0; index < reference.Length; ++index)
            {
                reference[index] = 100;
                subject[index] = 50;
            }
            BenchmarkSlotObservation[] slots = new BenchmarkSlotObservation[64];
            const long frequency = 1_000;
            string order = BenchmarkProtocol.BatchOrder();
            long cursor = 1000;
            for (int index = 0; index < slots.Length; ++index)
            {
                bool isSubject = order[index % order.Length] == 'B';
                long end = cursor + (isSubject ? 50 : 100);
                BenchmarkThreadSnapshot before = BenchmarkThreadSnapshot.FromNativeResults(
                    cursor - 2,
                    cursor - 1,
                    71,
                    72,
                    3,
                    true,
                    100,
                    200,
                    0,
                    true,
                    300,
                    0
                );
                BenchmarkThreadSnapshot after = BenchmarkThreadSnapshot.FromNativeResults(
                    end + 1,
                    end + 2,
                    71,
                    72,
                    4,
                    true,
                    110,
                    220,
                    0,
                    true,
                    330,
                    0
                );
                slots[index] = new BenchmarkSlotObservation(
                    index,
                    isSubject,
                    cursor,
                    end,
                    before,
                    after
                );
                cursor = end + 10;
            }
            BenchmarkSlotDiagnostics diagnostics = new(slots, frequency, true);
            CalibratedBenchmarkMeasurement measurement = new(
                reference,
                subject,
                1,
                12345,
                100,
                100,
                3,
                3,
                diagnostics
            );
            slots[0] = default;
            using JsonDocument document = JsonDocument.Parse(measurement.ToJson());
            JsonElement serialized = document.RootElement.GetProperty(
                nameof(CalibratedBenchmarkMeasurement.SlotDiagnostics)
            );
            Assert.IsTrue(
                serialized
                    .GetProperty(nameof(BenchmarkSlotDiagnostics.IsDiagnosticOnly))
                    .GetBoolean()
            );
            Assert.AreEqual(
                frequency,
                serialized.GetProperty(nameof(BenchmarkSlotDiagnostics.CounterFrequency)).GetInt64()
            );
            JsonElement observations = serialized.GetProperty(
                nameof(BenchmarkSlotDiagnostics.Slots)
            );
            Assert.AreEqual(64, observations.GetArrayLength());
            int referenceIndex = 0;
            int subjectIndex = 0;
            int chronologicalIndex = 0;
            foreach (JsonElement observation in observations.EnumerateArray())
            {
                Assert.AreEqual(
                    chronologicalIndex,
                    observation
                        .GetProperty(nameof(BenchmarkSlotObservation.ChronologicalIndex))
                        .GetInt32()
                );
                bool isSubject = observation
                    .GetProperty(nameof(BenchmarkSlotObservation.IsSubject))
                    .GetBoolean();
                Assert.AreEqual(order[chronologicalIndex % order.Length] == 'B', isSubject);
                long start = observation
                    .GetProperty(nameof(BenchmarkSlotObservation.WorkStartTimestamp))
                    .GetInt64();
                long end = observation
                    .GetProperty(nameof(BenchmarkSlotObservation.WorkEndTimestamp))
                    .GetInt64();
                double expected = isSubject ? subject[subjectIndex++] : reference[referenceIndex++];
                Assert.AreEqual(expected, (end - start) * (1000.0 / frequency));
                JsonElement before = observation.GetProperty(
                    nameof(BenchmarkSlotObservation.Before)
                );
                JsonElement after = observation.GetProperty(nameof(BenchmarkSlotObservation.After));
                Assert.AreEqual(
                    72u,
                    before.GetProperty(nameof(BenchmarkThreadSnapshot.NativeThreadId)).GetUInt32()
                );
                Assert.AreEqual(
                    71u,
                    after.GetProperty(nameof(BenchmarkThreadSnapshot.NativeProcessId)).GetUInt32()
                );
                Assert.AreEqual(
                    3u,
                    before.GetProperty(nameof(BenchmarkThreadSnapshot.ProcessorNumber)).GetUInt32()
                );
                Assert.AreEqual(
                    4u,
                    after.GetProperty(nameof(BenchmarkThreadSnapshot.ProcessorNumber)).GetUInt32()
                );
                ++chronologicalIndex;
            }
            Assert.AreEqual(32, referenceIndex);
            Assert.AreEqual(32, subjectIndex);
            Assert.AreEqual(1000, diagnostics.Slots[0].WorkStartTimestamp);
            CalibratedBenchmarkMeasurement normal = CreateSamples(reference, subject);
            Assert.AreEqual(normal.Comparison, measurement.Comparison);
            Assert.AreEqual(normal.RatioLower95, measurement.RatioLower95);
            Assert.AreEqual(normal.RatioUpper95, measurement.RatioUpper95);
            Assert.IsTrue(normal.HasTimingImprovement);
            Assert.IsTrue(normal.HasTimingNonInferiority);
            Assert.IsFalse(measurement.HasTimingImprovement);
            Assert.IsFalse(measurement.HasTimingNonInferiority);
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void FailedNativeMetricsSerializeAsNullAndPreserveExactApiErrors(
            bool timesSucceeded,
            bool cyclesSucceeded
        )
        {
            BenchmarkThreadSnapshot snapshot = BenchmarkThreadSnapshot.FromNativeResults(
                100,
                110,
                71,
                72,
                3,
                timesSucceeded,
                ulong.MaxValue,
                200,
                5,
                cyclesSucceeded,
                ulong.MaxValue,
                87
            );
            BenchmarkSlotDiagnostics diagnostics = new(
                new[] { new BenchmarkSlotObservation(0, false, 120, 130, snapshot, snapshot) },
                1000,
                true
            );
            double[] samples = new double[32];
            for (int index = 0; index < samples.Length; ++index)
            {
                samples[index] = 100;
            }
            CalibratedBenchmarkMeasurement measurement = new(
                samples,
                samples,
                1,
                12345,
                100,
                100,
                3,
                3,
                diagnostics
            );
            using JsonDocument document = JsonDocument.Parse(measurement.ToJson());
            JsonElement before = document
                .RootElement.GetProperty(nameof(CalibratedBenchmarkMeasurement.SlotDiagnostics))
                .GetProperty(nameof(BenchmarkSlotDiagnostics.Slots))[0]
                .GetProperty(nameof(BenchmarkSlotObservation.Before));
            Assert.AreEqual(
                timesSucceeded ? JsonValueKind.Number : JsonValueKind.Null,
                before
                    .GetProperty(nameof(BenchmarkThreadSnapshot.KernelTime100Nanoseconds))
                    .ValueKind
            );
            Assert.AreEqual(
                timesSucceeded ? JsonValueKind.Number : JsonValueKind.Null,
                before.GetProperty(nameof(BenchmarkThreadSnapshot.UserTime100Nanoseconds)).ValueKind
            );
            Assert.AreEqual(
                cyclesSucceeded ? JsonValueKind.Number : JsonValueKind.Null,
                before.GetProperty(nameof(BenchmarkThreadSnapshot.ThreadCycleCount)).ValueKind
            );
            if (timesSucceeded)
            {
                Assert.AreEqual(
                    ulong.MaxValue,
                    before
                        .GetProperty(nameof(BenchmarkThreadSnapshot.KernelTime100Nanoseconds))
                        .GetUInt64()
                );
                Assert.AreEqual(
                    JsonValueKind.Null,
                    before.GetProperty(nameof(BenchmarkThreadSnapshot.ThreadTimesError)).ValueKind
                );
            }
            else
            {
                Assert.AreEqual(
                    5,
                    before.GetProperty(nameof(BenchmarkThreadSnapshot.ThreadTimesError)).GetInt32()
                );
            }
            if (cyclesSucceeded)
            {
                Assert.AreEqual(
                    ulong.MaxValue,
                    before.GetProperty(nameof(BenchmarkThreadSnapshot.ThreadCycleCount)).GetUInt64()
                );
                Assert.AreEqual(
                    JsonValueKind.Null,
                    before.GetProperty(nameof(BenchmarkThreadSnapshot.ThreadCyclesError)).ValueKind
                );
            }
            else
            {
                Assert.AreEqual(
                    87,
                    before.GetProperty(nameof(BenchmarkThreadSnapshot.ThreadCyclesError)).GetInt32()
                );
            }
            Assert.AreEqual(
                timesSucceeded && cyclesSucceeded ? "Available" : "NativeApiFailed",
                before.GetProperty(nameof(BenchmarkThreadSnapshot.Availability)).GetString()
            );
        }

        [Test]
        public void NativeSnapshotReportsAvailabilityWithoutInventingCounters()
        {
            BenchmarkThreadSnapshot snapshot = BenchmarkThreadSnapshot.Capture();
            Assert.LessOrEqual(snapshot.CaptureStartTimestamp, snapshot.CaptureEndTimestamp);
            Assert.IsFalse(string.IsNullOrWhiteSpace(snapshot.Availability));
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (snapshot.NativeThreadId.HasValue)
            {
                Assert.Less(0u, snapshot.NativeThreadId.Value);
                Assert.IsTrue(snapshot.NativeProcessId.HasValue);
                Assert.Less(0u, snapshot.NativeProcessId.Value);
                Assert.IsTrue(snapshot.ProcessorNumber.HasValue);
                Assert.AreEqual(
                    snapshot.KernelTime100Nanoseconds.HasValue,
                    snapshot.UserTime100Nanoseconds.HasValue
                );
                Assert.AreEqual(
                    !snapshot.ThreadTimesError.HasValue,
                    snapshot.UserTime100Nanoseconds.HasValue
                );
                Assert.AreEqual(
                    !snapshot.ThreadCyclesError.HasValue,
                    snapshot.ThreadCycleCount.HasValue
                );
                return;
            }
#else
            Assert.AreEqual("UnsupportedPlatform", snapshot.Availability);
#endif
            Assert.IsFalse(snapshot.NativeThreadId.HasValue);
            Assert.IsFalse(snapshot.NativeProcessId.HasValue);
            Assert.IsFalse(snapshot.ProcessorNumber.HasValue);
            Assert.IsFalse(snapshot.KernelTime100Nanoseconds.HasValue);
            Assert.IsFalse(snapshot.UserTime100Nanoseconds.HasValue);
            Assert.IsFalse(snapshot.ThreadCycleCount.HasValue);
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
            for (int index = 0; index < reference.Length; ++index)
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
            for (int index = 0; index < reference.Length; ++index)
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
            for (int index = 0; index < reference.Length; ++index)
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
            for (int index = 0; index < count; ++index)
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
            for (int index = 0; index < count; ++index)
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
            for (int index = 0; index < order.Length; ++index)
            {
                if (order[index] == 'A')
                {
                    reference += index;
                    ++referenceCount;
                }
                else
                {
                    subject += index;
                    ++subjectCount;
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
            for (int slot = 0; slot < BenchmarkProtocol.CyclesPerBatch; ++slot)
            {
                referenceTotal += 100.0 * drift;
                drift *= decayPerSlot;
            }

            for (int slot = 0; slot < BenchmarkProtocol.CyclesPerBatch; ++slot)
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
