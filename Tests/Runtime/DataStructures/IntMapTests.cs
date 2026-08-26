// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop-studios.unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

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

        [TestCase(-1)]
        public void ConstructorRefusesNegativeCapacityHints(int capacityHint)
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
            for (int index = 0; index < 70_000; ++index)
            {
                Assert.IsTrue(map.TrySet(index * 2, index));
                if (map.Capacity != lastCapacity)
                {
                    Assert.AreEqual(lastCapacity * 2, map.Capacity);
                    lastCapacity = map.Capacity;

                    // Every survivor stays reachable right after a resize.
                    for (int check = 0; check <= index; ++check)
                    {
                        Assert.IsTrue(map.TryGet(check * 2, out int value));
                        Assert.AreEqual(check, value);
                    }
                }
            }

            Assert.AreEqual(70_000, map.Count);
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

        private sealed class GuidHolder
        {
            public Guid Payload;
        }
    }
}
