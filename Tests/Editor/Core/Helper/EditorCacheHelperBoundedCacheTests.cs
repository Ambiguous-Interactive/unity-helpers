// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*
    WUH010 is suppressed for this file: its subject is EditorCacheHelper's bounded cache, whose indexer is part of the type under test.
    Rewriting those reads through TryGetValue would delete what they assert. Everywhere the
    indexer is incidental, tests read through DictionaryAssertions.ValueFor instead (#653).
*/
#pragma warning disable WUH010

namespace WallstopStudios.UnityHelpers.Tests.Editor.Core.Helper
{
#if UNITY_EDITOR

    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class EditorCacheHelperBoundedCacheTests
    {
        [Test]
        [TestCaseSource(nameof(NormalAddEntryCases))]
        public void AddToBoundedCacheHandlesNormalCases(
            int initialCount,
            string keyToAdd,
            int valueToAdd,
            int maxSize,
            int expectedCountAfter
        )
        {
            Dictionary<string, int> cache = new();
            for (int i = 0; i < initialCount; i++)
            {
                cache[$"existing{i}"] = i;
            }

            EditorCacheHelper.AddToBoundedCache(cache, keyToAdd, valueToAdd, maxSize);

            Assert.That(
                cache.Count,
                Is.EqualTo(expectedCountAfter),
                $"Cache should have {expectedCountAfter} entries after adding '{keyToAdd}' to cache with {initialCount} existing entries and maxSize {maxSize}"
            );
            Assert.That(
                cache.ContainsKey(keyToAdd),
                Is.True,
                $"Cache should contain the added key '{keyToAdd}'"
            );
            Assert.That(
                cache[keyToAdd],
                Is.EqualTo(valueToAdd),
                $"Value for key '{keyToAdd}' should be {valueToAdd}"
            );
        }

        private static IEnumerable<TestCaseData> NormalAddEntryCases()
        {
            yield return new TestCaseData(0, "newKey", 42, 10, 1).SetName(
                "Normal.AddEntryToEmptyCache.Succeeds"
            );

            yield return new TestCaseData(3, "newKey", 99, 10, 4).SetName(
                "Normal.AddEntryToPartialCache.CountIncreases"
            );

            yield return new TestCaseData(5, "newKey", 100, 10, 6).SetName(
                "Normal.AddMultipleEntries.AllPresent"
            );

            yield return new TestCaseData(1, "secondKey", 200, 100, 2).SetName(
                "Normal.LargeMaxSize.AcceptsEntries"
            );
        }

        [Test]
        public void AddToBoundedCacheUpdateExistingKeyUpdatesValue()
        {
            Dictionary<string, int> cache = new() { { "existingKey", 10 } };

            EditorCacheHelper.AddToBoundedCache(cache, "existingKey", 999, 5);

            Assert.That(
                cache.Count,
                Is.EqualTo(1),
                "Updating existing key should not change cache count"
            );
            Assert.That(
                cache["existingKey"],
                Is.EqualTo(999),
                "Existing key value should be updated to 999"
            );
        }

        [Test]
        public void AddToBoundedCacheUpdateExistingKeyDoesNotEvict()
        {
            Dictionary<string, int> cache = new()
            {
                { "key1", 1 },
                { "key2", 2 },
                { "key3", 3 },
            };

            EditorCacheHelper.AddToBoundedCache(cache, "key2", 222, 3);

            Assert.That(
                cache.Count,
                Is.EqualTo(3),
                "Updating existing key at capacity should not change count"
            );
            Assert.That(cache.ContainsKey("key1"), Is.True, "key1 should still be present");
            Assert.That(cache.ContainsKey("key2"), Is.True, "key2 should still be present");
            Assert.That(cache.ContainsKey("key3"), Is.True, "key3 should still be present");
            Assert.That(cache["key2"], Is.EqualTo(222), "key2 value should be updated to 222");
        }

        [Test]
        [TestCaseSource(nameof(EdgeCaseCases))]
        public void AddToBoundedCacheHandlesEdgeCases(
            int initialCount,
            int maxSize,
            bool expectEviction,
            int expectedFinalCount
        )
        {
            Dictionary<string, int> cache = new();
            for (int i = 0; i < initialCount; i++)
            {
                // Use AddToBoundedCache to ensure entries are tracked in the LRU tracker
                EditorCacheHelper.AddToBoundedCache(cache, $"key{i}", i, maxSize);
            }
            string firstKey = 0 < initialCount ? "key0" : null;

            EditorCacheHelper.AddToBoundedCache(cache, "newEntry", 42, maxSize);

            Assert.That(
                cache.Count,
                Is.EqualTo(expectedFinalCount),
                $"Cache should have {expectedFinalCount} entries after adding to cache with {initialCount} entries and maxSize {maxSize}"
            );
            Assert.That(cache.ContainsKey("newEntry"), Is.True, "newEntry should be in cache");

            if (expectEviction && firstKey != null)
            {
                Assert.That(
                    cache.ContainsKey(firstKey),
                    Is.False,
                    $"First key '{firstKey}' should have been evicted"
                );
            }
        }

        private static IEnumerable<TestCaseData> EdgeCaseCases()
        {
            yield return new TestCaseData(0, 1, false, 1).SetName(
                "Edge.EmptyCache.SingleEntryCapacity"
            );

            yield return new TestCaseData(1, 1, true, 1).SetName(
                "Edge.SingleEntryCapacity.EvictsOnSecondAdd"
            );

            yield return new TestCaseData(4, 5, false, 5).SetName(
                "Edge.ExactlyUnderCapacity.NoEviction"
            );

            yield return new TestCaseData(5, 5, true, 5).SetName(
                "Edge.ExactlyAtCapacity.EvictsOldest"
            );

            yield return new TestCaseData(6, 5, true, 5).SetName("Edge.OverCapacity.EvictsOldest");

            yield return new TestCaseData(10, 10, true, 10).SetName(
                "Edge.AtCapacityBoundary.EvictsOldest"
            );
        }

        [Test]
        [TestCaseSource(nameof(NegativeCases))]
        public void AddToBoundedCacheHandlesNegativeCases(
            Dictionary<string, int> cache,
            string key,
            int value,
            int maxSize,
            int expectedCount,
            bool expectKeyInCache
        )
        {
            EditorCacheHelper.AddToBoundedCache(cache, key, value, maxSize);

            if (cache != null)
            {
                Assert.That(
                    cache.Count,
                    Is.EqualTo(expectedCount),
                    $"Cache count should be {expectedCount} after operation with maxSize {maxSize}"
                );

                if (expectKeyInCache && key != null)
                {
                    Assert.That(
                        cache.ContainsKey(key),
                        Is.True,
                        $"Key '{key}' should be in cache when expectKeyInCache is true"
                    );
                }
                else if (key != null)
                {
                    Assert.That(
                        cache.ContainsKey(key),
                        Is.False,
                        $"Key '{key}' should NOT be in cache when expectKeyInCache is false"
                    );
                }
            }
        }

        private static IEnumerable<TestCaseData> NegativeCases()
        {
            yield return new TestCaseData(null, "key", 1, 10, 0, false).SetName(
                "Negative.NullCache.DoesNotThrow"
            );

            yield return new TestCaseData(
                new Dictionary<string, int>(),
                "key",
                1,
                0,
                0,
                false
            ).SetName("Negative.ZeroMaxSize.RejectsEntry");

            yield return new TestCaseData(
                new Dictionary<string, int>(),
                "key",
                1,
                -1,
                0,
                false
            ).SetName("Negative.NegativeMaxSize.RejectsEntry");

            yield return new TestCaseData(
                new Dictionary<string, int> { { "existing", 1 } },
                "key",
                2,
                -5,
                1,
                false
            ).SetName("Negative.NegativeMaxSizeWithExisting.PreservesExisting");
        }

        [Test]
        public void AddToBoundedCacheWithNullKeyRejectsNullKeys()
        {
            Dictionary<string, int> cache = new();

            EditorCacheHelper.AddToBoundedCache(cache, null, 42, 10);

            // Dictionary.ContainsKey(null) throws for a reference-type key, so only the count can be read.
            Assert.That(cache.Count, Is.EqualTo(0), "Null key should not be added to cache");
        }

        [Test]
        public void AddToBoundedCacheWithNullKeyDoesNotEvictExisting()
        {
            Dictionary<string, int> cache = new() { { "existing", 100 } };

            EditorCacheHelper.AddToBoundedCache(cache, null, 42, 1);

            Assert.That(
                cache.Count,
                Is.EqualTo(1),
                "Adding null key should not change existing cache"
            );
            Assert.That(
                cache.ContainsKey("existing"),
                Is.True,
                "Existing entry should remain after null key add attempt"
            );
            Assert.That(
                cache["existing"],
                Is.EqualTo(100),
                "Existing entry value should be unchanged"
            );
        }

        [Test]
        [TestCaseSource(nameof(ExtremeCases))]
        public void AddToBoundedCacheHandlesExtremeCases(int maxSize, int entriesToAdd)
        {
            Dictionary<int, int> cache = new();

            for (int i = 0; i < entriesToAdd; i++)
            {
                EditorCacheHelper.AddToBoundedCache(cache, i, i * 2, maxSize);
            }

            Assert.That(
                cache.Count,
                Is.LessThanOrEqualTo(maxSize),
                $"Cache count {cache.Count} should not exceed maxSize {maxSize}"
            );

            if (maxSize < entriesToAdd)
            {
                for (int i = entriesToAdd - maxSize; i < entriesToAdd; i++)
                {
                    Assert.That(cache.ContainsKey(i), Is.True, $"Expected key {i} to be present");
                }
                for (int i = 0; i < entriesToAdd - maxSize; i++)
                {
                    Assert.That(cache.ContainsKey(i), Is.False, $"Expected key {i} to be evicted");
                }
            }
        }

        private static IEnumerable<TestCaseData> ExtremeCases()
        {
            yield return new TestCaseData(100, 200).SetName("Extreme.MediumMaxSizeDoubleEntries");

            yield return new TestCaseData(1000, 2000).SetName("Extreme.LargeMaxSizeDoubleEntries");

            yield return new TestCaseData(10000, 10500).SetName(
                "Extreme.VeryLargeMaxSizeSlightlyOver"
            );

            yield return new TestCaseData(1, 1000).SetName("Extreme.SingleCapacityManyEvictions");

            yield return new TestCaseData(5, 10000).SetName(
                "Extreme.SmallCapacityMassiveEvictions"
            );
        }

        [Test]
        public void AddToBoundedCacheLRUEvictsLeastRecentlyUsedNotLastAdded()
        {
            Dictionary<string, int> cache = new();

            EditorCacheHelper.AddToBoundedCache(cache, "first", 1, 3);
            EditorCacheHelper.AddToBoundedCache(cache, "second", 2, 3);
            EditorCacheHelper.AddToBoundedCache(cache, "third", 3, 3);

            Assert.That(cache.Count, Is.EqualTo(3), "Cache should have exactly 3 entries");
            Assert.That(cache.ContainsKey("first"), Is.True, "first should be present");
            Assert.That(cache.ContainsKey("second"), Is.True, "second should be present");
            Assert.That(cache.ContainsKey("third"), Is.True, "third should be present");

            EditorCacheHelper.AddToBoundedCache(cache, "fourth", 4, 3);

            Assert.That(cache.Count, Is.EqualTo(3), "Cache should still have exactly 3 entries");
            Assert.That(cache.ContainsKey("first"), Is.False, "First entry should be evicted");
            Assert.That(
                cache.ContainsKey("second"),
                Is.True,
                "Second entry should still be present"
            );
            Assert.That(cache.ContainsKey("third"), Is.True, "Third entry should still be present");
            Assert.That(
                cache.ContainsKey("fourth"),
                Is.True,
                "Fourth (newest) entry should be present"
            );
        }

        [Test]
        public void AddToBoundedCacheLRUOrderPreservedAfterMultipleEvictions()
        {
            Dictionary<string, int> cache = new();
            int maxSize = 3;

            EditorCacheHelper.AddToBoundedCache(cache, "a", 1, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "b", 2, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "c", 3, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "d", 4, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "e", 5, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "f", 6, maxSize);

            Assert.That(
                cache.Count,
                Is.EqualTo(3),
                "Cache should be at capacity after multiple adds"
            );
            Assert.That(cache.ContainsKey("a"), Is.False, "a should have been evicted");
            Assert.That(cache.ContainsKey("b"), Is.False, "b should have been evicted");
            Assert.That(cache.ContainsKey("c"), Is.False, "c should have been evicted");
            Assert.That(cache.ContainsKey("d"), Is.True, "d should still be present");
            Assert.That(cache.ContainsKey("e"), Is.True, "e should still be present");
            Assert.That(cache.ContainsKey("f"), Is.True, "f should still be present");
        }

        [Test]
        public void AddToBoundedCacheLRUUpdatingExistingKeyResetsOrderToMostRecent()
        {
            Dictionary<string, int> cache = new();
            int maxSize = 3;

            EditorCacheHelper.AddToBoundedCache(cache, "a", 1, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "b", 2, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "c", 3, maxSize);

            EditorCacheHelper.AddToBoundedCache(cache, "a", 100, maxSize);

            EditorCacheHelper.AddToBoundedCache(cache, "d", 4, maxSize);

            Assert.That(cache.Count, Is.EqualTo(3), "Cache should be at capacity");
            Assert.That(
                cache.ContainsKey("a"),
                Is.True,
                "a should NOT be evicted (update moved it to most recently used)"
            );
            Assert.That(cache["a"], Is.EqualTo(100), "a should have updated value");
            Assert.That(
                cache.ContainsKey("b"),
                Is.False,
                "b should be evicted (least recently used)"
            );
            Assert.That(cache.ContainsKey("c"), Is.True, "c should still be present");
            Assert.That(cache.ContainsKey("d"), Is.True, "d should still be present");
        }

        [Test]
        public void TryGetFromBoundedLRUCacheUpdatesAccessOrder()
        {
            Dictionary<string, int> cache = new();
            int maxSize = 3;

            EditorCacheHelper.AddToBoundedCache(cache, "a", 1, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "b", 2, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "c", 3, maxSize);

            Assert.That(
                EditorCacheHelper.TryGetFromBoundedLRUCache(cache, "a", out int aValue),
                Is.True,
                "Should find key 'a' in cache"
            );
            Assert.That(aValue, Is.EqualTo(1), "Value for 'a' should be 1");

            EditorCacheHelper.AddToBoundedCache(cache, "d", 4, maxSize);

            Assert.That(cache.Count, Is.EqualTo(3), "Cache should be at capacity");
            Assert.That(
                cache.ContainsKey("a"),
                Is.True,
                "a should NOT be evicted (read moved it to most recently used)"
            );
            Assert.That(
                cache.ContainsKey("b"),
                Is.False,
                "b should be evicted (least recently used)"
            );
            Assert.That(cache.ContainsKey("c"), Is.True, "c should still be present");
            Assert.That(cache.ContainsKey("d"), Is.True, "d should still be present");
        }

        [Test]
        public void TryGetFromBoundedLRUCacheReturnsFalseForMissingKey()
        {
            Dictionary<string, int> cache = new() { { "existing", 42 } };

            bool found = EditorCacheHelper.TryGetFromBoundedLRUCache(
                cache,
                "missing",
                out int value
            );

            Assert.That(found, Is.False, "Should return false for missing key");
            Assert.That(
                value,
                Is.EqualTo(default(int)),
                "Value should be default when key not found"
            );
        }

        [Test]
        public void TryGetFromBoundedLRUCacheHandlesNullCache()
        {
            bool found = EditorCacheHelper.TryGetFromBoundedLRUCache(
                (Dictionary<string, int>)null,
                "key",
                out int value
            );

            Assert.That(found, Is.False, "Should return false for null cache");
            Assert.That(value, Is.EqualTo(default(int)), "Value should be default for null cache");
        }

        [Test]
        public void TryGetFromBoundedLRUCacheHandlesNullKey()
        {
            Dictionary<string, int> cache = new() { { "existing", 42 } };

            bool found = EditorCacheHelper.TryGetFromBoundedLRUCache(cache, null, out int value);

            Assert.That(found, Is.False, "Should return false for null key");
            Assert.That(value, Is.EqualTo(default(int)), "Value should be default for null key");
        }

        [Test]
        public void TryGetFromBoundedLRUCacheMultipleReadsKeepItemAlive()
        {
            Dictionary<string, int> cache = new();
            int maxSize = 3;

            EditorCacheHelper.AddToBoundedCache(cache, "a", 1, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "b", 2, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "c", 3, maxSize);

            EditorCacheHelper.TryGetFromBoundedLRUCache(cache, "a", out _);

            EditorCacheHelper.AddToBoundedCache(cache, "d", 4, maxSize);

            EditorCacheHelper.TryGetFromBoundedLRUCache(cache, "a", out _);

            EditorCacheHelper.AddToBoundedCache(cache, "e", 5, maxSize);

            Assert.That(cache.Count, Is.EqualTo(3), "Cache should be at capacity");
            Assert.That(
                cache.ContainsKey("a"),
                Is.True,
                "a should still be present after multiple reads"
            );
            Assert.That(cache.ContainsKey("b"), Is.False, "b should have been evicted");
            Assert.That(cache.ContainsKey("c"), Is.False, "c should have been evicted");
            Assert.That(cache.ContainsKey("d"), Is.True, "d should still be present");
            Assert.That(cache.ContainsKey("e"), Is.True, "e should still be present");
        }

        [Test]
        [TestCaseSource(nameof(ValueTypeCases))]
        public void AddToBoundedCacheWorksWithDifferentValueTypes<TValue>(
            TValue initialValue,
            TValue newValue,
            TValue expectedValue
        )
        {
            Dictionary<string, TValue> cache = new();

            EditorCacheHelper.AddToBoundedCache(cache, "key", initialValue, 10);
            EditorCacheHelper.AddToBoundedCache(cache, "key", newValue, 10);

            Assert.That(
                cache["key"],
                Is.EqualTo(expectedValue),
                $"Value should be updated to {expectedValue}"
            );
        }

        private static IEnumerable<TestCaseData> ValueTypeCases()
        {
            yield return new TestCaseData(1, 2, 2).SetName("ValueType.Int.UpdatesCorrectly");

            yield return new TestCaseData("first", "second", "second").SetName(
                "ValueType.String.UpdatesCorrectly"
            );

            yield return new TestCaseData(1.5f, 2.5f, 2.5f).SetName(
                "ValueType.Float.UpdatesCorrectly"
            );

            yield return new TestCaseData(true, false, false).SetName(
                "ValueType.Bool.UpdatesCorrectly"
            );

            yield return new TestCaseData(
                new UnityEngine.Vector2(1, 2),
                new UnityEngine.Vector2(3, 4),
                new UnityEngine.Vector2(3, 4)
            ).SetName("ValueType.Vector2.UpdatesCorrectly");
        }

        [Test]
        public void AddToBoundedCacheWithIntKeysEvictsCorrectly()
        {
            Dictionary<int, string> cache = new();
            int maxSize = 5;

            for (int i = 0; i < 10; i++)
            {
                EditorCacheHelper.AddToBoundedCache(cache, i, $"value{i}", maxSize);
            }

            Assert.That(cache.Count, Is.EqualTo(5), "Cache should be at capacity of 5");
            for (int i = 0; i < 5; i++)
            {
                Assert.That(cache.ContainsKey(i), Is.False, $"Key {i} should have been evicted");
            }
            for (int i = 5; i < 10; i++)
            {
                Assert.That(cache.ContainsKey(i), Is.True, $"Key {i} should still be present");
                Assert.That(
                    cache[i],
                    Is.EqualTo($"value{i}"),
                    $"Value for key {i} should be 'value{i}'"
                );
            }
        }

        [Test]
        public void AddToBoundedCacheMassiveEvictionsStillLRU()
        {
            Dictionary<int, int> cache = new();
            int maxSize = 10;
            int totalEntries = 1000;

            for (int i = 0; i < totalEntries; i++)
            {
                EditorCacheHelper.AddToBoundedCache(cache, i, i, maxSize);
            }

            Assert.That(
                cache.Count,
                Is.EqualTo(maxSize),
                $"Cache should be at capacity of {maxSize}"
            );

            for (int i = totalEntries - maxSize; i < totalEntries; i++)
            {
                Assert.That(cache.ContainsKey(i), Is.True, $"Key {i} should be present");
                Assert.That(cache[i], Is.EqualTo(i), $"Value for key {i} should be {i}");
            }
        }

        [Test]
        public void AddToBoundedCacheDefaultConstantsAreReasonable()
        {
            Assert.That(
                EditorCacheHelper.DefaultUIStateCacheSize,
                Is.GreaterThan(0),
                "DefaultUIStateCacheSize should be positive"
            );
            Assert.That(
                EditorCacheHelper.DefaultReflectionCacheSize,
                Is.GreaterThan(0),
                "DefaultReflectionCacheSize should be positive"
            );
            Assert.That(
                EditorCacheHelper.DefaultEditorCacheSize,
                Is.GreaterThan(0),
                "DefaultEditorCacheSize should be positive"
            );

            Assert.That(
                EditorCacheHelper.DefaultUIStateCacheSize,
                Is.GreaterThanOrEqualTo(EditorCacheHelper.DefaultEditorCacheSize),
                "UI state cache should be at least as large as editor cache"
            );
        }

        [Test]
        public void AddToBoundedCacheWithMaxValueMaxSizeHandlesGracefully()
        {
            Dictionary<string, int> cache = new();

            EditorCacheHelper.AddToBoundedCache(cache, "key", 42, int.MaxValue);

            Assert.That(cache.Count, Is.EqualTo(1), "Cache should have 1 entry");
            Assert.That(cache["key"], Is.EqualTo(42), "Value should be 42");
        }

        [Test]
        public void AddToBoundedCacheEmptyKeyStringIsValidKey()
        {
            Dictionary<string, int> cache = new();

            EditorCacheHelper.AddToBoundedCache(cache, string.Empty, 42, 10);

            Assert.That(cache.ContainsKey(string.Empty), Is.True, "Empty string key should work");
            Assert.That(
                cache[string.Empty],
                Is.EqualTo(42),
                "Value for empty string key should be 42"
            );
        }

        [Test]
        public void AddToBoundedCacheSpecialCharacterKeysWork()
        {
            Dictionary<string, int> cache = new();
            string[] specialKeys = new[]
            {
                "key with spaces",
                "key\twith\ttabs",
                "key\nwith\nnewlines",
                "key::with::colons",
                "",
                "   ",
                "\u0000\u0001\u0002",
            };

            for (int i = 0; i < specialKeys.Length; i++)
            {
                EditorCacheHelper.AddToBoundedCache(cache, specialKeys[i], i, 100);
            }

            Assert.That(
                cache.Count,
                Is.EqualTo(specialKeys.Length),
                $"Cache should contain all {specialKeys.Length} special keys"
            );
            for (int i = 0; i < specialKeys.Length; i++)
            {
                Assert.That(
                    cache[specialKeys[i]],
                    Is.EqualTo(i),
                    $"Value for special key at index {i} should be {i}"
                );
            }
        }

        [Test]
        public void MultipleDictionariesHaveIndependentLRUTracking()
        {
            Dictionary<string, int> cache1 = new();
            Dictionary<string, int> cache2 = new();
            int maxSize = 3;

            EditorCacheHelper.AddToBoundedCache(cache1, "a", 1, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache1, "b", 2, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache1, "c", 3, maxSize);

            EditorCacheHelper.AddToBoundedCache(cache2, "x", 10, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache2, "y", 20, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache2, "z", 30, maxSize);

            EditorCacheHelper.TryGetFromBoundedLRUCache(cache1, "a", out _);

            EditorCacheHelper.AddToBoundedCache(cache1, "d", 4, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache2, "w", 40, maxSize);

            Assert.That(
                cache1.ContainsKey("a"),
                Is.True,
                "cache1: 'a' should be present (was accessed)"
            );
            Assert.That(
                cache1.ContainsKey("b"),
                Is.False,
                "cache1: 'b' should be evicted (LRU in cache1)"
            );
            Assert.That(cache1.ContainsKey("c"), Is.True, "cache1: 'c' should be present");
            Assert.That(cache1.ContainsKey("d"), Is.True, "cache1: 'd' should be present");

            Assert.That(
                cache2.ContainsKey("x"),
                Is.False,
                "cache2: 'x' should be evicted (LRU in cache2)"
            );
            Assert.That(cache2.ContainsKey("y"), Is.True, "cache2: 'y' should be present");
            Assert.That(cache2.ContainsKey("z"), Is.True, "cache2: 'z' should be present");
            Assert.That(cache2.ContainsKey("w"), Is.True, "cache2: 'w' should be present");
        }

        [Test]
        public void LRUTrackerHandlesRapidAddRemoveCycles()
        {
            Dictionary<string, int> cache = new();
            int maxSize = 5;

            for (int cycle = 0; cycle < 100; cycle++)
            {
                for (int i = 0; i < 10; i++)
                {
                    EditorCacheHelper.AddToBoundedCache(cache, $"key{i}", cycle * 10 + i, maxSize);
                }
            }

            Assert.That(
                cache.Count,
                Is.EqualTo(maxSize),
                "Cache should be at capacity after cycles"
            );

            for (int i = 5; i < 10; i++)
            {
                Assert.That(
                    cache.ContainsKey($"key{i}"),
                    Is.True,
                    $"key{i} should be present (last 5 added in final cycle)"
                );
            }
        }

        [Test]
        public void ConcurrentLikeAccessPatternMaintainsLRU()
        {
            Dictionary<string, int> cache = new();
            int maxSize = 4;

            EditorCacheHelper.AddToBoundedCache(cache, "a", 1, maxSize);
            EditorCacheHelper.TryGetFromBoundedLRUCache(cache, "a", out _);

            EditorCacheHelper.AddToBoundedCache(cache, "b", 2, maxSize);
            EditorCacheHelper.TryGetFromBoundedLRUCache(cache, "a", out _);

            EditorCacheHelper.AddToBoundedCache(cache, "c", 3, maxSize);
            EditorCacheHelper.TryGetFromBoundedLRUCache(cache, "b", out _);

            EditorCacheHelper.AddToBoundedCache(cache, "d", 4, maxSize);

            EditorCacheHelper.AddToBoundedCache(cache, "e", 5, maxSize);

            Assert.That(cache.Count, Is.EqualTo(4), "Cache should be at capacity of 4");
            Assert.That(
                cache.ContainsKey("a"),
                Is.False,
                "'a' should be evicted (least recently accessed before 'e' add)"
            );
            Assert.That(cache.ContainsKey("b"), Is.True, "'b' should be present");
            Assert.That(cache.ContainsKey("c"), Is.True, "'c' should be present");
            Assert.That(cache.ContainsKey("d"), Is.True, "'d' should be present");
            Assert.That(cache.ContainsKey("e"), Is.True, "'e' should be present");
        }

        [Test]
        public void UpdatingSameKeyMultipleTimesMaintainsCorrectLRUOrder()
        {
            Dictionary<string, int> cache = new();
            int maxSize = 3;

            EditorCacheHelper.AddToBoundedCache(cache, "a", 1, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "b", 2, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "c", 3, maxSize);

            EditorCacheHelper.AddToBoundedCache(cache, "a", 10, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "a", 100, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "a", 1000, maxSize);

            EditorCacheHelper.AddToBoundedCache(cache, "d", 4, maxSize);

            Assert.That(cache.Count, Is.EqualTo(3), "Cache should be at capacity");
            Assert.That(
                cache.ContainsKey("a"),
                Is.True,
                "'a' should be present (multiple updates made it MRU)"
            );
            Assert.That(cache["a"], Is.EqualTo(1000), "'a' should have latest value 1000");
            Assert.That(
                cache.ContainsKey("b"),
                Is.False,
                "'b' should be evicted (was LRU after 'a' updates)"
            );
            Assert.That(cache.ContainsKey("c"), Is.True, "'c' should be present");
            Assert.That(cache.ContainsKey("d"), Is.True, "'d' should be present");
        }

        [Test]
        [TestCaseSource(nameof(MaxSizeOneCases))]
        public void BoundaryConditionsMaxSizeOneWithManyOperations(
            int operationCount,
            string expectedKey,
            int expectedValue
        )
        {
            Dictionary<string, int> cache = new();
            int maxSize = 1;

            for (int i = 0; i < operationCount; i++)
            {
                EditorCacheHelper.AddToBoundedCache(cache, $"key{i}", i, maxSize);
            }

            Assert.That(
                cache.Count,
                Is.EqualTo(1),
                "Cache should only have 1 entry with maxSize 1"
            );
            Assert.That(
                cache.ContainsKey(expectedKey),
                Is.True,
                $"Cache should contain '{expectedKey}'"
            );
            Assert.That(
                cache[expectedKey],
                Is.EqualTo(expectedValue),
                $"Value should be {expectedValue}"
            );
        }

        private static IEnumerable<TestCaseData> MaxSizeOneCases()
        {
            yield return new TestCaseData(1, "key0", 0).SetName(
                "MaxSizeOne.SingleOperation.HasLastEntry"
            );

            yield return new TestCaseData(10, "key9", 9).SetName(
                "MaxSizeOne.TenOperations.HasLastEntry"
            );

            yield return new TestCaseData(100, "key99", 99).SetName(
                "MaxSizeOne.HundredOperations.HasLastEntry"
            );

            yield return new TestCaseData(1000, "key999", 999).SetName(
                "MaxSizeOne.ThousandOperations.HasLastEntry"
            );
        }

        [Test]
        public void CacheBehaviorCorrectAfterClearingAndReusing()
        {
            Dictionary<string, int> cache = new();
            int maxSize = 3;

            EditorCacheHelper.AddToBoundedCache(cache, "a", 1, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "b", 2, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "c", 3, maxSize);

            Assert.That(cache.Count, Is.EqualTo(3), "Cache should have 3 entries before clear");

            cache.Clear();

            Assert.That(cache.Count, Is.EqualTo(0), "Cache should be empty after clear");

            EditorCacheHelper.AddToBoundedCache(cache, "x", 10, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "y", 20, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "z", 30, maxSize);

            Assert.That(cache.Count, Is.EqualTo(3), "Cache should have 3 entries after reuse");
            Assert.That(cache.ContainsKey("x"), Is.True, "'x' should be present");
            Assert.That(cache.ContainsKey("y"), Is.True, "'y' should be present");
            Assert.That(cache.ContainsKey("z"), Is.True, "'z' should be present");

            EditorCacheHelper.AddToBoundedCache(cache, "w", 40, maxSize);

            Assert.That(cache.Count, Is.EqualTo(3), "Cache should be at capacity");
            Assert.That(
                cache.ContainsKey("x"),
                Is.False,
                "'x' should be evicted (LRU after clear and reuse)"
            );
            Assert.That(cache.ContainsKey("y"), Is.True, "'y' should be present");
            Assert.That(cache.ContainsKey("z"), Is.True, "'z' should be present");
            Assert.That(cache.ContainsKey("w"), Is.True, "'w' should be present");
        }

        [Test]
        public void LRUOrderTrackerMarkAccessedAddsNewKeyWhenNotPresent()
        {
            LRUOrderTracker<string> tracker = new();

            tracker.MarkAccessed("newKey");

            Assert.That(
                tracker.TryGetLeastRecentlyUsed(out string lruKey),
                Is.True,
                "Should have at least one tracked key"
            );
            Assert.That(lruKey, Is.EqualTo("newKey"), "newKey should be LRU (only key)");
        }

        [Test]
        public void LRUOrderTrackerMarkAccessedMovesExistingKeyToEnd()
        {
            LRUOrderTracker<string> tracker = new();

            tracker.MarkAccessed("a");
            tracker.MarkAccessed("b");
            tracker.MarkAccessed("c");

            Assert.That(
                tracker.TryGetLeastRecentlyUsed(out string lruBefore),
                Is.True,
                "Should have tracked keys"
            );
            Assert.That(lruBefore, Is.EqualTo("a"), "'a' should be LRU before access");

            tracker.MarkAccessed("a");

            Assert.That(
                tracker.TryGetLeastRecentlyUsed(out string lruAfter),
                Is.True,
                "Should still have tracked keys"
            );
            Assert.That(lruAfter, Is.EqualTo("b"), "'b' should be LRU after 'a' was moved to end");
        }

        [Test]
        public void LRUOrderTrackerRemoveRemovesKey()
        {
            LRUOrderTracker<string> tracker = new();

            tracker.MarkAccessed("a");
            tracker.MarkAccessed("b");
            tracker.MarkAccessed("c");

            tracker.Remove("a");

            Assert.That(
                tracker.TryGetLeastRecentlyUsed(out string lru),
                Is.True,
                "Should still have tracked keys after removal"
            );
            Assert.That(lru, Is.EqualTo("b"), "'b' should be LRU after 'a' was removed");
        }

        [Test]
        public void LRUOrderTrackerRemoveHandlesNonExistentKey()
        {
            LRUOrderTracker<string> tracker = new();

            tracker.MarkAccessed("a");

            tracker.Remove("nonexistent");

            Assert.That(
                tracker.TryGetLeastRecentlyUsed(out string lru),
                Is.True,
                "Should still have tracked 'a'"
            );
            Assert.That(lru, Is.EqualTo("a"), "'a' should still be tracked");
        }

        [Test]
        public void LRUOrderTrackerTryGetLeastRecentlyUsedReturnsFalseWhenEmpty()
        {
            LRUOrderTracker<string> tracker = new();

            bool result = tracker.TryGetLeastRecentlyUsed(out string lru);

            Assert.That(result, Is.False, "Should return false when empty");
            Assert.That(lru, Is.EqualTo(default(string)), "LRU key should be default when empty");
        }

        [Test]
        public void LRUOrderTrackerTryGetLeastRecentlyUsedReturnsFirstAddedKey()
        {
            LRUOrderTracker<int> tracker = new();

            tracker.MarkAccessed(100);
            tracker.MarkAccessed(200);
            tracker.MarkAccessed(300);

            Assert.That(
                tracker.TryGetLeastRecentlyUsed(out int lru),
                Is.True,
                "Should return true when keys exist"
            );
            Assert.That(lru, Is.EqualTo(100), "First added key should be LRU");
        }

        [Test]
        public void LRUOrderTrackerWorksWithValueTypeKeys()
        {
            LRUOrderTracker<int> tracker = new();

            for (int i = 0; i < 10; i++)
            {
                tracker.MarkAccessed(i);
            }

            Assert.That(
                tracker.TryGetLeastRecentlyUsed(out int lru),
                Is.True,
                "Should have tracked keys"
            );
            Assert.That(lru, Is.EqualTo(0), "First added (0) should be LRU");

            tracker.MarkAccessed(0);

            Assert.That(
                tracker.TryGetLeastRecentlyUsed(out int lruAfter),
                Is.True,
                "Should still have tracked keys"
            );
            Assert.That(lruAfter, Is.EqualTo(1), "1 should be LRU after 0 was accessed");
        }

        [Test]
        public void LRUOrderTrackerHandlesRemoveAllKeys()
        {
            LRUOrderTracker<string> tracker = new();

            tracker.MarkAccessed("a");
            tracker.MarkAccessed("b");
            tracker.MarkAccessed("c");

            tracker.Remove("a");
            tracker.Remove("b");
            tracker.Remove("c");

            Assert.That(
                tracker.TryGetLeastRecentlyUsed(out string lru),
                Is.False,
                "Should return false when all keys removed"
            );
            Assert.That(lru, Is.EqualTo(default(string)), "LRU should be default when empty");
        }

        [Test]
        public void LRUOrderTrackerHandlesReAddAfterRemove()
        {
            LRUOrderTracker<string> tracker = new();

            tracker.MarkAccessed("a");
            tracker.MarkAccessed("b");

            tracker.Remove("a");

            tracker.MarkAccessed("a");

            Assert.That(
                tracker.TryGetLeastRecentlyUsed(out string lru),
                Is.True,
                "Should have tracked keys"
            );
            Assert.That(
                lru,
                Is.EqualTo("b"),
                "'b' should be LRU (was not removed, 'a' re-added at end)"
            );
        }

        [Test]
        [TestCaseSource(nameof(TryGetNullCases))]
        public void TryGetFromBoundedLRUCacheHandlesNullCases(
            Dictionary<string, int> cache,
            string key,
            bool expectedFound
        )
        {
            bool found = EditorCacheHelper.TryGetFromBoundedLRUCache(cache, key, out int value);

            Assert.That(
                found,
                Is.EqualTo(expectedFound),
                $"Expected found={expectedFound} for cache={cache}, key={key}"
            );
            if (!expectedFound)
            {
                Assert.That(
                    value,
                    Is.EqualTo(default(int)),
                    "Value should be default when not found"
                );
            }
        }

        private static IEnumerable<TestCaseData> TryGetNullCases()
        {
            yield return new TestCaseData(null, "key", false).SetName(
                "TryGet.NullCache.ReturnsFalse"
            );

            yield return new TestCaseData(new Dictionary<string, int>(), null, false).SetName(
                "TryGet.NullKey.ReturnsFalse"
            );

            yield return new TestCaseData(null, null, false).SetName(
                "TryGet.BothNull.ReturnsFalse"
            );

            yield return new TestCaseData(
                new Dictionary<string, int> { { "existing", 42 } },
                "missing",
                false
            ).SetName("TryGet.MissingKey.ReturnsFalse");

            yield return new TestCaseData(
                new Dictionary<string, int> { { "existing", 42 } },
                "existing",
                true
            ).SetName("TryGet.ExistingKey.ReturnsTrue");
        }

        [Test]
        public void CacheWithCustomReferenceTypeKeysEvictsCorrectly()
        {
            Dictionary<List<int>, string> cache = new();
            int maxSize = 3;

            List<int> key1 = new() { 1 };
            List<int> key2 = new() { 2 };
            List<int> key3 = new() { 3 };
            List<int> key4 = new() { 4 };

            EditorCacheHelper.AddToBoundedCache(cache, key1, "one", maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, key2, "two", maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, key3, "three", maxSize);

            Assert.That(cache.Count, Is.EqualTo(3), "Cache should have 3 entries");

            EditorCacheHelper.AddToBoundedCache(cache, key4, "four", maxSize);

            Assert.That(
                cache.Count,
                Is.EqualTo(3),
                "Cache should still have 3 entries after eviction"
            );
            Assert.That(cache.ContainsKey(key1), Is.False, "key1 should be evicted (LRU)");
            Assert.That(cache.ContainsKey(key2), Is.True, "key2 should be present");
            Assert.That(cache.ContainsKey(key3), Is.True, "key3 should be present");
            Assert.That(cache.ContainsKey(key4), Is.True, "key4 should be present");
        }

        [Test]
        public void DirectDictionaryRemoveDoesNotBreakLRUTracking()
        {
            Dictionary<string, int> cache = new();
            int maxSize = 3;

            EditorCacheHelper.AddToBoundedCache(cache, "a", 1, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "b", 2, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "c", 3, maxSize);

            // Direct dictionary removal (bypasses LRU tracker)
            cache.Remove("b");

            Assert.That(
                cache.Count,
                Is.EqualTo(2),
                "Cache should have 2 entries after direct remove"
            );
            Assert.That(cache.ContainsKey("a"), Is.True, "'a' should still be present");
            Assert.That(cache.ContainsKey("c"), Is.True, "'c' should still be present");

            // The tracker still believes "b" exists; the adds have to cope with that.
            EditorCacheHelper.AddToBoundedCache(cache, "d", 4, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "e", 5, maxSize);

            Assert.That(
                cache.Count,
                Is.LessThanOrEqualTo(maxSize),
                $"Cache count {cache.Count} should not exceed maxSize {maxSize}"
            );
        }

        [Test]
        public void DirectDictionaryAddDoesNotBreakLRUTracking()
        {
            Dictionary<string, int> cache = new();
            int maxSize = 3;

            EditorCacheHelper.AddToBoundedCache(cache, "a", 1, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "b", 2, maxSize);

            // Direct dictionary add (bypasses LRU tracker)
            cache["direct"] = 999;

            Assert.That(cache.Count, Is.EqualTo(3), "Cache should have 3 entries after direct add");

            // "direct" is untracked, so "a" is the LRU entry that gets evicted.
            EditorCacheHelper.AddToBoundedCache(cache, "c", 3, maxSize);

            Assert.That(
                cache.Count,
                Is.LessThanOrEqualTo(maxSize),
                $"Cache count {cache.Count} should not exceed maxSize {maxSize}"
            );
            Assert.That(
                cache.ContainsKey("a"),
                Is.False,
                "'a' should be evicted (LRU tracked entry)"
            );
        }

        [Test]
        public void DirectDictionaryClearSynchronizesTrackerOnNextAdd()
        {
            Dictionary<string, int> cache = new();
            int maxSize = 3;

            EditorCacheHelper.AddToBoundedCache(cache, "a", 1, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "b", 2, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "c", 3, maxSize);

            // Direct dictionary clear (bypasses LRU tracker)
            cache.Clear();

            Assert.That(cache.Count, Is.EqualTo(0), "Cache should be empty after clear");

            // Next AddToBoundedCache should synchronize the tracker
            EditorCacheHelper.AddToBoundedCache(cache, "x", 10, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "y", 20, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "z", 30, maxSize);

            Assert.That(cache.Count, Is.EqualTo(3), "Cache should have 3 entries after reuse");

            // Adding one more should evict 'x' (the actual LRU after synchronization)
            EditorCacheHelper.AddToBoundedCache(cache, "w", 40, maxSize);

            Assert.That(cache.Count, Is.EqualTo(3), "Cache should be at capacity");
            Assert.That(
                cache.ContainsKey("x"),
                Is.False,
                "'x' should be evicted (LRU after tracker sync)"
            );
            Assert.That(cache.ContainsKey("y"), Is.True, "'y' should be present");
            Assert.That(cache.ContainsKey("z"), Is.True, "'z' should be present");
            Assert.That(cache.ContainsKey("w"), Is.True, "'w' should be present");
        }

        [Test]
        public void LRUOrderTrackerClearRemovesAllTrackedKeys()
        {
            LRUOrderTracker<string> tracker = new();

            tracker.MarkAccessed("a");
            tracker.MarkAccessed("b");
            tracker.MarkAccessed("c");

            Assert.That(
                tracker.TryGetLeastRecentlyUsed(out string lruBefore),
                Is.True,
                "Should have tracked keys before clear"
            );
            Assert.That(lruBefore, Is.EqualTo("a"), "'a' should be LRU before clear");

            tracker.Clear();

            Assert.That(
                tracker.TryGetLeastRecentlyUsed(out string lruAfter),
                Is.False,
                "Should have no tracked keys after clear"
            );
            Assert.That(
                lruAfter,
                Is.EqualTo(default(string)),
                "LRU key should be default after clear"
            );
        }

        [Test]
        public void LRUOrderTrackerClearAllowsReaddingKeys()
        {
            LRUOrderTracker<string> tracker = new();

            tracker.MarkAccessed("a");
            tracker.MarkAccessed("b");

            tracker.Clear();

            tracker.MarkAccessed("x");
            tracker.MarkAccessed("y");

            Assert.That(
                tracker.TryGetLeastRecentlyUsed(out string lru),
                Is.True,
                "Should have tracked keys after re-adding"
            );
            Assert.That(lru, Is.EqualTo("x"), "'x' should be LRU (first added after clear)");
        }

        /// <summary>
        /// An entry added straight to the dictionary is never tracked, so it takes no part in LRU
        /// eviction and survives the eviction of a tracked entry.
        /// </summary>
        [Test]
        public void OrphanDictionaryEntryRemainsAfterTrackedEntryEviction()
        {
            Dictionary<string, int> cache = new();
            int maxSize = 2;

            // Add through the helper to establish tracking
            EditorCacheHelper.AddToBoundedCache(cache, "tracked1", 1, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "tracked2", 2, maxSize);

            // Directly add an orphan entry (not tracked by LRU)
            cache["orphan"] = 999;

            Assert.That(
                cache.Count,
                Is.EqualTo(3),
                "Cache should have 3 entries (2 tracked + 1 orphan)"
            );

            // Add another entry through the helper - this should evict tracked1 (LRU)
            EditorCacheHelper.AddToBoundedCache(cache, "tracked3", 3, maxSize);

            // The orphan is untracked, so the cache keeps three entries even though maxSize is 2.
            Assert.That(
                cache.ContainsKey("tracked1"),
                Is.False,
                "'tracked1' should be evicted (LRU tracked entry)"
            );
            Assert.That(
                cache.ContainsKey("orphan"),
                Is.True,
                "'orphan' remains (not tracked, not evictable)"
            );
        }

        [Test]
        public void CacheClearThenAddRespectsBoundary()
        {
            Dictionary<string, int> cache = new();
            int maxSize = 1;

            EditorCacheHelper.AddToBoundedCache(cache, "first", 1, maxSize);
            Assert.That(cache.Count, Is.EqualTo(1), "Cache should have 1 entry");

            cache.Clear();
            Assert.That(cache.Count, Is.EqualTo(0), "Cache should be empty after clear");

            // Add two entries - the second should evict the first
            EditorCacheHelper.AddToBoundedCache(cache, "second", 2, maxSize);
            Assert.That(
                cache.Count,
                Is.EqualTo(1),
                "Cache should have 1 entry after first add post-clear"
            );

            EditorCacheHelper.AddToBoundedCache(cache, "third", 3, maxSize);
            Assert.That(
                cache.Count,
                Is.EqualTo(1),
                "Cache should still have 1 entry (at capacity)"
            );
            Assert.That(cache.ContainsKey("third"), Is.True, "'third' should be the only entry");
            Assert.That(cache.ContainsKey("second"), Is.False, "'second' should be evicted");
        }

        [Test]
        public void AddToBoundedCacheWithDefaultValueTypeKeyIsValid()
        {
            Dictionary<int, string> cache = new();

            EditorCacheHelper.AddToBoundedCache(cache, 0, "zero", 10);

            Assert.That(
                cache.ContainsKey(0),
                Is.True,
                "Default int key (0) should be valid and added to cache"
            );
            Assert.That(
                cache.Count,
                Is.EqualTo(1),
                "Cache should contain exactly one entry after adding default int key"
            );
            Assert.That(
                cache[0],
                Is.EqualTo("zero"),
                "Value for default int key (0) should be 'zero'"
            );
        }

        [Test]
        [TestCaseSource(nameof(DefaultValueTypeKeyCases))]
        public void AddToBoundedCacheWithDefaultValueTypeKeysAreValid<TKey, TValue>(
            TKey defaultKey,
            TValue testValue,
            string keyDescription
        )
        {
            Dictionary<TKey, TValue> cache = new();

            EditorCacheHelper.AddToBoundedCache(cache, defaultKey, testValue, 10);

            Assert.That(
                cache.ContainsKey(defaultKey),
                Is.True,
                $"Default {keyDescription} key should be valid and added to cache"
            );
            Assert.That(
                cache.Count,
                Is.EqualTo(1),
                $"Cache should contain exactly one entry after adding default {keyDescription} key"
            );
            Assert.That(
                cache[defaultKey],
                Is.EqualTo(testValue),
                $"Value for default {keyDescription} key should be '{testValue}'"
            );
        }

        private static IEnumerable<TestCaseData> DefaultValueTypeKeyCases()
        {
            yield return new TestCaseData(0, "zero", "int").SetName(
                "DefaultValueType.IntZero.IsValidKey"
            );

            yield return new TestCaseData(0L, "longZero", "long").SetName(
                "DefaultValueType.LongZero.IsValidKey"
            );

            yield return new TestCaseData(0f, "floatZero", "float").SetName(
                "DefaultValueType.FloatZero.IsValidKey"
            );

            yield return new TestCaseData(0.0, "doubleZero", "double").SetName(
                "DefaultValueType.DoubleZero.IsValidKey"
            );

            yield return new TestCaseData(false, "boolFalse", "bool").SetName(
                "DefaultValueType.BoolFalse.IsValidKey"
            );

            yield return new TestCaseData('\0', "nullChar", "char").SetName(
                "DefaultValueType.CharNull.IsValidKey"
            );

            yield return new TestCaseData((byte)0, "byteZero", "byte").SetName(
                "DefaultValueType.ByteZero.IsValidKey"
            );

            yield return new TestCaseData((short)0, "shortZero", "short").SetName(
                "DefaultValueType.ShortZero.IsValidKey"
            );
        }

        [Test]
        public void TryGetFromBoundedLRUCacheWorksWithOrphanDictionaryEntries()
        {
            // An "orphan" entry was added straight to the dictionary, so the LRU tracker never saw it.
            Dictionary<string, int> cache = new() { { "orphan", 42 } };

            bool found = EditorCacheHelper.TryGetFromBoundedLRUCache(
                cache,
                "orphan",
                out int value
            );

            Assert.That(
                found,
                Is.True,
                "Should find orphan entry in cache (entry exists in dictionary)"
            );
            Assert.That(value, Is.EqualTo(42), "Should return correct value for orphan entry");
        }

        [Test]
        public void TryGetFromBoundedLRUCacheWithOrphanEntryAddsToTracking()
        {
            Dictionary<string, int> cache = new() { { "orphan", 42 } };
            int maxSize = 2;

            // Access the orphan - this should add it to LRU tracking
            EditorCacheHelper.TryGetFromBoundedLRUCache(cache, "orphan", out _);

            // Add a tracked entry
            EditorCacheHelper.AddToBoundedCache(cache, "tracked", 100, maxSize);

            Assert.That(
                cache.Count,
                Is.EqualTo(2),
                "Cache should have 2 entries (orphan + tracked)"
            );

            // Add another entry - should evict orphan (now LRU since it was accessed first)
            EditorCacheHelper.AddToBoundedCache(cache, "newest", 200, maxSize);

            Assert.That(cache.Count, Is.EqualTo(2), "Cache should be at capacity of 2");
            Assert.That(
                cache.ContainsKey("orphan"),
                Is.False,
                "'orphan' should be evicted (LRU after being added to tracking)"
            );
            Assert.That(cache.ContainsKey("tracked"), Is.True, "'tracked' should still be present");
            Assert.That(cache.ContainsKey("newest"), Is.True, "'newest' should be present");
        }

        [Test]
        public void TryGetFromBoundedLRUCacheWithMultipleOrphanEntries()
        {
            // Test behavior when dictionary has multiple orphan entries
            Dictionary<string, int> cache = new()
            {
                { "orphan1", 1 },
                { "orphan2", 2 },
                { "orphan3", 3 },
            };

            bool found1 = EditorCacheHelper.TryGetFromBoundedLRUCache(
                cache,
                "orphan1",
                out int value1
            );
            bool found2 = EditorCacheHelper.TryGetFromBoundedLRUCache(
                cache,
                "orphan2",
                out int value2
            );
            bool found3 = EditorCacheHelper.TryGetFromBoundedLRUCache(
                cache,
                "orphan3",
                out int value3
            );

            Assert.That(found1, Is.True, "Should find first orphan entry");
            Assert.That(found2, Is.True, "Should find second orphan entry");
            Assert.That(found3, Is.True, "Should find third orphan entry");
            Assert.That(value1, Is.EqualTo(1), "First orphan should have value 1");
            Assert.That(value2, Is.EqualTo(2), "Second orphan should have value 2");
            Assert.That(value3, Is.EqualTo(3), "Third orphan should have value 3");
        }

        [Test]
        public void TryGetFromBoundedLRUCacheOrphanEntryDoesNotBreakSubsequentOperations()
        {
            // Ensures that orphan entries don't cause issues with normal cache operations
            Dictionary<string, int> cache = new() { { "orphan", 999 } };
            int maxSize = 3;

            // Access orphan to add it to tracking
            EditorCacheHelper.TryGetFromBoundedLRUCache(cache, "orphan", out _);

            // Normal cache operations
            EditorCacheHelper.AddToBoundedCache(cache, "a", 1, maxSize);
            EditorCacheHelper.AddToBoundedCache(cache, "b", 2, maxSize);

            Assert.That(cache.Count, Is.EqualTo(3), "Cache should have 3 entries at capacity");

            // Access 'a' to make it recently used
            EditorCacheHelper.TryGetFromBoundedLRUCache(cache, "a", out _);

            // Add new entry - should evict 'orphan' (LRU)
            EditorCacheHelper.AddToBoundedCache(cache, "c", 3, maxSize);

            Assert.That(cache.Count, Is.EqualTo(3), "Cache should still be at capacity of 3");
            Assert.That(
                cache.ContainsKey("orphan"),
                Is.False,
                "'orphan' should be evicted (was LRU)"
            );
            Assert.That(
                cache.ContainsKey("a"),
                Is.True,
                "'a' should be present (was accessed recently)"
            );
            Assert.That(cache.ContainsKey("b"), Is.True, "'b' should be present");
            Assert.That(cache.ContainsKey("c"), Is.True, "'c' should be present");
        }

        [Test]
        [TestCaseSource(nameof(ValueTypeKeyEdgeCases))]
        public void AddToBoundedCacheValueTypeKeyEdgeCases<TKey>(
            TKey key,
            string expectedDescription
        )
            where TKey : struct
        {
            Dictionary<TKey, string> cache = new();

            EditorCacheHelper.AddToBoundedCache(cache, key, expectedDescription, 10);

            Assert.That(
                cache.ContainsKey(key),
                Is.True,
                $"Value type key ({expectedDescription}) should be valid"
            );
            Assert.That(
                cache[key],
                Is.EqualTo(expectedDescription),
                $"Value for {expectedDescription} key should match"
            );
        }

        private static IEnumerable<TestCaseData> ValueTypeKeyEdgeCases()
        {
            yield return new TestCaseData(int.MinValue, "int.MinValue").SetName(
                "ValueTypeEdge.IntMinValue.IsValidKey"
            );

            yield return new TestCaseData(int.MaxValue, "int.MaxValue").SetName(
                "ValueTypeEdge.IntMaxValue.IsValidKey"
            );

            yield return new TestCaseData(-1, "negative one").SetName(
                "ValueTypeEdge.NegativeOne.IsValidKey"
            );

            yield return new TestCaseData(float.NaN, "NaN").SetName(
                "ValueTypeEdge.FloatNaN.IsValidKey"
            );

            yield return new TestCaseData(float.PositiveInfinity, "positive infinity").SetName(
                "ValueTypeEdge.FloatPosInf.IsValidKey"
            );

            yield return new TestCaseData(float.NegativeInfinity, "negative infinity").SetName(
                "ValueTypeEdge.FloatNegInf.IsValidKey"
            );

            yield return new TestCaseData(double.Epsilon, "double epsilon").SetName(
                "ValueTypeEdge.DoubleEpsilon.IsValidKey"
            );
        }

        [Test]
        public void LRUOrderTrackerMarkAccessedWithDefaultValueTypeKey()
        {
            LRUOrderTracker<int> tracker = new();

            tracker.MarkAccessed(0);
            tracker.MarkAccessed(1);
            tracker.MarkAccessed(2);

            Assert.That(
                tracker.TryGetLeastRecentlyUsed(out int lru),
                Is.True,
                "Should have tracked keys including default value type"
            );
            Assert.That(lru, Is.EqualTo(0), "Default int (0) should be LRU (first added)");

            // Access 0 to move it to end
            tracker.MarkAccessed(0);

            Assert.That(
                tracker.TryGetLeastRecentlyUsed(out int lruAfter),
                Is.True,
                "Should still have tracked keys after accessing default value"
            );
            Assert.That(lruAfter, Is.EqualTo(1), "1 should now be LRU after 0 was moved to end");
        }

        [Test]
        public void LRUOrderTrackerRemoveDefaultValueTypeKey()
        {
            LRUOrderTracker<int> tracker = new();

            tracker.MarkAccessed(0);
            tracker.MarkAccessed(1);

            tracker.Remove(0);

            Assert.That(
                tracker.TryGetLeastRecentlyUsed(out int lru),
                Is.True,
                "Should still have tracked key after removing default value type key"
            );
            Assert.That(lru, Is.EqualTo(1), "1 should be the only remaining key and thus LRU");
        }

        [Test]
        public void GetCachedIntStringReturnsCorrectStringForPositiveInt()
        {
            string result = EditorCacheHelper.GetCachedIntString(42);

            Assert.That(result, Is.EqualTo("42"), "Should return correct string for positive int");
        }

        [Test]
        public void GetCachedIntStringReturnsCorrectStringForNegativeInt()
        {
            string result = EditorCacheHelper.GetCachedIntString(-123);

            Assert.That(
                result,
                Is.EqualTo("-123"),
                "Should return correct string for negative int"
            );
        }

        [Test]
        public void GetCachedIntStringReturnsCorrectStringForZero()
        {
            string result = EditorCacheHelper.GetCachedIntString(0);

            Assert.That(result, Is.EqualTo("0"), "Should return correct string for zero");
        }

        [Test]
        public void GetCachedIntStringReturnsSameInstanceForSameValue()
        {
            string result1 = EditorCacheHelper.GetCachedIntString(999);
            string result2 = EditorCacheHelper.GetCachedIntString(999);

            Assert.That(
                ReferenceEquals(result1, result2),
                Is.True,
                "Should return same cached instance for same value"
            );
        }

        [Test]
        public void GetCachedIntStringHandlesIntMaxValue()
        {
            string result = EditorCacheHelper.GetCachedIntString(int.MaxValue);

            Assert.That(
                result,
                Is.EqualTo(int.MaxValue.ToString()),
                "Should correctly convert int.MaxValue"
            );
        }

        [Test]
        public void GetCachedIntStringHandlesIntMinValue()
        {
            string result = EditorCacheHelper.GetCachedIntString(int.MinValue);

            Assert.That(
                result,
                Is.EqualTo(int.MinValue.ToString()),
                "Should correctly convert int.MinValue"
            );
        }

        [Test]
        public void GetPaginationLabelReturnsCorrectFormat()
        {
            string result = EditorCacheHelper.GetPaginationLabel(3, 10);

            Assert.That(
                result,
                Is.EqualTo("Page 3 / 10"),
                "Should return pagination label in correct format"
            );
        }

        [Test]
        public void GetPaginationLabelReturnsSameInstanceForSameValues()
        {
            string result1 = EditorCacheHelper.GetPaginationLabel(5, 20);
            string result2 = EditorCacheHelper.GetPaginationLabel(5, 20);

            Assert.That(
                ReferenceEquals(result1, result2),
                Is.True,
                "Should return same cached instance for same page/total values"
            );
        }

        [Test]
        public void GetPaginationLabelHandlesFirstPage()
        {
            string result = EditorCacheHelper.GetPaginationLabel(1, 1);

            Assert.That(
                result,
                Is.EqualTo("Page 1 / 1"),
                "Should handle first page of single page correctly"
            );
        }

        [Test]
        public void GetPaginationLabelHandlesLargeValues()
        {
            string result = EditorCacheHelper.GetPaginationLabel(999, 1000);

            Assert.That(
                result,
                Is.EqualTo("Page 999 / 1000"),
                "Should handle large page numbers correctly"
            );
        }

        [Test]
        public void GetPaginationLabelReturnsDifferentInstancesForDifferentValues()
        {
            string result1 = EditorCacheHelper.GetPaginationLabel(1, 5);
            string result2 = EditorCacheHelper.GetPaginationLabel(2, 5);

            Assert.That(
                ReferenceEquals(result1, result2),
                Is.False,
                "Should return different instances for different page values"
            );
        }

        [Test]
        public void ClearAllCachesClearsIntToStringCache()
        {
            // Populate the cache
            EditorCacheHelper.GetCachedIntString(12345);
            int countBefore = EditorCacheHelper.GetIntToStringCacheCount();

            Assert.That(countBefore, Is.GreaterThan(0), "Cache should have entries before clear");

            EditorCacheHelper.ClearAllCaches();

            int countAfter = EditorCacheHelper.GetIntToStringCacheCount();
            Assert.That(countAfter, Is.EqualTo(0), "Cache should be empty after clear");
        }

        [Test]
        public void ClearAllCachesClearsPaginationLabelCache()
        {
            // Populate the cache
            EditorCacheHelper.GetPaginationLabel(1, 100);
            EditorCacheHelper.GetPaginationLabel(2, 100);
            int countBefore = EditorCacheHelper.GetPaginationLabelCacheCount();

            Assert.That(
                countBefore,
                Is.GreaterThan(0),
                "Pagination cache should have entries before clear"
            );

            EditorCacheHelper.ClearAllCaches();

            int countAfter = EditorCacheHelper.GetPaginationLabelCacheCount();
            Assert.That(countAfter, Is.EqualTo(0), "Pagination cache should be empty after clear");
        }

        [Test]
        public void GetCachedIntStringPopulatesCacheProgressively()
        {
            EditorCacheHelper.ClearAllCaches();

            int initialCount = EditorCacheHelper.GetIntToStringCacheCount();

            EditorCacheHelper.GetCachedIntString(1);
            EditorCacheHelper.GetCachedIntString(2);
            EditorCacheHelper.GetCachedIntString(3);

            int afterCount = EditorCacheHelper.GetIntToStringCacheCount();

            Assert.That(
                afterCount,
                Is.EqualTo(initialCount + 3),
                "Cache should grow as new values are added"
            );
        }

        [Test]
        public void GetCachedIntStringDoesNotDuplicateExistingEntries()
        {
            EditorCacheHelper.ClearAllCaches();

            EditorCacheHelper.GetCachedIntString(100);
            int countAfterFirst = EditorCacheHelper.GetIntToStringCacheCount();

            EditorCacheHelper.GetCachedIntString(100);
            int countAfterSecond = EditorCacheHelper.GetIntToStringCacheCount();

            Assert.That(
                countAfterSecond,
                Is.EqualTo(countAfterFirst),
                "Calling GetCachedIntString with same value should not add duplicate entry"
            );
        }
    }

#endif
}
