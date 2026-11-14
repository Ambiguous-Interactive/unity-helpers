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
    using ThreadAllocationCounter = WallstopStudios.UnityHelpers.Tests.Core.ThreadAllocationCounter;

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

        private static void AssertViewsMatch(IntMap<int> map, Dictionary<int, int> oracle)
        {
            HashSet<int> seen = new();
            foreach (KeyValuePair<int, int> entry in map)
            {
                Assert.IsTrue(seen.Add(entry.Key), "An entry must be yielded once.");
                Assert.IsTrue(oracle.TryGetValue(entry.Key, out int expected));
                Assert.AreEqual(expected, entry.Value);
            }
            Assert.AreEqual(oracle.Count, seen.Count);
            seen.Clear();
            foreach (int key in map.Keys)
            {
                Assert.IsTrue(seen.Add(key), "A key must be yielded once.");
                Assert.IsTrue(oracle.ContainsKey(key));
            }
            Assert.AreEqual(oracle.Count, seen.Count);
            Dictionary<int, int> valueCounts = new();
            foreach (int value in oracle.Values)
            {
                if (valueCounts.TryGetValue(value, out int count))
                {
                    valueCounts[value] = count + 1;
                }
                else
                {
                    valueCounts.Add(value, 1);
                }
            }
            foreach (int value in map.Values)
            {
                Assert.IsTrue(valueCounts.TryGetValue(value, out int count));
                Assert.That(count, Is.Positive);
                if (count == 1)
                {
                    valueCounts.Remove(value);
                }
                else
                {
                    valueCounts[value] = count - 1;
                }
            }
            Assert.AreEqual(0, valueCounts.Count);
        }

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
                long before = ThreadAllocationCounter.Read();
                action();
                return ThreadAllocationCounter.Read() - before;
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

#if !UNITY_5_3_OR_NEWER
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining
        )]
        private static (IntMap<object> map, WeakReference[] references) CreateReferenceMap(
            int hint,
            int count
        )
        {
            IntMap<object> map = new(hint);
            WeakReference[] references = new WeakReference[count + 1];
            for (int index = 0; index <= count; ++index)
            {
                object value = new();
                references[index] = new WeakReference(value);
                Assert.IsTrue(map.TrySet(index, value));
            }
            return (map, references);
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining
        )]
        private static WeakReference CreateUnrootedReference()
        {
            return new WeakReference(new object());
        }
