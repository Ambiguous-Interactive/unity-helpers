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
#if !UNITY_5_3_OR_NEWER
    [NonParallelizable]
#endif
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class WProtoContractConstructionBudgetTests
    {
        private static void AssertNoSideEffects()
        {
            Assert.AreEqual(0, WProtoConstructionBudgetEvents.Initializations);
            Assert.AreEqual(0, WProtoConstructionBudgetEvents.Constructors);
            Assert.AreEqual(0, WProtoConstructionBudgetEvents.BeforeReads);
            Assert.AreEqual(0, WProtoConstructionBudgetEvents.AfterReads);
            Assert.AreEqual(0, WProtoConstructionBudgetEvents.Assignments);
        }

        private static void AssertBudget<T>(T expected, int count)
        {
            byte[] bytes = WProtoFacade.Serialize(expected);
            T actual = WProtoFacade.Deserialize<T>(
                bytes,
                new WProtoReadLimits().WithContractConstructionCount(count)
            );
            CollectionAssert.AreEqual(bytes, WProtoFacade.Serialize(actual));
            Assert.Throws<SerializationCorruptDataException>(() =>
                WProtoFacade.Deserialize<T>(
                    bytes,
                    new WProtoReadLimits().WithContractConstructionCount(count - 1)
                )
            );
        }

        private static bool ExerciseAllowance(WProtoReadLimits limits)
        {
            for (int index = 0; index < 128; ++index)
            {
                WProtoReader reader = new WProtoReader(Array.Empty<byte>(), limits);
                if (!reader.TryChargeContractConstructions(1))
                {
                    return false;
                }
            }
            return true;
        }

        [SetUp]
        public void SetUp()
        {
            WProtoConstructionBudgetEvents.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            WProtoConstructionBudgetEvents.Reset();
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
                original.WithContractConstructionCount(requested).MaximumContractConstructionCount
            );
            Assert.AreEqual(int.MaxValue, original.MaximumContractConstructionCount);
            Assert.AreEqual(
                int.MaxValue,
                new WProtoReadLimits(1, 2, 3, 4).MaximumContractConstructionCount
            );
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void BuilderOrderPreservesEverySetting(int order)
        {
            WProtoReadLimits limits = new WProtoReadLimits(101, 102, 103, 4, 105);
            if (order == 0)
            {
                limits = limits.WithContractConstructionCount(7);
            }
            limits = limits.WithTotalFieldCount(11);
            if (order == 1)
            {
                limits = limits.WithContractConstructionCount(7);
            }
            limits = limits.WithMessageMergeCopyBytes(13);
            if (order == 2)
            {
                limits = limits.WithContractConstructionCount(7);
            }
            limits = limits.WithDecodedPayloadBytes(17);
            if (order == 3)
            {
                limits = limits.WithContractConstructionCount(7);
            }
            limits = limits.WithCollectionElementCount(19);
            if (order == 4)
            {
                limits = limits.WithContractConstructionCount(7);
            }
            Assert.AreEqual(7, limits.MaximumContractConstructionCount);
            Assert.AreEqual(11, limits.MaximumTotalFieldCount);
            Assert.AreEqual(13, limits.MaximumMessageMergeCopyBytes);
            Assert.AreEqual(17, limits.MaximumDecodedPayloadBytes);
            Assert.AreEqual(19, limits.MaximumCollectionElementCount);
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
        public void DirectChargesAreCheckedBeforeSubtraction(int count, bool accepted)
        {
            WProtoReader reader = new WProtoReader(
                Array.Empty<byte>(),
                new WProtoReadLimits().WithContractConstructionCount(2)
            );
            WProtoReader copy = reader;
            Assert.AreEqual(accepted, reader.TryChargeContractConstructions(count));
            Assert.AreEqual(!accepted, reader.Malformed);
            Assert.AreEqual(0, reader.Position);
            Assert.IsTrue(copy.TryChargeContractConstructions(accepted ? 2 - count : 2));
            Assert.IsFalse(copy.TryChargeContractConstructions(1));
            Assert.IsTrue(copy.Malformed);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisabledAllowanceDoesNotAccumulateCharges(bool explicitMaximum)
        {
            WProtoReadLimits limits = new WProtoReadLimits();
            if (explicitMaximum)
            {
                limits = limits.WithContractConstructionCount(int.MaxValue);
            }
            WProtoReader reader = new WProtoReader(Array.Empty<byte>(), limits);
            Assert.IsTrue(reader.TryChargeContractConstructions(int.MaxValue));
            Assert.IsTrue(reader.TryChargeContractConstructions(int.MaxValue));
            Assert.IsFalse(reader.Malformed);
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
                new WProtoReadLimits().WithContractConstructionCount(1)
            );
            if (route == 1)
            {
                WProtoReader derived = new WProtoReader(Array.Empty<byte>(), in parent);
                Assert.IsTrue(derived.TryChargeContractConstructions(1));
                Assert.IsFalse(parent.TryChargeContractConstructions(1));
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
            Assert.IsTrue(child.TryChargeContractConstructions(1));
            Assert.IsFalse(parent.TryChargeContractConstructions(1));
            Assert.IsTrue(parent.Malformed);
        }

        [Test]
        public void ReusingLimitsKeepsIndependentRoots()
        {
            WProtoReadLimits limits = new WProtoReadLimits().WithContractConstructionCount(1);
            WProtoReader first = new WProtoReader(Array.Empty<byte>(), limits);
            WProtoReader second = new WProtoReader(Array.Empty<byte>(), limits);
            Assert.IsTrue(first.TryChargeContractConstructions(1));
            Assert.IsTrue(second.TryChargeContractConstructions(1));
            Assert.IsFalse(first.TryChargeContractConstructions(1));
            Assert.IsFalse(second.Malformed);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FormatterCannotReplaceTheAccountingIdentity(bool finite)
        {
            WProtoReadLimits limits = new WProtoReadLimits().WithContractConstructionCount(
                finite ? 1 : int.MaxValue
            );
            WProtoReader expected = new WProtoReader(ReadOnlySpan<byte>.Empty, limits);
            WProtoReader replaced = new WProtoReader(ReadOnlySpan<byte>.Empty, limits);
            Assert.AreEqual(!finite, WProtoReader.ReadCompleted(true, in replaced, in expected));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void RefusalPrecedesConstructorInitializerSetterAndHooks(int route)
        {
            byte[] bytes = route == 0 ? Array.Empty<byte>() : new byte[] { 8, 1 };
            WProtoReadLimits limits =
                route == 2
                    ? new WProtoReadLimits(maximumMessageBytes: 0)
                    : new WProtoReadLimits().WithContractConstructionCount(0);
            WProtoReader reader = new WProtoReader(bytes, limits);
            Assert.IsFalse(
                WProtoConstructionBudgetContract.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out WProtoConstructionBudgetContract value
                )
            );
            Assert.IsTrue(value == null);
            Assert.IsTrue(reader.Malformed);
            Assert.AreEqual(0, reader.Position);
            AssertNoSideEffects();
        }

        [Test]
        public void ExactRootAllowanceConstructsOnceAndKeepsWireBytes()
        {
            byte[] bytes = { 8, 1 };
            WProtoConstructionBudgetContract value =
                WProtoFacade.Deserialize<WProtoConstructionBudgetContract>(
                    bytes,
                    new WProtoReadLimits().WithContractConstructionCount(1)
                );
            Assert.AreEqual(1, value.Value);
            CollectionAssert.AreEqual(bytes, WProtoFacade.Serialize(value));
            Assert.AreEqual(1, WProtoConstructionBudgetEvents.Initializations);
            Assert.AreEqual(1, WProtoConstructionBudgetEvents.Constructors);
            Assert.AreEqual(1, WProtoConstructionBudgetEvents.BeforeReads);
            Assert.AreEqual(1, WProtoConstructionBudgetEvents.AfterReads);
            Assert.AreEqual(1, WProtoConstructionBudgetEvents.Assignments);
        }

        [Test]
        public void MalformedPayloadDoesNotRefundConstruction()
        {
            WProtoReader reader = new WProtoReader(
                new byte[] { 8 },
                new WProtoReadLimits().WithContractConstructionCount(1)
            );
            WProtoReader copy = reader;
            Assert.IsFalse(
                WProtoConstructionBudgetContract.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out WProtoConstructionBudgetContract value
                )
            );
            Assert.IsTrue(value == null);
            Assert.IsTrue(reader.Malformed);
            Assert.AreEqual(1, WProtoConstructionBudgetEvents.Constructors);
            Assert.IsFalse(copy.TryChargeContractConstructions(1));
        }

        [Test]
        public void SuppliedMergeSeedCostsNoConstruction()
        {
            WProtoConstructionBudgetContract seed = new WProtoConstructionBudgetContract();
            WProtoConstructionBudgetEvents.Reset();
            WProtoReader reader = new WProtoReader(
                new byte[] { 8, 3 },
                new WProtoReadLimits().WithContractConstructionCount(0)
            );
            Assert.IsTrue(
                WProtoConstructionBudgetContract.WProtoFormatter.Instance.TryReadInto(
                    ref reader,
                    seed,
                    out WProtoConstructionBudgetContract value
                )
            );
            Assert.AreSame(seed, value);
            Assert.AreEqual(3, value.Value);
            Assert.AreEqual(0, WProtoConstructionBudgetEvents.Initializations);
            Assert.AreEqual(0, WProtoConstructionBudgetEvents.Constructors);
            Assert.AreEqual(1, WProtoConstructionBudgetEvents.BeforeReads);
            Assert.AreEqual(1, WProtoConstructionBudgetEvents.AfterReads);
        }

        [TestCase(0, 1)]
        [TestCase(1, 2)]
        [TestCase(2, 4)]
        public void EmptyNestedContractsSpendSharedAllowance(int shape, int count)
        {
            WProtoConstructionBudgetContract value = new WProtoConstructionBudgetContract();
            if (shape == 1)
            {
                value.Child = new WProtoConstructionBudgetContract();
            }
            if (shape == 2)
            {
                value.Children = new List<WProtoConstructionBudgetContract>
                {
                    new WProtoConstructionBudgetContract(),
                    new WProtoConstructionBudgetContract(),
                    new WProtoConstructionBudgetContract(),
                };
            }
            AssertBudget(value, count);
        }

        [TestCase(3, false)]
        [TestCase(4, true)]
        public void LiteralEmptyChildrenAreRefusedBeforeTheirConstructors(int count, bool accepted)
        {
            byte[] bytes = { 18, 0, 26, 0, 26, 0 };
            WProtoReader reader = new WProtoReader(
                bytes,
                new WProtoReadLimits().WithContractConstructionCount(count)
            );
            Assert.AreEqual(
                accepted,
                WProtoConstructionBudgetContract.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out WProtoConstructionBudgetContract value
                )
            );
            Assert.AreEqual(!accepted, reader.Malformed);
            Assert.AreEqual(accepted, value != null);
            Assert.AreEqual(count, WProtoConstructionBudgetEvents.Initializations);
            Assert.AreEqual(count, WProtoConstructionBudgetEvents.Constructors);
            if (accepted)
            {
                Assert.AreEqual(7, value.Value);
                Assert.AreEqual(7, value.Child.Value);
                Assert.AreEqual(2, value.Children.Count);
                foreach (WProtoConstructionBudgetContract child in value.Children)
                {
                    Assert.AreEqual(7, child.Value);
                }
            }
        }

        [Test]
        public void LargestFiniteAllowanceCannotOverflow()
        {
            WProtoReader reader = new WProtoReader(
                ReadOnlySpan<byte>.Empty,
                new WProtoReadLimits().WithContractConstructionCount(int.MaxValue - 1)
            );
            Assert.IsTrue(reader.TryChargeContractConstructions(int.MaxValue - 2));
            Assert.IsTrue(reader.TryChargeContractConstructions(1));
            Assert.IsFalse(reader.TryChargeContractConstructions(1));
            Assert.IsTrue(reader.Malformed);
        }

        [Test]
        public void GenericAndRootCollectionReadersShareContractAllowance()
        {
            List<WProtoConstructionBudgetContract> children =
                new List<WProtoConstructionBudgetContract>
                {
                    new WProtoConstructionBudgetContract(),
                    new WProtoConstructionBudgetContract(),
                };
            AssertBudget(children, 2);
            AssertBudget(
                new WProtoCollectionBudgetGenericContract<WProtoConstructionBudgetContract>
                {
                    Values = children,
                },
                3
            );
        }

        [TestCase(0, false, 0)]
        [TestCase(1, false, 1)]
        [TestCase(2, true, 2)]
        public void ImmutableSeedAndFinalInstancesEachSpendAllowance(
            int count,
            bool accepted,
            int initializations
        )
        {
            WProtoReader reader = new WProtoReader(
                new byte[] { 8, 3 },
                new WProtoReadLimits().WithContractConstructionCount(count)
            );
            Assert.AreEqual(
                accepted,
                WProtoConstructionBudgetImmutable.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out WProtoConstructionBudgetImmutable value
                )
            );
            Assert.AreEqual(!accepted, reader.Malformed);
            Assert.AreEqual(accepted, value != null);
            Assert.AreEqual(initializations, WProtoConstructionBudgetEvents.Initializations);
            Assert.AreEqual(count == 0 ? 0 : 1, WProtoConstructionBudgetEvents.Constructors);
            Assert.AreEqual(accepted ? 1 : 0, WProtoConstructionBudgetEvents.BeforeReads);
            Assert.AreEqual(accepted ? 1 : 0, WProtoConstructionBudgetEvents.AfterReads);
            if (accepted)
            {
                Assert.AreEqual(3, value.Value);
            }
        }

        [TestCase(0, false)]
        [TestCase(1, true)]
        public void SkippedConstructorStillChargesGeneratedConstruction(int count, bool accepted)
        {
            WProtoReader reader = new WProtoReader(
                new byte[] { 8, 3 },
                new WProtoReadLimits().WithContractConstructionCount(count)
            );
            Assert.AreEqual(
                accepted,
                WProtoConstructionBudgetSkipped.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out WProtoConstructionBudgetSkipped value
                )
            );
            Assert.AreEqual(!accepted, reader.Malformed);
            Assert.AreEqual(accepted, value != null);
            Assert.AreEqual(accepted ? 1 : 0, WProtoConstructionBudgetEvents.Initializations);
            Assert.AreEqual(0, WProtoConstructionBudgetEvents.Constructors);
            Assert.AreEqual(accepted ? 1 : 0, WProtoConstructionBudgetEvents.AfterReads);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PolymorphicConstructionDoesNotChargeDelegatingWrappers(bool declaredLeaf)
        {
            WProtoConstructionBudgetDerived value = new WProtoConstructionBudgetDerived
            {
                Value = 3,
                DerivedValue = 5,
            };
            if (declaredLeaf)
            {
                AssertBudget(value, 2);
            }
            else
            {
                AssertBudget<WProtoConstructionBudgetBase>(value, 2);
            }
        }

        [Test]
        public void StructAndCollectionStorageDoNotSpendContractConstructions()
        {
            WProtoReadLimits limits = new WProtoReadLimits().WithContractConstructionCount(0);
            WProtoConstructionBudgetValue expected = new WProtoConstructionBudgetValue
            {
                Value = 3,
            };
            Assert.AreEqual(
                expected,
                WProtoFacade.Deserialize<WProtoConstructionBudgetValue>(
                    WProtoFacade.Serialize(expected),
                    limits
                )
            );
            List<WProtoConstructionBudgetValue> values = new List<WProtoConstructionBudgetValue>
            {
                expected,
            };
            List<WProtoConstructionBudgetValue> actual = WProtoFacade.Deserialize<
                List<WProtoConstructionBudgetValue>
            >(WProtoFacade.Serialize(values), limits);
            CollectionAssert.AreEqual(values, actual);
            AssertNoSideEffects();
        }

        [Test]
        public void ConstructorOwnedSeedIsReusedWithoutAnotherGeneratedConstruction()
        {
            byte[] bytes = { 10, 2, 8, 3 };
            WProtoConstructionBudgetSeeded value =
                WProtoFacade.Deserialize<WProtoConstructionBudgetSeeded>(
                    bytes,
                    new WProtoReadLimits().WithContractConstructionCount(1)
                );
            Assert.AreEqual(3, value.Child.Value);
            Assert.AreEqual(1, WProtoConstructionBudgetEvents.Initializations);
            Assert.AreEqual(1, WProtoConstructionBudgetEvents.Constructors);
            Assert.AreEqual(1, WProtoConstructionBudgetEvents.BeforeReads);
            Assert.AreEqual(1, WProtoConstructionBudgetEvents.AfterReads);
        }

        [Test]
        public void MalformedSeededReadDoesNotInvokeHooksOrSetters()
        {
            WProtoConstructionBudgetContract seed = new WProtoConstructionBudgetContract();
            WProtoConstructionBudgetEvents.Reset();
            WProtoReader reader = new WProtoReader(
                new byte[] { 8, 3 },
                new WProtoReadLimits(maximumMessageBytes: 0)
            );
            Assert.IsFalse(
                WProtoConstructionBudgetContract.WProtoFormatter.Instance.TryReadInto(
                    ref reader,
                    seed,
                    out WProtoConstructionBudgetContract value
                )
            );
            Assert.IsTrue(value == null);
            Assert.AreEqual(7, seed.Value);
            AssertNoSideEffects();
        }

        [Test]
        public void ThrowingConstructorDoesNotRefundItsAttempt()
        {
            WProtoReader reader = new WProtoReader(
                Array.Empty<byte>(),
                new WProtoReadLimits().WithContractConstructionCount(1)
            );
            WProtoReader copy = reader;
            WProtoConstructionBudgetEvents.ThrowInConstructor = true;
            try
            {
                WProtoConstructionBudgetContract.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out WProtoConstructionBudgetContract value
                );
                Assert.Fail("The constructor must exercise the requested failure.");
            }
            catch (InvalidOperationException)
            {
                Assert.AreEqual(1, WProtoConstructionBudgetEvents.Initializations);
                Assert.AreEqual(1, WProtoConstructionBudgetEvents.Constructors);
                Assert.AreEqual(0, WProtoConstructionBudgetEvents.BeforeReads);
            }
            Assert.IsFalse(copy.TryChargeContractConstructions(1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void EveryFormatterReaderRouteSharesPreviouslySpentConstructionAllowance(int route)
        {
            WProtoReader parent = new WProtoReader(
                new byte[] { 0 },
                new WProtoReadLimits().WithContractConstructionCount(1)
            );
            Assert.IsTrue(parent.TryChargeContractConstructions(1));
            bool accepted;
            WProtoConstructionBudgetContract value;
            switch (route)
            {
                case 0:
                    accepted = parent.TryReadMessage(
                        WProtoConstructionBudgetContract.WProtoFormatter.Instance,
                        out value
                    );
                    break;
                case 1:
                    accepted = parent.TryReadMessage(
                        ReadOnlySpan<byte>.Empty,
                        WProtoConstructionBudgetContract.WProtoFormatter.Instance,
                        out value
                    );
                    break;
                default:
                    accepted = parent.TryReadMessage(
                        ReadOnlySpan<byte>.Empty,
                        WProtoConstructionBudgetContract.WProtoFormatter.Instance,
                        default(WProtoConstructionBudgetContract),
                        out value
                    );
                    break;
            }
            Assert.IsFalse(accepted);
            Assert.IsTrue(value == null);
            Assert.IsTrue(parent.Malformed);
            AssertNoSideEffects();
        }

        [Test]
        public void DuplicateMessageOccurrencesMergeIntoOneConstructedChild()
        {
            byte[] bytes = { 18, 2, 8, 3, 18, 2, 8, 5 };
            WProtoConstructionBudgetContract value =
                WProtoFacade.Deserialize<WProtoConstructionBudgetContract>(
                    bytes,
                    new WProtoReadLimits().WithContractConstructionCount(2)
                );
            Assert.AreEqual(5, value.Child.Value);
            Assert.AreEqual(2, WProtoConstructionBudgetEvents.Initializations);
            Assert.AreEqual(2, WProtoConstructionBudgetEvents.Constructors);
            Assert.Throws<SerializationCorruptDataException>(() =>
                WProtoFacade.Deserialize<WProtoConstructionBudgetContract>(
                    bytes,
                    new WProtoReadLimits().WithContractConstructionCount(1)
                )
            );
        }

        [Test]
        public void MapValueContractConstructionsShareTheRootAllowance()
        {
            AssertBudget(
                new WProtoCollectionBudgetContract
                {
                    Values = new Dictionary<string, WProtoCollectionBudgetContract>
                    {
                        { "A", new WProtoCollectionBudgetContract() },
                        { "B", new WProtoCollectionBudgetContract() },
                    },
                },
                3
            );
        }

        [TestCase(0, false)]
        [TestCase(1, true)]
        public void UnseededImmutableReadChargesOnlyItsFinalInstance(int count, bool accepted)
        {
            WProtoReader reader = new WProtoReader(
                new byte[] { 8, 3 },
                new WProtoReadLimits().WithContractConstructionCount(count)
            );
            Assert.AreEqual(
                accepted,
                WProtoConstructionBudgetUnseededImmutable.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out WProtoConstructionBudgetUnseededImmutable value
                )
            );
            Assert.AreEqual(!accepted, reader.Malformed);
            Assert.AreEqual(accepted, value != null);
            Assert.AreEqual(accepted ? 1 : 0, WProtoConstructionBudgetEvents.Initializations);
            Assert.AreEqual(0, WProtoConstructionBudgetEvents.Constructors);
            if (accepted)
            {
                Assert.AreEqual(3, value.Value);
            }
        }

        [Test]
        public void AbstractEnvelopeCostsOnlyTheConcreteConstruction()
        {
            WProtoConstructionBudgetAbstract value = new WProtoConstructionBudgetConcrete
            {
                Value = 3,
                DerivedValue = 5,
            };
            AssertBudget(value, 1);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisabledAllowanceCreatesNoAccountingState(bool explicitMaximum)
        {
            WProtoReadLimits limits = new WProtoReadLimits();
            if (explicitMaximum)
            {
                limits = limits.WithContractConstructionCount(int.MaxValue);
            }
            WProtoReadLimits finite = limits.WithContractConstructionCount(1);
            Assert.IsTrue(ExerciseAllowance(limits));
            Assert.IsTrue(ExerciseAllowance(finite));
#if ENABLE_IL2CPP && !UNITY_6000_0_OR_NEWER
            Assert.Inconclusive(
                "The per-thread allocation counter is unavailable on legacy IL2CPP."
            );
#else
            const int length = 4096;
            long before = GC.GetAllocatedBytesForCurrentThread();
            byte[] control = new byte[length];
            long sensitivity = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(control);
            if (sensitivity < length)
            {
                Assert.Inconclusive(
                    "The allocation counter missed the positive allocation control."
                );
            }
            before = GC.GetAllocatedBytesForCurrentThread();
            bool accepted = ExerciseAllowance(limits);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(accepted);
            Assert.AreEqual(0, allocated);
            before = GC.GetAllocatedBytesForCurrentThread();
            accepted = ExerciseAllowance(finite);
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(accepted);
            Assert.Less(0, allocated);
#endif
        }
    }
}
