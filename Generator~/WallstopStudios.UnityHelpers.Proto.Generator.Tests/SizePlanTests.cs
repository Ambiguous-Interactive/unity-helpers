// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Diagnostics;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [TestFixture]
    public sealed class SizePlanTests
    {
        [Test]
        public void CapturedSizesFollowWritePreorderAcrossSiblings()
        {
            Node value = new Node
            {
                PayloadLength = 1,
                First = new Node
                {
                    PayloadLength = 127,
                    First = new Node { PayloadLength = 128 },
                },
                Second = new Node { PayloadLength = 16_384 },
            };
            NodeFormatter formatter = new NodeFormatter();

            using WProtoSizes.SizePlanScope scope = WProtoSizes.BeginSizePlan();
            _ = formatter.Measure(value);
            int[] plan = scope.Freeze().ToArray();

            CollectionAssert.AreEqual(
                new[]
                {
                    formatter.MeasurePayload(value.First),
                    formatter.MeasurePayload(value.First.First),
                    formatter.MeasurePayload(value.Second),
                },
                plan
            );
        }

        [TestCase(0, 16_384)]
        [TestCase(127, 128)]
        [TestCase(128, 127)]
        [TestCase(16_383, 16_384)]
        [TestCase(16_384, 16_383)]
        public void IncorrectPrefixWidthHintsStillProduceCanonicalBytes(
            int payloadLength,
            int plannedPayloadLength
        )
        {
            RawPayloadFormatter formatter = new RawPayloadFormatter();
            RawPayload value = new RawPayload(payloadLength);
            byte[] plannedBuffer = new byte[payloadLength + 32];
            int[] sizePlan = { plannedPayloadLength };
            WProtoWriter planned = new WProtoWriter(plannedBuffer, sizePlan);

            Assert.IsTrue(planned.TryWriteMessage(1, formatter, value));
            Assert.IsTrue(planned.TryWriteTag(2, WProtoWireType.Varint));
            Assert.IsTrue(planned.TryWriteInt32(99));

            byte[] baselineBuffer = new byte[payloadLength + 32];
            WProtoWriter baseline = new WProtoWriter(baselineBuffer);
            Assert.IsTrue(baseline.TryWriteMessage(1, formatter, value));
            Assert.IsTrue(baseline.TryWriteTag(2, WProtoWireType.Varint));
            Assert.IsTrue(baseline.TryWriteInt32(99));

            Assert.AreEqual(baseline.Position, planned.Position);
            CollectionAssert.AreEqual(baseline.Written.ToArray(), planned.Written.ToArray());
        }

        [Test]
        public void OversizedHintFallsBackInsideAnExactCanonicalBuffer()
        {
            RawPayloadFormatter formatter = new RawPayloadFormatter();
            RawPayload value = new RawPayload(2);
            byte[] buffer = new byte[4];
            int[] sizePlan = { 128 };
            WProtoWriter writer = new WProtoWriter(buffer, sizePlan);

            Assert.IsTrue(writer.TryWriteMessage(1, formatter, value));

            CollectionAssert.AreEqual(
                new byte[] { 0x0A, 0x02, 0x00, 0x00 },
                writer.Written.ToArray()
            );
        }

        [TestCase(0)]
        [TestCase(127)]
        [TestCase(128)]
        [TestCase(16_383)]
        [TestCase(16_384)]
        public void FacadeSizePlanMatchesDirectBackpatchingAtEveryPrefixBoundary(int payloadLength)
        {
            Node value = new Node
            {
                First = new Node { PayloadLength = payloadLength },
                Second = new Node { PayloadLength = 3 },
            };
            NodeFormatter formatter = new NodeFormatter();
            WProtoFormatterProvider.Register<Node>(formatter);
            try
            {
                int size = formatter.Measure(value);
                byte[] baselineBytes = new byte[size];
                WProtoWriter baseline = new WProtoWriter(baselineBytes);
                Assert.IsTrue(formatter.Write(ref baseline, value));
                Assert.AreEqual(size, baseline.Position);

                byte[] plannedBytes = null;
                WProtoWriteResult result = WProtoFacade.Serialize(value, ref plannedBytes);

                Assert.IsTrue(result.Served);
                Assert.AreEqual(size, result.Length);
                CollectionAssert.AreEqual(baselineBytes, plannedBytes);
            }
            finally
            {
                WProtoFormatterProvider.Register<Node>(null);
            }
        }

        [TestCase(128, 3)]
        [TestCase(16_384, 4)]
        public void FacadeReservesTheMeasuredPrefixBeforeWritingPayload(
            int payloadLength,
            int expectedPayloadStart
        )
        {
            ObservingPayloadFormatter payloadFormatter = new ObservingPayloadFormatter();
            EnvelopeFormatter envelopeFormatter = new EnvelopeFormatter(payloadFormatter);
            WProtoFormatterProvider.Register<Envelope>(envelopeFormatter);
            try
            {
                byte[] buffer = null;
                WProtoWriteResult result = WProtoFacade.Serialize(
                    new Envelope { Payload = new RawPayload(payloadLength) },
                    ref buffer
                );

                Assert.IsTrue(result.Served);
                Assert.AreEqual(expectedPayloadStart, payloadFormatter.PayloadStart);
            }
            finally
            {
                WProtoFormatterProvider.Register<Envelope>(null);
            }
        }

        [Test]
        public void ReentrantFacadeCallsPreserveBothSizePlans()
        {
            NodeFormatter nodeFormatter = new NodeFormatter();
            ReentrantFormatter outerFormatter = new ReentrantFormatter(nodeFormatter);
            Node inner = new Node
            {
                First = new Node { PayloadLength = 128 },
                Second = new Node { PayloadLength = 16_384 },
            };
            ReentrantContract value = new ReentrantContract
            {
                Inner = inner,
                Value = new Node { First = new Node { PayloadLength = 16_384 } },
            };
            WProtoFormatterProvider.Register<Node>(nodeFormatter);
            WProtoFormatterProvider.Register<ReentrantContract>(outerFormatter);
            try
            {
                byte[] plannedBytes = null;
                WProtoWriteResult result = WProtoFacade.Serialize(value, ref plannedBytes);

                int size = outerFormatter.Measure(value);
                byte[] baselineBytes = new byte[size];
                WProtoWriter baseline = new WProtoWriter(baselineBytes);
                Assert.IsTrue(outerFormatter.Write(ref baseline, value));

                Assert.IsTrue(result.Served);
                Assert.AreEqual(size, result.Length);
                CollectionAssert.AreEqual(baselineBytes, plannedBytes);
            }
            finally
            {
                WProtoFormatterProvider.Register<ReentrantContract>(null);
                WProtoFormatterProvider.Register<Node>(null);
            }
        }

        [Test]
        public void FailedMeasurementDoesNotPoisonTheNextSizePlan()
        {
            NodeFormatter nodeFormatter = new NodeFormatter();
            ThrowOnceFormatter formatter = new ThrowOnceFormatter(nodeFormatter);
            ThrowOnceContract value = new ThrowOnceContract
            {
                Value = new Node { First = new Node { PayloadLength = 128 } },
            };
            WProtoFormatterProvider.Register<ThrowOnceContract>(formatter);
            try
            {
                byte[] buffer = null;
                Assert.Throws<InvalidOperationException>(() =>
                    WProtoFacade.Serialize(value, ref buffer)
                );

                WProtoWriteResult result = WProtoFacade.Serialize(value, ref buffer);

                Assert.IsTrue(result.Served);
                Assert.AreEqual(formatter.Measure(value), result.Length);
            }
            finally
            {
                WProtoFormatterProvider.Register<ThrowOnceContract>(null);
            }
        }

        [Test]
        [Category("Performance")]
        public void PlannedPrefixesAvoidTheDeepLargePayloadBackpatchCost()
        {
            const int iterations = 100;
            BulkNodeFormatter formatter = new BulkNodeFormatter();
            BulkNode value = new BulkNode
            {
                Child = new BulkNode
                {
                    Child = new BulkNode
                    {
                        Child = new BulkNode { Payload = new byte[256 * 1024] },
                    },
                },
            };
            WProtoFormatterProvider.Register<BulkNode>(formatter);
            try
            {
                int size = formatter.Measure(value);
                byte[] plannedBuffer = new byte[size];
                byte[] baselineBuffer = new byte[size];

                for (int iteration = 0; iteration < 100; iteration++)
                {
                    _ = WProtoFacade.Serialize(value, ref plannedBuffer);
                    _ = formatter.Measure(value);
                    WProtoWriter warmBaseline = new WProtoWriter(baselineBuffer);
                    Assert.IsTrue(formatter.Write(ref warmBaseline, value));
                }

                long plannedAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long plannedTicks = MeasurePlanned(value, ref plannedBuffer, iterations);
                long plannedAllocated =
                    GC.GetAllocatedBytesForCurrentThread() - plannedAllocatedBefore;

                long baselineAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long baselineTicks = MeasureBackpatched(
                    formatter,
                    value,
                    baselineBuffer,
                    iterations
                );
                long baselineAllocated =
                    GC.GetAllocatedBytesForCurrentThread() - baselineAllocatedBefore;

                TestContext.WriteLine(
                    $"Depth-3 256 KiB nested payload, {iterations} operations: "
                        + $"planned {ToNanoseconds(plannedTicks, iterations):N0} ns/op, "
                        + $"backpatched {ToNanoseconds(baselineTicks, iterations):N0} ns/op."
                );
                Assert.AreEqual(0, plannedAllocated, "The warmed size plan must allocate 0 B/op.");
                Assert.AreEqual(0, baselineAllocated, "The direct baseline must allocate 0 B/op.");
                // Timing is evidence, not a gate: shared runners occasionally interrupt either
                // half. The observed payload-start test above deterministically proves that the
                // planned path skips all three moves; this comparison records their current cost.
                CollectionAssert.AreEqual(baselineBuffer, plannedBuffer);
            }
            finally
            {
                WProtoFormatterProvider.Register<BulkNode>(null);
            }
        }

        private static long MeasurePlanned(BulkNode value, ref byte[] buffer, int iterations)
        {
            long started = Stopwatch.GetTimestamp();
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                WProtoWriteResult result = WProtoFacade.Serialize(value, ref buffer);
                if (!result.Served || result.Resized)
                {
                    Assert.Fail("The warmed planned writer stopped reusing its buffer.");
                }
            }

            return Stopwatch.GetTimestamp() - started;
        }

        private static long MeasureBackpatched(
            BulkNodeFormatter formatter,
            BulkNode value,
            byte[] buffer,
            int iterations
        )
        {
            long started = Stopwatch.GetTimestamp();
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                _ = formatter.Measure(value);
                WProtoWriter writer = new WProtoWriter(buffer);
                if (!formatter.Write(ref writer, value))
                {
                    Assert.Fail("The direct backpatch baseline refused the measured value.");
                }
            }

            return Stopwatch.GetTimestamp() - started;
        }

        private static double ToNanoseconds(long ticks, int iterations)
        {
            return ticks * (1_000_000_000d / Stopwatch.Frequency) / iterations;
        }

        private sealed class BulkNode
        {
            internal byte[] Payload { get; set; }
            internal BulkNode Child { get; set; }
        }

        private sealed class BulkNodeFormatter : IWProtoFormatter<BulkNode>
        {
            public int Measure(in BulkNode value)
            {
                int size = value.Payload?.Length ?? 0;
                if (value.Child != null)
                {
                    size += WProtoSizes.TagSize(1);
                    size += WProtoSizes.MessageSize(this, value.Child);
                }

                return size;
            }

            public bool Write(ref WProtoWriter writer, in BulkNode value)
            {
                if (value.Payload != null && !writer.TryWriteRaw(value.Payload))
                {
                    return false;
                }

                return value.Child == null || writer.TryWriteMessage(1, this, value.Child);
            }

            public bool TryRead(ref WProtoReader reader, out BulkNode value)
            {
                value = null;
                return false;
            }
        }

        private sealed class ObservingPayloadFormatter : IWProtoFormatter<RawPayload>
        {
            internal int PayloadStart { get; private set; }

            public int Measure(in RawPayload value)
            {
                return value.Length;
            }

            public bool Write(ref WProtoWriter writer, in RawPayload value)
            {
                PayloadStart = writer.Position;
                for (int index = 0; index < value.Length; index++)
                {
                    if (!writer.TryWriteVarint32(0))
                    {
                        return false;
                    }
                }

                return true;
            }

            public bool TryRead(ref WProtoReader reader, out RawPayload value)
            {
                value = null;
                return false;
            }
        }

        private sealed class Envelope
        {
            internal RawPayload Payload { get; set; }
        }

        private sealed class EnvelopeFormatter : IWProtoFormatter<Envelope>
        {
            private readonly ObservingPayloadFormatter _payloadFormatter;

            internal EnvelopeFormatter(ObservingPayloadFormatter payloadFormatter)
            {
                _payloadFormatter = payloadFormatter;
            }

            public int Measure(in Envelope value)
            {
                return WProtoSizes.TagSize(1)
                    + WProtoSizes.MessageSize(_payloadFormatter, value.Payload);
            }

            public bool Write(ref WProtoWriter writer, in Envelope value)
            {
                return writer.TryWriteMessage(1, _payloadFormatter, value.Payload);
            }

            public bool TryRead(ref WProtoReader reader, out Envelope value)
            {
                value = null;
                return false;
            }
        }

        private sealed class Node
        {
            internal int PayloadLength { get; set; }
            internal Node First { get; set; }
            internal Node Second { get; set; }
        }

        private sealed class NodeFormatter : IWProtoFormatter<Node>
        {
            public int Measure(in Node value)
            {
                return MeasurePayload(value);
            }

            internal int MeasurePayload(Node value)
            {
                int size = value.PayloadLength;
                if (value.First != null)
                {
                    size += WProtoSizes.TagSize(1);
                    size += WProtoSizes.MessageSize(this, value.First);
                }

                if (value.Second != null)
                {
                    size += WProtoSizes.TagSize(2);
                    size += WProtoSizes.MessageSize(this, value.Second);
                }

                return size;
            }

            public bool Write(ref WProtoWriter writer, in Node value)
            {
                for (int index = 0; index < value.PayloadLength; index++)
                {
                    if (!writer.TryWriteVarint32(0))
                    {
                        return false;
                    }
                }

                if (value.First != null && !writer.TryWriteMessage(1, this, value.First))
                {
                    return false;
                }

                return value.Second == null || writer.TryWriteMessage(2, this, value.Second);
            }

            public bool TryRead(ref WProtoReader reader, out Node value)
            {
                value = null;
                return false;
            }
        }

        private sealed class ReentrantContract
        {
            internal Node Inner { get; set; }
            internal Node Value { get; set; }
        }

        private sealed class ReentrantFormatter : IWProtoFormatter<ReentrantContract>
        {
            private readonly NodeFormatter _nodeFormatter;

            internal ReentrantFormatter(NodeFormatter nodeFormatter)
            {
                _nodeFormatter = nodeFormatter;
            }

            public int Measure(in ReentrantContract value)
            {
                SerializeInner(value.Inner);
                return WProtoSizes.TagSize(1)
                    + WProtoSizes.MessageSize(_nodeFormatter, value.Value);
            }

            public bool Write(ref WProtoWriter writer, in ReentrantContract value)
            {
                SerializeInner(value.Inner);
                return writer.TryWriteMessage(1, _nodeFormatter, value.Value);
            }

            public bool TryRead(ref WProtoReader reader, out ReentrantContract value)
            {
                value = null;
                return false;
            }

            private static void SerializeInner(Node value)
            {
                byte[] buffer = null;
                WProtoWriteResult result = WProtoFacade.Serialize(value, ref buffer);
                if (!result.Served)
                {
                    throw new InvalidOperationException("The reentrant serialization was refused.");
                }
            }
        }

        private sealed class ThrowOnceContract
        {
            internal Node Value { get; set; }
        }

        private sealed class ThrowOnceFormatter : IWProtoFormatter<ThrowOnceContract>
        {
            private readonly NodeFormatter _nodeFormatter;
            private bool _throw = true;

            internal ThrowOnceFormatter(NodeFormatter nodeFormatter)
            {
                _nodeFormatter = nodeFormatter;
            }

            public int Measure(in ThrowOnceContract value)
            {
                int size =
                    WProtoSizes.TagSize(1) + WProtoSizes.MessageSize(_nodeFormatter, value.Value);
                if (_throw)
                {
                    _throw = false;
                    throw new InvalidOperationException("Deliberate measurement failure.");
                }

                return size;
            }

            public bool Write(ref WProtoWriter writer, in ThrowOnceContract value)
            {
                return writer.TryWriteMessage(1, _nodeFormatter, value.Value);
            }

            public bool TryRead(ref WProtoReader reader, out ThrowOnceContract value)
            {
                value = null;
                return false;
            }
        }

        private sealed class RawPayload
        {
            internal RawPayload(int length)
            {
                Length = length;
            }

            internal int Length { get; }
        }

        private sealed class RawPayloadFormatter : IWProtoFormatter<RawPayload>
        {
            public int Measure(in RawPayload value)
            {
                return value.Length;
            }

            public bool Write(ref WProtoWriter writer, in RawPayload value)
            {
                for (int index = 0; index < value.Length; index++)
                {
                    if (!writer.TryWriteVarint32(0))
                    {
                        return false;
                    }
                }

                return true;
            }

            public bool TryRead(ref WProtoReader reader, out RawPayload value)
            {
                value = null;
                return false;
            }
        }
    }
}
