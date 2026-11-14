// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#pragma warning disable WUH010

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
    public sealed class LongMapTests
    {
        private static readonly ulong[] BoundaryKeys =
        {
            0,
            1,
            2,
            63,
            64,
            uint.MaxValue,
            (ulong)uint.MaxValue + 1,
            1UL << 31,
            1UL << 33,
            1UL << 63,
            ulong.MaxValue - 1,
            ulong.MaxValue,
        };

        private static ulong Key(Random random, int shape)
        {
            ulong value = ((ulong)(uint)random.Next() << 32) | (uint)random.Next();
            return shape == 0 ? value
                : shape == 1 ? value << 32
                : (value & 0xFFFFUL) << 48;
        }

        private static void VerifyMap(LongMap<int> actual, Dictionary<ulong, int> expected)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            Assert.That(actual.IsEmpty, Is.EqualTo(expected.Count == 0));
            HashSet<ulong> seen = new();
            foreach (KeyValuePair<ulong, int> entry in actual)
            {
                Assert.That(seen.Add(entry.Key), Is.True);
                Assert.That(expected.TryGetValue(entry.Key, out int value), Is.True);
                Assert.That(entry.Value, Is.EqualTo(value));
                Assert.That(actual.TryGet(entry.Key, out int stored), Is.True);
                Assert.That(stored, Is.EqualTo(value));
                Assert.That(actual.ContainsKey(entry.Key), Is.True);
            }
            CollectionAssert.AreEquivalent(expected.Keys, seen);
            List<ulong> keys = new();
            foreach (ulong key in actual.Keys)
            {
                keys.Add(key);
            }
            CollectionAssert.AreEquivalent(expected.Keys, keys);
            List<int> values = new();
            foreach (int value in actual.Values)
            {
                values.Add(value);
            }
            CollectionAssert.AreEquivalent(expected.Values, values);
        }

        private static long ConsumeMap(LongMap<int> map)
        {
            long sum = 0;
            foreach (KeyValuePair<ulong, int> pair in map)
            {
                sum += pair.Value;
            }
            foreach (ulong key in map.Keys)
            {
                sum += (long)key;
            }
            foreach (int value in map.Values)
            {
                sum += value;
            }
            map.Remove(1, out _);
            map.TryAdd(1, 1);
            return sum;
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(1024)]
        public void EveryUnsignedBoundaryKeySurvivesGrowthAndRemoval(int capacity)
        {
            LongMap<int> map = new(capacity);
            Dictionary<ulong, int> expected = new();
            int value = 0;
            foreach (ulong key in BoundaryKeys)
            {
                Assert.That(map.TrySet(key, value), Is.True);
                expected.Add(key, value);
                ++value;
            }
            VerifyMap(map, expected);
            foreach (ulong key in BoundaryKeys)
            {
                Assert.That(map.TryAdd(key, -1), Is.False);
                Assert.That(map[key], Is.EqualTo(expected[key]));
                Assert.That(map.Remove(key, out int removed), Is.True);
                Assert.That(removed, Is.EqualTo(expected[key]));
                expected.Remove(key);
                Assert.That(map.Remove(key, out removed), Is.False);
                Assert.That(removed, Is.Zero);
                VerifyMap(map, expected);
            }
        }

        [TestCase(0, 17)]
        [TestCase(1, 17)]
        [TestCase(2, 17)]
        [TestCase(0, 741)]
        [TestCase(1, 741)]
        [TestCase(2, 741)]
        public void SeededGrowthOverwriteRemovalAndClearMatchDictionary(int shape, int seed)
        {
            Random random = new(seed);
            LongMap<int> map = new();
            Dictionary<ulong, int> expected = new();
            List<ulong> inserted = new();
            for (int step = 0; step < 4096; ++step)
            {
                ulong key = Key(random, shape);
                if (step % 3 == 0 && 0 < inserted.Count)
                {
                    key = inserted[random.Next(inserted.Count)];
                }
                if (step % 5 == 0)
                {
                    bool present = expected.TryGetValue(key, out int old);
                    Assert.That(map.Remove(key, out int removed), Is.EqualTo(present));
                    Assert.That(removed, Is.EqualTo(old));
                    expected.Remove(key);
                }
                else
                {
                    Assert.That(map.TrySet(key, step), Is.True);
                    expected[key] = step;
                    inserted.Add(key);
                }
                if (step % 256 == 0)
                {
                    VerifyMap(map, expected);
                }
            }
            VerifyMap(map, expected);
            int capacity = map.Capacity;
            map.Clear();
            expected.Clear();
            VerifyMap(map, expected);
            Assert.That(map.Capacity, Is.EqualTo(capacity));
            foreach (ulong key in inserted)
            {
                Assert.That(map.TryGet(key, out int missing), Is.False);
                Assert.That(missing, Is.Zero);
                Assert.That(map.ContainsKey(key), Is.False);
            }
            foreach (ulong key in BoundaryKeys)
            {
                map[key] = 42;
                expected[key] = 42;
            }
            VerifyMap(map, expected);
        }

        [Test]
        public void ChurnClosesProbeChainsWithoutCapacityGrowth()
        {
            LongMap<int> map = new(16);
            Queue<ulong> live = new();
            Dictionary<ulong, int> expected = new();
            for (ulong key = 0; key < 16; ++key)
            {
                Assert.That(map.TryAdd(key << 32, (int)key), Is.True);
                live.Enqueue(key << 32);
                expected.Add(key << 32, (int)key);
            }
            int capacity = map.Capacity;
            for (ulong key = 16; key < 4096; ++key)
            {
                Assert.That(live.TryDequeue(out ulong oldest), Is.True);
                Assert.That(map.Remove(oldest, out _), Is.True);
                expected.Remove(oldest);
                ulong added = key << 32;
                Assert.That(map.TryAdd(added, (int)key), Is.True);
                expected.Add(added, (int)key);
                live.Enqueue(added);
                if (key % 256 == 0)
                {
                    VerifyMap(map, expected);
                }
            }
            VerifyMap(map, expected);
            Assert.That(map.Capacity, Is.EqualTo(capacity));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void AllEnumeratorsRejectSuccessfulMutation(int mutation)
        {
            LongMap<int> map = new();
            Assert.That(map.TrySet(1, 42), Is.True);
            LongMap<int>.Enumerator pairs = map.GetEnumerator();
            LongMap<int>.KeyEnumerator keys = map.Keys.GetEnumerator();
            LongMap<int>.ValueEnumerator values = map.Values.GetEnumerator();
            switch (mutation)
            {
                case 0:
                    map[1] = 44;
                    break;
                case 1:
                    Assert.That(map.TryAdd(2, 44), Is.True);
                    break;
                case 2:
                    Assert.That(map.Remove(1, out _), Is.True);
                    break;
                default:
                    map.Clear();
                    break;
            }
            Assert.Throws<InvalidOperationException>(() => pairs.MoveNext());
            Assert.Throws<InvalidOperationException>(() => keys.MoveNext());
            Assert.Throws<InvalidOperationException>(() => values.MoveNext());
            Assert.Throws<InvalidOperationException>(() => pairs.Reset());
            Assert.Throws<InvalidOperationException>(() => keys.Reset());
            Assert.Throws<InvalidOperationException>(() => values.Reset());
        }

        [Test]
        public void FailedOperationsLeaveEnumeratorsUsableAndViewsStayLive()
        {
            LongMap<int> map = new();
            Assert.That(map.TryAdd(1, 42), Is.True);
            LongMap<int>.KeyView view = map.Keys;
            LongMap<int>.Enumerator iterator = map.GetEnumerator();
            Assert.That(map.TryAdd(1, 43), Is.False);
            Assert.That(map.Remove(2, out _), Is.False);
            Assert.That(iterator.MoveNext(), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(new KeyValuePair<ulong, int>(1, 42)));
            LongMap<int>.Enumerator copy = iterator;
            Assert.That(iterator.MoveNext(), Is.False);
            Assert.That(iterator.Equals(copy), Is.False);
            iterator.Reset();
            Assert.That(iterator.MoveNext(), Is.True);
            Assert.That(iterator.Equals(copy), Is.True);
            Assert.That(iterator.GetHashCode(), Is.EqualTo(copy.GetHashCode()));
            Assert.That(map.TryAdd(2, 43), Is.True);
            CollectionAssert.AreEquivalent(new ulong[] { 1, 2 }, view);
            Assert.That(view, Is.EqualTo(map.Keys));
            Assert.That(view.GetHashCode(), Is.EqualTo(map.Keys.GetHashCode()));
        }

        [Test]
        public void DefaultEnumeratorsAndEmptyViewsAreSafe()
        {
            LongMap<int>.Enumerator pairs = default;
            LongMap<int>.KeyEnumerator keys = default;
            LongMap<int>.ValueEnumerator values = default;
            Assert.That(pairs.MoveNext(), Is.False);
            Assert.That(keys.MoveNext(), Is.False);
            Assert.That(values.MoveNext(), Is.False);
            pairs.Reset();
            keys.Reset();
            values.Reset();
            pairs.Dispose();
            keys.Dispose();
            values.Dispose();
            CollectionAssert.IsEmpty(default(LongMap<int>.KeyView));
            CollectionAssert.IsEmpty(default(LongMap<int>.ValueView));
        }

        [Test]
        public void ReadOnlyAndNongenericInterfacesExposeTheSameEntries()
        {
            LongMap<string> map = new();
            map[ulong.MaxValue] = null;
            IReadOnlyDictionary<ulong, string> view = map;
            Assert.That(view.TryGetValue(ulong.MaxValue, out string value), Is.True);
            Assert.That(value, Is.Null);
            Assert.That(view[ulong.MaxValue], Is.Null);
            Assert.That(view.ContainsKey(ulong.MaxValue), Is.True);
            CollectionAssert.AreEqual(new[] { ulong.MaxValue }, view.Keys);
            CollectionAssert.AreEqual(new string[] { null }, view.Values);
            IEnumerator iterator = ((IEnumerable)map).GetEnumerator();
            Assert.That(iterator.MoveNext(), Is.True);
            Assert.That(
                iterator.Current,
                Is.EqualTo(new KeyValuePair<ulong, string>(ulong.MaxValue, null))
            );
            iterator.Reset();
            Assert.That(iterator.MoveNext(), Is.True);
            Assert.That(iterator.MoveNext(), Is.False);
            Assert.That(view.TryGetValue(1, out value), Is.False);
            Assert.That(value, Is.Null);
            Assert.Throws<KeyNotFoundException>(() => _ = map[1]);
        }

        [TestCase(-1)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void InvalidInitialCapacitiesAreRejected(int capacity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LongMap<int>(capacity));
        }

        [Test]
        public void WarmMutationAndAllConcreteViewsDoNotAllocate()
        {
            LongMap<int> map = new(64);
            for (ulong key = 0; key < 64; ++key)
            {
                map[key] = (int)key;
            }
            GCAssert.IgnoreIfAllocationMeasurementUnavailable();
            long controlStart = ThreadAllocationCounter.Read();
            object control = new byte[128];
            long controlBytes = ThreadAllocationCounter.Read() - controlStart;
            GC.KeepAlive(control);
            if (controlBytes == 0)
            {
                Assert.Ignore("The counter did not observe the retained control.");
            }
            long sum = 0;
            Action consume = () => sum = ConsumeMap(map);
            long allocated = GCAssert.MeasureAllocatedBytes(
                consume,
                warmupIterations: 64,
                measuredIterations: 128
            );
            Assert.That(sum, Is.EqualTo(3L * 2016));
            Assert.That(allocated, Is.Zero);
        }
    }
}
