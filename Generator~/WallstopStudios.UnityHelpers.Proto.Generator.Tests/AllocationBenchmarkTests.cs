// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [TestFixture]
    [Category("Performance")]
    public sealed partial class AllocationBenchmarkTests
    {
        private const int WarmupIterations = 100;
        private const int MeasuredIterations = 10_000;
        private const int MeasurementRounds = 5;

        [Test]
        public void RepresentativeContractAllocationAndThroughputBaseline()
        {
            AllocationContract original = CreateRepresentativeContract();
            byte[] buffer = null;
            WProtoWriteResult writeResult = WProtoFacade.Serialize(original, ref buffer);
            Assert.IsTrue(writeResult.Served);
            Assert.IsNotNull(buffer);
            Assert.Greater(writeResult.Length, 0);
            using MemoryStream protobufWriteBuffer = new MemoryStream(buffer.Length * 2);
            ProtoBuf.Serializer.Serialize(protobufWriteBuffer, original);
            using MemoryStream protobufReadBuffer = new MemoryStream(buffer, writable: false);

            for (int iteration = 0; iteration < WarmupIterations; iteration++)
            {
                WProtoFacade.Serialize(original, ref buffer);
                Assert.IsTrue(WProtoFacade.TryDeserialize(buffer, out AllocationContract _));
                protobufWriteBuffer.Position = 0;
                protobufWriteBuffer.SetLength(0);
                ProtoBuf.Serializer.Serialize(protobufWriteBuffer, original);
                protobufReadBuffer.Position = 0;
                _ = ProtoBuf.Serializer.Deserialize<AllocationContract>(protobufReadBuffer);
            }

            Measurement[] wallstopProtoSerializeRounds = new Measurement[MeasurementRounds];
            Measurement[] protobufNetSerializeRounds = new Measurement[MeasurementRounds];
            Measurement[] wallstopProtoDeserializeRounds = new Measurement[MeasurementRounds];
            Measurement[] protobufNetDeserializeRounds = new Measurement[MeasurementRounds];
            for (int round = 0; round < MeasurementRounds; round++)
            {
                if ((round & 1) == 0)
                {
                    wallstopProtoSerializeRounds[round] = MeasureWallstopProtoSerialize(
                        original,
                        ref buffer
                    );
                    protobufNetSerializeRounds[round] = MeasureProtobufNetSerialize(
                        original,
                        protobufWriteBuffer
                    );
                    wallstopProtoDeserializeRounds[round] = MeasureWallstopProtoDeserialize(buffer);
                    protobufNetDeserializeRounds[round] = MeasureProtobufNetDeserialize(
                        protobufReadBuffer
                    );
                }
                else
                {
                    protobufNetDeserializeRounds[round] = MeasureProtobufNetDeserialize(
                        protobufReadBuffer
                    );
                    wallstopProtoDeserializeRounds[round] = MeasureWallstopProtoDeserialize(buffer);
                    protobufNetSerializeRounds[round] = MeasureProtobufNetSerialize(
                        original,
                        protobufWriteBuffer
                    );
                    wallstopProtoSerializeRounds[round] = MeasureWallstopProtoSerialize(
                        original,
                        ref buffer
                    );
                }
            }

            Measurement wallstopProtoSerialize = Summarize(wallstopProtoSerializeRounds);
            Measurement protobufNetSerialize = Summarize(protobufNetSerializeRounds);
            Measurement wallstopProtoDeserialize = Summarize(wallstopProtoDeserializeRounds);
            Measurement protobufNetDeserialize = Summarize(protobufNetDeserializeRounds);

            TestContext.WriteLine(
                $"Allocation/throughput baseline: median of {MeasurementRounds} alternating rounds, {MeasuredIterations:N0} operations each:\n"
                    + $"  WallstopProto serialize: {wallstopProtoSerialize.BytesPerOperation:N2} B/op, {wallstopProtoSerialize.NanosecondsPerOperation:N2} ns/op\n"
                    + $"  protobuf-net serialize: {protobufNetSerialize.BytesPerOperation:N2} B/op, {protobufNetSerialize.NanosecondsPerOperation:N2} ns/op\n"
                    + $"  WallstopProto deserialize: {wallstopProtoDeserialize.BytesPerOperation:N2} B/op, {wallstopProtoDeserialize.NanosecondsPerOperation:N2} ns/op\n"
                    + $"  protobuf-net deserialize: {protobufNetDeserialize.BytesPerOperation:N2} B/op, {protobufNetDeserialize.NanosecondsPerOperation:N2} ns/op."
            );

            Assert.AreEqual(
                0,
                wallstopProtoSerialize.AllocatedBytes,
                "Serialization into a reusable buffer must remain allocation-free after warmup."
            );
            Assert.Less(
                wallstopProtoSerialize.AllocatedBytes,
                protobufNetSerialize.AllocatedBytes,
                "WallstopProto should not regress to protobuf-net's per-call write allocation."
            );
            Assert.LessOrEqual(
                wallstopProtoDeserialize.BytesPerOperation,
                5_272d,
                "The read baseline includes the returned strings, array, dictionary, and nested object; "
                    + "lower the ceiling when those allocations are reduced."
            );
#if !PROTOBUF_NET_ORACLE_V2
            Assert.LessOrEqual(
                wallstopProtoSerialize.NanosecondsPerOperation,
                protobufNetSerialize.NanosecondsPerOperation,
                "Warm WallstopProto serialization must be at least as fast as protobuf-net v3."
            );
            Assert.LessOrEqual(
                wallstopProtoDeserialize.NanosecondsPerOperation,
                protobufNetDeserialize.NanosecondsPerOperation,
                "Warm WallstopProto deserialization must be at least as fast as protobuf-net v3."
            );
#endif
        }

        private static Measurement Summarize(Measurement[] measurements)
        {
            long maximumAllocatedBytes = 0;
            double[] nanosecondsPerOperation = new double[measurements.Length];
            for (int index = 0; index < measurements.Length; index++)
            {
                maximumAllocatedBytes = Math.Max(
                    maximumAllocatedBytes,
                    measurements[index].AllocatedBytes
                );
                nanosecondsPerOperation[index] = measurements[index].NanosecondsPerOperation;
            }

            Array.Sort(nanosecondsPerOperation);
            return new Measurement(
                maximumAllocatedBytes,
                nanosecondsPerOperation[nanosecondsPerOperation.Length / 2]
            );
        }

        private static Measurement MeasureWallstopProtoSerialize(
            AllocationContract value,
            ref byte[] buffer
        )
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int iteration = 0; iteration < MeasuredIterations; iteration++)
            {
                WProtoWriteResult result = WProtoFacade.Serialize(value, ref buffer);
                if (!result.Served || result.Resized)
                {
                    Assert.Fail("The warmed serialization path stopped reusing its buffer.");
                }
            }

            long elapsed = Stopwatch.GetTimestamp() - started;
            return new Measurement(GC.GetAllocatedBytesForCurrentThread() - before, elapsed);
        }

        private static Measurement MeasureProtobufNetSerialize(
            AllocationContract value,
            MemoryStream destination
        )
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int iteration = 0; iteration < MeasuredIterations; iteration++)
            {
                destination.Position = 0;
                destination.SetLength(0);
                ProtoBuf.Serializer.Serialize(destination, value);
            }

            long elapsed = Stopwatch.GetTimestamp() - started;
            return new Measurement(GC.GetAllocatedBytesForCurrentThread() - before, elapsed);
        }

        private static Measurement MeasureWallstopProtoDeserialize(byte[] payload)
        {
            AllocationContract restored = null;
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int iteration = 0; iteration < MeasuredIterations; iteration++)
            {
                if (!WProtoFacade.TryDeserialize(payload, out restored))
                {
                    Assert.Fail("The representative contract stopped using WallstopProto.");
                }
            }

            long elapsed = Stopwatch.GetTimestamp() - started;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(restored);
            return new Measurement(allocated, elapsed);
        }

        private static Measurement MeasureProtobufNetDeserialize(MemoryStream source)
        {
            AllocationContract restored = null;
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int iteration = 0; iteration < MeasuredIterations; iteration++)
            {
                source.Position = 0;
                restored = ProtoBuf.Serializer.Deserialize<AllocationContract>(source);
            }

            long elapsed = Stopwatch.GetTimestamp() - started;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(restored);
            return new Measurement(allocated, elapsed);
        }

        private static AllocationContract CreateRepresentativeContract()
        {
            int[] values = new int[128];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = index * 17 + 3;
            }

            Dictionary<string, int> scores = new Dictionary<string, int>(32);
            for (int index = 0; index < 32; index++)
            {
                scores.Add($"score-{index:D2}", index * 23 + 11);
            }

            return new AllocationContract
            {
                Id = 42,
                Label = "allocation-baseline",
                Values = values,
                Scores = scores,
                Child = new AllocationChild { Sequence = 987_654_321L, Name = "nested" },
            };
        }

        private readonly struct Measurement
        {
            public Measurement(long allocatedBytes, long elapsedTimestampTicks)
            {
                AllocatedBytes = allocatedBytes;
                BytesPerOperation = allocatedBytes / (double)MeasuredIterations;
                NanosecondsPerOperation =
                    elapsedTimestampTicks
                    * (1_000_000_000d / Stopwatch.Frequency)
                    / MeasuredIterations;
            }

            public Measurement(long allocatedBytes, double nanosecondsPerOperation)
            {
                AllocatedBytes = allocatedBytes;
                BytesPerOperation = allocatedBytes / (double)MeasuredIterations;
                NanosecondsPerOperation = nanosecondsPerOperation;
            }

            public long AllocatedBytes { get; }

            public double BytesPerOperation { get; }

            public double NanosecondsPerOperation { get; }
        }

        [ProtoContract]
        [WProtoContract]
        internal sealed partial class AllocationContract
        {
            [ProtoMember(1)]
            [WProtoMember(1)]
            public int Id;

            [ProtoMember(2)]
            [WProtoMember(2)]
            public string Label;

            [ProtoMember(3)]
            [WProtoMember(3)]
            public int[] Values;

            [ProtoMember(4)]
            [WProtoMember(4)]
            public Dictionary<string, int> Scores;

            [ProtoMember(5)]
            [WProtoMember(5)]
            public AllocationChild Child;
        }

        [ProtoContract]
        [WProtoContract]
        internal sealed partial class AllocationChild
        {
            [ProtoMember(1)]
            [WProtoMember(1)]
            public long Sequence;

            [ProtoMember(2)]
            [WProtoMember(2)]
            public string Name;
        }
    }
}
