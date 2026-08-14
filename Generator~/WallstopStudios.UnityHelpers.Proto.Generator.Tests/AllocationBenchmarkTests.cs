// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
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
        private const int ShapeIterations = 2_000;

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
                $"Allocation/throughput baseline: {MeasurementRounds} alternating rounds, {MeasuredIterations:N0} operations each (median, fastest):\n"
                    + $"  WallstopProto serialize: {wallstopProtoSerialize.BytesPerOperation:N2} B/op, {wallstopProtoSerialize.NanosecondsPerOperation:N2} ns/op, {wallstopProtoSerialize.FastestNanosecondsPerOperation:N2} ns/op fastest\n"
                    + $"  protobuf-net serialize: {protobufNetSerialize.BytesPerOperation:N2} B/op, {protobufNetSerialize.NanosecondsPerOperation:N2} ns/op, {protobufNetSerialize.FastestNanosecondsPerOperation:N2} ns/op fastest\n"
                    + $"  WallstopProto deserialize: {wallstopProtoDeserialize.BytesPerOperation:N2} B/op, {wallstopProtoDeserialize.NanosecondsPerOperation:N2} ns/op, {wallstopProtoDeserialize.FastestNanosecondsPerOperation:N2} ns/op fastest\n"
                    + $"  protobuf-net deserialize: {protobufNetDeserialize.BytesPerOperation:N2} B/op, {protobufNetDeserialize.NanosecondsPerOperation:N2} ns/op, {protobufNetDeserialize.FastestNanosecondsPerOperation:N2} ns/op fastest."
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
            // The oracle rather than a constant, because both implementations return the same object
            // graph from the same contract: whatever protobuf-net allocates is the graph, and
            // anything above it is overhead this package chose. A hand-written ceiling would also
            // have to be re-tuned whenever a runtime changes what a Dictionary costs.
            Assert.LessOrEqual(
                wallstopProtoDeserialize.BytesPerOperation,
                protobufNetDeserialize.BytesPerOperation,
                "Deserializing the representative contract must not allocate more than protobuf-net "
                    + "does for the same object graph."
            );
#if !PROTOBUF_NET_ORACLE_V2
            // The FASTEST round on each side, not the median. Noise on a shared runner only ever
            // adds time, so the minimum is the closest either implementation gets to its own cost,
            // and comparing minima is what makes this a claim about the code rather than about the
            // machine. Measured on a hosted runner: one descheduled round reported this write path
            // at 17,710 ns/op against a local 800, which reddened a pull request whose allocation
            // numbers were identical. A real regression is present in every round and still fails.
            Assert.LessOrEqual(
                wallstopProtoSerialize.FastestNanosecondsPerOperation,
                protobufNetSerialize.FastestNanosecondsPerOperation,
                "Warm WallstopProto serialization must be at least as fast as protobuf-net v3."
            );
            Assert.LessOrEqual(
                wallstopProtoDeserialize.FastestNanosecondsPerOperation,
                protobufNetDeserialize.FastestNanosecondsPerOperation,
                "Warm WallstopProto deserialization must be at least as fast as protobuf-net v3."
            );