#endif

        [TestCase(8)]
        [TestCase(64)]
        [TestCase(1024)]
        [TestCase(16385)]
        public void OppositeSignKeysPreserveRawIdentityAcrossEveryMapOperation(int count)
        {
            IntMap<int> map = new();
            Dictionary<int, int> oracle = new();
            int[] keys = new int[count];
            for (int index = 0; index < count; ++index)
            {
                int key = index % 2 == 0 ? index / 2 : int.MinValue + 2 + index / 2;
                keys[index] = key;
                map.TrySet(key, index);
                oracle.Add(key, index);
            }
            map.TrySet(int.MaxValue, -1);
            oracle.Add(int.MaxValue, -1);
            AssertViewsMatch(map, oracle);
            for (int index = 0; index < count; ++index)
            {
                int key = keys[index];
                Assert.IsTrue(map.ContainsKey(key));
                Assert.AreEqual(index, map[key]);
                if (index % 3 == 0)
                {
                    Assert.IsTrue(map.Remove(key, out int removed));
                    Assert.AreEqual(index, removed);
                    oracle.Remove(key);
                    Assert.IsFalse(map.TryGet(key, out int missing));
                    Assert.AreEqual(0, missing);
                }
                else
                {
                    map[key] = -index;
                    oracle[key] = -index;
                }
            }
            AssertViewsMatch(map, oracle);
            foreach (int key in keys)
            {
                Assert.IsTrue(map.TrySet(key, key));
                oracle[key] = key;
            }
            AssertViewsMatch(map, oracle);
            map.Clear();
            oracle.Clear();
            foreach (int key in keys)
            {
                map[key] = key;
                oracle.Add(key, key);
            }
            AssertViewsMatch(map, oracle);
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

        [TestCase(0, 16)]
        [TestCase(1, 16)]
        [TestCase(2, 16)]
        [TestCase(3, 16)]
        [TestCase(16, 128)]
        [TestCase(17, 128)]
        [TestCase(1000, 4096)]
        public void ConstructorReservesFourSlotsPerRequestedEntry(
            int capacityHint,
            int expectedCapacity
        )
        {
            IntMap<int> map = new(capacityHint);
            Assert.AreEqual(expectedCapacity, map.Capacity);
            for (int key = 0; key < capacityHint; ++key)
            {
                Assert.IsTrue(map.TrySet(key, key));
            }
            Assert.AreEqual(expectedCapacity, map.Capacity);
            Assert.AreEqual(capacityHint, map.Count);
        }

        [Test]
        public void DefaultTableKeepsSixtyFourSlotsUntilTheSeventeenthEntry()
        {
            IntMap<int> map = new();
            Assert.AreEqual(64, map.Capacity);
            for (int key = 0; key < 16; ++key)
            {
                map.TrySet(key, key);
            }
            Assert.AreEqual(64, map.Capacity);
            Assert.IsTrue(map.TrySet(16, 16));
            Assert.AreEqual(128, map.Capacity);
            for (int key = 0; key <= 16; ++key)
            {
                Assert.IsTrue(map.TryGet(key, out int value));
                Assert.AreEqual(key, value);
            }
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

        [TestCase(255)]
        [TestCase(256)]
        [TestCase(257)]
        [TestCase(16383)]
        [TestCase(16384)]
        [TestCase(16385)]
        [TestCase(65535)]
        [TestCase(65536)]
        [TestCase(65537)]
        public void EnumerationStaysCompleteAcrossGrowthAndRemovalBoundaries(int count)
        {
            IntMap<int> map = new(0);
            for (int key = 0; key < count; ++key)
            {
                if (!map.TrySet(key, key))
                {
                    Assert.Fail($"Could not add {key}.");
                }
            }
            Assert.That(map.Remove(count - 1, out int last), Is.True);
            Assert.That(last, Is.EqualTo(count - 1));
            Assert.That(map.Remove(0, out int first), Is.True);
            Assert.That(first, Is.Zero);
            HashSet<int> seen = new();
            foreach (KeyValuePair<int, int> pair in map)
            {
                if (
                    pair.Key != pair.Value
                    || pair.Key < 1
                    || count - 1 <= pair.Key
                    || !seen.Add(pair.Key)
                )
                {
                    Assert.Fail($"Invalid or repeated entry {pair.Key}.");
                }
            }
            Assert.That(seen.Count, Is.EqualTo(count - 2));
            int keyCount = 0;
            foreach (int key in map.Keys)
            {
                if (!seen.Contains(key))
                {
                    Assert.Fail($"Unexpected key {key}.");
                }
                ++keyCount;
            }
            int valueCount = 0;
            foreach (int value in map.Values)
            {
                if (!seen.Contains(value))
                {
                    Assert.Fail($"Unexpected value {value}.");
                }
                ++valueCount;
            }
            Assert.That(keyCount, Is.EqualTo(seen.Count));
            Assert.That(valueCount, Is.EqualTo(seen.Count));
            map.Clear();
            Assert.That(map.Count, Is.Zero);
            Assert.That(map.TrySet(int.MaxValue, 7), Is.True);
            Dictionary<int, int> expected = new() { { int.MaxValue, 7 } };
            AssertViewsMatch(map, expected);
        }

        [Test]
        public void ZeroKeyKeepsPresenceAndValueAcrossGrowthRemovalAndClear()
        {
            IntMap<string> map = new(0);
            Assert.IsTrue(map.TrySet(0, null));
            Assert.IsFalse(map.IsEmpty);
            Assert.That(map.Count, Is.EqualTo(1));
            Assert.IsTrue(map.TryGet(0, out string initial));
            Assert.That(initial, Is.Null);
            Assert.IsTrue(map.ContainsKey(0));
            Assert.IsFalse(map.ContainsKey(1));
            Assert.That(map[0], Is.Null);
            IntMap<string>.Enumerator snapshot = map.GetEnumerator();
            map[0] = "zero";
            Assert.Throws<InvalidOperationException>(() => snapshot.MoveNext());
            for (int key = 1; key < 66; ++key)
            {
                map.TrySet(key, "other");
            }
            Assert.That(map.Count, Is.EqualTo(66));
            Assert.IsTrue(map.TryGet(0, out string grown));
            Assert.That(grown, Is.EqualTo("zero"));
            int zeros = 0;
            foreach (KeyValuePair<int, string> pair in map)
            {
                if (pair.Key == 0)
                {
                    ++zeros;
                    Assert.That(pair.Value, Is.EqualTo("zero"));
                }
            }
            Assert.That(zeros, Is.EqualTo(1));
            Assert.IsTrue(map.Remove(0, out string removed));
            Assert.That(removed, Is.EqualTo("zero"));
            Assert.IsFalse(map.TryGet(0, out string absent));
            Assert.That(absent, Is.Null);
            Assert.IsFalse(map.Remove(0, out _));
            Assert.Throws<KeyNotFoundException>(() =>
            {
                _ = map[0];
            });
            map.Clear();
            Assert.IsTrue(map.IsEmpty);
            Assert.IsTrue(map.TrySet(0, "again"));
            Assert.That(map.Count, Is.EqualTo(1));
            Assert.That(map[0], Is.EqualTo("again"));
            Assert.IsTrue(map.Remove(0, out _));
            Assert.IsTrue(map.IsEmpty);
        }

#if UNITY_5_3_OR_NEWER
        [Test]
        public void TypedViewsReplacementRemovalAndClearHaveNoProfilerAllocationEvents()
        {
            object[] retained = new object[1];
            object existing = new();
            Action allocate = () => retained[0] = new object();
            Action reuse = () => retained[0] = existing;
            const int iterations = 128;
            const int capacity = 512;
            long positive =
                global::WallstopStudios.UnityHelpers.Tests.Core.ProfilerAllocationCounter.Measure(
                    allocate,
                    iterations,
                    capacity
                );
            if (positive == 0)
            {
                Assert.Ignore("The GC.Alloc recorder cannot observe fresh retained objects.");
            }
            Assert.That(positive, Is.EqualTo(iterations));
            Assert.That(
                global::WallstopStudios.UnityHelpers.Tests.Core.ProfilerAllocationCounter.Measure(
                    reuse,
                    iterations,
                    capacity
                ),
                Is.Zero
            );
            IntMap<int> map = new();
            for (int key = 0; key < 16; ++key)
            {
                map.TrySet(key, key);
            }
            int sum = 0;
            Action operations = () =>
            {
                map.TrySet(7, 7);
                map.Remove(11, out _);
                map.TrySet(11, 11);
                foreach (KeyValuePair<int, int> pair in map)
                {
                    sum += pair.Value;
                }
                foreach (int key in map.Keys)
                {
                    sum += key;
                }
                foreach (int value in map.Values)
                {
                    sum += value;
                }
                map.Clear();
                for (int key = 0; key < 16; ++key)
                {
                    map.TrySet(key, key);
                }
            };
            Assert.That(
                global::WallstopStudios.UnityHelpers.Tests.Core.ProfilerAllocationCounter.Measure(
                    operations,
                    iterations,
                    capacity
                ),
                Is.Zero
            );
            Assert.That(sum, Is.EqualTo((iterations + 64) * 360));
            Assert.That(map.Capacity, Is.EqualTo(64));
        }
#endif

        [TestCase(0, 0)]
        [TestCase(0, 1)]
        [TestCase(0, 2)]
        [TestCase(4096, 0)]
        [TestCase(4096, 1)]
        [TestCase(4096, 2)]
        public void EveryViewMatchesTheOracleAfterEachRemovalAndRefill(
            int capacityHint,
            int keyShape
        )
        {
            IntMap<int> map = new(capacityHint);
            Dictionary<int, int> oracle = new();
            Random random = new(7391);
            List<int> keys = new();
            for (int index = 0; index < 129; ++index)
            {
                int key =
                    keyShape == 0 ? index
                    : keyShape == 1 ? index * 1024
                    : random.Next();
                if (oracle.ContainsKey(key))
                {
                    --index;
                    continue;
                }
                keys.Add(key);
                oracle.Add(key, index);
                Assert.IsTrue(map.TrySet(key, index));
            }
            for (int remaining = keys.Count; 0 < remaining; --remaining)
            {
                int index = random.Next(remaining);
                int key = keys[index];
                keys[index] = keys[remaining - 1];
                Assert.IsTrue(oracle.Remove(key, out int expected));
                Assert.IsTrue(map.Remove(key, out int actual));
                Assert.AreEqual(expected, actual);
                AssertViewsMatch(map, oracle);
            }
            Assert.AreEqual(0, map.Count);
            foreach (int key in BoundaryKeys)
            {
                Assert.IsTrue(map.TrySet(key, key));
                oracle.Add(key, key);
            }
            AssertViewsMatch(map, oracle);
            map.Clear();
            oracle.Clear();
            AssertViewsMatch(map, oracle);
            Assert.IsTrue(map.TrySet(0, 42));
            oracle.Add(0, 42);
            AssertViewsMatch(map, oracle);
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

        [TestCase(64)]
        [TestCase(16384)]
        public void GrowthAfterDenseIndexMovesPreservesReferenceValuesAndEveryView(int count)
        {
            IntMap<object> map = new(0);
            Dictionary<int, object> expected = new();
            for (int key = 0; key < count; ++key)
            {
                object value = new object();
                Assert.IsTrue(map.TrySet(key, value), $"Initial insertion failed for key {key}.");
                expected.Add(key, value);
            }
            for (int key = 1; key < count; key += 3)
            {
                Assert.IsTrue(map.Remove(key, out object removed));
                Assert.AreSame(expected[key], removed);
                expected.Remove(key);
            }
            int capacity = map.Capacity;
            int next = count;
            while (map.Capacity == capacity)
            {
                object value = new object();
                Assert.IsTrue(map.TrySet(next, value), $"Growth insertion failed for key {next}.");
                expected.Add(next, value);
                ++next;
            }
            Assert.That(map.Count, Is.EqualTo(expected.Count));
            HashSet<int> keys = new();
            HashSet<object> values = new();
            foreach (KeyValuePair<int, object> pair in map)
            {
                Assert.IsTrue(expected.TryGetValue(pair.Key, out object value));
                Assert.AreSame(value, pair.Value);
                Assert.IsTrue(keys.Add(pair.Key));
                Assert.IsTrue(values.Add(pair.Value));
            }
            Assert.That(keys.Count, Is.EqualTo(expected.Count));
            foreach (int key in map.Keys)
            {
                Assert.IsTrue(keys.Remove(key));
                Assert.IsTrue(map.TryGet(key, out object value));
                Assert.AreSame(expected[key], value);
            }
            foreach (object value in map.Values)
            {
                Assert.IsTrue(values.Remove(value));
            }
            Assert.That(keys, Is.Empty);
            Assert.That(values, Is.Empty);
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
        public void ReplacingAtLoadLimitPreservesCapacityAndAllValues(
            int capacityHint,
            int stride,
            bool useIndexer
        )
        {
            IntMap<int> map = new(capacityHint);
            int capacity = map.Capacity;
            int count = capacity / 4;
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
        public void ReinsertingRemovedEntryAtLoadLimitDoesNotAllocate(
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
            int count = capacity / 4;
            for (int key = 0; key < count; ++key)
            {
                Assert.IsTrue(map.TrySet(key, "original"));
            }
            Assert.IsTrue(map.Remove(0, out string removed));
            Assert.AreEqual("original", removed);

            long before = ThreadAllocationCounter.Read();
            bool written = true;
            if (useIndexer)
            {
                map[0] = null;
            }
            else
            {
                written = map.TrySet(0, null);
            }
            long allocated = ThreadAllocationCounter.Read() - before;
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
        public void SlidingLoadLimitWindowReclaimsDeletedSlotsBeforeGrowing(
            int capacityHint,
            int stride
        )
        {
            IntMap<int> map = new(capacityHint);
            int capacity = map.Capacity;
            int count = capacity / 4;
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
        public void SlidingLoadLimitWindowDoesNotAllocate(int capacityHint, int stride)
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
            int count = capacity / 4;
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
            for (int index = 0; index < capacity / 4; ++index)
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
            int loadLimit = capacity / 4;
            for (int key = 0; key < loadLimit; ++key)
            {
                map.TrySet(key * 1024, "original");
            }
            for (int key = 1; key < loadLimit; key += 2)
            {
                Assert.IsTrue(map.Remove(key * 1024, out string removed));
                Assert.AreEqual("original", removed);
            }

            IntMap<string>.Enumerator enumerator = map.GetEnumerator();
            Assert.IsTrue(enumerator.MoveNext());
            for (int key = 0; key < loadLimit; key += 2)
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
            Assert.AreEqual(loadLimit / 2, map.Count);
            for (int key = 0; key < loadLimit; ++key)
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
                long beforeControl = ThreadAllocationCounter.Read();
                byte[] control = new byte[controlSize];
                long controlBytes = ThreadAllocationCounter.Read() - beforeControl;
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
            int count = map.Capacity / 4;
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

            long before = ThreadAllocationCounter.Read();
            bool written = true;
            if (useIndexer)
            {
                map[0] = null;
            }
            else
            {
                written = map.TrySet(0, null);
            }
            long allocated = ThreadAllocationCounter.Read() - before;
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
            int loadLimit = startingCapacity / 4;
            for (int key = 0; key < loadLimit; ++key)
            {
                map.TrySet(key, key);
            }
            for (int key = 8; key < loadLimit; ++key)
            {
                Assert.IsTrue(map.Remove(key, out _));
            }

            Assert.IsTrue(map.TrySet(loadLimit, loadLimit));
            Assert.AreEqual(startingCapacity, map.Capacity);
            Assert.AreEqual(9, map.Count);
            for (int key = 0; key < 8; ++key)
            {
                Assert.IsTrue(map.TryGet(key, out int value));
                Assert.AreEqual(key, value);
            }
            for (int key = 8; key < loadLimit; ++key)
            {
                Assert.IsFalse(map.TryGet(key, out _));
                Assert.IsTrue(map.TrySet(key, key));
            }

            Assert.AreEqual(loadLimit + 1, map.Count);
            Assert.AreEqual(startingCapacity * 2, map.Capacity);
            for (int key = 0; key <= loadLimit; ++key)
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

        [TestCase(int.MinValue, 0)]
        [TestCase(int.MinValue + 1, 0)]
        [TestCase(int.MinValue, 64)]
        [TestCase(int.MinValue + 1, 64)]
        public void ReservedLookupsInPopulatedTablesPreserveValuesAndEnumerators(
            int reserved,
            int hint
        )
        {
            IntMap<string> map = new(hint);
            for (int index = 0; index < 64; ++index)
            {
                Assert.IsTrue(map.TrySet(index * 1024, "value"));
            }
            int capacity = map.Capacity;
            IntMap<string>.Enumerator entries = map.GetEnumerator();
            Assert.IsTrue(entries.MoveNext());
            Assert.IsFalse(map.TryGet(reserved, out string missing));
            Assert.IsTrue(missing == null);
            Assert.IsFalse(map.ContainsKey(reserved));
            Assert.IsFalse(map.TrySet(reserved, "invalid"));
            Assert.IsFalse(map.Remove(reserved, out string removed));
            Assert.IsTrue(removed == null);
            int observed = 1;
            while (entries.MoveNext())
            {
                Assert.AreEqual("value", entries.Current.Value);
                ++observed;
            }
            Assert.AreEqual(64, observed);
            Assert.AreEqual(64, map.Count);
            Assert.AreEqual(capacity, map.Capacity);
            for (int index = 0; index < 64; ++index)
            {
                Assert.IsTrue(map.TryGet(index * 1024, out string value));
                Assert.AreEqual("value", value);
            }
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

        [TestCase(64, 0, false)]
        [TestCase(64, 0, true)]
        [TestCase(64, 1, false)]
        [TestCase(64, 1, true)]
        [TestCase(64, 7, false)]
        [TestCase(64, 7, true)]
        [TestCase(64, 8, false)]
        [TestCase(64, 8, true)]
        [TestCase(64, 64, false)]
        [TestCase(64, 64, true)]
        [TestCase(1024, 1, false)]
        [TestCase(1024, 1, true)]
        [TestCase(16384, 1, false)]
        [TestCase(16384, 1, true)]
        [TestCase(16384, 1024, false)]
        [TestCase(16384, 1024, true)]
        [TestCase(16384, 4096, false)]
        [TestCase(16384, 4096, true)]
        public void ClearKeepsCapacityAndAllowsRefillAcrossSparseAndDenseStates(
            int hint,
            int count,
            bool includeZero
        )
        {
            IntMap<object> map = new(hint);
            Dictionary<int, object> expected = new();
            for (int index = 0; index < count; ++index)
            {
                int key = index % 2 == 0 ? index + 1 : -index - 1;
                object value = new();
                Assert.IsTrue(map.TrySet(key, value));
                expected.Add(key, value);
            }
            if (includeZero)
            {
                object value = new();
                Assert.IsTrue(map.TrySet(0, value));
                expected.Add(0, value);
            }
            int capacity = map.Capacity;
            IntMap<object>.KeyView keys = map.Keys;
            IntMap<object>.ValueView values = map.Values;
            IntMap<object>.Enumerator iterator = map.GetEnumerator();
            map.Clear();
            Assert.AreEqual(capacity, map.Capacity);
            Assert.IsTrue(map.IsEmpty);
            Assert.IsEmpty(keys);
            Assert.IsEmpty(values);
            Assert.Throws<InvalidOperationException>(() => iterator.MoveNext());
            foreach (KeyValuePair<int, object> pair in expected)
            {
                Assert.IsFalse(map.TryGet(pair.Key, out object removed));
                Assert.IsTrue(removed == null);
                Assert.IsTrue(map.TrySet(pair.Key, pair.Value));
            }
            Assert.AreEqual(expected.Count, map.Count);
            foreach (KeyValuePair<int, object> pair in map)
            {
                Assert.AreSame(expected[pair.Key], pair.Value);
            }
            foreach (KeyValuePair<int, object> pair in expected)
            {
                Assert.IsTrue(map.Remove(pair.Key, out object removed));
                Assert.AreSame(pair.Value, removed);
            }
            Assert.IsTrue(map.IsEmpty);
            Assert.AreEqual(capacity, map.Capacity);
            Assert.IsTrue(map.TrySet(17, null));
            Assert.IsTrue(map.TryGet(17, out object empty));
            Assert.IsTrue(empty == null);
        }

#if !UNITY_5_3_OR_NEWER
        [TestCase(64, 1)]
        [TestCase(64, 7)]
        [TestCase(16384, 1)]
        [TestCase(16384, 1024)]
        public void ClearReleasesStoredReferencesOnAnObservableManagedCollector(int hint, int count)
        {
            WeakReference control = CreateUnrootedReference();
            GC.Collect();
            if (control.IsAlive)
            {
                Assert.Ignore("The collector retains the unrooted reference control.");
            }
            (IntMap<object> map, WeakReference[] references) = CreateReferenceMap(hint, count);
            map.Clear();
            GC.Collect();
            foreach (WeakReference reference in references)
            {
                Assert.IsFalse(reference.IsAlive, "Clearing the map must release its values.");
            }
            GC.KeepAlive(map);
        }
#endif

        private sealed class GuidHolder
        {
            public Guid Payload;
        }
    }
}
