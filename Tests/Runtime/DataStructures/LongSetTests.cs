// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

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
            LongMap<byte>.KeyEnumerator iterator = set.GetEnumerator();
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
    }
}