#endif
        }

        /// <summary>
        /// Attributes read allocation to the member shape that causes it, against the oracle
        /// decoding the same graph.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The aggregate above says WallstopProto reads allocate more than protobuf-net without
        /// saying which member is responsible, and "reduce read allocation" cannot be gated on a
        /// number that mixes five shapes together. Each contract here holds exactly one member, so
        /// the object graph both serializers must produce is identical and the difference between
        /// them is the serializer's own overhead rather than the payload's.
        /// </para>
        /// <para>
        /// The bar is the oracle, not a hand-written constant: a decoded value both implementations
        /// have to materialize is not overhead, so allocating more than protobuf-net does for the
        /// same result is the definition of a read this package pays for and could stop paying for.
        /// </para>
        /// </remarks>
        [Test]
        public void NoMemberShapeReadsAllocateMoreThanProtobufNet()
        {
            ShapeCase[] cases =
            {
                new ShapeCase<ArrayShape>("int[128]", ArrayShape.Representative()),
                new ShapeCase<ListShape>("List<int>[128]", ListShape.Representative()),
                new ShapeCase<StringShape>("string", StringShape.Representative()),
                new ShapeCase<MapShape>("Dictionary<string, int>[32]", MapShape.Representative()),
                new ShapeCase<NestedShape>("nested contract", NestedShape.Representative()),
            };

            StringBuilder report = new StringBuilder("Read allocation by member shape, ");
            report
                .Append(ShapeIterations.ToString("N0"))
                .AppendLine(" operations each:")
                .AppendLine("  shape                        WallstopProto    protobuf-net");
            List<string> regressions = new List<string>();
            foreach (ShapeCase shape in cases)
            {
                ShapeComparison comparison = shape.Measure();
                report.AppendLine(
                    $"  {shape.Name, -28}{comparison.WallstopProtoBytesPerOperation, 10:N2} B/op{comparison.ProtobufNetBytesPerOperation, 12:N2} B/op"
                );
                if (
                    comparison.ProtobufNetBytesPerOperation
                    < comparison.WallstopProtoBytesPerOperation
                )
                {
                    regressions.Add(
                        $"{shape.Name}: {comparison.WallstopProtoBytesPerOperation:N2} B/op against the oracle's {comparison.ProtobufNetBytesPerOperation:N2} B/op"
                    );
                }
            }

            TestContext.WriteLine(report.ToString());
            Assert.IsEmpty(
                regressions,
                "Decoding one member into the same object graph allocated more than protobuf-net: "
                    + string.Join("; ", regressions)
            );
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
                nanosecondsPerOperation[nanosecondsPerOperation.Length / 2],
                nanosecondsPerOperation[0]
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
                FastestNanosecondsPerOperation = NanosecondsPerOperation;
            }

            public Measurement(
                long allocatedBytes,
                double nanosecondsPerOperation,
                double fastestNanosecondsPerOperation
            )
            {
                AllocatedBytes = allocatedBytes;
                BytesPerOperation = allocatedBytes / (double)MeasuredIterations;
                NanosecondsPerOperation = nanosecondsPerOperation;
                FastestNanosecondsPerOperation = fastestNanosecondsPerOperation;
            }

            public long AllocatedBytes { get; }

            public double BytesPerOperation { get; }

            public double NanosecondsPerOperation { get; }

            /// <summary>The fastest round, which is the one least contaminated by the machine.</summary>
            public double FastestNanosecondsPerOperation { get; }
        }

        /// <summary>One member shape, measured on both serializers.</summary>
        /// <remarks>
        /// Abstract so the fixture can iterate shapes of different contract types in one loop
        /// instead of repeating the same twelve lines per shape.
        /// </remarks>
        private abstract class ShapeCase
        {
            protected ShapeCase(string name)
            {
                Name = name;
            }

            internal string Name { get; }

            internal abstract ShapeComparison Measure();
        }

        private sealed class ShapeCase<T> : ShapeCase
            where T : class
        {
            private readonly T _value;

            internal ShapeCase(string name, T value)
                : base(name)
            {
                _value = value;
            }

            /// <summary>
            /// Measures both readers on the payload each writer produces.
            /// </summary>
            /// <returns>The comparison.</returns>
            /// <remarks>
            /// Each serializer reads its own bytes, because that is what a consumer's save file
            /// holds and because the two forms are not the same bytes: repeated scalars are written
            /// packed here and unpacked by protobuf-net, by the encoding policy. The decoded graph
            /// is identical either way, which is what makes the two allocation figures comparable.
            /// </remarks>
            internal override ShapeComparison Measure()
            {
                byte[] wallstopProtoPayload = null;
                WProtoWriteResult written = WProtoFacade.Serialize(
                    _value,
                    ref wallstopProtoPayload
                );
                Assert.IsTrue(written.Served, "The shape contract is not served by WallstopProto.");
                byte[] exact = new byte[written.Length];
                Array.Copy(wallstopProtoPayload, exact, written.Length);
                using MemoryStream protobufNetPayload = new MemoryStream();
                ProtoBuf.Serializer.Serialize(protobufNetPayload, _value);

                for (int iteration = 0; iteration < WarmupIterations; iteration++)
                {
                    Assert.IsTrue(WProtoFacade.TryDeserialize(exact, out T _));
                    protobufNetPayload.Position = 0;
                    _ = ProtoBuf.Serializer.Deserialize<T>(protobufNetPayload);
                }

                T restored = null;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int iteration = 0; iteration < ShapeIterations; iteration++)
                {
                    if (!WProtoFacade.TryDeserialize(exact, out restored))
                    {
                        Assert.Fail("The shape contract stopped using WallstopProto.");
                    }
                }

                long wallstopProtoBytes = GC.GetAllocatedBytesForCurrentThread() - before;
                before = GC.GetAllocatedBytesForCurrentThread();
                for (int iteration = 0; iteration < ShapeIterations; iteration++)
                {
                    protobufNetPayload.Position = 0;
                    restored = ProtoBuf.Serializer.Deserialize<T>(protobufNetPayload);
                }

                long protobufNetBytes = GC.GetAllocatedBytesForCurrentThread() - before;
                GC.KeepAlive(restored);
                return new ShapeComparison(
                    wallstopProtoBytes / (double)ShapeIterations,
                    protobufNetBytes / (double)ShapeIterations
                );
            }
        }

        private readonly struct ShapeComparison
        {
            internal ShapeComparison(
                double wallstopProtoBytesPerOperation,
                double protobufNetBytesPerOperation
            )
            {
                WallstopProtoBytesPerOperation = wallstopProtoBytesPerOperation;
                ProtobufNetBytesPerOperation = protobufNetBytesPerOperation;
            }

            internal double WallstopProtoBytesPerOperation { get; }

            internal double ProtobufNetBytesPerOperation { get; }
        }

        [ProtoContract]
        [WProtoContract]
        internal sealed partial class ArrayShape
        {
            [ProtoMember(1)]
            [WProtoMember(1)]
            public int[] Values;

            internal static ArrayShape Representative()
            {
                int[] values = new int[128];
                for (int index = 0; index < values.Length; index++)
                {
                    values[index] = index * 17 + 3;
                }

                return new ArrayShape { Values = values };
            }
        }

        [ProtoContract]
        [WProtoContract]
        internal sealed partial class ListShape
        {
            [ProtoMember(1)]
            [WProtoMember(1)]
            public List<int> Values;

            internal static ListShape Representative()
            {
                List<int> values = new List<int>(128);
                for (int index = 0; index < 128; index++)
                {
                    values.Add(index * 17 + 3);
                }

                return new ListShape { Values = values };
            }
        }

        [ProtoContract]
        [WProtoContract]
        internal sealed partial class StringShape
        {
            [ProtoMember(1)]
            [WProtoMember(1)]
            public string Text;

            internal static StringShape Representative()
            {
                return new StringShape { Text = "allocation-baseline" };
            }
        }

        [ProtoContract]
        [WProtoContract]
        internal sealed partial class MapShape
        {
            [ProtoMember(1)]
            [WProtoMember(1)]
            public Dictionary<string, int> Scores;

            internal static MapShape Representative()
            {
                Dictionary<string, int> scores = new Dictionary<string, int>(32);
                for (int index = 0; index < 32; index++)
                {
                    scores.Add($"score-{index:D2}", index * 23 + 11);
                }

                return new MapShape { Scores = scores };
            }
        }

        [ProtoContract]
        [WProtoContract]
        internal sealed partial class NestedShape
        {
            [ProtoMember(1)]
            [WProtoMember(1)]
            public AllocationChild Child;

            internal static NestedShape Representative()
            {
                return new NestedShape
                {
                    Child = new AllocationChild { Sequence = 987_654_321L, Name = "nested" },
                };
            }
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
