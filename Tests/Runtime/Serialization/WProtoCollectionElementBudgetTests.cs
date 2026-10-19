// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [TestFixture]
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class WProtoCollectionElementBudgetTests
    {
        private static void AssertBudget<T>(T expected, int count)
        {
            byte[] bytes = WProtoFacade.Serialize(expected);
            T actual = WProtoFacade.Deserialize<T>(
                bytes,
                new WProtoReadLimits().WithCollectionElementCount(count)
            );
            CollectionAssert.AreEqual(bytes, WProtoFacade.Serialize(actual));
            Assert.Throws<SerializationCorruptDataException>(() =>
                WProtoFacade.Deserialize<T>(
                    bytes,
                    new WProtoReadLimits().WithCollectionElementCount(count - 1)
                )
            );
        }

        private static bool ExerciseDisabledAllowance(WProtoReadLimits limits)
        {
            for (int index = 0; index < 128; ++index)
            {
                WProtoReader reader = new WProtoReader(Array.Empty<byte>(), limits);
                if (!reader.TryChargeCollectionElements(int.MaxValue))
                {
                    return false;
                }
            }
            return true;
        }

        private static WProtoCollectionBudgetContract CreateCase(int shape)
        {
            switch (shape)
            {
                case 0:
                    return new WProtoCollectionBudgetContract
                    {
                        Numbers = new List<int> { 1, 2, 3 },
                    };
                case 1:
                    return new WProtoCollectionBudgetContract
                    {
                        Texts = new List<string> { "", "A", "B" },
                    };
                case 2:
                    return new WProtoCollectionBudgetContract
                    {
                        Values = new Dictionary<string, WProtoCollectionBudgetContract>
                        {
                            { "A", new WProtoCollectionBudgetContract() },
                            { "B", new WProtoCollectionBudgetContract() },
                        },
                    };
                case 3:
                    return new WProtoCollectionBudgetContract
                    {
                        Children = new List<WProtoCollectionBudgetContract>
                        {
                            new WProtoCollectionBudgetContract(),
                            new WProtoCollectionBudgetContract(),
                        },
                    };
                case 4:
                    return new WProtoCollectionBudgetContract
                    {
                        Grid = new int[,]
                        {
                            { 1, 2 },
                            { 3, 4 },
                        },
                    };
                case 5:
                    return new WProtoCollectionBudgetContract
                    {
                        Groups = new List<List<int>>
                        {
                            new List<int> { 1, 2 },
                            new List<int> { 3 },
                        },
                    };
                case 6:
                    return new WProtoCollectionBudgetContract
                    {
                        Unique = new HashSet<int> { 1, 2, 3 },
                    };
                default:
                    return new WProtoCollectionBudgetContract
                    {
                        Numbers = new List<int> { 1, 2 },
                        Texts = new List<string> { "A", "B" },
                        Values = new Dictionary<string, WProtoCollectionBudgetContract>
                        {
                            { "A", new WProtoCollectionBudgetContract() },
                        },
                        Child = new WProtoCollectionBudgetContract
                        {
                            Texts = new List<string> { "C" },
                        },
                    };
            }
        }

        private static bool RefusesContract(byte[] bytes, WProtoReadLimits limits)
        {
            WProtoReader reader = new WProtoReader(bytes, limits);
            bool accepted = WProtoCollectionBudgetContract.WProtoFormatter.Instance.TryRead(
                ref reader,
                out WProtoCollectionBudgetContract value
            );
            return !accepted && value == null && reader.Malformed;
        }

        [TestCase(int.MinValue, 0)]
        [TestCase(-1, 0)]
        [TestCase(0, 0)]
        [TestCase(7, 7)]
        [TestCase(int.MaxValue, int.MaxValue)]
        public void ImmutableAllowanceNormalizesNegatives(int requested, int expected)
        {
            WProtoReadLimits original = new WProtoReadLimits();
            Assert.AreEqual(
                expected,
                original.WithCollectionElementCount(requested).MaximumCollectionElementCount
            );
            Assert.AreEqual(int.MaxValue, original.MaximumCollectionElementCount);
            Assert.AreEqual(
                int.MaxValue,
                new WProtoReadLimits(1, 2, 3, 4).MaximumCollectionElementCount
            );
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void BuilderOrderPreservesEverySetting(int order)
        {
            WProtoReadLimits limits = new WProtoReadLimits(101, 102, 103, 4, 105);
            if (order == 0)
            {
                limits = limits.WithCollectionElementCount(7);
            }
            limits = limits.WithTotalFieldCount(11);
            if (order == 1)
            {
                limits = limits.WithCollectionElementCount(7);
            }
            limits = limits.WithMessageMergeCopyBytes(13);
            if (order == 2)
            {
                limits = limits.WithCollectionElementCount(7);
            }
            limits = limits.WithDecodedPayloadBytes(17);
            if (order == 3)
            {
                limits = limits.WithCollectionElementCount(7);
            }
            Assert.AreEqual(7, limits.MaximumCollectionElementCount);
            Assert.AreEqual(11, limits.MaximumTotalFieldCount);
            Assert.AreEqual(13, limits.MaximumMessageMergeCopyBytes);
            Assert.AreEqual(17, limits.MaximumDecodedPayloadBytes);
            Assert.AreEqual(101, limits.MaximumMessageBytes);
            Assert.AreEqual(102, limits.MaximumLengthDelimitedBytes);
            Assert.AreEqual(103, limits.MaximumFieldCount);
            Assert.AreEqual(4, limits.MaximumNestingDepth);
            Assert.AreEqual(105, limits.MaximumPackedElementCount);
        }

        [TestCase(-1, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(3, false)]
        [TestCase(int.MaxValue, false)]
        public void DirectChargesAreCheckedBeforeSubtraction(int count, bool expected)
        {
            WProtoReader reader = new WProtoReader(
                Array.Empty<byte>(),
                new WProtoReadLimits().WithCollectionElementCount(2)
            );
            Assert.AreEqual(expected, reader.TryChargeCollectionElements(count));
            Assert.AreEqual(!expected, reader.Malformed);
            Assert.AreEqual(0, reader.Position);
            if (!expected)
            {
                Assert.IsFalse(reader.TryChargeCollectionElements(0));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void CopiesAndDerivedReadersShareTheAllowance(int route)
        {
            WProtoReader parent = new WProtoReader(
                new byte[] { 0 },
                new WProtoReadLimits().WithCollectionElementCount(1)
            );
            if (route == 1)
            {
                WProtoReader derived = new WProtoReader(Array.Empty<byte>(), in parent);
                Assert.IsTrue(derived.TryChargeCollectionElements(1));
                Assert.IsFalse(parent.TryChargeCollectionElements(1));
                Assert.IsTrue(parent.Malformed);
                return;
            }
            WProtoReader child;
            switch (route)
            {
                case 0:
                    child = parent;
                    break;
                case 2:
                    Assert.IsTrue(parent.TryReadMessage(out child));
                    break;
                case 3:
                    Assert.IsTrue(parent.TryReadPackedRun(out child));
                    break;
                default:
                    Assert.IsTrue(parent.TryReadPackedRun(WProtoWireType.Varint, out child));
                    break;
            }
            Assert.IsTrue(child.TryChargeCollectionElements(1));
            Assert.IsFalse(parent.TryChargeCollectionElements(1));
            Assert.IsTrue(parent.Malformed);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DetachedFormatterReadsShareTheAllowance(bool seeded)
        {
            WProtoReader parent = new WProtoReader(
                Array.Empty<byte>(),
                new WProtoReadLimits().WithCollectionElementCount(1)
            );
            ChargingFormatter formatter = new ChargingFormatter();
            Assert.IsTrue(parent.TryReadMessage(Array.Empty<byte>(), formatter, out int first));
            Assert.AreEqual(1, first);
            bool accepted = seeded
                ? parent.TryReadMessage(Array.Empty<byte>(), formatter, 3, out _)
                : parent.TryReadMessage(Array.Empty<byte>(), formatter, out _);
            Assert.IsFalse(accepted);
            Assert.IsTrue(parent.Malformed);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompletedReaderRequiresTheSameFiniteBudget(bool finite)
        {
            WProtoReadLimits limits = new WProtoReadLimits().WithCollectionElementCount(
                finite ? 1 : int.MaxValue
            );
            WProtoReader expected = new WProtoReader(Array.Empty<byte>(), limits);
            WProtoReader replaced = new WProtoReader(Array.Empty<byte>(), limits);
            Assert.AreEqual(!finite, WProtoReader.ReadCompleted(true, in replaced, in expected));
        }

        [Test]
        public void RootBudgetsAreIndependentAndAcceptedChargesAreNotRefunded()
        {
            WProtoReadLimits limits = new WProtoReadLimits().WithCollectionElementCount(1);
            WProtoReader first = new WProtoReader(Array.Empty<byte>(), limits);
            WProtoReader second = new WProtoReader(Array.Empty<byte>(), limits);
            WProtoReader replay = first;
            Assert.IsTrue(first.TryChargeCollectionElements(1));
            Assert.IsTrue(second.TryChargeCollectionElements(1));
            Assert.IsFalse(replay.TryChargeCollectionElements(1));
        }

        [TestCase(0, 3, TestName = "CollectionBudget.PackedIntegers")]
        [TestCase(1, 3, TestName = "CollectionBudget.EmptyAndNonemptyStrings")]
        [TestCase(2, 2, TestName = "CollectionBudget.MapEntries")]
        [TestCase(3, 2, TestName = "CollectionBudget.Messages")]
        [TestCase(4, 4, TestName = "CollectionBudget.RectangularValues")]
        [TestCase(5, 5, TestName = "CollectionBudget.OuterAndInnerCollections")]
        [TestCase(6, 3, TestName = "CollectionBudget.SetElements")]
        [TestCase(7, 6, TestName = "CollectionBudget.FieldsAndDescendants")]
        public void GeneratedShapesUseOneCumulativeAllowance(int shape, int count)
        {
            WProtoCollectionBudgetContract expected = CreateCase(shape);
            AssertBudget(expected, count);
            Assert.IsTrue(
                RefusesContract(
                    WProtoFacade.Serialize(expected),
                    new WProtoReadLimits().WithCollectionElementCount(count - 1)
                )
            );
        }

        [Test]
        public void GenericAndRootCollectionsChargeTheirElements()
        {
            AssertBudget(
                new WProtoCollectionBudgetGenericContract<int>
                {
                    Values = new List<int> { 1, 2 },
                },
                2
            );
            AssertBudget(
                new WProtoCollectionBudgetGenericContract<string>
                {
                    Values = new List<string> { "A", "B" },
                },
                2
            );
            AssertBudget(new List<int> { 1, 2 }, 2);
            AssertBudget(new List<string> { "A", "B" }, 2);
        }

        [Test]
        public void UnsupportedNullableRepeatedElementsKeepTheirTypeFailure()
        {
            Assert.Throws<SerializationTypeException>(() =>
                WProtoFacade.Serialize(
                    new WProtoCollectionBudgetGenericContract<int?>
                    {
                        Values = new List<int?> { 1, 0 },
                    }
                )
            );
        }

        [Test]
        public void ConstructorSeedsAreExcludedFromDeliveredElementCharges()
        {
            WProtoCollectionBudgetSeedContract empty =
                WProtoFacade.Deserialize<WProtoCollectionBudgetSeedContract>(
                    new byte[] { 0x10, 1 },
                    new WProtoReadLimits().WithCollectionElementCount(0)
                );
            CollectionAssert.AreEqual(new[] { 7, 8 }, empty.Values);
            byte[] bytes = { 0x08, 9 };
            WProtoCollectionBudgetSeedContract appended =
                WProtoFacade.Deserialize<WProtoCollectionBudgetSeedContract>(
                    bytes,
                    new WProtoReadLimits().WithCollectionElementCount(1)
                );
            CollectionAssert.AreEqual(new[] { 7, 8, 9 }, appended.Values);
            Assert.Throws<SerializationCorruptDataException>(() =>
                WProtoFacade.Deserialize<WProtoCollectionBudgetSeedContract>(
                    bytes,
                    new WProtoReadLimits().WithCollectionElementCount(0)
                )
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DuplicateOccurrencesSpendAllowanceEvenWhenOutputDeduplicates(bool map)
        {
            byte[] bytes = map ? new byte[] { 0x1A, 0, 0x1A, 0 } : new byte[] { 0x38, 1, 0x38, 1 };
            WProtoCollectionBudgetContract value =
                WProtoFacade.Deserialize<WProtoCollectionBudgetContract>(
                    bytes,
                    new WProtoReadLimits().WithCollectionElementCount(2)
                );
            Assert.AreEqual(1, map ? value.Values.Count : value.Unique.Count);
            Assert.IsTrue(
                RefusesContract(bytes, new WProtoReadLimits().WithCollectionElementCount(1))
            );
        }

        [Test]
        public void SplitPackedAndUnpackedRunsSpendTheSameAllowance()
        {
            byte[] bytes = { 0x08, 1, 0x0A, 2, 2, 3, 0x08, 4 };
            WProtoCollectionBudgetContract value =
                WProtoFacade.Deserialize<WProtoCollectionBudgetContract>(
                    bytes,
                    new WProtoReadLimits().WithCollectionElementCount(4)
                );
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, value.Numbers);
            Assert.IsTrue(
                RefusesContract(bytes, new WProtoReadLimits().WithCollectionElementCount(3))
            );
        }

        [TestCase(1, 2)]
        [TestCase(2, 1)]
        public void PackedAndCollectionAllowancesAreIndependent(int packed, int collection)
        {
            byte[] bytes = { 0x0A, 2, 1, 2 };
            Assert.IsTrue(
                RefusesContract(
                    bytes,
                    new WProtoReadLimits(
                        maximumPackedElementCount: packed
                    ).WithCollectionElementCount(collection)
                )
            );
        }

        [Test]
        public void BytePayloadsScalarTextUnknownFieldsAndEmptyCollectionsCostNoElements()
        {
            WProtoCollectionBudgetContract expected = new WProtoCollectionBudgetContract
            {
                Data = new byte[] { 1, 2 },
                Text = "AB",
                Numbers = new List<int>(),
                Grid = new int[0, 3],
            };
            byte[] bytes = WProtoFacade.Serialize(expected);
            WProtoCollectionBudgetContract actual =
                WProtoFacade.Deserialize<WProtoCollectionBudgetContract>(
                    bytes,
                    new WProtoReadLimits().WithCollectionElementCount(0)
                );
            CollectionAssert.AreEqual(bytes, WProtoFacade.Serialize(actual));
            WProtoReader reader = new WProtoReader(
                new byte[] { 0xA0, 0x06, 1 },
                new WProtoReadLimits().WithCollectionElementCount(0)
            );
            Assert.IsTrue(reader.TryReadTag(out int field, out int wire));
            Assert.IsTrue(reader.TrySkipField(field, wire));
            Assert.IsTrue(reader.End);
        }

        [Test]
        public void ServedBudgetRefusalKeepsTheDispatchFailureContract()
        {
            byte[] bytes = { 0x12, 0 };
            Assert.Throws<InvalidOperationException>(() =>
                WProtoFacade.TryDeserialize<WProtoCollectionBudgetContract>(
                    bytes,
                    new WProtoReadLimits().WithCollectionElementCount(0),
                    out _
                )
            );
        }

        [TestCase(WProtoWireType.Varint, 2, 1, false)]
        [TestCase(WProtoWireType.Varint, 2, 2, true)]
        [TestCase(WProtoWireType.Fixed32, 8, 1, false)]
        [TestCase(WProtoWireType.Fixed32, 8, 2, true)]
        [TestCase(WProtoWireType.Fixed64, 16, 1, false)]
        [TestCase(WProtoWireType.Fixed64, 16, 2, true)]
        [TestCase(WProtoWireType.Varint, 0, 0, true)]
        [TestCase(WProtoWireType.LengthDelimited, 0, 0, false)]
        public void PackedCollectionChargesDeliveredElementsBeforeReturningTheRegion(
            int wire,
            int length,
            int maximum,
            bool expected
        )
        {
            byte[] bytes = new byte[length + 1];
            bytes[0] = (byte)length;
            WProtoReader reader = new WProtoReader(
                bytes,
                new WProtoReadLimits().WithCollectionElementCount(maximum)
            );
            Assert.AreEqual(
                expected,
                reader.TryReadPackedCollection(wire, out WProtoReader packed)
            );
            Assert.AreEqual(!expected, reader.Malformed);
            Assert.AreEqual(!expected, packed.Malformed);
            Assert.AreEqual(bytes.Length, reader.Position);
            if (expected)
            {
                int count =
                    wire == WProtoWireType.Fixed32 ? length / 4
                    : wire == WProtoWireType.Fixed64 ? length / 8
                    : length;
                Assert.AreEqual(count, packed.CountPackedElements(wire));
                while (!packed.End)
                {
                    bool read =
                        wire == WProtoWireType.Fixed32 ? packed.TryReadFixed32(out _)
                        : wire == WProtoWireType.Fixed64 ? packed.TryReadFixed64(out _)
                        : packed.TryReadVarint64(out _);
                    Assert.IsTrue(read);
                }
                Assert.IsFalse(reader.TryChargeCollectionElements(1));
            }
        }

        [Test]
        public void LargestFiniteAllowanceDoesNotOverflow()
        {
            WProtoReader reader = new WProtoReader(
                Array.Empty<byte>(),
                new WProtoReadLimits().WithCollectionElementCount(int.MaxValue - 1)
            );
            Assert.IsTrue(reader.TryChargeCollectionElements(int.MaxValue - 2));
            Assert.IsTrue(reader.TryChargeCollectionElements(1));
            Assert.IsFalse(reader.TryChargeCollectionElements(1));
            Assert.IsTrue(reader.Malformed);
        }

        [Test]
        public void ARefusedChargeDoesNotSpendTheRemainingAllowance()
        {
            WProtoReader reader = new WProtoReader(
                Array.Empty<byte>(),
                new WProtoReadLimits().WithCollectionElementCount(2)
            );
            WProtoReader prior = reader;
            Assert.IsFalse(reader.TryChargeCollectionElements(3));
            Assert.IsTrue(prior.TryChargeCollectionElements(2));
            Assert.IsFalse(prior.TryChargeCollectionElements(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisabledAllowanceAllocatesNoAccountingState(bool explicitUnlimited)
        {
            WProtoReadLimits limits = explicitUnlimited
                ? new WProtoReadLimits().WithCollectionElementCount(int.MaxValue)
                : new WProtoReadLimits();
            Assert.IsTrue(ExerciseDisabledAllowance(limits));
#if ENABLE_IL2CPP && !UNITY_6000_0_OR_NEWER
            Assert.Inconclusive(
                "The legacy IL2CPP allocation counter is unsafe to call. Functional default reads were checked; the allocation bound is unqualified."
            );
#else
            long before = GC.GetAllocatedBytesForCurrentThread();
            byte[] positive = new byte[65536];
            positive[0] = 1;
            long sensitivity = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(positive);
            if (sensitivity < positive.Length)
            {
                Assert.Inconclusive(
                    "The allocation counter missed the positive allocation control."
                );
            }
            Assert.IsTrue(ExerciseDisabledAllowance(limits));
            before = GC.GetAllocatedBytesForCurrentThread();
            bool completed = ExerciseDisabledAllowance(limits);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(completed);
            Assert.AreEqual(0, allocated);
#endif
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RefusalPrecedesCollectionMaterialization(bool text)
        {
            const int length = 1 << 16;
            WProtoCollectionBudgetContract original = text
                ? new WProtoCollectionBudgetContract
                {
                    Texts = new List<string> { new string('A', length) },
                }
                : new WProtoCollectionBudgetContract { Numbers = new List<int>(new int[length]) };
            byte[] bytes = WProtoFacade.Serialize(original);
            WProtoReadLimits limits = new WProtoReadLimits().WithCollectionElementCount(0);
            Assert.IsTrue(RefusesContract(bytes, limits));
#if ENABLE_IL2CPP && !UNITY_6000_0_OR_NEWER
            Assert.Inconclusive(
                "The legacy IL2CPP allocation counter is unsafe to call. Functional refusal was checked; the allocation bound is unqualified."
            );
#else
            long before = GC.GetAllocatedBytesForCurrentThread();
            byte[] positive = new byte[length];
            positive[0] = 1;
            long sensitivity = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(positive);
            if (sensitivity < length)
            {
                Assert.Inconclusive(
                    "The allocation counter missed the positive allocation control."
                );
            }
            Assert.IsTrue(RefusesContract(bytes, limits));
            before = GC.GetAllocatedBytesForCurrentThread();
            bool refused = RefusesContract(bytes, limits);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(refused);
            Assert.Less(allocated, length / 16);
#endif
        }

        private readonly struct ChargingFormatter
            : IWProtoFormatter<int>,
                IWProtoMergeFormatter<int>
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
                bool accepted = reader.TryChargeCollectionElements(1);
                value = accepted ? 1 : 0;
                return accepted;
            }

            public bool TryReadInto(ref WProtoReader reader, in int seed, out int value)
            {
                return TryRead(ref reader, out value);
            }
        }
    }
}
