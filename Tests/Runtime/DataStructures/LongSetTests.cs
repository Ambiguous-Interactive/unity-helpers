// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;
    using ThreadAllocationCounter = WallstopStudios.UnityHelpers.Tests.Core.ThreadAllocationCounter;

    [TestFixture]
    [Category("Fast")]
    public sealed class LongSetTests
    {
        [TestCase(0)]
        [TestCase(8)]
        [TestCase(1024)]
        public void EveryUnsignedBoundaryAndHighBitShapeMatchesHashSet(int initialCapacity)
        {
            LongSet set = new(initialCapacity);
            HashSet<ulong> expected = new();
            ulong[] boundaries = { 0, ulong.MaxValue, 1UL << 63, uint.MaxValue };
            foreach (ulong key in boundaries)
            {
                Assert.That(set.TryAdd(key), Is.EqualTo(expected.Add(key)));
                Assert.That(set.TryAdd(key), Is.False);
            }
            for (ulong key = 1; key < 2048; ++key)
            {
                ulong shaped = key << 32;
                Assert.That(set.TryAdd(shaped), Is.EqualTo(expected.Add(shaped)));
                Assert.That(set.Contains(shaped), Is.True);
            }
            CollectionAssert.AreEquivalent(expected, set);
            for (ulong key = 1; key < 2048; key += 2)
            {
                Assert.That(set.Remove(key << 32), Is.EqualTo(expected.Remove(key << 32)));
                Assert.That(set.Remove(key << 32), Is.False);
            }
            Assert.That(set.Count, Is.EqualTo(expected.Count));
            CollectionAssert.AreEquivalent(expected, set);
            int capacity = set.Capacity;
            set.Clear();
            set.Clear();
            Assert.That(set.Count, Is.Zero);
            Assert.That(set.IsEmpty, Is.True);
            Assert.That(set.Capacity, Is.EqualTo(capacity));
            CollectionAssert.IsEmpty(set);
            foreach (ulong key in expected)
            {
                Assert.That(set.Contains(key), Is.False);
                Assert.That(set.TryAdd(key), Is.True);
            }
            CollectionAssert.AreEquivalent(expected, set);
        }

        [Test]
        public void MutationChecksAndBothInterfacesMatch()
        {
            LongSet set = new();
            Assert.That(set.TryAdd(ulong.MaxValue), Is.True);
            LongSet.Enumerator iterator = set.GetEnumerator();
            Assert.That(set.TryAdd(ulong.MaxValue), Is.False);
            Assert.That(set.Remove(1), Is.False);
            Assert.That(iterator.MoveNext(), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(ulong.MaxValue));
            CollectionAssert.AreEqual(new[] { ulong.MaxValue }, (IEnumerable<ulong>)set);
            IEnumerator boxed = ((IEnumerable)set).GetEnumerator();
            Assert.That(boxed.MoveNext(), Is.True);
            Assert.That(boxed.Current, Is.EqualTo(ulong.MaxValue));
            set.Clear();
            Assert.Throws<InvalidOperationException>(() => iterator.MoveNext());
            Assert.Throws<InvalidOperationException>(() => boxed.MoveNext());
        }

        [Test]
        public void WraparoundCollisionsSurviveRemovalClearAndGrowth()
        {
            List<ulong> keys = new();
            for (ulong key = 0; keys.Count < 9; ++key)
            {
                if ((LongHash.Mix(key) & 15) == 15)
                {
                    keys.Add(key);
                }
            }
            LongSet set = new(0);
            for (int index = 0; index < 8; ++index)
            {
                Assert.IsTrue(set.TryAdd(keys[index]));
            }
            Assert.IsTrue(set.Remove(keys[3]));
            Assert.IsTrue(set.Remove(keys[7]));
            Assert.IsTrue(set.TryAdd(keys[7]));
            Assert.IsTrue(set.TryAdd(keys[3]));
            Assert.IsTrue(set.TryAdd(keys[8]));
            Assert.Greater(set.Capacity, 16);
            foreach (ulong key in keys)
            {
                Assert.IsTrue(set.Contains(key));
            }
            set.Clear();
            Assert.IsEmpty(set);
            foreach (ulong key in keys)
            {
                Assert.IsFalse(set.Contains(key));
                Assert.IsTrue(set.TryAdd(key));
            }
            CollectionAssert.AreEquivalent(keys, set);
        }

        [TestCase(11)]
        [TestCase(731)]
        public void SeededOperationsMatchIndependentSetAfterEveryStep(int seed)
        {
            Random random = new(seed);
            HashSet<ulong> expected = new();
            LongSet actual = new(0);
            for (int step = 0; step < 4096; ++step)
            {
                ulong key = (ulong)random.Next(128) << 40;
                int operation = random.Next(32);
                if (operation == 0)
                {
                    actual.Clear();
                    expected.Clear();
                }
                else if (operation < 12)
                {
                    Assert.AreEqual(expected.Remove(key), actual.Remove(key));
                }
                else
                {
                    Assert.AreEqual(expected.Add(key), actual.TryAdd(key));
                }
                Assert.AreEqual(expected.Count, actual.Count);
                Assert.IsTrue(expected.SetEquals(actual));
                Assert.AreEqual(expected.Contains(key), actual.Contains(key));
            }
        }

        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public void InvalidCapacityIsRejected(int capacity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LongSet(capacity));
        }

        [Test]
        public void DedicatedSetRetainsLessStorageThanAMapOfBytes()
        {
            GCAssert.IgnoreIfAllocationMeasurementUnavailable();
            _ = new LongSet(8);
            _ = new LongMap<byte>(8);
            long beforeMap = ThreadAllocationCounter.Read();
            LongMap<byte> map = new(1024);
            long mapBytes = ThreadAllocationCounter.Read() - beforeMap;
            long beforeSet = ThreadAllocationCounter.Read();
            LongSet set = new(1024);
            long setBytes = ThreadAllocationCounter.Read() - beforeSet;
            GC.KeepAlive(map);
            GC.KeepAlive(set);
            if (mapBytes == 0)
            {
                Assert.Ignore("The counter did not observe the retained map arrays.");
            }
            Assert.Less(setBytes, mapBytes);
            TestContext.WriteLine($"Map bytes: {mapBytes}; set bytes: {setBytes}.");
        }
    }
}
