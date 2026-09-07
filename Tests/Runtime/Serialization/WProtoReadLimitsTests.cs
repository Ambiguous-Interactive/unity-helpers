// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Verifies wire limits before consuming or decoding attacker-controlled regions.</summary>
    [TestFixture]
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class WProtoReadLimitsTests
    {
        [Test]
        public void ParameterlessConstructionSupportsGenericFactories()
        {
            WProtoReadLimits limits = CreateDefault<WProtoReadLimits>();
            Assert.AreEqual(int.MaxValue, limits.MaximumMessageBytes);
            Assert.AreEqual(int.MaxValue, limits.MaximumLengthDelimitedBytes);
            Assert.AreEqual(int.MaxValue, limits.MaximumFieldCount);
            Assert.AreEqual(WProtoReader.MaxNestingDepth, limits.MaximumNestingDepth);
        }

        [TestCase(-1, false)]
        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, true)]
        [TestCase(int.MaxValue, true)]
        public void MessageSizeLimitIsInclusiveAndRefusesBeforeConsumption(int limit, bool accepted)
        {
            byte[] payload = { 8, 1 };
            WProtoReader reader = new WProtoReader(
                payload,
                new WProtoReadLimits(maximumMessageBytes: limit)
            );
            Assert.AreEqual(!accepted, reader.Malformed);
            Assert.AreEqual(0, reader.Position);
            Assert.AreEqual(accepted, reader.TryReadTag(out int tag, out int wire));
            Assert.AreEqual(accepted ? 1 : 0, tag);
            Assert.AreEqual(accepted ? WProtoWireType.Varint : -1, wire);
        }

        [Test]
        public void ZeroLimitsPermitAnEmptyRoot()
        {
            WProtoReader reader = new WProtoReader(default, new WProtoReadLimits(0, 0, 0, 0));
            Assert.IsFalse(reader.TryReadTag(out _, out _));
            Assert.IsFalse(reader.Malformed);
            Assert.IsTrue(reader.End);
        }

        [TestCase(nameof(WProtoReader.TryReadBytes), 1, false)]
        [TestCase(nameof(WProtoReader.TryReadBytes), 2, true)]
        [TestCase(nameof(WProtoReader.TryReadString), 1, false)]
        [TestCase(nameof(WProtoReader.TryReadString), 2, true)]
        [TestCase(nameof(WProtoReader.TryReadMessage), 1, false)]
        [TestCase(nameof(WProtoReader.TryReadMessage), 2, true)]
        [TestCase(nameof(WProtoReader.TryReadPackedRun), 1, false)]
        [TestCase(nameof(WProtoReader.TryReadPackedRun), 2, true)]
        [TestCase(nameof(WProtoReader.TrySkipField), 1, false)]
        [TestCase(nameof(WProtoReader.TrySkipField), 2, true)]
        public void LengthLimitCoversEveryDelimitedRead(string operation, int limit, bool accepted)
        {
            byte[] payload = { 2, 8, 1 };
            WProtoReader reader = new WProtoReader(
                payload,
                new WProtoReadLimits(maximumLengthDelimitedBytes: limit)
            );
            bool actual;
            switch (operation)
            {
                case nameof(WProtoReader.TryReadBytes):
                    actual = reader.TryReadBytes(out ReadOnlySpan<byte> bytes);
                    Assert.AreEqual(accepted ? 2 : 0, bytes.Length);
                    break;
                case nameof(WProtoReader.TryReadString):
                    actual = reader.TryReadString(out string text);
                    Assert.AreEqual(accepted ? "\b\u0001" : null, text);
                    break;
                case nameof(WProtoReader.TryReadMessage):
                    actual = reader.TryReadMessage(out WProtoReader nested);
                    Assert.AreEqual(!accepted, nested.Malformed);
                    break;
                case nameof(WProtoReader.TryReadPackedRun):
                    actual = reader.TryReadPackedRun(out WProtoReader packed);
                    Assert.AreEqual(!accepted, packed.Malformed);
                    break;
                default:
                    actual = reader.TrySkipField(1, WProtoWireType.LengthDelimited);
                    break;
            }

            Assert.AreEqual(accepted, actual);
            Assert.AreEqual(!accepted, reader.Malformed);
            Assert.AreEqual(accepted ? payload.Length : 1, reader.Position);
            if (!accepted)
            {
                Assert.IsFalse(reader.TryReadRemaining(out ReadOnlySpan<byte> remaining));
                Assert.IsTrue(remaining.IsEmpty);
                Assert.AreEqual(1, reader.Position);
            }
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void DuplicateAndUnknownTagsSpendTheSameFieldBudget(int configured)
        {
            byte[] payload = { 8, 1, 8, 2, 16, 3 };
            int limit = configured < 0 ? 0 : configured;
            WProtoReader reader = new WProtoReader(
                payload,
                new WProtoReadLimits(maximumFieldCount: configured)
            );
            int count = 0;
            while (reader.TryReadTag(out int tag, out int wire))
            {
                Assert.IsTrue(reader.TrySkipField(tag, wire));
                count++;
            }

            Assert.AreEqual(limit, count);
            Assert.AreEqual(limit * 2, reader.Position);
            Assert.IsTrue(reader.Malformed);
        }

        [TestCase(1)]
        [TestCase(64)]
        [TestCase(1024)]
        public void DuplicateStormStopsAtTheConfiguredWorkBound(int limit)
        {
            byte[] payload = new byte[20000];
            for (int index = 0; index < payload.Length; index += 2)
            {
                payload[index] = 8;
                payload[index + 1] = 1;
            }

            WProtoReader reader = new WProtoReader(
                payload,
                new WProtoReadLimits(maximumFieldCount: limit)
            );
            int reads = 0;
            while (reader.TryReadTag(out int tag, out int wire))
            {
                Assert.IsTrue(reader.TrySkipField(tag, wire));
                reads++;
            }

            Assert.AreEqual(limit, reads);
            Assert.AreEqual(limit * 2, reader.Position);
            Assert.IsTrue(reader.Malformed);
        }

        [Test]
        public void ZeroLengthFieldsAreAcceptedAtTheZeroLengthLimit()
        {
            WProtoReader reader = new WProtoReader(
                new byte[] { 0 },
                new WProtoReadLimits(maximumLengthDelimitedBytes: 0)
            );
            Assert.IsTrue(reader.TryReadString(out string value));
            Assert.AreEqual(string.Empty, value);
            Assert.IsFalse(reader.Malformed);
            Assert.IsTrue(reader.End);
        }

        [Test]
        public void NullLimitsPreserveDefaultReaderBehavior()
        {
            WProtoReader reader = new WProtoReader(new byte[] { 8, 1 }, (WProtoReadLimits)null);
            Assert.IsTrue(reader.TryReadTag(out int tag, out int wire));
            Assert.IsTrue(reader.TrySkipField(tag, wire));
            Assert.IsFalse(reader.Malformed);
            Assert.IsTrue(reader.End);
        }

        [Test]
        public void FieldBudgetAtExactEndIsNotMalformed()
        {
            byte[] payload = { 8, 1 };
            WProtoReader reader = new WProtoReader(
                payload,
                new WProtoReadLimits(maximumFieldCount: 1)
            );
            Assert.IsTrue(reader.TryReadTag(out int tag, out int wire));
            Assert.IsTrue(reader.TrySkipField(tag, wire));
            Assert.IsFalse(reader.TryReadTag(out _, out _));
            Assert.IsFalse(reader.Malformed);
        }

        [TestCase(2, false)]
        [TestCase(3, true)]
        public void SkippedGroupsIncludeTheirTagsAndTerminators(int limit, bool accepted)
        {
            byte[] payload = { 11, 16, 1, 12 };
            WProtoReader reader = new WProtoReader(
                payload,
                new WProtoReadLimits(maximumFieldCount: limit)
            );
            Assert.IsTrue(reader.TryReadTag(out int tag, out int wire));
            Assert.AreEqual(accepted, reader.TrySkipField(tag, wire));
            Assert.AreEqual(!accepted, reader.Malformed);
        }

        [TestCase(0, false)]
        [TestCase(1, true)]
        public void GroupDepthUsesTheConfiguredLimit(int depth, bool accepted)
        {
            byte[] payload = { 11, 12 };
            WProtoReader reader = new WProtoReader(
                payload,
                new WProtoReadLimits(maximumNestingDepth: depth)
            );
            Assert.IsTrue(reader.TryReadTag(out int tag, out int wire));
            Assert.AreEqual(accepted, reader.TrySkipField(tag, wire));
            Assert.AreEqual(!accepted, reader.Malformed);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(4)]
        public void DetachedAndNestedReadersPreserveDepthLimits(int depth)
        {
            WProtoReader reader = new WProtoReader(
                default,
                new WProtoReadLimits(maximumNestingDepth: depth)
            );
            AssertDepthLimit(ref reader, depth);
        }

        private static void AssertDepthLimit(ref WProtoReader reader, int depth)
        {
            if (reader.Depth < depth)
            {
                WProtoReader child = new WProtoReader(default, in reader);
                Assert.IsFalse(child.Malformed);
                AssertDepthLimit(ref child, depth);
                return;
            }

            Assert.AreEqual(depth, reader.Depth);
            WProtoReader refused = new WProtoReader(default, in reader);
            Assert.IsTrue(refused.Malformed);
            Assert.IsFalse(reader.TryReadMessage(out WProtoReader nested));
            Assert.IsTrue(reader.Malformed);
            Assert.IsTrue(nested.Malformed);
        }

        [Test]
        public void NestedReadersHaveIndependentFieldCountsWithInheritedLimits()
        {
            byte[] payload = { 10, 4, 8, 1, 8, 2 };
            WProtoReader reader = new WProtoReader(
                payload,
                new WProtoReadLimits(maximumFieldCount: 1)
            );
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadMessage(out WProtoReader nested));
            Assert.IsTrue(nested.TryReadTag(out _, out _));
            Assert.IsTrue(nested.TryReadInt32(out _));
            Assert.IsFalse(nested.TryReadTag(out _, out _));
            Assert.IsTrue(nested.Malformed);
            Assert.AreEqual(2, nested.Position);
        }

        [Test]
        public void PackedRunsDoNotSpendDepthOrTreatElementsAsTags()
        {
            byte[] payload = { 10, 2, 1, 2 };
            WProtoReader reader = new WProtoReader(
                payload,
                new WProtoReadLimits(maximumFieldCount: 1, maximumNestingDepth: 0)
            );
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadPackedRun(out WProtoReader packed));
            Assert.AreEqual(0, packed.Depth);
            Assert.AreEqual(2, packed.CountPackedElements(WProtoWireType.Varint));
            Assert.IsTrue(packed.TryReadInt32(out _));
            Assert.IsTrue(packed.TryReadInt32(out _));
            Assert.IsTrue(packed.End);
            Assert.IsFalse(packed.Malformed);
        }

        [Test]
        public void DetachedOversizedPayloadNeverReachesTheFormatter()
        {
            WProtoReader reader = new WProtoReader(
                default,
                new WProtoReadLimits(maximumMessageBytes: 1)
            );
            CountingFormatter formatter = new CountingFormatter();
            byte[] payload = { 8, 1 };
            Assert.IsFalse(reader.TryReadMessage(payload, formatter, out int value));
            Assert.AreEqual(0, value);
            Assert.AreEqual(0, formatter.Reads);
            Assert.IsTrue(reader.Malformed);
        }

        [Test]
        public void FormatterCannotReplaceTheRootReaderToDiscardLimits()
        {
            IWProtoFormatter<LimitMarker> original = WProtoFormatterProvider.TryGet(
                out IWProtoFormatter<LimitMarker> registered
            )
                ? registered
                : null;
            try
            {
                WProtoFormatterProvider.Register<LimitMarker>(new ResettingFormatter());
                Assert.Throws<InvalidOperationException>(() =>
                    WProtoFacade.TryDeserialize(
                        ReadOnlySpan<byte>.Empty,
                        new WProtoReadLimits(maximumFieldCount: 1),
                        out LimitMarker _
                    )
                );
            }
            finally
            {
                WProtoFormatterProvider.Register(original);
            }
        }

        [Test]
        public void FacadeRejectsAnOversizedRootBeforeCallingTheFormatter()
        {
            IWProtoFormatter<LimitMarker> original = WProtoFormatterProvider.TryGet(
                out IWProtoFormatter<LimitMarker> registered
            )
                ? registered
                : null;
            try
            {
                MarkerFormatter formatter = new MarkerFormatter();
                WProtoFormatterProvider.Register<LimitMarker>(formatter);
                Assert.Throws<InvalidOperationException>(() =>
                    WProtoFacade.TryDeserialize(
                        new byte[] { 8, 1 },
                        new WProtoReadLimits(maximumMessageBytes: 1),
                        out LimitMarker _
                    )
                );
                Assert.AreEqual(0, formatter.Reads);
            }
            finally
            {
                WProtoFormatterProvider.Register(original);
            }
        }

        [TestCase(0, false)]
        [TestCase(1, true)]
        public void FacadePropagatesFieldLimitsAndKeepsItsRoutingContract(int limit, bool accepted)
        {
            IWProtoFormatter<LimitMarker> original = WProtoFormatterProvider.TryGet(
                out IWProtoFormatter<LimitMarker> registered
            )
                ? registered
                : null;
            try
            {
                WProtoFormatterProvider.Register<LimitMarker>(new MarkerFormatter());
                byte[] payload = { 8, 1 };
                WProtoReadLimits limits = new WProtoReadLimits(maximumFieldCount: limit);
                if (accepted)
                {
                    Assert.IsTrue(WProtoFacade.TryDeserialize(payload, limits, out LimitMarker _));
                    Assert.IsTrue(
                        WProtoFacade.TryDeserializeAs(
                            payload,
                            typeof(LimitMarker),
                            limits,
                            out LimitMarker _
                        )
                    );
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() =>
                        WProtoFacade.TryDeserialize(payload, limits, out LimitMarker _)
                    );
                    Assert.Throws<InvalidOperationException>(() =>
                        WProtoFacade.TryDeserializeAs(
                            payload,
                            typeof(LimitMarker),
                            limits,
                            out LimitMarker _
                        )
                    );
                }

                WProtoFormatterProvider.Register<LimitMarker>(null);
                Assert.IsFalse(WProtoFacade.TryDeserialize(payload, limits, out LimitMarker _));
            }
            finally
            {
                WProtoFormatterProvider.Register(original);
            }
        }

        [TestCase(-1, 0)]
        [TestCase(0, 0)]
        [TestCase(64, 64)]
        [TestCase(int.MaxValue, 64)]
        public void CallerCannotRaiseTheStackSafetyCeiling(int requested, int expected)
        {
            Assert.AreEqual(
                expected,
                new WProtoReadLimits(maximumNestingDepth: requested).MaximumNestingDepth
            );
        }

        private static T CreateDefault<T>()
            where T : class, new()
        {
            return new T();
        }

        private sealed class CountingFormatter : IWProtoFormatter<int>
        {
            internal int Reads;

            public int Measure(in int value) => 0;

            public bool Write(ref WProtoWriter writer, in int value) => true;

            public bool TryRead(ref WProtoReader reader, out int value)
            {
                Reads++;
                value = 1;
                return true;
            }
        }

        private sealed class MarkerFormatter : IWProtoFormatter<LimitMarker>
        {
            internal int Reads;

            public int Measure(in LimitMarker value) => 0;

            public bool Write(ref WProtoWriter writer, in LimitMarker value) => true;

            public bool TryRead(ref WProtoReader reader, out LimitMarker value)
            {
                Reads++;
                value = default;
                while (reader.TryReadTag(out int tag, out int wire))
                {
                    if (!reader.TrySkipField(tag, wire))
                    {
                        return false;
                    }
                }

                return !reader.Malformed;
            }
        }

        private struct LimitMarker { }

        private sealed class ResettingFormatter : IWProtoFormatter<LimitMarker>
        {
            public int Measure(in LimitMarker value) => 0;

            public bool Write(ref WProtoWriter writer, in LimitMarker value) => true;

            public bool TryRead(ref WProtoReader reader, out LimitMarker value)
            {
                reader = default;
                value = default;
                return true;
            }
        }
    }
}
