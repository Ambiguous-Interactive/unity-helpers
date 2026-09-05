// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ImmutableBitSetTests
    {
        [Test]
        public void ToImmutableCreatesImmutableCopy()
        {
            BitSet bits = new(64);
            bits.TrySet(5);
            bits.TrySet(10);
            bits.TrySet(63);

            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.AreEqual(64, immutable.Capacity);
            Assert.AreEqual(3, immutable.CountSetBits());
            Assert.IsTrue(immutable[5]);
            Assert.IsTrue(immutable[10]);
            Assert.IsTrue(immutable[63]);
        }

        [Test]
        public void ToImmutableCreatesIndependentCopy()
        {
            BitSet bits = new(64);
            bits.TrySet(5);

            ImmutableBitSet immutable = bits.ToImmutable();

            bits.TrySet(10);
            bits.TryClear(5);

            Assert.IsTrue(immutable[5]);
            Assert.IsFalse(immutable[10]);
            Assert.AreEqual(1, immutable.CountSetBits());
        }

        [Test]
        public void ToImmutableWithEmptyBitSet()
        {
            BitSet bits = new(64);
            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.AreEqual(64, immutable.Capacity);
            Assert.AreEqual(0, immutable.CountSetBits());
            Assert.IsTrue(immutable.None());
        }

        [Test]
        public void ToImmutableWithFullBitSet()
        {
            BitSet bits = new(64);
            bits.SetAll();

            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.AreEqual(64, immutable.Capacity);
            Assert.AreEqual(64, immutable.CountSetBits());
            Assert.IsTrue(immutable.All());
        }

        [Test]
        public void ToBitSetCreatesNewMutableCopy()
        {
            BitSet original = new(64);
            original.TrySet(5);
            original.TrySet(10);

            ImmutableBitSet immutable = original.ToImmutable();
            BitSet mutable = immutable.ToBitSet();

            Assert.AreEqual(64, mutable.Capacity);
            Assert.AreEqual(2, mutable.CountSetBits());
            Assert.IsTrue(mutable[5]);
            Assert.IsTrue(mutable[10]);
        }

        [Test]
        public void ToBitSetCreatesIndependentCopy()
        {
            BitSet original = new(64);
            original.TrySet(5);

            ImmutableBitSet immutable = original.ToImmutable();
            BitSet mutable = immutable.ToBitSet();

            mutable.TrySet(10);
            mutable.TryClear(5);

            Assert.IsTrue(immutable[5]);
            Assert.IsFalse(immutable[10]);
            Assert.AreEqual(1, immutable.CountSetBits());
        }

        [Test]
        public void RoundTripConversionPreservesData()
        {
            BitSet original = new(128);
            original.TrySet(0);
            original.TrySet(63);
            original.TrySet(64);
            original.TrySet(127);

            ImmutableBitSet immutable = original.ToImmutable();
            BitSet roundTrip = immutable.ToBitSet();

            Assert.AreEqual(original.Capacity, roundTrip.Capacity);
            Assert.AreEqual(original.CountSetBits(), roundTrip.CountSetBits());
            Assert.IsTrue(roundTrip[0]);
            Assert.IsTrue(roundTrip[63]);
            Assert.IsTrue(roundTrip[64]);
            Assert.IsTrue(roundTrip[127]);
        }

        [Test]
        public void TryGetReturnsCorrectValues()
        {
            BitSet bits = new(64);
            bits.TrySet(5);
            bits.TrySet(10);

            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.IsTrue(immutable.TryGet(5, out bool value5));
            Assert.IsTrue(value5);

            Assert.IsTrue(immutable.TryGet(10, out bool value10));
            Assert.IsTrue(value10);

            Assert.IsTrue(immutable.TryGet(7, out bool value7));
            Assert.IsFalse(value7);
        }

        [Test]
        public void TryGetWithNegativeIndexReturnsFalse()
        {
            BitSet bits = new(64);
            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.IsFalse(immutable.TryGet(-1, out bool value));
            Assert.IsFalse(value);
        }

        [Test]
        public void TryGetBeyondCapacityReturnsFalse()
        {
            BitSet bits = new(64);
            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.IsFalse(immutable.TryGet(64, out bool value));
            Assert.IsFalse(value);
            Assert.IsFalse(immutable.TryGet(100, out bool value2));
            Assert.IsFalse(value2);
        }

        [Test]
        public void IndexerReturnsCorrectValues()
        {
            BitSet bits = new(64);
            bits.TrySet(5);
            bits.TrySet(10);

            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.IsTrue(immutable[5]);
            Assert.IsTrue(immutable[10]);
            Assert.IsFalse(immutable[7]);
        }

        [Test]
        public void IndexerWithNegativeIndexReturnsFalse()
        {
            BitSet bits = new(64);
            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.IsFalse(immutable[-1]);
            Assert.IsFalse(immutable[-100]);
        }

        [Test]
        public void IndexerBeyondCapacityReturnsFalse()
        {
            BitSet bits = new(64);
            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.IsFalse(immutable[64]);
            Assert.IsFalse(immutable[100]);
        }

        [Test]
        public void CountSetBitsReturnsCorrectCount()
        {
            BitSet bits = new(64);
            bits.TrySet(0);
            bits.TrySet(5);
            bits.TrySet(10);
            bits.TrySet(63);

            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.AreEqual(4, immutable.CountSetBits());
        }

        [Test]
        public void CountSetBitsEmptyReturnsZero()
        {
            BitSet bits = new(64);
            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.AreEqual(0, immutable.CountSetBits());
        }

        [Test]
        public void CountSetBitsFullReturnsCapacity()
        {
            BitSet bits = new(64);
            bits.SetAll();
            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.AreEqual(64, immutable.CountSetBits());
        }

        [Test]
        public void CountSetBitsAcrossMultipleWords()
        {
            BitSet bits = new(200);
            bits.TrySet(0);
            bits.TrySet(63);
            bits.TrySet(64);
            bits.TrySet(127);
            bits.TrySet(128);
            bits.TrySet(199);

            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.AreEqual(6, immutable.CountSetBits());
        }

        [Test]
        public void AnyEmptyReturnsFalse()
        {
            BitSet bits = new(64);
            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.IsFalse(immutable.Any());
        }

        [Test]
        public void AnySingleBitSetReturnsTrue()
        {
            BitSet bits = new(64);
            bits.TrySet(5);
            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.IsTrue(immutable.Any());
        }

        [Test]
        public void NoneEmptyReturnsTrue()
        {
            BitSet bits = new(64);
            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.IsTrue(immutable.None());
        }

        [Test]
        public void NoneSingleBitSetReturnsFalse()
        {
            BitSet bits = new(64);
            bits.TrySet(5);
            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.IsFalse(immutable.None());
        }

        [Test]
        public void AllEmptyReturnsFalse()
        {
            BitSet bits = new(64);
            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.IsFalse(immutable.All());
        }

        [Test]
        public void AllFullReturnsTrue()
        {
            BitSet bits = new(64);
            bits.SetAll();
            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.IsTrue(immutable.All());
        }

        [Test]
        public void AllPartiallySetReturnsFalse()
        {
            BitSet bits = new(64);
            bits.TrySet(5);
            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.IsFalse(immutable.All());
        }

        [TestCase(65)]
        [TestCase(70)]
        [TestCase(100)]
        [TestCase(127)]
        public void AllNonMultipleOf64Works(int capacity)
        {
            BitSet bits = new(capacity);
            bits.SetAll();
            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.IsTrue(immutable.All());
        }

        [Test]
        public void GetSetBitsReturnsCorrectIndices()
        {
            BitSet bits = new(64);
            bits.TrySet(0);
            bits.TrySet(5);
            bits.TrySet(10);
            bits.TrySet(63);

            ImmutableBitSet immutable = bits.ToImmutable();
            List<int> results = new();
            immutable.GetSetBits(results);

            CollectionAssert.AreEquivalent(new[] { 0, 5, 10, 63 }, results);
        }

        [Test]
        public void GetSetBitsClearsList()
        {
            BitSet bits = new(64);
            bits.TrySet(5);

            ImmutableBitSet immutable = bits.ToImmutable();
            List<int> results = new() { 999, 888, 777 };
            immutable.GetSetBits(results);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(5, results[0]);
        }

        [Test]
        public void GetSetBitsWithNullListThrowsArgumentNullException()
        {
            BitSet bits = new(64);
            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.Throws<ArgumentNullException>(() => immutable.GetSetBits(null));
        }

        [Test]
        public void GetSetBitsEmptyReturnsEmptyList()
        {
            BitSet bits = new(64);
            ImmutableBitSet immutable = bits.ToImmutable();
            List<int> results = new();
            immutable.GetSetBits(results);

            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetSetBitsReturnsInAscendingOrder()
        {
            BitSet bits = new(200);
            bits.TrySet(100);
            bits.TrySet(5);
            bits.TrySet(50);
            bits.TrySet(199);

            ImmutableBitSet immutable = bits.ToImmutable();
            List<int> results = new();
            immutable.GetSetBits(results);

            CollectionAssert.AreEqual(new[] { 5, 50, 100, 199 }, results);
        }

        [Test]
        public void EnumerateSetIndicesReturnsCorrectIndices()
        {
            BitSet bits = new(200);
            bits.TrySet(0);
            bits.TrySet(63);
            bits.TrySet(64);
            bits.TrySet(127);
            bits.TrySet(199);

            ImmutableBitSet immutable = bits.ToImmutable();
            List<int> indices = immutable.EnumerateSetIndices().ToList();

            CollectionAssert.AreEqual(new[] { 0, 63, 64, 127, 199 }, indices);
        }

        [Test]
        public void EnumerateSetIndicesEmptyReturnsNothing()
        {
            BitSet bits = new(64);
            ImmutableBitSet immutable = bits.ToImmutable();

            List<int> indices = immutable.EnumerateSetIndices().ToList();

            Assert.AreEqual(0, indices.Count);
        }

        [Test]
        public void EnumerateSetIndicesReturnsInAscendingOrder()
        {
            BitSet bits = new(200);
            bits.TrySet(100);
            bits.TrySet(5);
            bits.TrySet(50);

            ImmutableBitSet immutable = bits.ToImmutable();
            List<int> indices = immutable.EnumerateSetIndices().ToList();

            CollectionAssert.AreEqual(new[] { 5, 50, 100 }, indices);
        }

        [Test]
        public void CountReturnsCapacity()
        {
            BitSet bits = new(123);
            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.AreEqual(123, immutable.Count);
            Assert.AreEqual(123, immutable.Capacity);
        }

        [Test]
        public void EnumerationYieldsAllBits()
        {
            BitSet bits = new(8);
            bits.TrySet(0);
            bits.TrySet(3);
            bits.TrySet(7);

            ImmutableBitSet immutable = bits.ToImmutable();
            List<bool> values = new();
            foreach (bool bit in immutable)
            {
                values.Add(bit);
            }

            Assert.AreEqual(8, values.Count);
            Assert.IsTrue(values[0]);
            Assert.IsFalse(values[1]);
            Assert.IsFalse(values[2]);
            Assert.IsTrue(values[3]);
            Assert.IsFalse(values[4]);
            Assert.IsFalse(values[5]);
            Assert.IsFalse(values[6]);
            Assert.IsTrue(values[7]);
        }

        [Test]
        public void EnumerationEmptyYieldsFalses()
        {
            BitSet bits = new(5);
            ImmutableBitSet immutable = bits.ToImmutable();

            List<bool> values = immutable.ToList();

            Assert.AreEqual(5, values.Count);
            Assert.IsTrue(values.All(v => !v));
        }

        [Test]
        public void EnumerationFullYieldsTrues()
        {
            BitSet bits = new(5);
            bits.SetAll();
            ImmutableBitSet immutable = bits.ToImmutable();

            List<bool> values = immutable.ToList();

            Assert.AreEqual(5, values.Count);
            Assert.IsTrue(values.All(v => v));
        }

        [Test]
        public void EnumerationMultipleIterationsWorks()
        {
            BitSet bits = new(5);
            bits.TrySet(2);
            ImmutableBitSet immutable = bits.ToImmutable();

            List<bool> first = immutable.ToList();
            List<bool> second = immutable.ToList();

            CollectionAssert.AreEqual(first, second);
        }

        [Test]
        public void EnumerationAcrossWordBoundariesWorks()
        {
            BitSet bits = new(130);
            bits.TrySet(0);
            bits.TrySet(63);
            bits.TrySet(64);
            bits.TrySet(127);
            bits.TrySet(129);

            ImmutableBitSet immutable = bits.ToImmutable();
            List<bool> values = immutable.ToList();

            Assert.AreEqual(130, values.Count);
            Assert.IsTrue(values[0]);
            Assert.IsTrue(values[63]);
            Assert.IsTrue(values[64]);
            Assert.IsTrue(values[127]);
            Assert.IsTrue(values[129]);
            Assert.IsFalse(values[128]);
        }

        [Test]
        public void ValueTypeAssignmentCreatesCopy()
        {
            BitSet bits = new(64);
            bits.TrySet(5);

            ImmutableBitSet first = bits.ToImmutable();
            ImmutableBitSet second = first;

            Assert.AreEqual(first.Capacity, second.Capacity);
            Assert.AreEqual(first.CountSetBits(), second.CountSetBits());
            Assert.AreEqual(first[5], second[5]);
        }

        [Test]
        public void EqualsWithSameDataReturnsTrue()
        {
            BitSet bits1 = new(64);
            bits1.TrySet(5);
            bits1.TrySet(10);

            BitSet bits2 = new(64);
            bits2.TrySet(5);
            bits2.TrySet(10);

            ImmutableBitSet immutable1 = bits1.ToImmutable();
            ImmutableBitSet immutable2 = bits2.ToImmutable();

            Assert.IsTrue(immutable1.Equals(immutable2));
            Assert.IsTrue(immutable2.Equals(immutable1));
        }

        [Test]
        public void EqualsWithDifferentDataReturnsFalse()
        {
            BitSet bits1 = new(64);
            bits1.TrySet(5);

            BitSet bits2 = new(64);
            bits2.TrySet(10);

            ImmutableBitSet immutable1 = bits1.ToImmutable();
            ImmutableBitSet immutable2 = bits2.ToImmutable();

            Assert.IsFalse(immutable1.Equals(immutable2));
        }

        [Test]
        public void EqualsWithDifferentCapacityReturnsFalse()
        {
            BitSet bits1 = new(64);
            BitSet bits2 = new(128);

            ImmutableBitSet immutable1 = bits1.ToImmutable();
            ImmutableBitSet immutable2 = bits2.ToImmutable();

            Assert.IsFalse(immutable1.Equals(immutable2));
        }

        [Test]
        public void EqualityOperatorWorks()
        {
            BitSet bits1 = new(64);
            bits1.TrySet(5);

            BitSet bits2 = new(64);
            bits2.TrySet(5);

            ImmutableBitSet immutable1 = bits1.ToImmutable();
            ImmutableBitSet immutable2 = bits2.ToImmutable();

            Assert.IsTrue(immutable1 == immutable2);
            Assert.IsFalse(immutable1 != immutable2);
        }

        [Test]
        public void InequalityOperatorWorks()
        {
            BitSet bits1 = new(64);
            bits1.TrySet(5);

            BitSet bits2 = new(64);
            bits2.TrySet(10);

            ImmutableBitSet immutable1 = bits1.ToImmutable();
            ImmutableBitSet immutable2 = bits2.ToImmutable();

            Assert.IsTrue(immutable1 != immutable2);
            Assert.IsFalse(immutable1 == immutable2);
        }

        [Test]
        public void GetHashCodeEqualObjectsHaveSameHash()
        {
            BitSet bits1 = new(64);
            bits1.TrySet(5);
            bits1.TrySet(10);

            BitSet bits2 = new(64);
            bits2.TrySet(5);
            bits2.TrySet(10);

            ImmutableBitSet immutable1 = bits1.ToImmutable();
            ImmutableBitSet immutable2 = bits2.ToImmutable();

            Assert.AreEqual(immutable1.GetHashCode(), immutable2.GetHashCode());
        }

        [Test]
        public void GetHashCodeConsistentAcrossMultipleCalls()
        {
            BitSet bits = new(64);
            bits.TrySet(5);
            ImmutableBitSet immutable = bits.ToImmutable();

            int hash1 = immutable.GetHashCode();
            int hash2 = immutable.GetHashCode();

            Assert.AreEqual(hash1, hash2);
        }

        [TestCase(1)]
        [TestCase(63)]
        [TestCase(64)]
        [TestCase(65)]
        [TestCase(127)]
        [TestCase(128)]
        [TestCase(129)]
        [TestCase(1000)]
        public void VariousCapacitiesWork(int capacity)
        {
            BitSet bits = new(capacity);
            bits.TrySet(0);
            if (1 < capacity)
            {
                bits.TrySet(capacity - 1);
            }

            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.AreEqual(capacity, immutable.Capacity);
            Assert.IsTrue(immutable[0]);
            if (1 < capacity)
            {
                Assert.IsTrue(immutable[capacity - 1]);
            }
        }

        [Test]
        public void AllBoundariesWork()
        {
            BitSet bits = new(256);

            bits.TrySet(0);
            bits.TrySet(63);
            bits.TrySet(64);
            bits.TrySet(127);
            bits.TrySet(128);
            bits.TrySet(191);
            bits.TrySet(192);
            bits.TrySet(255);

            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.IsTrue(immutable[0]);
            Assert.IsTrue(immutable[63]);
            Assert.IsTrue(immutable[64]);
            Assert.IsTrue(immutable[127]);
            Assert.IsTrue(immutable[128]);
            Assert.IsTrue(immutable[191]);
            Assert.IsTrue(immutable[192]);
            Assert.IsTrue(immutable[255]);
            Assert.AreEqual(8, immutable.CountSetBits());
        }

        [Test]
        public void LargeBitSetWorks()
        {
            BitSet bits = new(10000);
            for (int i = 0; i < 10000; i += 100)
            {
                bits.TrySet(i);
            }

            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.AreEqual(10000, immutable.Capacity);
            Assert.AreEqual(100, immutable.CountSetBits());
            for (int i = 0; i < 10000; i++)
            {
                if (i % 100 == 0)
                {
                    Assert.IsTrue(immutable[i], $"Bit {i} should be set");
                }
                else
                {
                    Assert.IsFalse(immutable[i], $"Bit {i} should not be set");
                }
            }
        }

        [Test]
        public void SparseBitSetWorks()
        {
            BitSet bits = new(1000);
            bits.TrySet(0);
            bits.TrySet(999);

            ImmutableBitSet immutable = bits.ToImmutable();

            Assert.AreEqual(2, immutable.CountSetBits());
            Assert.IsTrue(immutable[0]);
            Assert.IsTrue(immutable[999]);

            List<int> setIndices = immutable.EnumerateSetIndices().ToList();
            CollectionAssert.AreEqual(new[] { 0, 999 }, setIndices);
        }

        [Test]
        public void ComplexScenarioMultipleConversions()
        {
            BitSet original = new(128);
            original.TrySet(5);
            original.TrySet(10);
            original.TrySet(64);
            original.TrySet(127);

            ImmutableBitSet immutable1 = original.ToImmutable();

            original.TrySet(20);
            original.TryClear(5);

            ImmutableBitSet immutable2 = original.ToImmutable();

            Assert.IsTrue(immutable1[5]);
            Assert.IsFalse(immutable1[20]);
            Assert.AreEqual(4, immutable1.CountSetBits());

            Assert.IsFalse(immutable2[5]);
            Assert.IsTrue(immutable2[20]);
            Assert.AreEqual(4, immutable2.CountSetBits());

            Assert.IsFalse(immutable1.Equals(immutable2));
        }

        [Test]
        public void ComplexScenarioConversionChain()
        {
            BitSet original = new(64);
            original.TrySet(5);
            original.TrySet(10);
            original.TrySet(20);

            ImmutableBitSet immutable1 = original.ToImmutable();
            BitSet mutable = immutable1.ToBitSet();
            mutable.TrySet(30);
            ImmutableBitSet immutable2 = mutable.ToImmutable();

            Assert.AreEqual(3, immutable1.CountSetBits());
            Assert.IsFalse(immutable1[30]);

            Assert.AreEqual(4, immutable2.CountSetBits());
            Assert.IsTrue(immutable2[30]);
        }

        [Test]
        public void ComplexScenarioEnumerationAfterConversion()
        {
            BitSet bits = new(100);
            for (int i = 0; i < 100; i++)
            {
                if (i % 7 == 0)
                {
                    bits.TrySet(i);
                }
            }

            ImmutableBitSet immutable = bits.ToImmutable();

            int count = 0;
            foreach (int index in immutable.EnumerateSetIndices())
            {
                Assert.AreEqual(0, index % 7);
                count++;
            }

            Assert.AreEqual(15, count);
        }
    }
}
