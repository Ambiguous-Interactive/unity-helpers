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

        private static bool ReadPackedValue(ref WProtoReader reader, int wireType)
        {
            switch (wireType)
            {
                case WProtoWireType.Fixed32:
                    return reader.TryReadFixed32(out _);
                case WProtoWireType.Fixed64:
                    return reader.TryReadFixed64(out _);
                default:
                    return reader.TryReadInt32(out _);
            }
        }

        private static T CreateDefault<T>()
            where T : class, new()
        {
            return new T();
        }

        [Test]
        public void ParameterlessConstructionSupportsGenericFactories()
        {
            WProtoReadLimits limits = CreateDefault<WProtoReadLimits>();
            Assert.AreEqual(int.MaxValue, limits.MaximumMessageBytes);
            Assert.AreEqual(int.MaxValue, limits.MaximumLengthDelimitedBytes);
            Assert.AreEqual(int.MaxValue, limits.MaximumFieldCount);
            Assert.AreEqual(int.MaxValue, limits.MaximumPackedElementCount);
            Assert.AreEqual(WProtoReader.MaxNestingDepth, limits.MaximumNestingDepth);
        }

        [Test]
        public void EveryTwoBytePrefixPreservesValueConsumptionAndFailureLatching()
        {
            byte[] payload = new byte[3];
            payload[0] = 42;
            for (int first = 0; first <= byte.MaxValue; ++first)
            {
                payload[1] = (byte)first;
                for (int second = 0; second <= byte.MaxValue; ++second)
                {
                    payload[2] = (byte)second;
                    WProtoReader reader = new WProtoReader(payload);
                    Assert.IsTrue(reader.TryReadVarint64(out ulong prefix));
                    Assert.AreEqual(42UL, prefix);
                    bool accepted = first < 128 || second < 128;
                    Assert.AreEqual(accepted, reader.TryReadVarint64(out ulong value));
                    Assert.AreEqual(!accepted, reader.Malformed);
                    if (accepted)
                    {
                        ulong expected =
                            first < 128 ? (ulong)first : (ulong)((first & 127) | (second << 7));
                        Assert.AreEqual(expected, value);
                        Assert.AreEqual(first < 128 ? 2 : 3, reader.Position);
                    }
                    else
                    {
                        Assert.AreEqual(0UL, value);
                        Assert.IsFalse(reader.TryReadVarint64(out ulong refused));
                        Assert.AreEqual(0UL, refused);
                    }
                }
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        public void VarintWidthsChargeExactlyOnePackedElement(int width)
        {
            byte[] payload = new byte[1 + 2 * width];
            payload[0] = (byte)(2 * width);
            for (int index = 0; index < 2 * width; ++index)
            {
                payload[index + 1] = (byte)((index + 1) % width == 0 ? 1 : 128);
            }
            WProtoReader reader = new WProtoReader(
                payload,
                new WProtoReadLimits(maximumPackedElementCount: 1)
            );
            Assert.IsTrue(reader.TryReadPackedRun(out WProtoReader packed));
            Assert.IsTrue(packed.TryReadVarint64(out ulong value));
            Assert.AreEqual(1UL << ((width - 1) * 7), value);
            Assert.AreEqual(width, packed.Position);
            Assert.IsFalse(packed.TryReadVarint64(out ulong refused));
            Assert.AreEqual(0UL, refused);
            Assert.IsTrue(packed.Malformed);
            Assert.AreEqual(width, packed.Position);
        }

        [TestCase(0UL)]
        [TestCase(127UL)]
        [TestCase(128UL)]
        [TestCase(16383UL)]
        [TestCase(16384UL)]
        [TestCase(2097151UL)]
        [TestCase(2097152UL)]
        [TestCase(0x123456789ABCDEF0UL)]
        [TestCase(ulong.MaxValue)]
        public void VarintBoundariesPreserveValuesAndRejectEveryTruncatedPrefix(ulong expected)
        {
            byte[] bytes = new byte[WProtoSizes.MaxVarintBytes];
            WProtoWriter writer = new WProtoWriter(bytes);
            Assert.IsTrue(writer.TryWriteVarint64(expected));
            WProtoReader reader = new WProtoReader(writer.Written);
            Assert.IsTrue(reader.TryReadVarint64(out ulong actual));
            Assert.AreEqual(expected, actual);
            Assert.IsTrue(reader.End);
            Assert.IsFalse(reader.Malformed);
            for (int length = 0; length < writer.Position; ++length)
            {
                WProtoReader truncated = new WProtoReader(writer.Written.Slice(0, length));
                Assert.IsFalse(truncated.TryReadVarint64(out ulong refused));
                Assert.AreEqual(0UL, refused);
                Assert.IsTrue(truncated.Malformed);
            }
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
                ++count;
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
                ++reads;
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

        [TestCase(0, false, false)]
        [TestCase(1, true, false)]
        [TestCase(0, false, true)]
        [TestCase(1, true, true)]
        public void FacadePropagatesFieldLimitsAndKeepsItsRoutingContract(
            int limit,
            bool accepted,
            bool total
        )
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
                WProtoReadLimits limits = total
                    ? new WProtoReadLimits().WithTotalFieldCount(limit)
                    : new WProtoReadLimits(maximumFieldCount: limit);
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

        [TestCase(WProtoWireType.Varint)]
        [TestCase(WProtoWireType.Fixed32)]
        [TestCase(WProtoWireType.Fixed64)]
        public void PackedElementBudgetIsSharedAcrossSplitAndNestedRuns(int wireType)
        {
            int width =
                wireType == WProtoWireType.Fixed64 ? 8
                : wireType == WProtoWireType.Fixed32 ? 4
                : 1;
            byte[] payload = new byte[width + 1];
            payload[0] = (byte)width;
            WProtoReadLimits limits = new WProtoReadLimits(maximumPackedElementCount: 1);
            WProtoReader root = new WProtoReader(payload, limits);
            Assert.IsTrue(root.TryReadPackedRun(out WProtoReader first));
            Assert.AreEqual(1, first.CountPackedElements(wireType));
            Assert.IsTrue(ReadPackedValue(ref first, wireType));
            WProtoReader nested = new WProtoReader(payload, in root);
            Assert.IsTrue(nested.TryReadPackedRun(out WProtoReader second));
            Assert.AreEqual(0, second.CountPackedElements(wireType));
            Assert.IsFalse(ReadPackedValue(ref second, wireType));
            Assert.IsTrue(second.Malformed);
            Assert.AreEqual(0, second.Position);
            WProtoReader independent = new WProtoReader(payload, limits);
            Assert.IsTrue(independent.TryReadPackedRun(out WProtoReader fresh));
            Assert.IsTrue(ReadPackedValue(ref fresh, wireType));
        }

        [TestCase(WProtoWireType.Varint)]
        [TestCase(WProtoWireType.Fixed32)]
        [TestCase(WProtoWireType.Fixed64)]
        public void TypedPackedRunsRefuseBeforeReservationAndUseOneBudget(int wireType)
        {
            int width =
                wireType == WProtoWireType.Fixed64 ? 8
                : wireType == WProtoWireType.Fixed32 ? 4
                : 1;
            byte[] payload = new byte[2 * width + 2];
            payload[0] = (byte)width;
            payload[width + 1] = (byte)width;
            foreach (int limit in new[] { -1, 0, 1, 2, int.MaxValue })
            {
                WProtoReader reader = new WProtoReader(
                    payload,
                    new WProtoReadLimits(maximumPackedElementCount: limit)
                );
                bool firstAccepted = 0 < limit;
                Assert.AreEqual(
                    firstAccepted,
                    reader.TryReadPackedRun(wireType, out WProtoReader first)
                );
                Assert.AreEqual(firstAccepted ? 1 : 0, first.CountPackedElements(wireType));
                if (!firstAccepted)
                {
                    Assert.IsTrue(reader.Malformed);
                    Assert.IsTrue(first.Malformed);
                    continue;
                }

                Assert.IsTrue(ReadPackedValue(ref first, wireType));
                bool secondAccepted = 1 < limit;
                Assert.AreEqual(
                    secondAccepted,
                    reader.TryReadPackedRun(wireType, out WProtoReader second)
                );
                Assert.AreEqual(!secondAccepted, second.Malformed);
                Assert.AreEqual(secondAccepted ? 1 : 0, second.CountPackedElements(wireType));
                if (secondAccepted)
                {
                    Assert.IsTrue(ReadPackedValue(ref second, wireType));
                }
            }

            WProtoReader empty = new WProtoReader(
                new byte[] { 0 },
                new WProtoReadLimits(maximumPackedElementCount: 0)
            );
            Assert.IsTrue(empty.TryReadPackedRun(wireType, out WProtoReader emptyRun));
            Assert.IsTrue(emptyRun.End);
            foreach (
                int invalidWire in new[]
                {
                    -1,
                    WProtoWireType.LengthDelimited,
                    WProtoWireType.StartGroup,
                    WProtoWireType.EndGroup,
                    int.MaxValue,
                }
            )
            {
                WProtoReader invalid = new WProtoReader(new byte[] { 0 });
                Assert.IsFalse(invalid.TryReadPackedRun(invalidWire, out WProtoReader refused));
                Assert.IsTrue(invalid.Malformed);
                Assert.IsTrue(refused.Malformed);
            }
        }

        [Test]
        public void EveryNestedReadRouteRetainsTheCumulativePackedBudget()
        {
            PackedFormatter formatter = new PackedFormatter();
            byte[] payload = { 1, 0 };
            WProtoReadLimits limits = new WProtoReadLimits(maximumPackedElementCount: 1);
            WProtoReader detached = new WProtoReader(default, limits);
            Assert.IsTrue(detached.TryReadMessage(payload, formatter, out _));
            Assert.IsFalse(detached.TryReadMessage(payload, formatter, out _));
            Assert.IsTrue(detached.Malformed);
            WProtoReader merged = new WProtoReader(default, limits);
            int seed = 0;
            Assert.IsTrue(merged.TryReadMessage(payload, formatter, in seed, out _));
            Assert.IsFalse(merged.TryReadMessage(payload, formatter, in seed, out _));
            Assert.IsTrue(merged.Malformed);
            WProtoReader nested = new WProtoReader(new byte[] { 2, 1, 0, 2, 1, 0 }, limits);
            Assert.IsTrue(nested.TryReadMessage(formatter, out _));
            Assert.IsFalse(nested.TryReadMessage(formatter, out _));
            Assert.IsTrue(nested.Malformed);

            WProtoReader original = new WProtoReader(payload, limits);
            WProtoReader copy = original;
            Assert.IsTrue(original.TryReadPackedRun(WProtoWireType.Varint, out _));
            Assert.IsFalse(copy.TryReadPackedRun(WProtoWireType.Varint, out _));
            Assert.IsTrue(copy.Malformed);
            WProtoReader untyped = new WProtoReader(payload, limits);
            Assert.IsTrue(untyped.TryReadPackedRun(out WProtoReader packed));
            WProtoReader packedCopy = packed;
            Assert.IsTrue(packed.TryReadInt32(out _));
            Assert.IsFalse(packedCopy.TryReadInt32(out _));
            Assert.IsTrue(packedCopy.Malformed);
            Assert.AreEqual(0, packedCopy.Position);
        }

        [TestCase(-1, 0)]
        [TestCase(0, 0)]
        [TestCase(3, 3)]
        [TestCase(int.MaxValue, int.MaxValue)]
        public void TotalFieldLimitPreservesExistingConstructorSettings(int requested, int expected)
        {
            WProtoReadLimits[] existing =
            {
                new WProtoReadLimits(),
                new WProtoReadLimits(10, 9, 8, 7),
                new WProtoReadLimits(1, default),
                new WProtoReadLimits(default, 1),
                new WProtoReadLimits(10, 9, 8, 7, 6),
                new WProtoReadLimits(maximumFieldCount: 2),
                new WProtoReadLimits(maximumPackedElementCount: 4),
                new WProtoReadLimits().WithTotalFieldCount(1),
            };
            foreach (WProtoReadLimits original in existing)
            {
                WProtoReadLimits limits = original.WithTotalFieldCount(requested);
                Assert.AreEqual(expected, limits.MaximumTotalFieldCount);
                Assert.AreEqual(original.MaximumMessageBytes, limits.MaximumMessageBytes);
                Assert.AreEqual(
                    original.MaximumLengthDelimitedBytes,
                    limits.MaximumLengthDelimitedBytes
                );
                Assert.AreEqual(original.MaximumFieldCount, limits.MaximumFieldCount);
                Assert.AreEqual(
                    original.MaximumPackedElementCount,
                    limits.MaximumPackedElementCount
                );
                Assert.AreEqual(original.MaximumNestingDepth, limits.MaximumNestingDepth);
            }
            Assert.AreEqual(int.MaxValue, new WProtoReadLimits().MaximumTotalFieldCount);
            WProtoReadLimits defaults = new WProtoReadLimits().WithTotalFieldCount(requested);
            Assert.AreEqual(int.MaxValue, defaults.MaximumFieldCount);
            Assert.AreEqual(expected, defaults.MaximumTotalFieldCount);
        }

        [Test]
        public void TotalFieldBudgetStopsAcrossSiblingRegionsBeforeTheNextTag()
        {
            byte[] payload = { 10, 2, 8, 1, 18, 2, 8, 2 };
            WProtoReadLimits limits = new WProtoReadLimits(
                maximumFieldCount: 2
            ).WithTotalFieldCount(3);
            WProtoReader reader = new WProtoReader(payload, limits);
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadMessage(out WProtoReader first));
            Assert.IsTrue(first.TryReadTag(out _, out _));
            Assert.IsTrue(first.TryReadInt32(out _));
            Assert.IsFalse(first.TryReadTag(out _, out _));
            Assert.IsFalse(first.Malformed);
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadMessage(out WProtoReader second));
            Assert.IsFalse(second.TryReadTag(out int tag, out int wire));
            Assert.AreEqual(0, tag);
            Assert.AreEqual(-1, wire);
            Assert.AreEqual(0, second.Position);
            Assert.IsTrue(second.Malformed);
            Assert.IsFalse(second.TryReadInt32(out _));
            Assert.IsFalse(reader.Malformed);
        }

        [TestCase(0, TestName = "TotalFieldBudget.DetachedConstructor")]
        [TestCase(1, TestName = "TotalFieldBudget.NestedReader")]
        [TestCase(2, TestName = "TotalFieldBudget.NestedFormatter")]
        [TestCase(3, TestName = "TotalFieldBudget.DetachedFormatter")]
        [TestCase(4, TestName = "TotalFieldBudget.SeededFormatter")]
        [TestCase(5, TestName = "TotalFieldBudget.UntypedPackedReader")]
        [TestCase(6, TestName = "TotalFieldBudget.TypedPackedReader")]
        public void EveryNestedRouteSharesTheTotalFieldBudget(int route)
        {
            byte[] payload = { 8, 1 };
            WProtoReader root = new WProtoReader(
                new byte[] { 2, 8, 1 },
                new WProtoReadLimits().WithTotalFieldCount(1)
            );
            WProtoReader first = new WProtoReader(payload, in root);
            Assert.IsTrue(first.TryReadTag(out int tag, out int wire));
            Assert.IsTrue(first.TrySkipField(tag, wire));
            MarkerFormatter formatter = new MarkerFormatter();
            switch (route)
            {
                case 0:
                    WProtoReader detached = new WProtoReader(payload, in root);
                    Assert.IsFalse(detached.TryReadTag(out _, out _));
                    Assert.AreEqual(0, detached.Position);
                    Assert.IsTrue(detached.Malformed);
                    break;
                case 1:
                    Assert.IsTrue(root.TryReadMessage(out WProtoReader nested));
                    Assert.IsFalse(nested.TryReadTag(out _, out _));
                    Assert.AreEqual(0, nested.Position);
                    Assert.IsTrue(nested.Malformed);
                    break;
                case 2:
                    Assert.IsFalse(root.TryReadMessage(formatter, out _));
                    Assert.IsTrue(root.Malformed);
                    break;
                case 3:
                    Assert.IsFalse(root.TryReadMessage(payload, formatter, out _));
                    Assert.IsTrue(root.Malformed);
                    break;
                case 4:
                    LimitMarker seed = default;
                    Assert.IsFalse(root.TryReadMessage(payload, formatter, in seed, out _));
                    Assert.IsTrue(root.Malformed);
                    break;
                case 5:
                    Assert.IsTrue(root.TryReadPackedRun(out WProtoReader packed));
                    Assert.IsFalse(packed.TryReadTag(out _, out _));
                    Assert.IsTrue(packed.Malformed);
                    break;
                case 6:
                    Assert.IsTrue(
                        root.TryReadPackedRun(WProtoWireType.Varint, out WProtoReader typed)
                    );
                    Assert.IsFalse(typed.TryReadTag(out _, out _));
                    Assert.IsTrue(typed.Malformed);
                    break;
            }
        }

        [TestCase(-1, 0)]
        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(3, 3)]
        [TestCase(int.MaxValue, 3)]
        public void TotalFieldBudgetCountsDuplicateAndUnknownTags(int limit, int expected)
        {
            byte[] payload = { 8, 1, 8, 2, 248, 1, 3 };
            WProtoReader reader = new WProtoReader(
                payload,
                new WProtoReadLimits().WithTotalFieldCount(limit)
            );
            int count = 0;
            while (reader.TryReadTag(out int tag, out int wire))
            {
                Assert.IsTrue(reader.TrySkipField(tag, wire));
                ++count;
            }
            Assert.AreEqual(expected, count);
            Assert.AreEqual(expected < 3, reader.Malformed);
            Assert.AreEqual(expected < 3 ? expected * 2 : payload.Length, reader.Position);
        }

        [TestCase(2, false)]
        [TestCase(3, true)]
        public void TotalFieldBudgetCountsGroupTerminators(int limit, bool accepted)
        {
            WProtoReader reader = new WProtoReader(
                new byte[] { 11, 16, 1, 12 },
                new WProtoReadLimits().WithTotalFieldCount(limit)
            );
            Assert.IsTrue(reader.TryReadTag(out int tag, out int wire));
            Assert.AreEqual(accepted, reader.TrySkipField(tag, wire));
            Assert.AreEqual(!accepted, reader.Malformed);
            Assert.AreEqual(accepted ? 4 : 3, reader.Position);
        }

        [Test]
        public void TotalFieldBudgetChargesReplayedCopiesAndResetsForIndependentRoots()
        {
            byte[] payload = { 8, 1 };
            WProtoReadLimits limits = new WProtoReadLimits().WithTotalFieldCount(1);
            WProtoReader original = new WProtoReader(payload, limits);
            WProtoReader copy = original;
            Assert.IsTrue(original.TryReadTag(out _, out _));
            Assert.IsFalse(copy.TryReadTag(out _, out _));
            Assert.AreEqual(0, copy.Position);
            Assert.IsTrue(copy.Malformed);
            Assert.IsTrue(original.TryReadInt32(out _));
            Assert.IsFalse(original.TryReadTag(out _, out _));
            Assert.IsFalse(original.Malformed);
            WProtoReader independent = new WProtoReader(payload, limits);
            Assert.IsTrue(independent.TryReadTag(out _, out _));
        }

        [Test]
        public void TotalFieldBudgetDoesNotChargeUntaggedPackedElements()
        {
            WProtoReader reader = new WProtoReader(
                new byte[] { 10, 2, 1, 2 },
                new WProtoReadLimits().WithTotalFieldCount(1)
            );
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadPackedRun(WProtoWireType.Varint, out WProtoReader packed));
            Assert.IsTrue(packed.TryReadInt32(out _));
            Assert.IsTrue(packed.TryReadInt32(out _));
            Assert.IsTrue(packed.End);
            Assert.IsFalse(reader.TryReadTag(out _, out _));
            Assert.IsFalse(reader.Malformed);
            WProtoReader empty = new WProtoReader(
                default,
                new WProtoReadLimits().WithTotalFieldCount(0)
            );
            Assert.IsFalse(empty.TryReadTag(out _, out _));
            Assert.IsFalse(empty.Malformed);
        }

        [Test]
        public void TotalAndPackedBudgetsChargeIndependentWork()
        {
            WProtoReadLimits limits = new WProtoReadLimits(
                maximumPackedElementCount: 1
            ).WithTotalFieldCount(2);
            WProtoReader reader = new WProtoReader(new byte[] { 10, 1, 7, 16, 3 }, limits);
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadPackedRun(WProtoWireType.Varint, out WProtoReader packed));
            Assert.IsTrue(packed.TryReadInt32(out int packedValue));
            Assert.AreEqual(7, packedValue);
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadInt32(out int scalarValue));
            Assert.AreEqual(3, scalarValue);
            Assert.IsFalse(reader.TryReadTag(out _, out _));
            Assert.IsFalse(reader.Malformed);
        }

        private sealed class PackedFormatter : IWProtoFormatter<int>
        {
            public int Measure(in int value)
            {
                return 0;
            }

            public bool Write(ref WProtoWriter writer, in int value)
            {
                return true;
            }

            public bool TryRead(ref WProtoReader reader, out int value)
            {
                value = 0;
                return reader.TryReadPackedRun(WProtoWireType.Varint, out WProtoReader packed)
                    && packed.TryReadInt32(out value);
            }
        }

        private sealed class CountingFormatter : IWProtoFormatter<int>
        {
            internal int Reads;

            public int Measure(in int value)
            {
                return 0;
            }

            public bool Write(ref WProtoWriter writer, in int value)
            {
                return true;
            }

            public bool TryRead(ref WProtoReader reader, out int value)
            {
                ++Reads;
                value = 1;
                return true;
            }
        }

        private sealed class MarkerFormatter : IWProtoFormatter<LimitMarker>
        {
            internal int Reads;

            public int Measure(in LimitMarker value)
            {
                return 0;
            }

            public bool Write(ref WProtoWriter writer, in LimitMarker value)
            {
                return true;
            }

            public bool TryRead(ref WProtoReader reader, out LimitMarker value)
            {
                ++Reads;
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
            public int Measure(in LimitMarker value)
            {
                return 0;
            }

            public bool Write(ref WProtoWriter writer, in LimitMarker value)
            {
                return true;
            }

            public bool TryRead(ref WProtoReader reader, out LimitMarker value)
            {
                reader = default;
                value = default;
                return true;
            }
        }
    }
}
