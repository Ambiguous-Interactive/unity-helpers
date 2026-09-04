// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Serialization;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class BitSetTests
    {
        /// <summary>
        /// The capacity and the words are separate serialized members, so a payload can state one
        /// and deliver the other. Six bytes ask for 2^30 bits beside an empty array, and the
        /// capacity guard then admits an index the words cannot answer.
        /// </summary>
        /// <remarks>
        /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/647">#647</see>.
        /// ImmutableBitSet reconciles the two in its constructor and its doc names this exact
        /// failure; only the mutable binary paths were missed.
        /// </remarks>
        [Test]
        public void ACapacityLargerThanTheWordsDeliveredIsLoweredRatherThanTrusted()
        {
            byte[] payload = { 0x10, 0x80, 0x80, 0x80, 0x80, 0x04 };

            BitSet restored = Serializer.ProtoDeserialize<BitSet>(payload);

            Assert.IsTrue(
                restored != null,
                "the probe deserialized nothing, so it measured nothing"
            );
            Assert.AreEqual(0, restored.Capacity);
            Assert.DoesNotThrow(() =>
            {
                Assert.IsFalse(restored.TryGet(0, out bool _));
                Assert.IsFalse(restored.TryGet(1 << 20, out bool _));
                Assert.IsFalse(restored.TryClear(1 << 20));
            });
        }

        /// <summary>
        /// The clamp drives capacity to zero for a payload that delivers no words, and growth that
        /// doubles cannot leave zero. A write to a clamped instance must still make progress: a
        /// freeze is worse than the exception the clamp exists to prevent, because no catch reaches
        /// it.
        /// </summary>
        [Test]
        [Timeout(5000)]
        public void AClampedBitSetStillGrowsWhenWritten()
        {
            byte[] payload = { 0x10, 0x80, 0x80, 0x80, 0x80, 0x04 };

            BitSet restored = Serializer.ProtoDeserialize<BitSet>(payload);
            Assert.IsTrue(
                restored != null,
                "the probe deserialized nothing, so it measured nothing"
            );
            Assert.AreEqual(0, restored.Capacity, "the probe must start from a clamped capacity");

            Assert.IsTrue(restored.TrySet(0));
            Assert.IsTrue(restored.TryGet(0, out bool first) && first);

            Assert.IsTrue(restored.TrySet(500));
            Assert.IsTrue(restored.TryGet(500, out bool later) && later);
            Assert.IsTrue(restored.TryGet(0, out bool stillFirst) && stillFirst);
        }

        [Test]
        public void ConstructorWithPositiveCapacityInitializesCorrectly()
        {
            BitSet bits = new(64);
            Assert.AreEqual(64, bits.Capacity);
            Assert.AreEqual(0, bits.CountSetBits());
            Assert.IsTrue(bits.None());
        }

        [Test]
        public void ConstructorWithNegativeCapacityThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BitSet(-1));
        }

        [Test]
        public void ConstructorWithZeroCapacityThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BitSet(0));
        }

        [TestCase(1)]
        [TestCase(63)]
        [TestCase(64)]
        [TestCase(65)]
        [TestCase(127)]
        [TestCase(128)]
        [TestCase(129)]
        [TestCase(1000)]
        public void ConstructorWithVariousCapacitiesInitializesCorrectly(int capacity)
        {
            BitSet bits = new(capacity);
            Assert.AreEqual(capacity, bits.Capacity);
            Assert.AreEqual(0, bits.CountSetBits());
        }

        [Test]
        public void TrySetSetsBit()
        {
            BitSet bits = new(64);

            Assert.IsTrue(bits.TrySet(5));
            Assert.IsTrue(bits.TryGet(5, out bool value));
            Assert.IsTrue(value);
        }

        [Test]
        public void TrySetWithNegativeIndexReturnsFalse()
        {
            BitSet bits = new(64);
            Assert.IsFalse(bits.TrySet(-1));
            Assert.IsFalse(bits.TrySet(-100));
        }

        [Test]
        public void TrySetBeyondCapacityExpandsCapacity()
        {
            BitSet bits = new(10);
            Assert.IsTrue(bits.TrySet(50));
            Assert.IsTrue(51 <= bits.Capacity);
            Assert.IsTrue(bits.TryGet(50, out bool value));
            Assert.IsTrue(value);
        }

        [Test]
        public void TrySetAtBoundaryIndex63Works()
        {
            BitSet bits = new(64);
            Assert.IsTrue(bits.TrySet(63));
            Assert.IsTrue(bits.TryGet(63, out bool value));
            Assert.IsTrue(value);
        }

        [Test]
        public void TrySetAtBoundaryIndex64ExpandsAndWorks()
        {
            BitSet bits = new(64);
            Assert.IsTrue(bits.TrySet(64));
            Assert.IsTrue(64 < bits.Capacity);
            Assert.IsTrue(bits.TryGet(64, out bool value));
            Assert.IsTrue(value);
        }

        [Test]
        public void TrySetMultipleBitsInSameWordWorks()
        {
            BitSet bits = new(64);
            for (int i = 0; i < 64; i++)
            {
                Assert.IsTrue(bits.TrySet(i));
            }
            Assert.AreEqual(64, bits.CountSetBits());
        }

        [Test]
        public void TrySetMultipleBitsAcrossWordsWorks()
        {
            BitSet bits = new(200);
            Assert.IsTrue(bits.TrySet(0));
            Assert.IsTrue(bits.TrySet(63));
            Assert.IsTrue(bits.TrySet(64));
            Assert.IsTrue(bits.TrySet(127));
            Assert.IsTrue(bits.TrySet(128));
            Assert.IsTrue(bits.TrySet(199));
            Assert.AreEqual(6, bits.CountSetBits());
        }

        [Test]
        public void TrySetAlreadySetBitRemainsSet()
        {
            BitSet bits = new(64);
            Assert.IsTrue(bits.TrySet(5));
            Assert.IsTrue(bits.TrySet(5));
            Assert.IsTrue(bits.TryGet(5, out bool value));
            Assert.IsTrue(value);
            Assert.AreEqual(1, bits.CountSetBits());
        }

        [Test]
        public void TryClearClearsBit()
        {
            BitSet bits = new(64);
            Assert.IsTrue(bits.TrySet(5));

            Assert.IsTrue(bits.TryClear(5));
            Assert.IsTrue(bits.TryGet(5, out bool value));
            Assert.IsFalse(value);
        }

        [Test]
        public void TryClearWithNegativeIndexReturnsFalse()
        {
            BitSet bits = new(64);
            Assert.IsFalse(bits.TryClear(-1));
            Assert.IsFalse(bits.TryClear(-100));
        }

        [Test]
        public void TryClearBeyondCapacityReturnsFalse()
        {
            BitSet bits = new(10);
            Assert.IsFalse(bits.TryClear(50));
            Assert.AreEqual(10, bits.Capacity);
        }

        [Test]
        public void TryClearAlreadyClearedBitRemainsClear()
        {
            BitSet bits = new(64);
            Assert.IsTrue(bits.TryClear(5));
            Assert.IsTrue(bits.TryGet(5, out bool value));
            Assert.IsFalse(value);
        }

        [Test]
        public void TryClearAtBoundariesWorks()
        {
            BitSet bits = new(128);
            bits.SetAll();
            Assert.IsTrue(bits.TryClear(0));
            Assert.IsTrue(bits.TryClear(63));
            Assert.IsTrue(bits.TryClear(64));
            Assert.IsTrue(bits.TryClear(127));
            Assert.AreEqual(124, bits.CountSetBits());
        }

        [Test]
        public void TryFlipTogglesBit()
        {
            BitSet bits = new(64);

            Assert.IsTrue(bits.TryFlip(5));
            Assert.IsTrue(bits.TryGet(5, out bool value1));
            Assert.IsTrue(value1);

            Assert.IsTrue(bits.TryFlip(5));
            Assert.IsTrue(bits.TryGet(5, out bool value2));
            Assert.IsFalse(value2);
        }

        [Test]
        public void TryFlipWithNegativeIndexReturnsFalse()
        {
            BitSet bits = new(64);
            Assert.IsFalse(bits.TryFlip(-1));
            Assert.IsFalse(bits.TryFlip(-100));
        }

        [Test]
        public void TryFlipBeyondCapacityExpandsCapacity()
        {
            BitSet bits = new(10);
            Assert.IsTrue(bits.TryFlip(50));
            Assert.IsTrue(51 <= bits.Capacity);
            Assert.IsTrue(bits.TryGet(50, out bool value));
            Assert.IsTrue(value);
        }

        [Test]
        public void TryFlipMultipleTimesTogglesCorrectly()
        {
            BitSet bits = new(64);
            for (int i = 0; i < 10; i++)
            {
                Assert.IsTrue(bits.TryFlip(5));
                bool expected = i % 2 == 0;
                Assert.IsTrue(bits.TryGet(5, out bool value));
                Assert.AreEqual(expected, value);
            }
        }

        [Test]
        public void TryGetWithNegativeIndexReturnsFalseAndFalseValue()
        {
            BitSet bits = new(64);
            Assert.IsFalse(bits.TryGet(-1, out bool value));
            Assert.IsFalse(value);
        }

        [Test]
        public void TryGetBeyondCapacityReturnsFalseAndFalseValue()
        {
            BitSet bits = new(10);
            Assert.IsFalse(bits.TryGet(50, out bool value));
            Assert.IsFalse(value);
        }

        [Test]
        public void TryGetUnsetBitReturnsTrueAndFalseValue()
        {
            BitSet bits = new(64);
            Assert.IsTrue(bits.TryGet(5, out bool value));
            Assert.IsFalse(value);
        }

        [Test]
        public void TryGetSetBitReturnsTrueAndTrueValue()
        {
            BitSet bits = new(64);
            bits.TrySet(5);
            Assert.IsTrue(bits.TryGet(5, out bool value));
            Assert.IsTrue(value);
        }

        [Test]
        public void IndexerGetUnsetBitReturnsFalse()
        {
            BitSet bits = new(64);
            Assert.IsFalse(bits[5]);
        }

        [Test]
        public void IndexerGetSetBitReturnsTrue()
        {
            BitSet bits = new(64);
            Assert.IsTrue(bits.TrySet(5));
            Assert.IsTrue(bits[5]);
        }

        [Test]
        public void IndexerGetNegativeIndexReturnsFalse()
        {
            BitSet bits = new(64);
            Assert.IsFalse(bits[-1]);
        }

        [Test]
        public void IndexerGetBeyondCapacityReturnsFalse()
        {
            BitSet bits = new(10);
            Assert.IsFalse(bits[50]);
        }

        [Test]
        public void IndexerSetTrueSetsBit()
        {
            BitSet bits = new(64) { [5] = true };
            Assert.IsTrue(bits[5]);
        }

        [Test]
        public void IndexerSetFalseClearsBit()
        {
            BitSet bits = new(64) { [5] = true, [5] = false };
            Assert.IsFalse(bits[5]);
        }

        [Test]
        public void IndexerSetBeyondCapacityExpandsAndSets()
        {
            BitSet bits = new(10) { [50] = true };
            Assert.IsTrue(51 <= bits.Capacity);
            Assert.IsTrue(bits[50]);
        }

        [Test]
        public void CountSetBitsWorks()
        {
            BitSet bits = new(64);
            Assert.IsTrue(bits.TrySet(0));
            Assert.IsTrue(bits.TrySet(1));
            Assert.IsTrue(bits.TrySet(10));

            Assert.AreEqual(3, bits.CountSetBits());
        }

        [Test]
        public void CountSetBitsEmptyBitSetReturnsZero()
        {
            BitSet bits = new(64);
            Assert.AreEqual(0, bits.CountSetBits());
        }

        [Test]
        public void CountSetBitsFullBitSetReturnsCapacity()
        {
            BitSet bits = new(100);
            bits.SetAll();
            Assert.AreEqual(100, bits.CountSetBits());
        }

        [Test]
        public void CountSetBitsAcrossMultipleWordsWorks()
        {
            BitSet bits = new(200);
            for (int i = 0; i < 200; i += 2)
            {
                Assert.IsTrue(bits.TrySet(i));
            }
            Assert.AreEqual(100, bits.CountSetBits());
        }

        [TestCase(10)]
        [TestCase(64)]
        [TestCase(65)]
        public void SetAllSetsAllBits(int capacity)
        {
            BitSet bits = new(capacity);
            bits.SetAll();

            Assert.AreEqual(capacity, bits.CountSetBits());
            Assert.IsTrue(bits.All());
        }

        [TestCase(1)]
        [TestCase(63)]
        [TestCase(127)]
        [TestCase(129)]
        public void SetAllVariousCapacitiesSetsAllBitsCorrectly(int capacity)
        {
            BitSet bits = new(capacity);
            bits.SetAll();
            Assert.AreEqual(capacity, bits.CountSetBits());
            for (int i = 0; i < capacity; i++)
            {
                Assert.IsTrue(bits[i], $"Bit {i} should be set");
            }
        }

        [Test]
        public void SetAllAfterPartialSetSetsRemainingBits()
        {
            BitSet bits = new(100);
            for (int i = 0; i < 50; i++)
            {
                Assert.IsTrue(bits.TrySet(i));
            }
            bits.SetAll();
            Assert.AreEqual(100, bits.CountSetBits());
        }

        [Test]
        public void ClearAllClearsAllBits()
        {
            BitSet bits = new(64);
            bits.SetAll();
            bits.ClearAll();

            Assert.AreEqual(0, bits.CountSetBits());
            Assert.IsTrue(bits.None());
        }

        [Test]
        public void ClearAllEmptyBitSetRemainsEmpty()
        {
            BitSet bits = new(64);
            bits.ClearAll();
            Assert.AreEqual(0, bits.CountSetBits());
        }

        [Test]
        public void ClearAllAcrossMultipleWordsWorks()
        {
            BitSet bits = new(200);
            bits.SetAll();
            bits.ClearAll();
            Assert.AreEqual(0, bits.CountSetBits());
        }

        [Test]
        public void FlipAllEmptyBitSetSetsAllBits()
        {
            BitSet bits = new(64);
            bits.FlipAll();
            Assert.AreEqual(64, bits.CountSetBits());
            Assert.IsTrue(bits.All());
        }

        [Test]
        public void FlipAllFullBitSetClearsAllBits()
        {
            BitSet bits = new(64);
            bits.SetAll();
            bits.FlipAll();
            Assert.AreEqual(0, bits.CountSetBits());
            Assert.IsTrue(bits.None());
        }

        [Test]
        public void FlipAllPartiallySetFlipsCorrectly()
        {
            BitSet bits = new(100);
            for (int i = 0; i < 50; i++)
            {
                Assert.IsTrue(bits.TrySet(i));
            }
            bits.FlipAll();
            Assert.AreEqual(50, bits.CountSetBits());
            for (int i = 0; i < 50; i++)
            {
                Assert.IsFalse(bits[i]);
            }
            for (int i = 50; i < 100; i++)
            {
                Assert.IsTrue(bits[i]);
            }
        }

        [TestCase(65)]
        [TestCase(127)]
        [TestCase(128)]
        [TestCase(129)]
        public void FlipAllNonMultipleOf64FlipsCorrectly(int capacity)
        {
            BitSet bits = new(capacity);
            bits.FlipAll();
            Assert.AreEqual(capacity, bits.CountSetBits());
        }

        [Test]
        public void NotIsAliasForFlipAll()
        {
            BitSet bits = new(64);
            Assert.IsTrue(bits.TrySet(5));
            Assert.IsTrue(bits.TrySet(10));
            bits.Not();
            Assert.AreEqual(62, bits.CountSetBits());
            Assert.IsFalse(bits[5]);
            Assert.IsFalse(bits[10]);
        }

        [Test]
        public void AnyEmptyBitSetReturnsFalse()
        {
            BitSet bits = new(64);
            Assert.IsFalse(bits.Any());
        }

        [Test]
        public void AnySingleBitSetReturnsTrue()
        {
            BitSet bits = new(64);
            Assert.IsTrue(bits.TrySet(5));
            Assert.IsTrue(bits.Any());
        }

        [Test]
        public void NoneEmptyBitSetReturnsTrue()
        {
            BitSet bits = new(64);
            Assert.IsTrue(bits.None());
        }

        [Test]
        public void NoneSingleBitSetReturnsFalse()
        {
            BitSet bits = new(64);
            Assert.IsTrue(bits.TrySet(5));
            Assert.IsFalse(bits.None());
        }

        [Test]
        public void AllEmptyBitSetReturnsFalse()
        {
            BitSet bits = new(64);
            Assert.IsFalse(bits.All());
        }

        [Test]
        public void AllFullBitSetReturnsTrue()
        {
            BitSet bits = new(64);
            bits.SetAll();
            Assert.IsTrue(bits.All());
        }

        [Test]
        public void AllPartiallySetReturnsFalse()
        {
            BitSet bits = new(64);
            for (int i = 0; i < 63; i++)
            {
                Assert.IsTrue(bits.TrySet(i));
            }
            Assert.IsFalse(bits.All());
        }

        [TestCase(65)]
        [TestCase(127)]
        [TestCase(128)]
        [TestCase(129)]
        public void AllNonMultipleOf64Works(int capacity)
        {
            BitSet bits = new(capacity);
            bits.SetAll();
            Assert.IsTrue(bits.All());
            Assert.IsTrue(bits.TryClear(capacity - 1));
            Assert.IsFalse(bits.All());
        }

        [Test]
        public void LeftShiftWorks()
        {
            BitSet bits = new(8);
            Assert.IsTrue(bits.TrySet(0));
            Assert.IsTrue(bits.TrySet(1));

            bits.LeftShift(2);

            Assert.IsTrue(bits.TryGet(2, out bool v1) && v1);
            Assert.IsTrue(bits.TryGet(3, out bool v2) && v2);
            Assert.IsTrue(bits.TryGet(0, out bool v3));
            Assert.IsFalse(v3);
        }

        [Test]
        public void LeftShiftByZeroNoChange()
        {
            BitSet bits = new(8);
            Assert.IsTrue(bits.TrySet(0));
            Assert.IsTrue(bits.TrySet(5));
            bits.LeftShift(0);
            Assert.IsTrue(bits[0]);
            Assert.IsTrue(bits[5]);
        }

        [Test]
        public void LeftShiftByNegativeNoChange()
        {
            BitSet bits = new(8);
            Assert.IsTrue(bits.TrySet(0));
            Assert.IsTrue(bits.TrySet(5));
            bits.LeftShift(-5);
            Assert.IsTrue(bits[0]);
            Assert.IsTrue(bits[5]);
        }

        [Test]
        public void LeftShiftBeyondCapacityClearsAll()
        {
            BitSet bits = new(8);
            bits.SetAll();
            bits.LeftShift(10);
            Assert.AreEqual(0, bits.CountSetBits());
        }

        [Test]
        public void LeftShiftExactlyCapacityClearsAll()
        {
            BitSet bits = new(8);
            bits.SetAll();
            bits.LeftShift(8);
            Assert.AreEqual(0, bits.CountSetBits());
        }

        [Test]
        public void LeftShiftAcrossWordBoundaryWorks()
        {
            BitSet bits = new(128);
            Assert.IsTrue(bits.TrySet(60));
            Assert.IsTrue(bits.TrySet(61));
            bits.LeftShift(5);
            Assert.IsTrue(bits[65]);
            Assert.IsTrue(bits[66]);
            Assert.IsFalse(bits[60]);
            Assert.IsFalse(bits[61]);
        }

        [Test]
        public void LeftShiftShiftsOutHighBits()
        {
            BitSet bits = new(8);
            Assert.IsTrue(bits.TrySet(6));
            Assert.IsTrue(bits.TrySet(7));
            bits.LeftShift(3);
            Assert.IsFalse(bits[6]);
            Assert.IsFalse(bits[7]);
            Assert.AreEqual(0, bits.CountSetBits());
        }

        [Test]
        public void RightShiftWorks()
        {
            BitSet bits = new(8);
            Assert.IsTrue(bits.TrySet(5));
            Assert.IsTrue(bits.TrySet(6));

            bits.RightShift(2);

            Assert.IsTrue(bits.TryGet(3, out bool v1) && v1);
            Assert.IsTrue(bits.TryGet(4, out bool v2) && v2);
            Assert.IsTrue(bits.TryGet(5, out bool v3));
            Assert.IsFalse(v3);
        }

        [Test]
        public void RightShiftByZeroNoChange()
        {
            BitSet bits = new(8);
            Assert.IsTrue(bits.TrySet(3));
            Assert.IsTrue(bits.TrySet(5));
            bits.RightShift(0);
            Assert.IsTrue(bits[3]);
            Assert.IsTrue(bits[5]);
        }

        [Test]
        public void RightShiftByNegativeNoChange()
        {
            BitSet bits = new(8);
            Assert.IsTrue(bits.TrySet(3));
            Assert.IsTrue(bits.TrySet(5));
            bits.RightShift(-5);
            Assert.IsTrue(bits[3]);
            Assert.IsTrue(bits[5]);
        }

        [Test]
        public void RightShiftBeyondCapacityClearsAll()
        {
            BitSet bits = new(8);
            bits.SetAll();
            bits.RightShift(10);
            Assert.AreEqual(0, bits.CountSetBits());
        }

        [Test]
        public void RightShiftExactlyCapacityClearsAll()
        {
            BitSet bits = new(8);
            bits.SetAll();
            bits.RightShift(8);
            Assert.AreEqual(0, bits.CountSetBits());
        }

        [Test]
        public void RightShiftAcrossWordBoundaryWorks()
        {
            BitSet bits = new(128);
            Assert.IsTrue(bits.TrySet(66));
            Assert.IsTrue(bits.TrySet(67));
            bits.RightShift(5);
            Assert.IsTrue(bits[61]);
            Assert.IsTrue(bits[62]);
            Assert.IsFalse(bits[66]);
            Assert.IsFalse(bits[67]);
        }

        [Test]
        public void RightShiftShiftsOutLowBits()
        {
            BitSet bits = new(8);
            Assert.IsTrue(bits.TrySet(0));
            Assert.IsTrue(bits.TrySet(1));
            bits.RightShift(3);
            Assert.IsFalse(bits[0]);
            Assert.IsFalse(bits[1]);
            Assert.AreEqual(0, bits.CountSetBits());
        }

        [Test]
        public void TryAndWorks()
        {
            BitSet bits1 = new(8);
            BitSet bits2 = new(8);

            Assert.IsTrue(bits1.TrySet(0));
            Assert.IsTrue(bits1.TrySet(1));
            Assert.IsTrue(bits2.TrySet(1));
            Assert.IsTrue(bits2.TrySet(2));

            Assert.IsTrue(bits1.TryAnd(bits2));

            Assert.AreEqual(1, bits1.CountSetBits());
            Assert.IsTrue(bits1.TryGet(1, out bool value));
            Assert.IsTrue(value);
        }

        [Test]
        public void TryAndWithNullReturnsFalse()
        {
            BitSet bits = new(8);
            Assert.IsTrue(bits.TrySet(1));
            Assert.IsFalse(bits.TryAnd(null));
            Assert.AreEqual(1, bits.CountSetBits());
        }

        [Test]
        public void TryAndDifferentSizesResizesToMatch()
        {
            BitSet bits1 = new(8);
            BitSet bits2 = new(16);
            Assert.IsTrue(bits1.TrySet(5));
            Assert.IsTrue(bits2.TrySet(5));
            Assert.IsTrue(bits2.TrySet(10));

            Assert.IsTrue(bits1.TryAnd(bits2));

            Assert.AreEqual(16, bits1.Capacity);
            Assert.IsTrue(bits1[5]);
            Assert.IsFalse(bits1[10]);
        }

        [Test]
        public void TryAndNoOverlapResultsInEmpty()
        {
            BitSet bits1 = new(8);
            BitSet bits2 = new(8);
            Assert.IsTrue(bits1.TrySet(0));
            Assert.IsTrue(bits1.TrySet(1));
            Assert.IsTrue(bits2.TrySet(5));
            Assert.IsTrue(bits2.TrySet(6));

            Assert.IsTrue(bits1.TryAnd(bits2));

            Assert.AreEqual(0, bits1.CountSetBits());
        }

        [Test]
        public void TryAndWithEmptyResultsInEmpty()
        {
            BitSet bits1 = new(8);
            BitSet bits2 = new(8);
            bits1.SetAll();

            Assert.IsTrue(bits1.TryAnd(bits2));

            Assert.AreEqual(0, bits1.CountSetBits());
        }

        [Test]
        public void TryOrWorks()
        {
            BitSet bits1 = new(8);
            BitSet bits2 = new(8);

            Assert.IsTrue(bits1.TrySet(0));
            Assert.IsTrue(bits2.TrySet(1));

            Assert.IsTrue(bits1.TryOr(bits2));

            Assert.AreEqual(2, bits1.CountSetBits());
        }

        [Test]
        public void TryOrWithNullReturnsFalse()
        {
            BitSet bits = new(8);
            Assert.IsTrue(bits.TrySet(1));
            Assert.IsFalse(bits.TryOr(null));
            Assert.AreEqual(1, bits.CountSetBits());
        }

        [Test]
        public void TryOrDifferentSizesExpandsIfNeeded()
        {
            BitSet bits1 = new(8);
            BitSet bits2 = new(16);
            Assert.IsTrue(bits1.TrySet(5));
            Assert.IsTrue(bits2.TrySet(10));

            Assert.IsTrue(bits1.TryOr(bits2));

            Assert.AreEqual(16, bits1.Capacity);
            Assert.IsTrue(bits1[5]);
            Assert.IsTrue(bits1[10]);
        }

        [Test]
        public void TryOrWithEmptyRemainsUnchanged()
        {
            BitSet bits1 = new(8);
            BitSet bits2 = new(8);
            Assert.IsTrue(bits1.TrySet(1));
            Assert.IsTrue(bits1.TrySet(5));

            Assert.IsTrue(bits1.TryOr(bits2));

            Assert.AreEqual(2, bits1.CountSetBits());
        }

        [Test]
        public void TryOrWithOverlapUnionCorrectly()
        {
            BitSet bits1 = new(8);
            BitSet bits2 = new(8);
            bits1.TrySet(0);
            bits1.TrySet(1);
            bits1.TrySet(2);
            bits2.TrySet(1);
            bits2.TrySet(2);
            bits2.TrySet(3);

            bits1.TryOr(bits2);

            Assert.AreEqual(4, bits1.CountSetBits());
            Assert.IsTrue(bits1[0]);
            Assert.IsTrue(bits1[1]);
            Assert.IsTrue(bits1[2]);
            Assert.IsTrue(bits1[3]);
        }

        [Test]
        public void TryXorNoOverlapSetsUnion()
        {
            BitSet bits1 = new(8);
            BitSet bits2 = new(8);
            bits1.TrySet(0);
            bits1.TrySet(1);
            bits2.TrySet(5);
            bits2.TrySet(6);

            bits1.TryXor(bits2);

            Assert.AreEqual(4, bits1.CountSetBits());
            Assert.IsTrue(bits1[0]);
            Assert.IsTrue(bits1[1]);
            Assert.IsTrue(bits1[5]);
            Assert.IsTrue(bits1[6]);
        }

        [Test]
        public void TryXorWithOverlapTogglesOverlap()
        {
            BitSet bits1 = new(8);
            BitSet bits2 = new(8);
            bits1.TrySet(0);
            bits1.TrySet(1);
            bits1.TrySet(2);
            bits2.TrySet(1);
            bits2.TrySet(2);
            bits2.TrySet(3);

            bits1.TryXor(bits2);

            Assert.AreEqual(2, bits1.CountSetBits());
            Assert.IsTrue(bits1[0]);
            Assert.IsFalse(bits1[1]);
            Assert.IsFalse(bits1[2]);
            Assert.IsTrue(bits1[3]);
        }

        [Test]
        public void TryXorWithNullReturnsFalse()
        {
            BitSet bits = new(8);
            bits.TrySet(1);
            Assert.IsFalse(bits.TryXor(null));
            Assert.AreEqual(1, bits.CountSetBits());
        }

        [Test]
        public void TryXorDifferentSizesExpandsIfNeeded()
        {
            BitSet bits1 = new(8);
            BitSet bits2 = new(16);
            bits1.TrySet(5);
            bits2.TrySet(5);
            bits2.TrySet(10);

            bits1.TryXor(bits2);

            Assert.AreEqual(16, bits1.Capacity);
            Assert.IsFalse(bits1[5]);
            Assert.IsTrue(bits1[10]);
        }

        [Test]
        public void TryXorWithIdenticalResultsInEmpty()
        {
            BitSet bits1 = new(8);
            BitSet bits2 = new(8);
            bits1.TrySet(0);
            bits1.TrySet(5);
            bits2.TrySet(0);
            bits2.TrySet(5);

            bits1.TryXor(bits2);

            Assert.AreEqual(0, bits1.CountSetBits());
        }

        [Test]
        public void ResizeWorks()
        {
            BitSet bits = new(10);
            bits.TrySet(5);

            bits.Resize(20);

            Assert.AreEqual(20, bits.Capacity);
            Assert.IsTrue(bits.TryGet(5, out bool value));
            Assert.IsTrue(value);
        }

        [Test]
        public void ResizeToSameSizeNoChange()
        {
            BitSet bits = new(10);
            bits.TrySet(5);
            bits.Resize(10);
            Assert.AreEqual(10, bits.Capacity);
            Assert.IsTrue(bits[5]);
        }

        [Test]
        public void ResizeToZeroThrowsArgumentOutOfRangeException()
        {
            BitSet bits = new(10);
            Assert.Throws<ArgumentOutOfRangeException>(() => bits.Resize(0));
        }

        [Test]
        public void ResizeToNegativeThrowsArgumentOutOfRangeException()
        {
            BitSet bits = new(10);
            Assert.Throws<ArgumentOutOfRangeException>(() => bits.Resize(-5));
        }

        [Test]
        public void ResizeShrinkingLosesBits()
        {
            BitSet bits = new(20);
            bits.TrySet(5);
            bits.TrySet(15);

            bits.Resize(10);

            Assert.AreEqual(10, bits.Capacity);
            Assert.IsTrue(bits[5]);
            Assert.IsFalse(bits.TryGet(15, out _));
        }

        [Test]
        public void ResizeShrinkingClearsExtraBitsInLastWord()
        {
            BitSet bits = new(100);
            bits.SetAll();
            bits.Resize(65);
            Assert.AreEqual(65, bits.CountSetBits());
        }

        [Test]
        public void ResizeExpandingPreservesExistingBits()
        {
            BitSet bits = new(10);
            for (int i = 0; i < 10; i++)
            {
                bits.TrySet(i);
            }
            bits.Resize(100);
            for (int i = 0; i < 10; i++)
            {
                Assert.IsTrue(bits[i]);
            }
            for (int i = 10; i < 100; i++)
            {
                Assert.IsFalse(bits[i]);
            }
        }

        [Test]
        public void EnsureCapacityBelowCurrentNoChange()
        {
            BitSet bits = new(64);
            bits.EnsureCapacity(32);
            Assert.AreEqual(64, bits.Capacity);
        }

        [Test]
        public void EnsureCapacityEqualToCurrentNoChange()
        {
            BitSet bits = new(64);
            bits.EnsureCapacity(64);
            Assert.AreEqual(64, bits.Capacity);
        }

        [Test]
        public void EnsureCapacityAboveCurrentExpands()
        {
            BitSet bits = new(64);
            bits.EnsureCapacity(100);
            Assert.IsTrue(100 <= bits.Capacity);
        }

        [Test]
        public void EnsureCapacityPreservesExistingBits()
        {
            BitSet bits = new(10);
            bits.TrySet(5);
            bits.EnsureCapacity(100);
            Assert.IsTrue(bits[5]);
        }

        [Test]
        public void EnsureCapacityGrowthStrategyDoublesSmallCapacities()
        {
            BitSet bits = new(4);
            bits.EnsureCapacity(5);
            Assert.AreEqual(8, bits.Capacity);
        }

        [Test]
        public void TrimExcessWithNoBitsShrinksToMinimum()
        {
            BitSet bits = new(1000);
            bits.TrimExcess();
            Assert.AreEqual(64, bits.Capacity);
        }

        [Test]
        public void TrimExcessWithBitsShrinksToHighestSetBit()
        {
            BitSet bits = new(1000);
            bits.TrySet(50);
            bits.TrimExcess();
            Assert.IsTrue(51 <= bits.Capacity);
            Assert.IsTrue(bits.Capacity < 1000);
            Assert.IsTrue(bits[50]);
        }

        [Test]
        public void TrimExcessWithCustomMinimumRespectsMinimum()
        {
            BitSet bits = new(1000);
            bits.TrySet(5);
            bits.TrimExcess(100);
            Assert.AreEqual(100, bits.Capacity);
        }

        [Test]
        public void TrimExcessHighestBitBelowMinimumUsesMinimum()
        {
            BitSet bits = new(1000);
            bits.TrySet(10);
            bits.TrimExcess(50);
            Assert.AreEqual(50, bits.Capacity);
        }

        [Test]
        public void TrimExcessHighestBitAboveMinimumUsesHighestBit()
        {
            BitSet bits = new(1000);
            bits.TrySet(100);
            bits.TrimExcess(50);
            Assert.AreEqual(101, bits.Capacity);
        }

        [Test]
        public void TrimExcessNoExcessCapacityNoChange()
        {
            BitSet bits = new(10);
            bits.TrySet(9);
            bits.TrimExcess(10);
            Assert.AreEqual(10, bits.Capacity);
        }

        [Test]
        public void GetSetBitsReturnsCorrectIndices()
        {
            BitSet bits = new(100);
            bits.TrySet(5);
            bits.TrySet(10);
            bits.TrySet(99);

            List<int> result = new();
            bits.GetSetBits(result);

            Assert.AreEqual(3, result.Count);
            Assert.Contains(5, result);
            Assert.Contains(10, result);
            Assert.Contains(99, result);
        }

        [Test]
        public void GetSetBitsClearsList()
        {
            BitSet bits = new(10);
            bits.TrySet(5);

            List<int> result = new() { 999, 888 };
            bits.GetSetBits(result);

            Assert.AreEqual(1, result.Count);
            Assert.Contains(5, result);
        }

        [Test]
        public void GetSetBitsWithNullListThrowsArgumentNullException()
        {
            BitSet bits = new(10);
            Assert.Throws<ArgumentNullException>(() => bits.GetSetBits(null));
        }

        [Test]
        public void GetSetBitsEmptyBitSetReturnsEmptyList()
        {
            BitSet bits = new(64);
            List<int> result = new();
            bits.GetSetBits(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void GetSetBitsReturnsInAscendingOrder()
        {
            BitSet bits = new(100);
            bits.TrySet(99);
            bits.TrySet(5);
            bits.TrySet(50);

            List<int> result = new();
            bits.GetSetBits(result);

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(5, result[0]);
            Assert.AreEqual(50, result[1]);
            Assert.AreEqual(99, result[2]);
        }

        [Test]
        public void EnumerateSetIndicesReturnsCorrectIndices()
        {
            BitSet bits = new(100);
            bits.TrySet(5);
            bits.TrySet(10);
            bits.TrySet(99);

            List<int> result = new();
            foreach (int index in bits.EnumerateSetIndices())
            {
                result.Add(index);
            }

            Assert.AreEqual(3, result.Count);
            Assert.Contains(5, result);
            Assert.Contains(10, result);
            Assert.Contains(99, result);
        }

        [Test]
        public void EnumerateSetIndicesEmptyBitSetReturnsNothing()
        {
            BitSet bits = new(64);
            List<int> result = new();
            foreach (int index in bits.EnumerateSetIndices())
            {
                result.Add(index);
            }
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void EnumerateSetIndicesReturnsInAscendingOrder()
        {
            BitSet bits = new(100);
            bits.TrySet(99);
            bits.TrySet(5);
            bits.TrySet(50);

            List<int> result = new(bits.EnumerateSetIndices());

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(5, result[0]);
            Assert.AreEqual(50, result[1]);
            Assert.AreEqual(99, result[2]);
        }

        [Test]
        public void EnumerationYieldsAllBits()
        {
            BitSet bits = new(5);
            bits.TrySet(1);
            bits.TrySet(3);

            List<bool> values = new();
            foreach (bool bit in bits)
            {
                values.Add(bit);
            }

            Assert.AreEqual(5, values.Count);
            Assert.IsFalse(values[0]);
            Assert.IsTrue(values[1]);
            Assert.IsFalse(values[2]);
            Assert.IsTrue(values[3]);
            Assert.IsFalse(values[4]);
        }

        [Test]
        public void EnumerationEmptyBitSetYieldsFalses()
        {
            BitSet bits = new(10);
            List<bool> values = new(bits);
            Assert.AreEqual(10, values.Count);
            Assert.IsTrue(values.TrueForAll(b => !b));
        }

        [Test]
        public void EnumerationFullBitSetYieldsTrues()
        {
            BitSet bits = new(10);
            bits.SetAll();
            List<bool> values = new(bits);
            Assert.AreEqual(10, values.Count);
            Assert.IsTrue(values.TrueForAll(b => b));
        }

        [Test]
        public void EnumerationMultipleIterationsWorks()
        {
            BitSet bits = new(5);
            bits.TrySet(2);

            int iterations = 0;
            foreach (bool unused in bits)
            {
                iterations++;
            }
            Assert.AreEqual(5, iterations);

            iterations = 0;
            foreach (bool bit in bits)
            {
                iterations++;
            }
            Assert.AreEqual(5, iterations);
        }

        [Test]
        public void EnumerationAcrossWordBoundariesWorks()
        {
            BitSet bits = new(128);
            bits.TrySet(63);
            bits.TrySet(64);

            List<bool> values = new(bits);
            Assert.AreEqual(128, values.Count);
            Assert.IsTrue(values[63]);
            Assert.IsTrue(values[64]);
        }

        [Test]
        public void CountReturnsCapacity()
        {
            BitSet bits = new(100);
            Assert.AreEqual(100, bits.Count);
        }

        [Test]
        public void CountAfterResizeReturnsNewCapacity()
        {
            BitSet bits = new(10);
            bits.Resize(50);
            Assert.AreEqual(50, bits.Count);
        }

        [Test]
        public void ComplexScenarioMultipleOperations()
        {
            BitSet bits = new(64);
            Assert.IsTrue(bits.TrySet(10));
            Assert.IsTrue(bits.TrySet(20));
            Assert.IsTrue(bits.TrySet(30));
            Assert.AreEqual(3, bits.CountSetBits());

            bits.FlipAll();
            Assert.AreEqual(61, bits.CountSetBits());

            bits.LeftShift(5);
            // After LeftShift(5), position i gets value from position (i-5)
            // Position 10 gets value from position 5 (which was 1 after FlipAll)
            // Position 15 gets value from position 10 (which was 0 after FlipAll)
            Assert.IsTrue(bits[10]);
            Assert.IsFalse(bits[15]);

            bits.TrimExcess();
            Assert.IsTrue(64 <= bits.Capacity);
        }

        [Test]
        public void ComplexScenarioBitwiseOperationsChain()
        {
            BitSet bits1 = new(16);
            BitSet bits2 = new(16);
            BitSet bits3 = new(16);

            bits1.TrySet(0);
            bits1.TrySet(5);
            bits1.TrySet(10);

            bits2.TrySet(5);
            bits2.TrySet(10);
            bits2.TrySet(15);

            bits3.TrySet(10);
            bits3.TrySet(15);

            bits1.TryAnd(bits2);
            Assert.AreEqual(2, bits1.CountSetBits());
            Assert.IsTrue(bits1[5]);
            Assert.IsTrue(bits1[10]);

            bits1.TryOr(bits3);
            Assert.AreEqual(3, bits1.CountSetBits());
            Assert.IsTrue(bits1[5]);
            Assert.IsTrue(bits1[10]);
            Assert.IsTrue(bits1[15]);
        }

        [Test]
        public void ComplexScenarioAutoExpansion()
        {
            BitSet bits = new(10);
            Assert.AreEqual(10, bits.Capacity);

            bits[50] = true;
            Assert.IsTrue(51 <= bits.Capacity);
            Assert.IsTrue(bits[50]);

            bits.TryFlip(100);
            Assert.IsTrue(101 <= bits.Capacity);
            Assert.IsTrue(bits[100]);
        }

        [Test]
        public void StressTestManyBits()
        {
            BitSet bits = new(10000);
            for (int i = 0; i < 10000; i += 2)
            {
                bits.TrySet(i);
            }
            Assert.AreEqual(5000, bits.CountSetBits());

            bits.FlipAll();
            Assert.AreEqual(5000, bits.CountSetBits());

            for (int i = 1; i < 10000; i += 2)
            {
                Assert.IsTrue(bits[i]);
            }
        }

        [Test]
        public void EdgeCaseAllBoundaries()
        {
            int[] boundaries = { 1, 63, 64, 65, 127, 128, 129 };
            foreach (int size in boundaries)
            {
                BitSet bits = new(size);
                bits.SetAll();
                Assert.AreEqual(size, bits.CountSetBits(), $"Failed at size {size}");
                Assert.IsTrue(bits.All(), $"All() failed at size {size}");

                bits.ClearAll();
                Assert.IsTrue(bits.None(), $"None() failed at size {size}");
            }
        }
    }
}
