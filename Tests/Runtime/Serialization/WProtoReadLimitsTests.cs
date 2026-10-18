// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
#if !UNITY_5_3_OR_NEWER
    using HostContracts = WallstopStudios.UnityHelpers.Proto.Generator.Tests;
#endif

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

        private static bool TryMergeCopyPair(ref WProtoReader parent, ReadOnlySpan<byte> occurrence)
        {
            WProtoMessageAccumulator accumulator = default;
            return accumulator.TryAdd(occurrence, ref parent)
                && accumulator.TryAdd(occurrence, ref parent);
        }

        private static byte[] ParseMergePayload(string hex)
        {
            Assert.AreEqual(0, hex.Length % 2);
            byte[] bytes = new byte[hex.Length / 2];
            for (int index = 0; index < bytes.Length; ++index)
            {
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }
            return bytes;
        }

        private static long? BeginAllocationMeasurement()
        {
#if ENABLE_IL2CPP && !UNITY_6000_0_OR_NEWER
            return null;
#else
            return BeginAllocationMeasurement(GC.GetAllocatedBytesForCurrentThread);
#endif
        }

        private static long? BeginAllocationMeasurement(Func<long> readCounter)
        {
            const int controlBytes = 32;
            long before = readCounter();
            byte[] control = new byte[controlBytes];
            long after = readCounter();
            GC.KeepAlive(control);
            if (after - before < controlBytes)
            {
                return null;
            }

            return readCounter();
        }

        private static long? EndAllocationMeasurement(long? before)
        {
            return before.HasValue
                ? GC.GetAllocatedBytesForCurrentThread() - before.Value
                : (long?)null;
        }

        private static void AssertNoAllocationWhenMeasured(long? allocated)
        {
            if (!allocated.HasValue)
            {
                TestContext.Out.WriteLine(
                    "Allocation evidence unavailable: this platform cannot safely measure allocations or its counter did not detect the retained control allocation. Serialization assertions still execute."
                );
                return;
            }

            Assert.AreEqual(0, allocated.Value);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(31)]
        [TestCase(-1)]
        public void AllocationCounterWithoutByteArraySensitivityProvidesNoEvidence(long increment)
        {
            int reads = 0;
            long? measurement = BeginAllocationMeasurement(() => ++reads * increment);
            Assert.IsFalse(measurement.HasValue);
            AssertNoAllocationWhenMeasured(measurement);
        }

        [TestCase(32)]
        [TestCase(64)]
        public void ResponsiveAllocationCounterEnablesMeasurement(long increment)
        {
            int reads = 0;
            Assert.IsTrue(BeginAllocationMeasurement(() => ++reads * increment).HasValue);
        }

        [Test]
        public void FormatterFailureLatchesCurrentReaderWithoutPoisoningCopies()
        {
            WProtoReader reader = new WProtoReader(new byte[] { 0x08, 1 });
            WProtoReader copy = reader;
            reader.MarkMalformed();
            reader.MarkMalformed();
            Assert.IsTrue(reader.Malformed);
            Assert.IsFalse(reader.TryReadTag(out _, out _));
            Assert.IsFalse(reader.TryReadVarint64(out _));
            Assert.AreEqual(0, reader.Position);
            Assert.IsFalse(copy.Malformed);
            Assert.IsTrue(copy.TryReadTag(out int field, out int wire));
            Assert.AreEqual(1, field);
            Assert.AreEqual(WProtoWireType.Varint, wire);
            Assert.IsTrue(copy.TryReadInt32(out int value));
            Assert.AreEqual(1, value);
        }

        [TestCase(1, false)]
        [TestCase(2, true)]
        public void DuplicateBoundaryValidationSharesParentTagBudget(int maximumTags, bool accepted)
        {
            byte[] first = { 0x08, 1 };
            byte[] second = { 0x10, 2 };
            WProtoReader parent = new WProtoReader(
                default,
                new WProtoReadLimits().WithTotalFieldCount(maximumTags)
            );
            WProtoMessageAccumulator accumulator = default;
            Assert.IsTrue(accumulator.TryAdd(first, ref parent));
            Assert.AreEqual(accepted, accumulator.TryAdd(second, ref parent));
            Assert.AreEqual(!accepted, parent.Malformed);
            CollectionAssert.AreEqual(
                accepted ? new byte[] { 0x08, 1, 0x10, 2 } : first,
                accumulator.Payload.ToArray()
            );
        }

        [TestCase(0)]
        [TestCase(1)]
        public void DuplicateBoundaryBudgetRefusesBeforeAllocating(int maximumTags)
        {
            byte[] first = { 0x08, 1 };
            byte[] second = { 0x10, 2 };
            WProtoReadLimits limits = new WProtoReadLimits().WithTotalFieldCount(maximumTags);
            WProtoReader warmParent = new WProtoReader(default, limits);
            WProtoMessageAccumulator warm = default;
            Assert.IsTrue(warm.TryAdd(first, ref warmParent));
            Assert.IsFalse(warm.TryAdd(second, ref warmParent));
            WProtoReader parent = new WProtoReader(default, limits);
            WProtoMessageAccumulator accumulator = default;
            Assert.IsTrue(accumulator.TryAdd(first, ref parent));
            long? before = BeginAllocationMeasurement();
            bool accepted = accumulator.TryAdd(second, ref parent);
            long? allocated = EndAllocationMeasurement(before);
            Assert.IsFalse(accepted);
            AssertNoAllocationWhenMeasured(allocated);
            Assert.IsTrue(parent.Malformed);
            CollectionAssert.AreEqual(first, accumulator.Payload.ToArray());
            Assert.IsFalse(accumulator.TryAdd(Array.Empty<byte>(), ref parent));
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, true)]
        public void DuplicateBoundaryValidationUsesRemainingGroupDepth(
            int maximumDepth,
            bool accepted
        )
        {
            byte[] first = { 0x13, 0x18, 1, 0x14 };
            WProtoReader parent = new WProtoReader(
                default,
                new WProtoReadLimits(maximumNestingDepth: maximumDepth)
            );
            WProtoMessageAccumulator accumulator = default;
            Assert.IsTrue(accumulator.TryAdd(first, ref parent));
            Assert.AreEqual(accepted, accumulator.TryAdd(Array.Empty<byte>(), ref parent));
            Assert.AreEqual(!accepted, parent.Malformed);
            CollectionAssert.AreEqual(first, accumulator.Payload.ToArray());
        }

        [TestCase(2, false)]
        [TestCase(3, true)]
        public void DuplicateBoundaryValidationChargesUnknownGroupTerminators(
            int maximumTags,
            bool accepted
        )
        {
            byte[] first = { 0x13, 0x18, 1, 0x14 };
            WProtoReader parent = new WProtoReader(
                default,
                new WProtoReadLimits().WithTotalFieldCount(maximumTags)
            );
            WProtoMessageAccumulator accumulator = default;
            Assert.IsTrue(accumulator.TryAdd(first, ref parent));
            Assert.AreEqual(accepted, accumulator.TryAdd(Array.Empty<byte>(), ref parent));
            Assert.AreEqual(!accepted, parent.Malformed);
        }

        [TestCase(new byte[] { 0x08 })]
        [TestCase(new byte[] { 0x08, 0x80 })]
        [TestCase(new byte[] { 0x0A, 2, 1 })]
        [TestCase(new byte[] { 0x13, 0x1C })]
        [TestCase(new byte[] { 0 })]
        public void ParentAwareAccumulatorPreservesPayloadOnMalformedDuplicates(byte[] malformed)
        {
            byte[] valid = { 0x08, 1 };
            WProtoReader firstParent = new WProtoReader(default);
            WProtoMessageAccumulator first = default;
            Assert.IsTrue(first.TryAdd(malformed, ref firstParent));
            Assert.IsFalse(first.TryAdd(valid, ref firstParent));
            Assert.IsTrue(firstParent.Malformed);
            CollectionAssert.AreEqual(malformed, first.Payload.ToArray());

            WProtoReader secondParent = new WProtoReader(default);
            WProtoMessageAccumulator second = default;
            Assert.IsTrue(second.TryAdd(valid, ref secondParent));
            Assert.IsFalse(second.TryAdd(malformed, ref secondParent));
            Assert.IsTrue(secondParent.Malformed);
            CollectionAssert.AreEqual(valid, second.Payload.ToArray());
        }

        [Test]
        public void ParentAwareAccumulatorBorrowsFirstOccurrenceWithoutAllocation()
        {
            byte[] occurrence = { 0x08, 1 };
            WProtoReader parent = new WProtoReader(default);
            WProtoMessageAccumulator warm = default;
            Assert.IsTrue(warm.TryAdd(occurrence, ref parent));
            WProtoMessageAccumulator accumulator = default;
            long? before = BeginAllocationMeasurement();
            bool accepted = accumulator.TryAdd(occurrence, ref parent);
            long? allocated = EndAllocationMeasurement(before);
            Assert.IsTrue(accepted);
            AssertNoAllocationWhenMeasured(allocated);
            occurrence[1] = 7;
            Assert.AreEqual(7, accumulator.Payload[1]);
        }

        [Test]
        public void ParentAwareAccumulatorRefusesMalformedParentBeforeBorrowing()
        {
            WProtoReader parent = new WProtoReader(new byte[] { 0 });
            Assert.IsFalse(parent.TryReadTag(out _, out _));
            WProtoMessageAccumulator accumulator = default;
            Assert.IsFalse(accumulator.TryAdd(new byte[] { 0x08, 1 }, ref parent));
            Assert.IsFalse(accumulator.HasValue);
            Assert.AreEqual(0, accumulator.Payload.Length);
        }

        [Test]
        public void DuplicateBoundaryValidationSharesBudgetWithParentCopies()
        {
            WProtoReader parent = new WProtoReader(
                new byte[] { 0x08, 1 },
                new WProtoReadLimits().WithTotalFieldCount(1)
            );
            WProtoReader copy = parent;
            WProtoMessageAccumulator accumulator = default;
            Assert.IsTrue(accumulator.TryAdd(new byte[] { 0x08, 2 }, ref parent));
            Assert.IsTrue(accumulator.TryAdd(Array.Empty<byte>(), ref parent));
            Assert.IsFalse(copy.TryReadTag(out _, out _));
            Assert.IsTrue(copy.Malformed);
            Assert.AreEqual(0, copy.Position);
            Assert.IsFalse(parent.Malformed);
        }

        [TestCase(2, false)]
        [TestCase(3, true)]
        public void DuplicateBoundaryValidationHonorsNestedParentDepth(
            int maximumDepth,
            bool accepted
        )
        {
            WProtoReader root = new WProtoReader(
                new byte[] { 0 },
                new WProtoReadLimits(maximumNestingDepth: maximumDepth)
            );
            Assert.IsTrue(root.TryReadMessage(out WProtoReader parent));
            WProtoMessageAccumulator accumulator = default;
            Assert.IsTrue(accumulator.TryAdd(new byte[] { 0x13, 0x14 }, ref parent));
            Assert.AreEqual(accepted, accumulator.TryAdd(Array.Empty<byte>(), ref parent));
            Assert.AreEqual(!accepted, parent.Malformed);
            Assert.IsFalse(root.Malformed);
        }

        [TestCase(1, int.MaxValue, int.MaxValue, new byte[] { 0x08, 1 })]
        [TestCase(int.MaxValue, 1, int.MaxValue, new byte[] { 0x0A, 2, 1, 2 })]
        [TestCase(int.MaxValue, int.MaxValue, 1, new byte[] { 0x08, 1, 0x10, 2 })]
        public void DuplicateBoundaryValidationPreservesParentRegionLimits(
            int maximumBytes,
            int maximumDelimitedBytes,
            int maximumFields,
            byte[] occurrence
        )
        {
            WProtoReader parent = new WProtoReader(
                default,
                new WProtoReadLimits(maximumBytes, maximumDelimitedBytes, maximumFields, 64)
            );
            WProtoMessageAccumulator accumulator = default;
            Assert.IsTrue(accumulator.TryAdd(occurrence, ref parent));
            Assert.IsFalse(accumulator.TryAdd(Array.Empty<byte>(), ref parent));
            Assert.IsTrue(parent.Malformed);
            CollectionAssert.AreEqual(occurrence, accumulator.Payload.ToArray());
        }

        [Test]
        public void DuplicateBoundaryBudgetRefusesGrowthWithoutChangingAcceptedPayload()
        {
            byte[] occurrence = { 0x08, 1 };
            WProtoReader parent = new WProtoReader(
                default,
                new WProtoReadLimits().WithTotalFieldCount(2)
            );
            WProtoMessageAccumulator accumulator = default;
            Assert.IsTrue(accumulator.TryAdd(occurrence, ref parent));
            Assert.IsTrue(accumulator.TryAdd(occurrence, ref parent));
            long? before = BeginAllocationMeasurement();
            bool accepted = accumulator.TryAdd(occurrence, ref parent);
            long? allocated = EndAllocationMeasurement(before);
            Assert.IsFalse(accepted);
            AssertNoAllocationWhenMeasured(allocated);
            Assert.IsTrue(parent.Malformed);
            CollectionAssert.AreEqual(
                new byte[] { 0x08, 1, 0x08, 1 },
                accumulator.Payload.ToArray()
            );
        }

        [Test]
        public void EmptyDuplicateBoundaryScansDoNotSpendTagBudget()
        {
            WProtoReader parent = new WProtoReader(
                default,
                new WProtoReadLimits().WithTotalFieldCount(0)
            );
            WProtoMessageAccumulator accumulator = default;
            Assert.IsTrue(accumulator.TryAdd(Array.Empty<byte>(), ref parent));
            Assert.IsTrue(accumulator.TryAdd(Array.Empty<byte>(), ref parent));
            Assert.IsFalse(parent.Malformed);
            Assert.IsTrue(parent.End);
            Assert.IsTrue(accumulator.HasValue);
            Assert.AreEqual(0, accumulator.Payload.Length);
        }

        [Test]
        public void LegacyAccumulatorOverloadPreservesIndependentValidation()
        {
            byte[] group = { 0x13, 0x14 };
            WProtoMessageAccumulator accumulator = default;
            Assert.IsTrue(accumulator.TryAdd(group));
            Assert.IsTrue(accumulator.TryAdd(Array.Empty<byte>()));
            CollectionAssert.AreEqual(group, accumulator.Payload.ToArray());
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
        public void TypedPackedCopiesReplayPrepaidValuesWithoutResettingTheRootBudget()
        {
            byte[] payload = { 1, 7, 1, 8 };
            WProtoReader root = new WProtoReader(
                payload,
                new WProtoReadLimits(maximumPackedElementCount: 1)
                    .WithTotalFieldCount(0)
                    .WithMessageMergeCopyBytes(0)
            );
            Assert.IsTrue(root.TryReadPackedRun(WProtoWireType.Varint, out WProtoReader packed));
            WProtoReader replay = packed;
            Assert.IsTrue(packed.TryReadInt32(out int first));
            Assert.AreEqual(7, first);
            Assert.IsTrue(replay.TryReadInt32(out int repeated));
            Assert.AreEqual(7, repeated);
            Assert.IsTrue(packed.End);
            Assert.IsTrue(replay.End);
            Assert.IsFalse(packed.Malformed);
            Assert.IsFalse(replay.Malformed);
            Assert.IsFalse(root.TryReadPackedRun(WProtoWireType.Varint, out WProtoReader refused));
            WProtoFormatterFailureTests.AssertCannotContinue(ref root);
            WProtoFormatterFailureTests.AssertCannotContinue(ref refused);
            Assert.IsFalse(packed.Malformed);
            Assert.IsFalse(replay.Malformed);
        }

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public void CumulativeLimitsRemainIndependentAcrossFiniteCombinations(
            bool finiteTags,
            bool finitePacked,
            bool finiteCopies
        )
        {
            byte[] payload = { 8, 9, 8, 10, 8, 11 };
            byte[] occurrence = { 8, 1 };
            byte[] packedPayload = { 1, 7, 1, 8 };
            WProtoReadLimits limits = new WProtoReadLimits(
                maximumPackedElementCount: finitePacked ? 1 : int.MaxValue
            )
                .WithTotalFieldCount(finiteTags ? 6 : int.MaxValue)
                .WithMessageMergeCopyBytes(finiteCopies ? 4 : long.MaxValue);
            foreach (bool typed in new[] { false, true })
            {
                WProtoReader root = new WProtoReader(payload, limits);
                Assert.IsTrue(TryMergeCopyPair(ref root, occurrence));
                WProtoReader continuing = root;
                Assert.AreEqual(!finiteCopies, TryMergeCopyPair(ref root, occurrence));
                Assert.AreEqual(finiteCopies, root.Malformed);
                Assert.IsFalse(continuing.Malformed);

                WProtoReader packedParent = new WProtoReader(packedPayload, in continuing);
                if (typed)
                {
                    Assert.IsTrue(
                        packedParent.TryReadPackedRun(WProtoWireType.Varint, out WProtoReader first)
                    );
                    Assert.IsTrue(first.TryReadInt32(out int firstValue));
                    Assert.AreEqual(7, firstValue);
                    Assert.IsTrue(first.End);
                    Assert.AreEqual(
                        !finitePacked,
                        packedParent.TryReadPackedRun(
                            WProtoWireType.Varint,
                            out WProtoReader second
                        )
                    );
                    Assert.AreEqual(finitePacked, packedParent.Malformed);
                    Assert.AreEqual(finitePacked, second.Malformed);
                    if (!finitePacked)
                    {
                        Assert.IsTrue(second.TryReadInt32(out int secondValue));
                        Assert.AreEqual(8, secondValue);
                        Assert.IsTrue(second.End);
                    }
                    else
                    {
                        WProtoFormatterFailureTests.AssertCannotContinue(ref packedParent);
                        WProtoFormatterFailureTests.AssertCannotContinue(ref second);
                    }
                }
                else
                {
                    Assert.IsTrue(packedParent.TryReadPackedRun(out WProtoReader first));
                    Assert.IsTrue(first.TryReadInt32(out int firstValue));
                    Assert.AreEqual(7, firstValue);
                    Assert.IsTrue(first.End);
                    Assert.IsTrue(packedParent.TryReadPackedRun(out WProtoReader second));
                    Assert.AreEqual(!finitePacked, second.TryReadInt32(out int secondValue));
                    Assert.AreEqual(finitePacked ? 0 : 8, secondValue);
                    Assert.AreEqual(finitePacked, second.Malformed);
                    Assert.IsFalse(packedParent.Malformed);
                    if (finitePacked)
                    {
                        Assert.AreEqual(0, second.Position);
                        WProtoFormatterFailureTests.AssertCannotContinue(ref second);
                    }
                    else
                    {
                        Assert.IsTrue(second.End);
                    }
                }

                Assert.IsFalse(continuing.Malformed);
                for (int index = 0; index < 3; ++index)
                {
                    bool accepted = index < 2 || !finiteTags;
                    Assert.AreEqual(accepted, continuing.TryReadTag(out int tag, out int wire));
                    if (!accepted)
                    {
                        Assert.AreEqual(0, tag);
                        Assert.AreEqual(-1, wire);
                        Assert.AreEqual(4, continuing.Position);
                        WProtoFormatterFailureTests.AssertCannotContinue(ref continuing);
                        break;
                    }
                    Assert.AreEqual(1, tag);
                    Assert.AreEqual(WProtoWireType.Varint, wire);
                    Assert.IsTrue(continuing.TryReadInt32(out int value));
                    Assert.AreEqual(9 + index, value);
                }
                Assert.AreEqual(finiteTags, continuing.Malformed);
                if (!finiteTags)
                {
                    Assert.IsTrue(continuing.End);
                }
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

        [TestCase(0L, 1, true)]
        [TestCase(3L, 2, false)]
        [TestCase(4L, 2, true)]
        [TestCase(9L, 3, false)]
        [TestCase(10L, 3, true)]
        [TestCase(11L, 4, false)]
        [TestCase(12L, 4, true)]
        [TestCase(21L, 5, false)]
        [TestCase(22L, 5, true)]
        public void MergeCopyBudgetCountsConsolidationGrowthAndInCapacityAppends(
            long maximumCopyBytes,
            int occurrences,
            bool accepted
        )
        {
            byte[] occurrence = { 0x08, 1 };
            WProtoReader parent = new WProtoReader(
                default,
                new WProtoReadLimits().WithMessageMergeCopyBytes(maximumCopyBytes)
            );
            WProtoMessageAccumulator accumulator = default;
            for (int index = 1; index < occurrences; ++index)
            {
                Assert.IsTrue(accumulator.TryAdd(occurrence, ref parent));
            }
            Assert.AreEqual(accepted, accumulator.TryAdd(occurrence, ref parent));
            Assert.AreEqual(!accepted, parent.Malformed);
            int delivered = accepted ? occurrences : occurrences - 1;
            Assert.AreEqual(delivered * occurrence.Length, accumulator.Payload.Length);
            WProtoReader decoded = new WProtoReader(accumulator.Payload);
            for (int index = 0; index < delivered; ++index)
            {
                Assert.IsTrue(decoded.TryReadTag(out int tag, out int wire));
                Assert.AreEqual(1, tag);
                Assert.AreEqual(WProtoWireType.Varint, wire);
                Assert.IsTrue(decoded.TryReadInt32(out int value));
                Assert.AreEqual(1, value);
            }
            Assert.IsTrue(decoded.End);
            Assert.IsFalse(decoded.Malformed);
            if (!accepted)
            {
                WProtoFormatterFailureTests.AssertCannotContinue(ref parent);
            }
        }

        [TestCase(0L, false)]
        [TestCase(1L, false)]
        [TestCase(2L, true)]
        public void EmptyDuplicateAfterBorrowingChargesTheBorrowedBytes(long limit, bool accepted)
        {
            byte[] occurrence = { 0x08, 1 };
            WProtoReader parent = new WProtoReader(
                default,
                new WProtoReadLimits().WithMessageMergeCopyBytes(limit)
            );
            WProtoMessageAccumulator accumulator = default;
            Assert.IsTrue(accumulator.TryAdd(occurrence, ref parent));
            Assert.AreEqual(accepted, accumulator.TryAdd(ReadOnlySpan<byte>.Empty, ref parent));
            Assert.AreEqual(!accepted, parent.Malformed);
            CollectionAssert.AreEqual(occurrence, accumulator.Payload.ToArray());
            if (accepted)
            {
                Assert.IsTrue(accumulator.TryAdd(ReadOnlySpan<byte>.Empty, ref parent));
                Assert.IsFalse(parent.Malformed);
            }
        }

        [Test]
        public void ZeroMergeCopyBudgetPreservesBorrowingAndEmptyOccurrences()
        {
            byte[] occurrence = { 0x08, 1 };
            WProtoReader parent = new WProtoReader(
                default,
                new WProtoReadLimits().WithMessageMergeCopyBytes(0)
            );
            WProtoMessageAccumulator warm = default;
            Assert.IsTrue(warm.TryAdd(occurrence, ref parent));
            WProtoMessageAccumulator accumulator = default;
            long? before = BeginAllocationMeasurement();
            bool accepted = accumulator.TryAdd(occurrence, ref parent);
            long? allocated = EndAllocationMeasurement(before);
            Assert.IsTrue(accepted);
            AssertNoAllocationWhenMeasured(allocated);
            occurrence[1] = 7;
            Assert.AreEqual(7, accumulator.Payload[1]);
            WProtoMessageAccumulator empty = default;
            Assert.IsTrue(empty.TryAdd(ReadOnlySpan<byte>.Empty, ref parent));
            Assert.IsTrue(empty.TryAdd(ReadOnlySpan<byte>.Empty, ref parent));
            Assert.IsTrue(empty.TryAdd(ReadOnlySpan<byte>.Empty, ref parent));
            Assert.IsTrue(empty.HasValue);
            Assert.AreEqual(0, empty.Payload.Length);
            Assert.IsFalse(parent.Malformed);
        }

        [TestCase(0, TestName = "MergeCopyBudget.SiblingAccumulator")]
        [TestCase(1, TestName = "MergeCopyBudget.ReaderCopy")]
        [TestCase(2, TestName = "MergeCopyBudget.ParentConstructor")]
        [TestCase(3, TestName = "MergeCopyBudget.MessageReader")]
        [TestCase(4, TestName = "MergeCopyBudget.UntypedPackedReader")]
        [TestCase(5, TestName = "MergeCopyBudget.TypedPackedReader")]
        public void MergeCopyBudgetIsSharedAcrossAccumulatorsAndReaderRoutes(int route)
        {
            byte[] occurrence = { 0x08, 1 };
            WProtoReadLimits limits = new WProtoReadLimits().WithMessageMergeCopyBytes(4);
            WProtoReader root = new WProtoReader(new byte[] { 2, 0x08, 1 }, limits);
            Assert.IsTrue(TryMergeCopyPair(ref root, occurrence));
            WProtoReader derived;
            switch (route)
            {
                case 0:
                    Assert.IsFalse(TryMergeCopyPair(ref root, occurrence));
                    WProtoFormatterFailureTests.AssertCannotContinue(ref root);
                    return;
                case 1:
                    derived = root;
                    break;
                case 2:
                {
                    WProtoReader child = new WProtoReader(default, in root);
                    Assert.IsFalse(TryMergeCopyPair(ref child, occurrence));
                    WProtoFormatterFailureTests.AssertCannotContinue(ref child);
                    Assert.IsFalse(root.Malformed);
                    return;
                }
                case 3:
                    Assert.IsTrue(root.TryReadMessage(out derived));
                    break;
                case 4:
                    Assert.IsTrue(root.TryReadPackedRun(out derived));
                    break;
                default:
                    Assert.IsTrue(root.TryReadPackedRun(WProtoWireType.Varint, out derived));
                    break;
            }
            Assert.IsFalse(TryMergeCopyPair(ref derived, occurrence));
            WProtoFormatterFailureTests.AssertCannotContinue(ref derived);
            Assert.IsFalse(root.Malformed);
        }

        [Test]
        public void IndependentRootsDoNotShareMergeCopyAllowances()
        {
            byte[] occurrence = { 0x08, 1 };
            WProtoReadLimits limits = new WProtoReadLimits().WithMessageMergeCopyBytes(4);
            WProtoReader first = new WProtoReader(default, limits);
            WProtoReader second = new WProtoReader(default, limits);
            Assert.IsTrue(TryMergeCopyPair(ref first, occurrence));
            Assert.IsTrue(TryMergeCopyPair(ref second, occurrence));
            Assert.IsFalse(TryMergeCopyPair(ref first, occurrence));
            Assert.IsFalse(second.Malformed);
            Assert.IsFalse(second.TryReadTag(out _, out _));
            Assert.IsFalse(second.Malformed);
        }

        [Test]
        public void RefusedMergeCopyReservationPreservesTheRemainingAllowance()
        {
            byte[] occurrence = { 0x08, 1 };
            WProtoReader parent = new WProtoReader(
                default,
                new WProtoReadLimits().WithMessageMergeCopyBytes(6)
            );
            WProtoReader copy = parent;
            WProtoMessageAccumulator accumulator = default;
            Assert.IsTrue(accumulator.TryAdd(occurrence, ref parent));
            Assert.IsTrue(accumulator.TryAdd(occurrence, ref parent));
            Assert.IsFalse(accumulator.TryAdd(occurrence, ref parent));
            CollectionAssert.AreEqual(
                new byte[] { 0x08, 1, 0x08, 1 },
                accumulator.Payload.ToArray()
            );
            WProtoFormatterFailureTests.AssertCannotContinue(ref parent);
            Assert.IsFalse(copy.Malformed);
            WProtoMessageAccumulator final = default;
            Assert.IsTrue(final.TryAdd(occurrence, ref copy));
            Assert.IsTrue(final.TryAdd(ReadOnlySpan<byte>.Empty, ref copy));
            WProtoMessageAccumulator refused = default;
            Assert.IsTrue(refused.TryAdd(occurrence, ref copy));
            Assert.IsFalse(refused.TryAdd(ReadOnlySpan<byte>.Empty, ref copy));
            CollectionAssert.AreEqual(occurrence, refused.Payload.ToArray());
            WProtoFormatterFailureTests.AssertCannotContinue(ref copy);
        }

        [Test]
        public void MalformedDuplicateDoesNotSpendMergeCopyBytes()
        {
            byte[] occurrence = { 0x08, 1 };
            WProtoReader parent = new WProtoReader(
                default,
                new WProtoReadLimits().WithMessageMergeCopyBytes(4)
            );
            WProtoReader copy = parent;
            WProtoMessageAccumulator accumulator = default;
            Assert.IsTrue(accumulator.TryAdd(occurrence, ref parent));
            Assert.IsFalse(accumulator.TryAdd(new byte[] { 0x08 }, ref parent));
            CollectionAssert.AreEqual(occurrence, accumulator.Payload.ToArray());
            WProtoFormatterFailureTests.AssertCannotContinue(ref parent);
            Assert.IsTrue(TryMergeCopyPair(ref copy, occurrence));
            Assert.IsFalse(copy.Malformed);
            Assert.IsFalse(TryMergeCopyPair(ref copy, occurrence));
            WProtoFormatterFailureTests.AssertCannotContinue(ref copy);
        }

        [Test]
        public void RefusedMergeCopyStillSpendsBoundaryValidationTagWork()
        {
            byte[] occurrence = { 0x08, 1 };
            WProtoReader parent = new WProtoReader(
                occurrence,
                new WProtoReadLimits().WithMessageMergeCopyBytes(3).WithTotalFieldCount(2)
            );
            WProtoReader copy = parent;
            Assert.IsFalse(TryMergeCopyPair(ref parent, occurrence));
            WProtoFormatterFailureTests.AssertCannotContinue(ref parent);
            Assert.IsFalse(copy.Malformed);
            Assert.IsFalse(copy.TryReadTag(out int tag, out int wire));
            Assert.AreEqual(0, tag);
            Assert.AreEqual(-1, wire);
            Assert.AreEqual(0, copy.Position);
            WProtoFormatterFailureTests.AssertCannotContinue(ref copy);
        }

        [TestCase(1, 3L)]
        [TestCase(2, 9L)]
        public void MergeCopyBudgetRefusesBeforeAllocatingOrGrowing(
            int acceptedOccurrences,
            long limit
        )
        {
            byte[] occurrence = { 0x08, 1 };
            WProtoReadLimits limits = new WProtoReadLimits().WithMessageMergeCopyBytes(limit);
            WProtoReader warmParent = new WProtoReader(default, limits);
            WProtoMessageAccumulator warm = default;
            for (int index = 0; index < acceptedOccurrences; ++index)
            {
                Assert.IsTrue(warm.TryAdd(occurrence, ref warmParent));
            }
            Assert.IsFalse(warm.TryAdd(occurrence, ref warmParent));
            WProtoReader parent = new WProtoReader(default, limits);
            WProtoMessageAccumulator accumulator = default;
            for (int index = 0; index < acceptedOccurrences; ++index)
            {
                Assert.IsTrue(accumulator.TryAdd(occurrence, ref parent));
            }
            byte[] beforePayload = accumulator.Payload.ToArray();
            long? before = BeginAllocationMeasurement();
            bool accepted = accumulator.TryAdd(occurrence, ref parent);
            long? allocated = EndAllocationMeasurement(before);
            Assert.IsFalse(accepted);
            AssertNoAllocationWhenMeasured(allocated);
            CollectionAssert.AreEqual(beforePayload, accumulator.Payload.ToArray());
            WProtoFormatterFailureTests.AssertCannotContinue(ref parent);
        }

        [TestCase(-1L, 0L)]
        [TestCase(0L, 0L)]
        [TestCase(4L, 4L)]
        [TestCase(2147483648L, 2147483648L)]
        [TestCase(long.MaxValue, long.MaxValue)]
        public void MergeCopyLimitPreservesConstructorSettingsAndFactoryComposition(
            long requested,
            long expected
        )
        {
            WProtoReadLimits[] originals =
            {
                new WProtoReadLimits(),
                new WProtoReadLimits(10, 9, 8, 7),
                new WProtoReadLimits(10, 9, 8, 7, 6),
                new WProtoReadLimits(1, default),
                new WProtoReadLimits(default, 1),
            };
            foreach (WProtoReadLimits original in originals)
            {
                Assert.AreEqual(long.MaxValue, original.MaximumMessageMergeCopyBytes);
                WProtoReadLimits first = original
                    .WithTotalFieldCount(3)
                    .WithMessageMergeCopyBytes(requested);
                WProtoReadLimits second = original
                    .WithMessageMergeCopyBytes(requested)
                    .WithTotalFieldCount(3);
                foreach (WProtoReadLimits limits in new[] { first, second })
                {
                    Assert.AreEqual(expected, limits.MaximumMessageMergeCopyBytes);
                    Assert.AreEqual(3, limits.MaximumTotalFieldCount);
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
                Assert.AreEqual(int.MaxValue, original.MaximumTotalFieldCount);
                Assert.AreEqual(
                    long.MaxValue,
                    first.WithMessageMergeCopyBytes(long.MaxValue).MaximumMessageMergeCopyBytes
                );
                Assert.AreEqual(
                    3,
                    first.WithMessageMergeCopyBytes(long.MaxValue).MaximumTotalFieldCount
                );
                Assert.AreEqual(
                    expected,
                    first.WithTotalFieldCount(int.MaxValue).MaximumMessageMergeCopyBytes
                );
            }
        }

        [TestCase(-1L, false)]
        [TestCase(0L, false)]
        [TestCase(2147483648L, true)]
        [TestCase(long.MaxValue - 1, true)]
        [TestCase(long.MaxValue, true)]
        public void MergeCopyBudgetNormalizationAndDisablingAffectActualReads(
            long limit,
            bool accepted
        )
        {
            WProtoReader parent = new WProtoReader(
                default,
                new WProtoReadLimits().WithMessageMergeCopyBytes(0).WithMessageMergeCopyBytes(limit)
            );
            Assert.AreEqual(accepted, TryMergeCopyPair(ref parent, new byte[] { 0x08, 1 }));
            Assert.AreEqual(!accepted, parent.Malformed);
        }

        [Test]
        public void MergeCopyLimitLeavesLegacyAccumulatorAndOrdinaryReadsUnchanged()
        {
            byte[] occurrence = { 0x08, 1 };
            WProtoMessageAccumulator legacy = default;
            Assert.IsTrue(legacy.TryAdd(occurrence));
            Assert.IsTrue(legacy.TryAdd(occurrence));
            CollectionAssert.AreEqual(new byte[] { 0x08, 1, 0x08, 1 }, legacy.Payload.ToArray());
            WProtoReader parent = new WProtoReader(
                new byte[] { 0x08, 1, 0x08, 2, 2, 3, 4, 1, 65 },
                new WProtoReadLimits().WithMessageMergeCopyBytes(0)
            );
            for (int index = 1; index <= 2; ++index)
            {
                Assert.IsTrue(parent.TryReadTag(out int tag, out int wire));
                Assert.AreEqual(1, tag);
                Assert.AreEqual(WProtoWireType.Varint, wire);
                Assert.IsTrue(parent.TryReadInt32(out int value));
                Assert.AreEqual(index, value);
            }
            Assert.IsTrue(parent.TryReadPackedRun(WProtoWireType.Varint, out WProtoReader packed));
            Assert.IsTrue(packed.TryReadInt32(out int first));
            Assert.IsTrue(packed.TryReadInt32(out int second));
            Assert.AreEqual(3, first);
            Assert.AreEqual(4, second);
            Assert.IsTrue(packed.End);
            Assert.IsTrue(parent.TryReadString(out string text));
            Assert.AreEqual("A", text);
            Assert.IsTrue(parent.End);
            Assert.IsFalse(parent.Malformed);
            Assert.IsFalse(TryMergeCopyPair(ref parent, occurrence));
            WProtoFormatterFailureTests.AssertCannotContinue(ref parent);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CopyAndPackedLimitsChargeIndependentResources(bool typed)
        {
            WProtoReader reader = new WProtoReader(
                new byte[] { 2, 1, 2 },
                new WProtoReadLimits(maximumPackedElementCount: 1).WithMessageMergeCopyBytes(4)
            );
            byte[] occurrence = { 0x08, 1 };
            Assert.IsTrue(TryMergeCopyPair(ref reader, occurrence));
            WProtoReader copy = reader;
            if (typed)
            {
                Assert.IsFalse(
                    reader.TryReadPackedRun(WProtoWireType.Varint, out WProtoReader packed)
                );
                Assert.IsTrue(packed.Malformed);
                WProtoFormatterFailureTests.AssertCannotContinue(ref reader);
            }
            else
            {
                Assert.IsTrue(reader.TryReadPackedRun(out WProtoReader packed));
                Assert.IsTrue(packed.TryReadInt32(out int first));
                Assert.AreEqual(1, first);
                Assert.IsFalse(packed.TryReadInt32(out int second));
                Assert.AreEqual(0, second);
                WProtoFormatterFailureTests.AssertCannotContinue(ref packed);
                Assert.IsFalse(reader.Malformed);
            }
            Assert.IsFalse(copy.Malformed);
            Assert.IsFalse(TryMergeCopyPair(ref copy, occurrence));
            WProtoFormatterFailureTests.AssertCannotContinue(ref copy);
        }

        [Test]
        public void DisablingMergeCopyLimitDoesNotAllocateReaderState()
        {
            byte[] payload = { 0x08, 1 };
            WProtoReadLimits limits = new WProtoReadLimits()
                .WithMessageMergeCopyBytes(0)
                .WithMessageMergeCopyBytes(long.MaxValue);
            WProtoReader warm = new WProtoReader(payload, limits);
            Assert.IsTrue(warm.TryReadTag(out _, out _));
            long? before = BeginAllocationMeasurement();
            WProtoReader reader = new WProtoReader(payload, limits);
            long? allocated = EndAllocationMeasurement(before);
            AssertNoAllocationWhenMeasured(allocated);
            Assert.IsTrue(reader.TryReadTag(out int tag, out int wire));
            Assert.AreEqual(1, tag);
            Assert.AreEqual(WProtoWireType.Varint, wire);
            Assert.IsTrue(reader.TryReadInt32(out int value));
            Assert.AreEqual(1, value);
            Assert.IsTrue(reader.End);
            Assert.IsFalse(reader.Malformed);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void UnlimitedReaderConstructionDoesNotAllocateSharedAccounting(int construction)
        {
            byte[] payload = { 8, 7 };
            WProtoReadLimits limits = construction == 1 ? null : WProtoReadLimits.Default;
            if (construction == 3)
            {
                limits = new WProtoReadLimits(maximumPackedElementCount: int.MaxValue)
                    .WithTotalFieldCount(0)
                    .WithMessageMergeCopyBytes(0)
                    .WithTotalFieldCount(int.MaxValue)
                    .WithMessageMergeCopyBytes(long.MaxValue);
            }
            WProtoReader warm =
                construction == 0 ? new WProtoReader(payload) : new WProtoReader(payload, limits);
            Assert.IsTrue(warm.TryReadTag(out _, out _));
            long? before = BeginAllocationMeasurement();
            WProtoReader reader =
                construction == 0 ? new WProtoReader(payload) : new WProtoReader(payload, limits);
            long? allocated = EndAllocationMeasurement(before);
            AssertNoAllocationWhenMeasured(allocated);
            Assert.IsTrue(reader.TryReadTag(out int tag, out int wire));
            Assert.AreEqual(1, tag);
            Assert.AreEqual(WProtoWireType.Varint, wire);
            Assert.IsTrue(reader.TryReadInt32(out int value));
            Assert.AreEqual(7, value);
            Assert.IsTrue(reader.End);
            Assert.IsFalse(reader.Malformed);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FormatterCannotReplaceTheReaderToResetOnlyTagOrPackedAccounting(bool packed)
        {
            WProtoReadLimits limits = packed
                ? new WProtoReadLimits(maximumPackedElementCount: 1)
                : new WProtoReadLimits().WithTotalFieldCount(1);
            IWProtoFormatter<LimitMarker> previous = WProtoFormatterProvider.TryGet(
                out IWProtoFormatter<LimitMarker> registered
            )
                ? registered
                : null;
            try
            {
                WProtoFormatterProvider.Register<LimitMarker>(
                    new RestartingCopyBudgetFormatter(limits)
                );
                Assert.Throws<InvalidOperationException>(() =>
                    WProtoFacade.TryDeserialize(ReadOnlySpan<byte>.Empty, limits, out LimitMarker _)
                );
            }
            finally
            {
                WProtoFormatterProvider.Register(previous);
            }
        }

        [Test]
        public void FormatterCannotReplaceTheReaderToResetOnlyTheMergeCopyBudget()
        {
            WProtoReadLimits limits = new WProtoReadLimits().WithMessageMergeCopyBytes(4);
            IWProtoFormatter<LimitMarker> previous = WProtoFormatterProvider.TryGet(
                out IWProtoFormatter<LimitMarker> registered
            )
                ? registered
                : null;
            try
            {
                WProtoFormatterProvider.Register<LimitMarker>(
                    new RestartingCopyBudgetFormatter(limits)
                );
                Assert.Throws<InvalidOperationException>(() =>
                    WProtoFacade.TryDeserialize(ReadOnlySpan<byte>.Empty, limits, out LimitMarker _)
                );
            }
            finally
            {
                WProtoFormatterProvider.Register(previous);
            }
        }

#if !UNITY_5_3_OR_NEWER
        [TestCase(0, 3L, false, "1202080112021002")]
        [TestCase(0, 4L, true, "1202080112021002")]
        [TestCase(1, 3L, false, "0A0208010A021002")]
        [TestCase(1, 4L, true, "0A0208010A021002")]
        [TestCase(2, 3L, false, "0A0A08011202080712020809")]
        [TestCase(2, 4L, true, "0A0A08011202080712020809")]
        [TestCase(3, 11L, false, "0A04120208010A0412021002")]
        [TestCase(3, 12L, true, "0A04120208010A0412021002")]
        [TestCase(4, 7L, false, "0A0A080112020807120208090A0A08021202080B1202080D")]
        [TestCase(4, 8L, true, "0A0A080112020807120208090A0A08021202080B1202080D")]
        public void GeneratedMessageMergesShareCopyBudgetAndPreserveSemantics(
            int route,
            long limit,
            bool accepted,
            string hex
        )
        {
            WProtoReader reader = new WProtoReader(
                ParseMergePayload(hex),
                new WProtoReadLimits().WithMessageMergeCopyBytes(limit)
            );
            switch (route)
            {
                case 0:
                    Assert.AreEqual(
                        accepted,
                        WProtoFormatterProvider
                            .Get<HostContracts.DuplicateHolder>()
                            .TryRead(ref reader, out HostContracts.DuplicateHolder holder)
                    );
                    if (accepted)
                    {
                        Assert.AreEqual(1, holder.Child.A);
                        Assert.AreEqual(2, holder.Child.B);
                    }
                    else
                    {
                        Assert.IsTrue(holder == null);
                    }
                    break;
                case 1:
                    Assert.AreEqual(
                        accepted,
                        WProtoFormatterProvider
                            .Get<HostContracts.Box<HostContracts.DuplicateChild>>()
                            .TryRead(
                                ref reader,
                                out HostContracts.Box<HostContracts.DuplicateChild> box
                            )
                    );
                    if (accepted)
                    {
                        Assert.AreEqual(1, box.Value.A);
                        Assert.AreEqual(2, box.Value.B);
                    }
                    else
                    {
                        Assert.IsTrue(box == null);
                    }
                    break;
                case 2:
                case 4:
                    int before = HostContracts.HookedContract.AfterDeserializationRuns;
                    Assert.AreEqual(
                        accepted,
                        WProtoFormatterProvider
                            .Get<HostContracts.HookedMapContract>()
                            .TryRead(ref reader, out HostContracts.HookedMapContract map)
                    );
                    Assert.AreEqual(
                        route == 4 ? (accepted ? 2 : 1) : (accepted ? 1 : 0),
                        HostContracts.HookedContract.AfterDeserializationRuns - before
                    );
                    if (accepted)
                    {
                        Assert.AreEqual(route == 4 ? 2 : 1, map.ById.Count);
                        Assert.IsTrue(
                            map.ById.TryGetValue(1, out HostContracts.HookedContract child)
                        );
                        Assert.AreEqual(9, child.Value);
                        if (route == 4)
                        {
                            Assert.IsTrue(
                                map.ById.TryGetValue(2, out HostContracts.HookedContract second)
                            );
                            Assert.AreEqual(13, second.Value);
                        }
                    }
                    else
                    {
                        Assert.IsTrue(map == null);
                    }
                    break;
                default:
                    Assert.AreEqual(
                        accepted,
                        WProtoFormatterProvider
                            .Get<HostContracts.DuplicateGrandparent>()
                            .TryRead(ref reader, out HostContracts.DuplicateGrandparent grandparent)
                    );
                    if (accepted)
                    {
                        Assert.AreEqual(1, grandparent.Holder.Child.A);
                        Assert.AreEqual(2, grandparent.Holder.Child.B);
                    }
                    else
                    {
                        Assert.IsTrue(grandparent == null);
                    }
                    break;
            }
            Assert.AreEqual(!accepted, reader.Malformed);
            if (accepted)
            {
                Assert.IsTrue(reader.End);
            }
            else
            {
                WProtoFormatterFailureTests.AssertCannotContinue(ref reader);
            }
        }

        [TestCase(0, 4L, false)]
        [TestCase(0, 8L, true)]
        [TestCase(1, 4L, false)]
        [TestCase(1, 8L, true)]
        [TestCase(2, 4L, false)]
        [TestCase(2, 8L, true)]
        public void FormatterReaderRoutesSharePreviouslySpentMergeCopyBytes(
            int route,
            long limit,
            bool accepted
        )
        {
            byte[] payload = ParseMergePayload("1202080112021002");
            WProtoReader reader = new WProtoReader(
                ParseMergePayload("081202080112021002"),
                new WProtoReadLimits().WithMessageMergeCopyBytes(limit)
            );
            Assert.IsTrue(TryMergeCopyPair(ref reader, new byte[] { 0x08, 1 }));
            IWProtoFormatter<HostContracts.DuplicateHolder> formatter =
                WProtoFormatterProvider.Get<HostContracts.DuplicateHolder>();
            bool result;
            HostContracts.DuplicateHolder value;
            switch (route)
            {
                case 0:
                    result = reader.TryReadMessage(formatter, out value);
                    break;
                case 1:
                    result = reader.TryReadMessage(payload, formatter, out value);
                    break;
                default:
                    HostContracts.DuplicateHolder seed = new HostContracts.DuplicateHolder
                    {
                        Number = 99,
                    };
                    result = reader.TryReadMessage(payload, formatter, in seed, out value);
                    break;
            }
            Assert.AreEqual(accepted, result);
            Assert.AreEqual(!accepted, reader.Malformed);
            if (accepted)
            {
                Assert.AreEqual(route == 2 ? 99 : 0, value.Number);
                Assert.AreEqual(1, value.Child.A);
                Assert.AreEqual(2, value.Child.B);
            }
            else
            {
                Assert.IsTrue(value == null);
                WProtoFormatterFailureTests.AssertCannotContinue(ref reader);
            }
        }
#endif

        private readonly struct RestartingCopyBudgetFormatter : IWProtoFormatter<LimitMarker>
        {
            private readonly WProtoReadLimits _limits;

            internal RestartingCopyBudgetFormatter(WProtoReadLimits limits)
            {
                _limits = limits;
            }

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
                reader = new WProtoReader(ReadOnlySpan<byte>.Empty, _limits);
                value = default;
                return true;
            }
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
