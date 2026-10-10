// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*
    WUH010 is suppressed for this file: its subject is IntMap, whose indexer is part of the type under test.
    Rewriting those reads through TryGetValue would delete what they assert. Everywhere the
    indexer is incidental, tests read through DictionaryAssertions.ValueFor instead (#653).
*/
#pragma warning disable WUH010

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class IntMapTests
    {
        private static readonly int[] BoundaryKeys =
        {
            IntMap<int>.MinimumAllowedKey,
            int.MinValue + 3,
            -64,
            -1,
            0,
            1,
            64,
            short.MaxValue,
            int.MaxValue - 1,
            int.MaxValue,
        };

        private static bool IsPowerOfTwo(int value)
        {
            return 0 < value && (value & (value - 1)) == 0;
        }

        private static long MeasureAllocated(Action action)
        {
            GCAssert.IgnoreIfAllocationMeasurementUnavailable();
            action();
            try
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                action();
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }
            catch (PlatformNotSupportedException)
            {
                Assert.Ignore("allocation accounting is unavailable on this runtime");
                return -1;
            }
        }

        private static int CountByEnumeration<T>(IntMap<T> map)
        {
            int count = 0;
            using IEnumerator<KeyValuePair<int, T>> enumerator = (
                (IEnumerable<KeyValuePair<int, T>>)map
            ).GetEnumerator();
            while (enumerator.MoveNext())
            {
                ++count;
            }

            return count;
        }

        [TestCase(-1)]
        public void ConstructorRefusesNegativeCapacityHints(int capacityHint)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new IntMap<long>(capacityHint));
        }

        [TestCase((1 << 29) + 1)]
        [TestCase(int.MaxValue)]
        public void ConstructorRefusesHintsBeyondTheTableMaximum(int capacityHint)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new IntMap<long>(capacityHint));
        }

        [TestCase(0)]
        [TestCase(5)]
        [TestCase(16)]
        [TestCase(100)]
        [TestCase(4096)]
        public void ConstructorRoundsTheHintToAPowerOfTwo(int capacityHint)
        {
            IntMap<int> map = new(capacityHint);
            Assert.AreEqual(0, map.Count);
            Assert.IsTrue(IsPowerOfTwo(map.Capacity), $"{map.Capacity} is not a power of two");
            Assert.LessOrEqual(capacityHint, map.Capacity);
        }

        [Test]
        public void SetOverwriteAndRemoveAgreeWithADictionary()
        {
            IntMap<string> map = new();
            Dictionary<int, string> oracle = new();

            for (int key = -32; key < 48; ++key)
            {
                Assert.IsTrue(map.TrySet(key, "v" + key));
                oracle[key] = "v" + key;
            }

            Assert.AreEqual(oracle.Count, map.Count);

            for (int key = -32; key < 48; ++key)
            {
                Assert.IsTrue(map.TryGet(key, out string value));
                Assert.AreEqual("v" + key, value);
                Assert.AreEqual("v" + key, map[key]);

                string updated = "u" + key;
                Assert.IsTrue(map.TrySet(key, updated));
                oracle[key] = updated;
                Assert.AreEqual(updated, map[key]);
            }

            foreach (int key in new[] { -32, 3, 47 })
            {
                Assert.IsTrue(map.Remove(key, out string removedValue));
                Assert.IsTrue(oracle.Remove(key, out string oracleValue));
                Assert.AreEqual(oracleValue, removedValue);
                Assert.IsFalse(map.Remove(key, out _));
                Assert.IsFalse(map.TryGet(key, out _));
            }

            Assert.AreEqual(oracle.Count, map.Count);
        }

        [Test]
        public void RandomOperationSequenceStaysInLockstepWithDictionary()
        {
            for (int seed = 0; seed < 4; ++seed)
            {
                Random random = new Random(seed * 1_000_003 + 7);
                IntMap<long> map = new();
                Dictionary<int, long> oracle = new();

                for (int operation = 0; operation < 20_000; ++operation)
                {
                    int key = random.Next(-512, 512) * random.Next(1, 4);
                    switch (random.Next(6))
                    {
                        case 0:
                        case 1:
                        {
                            long value = random.Next();
                            Assert.IsTrue(map.TrySet(key, value));
                            oracle[key] = value;
                            break;
                        }
                        case 2:
                        {
                            bool removedFromMap = map.Remove(key, out long mapValue);
                            bool removedFromOracle = oracle.Remove(key, out long oracleValue);
                            Assert.AreEqual(
                                removedFromOracle,
                                removedFromMap,
                                $"remove({key}) disagreed at operation {operation}"
                            );
                            if (removedFromMap)
                            {
                                Assert.AreEqual(oracleValue, mapValue);
                            }

                            break;
                        }
                        default:
                        {
                            bool mapHas = map.TryGet(key, out long mapValue);
                            bool oracleHas = oracle.TryGetValue(key, out long oracleValue);
                            Assert.AreEqual(
                                oracleHas,
                                mapHas,
                                $"presence({key}) disagreed at operation {operation}"
                            );
                            if (oracleHas)
                            {
                                Assert.AreEqual(oracleValue, mapValue);
                            }

                            break;
                        }
                    }

                    if (map.Count != oracle.Count)
                    {
                        Assert.Fail(
                            $"count diverged at operation {operation}: {map.Count} vs {oracle.Count}"
                        );
                    }
                }

                int enumerated = 0;
                foreach (KeyValuePair<int, long> pair in map)
                {
                    ++enumerated;
                    Assert.IsTrue(oracle.TryGetValue(pair.Key, out long expected));
                    Assert.AreEqual(expected, pair.Value);
                }

                Assert.AreEqual(oracle.Count, enumerated);
            }
        }

        [Test]
        public void GrowthThroughEveryDoublingPreservesContents()
        {
            IntMap<int> map = new();
            int lastCapacity = map.Capacity;
            for (int index = 0; index < 12_000; ++index)
            {
                Assert.IsTrue(map.TrySet(index * 2, index));
                if (map.Capacity != lastCapacity)
                {
                    Assert.AreEqual(lastCapacity * 2, map.Capacity);
                    lastCapacity = map.Capacity;

                    for (int check = 0; check <= index; ++check)
                    {
                        Assert.IsTrue(map.TryGet(check * 2, out int value));
                        Assert.AreEqual(check, value);
                    }
                }
            }

            Assert.AreEqual(12_000, map.Count);
        }

        [TestCase(0, 1, false)]
        [TestCase(0, 1024, true)]
        [TestCase(16, -1024, false)]
        [TestCase(16, 1024, true)]
        [TestCase(4096, 1, false)]
        [TestCase(4096, -1024, true)]
        public void ReplacingAtHalfCapacityPreservesCapacityAndAllValues(
            int capacityHint,
            int stride,
            bool useIndexer
        )
        {
            IntMap<int> map = new(capacityHint);
            int capacity = map.Capacity;
            int count = capacity / 2;
            for (int index = 0; index < count; ++index)
            {
                Assert.IsTrue(map.TrySet(index * stride, index));
            }

            for (int index = 0; index < count; ++index)
            {
                int key = index * stride;
                if (useIndexer)
                {
                    map[key] = -index;
                }
                else
                {
                    Assert.IsTrue(map.TrySet(key, -index));
                }
                Assert.AreEqual(capacity, map.Capacity);
                Assert.AreEqual(count, map.Count);
            }

            foreach (KeyValuePair<int, int> entry in map)
            {
                Assert.AreEqual(-(entry.Key / stride), entry.Value);
            }
            Assert.AreEqual(count, CountByEnumeration(map));
            Assert.IsTrue(map.TrySet(count * stride, count));
            Assert.AreEqual(capacity * 2, map.Capacity);
            Assert.AreEqual(count + 1, map.Count);
        }

        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(16, false)]
        [TestCase(16, true)]
        [TestCase(4096, false)]
        [TestCase(4096, true)]
        public void ReinsertingRemovedEntryAtHalfOccupancyDoesNotAllocate(
            int capacityHint,
            bool useIndexer
        )
        {
            const int controlSize = 4096;
            long controlBytes = MeasureAllocated(() => GC.KeepAlive(new byte[controlSize]));
            if (controlBytes < controlSize)
            {
                Assert.Ignore("The allocation counter cannot observe a retained allocation.");
                return;
            }

            IntMap<string> map = new(capacityHint);
            int capacity = map.Capacity;
            int count = capacity / 2;
            for (int key = 0; key < count; ++key)
            {
                Assert.IsTrue(map.TrySet(key, "original"));
            }
            Assert.IsTrue(map.Remove(0, out string removed));
            Assert.AreEqual("original", removed);

            long before = GC.GetAllocatedBytesForCurrentThread();
            bool written = true;
            if (useIndexer)
            {
                map[0] = null;
            }
            else
            {
                written = map.TrySet(0, null);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(written);
            Assert.AreEqual(0L, allocated);
            Assert.AreEqual(capacity, map.Capacity);
            Assert.AreEqual(count, map.Count);
            Assert.IsTrue(map.TryGet(0, out string value));
            Assert.IsTrue(value == null);
            for (int key = 1; key < count; ++key)
            {
                Assert.AreEqual("original", map[key]);
            }
        }

        [Test]
        public void AddingAwayFromRemovedSlotPreservesCapacityWhenLiveEntriesFit()
        {
            IntMap<int> map = new(0);
            int capacity = map.Capacity;
            for (int key = 0; key < 4; ++key)
            {
                Assert.IsTrue(map.TrySet(key, key));
            }
            Assert.IsTrue(map.Remove(0, out int removed));
            Assert.AreEqual(0, removed);
            Assert.IsTrue(map.TrySet(4, 4));
            Assert.AreEqual(capacity, map.Capacity);
            Assert.AreEqual(4, map.Count);
            Assert.IsFalse(map.TryGet(0, out _));
            for (int key = 1; key <= 4; ++key)
            {
                Assert.IsTrue(map.TryGet(key, out int value));
                Assert.AreEqual(key, value);
            }
            Assert.IsTrue(map.TrySet(5, 5));
            Assert.AreEqual(capacity * 2, map.Capacity);
        }

        [TestCase(0, 1)]
        [TestCase(0, -1024)]
        [TestCase(16, 1)]
        [TestCase(16, 1024)]
        [TestCase(256, 1)]
        [TestCase(256, -1024)]
        public void SlidingHalfCapacityWindowReclaimsDeletedSlotsBeforeGrowing(
            int capacityHint,
            int stride
        )
        {
            IntMap<int> map = new(capacityHint);
            int capacity = map.Capacity;
            int count = capacity / 2;
            for (int index = 0; index < count; ++index)
            {
                Assert.IsTrue(map.TrySet(index * stride, index));
            }

            const int replacements = 4096;
            for (int index = count; index < count + replacements; ++index)
            {
                Assert.IsTrue(map.Remove((index - count) * stride, out int removed));
                Assert.AreEqual(index - count, removed);
                Assert.IsTrue(map.TrySet(index * stride, index));
                Assert.AreEqual(capacity, map.Capacity);
                Assert.AreEqual(count, map.Count);
                Assert.IsFalse(map.TryGet((index - count) * stride, out _));
            }

            for (int index = replacements; index < replacements + count; ++index)
            {
                Assert.IsTrue(map.TryGet(index * stride, out int value));
                Assert.AreEqual(index, value);
            }
            Assert.AreEqual(count, CountByEnumeration(map));
            Assert.IsTrue(map.TrySet((replacements + count) * stride, replacements + count));
            Assert.AreEqual(capacity * 2, map.Capacity);
            Assert.AreEqual(count + 1, map.Count);
        }

        [TestCase(0, 1)]
        [TestCase(16, 1024)]
        [TestCase(256, -1024)]
        public void SlidingHalfCapacityWindowDoesNotAllocate(int capacityHint, int stride)
        {
            const int controlSize = 4096;
            long controlBytes = MeasureAllocated(() => GC.KeepAlive(new byte[controlSize]));
            if (controlBytes < controlSize)
            {
                Assert.Ignore("The allocation counter cannot observe a retained allocation.");
                return;
            }

            IntMap<int> map = new(capacityHint);
            int capacity = map.Capacity;
            int count = capacity / 2;
            for (int index = 0; index < count; ++index)
            {
                Assert.IsTrue(map.TrySet(index * stride, index));
            }
            int next = count;
            long allocated = MeasureAllocated(() =>
            {
                for (int replacement = 0; replacement < 128; ++replacement)
                {
                    map.Remove((next - count) * stride, out _);
                    map.TrySet(next * stride, next);
                    ++next;
                }
            });
            Assert.AreEqual(0L, allocated);
            Assert.AreEqual(capacity, map.Capacity);
            Assert.AreEqual(count, map.Count);
            for (int index = next - count; index < next; ++index)
            {
                Assert.IsTrue(map.TryGet(index * stride, out int value));
                Assert.AreEqual(index, value);
            }
        }

        [TestCase(new[] { -191, -183, -178, -170 })]
        [TestCase(new[] { -196, -188, -180, -175 })]
        [TestCase(new[] { -199, -194, -186, -173 })]
        [TestCase(new[] { -191, -199, -183, -194 })]
        [TestCase(new[] { -191, -196, -199, -183 })]
        [TestCase(new[] { int.MinValue + 2, int.MinValue + 3, int.MaxValue, 0 })]
        public void EveryDeletionOrderPreservesSmallProbeChains(int[] keys)
        {
            for (int first = 0; first < keys.Length; ++first)
            {
                for (int second = 0; second < keys.Length; ++second)
                {
                    if (first == second)
                    {
                        continue;
                    }
                    for (int third = 0; third < keys.Length; ++third)
                    {
                        if (first == third || second == third)
                        {
                            continue;
                        }
                        int[] order = { first, second, third, 6 - first - second - third };
                        bool[] removed = new bool[keys.Length];
                        IntMap<string> map = new(0);
                        int capacity = map.Capacity;
                        for (int index = 0; index < keys.Length; ++index)
                        {
                            Assert.IsTrue(
                                map.TrySet(keys[index], index % 2 == 0 ? "stored" : null)
                            );
                        }
                        foreach (int removedIndex in order)
                        {
                            Assert.IsTrue(map.Remove(keys[removedIndex], out string value));
                            Assert.AreEqual(removedIndex % 2 == 0 ? "stored" : null, value);
                            removed[removedIndex] = true;
                            int survivors = 0;
                            for (int index = 0; index < keys.Length; ++index)
                            {
                                Assert.AreEqual(
                                    !removed[index],
                                    map.TryGet(keys[index], out string stored)
                                );
                                Assert.AreEqual(
                                    !removed[index] && index % 2 == 0 ? "stored" : null,
                                    stored
                                );
                                if (!removed[index])
                                {
                                    ++survivors;
                                }
                            }
                            Assert.AreEqual(survivors, map.Count);
                            Assert.AreEqual(survivors, CountByEnumeration(map));
                            Assert.AreEqual(capacity, map.Capacity);
                        }
                        foreach (int key in keys)
                        {
                            Assert.IsTrue(map.TrySet(key, "again"));
                            Assert.AreEqual("again", map[key]);
                        }
                        map.Clear();
                        Assert.AreEqual(0, map.Count);
                        Assert.AreEqual(0, CountByEnumeration(map));
                    }
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void RandomSmallTableChurnMatchesDictionary(int seed)
        {
            int[] keys =
            {
                IntMap<string>.MinimumAllowedKey,
                int.MinValue + 3,
                -199,
                -194,
                -191,
                -188,
                -183,
                -180,
                -178,
                -175,
                -170,
                -1,
                0,
                1,
                int.MaxValue - 1,
                int.MaxValue,
            };
            Random random = new(seed);
            IntMap<string> map = new(0);
            Dictionary<int, string> oracle = new();
            int capacity = map.Capacity;
            for (int index = 0; index < capacity / 2; ++index)
            {
                string value = index % 2 == 0 ? "stored" : null;
                map.TrySet(keys[index], value);
                oracle[keys[index]] = value;
            }
            for (int operation = 0; operation < 2048; ++operation)
            {
                int selected = random.Next(oracle.Count);
                int removedKey = 0;
                foreach (int storedKey in oracle.Keys)
                {
                    removedKey = storedKey;
                    if (selected == 0)
                    {
                        break;
                    }
                    --selected;
                }
                Assert.IsTrue(oracle.Remove(removedKey, out string expectedRemoved));
                Assert.IsTrue(map.Remove(removedKey, out string actualRemoved));
                Assert.AreEqual(expectedRemoved, actualRemoved);
                int nextIndex = random.Next(keys.Length);
                while (oracle.ContainsKey(keys[nextIndex]))
                {
                    ++nextIndex;
                    if (keys.Length <= nextIndex)
                    {
                        nextIndex = 0;
                    }
                }
                int nextKey = keys[nextIndex];
                string nextValue = operation % 3 == 0 ? null : "next";
                oracle[nextKey] = nextValue;
                Assert.IsTrue(map.TrySet(nextKey, nextValue));
                Assert.AreEqual(oracle.Count, map.Count);
                Assert.AreEqual(capacity, map.Capacity);
                Assert.AreEqual(oracle.Count, CountByEnumeration(map));
                foreach (int key in keys)
                {
                    Assert.AreEqual(
                        oracle.TryGetValue(key, out string expected),
                        map.TryGet(key, out string actual)
                    );
                    Assert.AreEqual(expected, actual);
                }
                foreach (KeyValuePair<int, string> entry in map)
                {
                    Assert.IsTrue(oracle.TryGetValue(entry.Key, out string expected));
                    Assert.AreEqual(expected, entry.Value);
                }
            }
        }

        [Test]
        public void RemovingWithinWrappingProbeChainInvalidatesAllEnumerators()
        {
            IntMap<int> map = new(0);
            foreach (int key in new[] { -191, -183, -178, -170 })
            {
                Assert.IsTrue(map.TrySet(key, key));
            }
            IntMap<int>.Enumerator entries = map.GetEnumerator();
            IntMap<int>.KeyEnumerator keys = map.Keys.GetEnumerator();
            IntMap<int>.ValueEnumerator values = map.Values.GetEnumerator();
            Assert.IsTrue(entries.MoveNext());
            Assert.IsTrue(keys.MoveNext());
            Assert.IsTrue(values.MoveNext());
            Assert.IsTrue(map.Remove(-191, out _));
            Assert.Throws<InvalidOperationException>(() => entries.MoveNext());
            Assert.Throws<InvalidOperationException>(() => entries.Reset());
            Assert.Throws<InvalidOperationException>(() => keys.MoveNext());
            Assert.Throws<InvalidOperationException>(() => keys.Reset());
            Assert.Throws<InvalidOperationException>(() => values.MoveNext());
            Assert.Throws<InvalidOperationException>(() => values.Reset());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReplacingAfterRemovalsPreservesNullsAndInvalidatesEnumerators(bool useIndexer)
        {
            IntMap<string> map = new();
            int capacity = map.Capacity;
            int halfCapacity = capacity / 2;
            for (int key = 0; key < halfCapacity; ++key)
            {
                map.TrySet(key * 1024, "original");
            }
            for (int key = 1; key < halfCapacity; key += 2)
            {
                Assert.IsTrue(map.Remove(key * 1024, out string removed));
                Assert.AreEqual("original", removed);
            }

            IntMap<string>.Enumerator enumerator = map.GetEnumerator();
            Assert.IsTrue(enumerator.MoveNext());
            for (int key = 0; key < halfCapacity; key += 2)
            {
                if (useIndexer)
                {
                    map[key * 1024] = null;
                }
                else
                {
                    Assert.IsTrue(map.TrySet(key * 1024, null));
                }
                Assert.AreEqual(capacity, map.Capacity);
            }
            Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
            Assert.Throws<InvalidOperationException>(() => enumerator.Reset());
            Assert.AreEqual(halfCapacity / 2, map.Count);
            for (int key = 0; key < halfCapacity; ++key)
            {
                Assert.AreEqual(key % 2 == 0, map.TryGet(key * 1024, out string value));
                Assert.IsTrue(value == null);
            }
        }

        [TestCase(0, 0, false)]
        [TestCase(16, 0, true)]
        [TestCase(16, 8, false)]
        [TestCase(16, 24, true)]
        [TestCase(4096, 0, false)]
        [TestCase(4096, 4096, true)]
        public void FirstReplacementAtTheOccupancyLimitDoesNotAllocate(
            int capacityHint,
            int removedCount,
            bool useIndexer
        )
        {
            GCAssert.IgnoreIfAllocationMeasurementUnavailable();
            const int controlSize = 4096;
            try
            {
                long beforeControl = GC.GetAllocatedBytesForCurrentThread();
                byte[] control = new byte[controlSize];
                long controlBytes = GC.GetAllocatedBytesForCurrentThread() - beforeControl;
                GC.KeepAlive(control);
                if (controlBytes < controlSize)
                {
                    Assert.Ignore("The allocation counter cannot observe a retained allocation.");
                    return;
                }
            }
            catch (PlatformNotSupportedException)
            {
                Assert.Ignore("The allocation counter is unavailable on this platform.");
                return;
            }

            IntMap<string> map = new(capacityHint);
            int count = map.Capacity / 2;
            for (int key = 0; key < count; ++key)
            {
                if (useIndexer)
                {
                    map[key] = "original";
                }
                else
                {
                    map.TrySet(key, "original");
                }
            }
            for (int key = 1; key <= removedCount; ++key)
            {
                Assert.IsTrue(map.Remove(key, out _));
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            bool written = true;
            if (useIndexer)
            {
                map[0] = null;
            }
            else
            {
                written = map.TrySet(0, null);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(written);
            Assert.AreEqual(0L, allocated);
            Assert.IsTrue(map.TryGet(0, out string value));
            Assert.IsTrue(value == null);
            Assert.AreEqual(count - removedCount, map.Count);
        }

        [Test]
        public void TombstoneHeavyChurnEndsEmptyAndStaysCorrect()
        {
            IntMap<string> map = new();
            for (int round = 0; round < 5; ++round)
            {
                int baseKey = round * 10_000 + 1_000_000;
                for (int key = 0; key < 10_000; ++key)
                {
                    Assert.IsTrue(map.TrySet(key + baseKey, "a"));
                }

                Assert.AreEqual(10_000, map.Count);
                for (int key = 0; key < 10_000; ++key)
                {
                    Assert.IsTrue(map.Remove(key + baseKey, out string removed));
                    Assert.AreEqual("a", removed);
                }

                Assert.AreEqual(0, map.Count);
                Assert.IsTrue(map.IsEmpty);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(8)]
        [TestCase(16)]
        [TestCase(256)]
        public void SlidingLiveWindowRetainsItsStartingCapacity(int liveWindow)
        {
            IntMap<int> map = new(liveWindow);
            int startingCapacity = map.Capacity;
            const int totalKeys = 20_000;
            for (int key = 0; key < totalKeys; ++key)
            {
                Assert.IsTrue(map.TrySet(key, key));
                if (liveWindow <= key)
                {
                    Assert.IsTrue(map.Remove(key - liveWindow, out int removed));
                    Assert.AreEqual(key - liveWindow, removed);
                }
            }

            Assert.AreEqual(liveWindow, map.Count);
            Assert.AreEqual(startingCapacity, map.Capacity);
            Assert.AreEqual(liveWindow, CountByEnumeration(map));
            for (int key = totalKeys - liveWindow; key < totalKeys; ++key)
            {
                Assert.IsTrue(map.TryGet(key, out int value));
                Assert.AreEqual(key, value);
            }
            Assert.IsFalse(map.TryGet(totalKeys - liveWindow - 1, out _));
        }

        [Test]
        public void TombstoneCleanupPreservesSurvivorsAndStillGrowsForLiveEntries()
        {
            IntMap<int> map = new();
            int startingCapacity = map.Capacity;
            int halfCapacity = startingCapacity / 2;
            for (int key = 0; key < halfCapacity; ++key)
            {
                map.TrySet(key, key);
            }
            for (int key = 8; key < halfCapacity; ++key)
            {
                Assert.IsTrue(map.Remove(key, out _));
            }

            Assert.IsTrue(map.TrySet(halfCapacity, halfCapacity));
            Assert.AreEqual(startingCapacity, map.Capacity);
            Assert.AreEqual(9, map.Count);
            for (int key = 0; key < 8; ++key)
            {
                Assert.IsTrue(map.TryGet(key, out int value));
                Assert.AreEqual(key, value);
            }
            for (int key = 8; key < halfCapacity; ++key)
            {
                Assert.IsFalse(map.TryGet(key, out _));
                Assert.IsTrue(map.TrySet(key, key));
            }

            Assert.AreEqual(halfCapacity + 1, map.Count);
            Assert.AreEqual(startingCapacity * 2, map.Capacity);
            for (int key = 0; key <= halfCapacity; ++key)
            {
                Assert.IsTrue(map.TryGet(key, out int value));
                Assert.AreEqual(key, value);
            }
        }

        [TestCase(64, 1024)]
        [TestCase(1024, 1024)]
        [TestCase(1024, -1024)]
        [TestCase(1024, 65_536)]
        public void SharedLowBitsRoundTripAcrossGrowthRemovalAndReinsertion(int count, int stride)
        {
            IntMap<int> map = new();
            for (int index = 0; index < count; ++index)
            {
                Assert.IsTrue(map.TrySet(index * stride, index));
            }
            for (int index = 0; index < count; index += 2)
            {
                Assert.IsTrue(map.Remove(index * stride, out int removed));
                Assert.AreEqual(index, removed);
            }
            for (int index = 0; index < count; ++index)
            {
                Assert.AreEqual(index % 2 != 0, map.TryGet(index * stride, out int value));
                Assert.AreEqual(index % 2 == 0 ? 0 : index, value);
                Assert.IsFalse(map.TryGet((index + count) * stride, out _));
            }
            for (int index = 0; index < count; index += 2)
            {
                Assert.IsTrue(map.TrySet(index * stride, -index));
            }
            for (int index = 0; index < count; ++index)
            {
                Assert.IsTrue(map.TryGet(index * stride, out int value));
                Assert.AreEqual(index % 2 == 0 ? -index : index, value);
            }
            Assert.AreEqual(count, map.Count);
        }

        [Test]
        public void ReservedMarkerKeysAreRefusedWithoutThrowing()
        {
            IntMap<string> map = new();
            int belowMinimum = IntMap<string>.MinimumAllowedKey - 1;

            Assert.IsFalse(map.TrySet(belowMinimum, "x"));
            Assert.IsFalse(map.TryGet(belowMinimum, out _));
            Assert.IsFalse(map.Remove(belowMinimum, out _));
            Assert.AreEqual(0, map.Count);

            int minimum = IntMap<string>.MinimumAllowedKey;
            Assert.IsTrue(map.TrySet(minimum, "edge"));
            Assert.IsTrue(map.TryGet(minimum, out string edgeValue));
            Assert.AreEqual("edge", edgeValue);
        }

        [Test]
        public void IndexerThrowsForAbsentOrReservedKeys()
        {
            IntMap<string> map = new();
            Assert.Throws<KeyNotFoundException>(() => _ = map[42]);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _ = map[IntMap<string>.MinimumAllowedKey - 1]
            );

            map[42] = "answer";
            Assert.AreEqual("answer", map[42]);
        }

        [Test]
        public void EveryBoundaryKeyRoundTrips()
        {
            IntMap<Guid> map = new();
            Guid value = Guid.NewGuid();
            foreach (int key in BoundaryKeys)
            {
                map.TrySet(key, value);
            }

            Assert.AreEqual(BoundaryKeys.Length, map.Count);
            foreach (int key in BoundaryKeys)
            {
                Assert.IsTrue(map.TryGet(key, out Guid stored));
                Assert.AreEqual(value, stored);
            }
        }

        [Test]
        public void ClearEmptiesTheMapButKeepsItUsable()
        {
            IntMap<long> map = new();
            for (int key = 0; key < 500; ++key)
            {
                Assert.IsTrue(map.TrySet(key, key * 3L));
            }

            Assert.AreEqual(500, map.Count);
            map.Clear();
            Assert.AreEqual(0, map.Count);
            Assert.IsFalse(map.TryGet(7, out _));
            Assert.AreEqual(0, CountByEnumeration(map));

            for (int key = 0; key < 50; ++key)
            {
                Assert.IsTrue(map.TrySet(key, key));
            }

            Assert.AreEqual(50, map.Count);
        }

        [Test]
        public void MutatingDuringEnumerationIsAnError()
        {
            IntMap<int> map = new();
            for (int key = 0; key < 32; ++key)
            {
                Assert.IsTrue(map.TrySet(key, key));
            }

            using IEnumerator<KeyValuePair<int, int>> enumerator = (
                (IEnumerable<KeyValuePair<int, int>>)map
            ).GetEnumerator();
            Assert.IsTrue(enumerator.MoveNext());
            map.TrySet(99, 99);
            Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
        }

        [Test]
        public void RemovingAClassValueHandsBackTheSameReference()
        {
            IntMap<GuidHolder> map = new();
            GuidHolder holder = new GuidHolder { Payload = Guid.NewGuid() };
            Assert.IsTrue(map.TrySet(1, holder));
            Assert.IsTrue(map.Remove(1, out GuidHolder removed));
            Assert.AreSame(holder, removed);
            Assert.IsFalse(map.TryGet(1, out _));

            Assert.IsTrue(map.TrySet(1, null));
            Assert.IsTrue(map.TryGet(1, out GuidHolder absent));
            Assert.IsTrue(absent == null);
        }

        [Test]
        public void KeysAndValuesViewsEnumerateLiveEntriesWithoutAllocating()
        {
            IntMap<GuidHolder> map = new();
            for (int key = 0; key < 24; ++key)
            {
                map.TrySet(key * 5, new GuidHolder { Payload = Guid.NewGuid() });
            }

            int keySum = 0;
            int valueCount = 0;
            int valueSeen = 0;
            foreach (int key in map.Keys)
            {
                ++valueCount;
                keySum += key;
                if (!map.ContainsKey(key) || map[key] == null)
                {
                    Assert.Fail($"key {key} should be live with a value");
                }
                else
                {
                    ++valueSeen;
                }
            }
            int valuesEnumerated = 0;
            foreach (GuidHolder value in map.Values)
            {
                if (value != null)
                {
                    ++valuesEnumerated;
                }
            }

            Assert.AreEqual(24, valueCount);
            Assert.AreEqual(24, valueSeen);
            Assert.AreEqual(24, valuesEnumerated);
            Assert.AreEqual(5 * 23 * 24 / 2, keySum);

            long allocated = MeasureAllocated(() =>
            {
                foreach (int ignored in map.Keys) { }
                foreach (GuidHolder ignored in map.Values) { }
            });
            Assert.AreEqual(
                0L,
                allocated,
                "typed foreach over Keys or Values must reach the struct enumerators"
            );
        }

        [Test]
        public void KeyAndViewEnumeratorsFailFastWhenTheMapChanges()
        {
            IntMap<int> map = new();
            for (int key = 0; key < 16; ++key)
            {
                map.TrySet(key, key);
            }

            IEnumerator<int> keys = ((IEnumerable<int>)map.Keys).GetEnumerator();
            Assert.IsTrue(keys.MoveNext());
            map.TrySet(1000, 1000);
            Assert.Throws<InvalidOperationException>(() => keys.MoveNext());

            IntMap<int> second = new();
            for (int key = 0; key < 4; ++key)
            {
                second.TrySet(key, key);
            }

            IEnumerator<int> frozenKeys = ((IEnumerable<int>)second.Keys).GetEnumerator();
            IEnumerator<int> typedReset = ((IEnumerable<int>)second.Keys).GetEnumerator();
            Assert.IsTrue(typedReset.MoveNext());
            second.Remove(0, out _);
            Assert.Throws<InvalidOperationException>(() => frozenKeys.MoveNext());
            Assert.Throws<InvalidOperationException>(() => typedReset.Reset());
        }

        private sealed class GuidHolder
        {
            public Guid Payload;
        }
    }
}
