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
    [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
    public sealed class SerializationOrderPreservationTests : CommonTestBase
    {
        [Test]
        public void SortedDictionaryPreservesSerializedKeyOrderAcrossSerializationCycle()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };

            dictionary.OnAfterDeserialize();
            dictionary.OnBeforeSerialize();

            int[] expectedKeys = { 3, 1, 2 };
            CollectionAssert.AreEqual(expectedKeys, dictionary._keys);
        }

        [Test]
        public void SortedDictionaryPreservesSerializedKeyOrderAfterMultipleSerializationCycles()
        {
            SerializableSortedDictionary<string, int> dictionary = new()
            {
                _keys = new[] { "zebra", "apple", "mango" },
                _values = new[] { 1, 2, 3 },
            };

            for (int cycle = 0; cycle < 5; cycle++)
            {
                dictionary.OnAfterDeserialize();
                dictionary.OnBeforeSerialize();
            }

            string[] expectedKeys = { "zebra", "apple", "mango" };
            CollectionAssert.AreEqual(expectedKeys, dictionary._keys);
        }

        [Test]
        public void SortedDictionaryPreservesOrderWhenValueIsUpdated()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 5, 2, 8, 1 },
                _values = new[] { "five", "two", "eight", "one" },
            };
            dictionary.OnAfterDeserialize();

            dictionary[2] = "TWO_UPDATED";
            dictionary.OnBeforeSerialize();

            int[] expectedKeys = { 5, 2, 8, 1 };
            CollectionAssert.AreEqual(expectedKeys, dictionary._keys);
            Assert.AreEqual("TWO_UPDATED", dictionary._values[1]);
        }

        [Test]
        public void SortedDictionaryAppendsNewKeysAtEndWhilePreservingExistingOrder()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 10, 5, 20 },
                _values = new[] { "ten", "five", "twenty" },
            };
            dictionary.OnAfterDeserialize();

            dictionary.Add(1, "one");
            dictionary.OnBeforeSerialize();

            Assert.AreEqual(4, dictionary._keys.Length);
            Assert.AreEqual(10, dictionary._keys[0]);
            Assert.AreEqual(5, dictionary._keys[1]);
            Assert.AreEqual(20, dictionary._keys[2]);
            Assert.AreEqual(1, dictionary._keys[3]);
        }

        [Test]
        public void SortedDictionaryRemovesKeyWhilePreservingRemainingOrder()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 10, 5, 20, 15 },
                _values = new[] { "ten", "five", "twenty", "fifteen" },
            };
            dictionary.OnAfterDeserialize();

            bool removed = dictionary.Remove(5);
            dictionary.OnBeforeSerialize();

            Assert.IsTrue(removed);
            int[] expectedKeys = { 10, 20, 15 };
            CollectionAssert.AreEqual(expectedKeys, dictionary._keys);
        }

        [Test]
        public void SortedDictionaryClearResultsInEmptyArraysOnNextSerialize()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };
            dictionary.OnAfterDeserialize();

            dictionary.Clear();
            dictionary.OnBeforeSerialize();

            Assert.AreEqual(0, dictionary._keys.Length);
            Assert.AreEqual(0, dictionary._values.Length);
        }

        [Test]
        public void SortedDictionaryWithDuplicateKeysPreservesOriginalArrayOnDeserialization()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 1, 2, 1, 3 },
                _values = new[] { "one-first", "two", "one-second", "three" },
            };

            dictionary.OnAfterDeserialize();

            Assert.IsTrue(dictionary.PreserveSerializedEntries);
            Assert.IsTrue(dictionary.HasDuplicatesOrNulls);
            CollectionAssert.AreEqual(new[] { 1, 2, 1, 3 }, dictionary._keys);
        }

        [Test]
        public void SortedDictionaryEnumerationOrderIsSortedRegardlessOfSerializedOrder()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 30, 10, 20 },
                _values = new[] { "thirty", "ten", "twenty" },
            };
            dictionary.OnAfterDeserialize();

            List<int> enumeratedKeys = new();
            foreach (KeyValuePair<int, string> pair in dictionary)
            {
                enumeratedKeys.Add(pair.Key);
            }

            int[] expectedSortedKeys = { 10, 20, 30 };
            CollectionAssert.AreEqual(expectedSortedKeys, enumeratedKeys);
        }

        [Test]
        public void SortedDictionaryIndexerUpdatesValueInPlacePreservingKeyPosition()
        {
            SerializableSortedDictionary<string, int> dictionary = new()
            {
                _keys = new[] { "z", "a", "m" },
                _values = new[] { 1, 2, 3 },
            };
            dictionary.OnAfterDeserialize();

            dictionary["z"] = 100;
            dictionary["a"] = 200;
            dictionary.OnBeforeSerialize();

            CollectionAssert.AreEqual(new[] { "z", "a", "m" }, dictionary._keys);
            Assert.AreEqual(100, dictionary._values[0]);
            Assert.AreEqual(200, dictionary._values[1]);
            Assert.AreEqual(3, dictionary._values[2]);
        }

        [Test]
        public void SortedDictionaryComplexScenarioAddRemoveUpdatePreservesOrder()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 100, 50, 200, 75 },
                _values = new[] { "hundred", "fifty", "two-hundred", "seventy-five" },
            };
            dictionary.OnAfterDeserialize();

            dictionary[50] = "FIFTY_UPDATED";
            dictionary.Remove(200);
            dictionary.Add(25, "twenty-five");
            dictionary.Add(300, "three-hundred");
            dictionary.OnBeforeSerialize();

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
            SerializableSortedSet<int> set = new() { _items = new[] { 30, 10, 20 } };

            set.OnAfterDeserialize();
            set.OnBeforeSerialize();

            int[] expectedItems = { 30, 10, 20 };
            CollectionAssert.AreEqual(expectedItems, set._items);
        }

        [Test]
        public void SortedSetPreservesOrderAfterMultipleSerializationCycles()
        {
            SerializableSortedSet<string> set = new()
            {
                _items = new[] { "zebra", "apple", "mango", "banana" },
            };

            for (int cycle = 0; cycle < 5; cycle++)
            {
                set.OnAfterDeserialize();
                set.OnBeforeSerialize();
            }

            string[] expectedItems = { "zebra", "apple", "mango", "banana" };
            CollectionAssert.AreEqual(expectedItems, set._items);
        }

        [Test]
        public void SortedSetAppendsNewItemsAtEndWhilePreservingExistingOrder()
        {
            SerializableSortedSet<int> set = new() { _items = new[] { 50, 20, 80 } };
            set.OnAfterDeserialize();

            set.Add(10);
            set.OnBeforeSerialize();

            Assert.AreEqual(4, set._items.Length);
            Assert.AreEqual(50, set._items[0]);
            Assert.AreEqual(20, set._items[1]);
            Assert.AreEqual(80, set._items[2]);
            Assert.AreEqual(10, set._items[3]);
        }

        [Test]
        public void SortedSetRemovesItemWhilePreservingRemainingOrder()
        {
            SerializableSortedSet<int> set = new() { _items = new[] { 50, 20, 80, 35 } };
            set.OnAfterDeserialize();

            bool removed = set.Remove(20);
            set.OnBeforeSerialize();

            Assert.IsTrue(removed);
            int[] expectedItems = { 50, 80, 35 };
            CollectionAssert.AreEqual(expectedItems, set._items);
        }

        [Test]
        public void SortedSetEnumerationIsSortedRegardlessOfSerializedOrder()
        {
            SerializableSortedSet<int> set = new() { _items = new[] { 30, 10, 20 } };
            set.OnAfterDeserialize();

            List<int> enumerated = new();
            foreach (int item in set)
            {
                enumerated.Add(item);
            }

            int[] expectedSorted = { 10, 20, 30 };
            CollectionAssert.AreEqual(expectedSorted, enumerated);
        }

        [Test]
        public void SortedSetWithDuplicatesPreservesOriginalArrayOnDeserialization()
        {
            SerializableSortedSet<int> set = new() { _items = new[] { 1, 2, 1, 3 } };

            set.OnAfterDeserialize();

            Assert.IsTrue(set.PreserveSerializedEntries);
            Assert.IsTrue(set.HasDuplicatesOrNulls);
            CollectionAssert.AreEqual(new[] { 1, 2, 1, 3 }, set._items);
        }

        [Test]
        public void HashSetPreservesSerializedItemOrderAcrossSerializationCycle()
        {
            SerializableHashSet<int> set = new() { _items = new[] { 7, 3, 9, 1 } };

            set.OnAfterDeserialize();
            set.OnBeforeSerialize();

            int[] expectedItems = { 7, 3, 9, 1 };
            CollectionAssert.AreEqual(expectedItems, set._items);
        }

        [Test]
        public void HashSetPreservesOrderAfterMultipleSerializationCycles()
        {
            SerializableHashSet<string> set = new()
            {
                _items = new[] { "delta", "alpha", "charlie", "bravo" },
            };

            for (int cycle = 0; cycle < 5; cycle++)
            {
                set.OnAfterDeserialize();
                set.OnBeforeSerialize();
            }

            string[] expectedItems = { "delta", "alpha", "charlie", "bravo" };
            CollectionAssert.AreEqual(expectedItems, set._items);
        }

        [Test]
        public void HashSetAppendsNewItemsAtEndWhilePreservingExistingOrder()
        {
            SerializableHashSet<int> set = new() { _items = new[] { 100, 50, 200 } };
            set.OnAfterDeserialize();

            set.Add(25);
            set.Add(150);
            set.OnBeforeSerialize();

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
            SerializableHashSet<int> set = new() { _items = new[] { 100, 50, 200, 75 } };
            set.OnAfterDeserialize();

            bool removed = set.Remove(50);
            set.OnBeforeSerialize();

            Assert.IsTrue(removed);
            int[] expectedItems = { 100, 200, 75 };
            CollectionAssert.AreEqual(expectedItems, set._items);
        }

        [Test]
        public void HashSetClearResultsInEmptyArrayOnNextSerialize()
        {
            SerializableHashSet<int> set = new() { _items = new[] { 1, 2, 3 } };
            set.OnAfterDeserialize();

            set.Clear();
            set.OnBeforeSerialize();

            Assert.AreEqual(0, set._items.Length);
        }

        [Test]
        public void HashSetWithDuplicatesPreservesOriginalArrayOnDeserialization()
        {
            SerializableHashSet<string> set = new() { _items = new[] { "a", "b", "a", "c" } };

            set.OnAfterDeserialize();

            Assert.IsTrue(set.PreserveSerializedEntries);
            Assert.IsTrue(set.HasDuplicatesOrNulls);
            CollectionAssert.AreEqual(new[] { "a", "b", "a", "c" }, set._items);
        }

        [Test]
        public void HashSetComplexScenarioAddRemovePreservesOrder()
        {
            SerializableHashSet<int> set = new() { _items = new[] { 10, 20, 30, 40, 50 } };
            set.OnAfterDeserialize();

            set.Remove(20);
            set.Remove(40);
            set.Add(25);
            set.Add(45);
            set.OnBeforeSerialize();

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
            SerializableSortedDictionary<int, string> dictionary = new();

            dictionary.OnBeforeSerialize();

            Assert.IsTrue(dictionary._keys != null);
            Assert.IsTrue(dictionary._values != null);
            Assert.AreEqual(0, dictionary._keys.Length);
            Assert.AreEqual(0, dictionary._values.Length);
        }

        [Test]
        public void EmptySortedSetSerializesToEmptyArray()
        {
            SerializableSortedSet<int> set = new();

            set.OnBeforeSerialize();

            Assert.IsTrue(set._items != null);
            Assert.AreEqual(0, set._items.Length);
        }

        [Test]
        public void EmptyHashSetSerializesToEmptyArray()
        {
            SerializableHashSet<int> set = new();

            set.OnBeforeSerialize();

            Assert.IsTrue(set._items != null);
            Assert.AreEqual(0, set._items.Length);
        }

        [Test]
        public void SingleItemSortedDictionaryPreservesOrder()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 42 },
                _values = new[] { "answer" },
            };

            dictionary.OnAfterDeserialize();
            dictionary.OnBeforeSerialize();

            Assert.AreEqual(1, dictionary._keys.Length);
            Assert.AreEqual(42, dictionary._keys[0]);
            Assert.AreEqual("answer", dictionary._values[0]);
        }

        [Test]
        public void SingleItemSetPreservesOrder()
        {
            SerializableHashSet<int> set = new() { _items = new[] { 42 } };

            set.OnAfterDeserialize();
            set.OnBeforeSerialize();

            Assert.AreEqual(1, set._items.Length);
            Assert.AreEqual(42, set._items[0]);
        }

        [Test]
        public void LargeSortedDictionaryPreservesOrderAcrossManyCycles()
        {
            int count = 1000;
            int[] keys = new int[count];
            string[] values = new string[count];
            for (int i = 0; i < count; i++)
            {
                keys[i] = count - i;
                values[i] = $"value_{count - i}";
            }

            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = keys,
                _values = values,
            };

            for (int cycle = 0; cycle < 3; cycle++)
            {
                dictionary.OnAfterDeserialize();
                dictionary.OnBeforeSerialize();
            }

            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(count - i, dictionary._keys[i]);
                Assert.AreEqual($"value_{count - i}", dictionary._values[i]);
            }
        }

        [Test]
        public void LargeHashSetPreservesOrderAcrossManyCycles()
        {
            int count = 1000;
            int[] items = new int[count];
            for (int i = 0; i < count; i++)
            {
                items[i] = (i * 7) % 10000;
            }

            SerializableHashSet<int> set = new() { _items = items };

            for (int cycle = 0; cycle < 3; cycle++)
            {
                set.OnAfterDeserialize();
                set.OnBeforeSerialize();
            }

            CollectionAssert.AreEqual(items, set._items);
        }

        [Test]
        public void SortedDictionaryWithNullKeysPreservesArrayForEditing()
        {
            SerializableSortedDictionary<string, int> dictionary = new()
            {
                _keys = new[] { "valid", null, "also-valid" },
                _values = new[] { 1, 2, 3 },
            };

            ExpectError(
                LogType.Warning,
                System.Text.RegularExpressions.Regex.Escape(
                    "SerializableSortedDictionary<System.String, System.Int32> skipped serialized entry at index 1 because the key reference was null."
                )
            );

            dictionary.OnAfterDeserialize();

            Assert.IsTrue(dictionary.PreserveSerializedEntries);
            Assert.IsTrue(dictionary.HasDuplicatesOrNulls);

            Assert.AreEqual(2, dictionary.Count);
            Assert.IsTrue(dictionary.ContainsKey("valid"));
            Assert.IsTrue(dictionary.ContainsKey("also-valid"));
        }

        [Test]
        public void HashSetWithNullItemsPreservesArrayForEditing()
        {
            SerializableHashSet<string> set = new()
            {
                _items = new[] { "valid", null, "also-valid" },
            };

            ExpectError(
                LogType.Warning,
                System.Text.RegularExpressions.Regex.Escape(
                    "SerializableSet<System.String> skipped serialized entry at index 1 because the value reference was null."
                )
            );

            set.OnAfterDeserialize();

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
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };
            dictionary.OnAfterDeserialize();

            dictionary[1] = "ONE_UPDATED";
            dictionary.OnBeforeSerialize();

            CollectionAssert.AreEqual(new[] { 3, 1, 2 }, dictionary._keys);
            Assert.AreEqual("three", dictionary._values[0]);
            Assert.AreEqual("ONE_UPDATED", dictionary._values[1]);
            Assert.AreEqual("two", dictionary._values[2]);
        }

        [Test]
        public void SortedDictionaryTryAddPreservesOrderWhenKeyExists()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };
            dictionary.OnAfterDeserialize();

            bool added = dictionary.TryAdd(1, "should-not-add");
            dictionary.OnBeforeSerialize();

            Assert.IsFalse(added);
            CollectionAssert.AreEqual(new[] { 3, 1, 2 }, dictionary._keys);
            Assert.AreEqual("one", dictionary._values[1]);
        }

        [Test]
        public void SortedDictionaryRemoveNonExistentKeyPreservesOrder()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };
            dictionary.OnAfterDeserialize();

            bool removed = dictionary.Remove(999);
            dictionary.OnBeforeSerialize();

            Assert.IsFalse(removed);
            CollectionAssert.AreEqual(new[] { 3, 1, 2 }, dictionary._keys);
        }

        [Test]
        public void HashSetAddDuplicateDoesNotChangeOrder()
        {
            SerializableHashSet<int> set = new() { _items = new[] { 5, 3, 7 } };
            set.OnAfterDeserialize();

            bool added = set.Add(3);
            set.OnBeforeSerialize();

            Assert.IsFalse(added);
            CollectionAssert.AreEqual(new[] { 5, 3, 7 }, set._items);
        }

        [Test]
        public void HashSetRemoveNonExistentItemPreservesOrder()
        {
            SerializableHashSet<int> set = new() { _items = new[] { 5, 3, 7 } };
            set.OnAfterDeserialize();

            bool removed = set.Remove(999);
            set.OnBeforeSerialize();

            Assert.IsFalse(removed);
            CollectionAssert.AreEqual(new[] { 5, 3, 7 }, set._items);
        }

        [Test]
        public void SortedDictionaryProtoSerializationPreservesOrder()
        {
            SerializableSortedDictionary<int, string> original = new()
            {
                _keys = new[] { 30, 10, 20 },
                _values = new[] { "thirty", "ten", "twenty" },
            };
            original.OnAfterDeserialize();

            byte[] bytes = Serializer.ProtoSerialize(original);
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(bytes.Length, 0, "Serialized bytes should not be empty");

            SerializableSortedDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableSortedDictionary<int, string>
            >(bytes);

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
            SerializableSortedSet<int> original = new() { _items = new[] { 30, 10, 20 } };
            original.OnAfterDeserialize();

            byte[] bytes = Serializer.ProtoSerialize(original);
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(bytes.Length, 0, "Serialized bytes should not be empty");

            SerializableSortedSet<int> restored = Serializer.ProtoDeserialize<
                SerializableSortedSet<int>
            >(bytes);

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
            SerializableHashSet<int> original = new() { _items = new[] { 7, 3, 9, 1 } };
            original.OnAfterDeserialize();

            byte[] bytes = Serializer.ProtoSerialize(original);
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(bytes.Length, 0, "Serialized bytes should not be empty");

            SerializableHashSet<int> restored = Serializer.ProtoDeserialize<
                SerializableHashSet<int>
            >(bytes);

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
            SerializableSortedDictionary<int, string> original = new()
            {
                _keys = new[] { 30, 10, 20 },
                _values = new[] { "thirty", "ten", "twenty" },
            };
            original.OnAfterDeserialize();

            string json = Serializer.JsonStringify(original);
            SerializableSortedDictionary<int, string> restored = Serializer.JsonDeserialize<
                SerializableSortedDictionary<int, string>
            >(json);

            CollectionAssert.AreEqual(new[] { 30, 10, 20 }, restored._keys);
            CollectionAssert.AreEqual(new[] { "thirty", "ten", "twenty" }, restored._values);
        }

        [Test]
        public void SortedSetJsonSerializationPreservesOrder()
        {
            SerializableSortedSet<int> original = new() { _items = new[] { 30, 10, 20 } };
            original.OnAfterDeserialize();

            string json = Serializer.JsonStringify(original);
            SerializableSortedSet<int> restored = Serializer.JsonDeserialize<
                SerializableSortedSet<int>
            >(json);

            CollectionAssert.AreEqual(new[] { 30, 10, 20 }, restored._items);
        }

        [Test]
        public void HashSetJsonSerializationPreservesOrder()
        {
            SerializableHashSet<int> original = new() { _items = new[] { 7, 3, 9, 1 } };
            original.OnAfterDeserialize();

            string json = Serializer.JsonStringify(original);
            Assert.IsTrue(json != null, "Serialized JSON should not be null");
            Assert.IsNotEmpty(json, "Serialized JSON should not be empty");

            SerializableHashSet<int> restored = Serializer.JsonDeserialize<
                SerializableHashSet<int>
            >(json);

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
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 100, 1, 50, 25, 75 },
                _values = new[] { "hundred", "one", "fifty", "twenty-five", "seventy-five" },
            };

            for (int i = 0; i < 10; i++)
            {
                dictionary.OnAfterDeserialize();
                dictionary.OnBeforeSerialize();
            }

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
            SerializableSortedSet<int> set = new() { _items = new[] { 100, 1, 50, 25, 75 } };

            for (int i = 0; i < 10; i++)
            {
                set.OnAfterDeserialize();
                set.OnBeforeSerialize();
            }

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
            SerializableHashSet<int> set = new() { _items = new[] { 42, 7, 99, 13, 55 } };

            for (int i = 0; i < 10; i++)
            {
                set.OnAfterDeserialize();
                set.OnBeforeSerialize();
            }

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
            SerializableSortedDictionary<string, int> dictionary = new()
            {
                _keys = new[] { "zebra", "apple", "mango", "banana", "cherry" },
                _values = new[] { 1, 2, 3, 4, 5 },
            };

            for (int i = 0; i < 5; i++)
            {
                dictionary.OnAfterDeserialize();
                dictionary.OnBeforeSerialize();
            }

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
            SerializableSortedSet<string> set = new()
            {
                _items = new[] { "zebra", "apple", "mango", "banana", "cherry" },
            };

            for (int i = 0; i < 5; i++)
            {
                set.OnAfterDeserialize();
                set.OnBeforeSerialize();
            }

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
            SerializableHashSet<int> set = new() { _items = initial };
            set.OnAfterDeserialize();

            foreach (int item in toRemove)
            {
                set.Remove(item);
            }
            foreach (int item in toAdd)
            {
                set.Add(item);
            }
            set.OnBeforeSerialize();

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
            SerializableSortedDictionary<int, string> dict = new()
            {
                _keys = initialKeys,
                _values = initialValues,
            };
            dict.OnAfterDeserialize();

            foreach (int key in keysToRemove)
            {
                dict.Remove(key);
            }
            for (int i = 0; i < keysToAdd.Length; i++)
            {
                dict.Add(keysToAdd[i], valuesToAdd[i]);
            }
            dict.OnBeforeSerialize();

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
            SerializableHashSet<int> set = new() { _items = new[] { 10, 20, 30, 40, 50 } };
            set.OnAfterDeserialize();

            set.Remove(20);
            set.Remove(40);
            set.Add(25);
            byte[] bytes = Serializer.ProtoSerialize(set);

            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(bytes.Length, 0, "Serialized bytes should not be empty");

            SerializableHashSet<int> restored = Serializer.ProtoDeserialize<
                SerializableHashSet<int>
            >(bytes);

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
            SerializableHashSet<int> set = new() { _items = new[] { 10, 20, 30, 40, 50 } };
            set.OnAfterDeserialize();

            set.Remove(20);
            set.Remove(40);
            set.Add(25);
            string json = Serializer.JsonStringify(set);
            SerializableHashSet<int> restored = Serializer.JsonDeserialize<
                SerializableHashSet<int>
            >(json);

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
            SerializableHashSet<int> set = new() { _items = new[] { 100, 50, 75, 25 } };
            set.OnAfterDeserialize();

            set.Remove(50);
            set.Add(60);

            for (int i = 0; i < 5; i++)
            {
                set.OnBeforeSerialize();
                set.OnAfterDeserialize();
            }

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
            SerializableSortedDictionary<int, string> dict = new()
            {
                _keys = new[] { 30, 10, 20, 40 },
                _values = new[] { "thirty", "ten", "twenty", "forty" },
            };
            dict.OnAfterDeserialize();

            dict.Remove(10);
            dict.Add(15, "fifteen");
            byte[] bytes = Serializer.ProtoSerialize(dict);

            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(bytes.Length, 0, "Serialized bytes should not be empty");

            SerializableSortedDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableSortedDictionary<int, string>
            >(bytes);

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
            SerializableSortedDictionary<int, string> dict = new()
            {
                _keys = new[] { 30, 10, 20, 40 },
                _values = new[] { "thirty", "ten", "twenty", "forty" },
            };
            dict.OnAfterDeserialize();

            dict.Remove(10);
            dict.Add(15, "fifteen");
            string json = Serializer.JsonStringify(dict);
            SerializableSortedDictionary<int, string> restored = Serializer.JsonDeserialize<
                SerializableSortedDictionary<int, string>
            >(json);

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
            SerializableHashSet<int> set = new() { _items = new[] { 1, 2, 3, 4, 5 } };
            set.OnAfterDeserialize();

            set.Clear();
            set.Add(100);
            set.Add(50);
            set.Add(75);
            set.OnBeforeSerialize();

            string actualItems = set._items != null ? string.Join(", ", set._items) : "null";
            Assert.AreEqual(
                3,
                set._items.Length,
                $"Expected 3 items after clear and add, got {set._items.Length}. Items: [{actualItems}]"
            );

            CollectionAssert.AreEqual(
                new[] { 100, 50, 75 },
                set._items,
                $"Expected items in insertion order [100, 50, 75], got [{actualItems}]"
            );
        }

        [Test]
        public void HashSetAddMultipleItemsPreservesInsertionOrder()
        {
            SerializableHashSet<int> set = new();
            set.OnAfterDeserialize();

            set.Add(7);
            set.Add(3);
            set.Add(9);
            set.Add(1);
            set.OnBeforeSerialize();

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
            SerializableHashSet<int> set = new() { _items = new[] { 100, 50, 200 } };
            set.OnAfterDeserialize();

            set.Add(25);
            set.Add(150);
            set.OnBeforeSerialize();

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
            SerializableHashSet<int> set = new() { _items = new[] { 10, 20, 30, 40, 50 } };
            set.OnAfterDeserialize();

            set.Remove(20);
            set.Add(25);
            set.Remove(40);
            set.Add(45);
            set.Add(15);
            set.OnBeforeSerialize();

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
            SerializableHashSet<int> original = new() { _items = new[] { 7, 3, 9, 1 } };
            original.OnAfterDeserialize();

            string json = Serializer.JsonStringify(original);

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
            SerializableSortedDictionary<int, string> original = new()
            {
                _keys = new[] { 30, 10, 20 },
                _values = new[] { "thirty", "ten", "twenty" },
            };
            original.OnAfterDeserialize();

            string json = Serializer.JsonStringify(original);

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
            SerializableHashSet<int> set = new() { _items = new[] { 10, 20, 30 } };
            set.OnAfterDeserialize();

            set.UnionWith(new[] { 5, 25, 35 });
            set.OnBeforeSerialize();

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
            SerializableSortedDictionary<int, string> dict = new()
            {
                _keys = new[] { 30, 10, 20 },
                _values = new[] { "thirty", "ten", "twenty" },
            };
            dict.OnAfterDeserialize();

            dict[10] = "TEN_UPDATED";
            dict.OnBeforeSerialize();

            CollectionAssert.AreEqual(
                new[] { 30, 10, 20 },
                dict._keys,
                "Key order should be preserved after value update"
            );
            Assert.AreEqual("TEN_UPDATED", dict.ValueFor(10), "Value should be updated");
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
            SerializableHashSet<int> original = new() { _items = items };
            original.OnAfterDeserialize();

            string originalItemsStr =
                original._items != null ? string.Join(", ", original._items) : "null";
            Assert.IsTrue(
                original._items != null,
                $"Original _items should not be null before serialization"
            );

            byte[] bytes = Serializer.ProtoSerialize(original);

            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(
                bytes.Length,
                0,
                $"Serialized bytes should not be empty for {items.Length} items"
            );

            string hexDump =
                0 < bytes.Length
                    ? string.Join(" ", bytes.Take(20).Select(b => b.ToString("X2")))
                    : "empty";

            SerializableHashSet<int> restored = Serializer.ProtoDeserialize<
                SerializableHashSet<int>
            >(bytes);

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
            SerializableSortedSet<int> original = new() { _items = items };
            original.OnAfterDeserialize();

            byte[] bytes = Serializer.ProtoSerialize(original);

            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(
                bytes.Length,
                0,
                $"Serialized bytes should not be empty for {items.Length} items"
            );

            SerializableSortedSet<int> restored = Serializer.ProtoDeserialize<
                SerializableSortedSet<int>
            >(bytes);

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
            SerializableSortedDictionary<int, string> original = new()
            {
                _keys = keys,
                _values = values,
            };
            original.OnAfterDeserialize();

            byte[] bytes = Serializer.ProtoSerialize(original);

            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(
                bytes.Length,
                0,
                $"Serialized bytes should not be empty for {keys.Length} entries"
            );

            SerializableSortedDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableSortedDictionary<int, string>
            >(bytes);

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
            SerializableHashSet<int> original = new();
            original.OnAfterDeserialize();

            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableHashSet<int> restored = Serializer.ProtoDeserialize<
                SerializableHashSet<int>
            >(bytes);

            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.AreEqual(0, restored.Count, "Restored set should be empty");
        }

        [Test]
        public void EmptySortedSetProtoSerializationRoundTrips()
        {
            SerializableSortedSet<int> original = new();
            original.OnAfterDeserialize();

            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableSortedSet<int> restored = Serializer.ProtoDeserialize<
                SerializableSortedSet<int>
            >(bytes);

            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.AreEqual(0, restored.Count, "Restored set should be empty");
        }

        [Test]
        public void EmptySortedDictionaryProtoSerializationRoundTrips()
        {
            SerializableSortedDictionary<int, string> original = new();
            original.OnAfterDeserialize();

            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableSortedDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableSortedDictionary<int, string>
            >(bytes);

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
            SerializableHashSet<int> original = new() { _items = items };
            original.OnAfterDeserialize();

            Assert.IsTrue(
                original._items != null,
                $"Original _items should not be null before serialization. Count={original.Count}"
            );
            Assert.AreEqual(
                items.Length,
                original._items.Length,
                $"Original _items.Length should match. Expected={items.Length}, Actual={original._items.Length}"
            );

            byte[] bytes = Serializer.ProtoSerialize(original);

            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(
                bytes.Length,
                0,
                $"Serialized bytes should not be empty for {items.Length} items"
            );

            SerializableHashSet<int> restored = Serializer.ProtoDeserialize<
                SerializableHashSet<int>
            >(bytes);

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
            SerializableSortedSet<int> original = new() { _items = items };
            original.OnAfterDeserialize();

            Assert.IsTrue(
                original._items != null,
                $"Original _items should not be null before serialization. Count={original.Count}"
            );
            Assert.AreEqual(
                items.Length,
                original._items.Length,
                $"Original _items.Length should match. Expected={items.Length}, Actual={original._items.Length}"
            );

            byte[] bytes = Serializer.ProtoSerialize(original);

            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(
                bytes.Length,
                0,
                $"Serialized bytes should not be empty for {items.Length} items"
            );

            SerializableSortedSet<int> restored = Serializer.ProtoDeserialize<
                SerializableSortedSet<int>
            >(bytes);

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
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };

            dictionary.OnAfterDeserialize();
            dictionary.OnBeforeSerialize();

            int[] expectedKeys = { 3, 1, 2 };
            CollectionAssert.AreEqual(expectedKeys, dictionary._keys);
        }

        [Test]
        public void DictionaryPreservesSerializedKeyOrderAfterMultipleSerializationCycles()
        {
            SerializableDictionary<string, int> dictionary = new()
            {
                _keys = new[] { "zebra", "apple", "mango" },
                _values = new[] { 1, 2, 3 },
            };

            for (int cycle = 0; cycle < 5; cycle++)
            {
                dictionary.OnAfterDeserialize();
                dictionary.OnBeforeSerialize();
            }

            string[] expectedKeys = { "zebra", "apple", "mango" };
            CollectionAssert.AreEqual(expectedKeys, dictionary._keys);
        }

        [Test]
        public void DictionaryPreservesOrderWhenValueIsUpdated()
        {
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 5, 2, 8, 1 },
                _values = new[] { "five", "two", "eight", "one" },
            };
            dictionary.OnAfterDeserialize();

            dictionary[2] = "TWO_UPDATED";
            dictionary.OnBeforeSerialize();

            int[] expectedKeys = { 5, 2, 8, 1 };
            CollectionAssert.AreEqual(expectedKeys, dictionary._keys);
            Assert.AreEqual("TWO_UPDATED", dictionary._values[1]);
        }

        [Test]
        public void DictionaryAppendsNewKeysAtEndWhilePreservingExistingOrder()
        {
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 10, 5, 20 },
                _values = new[] { "ten", "five", "twenty" },
            };
            dictionary.OnAfterDeserialize();

            dictionary.Add(1, "one");
            dictionary.OnBeforeSerialize();

            Assert.AreEqual(4, dictionary._keys.Length);
            Assert.AreEqual(10, dictionary._keys[0]);
            Assert.AreEqual(5, dictionary._keys[1]);
            Assert.AreEqual(20, dictionary._keys[2]);
            Assert.AreEqual(1, dictionary._keys[3]);
        }

        [Test]
        public void DictionaryRemovesKeyWhilePreservingRemainingOrder()
        {
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 10, 5, 20, 15 },
                _values = new[] { "ten", "five", "twenty", "fifteen" },
            };
            dictionary.OnAfterDeserialize();

            bool removed = dictionary.Remove(5);
            dictionary.OnBeforeSerialize();

            Assert.IsTrue(removed);
            int[] expectedKeys = { 10, 20, 15 };
            CollectionAssert.AreEqual(expectedKeys, dictionary._keys);
        }

        [Test]
        public void DictionaryClearResultsInEmptyArraysOnNextSerialize()
        {
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };
            dictionary.OnAfterDeserialize();

            dictionary.Clear();
            dictionary.OnBeforeSerialize();

            Assert.AreEqual(0, dictionary._keys.Length);
            Assert.AreEqual(0, dictionary._values.Length);
        }

        [Test]
        public void DictionaryWithDuplicateKeysPreservesOriginalArrayOnDeserialization()
        {
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 1, 2, 1, 3 },
                _values = new[] { "one-first", "two", "one-second", "three" },
            };

            dictionary.OnAfterDeserialize();

            Assert.IsTrue(dictionary.PreserveSerializedEntries);
            Assert.IsTrue(dictionary.HasDuplicatesOrNulls);
            CollectionAssert.AreEqual(new[] { 1, 2, 1, 3 }, dictionary._keys);
        }

        [Test]
        public void DictionaryIndexerUpdatesValueInPlacePreservingKeyPosition()
        {
            SerializableDictionary<string, int> dictionary = new()
            {
                _keys = new[] { "z", "a", "m" },
                _values = new[] { 1, 2, 3 },
            };
            dictionary.OnAfterDeserialize();

            dictionary["z"] = 100;
            dictionary["a"] = 200;
            dictionary.OnBeforeSerialize();

            CollectionAssert.AreEqual(new[] { "z", "a", "m" }, dictionary._keys);
            Assert.AreEqual(100, dictionary._values[0]);
            Assert.AreEqual(200, dictionary._values[1]);
            Assert.AreEqual(3, dictionary._values[2]);
        }

        [Test]
        public void DictionaryComplexScenarioAddRemoveUpdatePreservesOrder()
        {
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 100, 50, 200, 75 },
                _values = new[] { "hundred", "fifty", "two-hundred", "seventy-five" },
            };
            dictionary.OnAfterDeserialize();

            dictionary[50] = "FIFTY_UPDATED";
            dictionary.Remove(200);
            dictionary.Add(25, "twenty-five");
            dictionary.Add(300, "three-hundred");
            dictionary.OnBeforeSerialize();

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
            SerializableDictionary<int, string> dictionary = new();

            dictionary.OnBeforeSerialize();

            Assert.IsTrue(dictionary._keys != null);
            Assert.IsTrue(dictionary._values != null);
            Assert.AreEqual(0, dictionary._keys.Length);
            Assert.AreEqual(0, dictionary._values.Length);
        }

        [Test]
        public void SingleItemDictionaryPreservesOrder()
        {
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 42 },
                _values = new[] { "answer" },
            };

            dictionary.OnAfterDeserialize();
            dictionary.OnBeforeSerialize();

            Assert.AreEqual(1, dictionary._keys.Length);
            Assert.AreEqual(42, dictionary._keys[0]);
            Assert.AreEqual("answer", dictionary._values[0]);
        }

        [Test]
        public void LargeDictionaryPreservesOrderAcrossManyCycles()
        {
            int count = 1000;
            int[] keys = new int[count];
            string[] values = new string[count];
            for (int i = 0; i < count; i++)
            {
                keys[i] = count - i;
                values[i] = $"value_{count - i}";
            }

            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = keys,
                _values = values,
            };

            for (int cycle = 0; cycle < 3; cycle++)
            {
                dictionary.OnAfterDeserialize();
                dictionary.OnBeforeSerialize();
            }

            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(count - i, dictionary._keys[i]);
                Assert.AreEqual($"value_{count - i}", dictionary._values[i]);
            }
        }

        [Test]
        public void DictionaryWithNullKeysPreservesArrayForEditing()
        {
            SerializableDictionary<string, int> dictionary = new()
            {
                _keys = new[] { "valid", null, "also-valid" },
                _values = new[] { 1, 2, 3 },
            };

            ExpectError(
                LogType.Warning,
                System.Text.RegularExpressions.Regex.Escape(
                    "SerializableDictionary<System.String, System.Int32> skipped serialized entry at index 1 because the key reference was null."
                )
            );

            dictionary.OnAfterDeserialize();

            Assert.IsTrue(dictionary.PreserveSerializedEntries);
            Assert.IsTrue(dictionary.HasDuplicatesOrNulls);

            Assert.AreEqual(2, dictionary.Count);
            Assert.IsTrue(dictionary.ContainsKey("valid"));
            Assert.IsTrue(dictionary.ContainsKey("also-valid"));
        }

        [Test]
        public void DictionaryAddingExistingKeyUpdatesValuePreservesOrder()
        {
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };
            dictionary.OnAfterDeserialize();

            dictionary[1] = "ONE_UPDATED";
            dictionary.OnBeforeSerialize();

            CollectionAssert.AreEqual(new[] { 3, 1, 2 }, dictionary._keys);
            Assert.AreEqual("three", dictionary._values[0]);
            Assert.AreEqual("ONE_UPDATED", dictionary._values[1]);
            Assert.AreEqual("two", dictionary._values[2]);
        }

        [Test]
        public void DictionaryTryAddPreservesOrderWhenKeyExists()
        {
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };
            dictionary.OnAfterDeserialize();

            bool added = dictionary.TryAdd(1, "should-not-add");
            dictionary.OnBeforeSerialize();

            Assert.IsFalse(added);
            CollectionAssert.AreEqual(new[] { 3, 1, 2 }, dictionary._keys);
            Assert.AreEqual("one", dictionary._values[1]);
        }

        [Test]
        public void DictionaryRemoveNonExistentKeyPreservesOrder()
        {
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 3, 1, 2 },
                _values = new[] { "three", "one", "two" },
            };
            dictionary.OnAfterDeserialize();

            bool removed = dictionary.Remove(999);
            dictionary.OnBeforeSerialize();

            Assert.IsFalse(removed);
            CollectionAssert.AreEqual(new[] { 3, 1, 2 }, dictionary._keys);
        }

        [Test]
        public void DictionaryProtoSerializationPreservesOrder()
        {
            SerializableDictionary<int, string> original = new()
            {
                _keys = new[] { 30, 10, 20 },
                _values = new[] { "thirty", "ten", "twenty" },
            };
            original.OnAfterDeserialize();

            byte[] bytes = Serializer.ProtoSerialize(original);
            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(bytes.Length, 0, "Serialized bytes should not be empty");

            SerializableDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableDictionary<int, string>
            >(bytes);

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
            SerializableDictionary<int, string> original = new()
            {
                _keys = new[] { 30, 10, 20 },
                _values = new[] { "thirty", "ten", "twenty" },
            };
            original.OnAfterDeserialize();

            string json = Serializer.JsonStringify(original);
            SerializableDictionary<int, string> restored = Serializer.JsonDeserialize<
                SerializableDictionary<int, string>
            >(json);

            CollectionAssert.AreEqual(new[] { 30, 10, 20 }, restored._keys);
            CollectionAssert.AreEqual(new[] { "thirty", "ten", "twenty" }, restored._values);
        }

        [Test]
        public void DictionaryDomainReloadDoesNotReorderKeys()
        {
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 100, 1, 50, 25, 75 },
                _values = new[] { "hundred", "one", "fifty", "twenty-five", "seventy-five" },
            };

            for (int i = 0; i < 10; i++)
            {
                dictionary.OnAfterDeserialize();
                dictionary.OnBeforeSerialize();
            }

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
            SerializableDictionary<string, int> dictionary = new()
            {
                _keys = new[] { "zebra", "apple", "mango", "banana", "cherry" },
                _values = new[] { 1, 2, 3, 4, 5 },
            };

            for (int i = 0; i < 5; i++)
            {
                dictionary.OnAfterDeserialize();
                dictionary.OnBeforeSerialize();
            }

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
            SerializableDictionary<int, string> dict = new()
            {
                _keys = initialKeys,
                _values = initialValues,
            };
            dict.OnAfterDeserialize();

            foreach (int key in keysToRemove)
            {
                dict.Remove(key);
            }
            for (int i = 0; i < keysToAdd.Length; i++)
            {
                dict.Add(keysToAdd[i], valuesToAdd[i]);
            }
            dict.OnBeforeSerialize();

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
            SerializableDictionary<int, string> dict = new()
            {
                _keys = new[] { 30, 10, 20, 40 },
                _values = new[] { "thirty", "ten", "twenty", "forty" },
            };
            dict.OnAfterDeserialize();

            dict.Remove(10);
            dict.Add(15, "fifteen");
            byte[] bytes = Serializer.ProtoSerialize(dict);

            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(bytes.Length, 0, "Serialized bytes should not be empty");

            SerializableDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableDictionary<int, string>
            >(bytes);

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
            SerializableDictionary<int, string> dict = new()
            {
                _keys = new[] { 30, 10, 20, 40 },
                _values = new[] { "thirty", "ten", "twenty", "forty" },
            };
            dict.OnAfterDeserialize();

            dict.Remove(10);
            dict.Add(15, "fifteen");
            string json = Serializer.JsonStringify(dict);
            SerializableDictionary<int, string> restored = Serializer.JsonDeserialize<
                SerializableDictionary<int, string>
            >(json);

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
            SerializableDictionary<int, string> dict = new()
            {
                _keys = new[] { 100, 50, 75, 25 },
                _values = new[] { "hundred", "fifty", "seventy-five", "twenty-five" },
            };
            dict.OnAfterDeserialize();

            dict.Remove(50);
            dict.Add(60, "sixty");

            for (int i = 0; i < 5; i++)
            {
                dict.OnBeforeSerialize();
                dict.OnAfterDeserialize();
            }

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
            SerializableDictionary<int, string> original = new()
            {
                _keys = new[] { 30, 10, 20 },
                _values = new[] { "thirty", "ten", "twenty" },
            };
            original.OnAfterDeserialize();

            string json = Serializer.JsonStringify(original);

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
            SerializableDictionary<int, string> dict = new()
            {
                _keys = new[] { 30, 10, 20 },
                _values = new[] { "thirty", "ten", "twenty" },
            };
            dict.OnAfterDeserialize();

            dict[10] = "TEN_UPDATED";
            dict.OnBeforeSerialize();

            CollectionAssert.AreEqual(
                new[] { 30, 10, 20 },
                dict._keys,
                "Key order should be preserved after value update"
            );
            Assert.AreEqual("TEN_UPDATED", dict.ValueFor(10), "Value should be updated");
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
            SerializableDictionary<int, string> original = new() { _keys = keys, _values = values };
            original.OnAfterDeserialize();

            string originalKeysStr =
                original._keys != null ? string.Join(", ", original._keys) : "null";
            Assert.IsTrue(
                original._keys != null,
                "Original _keys should not be null before serialization"
            );

            byte[] bytes = Serializer.ProtoSerialize(original);

            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(
                bytes.Length,
                0,
                $"Serialized bytes should not be empty for {keys.Length} entries"
            );

            SerializableDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableDictionary<int, string>
            >(bytes);

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
            SerializableDictionary<int, string> original = new();
            original.OnAfterDeserialize();

            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableDictionary<int, string>
            >(bytes);

            Assert.IsTrue(restored != null, "Restored object should not be null");
            Assert.AreEqual(0, restored.Count, "Restored dictionary should be empty");
        }

        [Test]
        public void DictionaryClearThenAddPreservesNewOrder()
        {
            SerializableDictionary<int, string> dict = new()
            {
                _keys = new[] { 1, 2, 3, 4, 5 },
                _values = new[] { "one", "two", "three", "four", "five" },
            };
            dict.OnAfterDeserialize();

            dict.Clear();
            dict.Add(100, "hundred");
            dict.Add(50, "fifty");
            dict.Add(75, "seventy-five");
            dict.OnBeforeSerialize();

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
            SerializableDictionary<int, string> dict = new();
            dict.OnAfterDeserialize();

            dict.Add(7, "seven");
            dict.Add(3, "three");
            dict.Add(9, "nine");
            dict.Add(1, "one");
            dict.OnBeforeSerialize();

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
            SerializableDictionary<int, string> dict = new()
            {
                _keys = new[] { 100, 50, 200 },
                _values = new[] { "hundred", "fifty", "two-hundred" },
            };
            dict.OnAfterDeserialize();

            dict.Add(25, "twenty-five");
            dict.Add(150, "one-fifty");
            dict.OnBeforeSerialize();

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
            SerializableDictionary<int, string> dict = new()
            {
                _keys = new[] { 10, 20, 30, 40, 50 },
                _values = new[] { "ten", "twenty", "thirty", "forty", "fifty" },
            };
            dict.OnAfterDeserialize();

            dict.Remove(20);
            dict.Add(25, "twenty-five");
            dict.Remove(40);
            dict.Add(45, "forty-five");
            dict.Add(15, "fifteen");
            dict.OnBeforeSerialize();

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
            SerializableDictionary<int, string> original = new() { _keys = keys, _values = values };
            original.OnAfterDeserialize();

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

            byte[] bytes = Serializer.ProtoSerialize(original);

            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(
                bytes.Length,
                0,
                $"Serialized bytes should not be empty for {keys.Length} entries"
            );

            SerializableDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableDictionary<int, string>
            >(bytes);

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
            SerializableSortedDictionary<int, string> original = new()
            {
                _keys = keys,
                _values = values,
            };
            original.OnAfterDeserialize();

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

            byte[] bytes = Serializer.ProtoSerialize(original);

            Assert.IsTrue(bytes != null, "Serialized bytes should not be null");
            Assert.Greater(
                bytes.Length,
                0,
                $"Serialized bytes should not be empty for {keys.Length} entries"
            );

            SerializableSortedDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableSortedDictionary<int, string>
            >(bytes);

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
            SerializableHashSet<int> original = new();
            original.Add(7);
            original.Add(3);
            original.Add(9);
            original.Add(1);

            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableHashSet<int> restored = Serializer.ProtoDeserialize<
                SerializableHashSet<int>
            >(bytes);

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
            SerializableDictionary<int, string> original = new();
            original[5] = "five";
            original[2] = "two";
            original[8] = "eight";
            original[1] = "one";

            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableDictionary<int, string>
            >(bytes);

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
            SerializableHashSet<string> original = new()
            {
                _items = new[] { "apple", "banana", "cherry" },
            };
            original.OnAfterDeserialize();

            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableHashSet<string> restored = Serializer.ProtoDeserialize<
                SerializableHashSet<string>
            >(bytes);

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
            SerializableSortedSet<string> original = new()
            {
                _items = new[] { "zebra", "apple", "mango" },
            };
            original.OnAfterDeserialize();

            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableSortedSet<string> restored = Serializer.ProtoDeserialize<
                SerializableSortedSet<string>
            >(bytes);

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
            SerializableDictionary<string, int> original = new()
            {
                _keys = new[] { "gamma", "alpha", "beta" },
                _values = new[] { 3, 1, 2 },
            };
            original.OnAfterDeserialize();

            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableDictionary<string, int> restored = Serializer.ProtoDeserialize<
                SerializableDictionary<string, int>
            >(bytes);

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
            yield return new TestCaseData(
                new[] { 100 },
                new[] { "hundred" },
                new int[0],
                new[] { 50, 75, 25 },
                new[] { "fifty", "seventy-five", "twenty-five" },
                new[] { 100, 50, 75, 25 }
            ).SetName("Dictionary.SingleInitialWithMultipleAdds");

            yield return new TestCaseData(
                new[] { 10, 20, 30 },
                new[] { "ten", "twenty", "thirty" },
                new[] { 10 },
                new[] { 5 },
                new[] { "five" },
                new[] { 20, 30, 5 }
            ).SetName("Dictionary.RemoveFirstAddNew");

            yield return new TestCaseData(
                new[] { 10, 20, 30 },
                new[] { "ten", "twenty", "thirty" },
                new[] { 30 },
                new[] { 40, 50 },
                new[] { "forty", "fifty" },
                new[] { 10, 20, 40, 50 }
            ).SetName("Dictionary.RemoveLastAddNew");

            yield return new TestCaseData(
                new[] { 10, 20, 30, 40, 50 },
                new[] { "a", "b", "c", "d", "e" },
                new[] { 20, 40 },
                new[] { 15, 25, 35 },
                new[] { "fifteen", "twenty-five", "thirty-five" },
                new[] { 10, 30, 50, 15, 25, 35 }
            ).SetName("Dictionary.RemoveMultipleMiddleAddMultipleNew");

            yield return new TestCaseData(
                new int[0],
                new string[0],
                new int[0],
                new[] { 3, 1, 4, 1, 5 },
                new[] { "three", "one", "four", "one-dup", "five" },
                new[] { 3, 1, 4, 5 }
            ).SetName("Dictionary.StartEmptyAddWithDuplicateAttempts");

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
            SerializableDictionary<int, string> dict = new();
            if (0 < initialKeys.Length)
            {
                dict._keys = initialKeys;
                dict._values = initialValues;
                dict.OnAfterDeserialize();
            }

            string initialState =
                $"Initial: keys=[{string.Join(", ", initialKeys)}], values=[{string.Join(", ", initialValues)}]";
            string removeState = $"Removing: [{string.Join(", ", keysToRemove)}]";
            string addState =
                $"Adding: keys=[{string.Join(", ", keysToAdd)}], values=[{string.Join(", ", valuesToAdd)}]";

            foreach (int key in keysToRemove)
            {
                dict.Remove(key);
            }

            for (int i = 0; i < keysToAdd.Length; i++)
            {
                dict.TryAdd(keysToAdd[i], valuesToAdd[i]);
            }

            dict.OnBeforeSerialize();

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
            SerializableDictionary<int, string> original = new()
            {
                _keys = new[] { 100, 25, 75, 50 },
                _values = new[] { "hundred", "twenty-five", "seventy-five", "fifty" },
            };
            original.OnAfterDeserialize();

            original.Remove(25);
            original.Add(30, "thirty");
            original.Add(60, "sixty");

            int[] expectedOrder = { 100, 75, 50, 30, 60 };

            string json = Serializer.JsonStringify(original);
            SerializableDictionary<int, string> restored = Serializer.JsonDeserialize<
                SerializableDictionary<int, string>
            >(json);

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
            SerializableDictionary<int, string> original = new()
            {
                _keys = new[] { 100, 25, 75, 50 },
                _values = new[] { "hundred", "twenty-five", "seventy-five", "fifty" },
            };
            original.OnAfterDeserialize();

            original.Remove(25);
            original.Add(30, "thirty");
            original.Add(60, "sixty");

            int[] expectedOrder = { 100, 75, 50, 30, 60 };

            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableDictionary<int, string>
            >(bytes);

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
            SerializableDictionary<int, string> dict = new()
            {
                _keys = new[] { 50, 30, 70 },
                _values = new[] { "fifty", "thirty", "seventy" },
            };
            dict.OnAfterDeserialize();

            dict[10] = "ten";
            dict[90] = "ninety";
            dict.OnBeforeSerialize();

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
            SerializableDictionary<int, string> dict = new()
            {
                _keys = new[] { 50, 30, 70 },
                _values = new[] { "fifty", "thirty", "seventy" },
            };
            dict.OnAfterDeserialize();

            dict[30] = "THIRTY_UPDATED";
            dict[70] = "SEVENTY_UPDATED";
            dict.OnBeforeSerialize();

            int[] expectedKeys = { 50, 30, 70 };
            string actualKeys = dict._keys != null ? string.Join(", ", dict._keys) : "null";
            CollectionAssert.AreEqual(
                expectedKeys,
                dict._keys,
                $"Expected [{string.Join(", ", expectedKeys)}], got [{actualKeys}]"
            );

            Assert.AreEqual("THIRTY_UPDATED", dict._values[1]);
            Assert.AreEqual("SEVENTY_UPDATED", dict._values[2]);
        }
    }
}
