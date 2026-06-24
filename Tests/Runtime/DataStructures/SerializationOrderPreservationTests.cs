// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Tests that verify serialization order is preserved across domain reloads and serialization cycles.
    /// These tests validate that user-defined element ordering in the Unity inspector is maintained
    /// and not reordered by the underlying data structure's natural ordering.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializationOrderPreservationTests : CommonTestBase
    {
        [Test]
        public void SortedDictionaryPreservesSerializedKeyOrderAcrossSerializationCycle()
        {
            // Arrange: Create a dictionary with keys in descending order (opposite of sorted order)
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };

            // Act: Simulate domain reload - deserialize then serialize
            dictionary.OnAfterDeserialize();
            dictionary.OnBeforeSerialize();

            // Assert: Serialized keys should maintain original order, not sorted order
            int[] expectedKeys = { 3, 1, 2 };
            CollectionAssert.AreEqual(expectedKeys, dictionary._keys);
        }

        [Test]
        public void SortedDictionaryPreservesSerializedKeyOrderAfterMultipleSerializationCycles()
        {
            // Arrange: Create a dictionary with custom key order
            SerializableSortedDictionary<string, int> dictionary = new()
            {
                _keys = new[] { "zebra", "apple", "mango" },
                _values = new[] { 1, 2, 3 },
            };

            // Act: Simulate multiple domain reloads
            for (int cycle = 0; cycle < 5; cycle++)
            {
                dictionary.OnAfterDeserialize();
                dictionary.OnBeforeSerialize();
            }

            // Assert: Order should be maintained across all cycles
            string[] expectedKeys = { "zebra", "apple", "mango" };
            CollectionAssert.AreEqual(expectedKeys, dictionary._keys);
        }

        [Test]
        public void SortedDictionaryPreservesOrderWhenValueIsUpdated()
        {
            // Arrange: Create dictionary with specific order
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 5, 2, 8, 1 },
                _values = new[] { "five", "two", "eight", "one" },
            };
            dictionary.OnAfterDeserialize();

            // Act: Update a value (this marks the cache dirty)
            dictionary[2] = "TWO_UPDATED";
            dictionary.OnBeforeSerialize();

            // Assert: Key order should be preserved, only value changed
            int[] expectedKeys = { 5, 2, 8, 1 };
            CollectionAssert.AreEqual(expectedKeys, dictionary._keys);
            Assert.AreEqual("TWO_UPDATED", dictionary._values[1]);
        }

        [Test]
        public void SortedDictionaryAppendsNewKeysAtEndWhilePreservingExistingOrder()
        {
            // Arrange: Create dictionary with specific order
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 10, 5, 20 },
                _values = new[] { "ten", "five", "twenty" },
            };
            dictionary.OnAfterDeserialize();

            // Act: Add a new key (this marks the cache dirty)
            dictionary.Add(1, "one");
            dictionary.OnBeforeSerialize();

            // Assert: Existing keys maintain order, new key appended at end
            Assert.AreEqual(4, dictionary._keys.Length);
            Assert.AreEqual(10, dictionary._keys[0]);
            Assert.AreEqual(5, dictionary._keys[1]);
            Assert.AreEqual(20, dictionary._keys[2]);
            Assert.AreEqual(1, dictionary._keys[3]); // New key at end
        }

        [Test]
        public void SortedDictionaryRemovesKeyWhilePreservingRemainingOrder()
        {
            // Arrange: Create dictionary with specific order
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 10, 5, 20, 15 },
                _values = new[] { "ten", "five", "twenty", "fifteen" },
            };
            dictionary.OnAfterDeserialize();

            // Act: Remove a key from the middle
            bool removed = dictionary.Remove(5);
            dictionary.OnBeforeSerialize();

            // Assert: Remaining keys maintain original relative order
            Assert.IsTrue(removed);
            int[] expectedKeys = { 10, 20, 15 };
            CollectionAssert.AreEqual(expectedKeys, dictionary._keys);
        }

        [Test]
        public void SortedDictionaryClearResultsInEmptyArraysOnNextSerialize()
        {
            // Arrange: Create dictionary with specific order
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };
            dictionary.OnAfterDeserialize();

            // Act: Clear the dictionary
            dictionary.Clear();
            dictionary.OnBeforeSerialize();

            // Assert: Arrays should be empty
            Assert.AreEqual(0, dictionary._keys.Length);
            Assert.AreEqual(0, dictionary._values.Length);
        }

        [Test]
        public void SortedDictionaryWithDuplicateKeysPreservesOriginalArrayOnDeserialization()
        {
            // Arrange: Create arrays with duplicate keys (invalid but possible in inspector)
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 1, 2, 1, 3 },
                _values = new[] { "one-first", "two", "one-second", "three" },
            };

            // Act: Deserialize
            dictionary.OnAfterDeserialize();

            // Assert: Original arrays should be preserved (for editing in inspector)
            Assert.IsTrue(dictionary.PreserveSerializedEntries);
            Assert.IsTrue(dictionary.HasDuplicatesOrNulls);
            CollectionAssert.AreEqual(new[] { 1, 2, 1, 3 }, dictionary._keys);
        }

        [Test]
        public void SortedDictionaryEnumerationOrderIsSortedRegardlessOfSerializedOrder()
        {
            // Arrange: Create dictionary with custom serialized order
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 30, 10, 20 },
                _values = new[] { "thirty", "ten", "twenty" },
            };
            dictionary.OnAfterDeserialize();

            // Act: Enumerate
            List<int> enumeratedKeys = new();
            foreach (KeyValuePair<int, string> pair in dictionary)
            {
                enumeratedKeys.Add(pair.Key);
            }

            // Assert: Enumeration should be in sorted order (SortedDictionary behavior)
            int[] expectedSortedKeys = { 10, 20, 30 };
            CollectionAssert.AreEqual(expectedSortedKeys, enumeratedKeys);
        }

        [Test]
        public void SortedDictionaryIndexerUpdatesValueInPlacePreservingKeyPosition()
        {
            // Arrange
            SerializableSortedDictionary<string, int> dictionary = new()
            {
                _keys = new[] { "z", "a", "m" },
                _values = new[] { 1, 2, 3 },
            };
            dictionary.OnAfterDeserialize();

            // Act: Update multiple values
            dictionary["z"] = 100;
            dictionary["a"] = 200;
            dictionary.OnBeforeSerialize();

            // Assert: Key order preserved, values updated
            CollectionAssert.AreEqual(new[] { "z", "a", "m" }, dictionary._keys);
            Assert.AreEqual(100, dictionary._values[0]);
            Assert.AreEqual(200, dictionary._values[1]);
            Assert.AreEqual(3, dictionary._values[2]);
        }

        [Test]
        public void SortedDictionaryComplexScenarioAddRemoveUpdatePreservesOrder()
        {
            // Arrange: Start with ordered dictionary
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 100, 50, 200, 75 },
                _values = new[] { "hundred", "fifty", "two-hundred", "seventy-five" },
            };
            dictionary.OnAfterDeserialize();

            // Act: Complex operations
            dictionary[50] = "FIFTY_UPDATED"; // Update
            dictionary.Remove(200); // Remove from middle
            dictionary.Add(25, "twenty-five"); // Add new
            dictionary.Add(300, "three-hundred"); // Add another new
            dictionary.OnBeforeSerialize();

            // Assert: Original order preserved for remaining keys, new keys appended in insertion order
            string actualKeys =
                dictionary._keys != null ? string.Join(", ", dictionary._keys) : "null";
            int[] expectedKeys = { 100, 50, 75, 25, 300 };
            CollectionAssert.AreEqual(
                expectedKeys,
                dictionary._keys,
                $"Expected keys [100, 50, 75, 25, 300], got [{actualKeys}]"
            );
            Assert.AreEqual("FIFTY_UPDATED", dictionary._values[1]);
        }

        [Test]
        public void SortedSetPreservesSerializedItemOrderAcrossSerializationCycle()
        {
            // Arrange: Create a set with items in descending order (opposite of sorted order)
            SerializableSortedSet<int> set = new() { _items = new[] { 30, 10, 20 } };

            // Act: Simulate domain reload
            set.OnAfterDeserialize();
            set.OnBeforeSerialize();

            // Assert: Serialized items should maintain original order
            int[] expectedItems = { 30, 10, 20 };
            CollectionAssert.AreEqual(expectedItems, set._items);
        }

        [Test]
        public void SortedSetPreservesOrderAfterMultipleSerializationCycles()
        {
            // Arrange
            SerializableSortedSet<string> set = new()
            {
                _items = new[] { "zebra", "apple", "mango", "banana" },
            };

            // Act: Multiple cycles
            for (int cycle = 0; cycle < 5; cycle++)
            {
                set.OnAfterDeserialize();
                set.OnBeforeSerialize();
            }

            // Assert
            string[] expectedItems = { "zebra", "apple", "mango", "banana" };
            CollectionAssert.AreEqual(expectedItems, set._items);
        }

        [Test]
        public void SortedSetAppendsNewItemsAtEndWhilePreservingExistingOrder()
        {
            // Arrange
            SerializableSortedSet<int> set = new() { _items = new[] { 50, 20, 80 } };
            set.OnAfterDeserialize();

            // Act: Add new item
            set.Add(10);
            set.OnBeforeSerialize();

            // Assert: Existing items maintain order, new item appended
            Assert.AreEqual(4, set._items.Length);
            Assert.AreEqual(50, set._items[0]);
            Assert.AreEqual(20, set._items[1]);
            Assert.AreEqual(80, set._items[2]);
            Assert.AreEqual(10, set._items[3]);
        }

        [Test]
        public void SortedSetRemovesItemWhilePreservingRemainingOrder()
        {
            // Arrange
            SerializableSortedSet<int> set = new() { _items = new[] { 50, 20, 80, 35 } };
            set.OnAfterDeserialize();

            // Act: Remove from middle
            bool removed = set.Remove(20);
            set.OnBeforeSerialize();

            // Assert
            Assert.IsTrue(removed);
            int[] expectedItems = { 50, 80, 35 };
            CollectionAssert.AreEqual(expectedItems, set._items);
        }

        [Test]
        public void SortedSetEnumerationIsSortedRegardlessOfSerializedOrder()
        {
            // Arrange
            SerializableSortedSet<int> set = new() { _items = new[] { 30, 10, 20 } };
            set.OnAfterDeserialize();

            // Act: Enumerate
            List<int> enumerated = new();
            foreach (int item in set)
            {
                enumerated.Add(item);
            }

            // Assert: Enumeration should be in sorted order
            int[] expectedSorted = { 10, 20, 30 };
            CollectionAssert.AreEqual(expectedSorted, enumerated);
        }

        [Test]
        public void SortedSetWithDuplicatesPreservesOriginalArrayOnDeserialization()
        {
            // Arrange: Array with duplicates
            SerializableSortedSet<int> set = new() { _items = new[] { 1, 2, 1, 3 } };

            // Act
            set.OnAfterDeserialize();

            // Assert
            Assert.IsTrue(set.PreserveSerializedEntries);
            Assert.IsTrue(set.HasDuplicatesOrNulls);
            CollectionAssert.AreEqual(new[] { 1, 2, 1, 3 }, set._items);
        }

        [Test]
        public void HashSetPreservesSerializedItemOrderAcrossSerializationCycle()
        {
            // Arrange: Create a set with specific item order
            SerializableHashSet<int> set = new() { _items = new[] { 7, 3, 9, 1 } };

            // Act: Simulate domain reload
            set.OnAfterDeserialize();
            set.OnBeforeSerialize();

            // Assert: Serialized items should maintain original order
            int[] expectedItems = { 7, 3, 9, 1 };
            CollectionAssert.AreEqual(expectedItems, set._items);
        }

        [Test]
        public void HashSetPreservesOrderAfterMultipleSerializationCycles()
        {
            // Arrange
            SerializableHashSet<string> set = new()
            {
                _items = new[] { "delta", "alpha", "charlie", "bravo" },
            };

            // Act: Multiple cycles
            for (int cycle = 0; cycle < 5; cycle++)
            {
                set.OnAfterDeserialize();
                set.OnBeforeSerialize();
            }

            // Assert
            string[] expectedItems = { "delta", "alpha", "charlie", "bravo" };
            CollectionAssert.AreEqual(expectedItems, set._items);
        }

        [Test]
        public void HashSetAppendsNewItemsAtEndWhilePreservingExistingOrder()
        {
            // Arrange
            SerializableHashSet<int> set = new() { _items = new[] { 100, 50, 200 } };
            set.OnAfterDeserialize();

            // Act: Add new items
            set.Add(25);
            set.Add(150);
            set.OnBeforeSerialize();

            // Assert: Existing items maintain order, new items appended in insertion order
            string actualItems = set._items != null ? string.Join(", ", set._items) : "null";
            int[] expected = { 100, 50, 200, 25, 150 };
            CollectionAssert.AreEqual(
                expected,
                set._items,
                $"Expected [100, 50, 200, 25, 150], got [{actualItems}]"
            );
        }

        [Test]
        public void HashSetRemovesItemWhilePreservingRemainingOrder()
        {
            // Arrange
            SerializableHashSet<int> set = new() { _items = new[] { 100, 50, 200, 75 } };
            set.OnAfterDeserialize();

            // Act: Remove from middle
            bool removed = set.Remove(50);
            set.OnBeforeSerialize();

            // Assert
            Assert.IsTrue(removed);
            int[] expectedItems = { 100, 200, 75 };
            CollectionAssert.AreEqual(expectedItems, set._items);
        }

        [Test]
        public void HashSetClearResultsInEmptyArrayOnNextSerialize()
        {
            // Arrange
            SerializableHashSet<int> set = new() { _items = new[] { 1, 2, 3 } };
            set.OnAfterDeserialize();

            // Act
            set.Clear();
            set.OnBeforeSerialize();

            // Assert
            Assert.AreEqual(0, set._items.Length);
        }

        [Test]
        public void HashSetWithDuplicatesPreservesOriginalArrayOnDeserialization()
        {
            // Arrange
            SerializableHashSet<string> set = new() { _items = new[] { "a", "b", "a", "c" } };

            // Act
            set.OnAfterDeserialize();

            // Assert
            Assert.IsTrue(set.PreserveSerializedEntries);
            Assert.IsTrue(set.HasDuplicatesOrNulls);
            CollectionAssert.AreEqual(new[] { "a", "b", "a", "c" }, set._items);
        }

        [Test]
        public void HashSetComplexScenarioAddRemovePreservesOrder()
        {
            // Arrange
            SerializableHashSet<int> set = new() { _items = new[] { 10, 20, 30, 40, 50 } };
            set.OnAfterDeserialize();

            // Act: Complex operations
            set.Remove(20);
            set.Remove(40);
            set.Add(25);
            set.Add(45);
            set.OnBeforeSerialize();

            // Assert: Remaining original items maintain order, new items appended
            string actualItems = set._items != null ? string.Join(", ", set._items) : "null";
            Assert.AreEqual(
                5,
                set._items?.Length ?? 0,
                $"Expected 5 items, got {set._items?.Length ?? 0}. Items: [{actualItems}]"
            );
            Assert.AreEqual(
                10,
                set._items[0],
                $"Expected _items[0]=10, got {set._items[0]}. Items: [{actualItems}]"
            );
            Assert.AreEqual(
                30,
                set._items[1],
                $"Expected _items[1]=30, got {set._items[1]}. Items: [{actualItems}]"
            );
            Assert.AreEqual(
                50,
                set._items[2],
                $"Expected _items[2]=50, got {set._items[2]}. Items: [{actualItems}]"
            );
            // New items at end
            List<int> newItems = new(set._items.Skip(3));
            CollectionAssert.AreEquivalent(
                new[] { 25, 45 },
                newItems,
                $"New items at end should be [25, 45] (any order), got [{string.Join(", ", newItems)}]. Full items: [{actualItems}]"
            );
        }

        [Test]
        public void EmptySortedDictionarySerializesToEmptyArrays()
        {
            // Arrange
            SerializableSortedDictionary<int, string> dictionary = new();

            // Act
            dictionary.OnBeforeSerialize();

            // Assert
            Assert.IsTrue(dictionary._keys != null);
            Assert.IsTrue(dictionary._values != null);
            Assert.AreEqual(0, dictionary._keys.Length);
            Assert.AreEqual(0, dictionary._values.Length);
        }

        [Test]
        public void EmptySortedSetSerializesToEmptyArray()
        {
            // Arrange
            SerializableSortedSet<int> set = new();

            // Act
            set.OnBeforeSerialize();

            // Assert
            Assert.IsTrue(set._items != null);
            Assert.AreEqual(0, set._items.Length);
        }

        [Test]
        public void EmptyHashSetSerializesToEmptyArray()
        {
            // Arrange
            SerializableHashSet<int> set = new();

            // Act
            set.OnBeforeSerialize();

            // Assert
            Assert.IsTrue(set._items != null);
            Assert.AreEqual(0, set._items.Length);
        }

        [Test]
        public void SingleItemSortedDictionaryPreservesOrder()
        {
            // Arrange
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 42 },
                _values = new[] { "answer" },
            };

            // Act
            dictionary.OnAfterDeserialize();
            dictionary.OnBeforeSerialize();

            // Assert
            Assert.AreEqual(1, dictionary._keys.Length);
            Assert.AreEqual(42, dictionary._keys[0]);
            Assert.AreEqual("answer", dictionary._values[0]);
        }

        [Test]
        public void SingleItemSetPreservesOrder()
        {
            // Arrange
            SerializableHashSet<int> set = new() { _items = new[] { 42 } };

            // Act
            set.OnAfterDeserialize();
            set.OnBeforeSerialize();

            // Assert
            Assert.AreEqual(1, set._items.Length);
            Assert.AreEqual(42, set._items[0]);
        }

        [Test]
        public void LargeSortedDictionaryPreservesOrderAcrossManyCycles()
        {
            // Arrange: Create a large dictionary with reverse-sorted keys
            int count = 1000;
            int[] keys = new int[count];
            string[] values = new string[count];
            for (int i = 0; i < count; i++)
            {
                keys[i] = count - i; // Reverse order: 1000, 999, 998, ...
                values[i] = $"value_{count - i}";
            }

            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = keys,
                _values = values,
            };

            // Act: Multiple cycles
            for (int cycle = 0; cycle < 3; cycle++)
            {
                dictionary.OnAfterDeserialize();
                dictionary.OnBeforeSerialize();
            }

            // Assert: Order preserved
            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(count - i, dictionary._keys[i]);
                Assert.AreEqual($"value_{count - i}", dictionary._values[i]);
            }
        }

        [Test]
        public void LargeHashSetPreservesOrderAcrossManyCycles()
        {
            // Arrange: Create a large set with specific order
            int count = 1000;
            int[] items = new int[count];
            for (int i = 0; i < count; i++)
            {
                items[i] = (i * 7) % 10000; // Pseudo-random but deterministic order
            }

            SerializableHashSet<int> set = new() { _items = items };

            // Act: Multiple cycles
            for (int cycle = 0; cycle < 3; cycle++)
            {
                set.OnAfterDeserialize();
                set.OnBeforeSerialize();
            }

            // Assert: Order preserved
            CollectionAssert.AreEqual(items, set._items);
        }

        [Test]
        public void SortedDictionaryWithNullKeysPreservesArrayForEditing()
        {
            // Arrange: Array with null key (string type)
            SerializableSortedDictionary<string, int> dictionary = new()
            {
                _keys = new[] { "valid", null, "also-valid" },
                _values = new[] { 1, 2, 3 },
            };

            // Expect the error log about null key being skipped
            LogAssert.Expect(
                LogType.Warning,
                "SerializableSortedDictionary<System.String, System.Int32> skipped serialized entry at index 1 because the key reference was null."
            );

            // Act
            dictionary.OnAfterDeserialize();

            // Assert: Array preserved, duplicates/nulls flag set
            Assert.IsTrue(dictionary.PreserveSerializedEntries);
            Assert.IsTrue(dictionary.HasDuplicatesOrNulls);
            // Dictionary should contain only valid keys
            Assert.AreEqual(2, dictionary.Count);
            Assert.IsTrue(dictionary.ContainsKey("valid"));
            Assert.IsTrue(dictionary.ContainsKey("also-valid"));
        }

        [Test]
        public void HashSetWithNullItemsPreservesArrayForEditing()
        {
            // Arrange: Array with null item
            SerializableHashSet<string> set = new()
            {
                _items = new[] { "valid", null, "also-valid" },
            };

            // Expect the error log about null entry being skipped
            LogAssert.Expect(
                LogType.Warning,
                "SerializableSet<System.String> skipped serialized entry at index 1 because the value reference was null."
            );

            // Act
            set.OnAfterDeserialize();

            // Assert
            Assert.IsTrue(
                set.PreserveSerializedEntries,
                "PreserveSerializedEntries should be true after deserialization with nulls"
            );
            Assert.IsTrue(
                set.HasDuplicatesOrNulls,
                "HasDuplicatesOrNulls should be true when null items are present"
            );
            Assert.AreEqual(
                2,
                set.Count,
                $"Count should be 2 (skipping null), but was {set.Count}"
            );
        }

        [Test]
        public void SortedDictionaryAddingExistingKeyUpdatesValuePreservesOrder()
        {
            // Arrange
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };
            dictionary.OnAfterDeserialize();

            // Act: Use indexer to update existing key
            dictionary[1] = "ONE_UPDATED";
            dictionary.OnBeforeSerialize();

            // Assert: Order preserved, value updated
            CollectionAssert.AreEqual(new[] { 3, 1, 2 }, dictionary._keys);
            Assert.AreEqual("three", dictionary._values[0]);
            Assert.AreEqual("ONE_UPDATED", dictionary._values[1]);
            Assert.AreEqual("two", dictionary._values[2]);
        }

        [Test]
        public void SortedDictionaryTryAddPreservesOrderWhenKeyExists()
        {
            // Arrange
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };
            dictionary.OnAfterDeserialize();

            // Act: TryAdd with existing key (should not add)
            bool added = dictionary.TryAdd(1, "should-not-add");
            dictionary.OnBeforeSerialize();

            // Assert: Not added, order preserved
            Assert.IsFalse(added);
            CollectionAssert.AreEqual(new[] { 3, 1, 2 }, dictionary._keys);
            Assert.AreEqual("one", dictionary._values[1]); // Original value
        }

        [Test]
        public void SortedDictionaryRemoveNonExistentKeyPreservesOrder()
        {
            // Arrange
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };
            dictionary.OnAfterDeserialize();

            // Act: Try to remove non-existent key
            bool removed = dictionary.Remove(999);
            dictionary.OnBeforeSerialize();

            // Assert: Not removed, order preserved
            Assert.IsFalse(removed);
            CollectionAssert.AreEqual(new[] { 3, 1, 2 }, dictionary._keys);
        }

        [Test]
        public void HashSetAddDuplicateDoesNotChangeOrder()
        {
            // Arrange
            SerializableHashSet<int> set = new() { _items = new[] { 5, 3, 7 } };
            set.OnAfterDeserialize();

            // Act: Try to add existing item
            bool added = set.Add(3);
            set.OnBeforeSerialize();

            // Assert: Not added, order preserved
            Assert.IsFalse(added);
            CollectionAssert.AreEqual(new[] { 5, 3, 7 }, set._items);
        }

        [Test]
        public void HashSetRemoveNonExistentItemPreservesOrder()
        {
            // Arrange
            SerializableHashSet<int> set = new() { _items = new[] { 5, 3, 7 } };
            set.OnAfterDeserialize();

            // Act: Try to remove non-existent item
            bool removed = set.Remove(999);
            set.OnBeforeSerialize();

            // Assert: Not removed, order preserved
            Assert.IsFalse(removed);
            CollectionAssert.AreEqual(new[] { 5, 3, 7 }, set._items);
        }

        [Test]
        public void SortedDictionaryProtoSerializationPreservesOrder()
        {
            // Arrange
            SerializableSortedDictionary<int, string> original = new()
            {
                _keys = new[] { 30, 10, 20 },
                _values = new[] { "thirty", "ten", "twenty" },
            };
            original.OnAfterDeserialize();

            // Act: Protobuf round-trip
            byte[] bytes = Serializer.ProtoSerialize(original);
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(bytes.Length, 0, "Serialized bytes should not be empty");

            SerializableSortedDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableSortedDictionary<int, string>
            >(bytes);

            // Assert: Order preserved after round-trip
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._keys != null,
                $"Restored _keys should not be null. Serialized {bytes.Length} bytes."
            );
            Assert.IsTrue(
                restored._values != null,
                $"Restored _values should not be null. Serialized {bytes.Length} bytes."
            );
            string actualKeys = string.Join(", ", restored._keys);
            string actualValues = string.Join(", ", restored._values);
            CollectionAssert.AreEqual(
                new[] { 30, 10, 20 },
                restored._keys,
                $"Expected keys [30, 10, 20], got [{actualKeys}]"
            );
            CollectionAssert.AreEqual(
                new[] { "thirty", "ten", "twenty" },
                restored._values,
                $"Expected values [thirty, ten, twenty], got [{actualValues}]"
            );
        }

        [Test]
        public void SortedSetProtoSerializationPreservesOrder()
        {
            // Arrange
            SerializableSortedSet<int> original = new() { _items = new[] { 30, 10, 20 } };
            original.OnAfterDeserialize();

            // Act: Protobuf round-trip
            byte[] bytes = Serializer.ProtoSerialize(original);
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(bytes.Length, 0, "Serialized bytes should not be empty");

            SerializableSortedSet<int> restored = Serializer.ProtoDeserialize<
                SerializableSortedSet<int>
            >(bytes);

            // Assert: Order preserved after round-trip
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._items != null,
                $"Restored _items should not be null. Serialized {bytes.Length} bytes."
            );
            string actualItems = string.Join(", ", restored._items);
            CollectionAssert.AreEqual(
                new[] { 30, 10, 20 },
                restored._items,
                $"Expected [30, 10, 20], got [{actualItems}]"
            );
        }

        [Test]
        public void HashSetProtoSerializationPreservesOrder()
        {
            // Arrange
            SerializableHashSet<int> original = new() { _items = new[] { 7, 3, 9, 1 } };
            original.OnAfterDeserialize();

            // Act: Protobuf round-trip
            byte[] bytes = Serializer.ProtoSerialize(original);
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(bytes.Length, 0, "Serialized bytes should not be empty");

            SerializableHashSet<int> restored = Serializer.ProtoDeserialize<
                SerializableHashSet<int>
            >(bytes);

            // Assert: Order preserved after round-trip
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._items != null,
                $"Restored _items should not be null. Serialized {bytes.Length} bytes."
            );
            string restoredItems = string.Join(", ", restored._items);
            CollectionAssert.AreEqual(
                new[] { 7, 3, 9, 1 },
                restored._items,
                $"Expected [7, 3, 9, 1], got [{restoredItems}]"
            );
        }

        [Test]
        public void SortedDictionaryJsonSerializationPreservesOrder()
        {
            // Arrange
            SerializableSortedDictionary<int, string> original = new()
            {
                _keys = new[] { 30, 10, 20 },
                _values = new[] { "thirty", "ten", "twenty" },
            };
            original.OnAfterDeserialize();

            // Act: JSON round-trip
            string json = Serializer.JsonStringify(original);
            SerializableSortedDictionary<int, string> restored = Serializer.JsonDeserialize<
                SerializableSortedDictionary<int, string>
            >(json);

            // Assert: Order preserved after round-trip
            CollectionAssert.AreEqual(new[] { 30, 10, 20 }, restored._keys);
            CollectionAssert.AreEqual(new[] { "thirty", "ten", "twenty" }, restored._values);
        }

        [Test]
        public void SortedSetJsonSerializationPreservesOrder()
        {
            // Arrange
            SerializableSortedSet<int> original = new() { _items = new[] { 30, 10, 20 } };
            original.OnAfterDeserialize();

            // Act: JSON round-trip
            string json = Serializer.JsonStringify(original);
            SerializableSortedSet<int> restored = Serializer.JsonDeserialize<
                SerializableSortedSet<int>
            >(json);

            // Assert: Order preserved after round-trip
            CollectionAssert.AreEqual(new[] { 30, 10, 20 }, restored._items);
        }

        [Test]
        public void HashSetJsonSerializationPreservesOrder()
        {
            // Arrange
            SerializableHashSet<int> original = new() { _items = new[] { 7, 3, 9, 1 } };
            original.OnAfterDeserialize();

            // Act: JSON round-trip
            string json = Serializer.JsonStringify(original);
            Assert.IsTrue(json != null, "Serialized JSON should not be null");
            Assert.IsNotEmpty(json, "Serialized JSON should not be empty");

            SerializableHashSet<int> restored = Serializer.JsonDeserialize<
                SerializableHashSet<int>
            >(json);

            // Assert: Order preserved after round-trip
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._items != null,
                $"Restored _items should not be null. JSON: {json}"
            );
            string restoredItems = string.Join(", ", restored._items);
            CollectionAssert.AreEqual(
                new[] { 7, 3, 9, 1 },
                restored._items,
                $"Expected [7, 3, 9, 1], got [{restoredItems}]. JSON: {json}"
            );
        }

        [Test]
        public void SortedDictionaryDomainReloadDoesNotReorderKeys()
        {
            // This test specifically verifies the bug fix:
            // Before the fix, domain reloads would cause keys to be reordered to sorted order

            // Arrange: Keys explicitly NOT in sorted order
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 100, 1, 50, 25, 75 },
                _values = new[] { "hundred", "one", "fifty", "twenty-five", "seventy-five" },
            };

            // Sorted order would be: 1, 25, 50, 75, 100
            // We want to preserve: 100, 1, 50, 25, 75

            // Act: Simulate domain reload (multiple cycles)
            for (int i = 0; i < 10; i++)
            {
                dictionary.OnAfterDeserialize();
                dictionary.OnBeforeSerialize();
            }

            // Assert: Order must NOT have changed to sorted order
            int[] expectedOrder = { 100, 1, 50, 25, 75 };
            CollectionAssert.AreEqual(
                expectedOrder,
                dictionary._keys,
                "Keys should preserve user-defined order, not be sorted"
            );
        }

        [Test]
        public void SortedSetDomainReloadDoesNotReorderItems()
        {
            // Arrange: Items explicitly NOT in sorted order
            SerializableSortedSet<int> set = new() { _items = new[] { 100, 1, 50, 25, 75 } };

            // Act: Simulate domain reload (multiple cycles)
            for (int i = 0; i < 10; i++)
            {
                set.OnAfterDeserialize();
                set.OnBeforeSerialize();
            }

            // Assert: Order must NOT have changed to sorted order
            int[] expectedOrder = { 100, 1, 50, 25, 75 };
            CollectionAssert.AreEqual(
                expectedOrder,
                set._items,
                "Items should preserve user-defined order, not be sorted"
            );
        }

        [Test]
        public void HashSetDomainReloadDoesNotReorderItems()
        {
            // Arrange: Items in specific order
            SerializableHashSet<int> set = new() { _items = new[] { 42, 7, 99, 13, 55 } };

            // Act: Simulate domain reload (multiple cycles)
            for (int i = 0; i < 10; i++)
            {
                set.OnAfterDeserialize();
                set.OnBeforeSerialize();
            }

            // Assert: Order preserved
            int[] expectedOrder = { 42, 7, 99, 13, 55 };
            CollectionAssert.AreEqual(
                expectedOrder,
                set._items,
                "Items should preserve user-defined order"
            );
        }

        [Test]
        public void SortedDictionaryStringKeysDomainReloadPreservesOrder()
        {
            // Arrange: String keys NOT in alphabetical order
            SerializableSortedDictionary<string, int> dictionary = new()
            {
                _keys = new[] { "zebra", "apple", "mango", "banana", "cherry" },
                _values = new[] { 1, 2, 3, 4, 5 },
            };

            // Alphabetical would be: apple, banana, cherry, mango, zebra
            // We want to preserve: zebra, apple, mango, banana, cherry

            // Act
            for (int i = 0; i < 5; i++)
            {
                dictionary.OnAfterDeserialize();
                dictionary.OnBeforeSerialize();
            }

            // Assert
            string[] expectedOrder = { "zebra", "apple", "mango", "banana", "cherry" };
            CollectionAssert.AreEqual(
                expectedOrder,
                dictionary._keys,
                "String keys should preserve user-defined order, not alphabetical"
            );
        }

        [Test]
        public void SortedSetStringItemsDomainReloadPreservesOrder()
        {
            // Arrange
            SerializableSortedSet<string> set = new()
            {
                _items = new[] { "zebra", "apple", "mango", "banana", "cherry" },
            };

            // Act
            for (int i = 0; i < 5; i++)
            {
                set.OnAfterDeserialize();
                set.OnBeforeSerialize();
            }

            // Assert
            string[] expectedOrder = { "zebra", "apple", "mango", "banana", "cherry" };
            CollectionAssert.AreEqual(
                expectedOrder,
                set._items,
                "String items should preserve user-defined order, not alphabetical"
            );
        }

        /// <summary>
        /// Test cases for HashSet mutation scenarios that should preserve order.
        /// Format: (initialItems, itemsToRemove, itemsToAdd, expectedOrder)
        /// </summary>
        private static IEnumerable<TestCaseData> HashSetMutationTestCases()
        {
            yield return new TestCaseData(
                new[] { 1, 2, 3, 4, 5 },
                new[] { 2, 4 },
                new[] { 6 },
                new[] { 1, 3, 5, 6 }
            ).SetName("Remove middle items, add one");

            yield return new TestCaseData(
                new[] { 10, 20, 30 },
                new int[0],
                new[] { 5, 25 },
                new[] { 10, 20, 30, 5, 25 }
            ).SetName("Add items without removing");

            yield return new TestCaseData(
                new[] { 100, 50, 75 },
                new[] { 100, 75 },
                new int[0],
                new[] { 50 }
            ).SetName("Remove items without adding");

            yield return new TestCaseData(
                new[] { 1, 2, 3, 4, 5 },
                new[] { 1, 2, 3, 4, 5 },
                new[] { 10, 20 },
                new[] { 10, 20 }
            ).SetName("Remove all, add new items");

            yield return new TestCaseData(
                new[] { 5, 3, 7, 1, 9 },
                new[] { 5, 7 },
                new[] { 2, 8 },
                new[] { 3, 1, 9, 2, 8 }
            ).SetName("Non-sorted initial order preserved");
        }

        [Test]
        [TestCaseSource(nameof(HashSetMutationTestCases))]
        public void HashSetMutationPreservesOrder(
            int[] initial,
            int[] toRemove,
            int[] toAdd,
            int[] expected
        )
        {
            // Arrange
            SerializableHashSet<int> set = new() { _items = initial };
            set.OnAfterDeserialize();

            // Act: Apply mutations
            foreach (int item in toRemove)
            {
                set.Remove(item);
            }
            foreach (int item in toAdd)
            {
                set.Add(item);
            }
            set.OnBeforeSerialize();

            // Assert
            string actualItems = set._items != null ? string.Join(", ", set._items) : "null";
            string expectedItems = string.Join(", ", expected);
            CollectionAssert.AreEqual(
                expected,
                set._items,
                $"Expected [{expectedItems}], got [{actualItems}]"
            );
        }

        /// <summary>
        /// Test cases for SortedDictionary mutation scenarios that should preserve order.
        /// Format: (initialKeys, initialValues, keysToRemove, keysToAdd, valuesToAdd, expectedKeys)
        /// </summary>
        private static IEnumerable<TestCaseData> SortedDictionaryMutationTestCases()
        {
            yield return new TestCaseData(
                new[] { 30, 10, 20 },
                new[] { "thirty", "ten", "twenty" },
                new[] { 10 },
                new[] { 15 },
                new[] { "fifteen" },
                new[] { 30, 20, 15 }
            ).SetName("Remove and add key");

            yield return new TestCaseData(
                new[] { 5, 3, 1 },
                new[] { "five", "three", "one" },
                new int[0],
                new[] { 2, 4 },
                new[] { "two", "four" },
                new[] { 5, 3, 1, 2, 4 }
            ).SetName("Add keys without removing");

            yield return new TestCaseData(
                new[] { 100, 50, 25, 75 },
                new[] { "a", "b", "c", "d" },
                new[] { 50, 75 },
                new int[0],
                new string[0],
                new[] { 100, 25 }
            ).SetName("Remove keys without adding");
        }

        [Test]
        [TestCaseSource(nameof(SortedDictionaryMutationTestCases))]
        public void SortedDictionaryMutationPreservesOrder(
            int[] initialKeys,
            string[] initialValues,
            int[] keysToRemove,
            int[] keysToAdd,
            string[] valuesToAdd,
            int[] expectedKeys
        )
        {
            // Arrange
            SerializableSortedDictionary<int, string> dict = new()
            {
                _keys = initialKeys,
                _values = initialValues,
            };
            dict.OnAfterDeserialize();

            // Act: Apply mutations
            foreach (int key in keysToRemove)
            {
                dict.Remove(key);
            }
            for (int i = 0; i < keysToAdd.Length; i++)
            {
                dict.Add(keysToAdd[i], valuesToAdd[i]);
            }
            dict.OnBeforeSerialize();

            // Assert
            string actualKeys = dict._keys != null ? string.Join(", ", dict._keys) : "null";
            string expected = string.Join(", ", expectedKeys);
            CollectionAssert.AreEqual(
                expectedKeys,
                dict._keys,
                $"Expected keys [{expected}], got [{actualKeys}]"
            );
        }

        [Test]
        public void HashSetMutationThenProtoSerializationPreservesOrder()
        {
            // Arrange
            SerializableHashSet<int> set = new() { _items = new[] { 10, 20, 30, 40, 50 } };
            set.OnAfterDeserialize();

            // Act: Mutate then serialize
            set.Remove(20);
            set.Remove(40);
            set.Add(25);
            byte[] bytes = Serializer.ProtoSerialize(set);

            // Assert: Bytes were serialized
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(bytes.Length, 0, "Serialized bytes should not be empty");

            SerializableHashSet<int> restored = Serializer.ProtoDeserialize<
                SerializableHashSet<int>
            >(bytes);

            // Assert: Restored correctly
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._items != null,
                $"Restored _items should not be null. Serialized {bytes.Length} bytes."
            );
            int[] expected = { 10, 30, 50, 25 };
            string actualItems = string.Join(", ", restored._items);
            CollectionAssert.AreEqual(
                expected,
                restored._items,
                $"Expected [10, 30, 50, 25], got [{actualItems}]"
            );
        }

        [Test]
        public void HashSetMutationThenJsonSerializationPreservesOrder()
        {
            // Arrange
            SerializableHashSet<int> set = new() { _items = new[] { 10, 20, 30, 40, 50 } };
            set.OnAfterDeserialize();

            // Act: Mutate then serialize
            set.Remove(20);
            set.Remove(40);
            set.Add(25);
            string json = Serializer.JsonStringify(set);
            SerializableHashSet<int> restored = Serializer.JsonDeserialize<
                SerializableHashSet<int>
            >(json);

            // Assert
            Assert.IsTrue(
                restored._items != null,
                $"Restored _items should not be null. JSON: {json}"
            );
            int[] expected = { 10, 30, 50, 25 };
            string actualItems = string.Join(", ", restored._items);
            CollectionAssert.AreEqual(
                expected,
                restored._items,
                $"Expected [10, 30, 50, 25], got [{actualItems}]. JSON: {json}"
            );
        }

        [Test]
        public void HashSetMultipleSerializeCyclesAfterMutationPreservesOrder()
        {
            // Arrange
            SerializableHashSet<int> set = new() { _items = new[] { 100, 50, 75, 25 } };
            set.OnAfterDeserialize();

            // Act: Mutate
            set.Remove(50);
            set.Add(60);

            // Then run multiple serialize/deserialize cycles
            for (int i = 0; i < 5; i++)
            {
                set.OnBeforeSerialize();
                set.OnAfterDeserialize();
            }

            // Assert: Order should remain stable
            int[] expected = { 100, 75, 25, 60 };
            string actualItems = set._items != null ? string.Join(", ", set._items) : "null";
            CollectionAssert.AreEqual(
                expected,
                set._items,
                $"Expected [100, 75, 25, 60], got [{actualItems}]"
            );
        }

        [Test]
        public void SortedDictionaryMutationThenProtoSerializationPreservesOrder()
        {
            // Arrange
            SerializableSortedDictionary<int, string> dict = new()
            {
                _keys = new[] { 30, 10, 20, 40 },
                _values = new[] { "thirty", "ten", "twenty", "forty" },
            };
            dict.OnAfterDeserialize();

            // Act: Mutate then serialize
            dict.Remove(10);
            dict.Add(15, "fifteen");
            byte[] bytes = Serializer.ProtoSerialize(dict);

            // Assert: Bytes were serialized
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(bytes.Length, 0, "Serialized bytes should not be empty");

            SerializableSortedDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableSortedDictionary<int, string>
            >(bytes);

            // Assert: Restored correctly
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._keys != null,
                $"Restored _keys should not be null. Serialized {bytes.Length} bytes."
            );
            Assert.IsTrue(
                restored._values != null,
                $"Restored _values should not be null. Serialized {bytes.Length} bytes."
            );
            int[] expected = { 30, 20, 40, 15 };
            string actualKeys = string.Join(", ", restored._keys);
            CollectionAssert.AreEqual(
                expected,
                restored._keys,
                $"Expected keys [30, 20, 40, 15], got [{actualKeys}]"
            );
        }

        [Test]
        public void SortedDictionaryMutationThenJsonSerializationPreservesOrder()
        {
            // Arrange
            SerializableSortedDictionary<int, string> dict = new()
            {
                _keys = new[] { 30, 10, 20, 40 },
                _values = new[] { "thirty", "ten", "twenty", "forty" },
            };
            dict.OnAfterDeserialize();

            // Act: Mutate then serialize
            dict.Remove(10);
            dict.Add(15, "fifteen");
            string json = Serializer.JsonStringify(dict);
            SerializableSortedDictionary<int, string> restored = Serializer.JsonDeserialize<
                SerializableSortedDictionary<int, string>
            >(json);

            // Assert
            Assert.IsTrue(
                restored._keys != null,
                $"Restored _keys should not be null. JSON: {json}"
            );
            int[] expected = { 30, 20, 40, 15 };
            string actualKeys = string.Join(", ", restored._keys);
            CollectionAssert.AreEqual(
                expected,
                restored._keys,
                $"Expected keys [30, 20, 40, 15], got [{actualKeys}]. JSON: {json}"
            );
        }

        [Test]
        public void HashSetClearThenAddPreservesNewOrder()
        {
            // Arrange
            SerializableHashSet<int> set = new() { _items = new[] { 1, 2, 3, 4, 5 } };
            set.OnAfterDeserialize();

            // Act: Clear and add new items
            set.Clear();
            set.Add(100);
            set.Add(50);
            set.Add(75);
            set.OnBeforeSerialize();

            // Assert: New items should be in the order they were added
            string actualItems = set._items != null ? string.Join(", ", set._items) : "null";
            Assert.AreEqual(
                3,
                set._items.Length,
                $"Expected 3 items after clear and add, got {set._items.Length}. Items: [{actualItems}]"
            );
            // With our fix, items should be in exact insertion order
            CollectionAssert.AreEqual(
                new[] { 100, 50, 75 },
                set._items,
                $"Expected items in insertion order [100, 50, 75], got [{actualItems}]"
            );
        }

        [Test]
        public void HashSetAddMultipleItemsPreservesInsertionOrder()
        {
            // Arrange: Empty set
            SerializableHashSet<int> set = new();
            set.OnAfterDeserialize();

            // Act: Add items in specific order
            set.Add(7);
            set.Add(3);
            set.Add(9);
            set.Add(1);
            set.OnBeforeSerialize();

            // Assert: Items should be in exact insertion order
            string actualItems = set._items != null ? string.Join(", ", set._items) : "null";
            int[] expected = { 7, 3, 9, 1 };
            CollectionAssert.AreEqual(
                expected,
                set._items,
                $"Expected items in insertion order [7, 3, 9, 1], got [{actualItems}]"
            );
        }

        [Test]
        public void HashSetAddAfterDeserializePreservesExistingThenNewOrder()
        {
            // Arrange: Set with existing items
            SerializableHashSet<int> set = new() { _items = new[] { 100, 50, 200 } };
            set.OnAfterDeserialize();

            // Act: Add new items in specific order
            set.Add(25);
            set.Add(150);
            set.OnBeforeSerialize();

            // Assert: Existing items first, then new items in insertion order
            string actualItems = set._items != null ? string.Join(", ", set._items) : "null";
            int[] expected = { 100, 50, 200, 25, 150 };
            CollectionAssert.AreEqual(
                expected,
                set._items,
                $"Expected existing items + new items in order [100, 50, 200, 25, 150], got [{actualItems}]"
            );
        }

        [Test]
        public void HashSetInterleavedAddRemovePreservesOrder()
        {
            // Arrange
            SerializableHashSet<int> set = new() { _items = new[] { 10, 20, 30, 40, 50 } };
            set.OnAfterDeserialize();

            // Act: Interleaved add/remove operations
            set.Remove(20);
            set.Add(25);
            set.Remove(40);
            set.Add(45);
            set.Add(15);
            set.OnBeforeSerialize();

            // Assert: Remaining original items preserve order, new items in insertion order
            string actualItems = set._items != null ? string.Join(", ", set._items) : "null";
            int[] expected = { 10, 30, 50, 25, 45, 15 };
            CollectionAssert.AreEqual(
                expected,
                set._items,
                $"Expected [10, 30, 50, 25, 45, 15], got [{actualItems}]"
            );
        }

        [Test]
        public void HashSetJsonSerializationProducesObjectFormat()
        {
            // This test verifies the JSON format is correct (object with _items property)
            // Arrange
            SerializableHashSet<int> original = new() { _items = new[] { 7, 3, 9, 1 } };
            original.OnAfterDeserialize();

            // Act
            string json = Serializer.JsonStringify(original);

            // Assert: JSON should be an object with _items property, not just an array
            Assert.IsTrue(
                json.Contains("_items"),
                $"JSON should contain '_items' property. Got: {json}"
            );
            Assert.IsTrue(
                json.StartsWith("{"),
                $"JSON should start with '{{' (object format). Got: {json}"
            );
            Assert.IsFalse(
                json.StartsWith("["),
                $"JSON should NOT start with '[' (array format). Got: {json}"
            );
        }

        [Test]
        public void SortedDictionaryJsonSerializationProducesObjectFormat()
        {
            // This test verifies the JSON format is correct (object with _keys and _values)
            // Arrange
            SerializableSortedDictionary<int, string> original = new()
            {
                _keys = new[] { 30, 10, 20 },
                _values = new[] { "thirty", "ten", "twenty" },
            };
            original.OnAfterDeserialize();

            // Act
            string json = Serializer.JsonStringify(original);

            // Assert: JSON should contain _keys and _values properties
            Assert.IsTrue(
                json.Contains("_keys"),
                $"JSON should contain '_keys' property. Got: {json}"
            );
            Assert.IsTrue(
                json.Contains("_values"),
                $"JSON should contain '_values' property. Got: {json}"
            );
            Assert.IsTrue(
                json.StartsWith("{"),
                $"JSON should start with '{{' (object format). Got: {json}"
            );
        }

        [Test]
        public void HashSetUnionWithPreservesExistingAndAddsInOrder()
        {
            // Arrange
            SerializableHashSet<int> set = new() { _items = new[] { 10, 20, 30 } };
            set.OnAfterDeserialize();

            // Act: UnionWith items in specific order
            set.UnionWith(new[] { 5, 25, 35 });
            set.OnBeforeSerialize();

            // Assert: Existing items first, then new items in the order they appear in UnionWith
            string actualItems = set._items != null ? string.Join(", ", set._items) : "null";
            int[] expected = { 10, 20, 30, 5, 25, 35 };
            CollectionAssert.AreEqual(
                expected,
                set._items,
                $"Expected [10, 20, 30, 5, 25, 35], got [{actualItems}]"
            );
        }

        [Test]
        public void SortedDictionaryUpdateValuePreservesKeyOrder()
        {
            // Arrange
            SerializableSortedDictionary<int, string> dict = new()
            {
                _keys = new[] { 30, 10, 20 },
                _values = new[] { "thirty", "ten", "twenty" },
            };
            dict.OnAfterDeserialize();

            // Act: Update existing value
            dict[10] = "TEN_UPDATED";
            dict.OnBeforeSerialize();

            // Assert: Key order should be preserved, value updated
            CollectionAssert.AreEqual(
                new[] { 30, 10, 20 },
                dict._keys,
                "Key order should be preserved after value update"
            );
            Assert.AreEqual("TEN_UPDATED", dict[10], "Value should be updated");
        }

        private static IEnumerable<TestCaseData> HashSetProtoSerializationTestCases()
        {
            yield return new TestCaseData(new[] { 1 }).SetName("SingleElement");
            yield return new TestCaseData(new[] { 5, 3, 8, 1, 9 }).SetName(
                "MultipleElements.Unordered"
            );
            yield return new TestCaseData(new[] { 1, 2, 3, 4, 5 }).SetName(
                "MultipleElements.Ascending"
            );
            yield return new TestCaseData(new[] { 5, 4, 3, 2, 1 }).SetName(
                "MultipleElements.Descending"
            );
            yield return new TestCaseData(new[] { 100, 1, 50, -10, 25 }).SetName(
                "MixedPositiveNegative"
            );
            yield return new TestCaseData(new[] { int.MaxValue, int.MinValue, 0 }).SetName(
                "ExtremeBoundaryValues"
            );
            yield return new TestCaseData(Enumerable.Range(0, 100).Reverse().ToArray()).SetName(
                "LargeArray.100Elements"
            );
        }

        [TestCaseSource(nameof(HashSetProtoSerializationTestCases))]
        public void HashSetProtoSerializationPreservesOrderDataDriven(int[] items)
        {
            // Arrange
            SerializableHashSet<int> original = new() { _items = items };
            original.OnAfterDeserialize();

            // Diagnostic: Verify original state
            string originalItemsStr =
                original._items != null ? string.Join(", ", original._items) : "null";
            Assert.IsTrue(
                original._items != null,
                $"Original _items should not be null before serialization"
            );

            // Act
            byte[] bytes = Serializer.ProtoSerialize(original);

            // Assert: Bytes were serialized
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(
                bytes.Length,
                0,
                $"Serialized bytes should not be empty for {items.Length} items"
            );

            // Diagnostic: Show hex dump of first bytes
            string hexDump =
                bytes.Length > 0
                    ? string.Join(" ", bytes.Take(20).Select(b => b.ToString("X2")))
                    : "empty";

            SerializableHashSet<int> restored = Serializer.ProtoDeserialize<
                SerializableHashSet<int>
            >(bytes);

            // Assert: Restored correctly
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._items != null,
                $"Restored _items should not be null. Serialized {bytes.Length} bytes for {items.Length} items. "
                    + $"Original=[{originalItemsStr}]. Hex={hexDump}"
            );
            string expectedStr = string.Join(", ", items);
            string actualStr = string.Join(", ", restored._items);
            CollectionAssert.AreEqual(
                items,
                restored._items,
                $"Expected [{expectedStr}], got [{actualStr}]"
            );
        }

        private static IEnumerable<TestCaseData> SortedSetProtoSerializationTestCases()
        {
            yield return new TestCaseData(new[] { 42 }).SetName("SingleElement");
            yield return new TestCaseData(new[] { 100, 50, 75, 25 }).SetName(
                "MultipleElements.Unordered"
            );
            yield return new TestCaseData(new[] { 10, 20, 30, 40 }).SetName(
                "MultipleElements.Ascending"
            );
            yield return new TestCaseData(new[] { 40, 30, 20, 10 }).SetName(
                "MultipleElements.Descending"
            );
            yield return new TestCaseData(new[] { 0, -1, 1, -100, 100 }).SetName(
                "MixedPositiveNegative"
            );
        }

        [TestCaseSource(nameof(SortedSetProtoSerializationTestCases))]
        public void SortedSetProtoSerializationPreservesOrderDataDriven(int[] items)
        {
            // Arrange
            SerializableSortedSet<int> original = new() { _items = items };
            original.OnAfterDeserialize();

            // Act
            byte[] bytes = Serializer.ProtoSerialize(original);

            // Assert: Bytes were serialized
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(
                bytes.Length,
                0,
                $"Serialized bytes should not be empty for {items.Length} items"
            );

            SerializableSortedSet<int> restored = Serializer.ProtoDeserialize<
                SerializableSortedSet<int>
            >(bytes);

            // Assert: Restored correctly
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._items != null,
                $"Restored _items should not be null. Serialized {bytes.Length} bytes for {items.Length} items."
            );
            string expectedStr = string.Join(", ", items);
            string actualStr = string.Join(", ", restored._items);
            CollectionAssert.AreEqual(
                items,
                restored._items,
                $"Expected [{expectedStr}], got [{actualStr}]"
            );
        }

        private static IEnumerable<TestCaseData> SortedDictionaryProtoSerializationTestCases()
        {
            yield return new TestCaseData(new[] { 1 }, new[] { "one" }).SetName("SingleEntry");
            yield return new TestCaseData(
                new[] { 30, 10, 20 },
                new[] { "thirty", "ten", "twenty" }
            ).SetName("MultipleEntries Unordered");
            yield return new TestCaseData(
                new[] { 1, 2, 3, 4, 5 },
                new[] { "one", "two", "three", "four", "five" }
            ).SetName("MultipleEntries Ascending");
            yield return new TestCaseData(
                new[] { 5, 4, 3, 2, 1 },
                new[] { "five", "four", "three", "two", "one" }
            ).SetName("MultipleEntries Descending");
            yield return new TestCaseData(
                new[] { 100, -50, 0, 25, -25 },
                new[] { "hundred", "neg-fifty", "zero", "twenty-five", "neg-twenty-five" }
            ).SetName("MixedPositiveNegative");
        }

        [TestCaseSource(nameof(SortedDictionaryProtoSerializationTestCases))]
        public void SortedDictionaryProtoSerializationPreservesOrderDataDriven(
            int[] keys,
            string[] values
        )
        {
            // Arrange
            SerializableSortedDictionary<int, string> original = new()
            {
                _keys = keys,
                _values = values,
            };
            original.OnAfterDeserialize();

            // Act
            byte[] bytes = Serializer.ProtoSerialize(original);

            // Assert: Bytes were serialized
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(
                bytes.Length,
                0,
                $"Serialized bytes should not be empty for {keys.Length} entries"
            );

            SerializableSortedDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableSortedDictionary<int, string>
            >(bytes);

            // Assert: Restored correctly
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._keys != null,
                $"Restored _keys should not be null. Serialized {bytes.Length} bytes for {keys.Length} entries."
            );
            Assert.IsTrue(
                restored._values != null,
                $"Restored _values should not be null. Serialized {bytes.Length} bytes for {keys.Length} entries."
            );
            string expectedKeys = string.Join(", ", keys);
            string actualKeys = string.Join(", ", restored._keys);
            string expectedValues = string.Join(", ", values);
            string actualValues = string.Join(", ", restored._values);
            CollectionAssert.AreEqual(
                keys,
                restored._keys,
                $"Expected keys [{expectedKeys}], got [{actualKeys}]"
            );
            CollectionAssert.AreEqual(
                values,
                restored._values,
                $"Expected values [{expectedValues}], got [{actualValues}]"
            );
        }

        [Test]
        public void EmptyHashSetProtoSerializationRoundTrips()
        {
            // Arrange
            SerializableHashSet<int> original = new();
            original.OnAfterDeserialize();

            // Act
            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableHashSet<int> restored = Serializer.ProtoDeserialize<
                SerializableHashSet<int>
            >(bytes);

            // Assert
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.AreEqual(0, restored.Count, "Restored set should be empty");
        }

        [Test]
        public void EmptySortedSetProtoSerializationRoundTrips()
        {
            // Arrange
            SerializableSortedSet<int> original = new();
            original.OnAfterDeserialize();

            // Act
            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableSortedSet<int> restored = Serializer.ProtoDeserialize<
                SerializableSortedSet<int>
            >(bytes);

            // Assert
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.AreEqual(0, restored.Count, "Restored set should be empty");
        }

        [Test]
        public void EmptySortedDictionaryProtoSerializationRoundTrips()
        {
            // Arrange
            SerializableSortedDictionary<int, string> original = new();
            original.OnAfterDeserialize();

            // Act
            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableSortedDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableSortedDictionary<int, string>
            >(bytes);

            // Assert
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.AreEqual(0, restored.Count, "Restored dictionary should be empty");
        }

        private static IEnumerable<TestCaseData> ProtoSerializationDiagnosticTestCases()
        {
            yield return new TestCaseData(new[] { 42 }).SetName("SingleInt");
            yield return new TestCaseData(new[] { 1, 2, 3 }).SetName("ThreeInts");
            yield return new TestCaseData(new[] { int.MaxValue, int.MinValue, 0 }).SetName(
                "BoundaryInts"
            );
            yield return new TestCaseData(new[] { -1, -2, -3, -4, -5 }).SetName("NegativeInts");
            yield return new TestCaseData(Enumerable.Range(1, 50).ToArray()).SetName("FiftyInts");
        }

        [TestCaseSource(nameof(ProtoSerializationDiagnosticTestCases))]
        public void HashSetProtoSerializationDiagnosticVerifiesInternalState(int[] items)
        {
            // Arrange: Create set with explicit _items assignment
            SerializableHashSet<int> original = new() { _items = items };
            original.OnAfterDeserialize();

            // Verify pre-serialization state
            Assert.IsTrue(
                original._items != null,
                $"Original _items should not be null before serialization. Count={original.Count}"
            );
            Assert.AreEqual(
                items.Length,
                original._items.Length,
                $"Original _items.Length should match. Expected={items.Length}, Actual={original._items.Length}"
            );

            // Act: Serialize
            byte[] bytes = Serializer.ProtoSerialize(original);

            // Diagnostic: Verify bytes were produced
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(
                bytes.Length,
                0,
                $"Serialized bytes should not be empty for {items.Length} items"
            );

            // Act: Deserialize
            SerializableHashSet<int> restored = Serializer.ProtoDeserialize<
                SerializableHashSet<int>
            >(bytes);

            // Assert: Verify restored state with comprehensive diagnostics
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._items != null,
                $"Restored _items should not be null. Serialized {bytes.Length} bytes for {items.Length} items. "
                    + $"Original._items was [{string.Join(", ", original._items)}]"
            );
            Assert.AreEqual(
                items.Length,
                restored._items.Length,
                $"Restored _items.Length mismatch. Expected={items.Length}, Actual={restored._items.Length}. "
                    + $"Original=[{string.Join(", ", original._items)}], Restored=[{string.Join(", ", restored._items)}]"
            );
            CollectionAssert.AreEqual(
                items,
                restored._items,
                $"Items should match exactly. Expected=[{string.Join(", ", items)}], Got=[{string.Join(", ", restored._items)}]"
            );
        }

        [TestCaseSource(nameof(ProtoSerializationDiagnosticTestCases))]
        public void SortedSetProtoSerializationDiagnosticVerifiesInternalState(int[] items)
        {
            // Arrange: Create set with explicit _items assignment
            SerializableSortedSet<int> original = new() { _items = items };
            original.OnAfterDeserialize();

            // Verify pre-serialization state
            Assert.IsTrue(
                original._items != null,
                $"Original _items should not be null before serialization. Count={original.Count}"
            );
            Assert.AreEqual(
                items.Length,
                original._items.Length,
                $"Original _items.Length should match. Expected={items.Length}, Actual={original._items.Length}"
            );

            // Act: Serialize
            byte[] bytes = Serializer.ProtoSerialize(original);

            // Diagnostic: Verify bytes were produced
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(
                bytes.Length,
                0,
                $"Serialized bytes should not be empty for {items.Length} items"
            );

            // Act: Deserialize
            SerializableSortedSet<int> restored = Serializer.ProtoDeserialize<
                SerializableSortedSet<int>
            >(bytes);

            // Assert: Verify restored state with comprehensive diagnostics
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._items != null,
                $"Restored _items should not be null. Serialized {bytes.Length} bytes for {items.Length} items. "
                    + $"Original._items was [{string.Join(", ", original._items)}]"
            );
            Assert.AreEqual(
                items.Length,
                restored._items.Length,
                $"Restored _items.Length mismatch. Expected={items.Length}, Actual={restored._items.Length}. "
                    + $"Original=[{string.Join(", ", original._items)}], Restored=[{string.Join(", ", restored._items)}]"
            );
            CollectionAssert.AreEqual(
                items,
                restored._items,
                $"Items should match exactly. Expected=[{string.Join(", ", items)}], Got=[{string.Join(", ", restored._items)}]"
            );
        }

        private static IEnumerable<TestCaseData> DictionaryProtoSerializationDiagnosticTestCases()
        {
            yield return new TestCaseData(new[] { 1 }, new[] { "one" }).SetName("SingleEntry");
            yield return new TestCaseData(
                new[] { 1, 2, 3 },
                new[] { "one", "two", "three" }
            ).SetName("ThreeEntries");
            yield return new TestCaseData(
                new[] { int.MaxValue, int.MinValue, 0 },
                new[] { "max", "min", "zero" }
            ).SetName("BoundaryKeys");
            yield return new TestCaseData(
                Enumerable.Range(1, 20).ToArray(),
                Enumerable.Range(1, 20).Select(i => $"value{i}").ToArray()
            ).SetName("TwentyEntries");
        }

        [Test]
        public void DictionaryPreservesSerializedKeyOrderAcrossSerializationCycle()
        {
            // Arrange: Create a dictionary with keys in descending order
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };

            // Act: Simulate domain reload - deserialize then serialize
            dictionary.OnAfterDeserialize();
            dictionary.OnBeforeSerialize();

            // Assert: Serialized keys should maintain original order
            int[] expectedKeys = { 3, 1, 2 };
            CollectionAssert.AreEqual(expectedKeys, dictionary._keys);
        }

        [Test]
        public void DictionaryPreservesSerializedKeyOrderAfterMultipleSerializationCycles()
        {
            // Arrange: Create a dictionary with custom key order
            SerializableDictionary<string, int> dictionary = new()
            {
                _keys = new[] { "zebra", "apple", "mango" },
                _values = new[] { 1, 2, 3 },
            };

            // Act: Simulate multiple domain reloads
            for (int cycle = 0; cycle < 5; cycle++)
            {
                dictionary.OnAfterDeserialize();
                dictionary.OnBeforeSerialize();
            }

            // Assert: Order should be maintained across all cycles
            string[] expectedKeys = { "zebra", "apple", "mango" };
            CollectionAssert.AreEqual(expectedKeys, dictionary._keys);
        }

        [Test]
        public void DictionaryPreservesOrderWhenValueIsUpdated()
        {
            // Arrange: Create dictionary with specific order
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 5, 2, 8, 1 },
                _values = new[] { "five", "two", "eight", "one" },
            };
            dictionary.OnAfterDeserialize();

            // Act: Update a value (this marks the cache dirty)
            dictionary[2] = "TWO_UPDATED";
            dictionary.OnBeforeSerialize();

            // Assert: Key order should be preserved, only value changed
            int[] expectedKeys = { 5, 2, 8, 1 };
            CollectionAssert.AreEqual(expectedKeys, dictionary._keys);
            Assert.AreEqual("TWO_UPDATED", dictionary._values[1]);
        }

        [Test]
        public void DictionaryAppendsNewKeysAtEndWhilePreservingExistingOrder()
        {
            // Arrange: Create dictionary with specific order
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 10, 5, 20 },
                _values = new[] { "ten", "five", "twenty" },
            };
            dictionary.OnAfterDeserialize();

            // Act: Add a new key (this marks the cache dirty)
            dictionary.Add(1, "one");
            dictionary.OnBeforeSerialize();

            // Assert: Existing keys maintain order, new key appended at end
            Assert.AreEqual(4, dictionary._keys.Length);
            Assert.AreEqual(10, dictionary._keys[0]);
            Assert.AreEqual(5, dictionary._keys[1]);
            Assert.AreEqual(20, dictionary._keys[2]);
            Assert.AreEqual(1, dictionary._keys[3]); // New key at end
        }

        [Test]
        public void DictionaryRemovesKeyWhilePreservingRemainingOrder()
        {
            // Arrange: Create dictionary with specific order
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 10, 5, 20, 15 },
                _values = new[] { "ten", "five", "twenty", "fifteen" },
            };
            dictionary.OnAfterDeserialize();

            // Act: Remove a key from the middle
            bool removed = dictionary.Remove(5);
            dictionary.OnBeforeSerialize();

            // Assert: Remaining keys maintain original relative order
            Assert.IsTrue(removed);
            int[] expectedKeys = { 10, 20, 15 };
            CollectionAssert.AreEqual(expectedKeys, dictionary._keys);
        }

        [Test]
        public void DictionaryClearResultsInEmptyArraysOnNextSerialize()
        {
            // Arrange: Create dictionary with specific order
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };
            dictionary.OnAfterDeserialize();

            // Act: Clear the dictionary
            dictionary.Clear();
            dictionary.OnBeforeSerialize();

            // Assert: Arrays should be empty
            Assert.AreEqual(0, dictionary._keys.Length);
            Assert.AreEqual(0, dictionary._values.Length);
        }

        [Test]
        public void DictionaryWithDuplicateKeysPreservesOriginalArrayOnDeserialization()
        {
            // Arrange: Create arrays with duplicate keys (invalid but possible in inspector)
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 1, 2, 1, 3 },
                _values = new[] { "one-first", "two", "one-second", "three" },
            };

            // Act: Deserialize
            dictionary.OnAfterDeserialize();

            // Assert: Original arrays should be preserved (for editing in inspector)
            Assert.IsTrue(dictionary.PreserveSerializedEntries);
            Assert.IsTrue(dictionary.HasDuplicatesOrNulls);
            CollectionAssert.AreEqual(new[] { 1, 2, 1, 3 }, dictionary._keys);
        }

        [Test]
        public void DictionaryIndexerUpdatesValueInPlacePreservingKeyPosition()
        {
            // Arrange
            SerializableDictionary<string, int> dictionary = new()
            {
                _keys = new[] { "z", "a", "m" },
                _values = new[] { 1, 2, 3 },
            };
            dictionary.OnAfterDeserialize();

            // Act: Update multiple values
            dictionary["z"] = 100;
            dictionary["a"] = 200;
            dictionary.OnBeforeSerialize();

            // Assert: Key order preserved, values updated
            CollectionAssert.AreEqual(new[] { "z", "a", "m" }, dictionary._keys);
            Assert.AreEqual(100, dictionary._values[0]);
            Assert.AreEqual(200, dictionary._values[1]);
            Assert.AreEqual(3, dictionary._values[2]);
        }

        [Test]
        public void DictionaryComplexScenarioAddRemoveUpdatePreservesOrder()
        {
            // Arrange: Start with ordered dictionary
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 100, 50, 200, 75 },
                _values = new[] { "hundred", "fifty", "two-hundred", "seventy-five" },
            };
            dictionary.OnAfterDeserialize();

            // Act: Complex operations
            dictionary[50] = "FIFTY_UPDATED"; // Update
            dictionary.Remove(200); // Remove from middle
            dictionary.Add(25, "twenty-five"); // Add new
            dictionary.Add(300, "three-hundred"); // Add another new
            dictionary.OnBeforeSerialize();

            // Assert: Original order preserved for remaining keys, new keys appended in insertion order
            string actualKeys =
                dictionary._keys != null ? string.Join(", ", dictionary._keys) : "null";
            int[] expectedKeys = { 100, 50, 75, 25, 300 };
            CollectionAssert.AreEqual(
                expectedKeys,
                dictionary._keys,
                $"Expected keys [100, 50, 75, 25, 300], got [{actualKeys}]"
            );
            Assert.AreEqual("FIFTY_UPDATED", dictionary._values[1]);
        }

        [Test]
        public void EmptyDictionarySerializesToEmptyArrays()
        {
            // Arrange
            SerializableDictionary<int, string> dictionary = new();

            // Act
            dictionary.OnBeforeSerialize();

            // Assert
            Assert.IsTrue(dictionary._keys != null);
            Assert.IsTrue(dictionary._values != null);
            Assert.AreEqual(0, dictionary._keys.Length);
            Assert.AreEqual(0, dictionary._values.Length);
        }

        [Test]
        public void SingleItemDictionaryPreservesOrder()
        {
            // Arrange
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 42 },
                _values = new[] { "answer" },
            };

            // Act
            dictionary.OnAfterDeserialize();
            dictionary.OnBeforeSerialize();

            // Assert
            Assert.AreEqual(1, dictionary._keys.Length);
            Assert.AreEqual(42, dictionary._keys[0]);
            Assert.AreEqual("answer", dictionary._values[0]);
        }

        [Test]
        public void LargeDictionaryPreservesOrderAcrossManyCycles()
        {
            // Arrange: Create a large dictionary with reverse-sorted keys
            int count = 1000;
            int[] keys = new int[count];
            string[] values = new string[count];
            for (int i = 0; i < count; i++)
            {
                keys[i] = count - i; // Reverse order: 1000, 999, 998, ...
                values[i] = $"value_{count - i}";
            }

            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = keys,
                _values = values,
            };

            // Act: Multiple cycles
            for (int cycle = 0; cycle < 3; cycle++)
            {
                dictionary.OnAfterDeserialize();
                dictionary.OnBeforeSerialize();
            }

            // Assert: Order preserved
            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(count - i, dictionary._keys[i]);
                Assert.AreEqual($"value_{count - i}", dictionary._values[i]);
            }
        }

        [Test]
        public void DictionaryWithNullKeysPreservesArrayForEditing()
        {
            // Arrange: Array with null key (string type)
            SerializableDictionary<string, int> dictionary = new()
            {
                _keys = new[] { "valid", null, "also-valid" },
                _values = new[] { 1, 2, 3 },
            };

            // Expect the error log about null key being skipped
            LogAssert.Expect(
                LogType.Warning,
                "SerializableDictionary<System.String, System.Int32> skipped serialized entry at index 1 because the key reference was null."
            );

            // Act
            dictionary.OnAfterDeserialize();

            // Assert: Array preserved, duplicates/nulls flag set
            Assert.IsTrue(dictionary.PreserveSerializedEntries);
            Assert.IsTrue(dictionary.HasDuplicatesOrNulls);
            // Dictionary should contain only valid keys
            Assert.AreEqual(2, dictionary.Count);
            Assert.IsTrue(dictionary.ContainsKey("valid"));
            Assert.IsTrue(dictionary.ContainsKey("also-valid"));
        }

        [Test]
        public void DictionaryAddingExistingKeyUpdatesValuePreservesOrder()
        {
            // Arrange
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };
            dictionary.OnAfterDeserialize();

            // Act: Use indexer to update existing key
            dictionary[1] = "ONE_UPDATED";
            dictionary.OnBeforeSerialize();

            // Assert: Order preserved, value updated
            CollectionAssert.AreEqual(new[] { 3, 1, 2 }, dictionary._keys);
            Assert.AreEqual("three", dictionary._values[0]);
            Assert.AreEqual("ONE_UPDATED", dictionary._values[1]);
            Assert.AreEqual("two", dictionary._values[2]);
        }

        [Test]
        public void DictionaryTryAddPreservesOrderWhenKeyExists()
        {
            // Arrange
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };
            dictionary.OnAfterDeserialize();

            // Act: TryAdd with existing key (should not add)
            bool added = dictionary.TryAdd(1, "should-not-add");
            dictionary.OnBeforeSerialize();

            // Assert: Not added, order preserved
            Assert.IsFalse(added);
            CollectionAssert.AreEqual(new[] { 3, 1, 2 }, dictionary._keys);
            Assert.AreEqual("one", dictionary._values[1]); // Original value
        }

        [Test]
        public void DictionaryRemoveNonExistentKeyPreservesOrder()
        {
            // Arrange
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };
            dictionary.OnAfterDeserialize();

            // Act: Try to remove non-existent key
            bool removed = dictionary.Remove(999);
            dictionary.OnBeforeSerialize();

            // Assert: Not removed, order preserved
            Assert.IsFalse(removed);
            CollectionAssert.AreEqual(new[] { 3, 1, 2 }, dictionary._keys);
        }

        [Test]
        public void DictionaryProtoSerializationPreservesOrder()
        {
            // Arrange
            SerializableDictionary<int, string> original = new()
            {
                _keys = new[] { 30, 10, 20 },
                _values = new[] { "thirty", "ten", "twenty" },
            };
            original.OnAfterDeserialize();

            // Act: Protobuf round-trip
            byte[] bytes = Serializer.ProtoSerialize(original);
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(bytes.Length, 0, "Serialized bytes should not be empty");

            SerializableDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableDictionary<int, string>
            >(bytes);

            // Assert: Order preserved after round-trip
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._keys != null,
                $"Restored _keys should not be null. Serialized {bytes.Length} bytes."
            );
            Assert.IsTrue(
                restored._values != null,
                $"Restored _values should not be null. Serialized {bytes.Length} bytes."
            );
            string actualKeys = string.Join(", ", restored._keys);
            string actualValues = string.Join(", ", restored._values);
            CollectionAssert.AreEqual(
                new[] { 30, 10, 20 },
                restored._keys,
                $"Expected keys [30, 10, 20], got [{actualKeys}]"
            );
            CollectionAssert.AreEqual(
                new[] { "thirty", "ten", "twenty" },
                restored._values,
                $"Expected values [thirty, ten, twenty], got [{actualValues}]"
            );
        }

        [Test]
        public void DictionaryJsonSerializationPreservesOrder()
        {
            // Arrange
            SerializableDictionary<int, string> original = new()
            {
                _keys = new[] { 30, 10, 20 },
                _values = new[] { "thirty", "ten", "twenty" },
            };
            original.OnAfterDeserialize();

            // Act: JSON round-trip
            string json = Serializer.JsonStringify(original);
            SerializableDictionary<int, string> restored = Serializer.JsonDeserialize<
                SerializableDictionary<int, string>
            >(json);

            // Assert: Order preserved after round-trip
            CollectionAssert.AreEqual(new[] { 30, 10, 20 }, restored._keys);
            CollectionAssert.AreEqual(new[] { "thirty", "ten", "twenty" }, restored._values);
        }

        [Test]
        public void DictionaryDomainReloadDoesNotReorderKeys()
        {
            // This test specifically verifies order preservation across domain reloads

            // Arrange: Keys explicitly NOT in sorted order
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 100, 1, 50, 25, 75 },
                _values = new[] { "hundred", "one", "fifty", "twenty-five", "seventy-five" },
            };

            // Sorted order would be: 1, 25, 50, 75, 100
            // We want to preserve: 100, 1, 50, 25, 75

            // Act: Simulate domain reload (multiple cycles)
            for (int i = 0; i < 10; i++)
            {
                dictionary.OnAfterDeserialize();
                dictionary.OnBeforeSerialize();
            }

            // Assert: Order must NOT have changed to sorted order
            int[] expectedOrder = { 100, 1, 50, 25, 75 };
            CollectionAssert.AreEqual(
                expectedOrder,
                dictionary._keys,
                "Keys should preserve user-defined order, not be sorted"
            );
        }

        [Test]
        public void DictionaryStringKeysDomainReloadPreservesOrder()
        {
            // Arrange: String keys NOT in alphabetical order
            SerializableDictionary<string, int> dictionary = new()
            {
                _keys = new[] { "zebra", "apple", "mango", "banana", "cherry" },
                _values = new[] { 1, 2, 3, 4, 5 },
            };

            // Alphabetical would be: apple, banana, cherry, mango, zebra
            // We want to preserve: zebra, apple, mango, banana, cherry

            // Act
            for (int i = 0; i < 5; i++)
            {
                dictionary.OnAfterDeserialize();
                dictionary.OnBeforeSerialize();
            }

            // Assert
            string[] expectedOrder = { "zebra", "apple", "mango", "banana", "cherry" };
            CollectionAssert.AreEqual(
                expectedOrder,
                dictionary._keys,
                "String keys should preserve user-defined order, not alphabetical"
            );
        }

        /// <summary>
        /// Test cases for Dictionary mutation scenarios that should preserve order.
        /// Format: (initialKeys, initialValues, keysToRemove, keysToAdd, valuesToAdd, expectedKeys)
        /// </summary>
        private static IEnumerable<TestCaseData> DictionaryMutationTestCases()
        {
            yield return new TestCaseData(
                new[] { 30, 10, 20 },
                new[] { "thirty", "ten", "twenty" },
                new[] { 10 },
                new[] { 15 },
                new[] { "fifteen" },
                new[] { 30, 20, 15 }
            ).SetName("DictionaryMutation.RemoveAndAddKey");

            yield return new TestCaseData(
                new[] { 5, 3, 1 },
                new[] { "five", "three", "one" },
                new int[0],
                new[] { 2, 4 },
                new[] { "two", "four" },
                new[] { 5, 3, 1, 2, 4 }
            ).SetName("DictionaryMutation.AddKeysWithoutRemoving");

            yield return new TestCaseData(
                new[] { 100, 50, 25, 75 },
                new[] { "a", "b", "c", "d" },
                new[] { 50, 75 },
                new int[0],
                new string[0],
                new[] { 100, 25 }
            ).SetName("DictionaryMutation.RemoveKeysWithoutAdding");

            yield return new TestCaseData(
                new[] { 1, 2, 3, 4, 5 },
                new[] { "one", "two", "three", "four", "five" },
                new[] { 1, 2, 3, 4, 5 },
                new[] { 10, 20 },
                new[] { "ten", "twenty" },
                new[] { 10, 20 }
            ).SetName("DictionaryMutation.RemoveAllAddNew");

            yield return new TestCaseData(
                new[] { 50, 30, 70, 10, 90 },
                new[] { "fifty", "thirty", "seventy", "ten", "ninety" },
                new[] { 50, 70 },
                new[] { 20, 80 },
                new[] { "twenty", "eighty" },
                new[] { 30, 10, 90, 20, 80 }
            ).SetName("DictionaryMutation.NonSortedInitialOrderPreserved");
        }

        [Test]
        [TestCaseSource(nameof(DictionaryMutationTestCases))]
        public void DictionaryMutationPreservesOrder(
            int[] initialKeys,
            string[] initialValues,
            int[] keysToRemove,
            int[] keysToAdd,
            string[] valuesToAdd,
            int[] expectedKeys
        )
        {
            // Arrange
            SerializableDictionary<int, string> dict = new()
            {
                _keys = initialKeys,
                _values = initialValues,
            };
            dict.OnAfterDeserialize();

            // Act: Apply mutations
            foreach (int key in keysToRemove)
            {
                dict.Remove(key);
            }
            for (int i = 0; i < keysToAdd.Length; i++)
            {
                dict.Add(keysToAdd[i], valuesToAdd[i]);
            }
            dict.OnBeforeSerialize();

            // Assert
            string actualKeys = dict._keys != null ? string.Join(", ", dict._keys) : "null";
            string expected = string.Join(", ", expectedKeys);
            CollectionAssert.AreEqual(
                expectedKeys,
                dict._keys,
                $"Expected keys [{expected}], got [{actualKeys}]"
            );
        }

        [Test]
        public void DictionaryMutationThenProtoSerializationPreservesOrder()
        {
            // Arrange
            SerializableDictionary<int, string> dict = new()
            {
                _keys = new[] { 30, 10, 20, 40 },
                _values = new[] { "thirty", "ten", "twenty", "forty" },
            };
            dict.OnAfterDeserialize();

            // Act: Mutate then serialize
            dict.Remove(10);
            dict.Add(15, "fifteen");
            byte[] bytes = Serializer.ProtoSerialize(dict);

            // Assert: Bytes were serialized
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(bytes.Length, 0, "Serialized bytes should not be empty");

            SerializableDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableDictionary<int, string>
            >(bytes);

            // Assert: Restored correctly
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._keys != null,
                $"Restored _keys should not be null. Serialized {bytes.Length} bytes."
            );
            Assert.IsTrue(
                restored._values != null,
                $"Restored _values should not be null. Serialized {bytes.Length} bytes."
            );
            int[] expected = { 30, 20, 40, 15 };
            string actualKeys = string.Join(", ", restored._keys);
            CollectionAssert.AreEqual(
                expected,
                restored._keys,
                $"Expected keys [30, 20, 40, 15], got [{actualKeys}]"
            );
        }

        [Test]
        public void DictionaryMutationThenJsonSerializationPreservesOrder()
        {
            // Arrange
            SerializableDictionary<int, string> dict = new()
            {
                _keys = new[] { 30, 10, 20, 40 },
                _values = new[] { "thirty", "ten", "twenty", "forty" },
            };
            dict.OnAfterDeserialize();

            // Act: Mutate then serialize
            dict.Remove(10);
            dict.Add(15, "fifteen");
            string json = Serializer.JsonStringify(dict);
            SerializableDictionary<int, string> restored = Serializer.JsonDeserialize<
                SerializableDictionary<int, string>
            >(json);

            // Assert
            Assert.IsTrue(
                restored._keys != null,
                $"Restored _keys should not be null. JSON: {json}"
            );
            int[] expected = { 30, 20, 40, 15 };
            string actualKeys = string.Join(", ", restored._keys);
            CollectionAssert.AreEqual(
                expected,
                restored._keys,
                $"Expected keys [30, 20, 40, 15], got [{actualKeys}]. JSON: {json}"
            );
        }

        [Test]
        public void DictionaryMultipleSerializeCyclesAfterMutationPreservesOrder()
        {
            // Arrange
            SerializableDictionary<int, string> dict = new()
            {
                _keys = new[] { 100, 50, 75, 25 },
                _values = new[] { "hundred", "fifty", "seventy-five", "twenty-five" },
            };
            dict.OnAfterDeserialize();

            // Act: Mutate
            dict.Remove(50);
            dict.Add(60, "sixty");

            // Then run multiple serialize/deserialize cycles
            for (int i = 0; i < 5; i++)
            {
                dict.OnBeforeSerialize();
                dict.OnAfterDeserialize();
            }

            // Assert: Order should remain stable
            int[] expected = { 100, 75, 25, 60 };
            string actualKeys = dict._keys != null ? string.Join(", ", dict._keys) : "null";
            CollectionAssert.AreEqual(
                expected,
                dict._keys,
                $"Expected [100, 75, 25, 60], got [{actualKeys}]"
            );
        }

        [Test]
        public void DictionaryJsonSerializationProducesObjectFormat()
        {
            // This test verifies the JSON format is correct (object with _keys and _values)
            // Arrange
            SerializableDictionary<int, string> original = new()
            {
                _keys = new[] { 30, 10, 20 },
                _values = new[] { "thirty", "ten", "twenty" },
            };
            original.OnAfterDeserialize();

            // Act
            string json = Serializer.JsonStringify(original);

            // Assert: JSON should contain _keys and _values properties
            Assert.IsTrue(
                json.Contains("_keys"),
                $"JSON should contain '_keys' property. Got: {json}"
            );
            Assert.IsTrue(
                json.Contains("_values"),
                $"JSON should contain '_values' property. Got: {json}"
            );
            Assert.IsTrue(
                json.StartsWith("{"),
                $"JSON should start with '{{' (object format). Got: {json}"
            );
        }

        [Test]
        public void DictionaryUpdateValuePreservesKeyOrder()
        {
            // Arrange
            SerializableDictionary<int, string> dict = new()
            {
                _keys = new[] { 30, 10, 20 },
                _values = new[] { "thirty", "ten", "twenty" },
            };
            dict.OnAfterDeserialize();

            // Act: Update existing value
            dict[10] = "TEN_UPDATED";
            dict.OnBeforeSerialize();

            // Assert: Key order should be preserved, value updated
            CollectionAssert.AreEqual(
                new[] { 30, 10, 20 },
                dict._keys,
                "Key order should be preserved after value update"
            );
            Assert.AreEqual("TEN_UPDATED", dict[10], "Value should be updated");
        }

        private static IEnumerable<TestCaseData> DictionaryProtoSerializationTestCases()
        {
            yield return new TestCaseData(new[] { 1 }, new[] { "one" }).SetName(
                "Dictionary.SingleEntry"
            );
            yield return new TestCaseData(
                new[] { 30, 10, 20 },
                new[] { "thirty", "ten", "twenty" }
            ).SetName("Dictionary.MultipleEntries.Unordered");
            yield return new TestCaseData(
                new[] { 1, 2, 3, 4, 5 },
                new[] { "one", "two", "three", "four", "five" }
            ).SetName("Dictionary.MultipleEntries.Ascending");
            yield return new TestCaseData(
                new[] { 5, 4, 3, 2, 1 },
                new[] { "five", "four", "three", "two", "one" }
            ).SetName("Dictionary.MultipleEntries.Descending");
            yield return new TestCaseData(
                new[] { 100, -50, 0, 25, -25 },
                new[] { "hundred", "neg-fifty", "zero", "twenty-five", "neg-twenty-five" }
            ).SetName("Dictionary.MixedPositiveNegative");
            yield return new TestCaseData(
                new[] { int.MaxValue, int.MinValue, 0 },
                new[] { "max", "min", "zero" }
            ).SetName("Dictionary.ExtremeBoundaryValues");
            yield return new TestCaseData(
                Enumerable.Range(0, 100).Reverse().ToArray(),
                Enumerable.Range(0, 100).Reverse().Select(i => $"value{i}").ToArray()
            ).SetName("Dictionary.LargeArray.100Elements");
        }

        [TestCaseSource(nameof(DictionaryProtoSerializationTestCases))]
        public void DictionaryProtoSerializationPreservesOrderDataDriven(
            int[] keys,
            string[] values
        )
        {
            // Arrange
            SerializableDictionary<int, string> original = new() { _keys = keys, _values = values };
            original.OnAfterDeserialize();

            // Diagnostic: Verify original state
            string originalKeysStr =
                original._keys != null ? string.Join(", ", original._keys) : "null";
            Assert.IsTrue(
                original._keys != null,
                "Original _keys should not be null before serialization"
            );

            // Act
            byte[] bytes = Serializer.ProtoSerialize(original);

            // Assert: Bytes were serialized
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(
                bytes.Length,
                0,
                $"Serialized bytes should not be empty for {keys.Length} entries"
            );

            SerializableDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableDictionary<int, string>
            >(bytes);

            // Assert: Restored correctly
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._keys != null,
                $"Restored _keys should not be null. Serialized {bytes.Length} bytes for {keys.Length} entries. "
                    + $"Original=[{originalKeysStr}]"
            );
            Assert.IsTrue(
                restored._values != null,
                $"Restored _values should not be null. Serialized {bytes.Length} bytes."
            );
            string expectedStr = string.Join(", ", keys);
            string actualStr = string.Join(", ", restored._keys);
            CollectionAssert.AreEqual(
                keys,
                restored._keys,
                $"Expected [{expectedStr}], got [{actualStr}]"
            );
            CollectionAssert.AreEqual(
                values,
                restored._values,
                $"Expected [{string.Join(", ", values)}], got [{string.Join(", ", restored._values)}]"
            );
        }

        [Test]
        public void EmptyDictionaryProtoSerializationRoundTrips()
        {
            // Arrange
            SerializableDictionary<int, string> original = new();
            original.OnAfterDeserialize();

            // Act
            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableDictionary<int, string>
            >(bytes);

            // Assert
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.AreEqual(0, restored.Count, "Restored dictionary should be empty");
        }

        [Test]
        public void DictionaryClearThenAddPreservesNewOrder()
        {
            // Arrange
            SerializableDictionary<int, string> dict = new()
            {
                _keys = new[] { 1, 2, 3, 4, 5 },
                _values = new[] { "one", "two", "three", "four", "five" },
            };
            dict.OnAfterDeserialize();

            // Act: Clear and add new items
            dict.Clear();
            dict.Add(100, "hundred");
            dict.Add(50, "fifty");
            dict.Add(75, "seventy-five");
            dict.OnBeforeSerialize();

            // Assert: New items should be in the order they were added
            string actualKeys = dict._keys != null ? string.Join(", ", dict._keys) : "null";
            Assert.AreEqual(
                3,
                dict._keys.Length,
                $"Expected 3 keys after clear and add, got {dict._keys.Length}. Keys: [{actualKeys}]"
            );
            CollectionAssert.AreEqual(
                new[] { 100, 50, 75 },
                dict._keys,
                $"Expected keys in insertion order [100, 50, 75], got [{actualKeys}]"
            );
        }

        [Test]
        public void DictionaryAddMultipleEntriesPreservesInsertionOrder()
        {
            // Arrange: Empty dictionary
            SerializableDictionary<int, string> dict = new();
            dict.OnAfterDeserialize();

            // Act: Add entries in specific order
            dict.Add(7, "seven");
            dict.Add(3, "three");
            dict.Add(9, "nine");
            dict.Add(1, "one");
            dict.OnBeforeSerialize();

            // Assert: Keys should be in exact insertion order
            string actualKeys = dict._keys != null ? string.Join(", ", dict._keys) : "null";
            int[] expected = { 7, 3, 9, 1 };
            CollectionAssert.AreEqual(
                expected,
                dict._keys,
                $"Expected keys in insertion order [7, 3, 9, 1], got [{actualKeys}]"
            );
        }

        [Test]
        public void DictionaryAddAfterDeserializePreservesExistingThenNewOrder()
        {
            // Arrange: Dictionary with existing entries
            SerializableDictionary<int, string> dict = new()
            {
                _keys = new[] { 100, 50, 200 },
                _values = new[] { "hundred", "fifty", "two-hundred" },
            };
            dict.OnAfterDeserialize();

            // Act: Add new entries in specific order
            dict.Add(25, "twenty-five");
            dict.Add(150, "one-fifty");
            dict.OnBeforeSerialize();

            // Assert: Existing keys first, then new keys in insertion order
            string actualKeys = dict._keys != null ? string.Join(", ", dict._keys) : "null";
            int[] expected = { 100, 50, 200, 25, 150 };
            CollectionAssert.AreEqual(
                expected,
                dict._keys,
                $"Expected existing keys + new keys in order [100, 50, 200, 25, 150], got [{actualKeys}]"
            );
        }

        [Test]
        public void DictionaryInterleavedAddRemovePreservesOrder()
        {
            // Arrange
            SerializableDictionary<int, string> dict = new()
            {
                _keys = new[] { 10, 20, 30, 40, 50 },
                _values = new[] { "ten", "twenty", "thirty", "forty", "fifty" },
            };
            dict.OnAfterDeserialize();

            // Act: Interleaved add/remove operations
            dict.Remove(20);
            dict.Add(25, "twenty-five");
            dict.Remove(40);
            dict.Add(45, "forty-five");
            dict.Add(15, "fifteen");
            dict.OnBeforeSerialize();

            // Assert: Remaining original keys preserve order, new keys in insertion order
            string actualKeys = dict._keys != null ? string.Join(", ", dict._keys) : "null";
            int[] expected = { 10, 30, 50, 25, 45, 15 };
            CollectionAssert.AreEqual(
                expected,
                dict._keys,
                $"Expected [10, 30, 50, 25, 45, 15], got [{actualKeys}]"
            );
        }

        [TestCaseSource(nameof(DictionaryProtoSerializationDiagnosticTestCases))]
        public void DictionaryProtoSerializationDiagnosticVerifiesInternalState(
            int[] keys,
            string[] values
        )
        {
            // Arrange: Create dictionary with explicit _keys/_values assignment
            SerializableDictionary<int, string> original = new() { _keys = keys, _values = values };
            original.OnAfterDeserialize();

            // Verify pre-serialization state
            Assert.IsTrue(
                original._keys != null,
                $"Original _keys should not be null before serialization. Count={original.Count}"
            );
            Assert.IsTrue(
                original._values != null,
                $"Original _values should not be null before serialization. Count={original.Count}"
            );
            Assert.AreEqual(
                keys.Length,
                original._keys.Length,
                $"Original _keys.Length should match. Expected={keys.Length}, Actual={original._keys.Length}"
            );

            // Act: Serialize
            byte[] bytes = Serializer.ProtoSerialize(original);

            // Diagnostic: Verify bytes were produced
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(
                bytes.Length,
                0,
                $"Serialized bytes should not be empty for {keys.Length} entries"
            );

            // Act: Deserialize
            SerializableDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableDictionary<int, string>
            >(bytes);

            // Assert: Verify restored state with comprehensive diagnostics
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._keys != null,
                $"Restored _keys should not be null. Serialized {bytes.Length} bytes for {keys.Length} entries. "
                    + $"Original._keys was [{string.Join(", ", original._keys)}]"
            );
            Assert.IsTrue(
                restored._values != null,
                $"Restored _values should not be null. Serialized {bytes.Length} bytes for {values.Length} entries. "
                    + $"Original._values was [{string.Join(", ", original._values)}]"
            );
            Assert.AreEqual(
                keys.Length,
                restored._keys.Length,
                $"Restored _keys.Length mismatch. Expected={keys.Length}, Actual={restored._keys.Length}. "
                    + $"Original=[{string.Join(", ", original._keys)}], Restored=[{string.Join(", ", restored._keys)}]"
            );
            CollectionAssert.AreEqual(
                keys,
                restored._keys,
                $"Keys should match exactly. Expected=[{string.Join(", ", keys)}], Got=[{string.Join(", ", restored._keys)}]"
            );
            CollectionAssert.AreEqual(
                values,
                restored._values,
                $"Values should match exactly. Expected=[{string.Join(", ", values)}], Got=[{string.Join(", ", restored._values)}]"
            );
        }

        [TestCaseSource(nameof(DictionaryProtoSerializationDiagnosticTestCases))]
        public void SortedDictionaryProtoSerializationDiagnosticVerifiesInternalState(
            int[] keys,
            string[] values
        )
        {
            // Arrange: Create sorted dictionary with explicit _keys/_values assignment
            SerializableSortedDictionary<int, string> original = new()
            {
                _keys = keys,
                _values = values,
            };
            original.OnAfterDeserialize();

            // Verify pre-serialization state
            Assert.IsTrue(
                original._keys != null,
                $"Original _keys should not be null before serialization. Count={original.Count}"
            );
            Assert.IsTrue(
                original._values != null,
                $"Original _values should not be null before serialization. Count={original.Count}"
            );
            Assert.AreEqual(
                keys.Length,
                original._keys.Length,
                $"Original _keys.Length should match. Expected={keys.Length}, Actual={original._keys.Length}"
            );

            // Act: Serialize
            byte[] bytes = Serializer.ProtoSerialize(original);

            // Diagnostic: Verify bytes were produced
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(
                bytes.Length,
                0,
                $"Serialized bytes should not be empty for {keys.Length} entries"
            );

            // Act: Deserialize
            SerializableSortedDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableSortedDictionary<int, string>
            >(bytes);

            // Assert: Verify restored state with comprehensive diagnostics
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._keys != null,
                $"Restored _keys should not be null. Serialized {bytes.Length} bytes for {keys.Length} entries. "
                    + $"Original._keys was [{string.Join(", ", original._keys)}]"
            );
            Assert.IsTrue(
                restored._values != null,
                $"Restored _values should not be null. Serialized {bytes.Length} bytes for {values.Length} entries. "
                    + $"Original._values was [{string.Join(", ", original._values)}]"
            );
            Assert.AreEqual(
                keys.Length,
                restored._keys.Length,
                $"Restored _keys.Length mismatch. Expected={keys.Length}, Actual={restored._keys.Length}. "
                    + $"Original=[{string.Join(", ", original._keys)}], Restored=[{string.Join(", ", restored._keys)}]"
            );
            CollectionAssert.AreEqual(
                keys,
                restored._keys,
                $"Keys should match exactly. Expected=[{string.Join(", ", keys)}], Got=[{string.Join(", ", restored._keys)}]"
            );
            CollectionAssert.AreEqual(
                values,
                restored._values,
                $"Values should match exactly. Expected=[{string.Join(", ", values)}], Got=[{string.Join(", ", restored._values)}]"
            );
        }

        [Test]
        public void HashSetProtoSerializationWithAddOperationsPreservesOrder()
        {
            // Arrange: Create set using Add operations (not direct _items assignment)
            SerializableHashSet<int> original = new();
            original.Add(7);
            original.Add(3);
            original.Add(9);
            original.Add(1);

            // Act: Proto round-trip
            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableHashSet<int> restored = Serializer.ProtoDeserialize<
                SerializableHashSet<int>
            >(bytes);

            // Assert
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._items != null,
                $"Restored _items should not be null. Serialized {bytes.Length} bytes."
            );
            Assert.AreEqual(
                4,
                restored._items.Length,
                $"Should have 4 items. Got {restored._items.Length}: [{string.Join(", ", restored._items)}]"
            );

            // Order should be: 7, 3, 9, 1 (insertion order)
            int[] expectedOrder = { 7, 3, 9, 1 };
            CollectionAssert.AreEqual(
                expectedOrder,
                restored._items,
                $"Expected insertion order [{string.Join(", ", expectedOrder)}], got [{string.Join(", ", restored._items)}]"
            );
        }

        [Test]
        public void DictionaryProtoSerializationWithAddOperationsPreservesOrder()
        {
            // Arrange: Create dictionary using indexer (not direct _keys/_values assignment)
            SerializableDictionary<int, string> original = new();
            original[5] = "five";
            original[2] = "two";
            original[8] = "eight";
            original[1] = "one";

            // Act: Proto round-trip
            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableDictionary<int, string>
            >(bytes);

            // Assert
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._keys != null,
                $"Restored _keys should not be null. Serialized {bytes.Length} bytes."
            );
            Assert.IsTrue(
                restored._values != null,
                $"Restored _values should not be null. Serialized {bytes.Length} bytes."
            );
            Assert.AreEqual(
                4,
                restored._keys.Length,
                $"Should have 4 keys. Got {restored._keys.Length}: [{string.Join(", ", restored._keys)}]"
            );

            // Order should be: 5, 2, 8, 1 (insertion order)
            int[] expectedKeys = { 5, 2, 8, 1 };
            string[] expectedValues = { "five", "two", "eight", "one" };
            CollectionAssert.AreEqual(
                expectedKeys,
                restored._keys,
                $"Expected key order [{string.Join(", ", expectedKeys)}], got [{string.Join(", ", restored._keys)}]"
            );
            CollectionAssert.AreEqual(
                expectedValues,
                restored._values,
                $"Expected value order [{string.Join(", ", expectedValues)}], got [{string.Join(", ", restored._values)}]"
            );
        }

        [Test]
        public void HashSetProtoSerializationWithStringElementsRoundTrips()
        {
            // Arrange: String elements to test non-int types
            SerializableHashSet<string> original = new()
            {
                _items = new[] { "apple", "banana", "cherry" },
            };
            original.OnAfterDeserialize();

            // Act
            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableHashSet<string> restored = Serializer.ProtoDeserialize<
                SerializableHashSet<string>
            >(bytes);

            // Assert
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._items != null,
                $"Restored _items should not be null. Serialized {bytes.Length} bytes."
            );
            CollectionAssert.AreEqual(
                new[] { "apple", "banana", "cherry" },
                restored._items,
                $"Expected [apple, banana, cherry], got [{string.Join(", ", restored._items)}]"
            );
        }

        [Test]
        public void SortedSetProtoSerializationWithStringElementsRoundTrips()
        {
            // Arrange: String elements to test non-int types
            SerializableSortedSet<string> original = new()
            {
                _items = new[] { "zebra", "apple", "mango" },
            };
            original.OnAfterDeserialize();

            // Act
            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableSortedSet<string> restored = Serializer.ProtoDeserialize<
                SerializableSortedSet<string>
            >(bytes);

            // Assert
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._items != null,
                $"Restored _items should not be null. Serialized {bytes.Length} bytes."
            );
            CollectionAssert.AreEqual(
                new[] { "zebra", "apple", "mango" },
                restored._items,
                $"Expected [zebra, apple, mango], got [{string.Join(", ", restored._items)}]"
            );
        }

        [Test]
        public void DictionaryProtoSerializationWithComplexKeyTypeRoundTrips()
        {
            // Arrange: String keys instead of int
            SerializableDictionary<string, int> original = new()
            {
                _keys = new[] { "gamma", "alpha", "beta" },
                _values = new[] { 3, 1, 2 },
            };
            original.OnAfterDeserialize();

            // Act
            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableDictionary<string, int> restored = Serializer.ProtoDeserialize<
                SerializableDictionary<string, int>
            >(bytes);

            // Assert
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._keys != null,
                $"Restored _keys should not be null. Serialized {bytes.Length} bytes."
            );
            Assert.IsTrue(
                restored._values != null,
                $"Restored _values should not be null. Serialized {bytes.Length} bytes."
            );
            CollectionAssert.AreEqual(
                new[] { "gamma", "alpha", "beta" },
                restored._keys,
                $"Expected [gamma, alpha, beta], got [{string.Join(", ", restored._keys)}]"
            );
            CollectionAssert.AreEqual(
                new[] { 3, 1, 2 },
                restored._values,
                $"Expected [3, 1, 2], got [{string.Join(", ", restored._values)}]"
            );
        }

        /// <summary>
        /// Additional data-driven test cases that exercise various mutation patterns
        /// to ensure order preservation works correctly across different scenarios.
        /// </summary>
        private static IEnumerable<TestCaseData> DictionaryOrderPreservationEdgeCases()
        {
            // Adding keys in specific order after deserialization
            yield return new TestCaseData(
                new[] { 100 },
                new[] { "hundred" },
                new int[0],
                new[] { 50, 75, 25 },
                new[] { "fifty", "seventy-five", "twenty-five" },
                new[] { 100, 50, 75, 25 }
            ).SetName("Dictionary.SingleInitialWithMultipleAdds");

            // Removing first key, adding new keys
            yield return new TestCaseData(
                new[] { 10, 20, 30 },
                new[] { "ten", "twenty", "thirty" },
                new[] { 10 },
                new[] { 5 },
                new[] { "five" },
                new[] { 20, 30, 5 }
            ).SetName("Dictionary.RemoveFirstAddNew");

            // Removing last key, adding new keys
            yield return new TestCaseData(
                new[] { 10, 20, 30 },
                new[] { "ten", "twenty", "thirty" },
                new[] { 30 },
                new[] { 40, 50 },
                new[] { "forty", "fifty" },
                new[] { 10, 20, 40, 50 }
            ).SetName("Dictionary.RemoveLastAddNew");

            // Removing middle keys, adding multiple new ones
            yield return new TestCaseData(
                new[] { 10, 20, 30, 40, 50 },
                new[] { "a", "b", "c", "d", "e" },
                new[] { 20, 40 },
                new[] { 15, 25, 35 },
                new[] { "fifteen", "twenty-five", "thirty-five" },
                new[] { 10, 30, 50, 15, 25, 35 }
            ).SetName("Dictionary.RemoveMultipleMiddleAddMultipleNew");

            // Start empty, add keys in specific order
            yield return new TestCaseData(
                new int[0],
                new string[0],
                new int[0],
                new[] { 3, 1, 4, 1, 5 },
                new[] { "three", "one", "four", "one-dup", "five" },
                new[] { 3, 1, 4, 5 }
            ).SetName("Dictionary.StartEmptyAddWithDuplicateAttempts");

            // Large re-ordering scenario
            yield return new TestCaseData(
                new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
                new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" },
                new[] { 2, 4, 6, 8, 10 },
                new[] { 11, 12 },
                new[] { "11", "12" },
                new[] { 1, 3, 5, 7, 9, 11, 12 }
            ).SetName("Dictionary.RemoveEvenNumbersAddNew");
        }

        [Test]
        [TestCaseSource(nameof(DictionaryOrderPreservationEdgeCases))]
        public void DictionaryOrderPreservationEdgeCasesPreserveOrder(
            int[] initialKeys,
            string[] initialValues,
            int[] keysToRemove,
            int[] keysToAdd,
            string[] valuesToAdd,
            int[] expectedKeys
        )
        {
            // Arrange
            SerializableDictionary<int, string> dict = new();
            if (initialKeys.Length > 0)
            {
                dict._keys = initialKeys;
                dict._values = initialValues;
                dict.OnAfterDeserialize();
            }

            // Diagnostic info
            string initialState =
                $"Initial: keys=[{string.Join(", ", initialKeys)}], values=[{string.Join(", ", initialValues)}]";
            string removeState = $"Removing: [{string.Join(", ", keysToRemove)}]";
            string addState =
                $"Adding: keys=[{string.Join(", ", keysToAdd)}], values=[{string.Join(", ", valuesToAdd)}]";

            // Act: Apply mutations
            foreach (int key in keysToRemove)
            {
                dict.Remove(key);
            }

            for (int i = 0; i < keysToAdd.Length; i++)
            {
                // TryAdd to handle potential duplicate attempts
                dict.TryAdd(keysToAdd[i], valuesToAdd[i]);
            }

            dict.OnBeforeSerialize();

            // Assert
            string actualKeys = dict._keys != null ? string.Join(", ", dict._keys) : "null";
            string expected = string.Join(", ", expectedKeys);
            CollectionAssert.AreEqual(
                expectedKeys,
                dict._keys,
                $"Order mismatch. {initialState}. {removeState}. {addState}. Expected [{expected}], got [{actualKeys}]"
            );
        }

        [Test]
        public void DictionaryJsonRoundTripWithOrderPreservation()
        {
            // Arrange: Dictionary with specific non-sorted order
            SerializableDictionary<int, string> original = new()
            {
                _keys = new[] { 100, 25, 75, 50 },
                _values = new[] { "hundred", "twenty-five", "seventy-five", "fifty" },
            };
            original.OnAfterDeserialize();

            // Apply some mutations
            original.Remove(25);
            original.Add(30, "thirty");
            original.Add(60, "sixty");

            // Expected order: [100, 75, 50, 30, 60] (original minus removed, plus new in insertion order)
            int[] expectedOrder = { 100, 75, 50, 30, 60 };

            // Act: JSON round-trip
            string json = Serializer.JsonStringify(original);
            SerializableDictionary<int, string> restored = Serializer.JsonDeserialize<
                SerializableDictionary<int, string>
            >(json);

            // Assert
            Assert.IsTrue(restored != null, $"Restored object should not be null. JSON: {json}");
            Assert.IsTrue(
                restored._keys != null,
                $"Restored _keys should not be null. JSON: {json}"
            );
            Assert.IsTrue(
                restored._values != null,
                $"Restored _values should not be null. JSON: {json}"
            );

            string actualKeys = string.Join(", ", restored._keys);
            CollectionAssert.AreEqual(
                expectedOrder,
                restored._keys,
                $"Expected keys [{string.Join(", ", expectedOrder)}], got [{actualKeys}]. JSON: {json}"
            );
        }

        [Test]
        public void DictionaryProtoRoundTripWithOrderPreservation()
        {
            // Arrange: Dictionary with specific non-sorted order
            SerializableDictionary<int, string> original = new()
            {
                _keys = new[] { 100, 25, 75, 50 },
                _values = new[] { "hundred", "twenty-five", "seventy-five", "fifty" },
            };
            original.OnAfterDeserialize();

            // Apply some mutations
            original.Remove(25);
            original.Add(30, "thirty");
            original.Add(60, "sixty");

            // Expected order: [100, 75, 50, 30, 60] (original minus removed, plus new in insertion order)
            int[] expectedOrder = { 100, 75, 50, 30, 60 };

            // Act: Proto round-trip
            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableDictionary<int, string>
            >(bytes);

            // Assert
            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.IsTrue(
                restored._keys != null,
                $"Restored _keys should not be null. Serialized {bytes.Length} bytes."
            );
            Assert.IsTrue(
                restored._values != null,
                $"Restored _values should not be null. Serialized {bytes.Length} bytes."
            );

            string actualKeys = string.Join(", ", restored._keys);
            CollectionAssert.AreEqual(
                expectedOrder,
                restored._keys,
                $"Expected keys [{string.Join(", ", expectedOrder)}], got [{actualKeys}]"
            );
        }

        [Test]
        public void DictionaryIndexerAddNewKeyPreservesExistingOrder()
        {
            // Arrange: Start with specific order
            SerializableDictionary<int, string> dict = new()
            {
                _keys = new[] { 50, 30, 70 },
                _values = new[] { "fifty", "thirty", "seventy" },
            };
            dict.OnAfterDeserialize();

            // Act: Use indexer to add new keys (should track them as new)
            dict[10] = "ten";
            dict[90] = "ninety";
            dict.OnBeforeSerialize();

            // Assert: Original order preserved, new keys appended in order of addition
            int[] expectedKeys = { 50, 30, 70, 10, 90 };
            string actualKeys = dict._keys != null ? string.Join(", ", dict._keys) : "null";
            CollectionAssert.AreEqual(
                expectedKeys,
                dict._keys,
                $"Expected [{string.Join(", ", expectedKeys)}], got [{actualKeys}]"
            );
        }

        [Test]
        public void DictionaryIndexerUpdateExistingKeyDoesNotChangeOrder()
        {
            // Arrange: Start with specific order
            SerializableDictionary<int, string> dict = new()
            {
                _keys = new[] { 50, 30, 70 },
                _values = new[] { "fifty", "thirty", "seventy" },
            };
            dict.OnAfterDeserialize();

            // Act: Update existing keys via indexer
            dict[30] = "THIRTY_UPDATED";
            dict[70] = "SEVENTY_UPDATED";
            dict.OnBeforeSerialize();

            // Assert: Order remains the same
            int[] expectedKeys = { 50, 30, 70 };
            string actualKeys = dict._keys != null ? string.Join(", ", dict._keys) : "null";
            CollectionAssert.AreEqual(
                expectedKeys,
                dict._keys,
                $"Expected [{string.Join(", ", expectedKeys)}], got [{actualKeys}]"
            );

            // Verify values were updated
            Assert.AreEqual("THIRTY_UPDATED", dict._values[1]);
            Assert.AreEqual("SEVENTY_UPDATED", dict._values[2]);
        }
    }
}
