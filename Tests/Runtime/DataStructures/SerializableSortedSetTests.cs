// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializableSortedSetTests : CommonTestBase
    {
        [Test]
        public void SortedHashSetEnumeratesInComparerOrder()
        {
            SerializableSortedSet<int> set = new() { 5, 1, 3 };

            int[] expected = { 1, 3, 5 };
            int index = 0;
            foreach (int value in set)
            {
                Assert.Less(index, expected.Length);
                Assert.AreEqual(expected[index], value);
                index++;
            }

            Assert.AreEqual(expected.Length, index);
        }

        [Test]
        public void ToSortedSetReturnsIndependentCopy()
        {
            SerializableSortedSet<int> set = new();
            bool addedFive = set.Add(5);
            bool addedTwo = set.Add(2);

            Assert.IsTrue(addedFive);
            Assert.IsTrue(addedTwo);

            SortedSet<int> copy = set.ToSortedSet();

            Assert.AreEqual(set.Count, copy.Count);

            bool firstAssigned = false;
            int firstValue = 0;
            foreach (int value in copy)
            {
                if (!firstAssigned)
                {
                    firstValue = value;
                    firstAssigned = true;
                }
            }

            Assert.IsTrue(firstAssigned);
            Assert.AreEqual(2, firstValue);

            bool copyAdded = copy.Add(10);
            Assert.IsTrue(copyAdded);
            Assert.IsFalse(set.Contains(10));

            bool setAdded = set.Add(12);
            Assert.IsTrue(setAdded);
            Assert.IsFalse(copy.Contains(12));
        }

        [Test]
        public void SortedHashSetProtoRoundTripRetainsOrdering()
        {
            SerializableSortedSet<string> original = new(new[] { "kiwi", "apple", "mango" });

            byte[] payload = Serializer.ProtoSerialize(original);
            SerializableSortedSet<string> roundTrip = Serializer.ProtoDeserialize<
                SerializableSortedSet<string>
            >(payload);

            string[] expected = roundTrip.ToArray();
            Array.Sort(expected, StringComparer.Ordinal);

            int index = 0;
            foreach (string value in roundTrip)
            {
                Assert.Less(index, expected.Length);
                Assert.AreEqual(expected[index], value);
                index++;
            }

            Assert.AreEqual(expected.Length, index);
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void SortedHashSetJsonRoundTripRetainsOrdering()
        {
            SerializableSortedSet<int> original = new() { 42, -5, 99 };

            string json = Serializer.JsonStringify(original);
            SerializableSortedSet<int> roundTrip = Serializer.JsonDeserialize<
                SerializableSortedSet<int>
            >(json);

            int[] expected = roundTrip.ToArray();
            Array.Sort(expected);

            int index = 0;
            foreach (int value in roundTrip)
            {
                Assert.Less(index, expected.Length);
                Assert.AreEqual(expected[index], value);
                index++;
            }

            Assert.AreEqual(expected.Length, index);
        }

        [Test]
        public void UnityDeserializationPreservesDuplicateSerializedEntries()
        {
            SerializableSortedSet<int> set = new() { _items = new[] { 1, 1, 2 } };
            set.OnAfterDeserialize();

            int[] cached = set.SerializedItems;
            Assert.IsTrue(
                cached != null,
                "Duplicate entries must keep serialized cache for inspector."
            );

            CollectionAssert.AreEqual(new[] { 1, 1, 2 }, cached);
            Assert.AreEqual(2, set.Count);
            Assert.IsTrue(set.Contains(1));
            Assert.IsTrue(set.Contains(2));
        }

        [Test]
        public void CustomComparerDuplicatesPreserveSerializedItems()
        {
            SerializableSortedSet<CaseInsensitiveString> set = new();
            CaseInsensitiveString[] serializedItems = { new("ALPHA"), new("alpha"), new("bravo") };
            set._items = serializedItems;

            set.OnAfterDeserialize();

            Assert.AreEqual(2, set.Count);
            Assert.IsTrue(set.Contains(new CaseInsensitiveString("alpha")));
            Assert.IsTrue(set.Contains(new CaseInsensitiveString("BRAVO")));
            Assert.AreSame(
                serializedItems,
                set.SerializedItems,
                "Comparer-driven duplicates must keep serialized cache."
            );
            Assert.IsTrue(set.PreserveSerializedEntries);
        }

        [Test]
        public void SortedSetMutationsClearPreserveFlagButKeepArraysForOrder()
        {
            SerializableSortedSet<int> set = new();

            int[] serializedItems = { 3, 3 };
            set._items = serializedItems;

            set.OnAfterDeserialize();
            Assert.IsTrue(
                set.PreserveSerializedEntries,
                "PreserveSerializedEntries should be true after OnAfterDeserialize."
            );
            Assert.IsTrue(
                set.HasDuplicatesOrNulls,
                "HasDuplicatesOrNulls should be true when duplicate items exist."
            );
            Assert.AreEqual(
                1,
                set.Count,
                "Set should have only 1 unique item (duplicates ignored)."
            );

            set.Add(4);

            bool preserveAfterAdd = set.PreserveSerializedEntries;
            bool hasDuplicatesAfterAdd = set.HasDuplicatesOrNulls;
            int[] storedItemsAfterAdd = set.SerializedItems;

            Assert.IsFalse(preserveAfterAdd, "Preserve flag should clear after set mutation.");
            Assert.IsFalse(
                hasDuplicatesAfterAdd,
                "HasDuplicatesOrNulls should be cleared after mutation (MarkSerializationCacheDirty)."
            );
            // Arrays are preserved for order maintenance, not nulled
            Assert.IsTrue(
                storedItemsAfterAdd != null,
                "Serialized items should be preserved for order maintenance after mutation."
            );

            // After OnBeforeSerialize, the new item should be added and duplicates handled
            set.OnBeforeSerialize();

            string diagnosticInfo =
                $"After OnBeforeSerialize - Items: [{string.Join(", ", set.SerializedItems)}], "
                + $"Set.Count: {set.Count}, "
                + $"PreserveSerializedEntries: {set.PreserveSerializedEntries}, "
                + $"HasDuplicatesOrNulls: {set.HasDuplicatesOrNulls}";

            Assert.AreEqual(
                2,
                set.SerializedItems.Length,
                $"Expected 2 serialized items. {diagnosticInfo}"
            );
            Assert.Contains(
                3,
                set.SerializedItems,
                $"Serialized items should contain 3. {diagnosticInfo}"
            );
            Assert.Contains(
                4,
                set.SerializedItems,
                $"Serialized items should contain 4. {diagnosticInfo}"
            );
        }

        [Test]
        public void SortedSetRemoveMutationClearsHasDuplicatesOrNullsFlag()
        {
            SerializableSortedSet<int> set = new();
            int[] serializedItems = { 1, 1, 2 }; // Has duplicates
            set._items = serializedItems;
            set.OnAfterDeserialize();

            Assert.IsTrue(
                set.HasDuplicatesOrNulls,
                "HasDuplicatesOrNulls should be true after deserializing duplicates."
            );
            Assert.AreEqual(2, set.Count, "Set should have 2 unique items.");

            bool removed = set.Remove(2);

            string diagnosticInfo =
                $"After Remove - Removed: {removed}, "
                + $"Set.Count: {set.Count}, "
                + $"HasDuplicatesOrNulls: {set.HasDuplicatesOrNulls}, "
                + $"PreserveSerializedEntries: {set.PreserveSerializedEntries}";

            Assert.IsTrue(removed, $"Remove should return true. {diagnosticInfo}");
            Assert.IsFalse(
                set.HasDuplicatesOrNulls,
                $"HasDuplicatesOrNulls should be cleared after Remove mutation. {diagnosticInfo}"
            );
            Assert.IsFalse(
                set.PreserveSerializedEntries,
                $"PreserveSerializedEntries should be cleared after Remove mutation. {diagnosticInfo}"
            );
        }

        [Test]
        public void SortedSetClearMutationClearsHasDuplicatesOrNullsFlag()
        {
            SerializableSortedSet<int> set = new();
            int[] serializedItems = { 1, 1, 2 }; // Has duplicates
            set._items = serializedItems;
            set.OnAfterDeserialize();

            Assert.IsTrue(
                set.HasDuplicatesOrNulls,
                "HasDuplicatesOrNulls should be true after deserializing duplicates."
            );

            set.Clear();

            string diagnosticInfo =
                $"After Clear - Set.Count: {set.Count}, "
                + $"HasDuplicatesOrNulls: {set.HasDuplicatesOrNulls}, "
                + $"PreserveSerializedEntries: {set.PreserveSerializedEntries}";

            Assert.AreEqual(0, set.Count, $"Set should be empty after Clear. {diagnosticInfo}");
            Assert.IsFalse(
                set.HasDuplicatesOrNulls,
                $"HasDuplicatesOrNulls should be cleared after Clear mutation. {diagnosticInfo}"
            );
            Assert.IsFalse(
                set.PreserveSerializedEntries,
                $"PreserveSerializedEntries should be cleared after Clear mutation. {diagnosticInfo}"
            );
        }

        [Test]
        public void SortedSetUnionWithMutationClearsHasDuplicatesOrNullsFlag()
        {
            SerializableSortedSet<int> set = new();
            int[] serializedItems = { 1, 1 }; // Has duplicates
            set._items = serializedItems;
            set.OnAfterDeserialize();

            Assert.IsTrue(
                set.HasDuplicatesOrNulls,
                "HasDuplicatesOrNulls should be true after deserializing duplicates."
            );

            set.UnionWith(new[] { 3, 4 });

            string diagnosticInfo =
                $"After UnionWith - Set.Count: {set.Count}, "
                + $"HasDuplicatesOrNulls: {set.HasDuplicatesOrNulls}, "
                + $"PreserveSerializedEntries: {set.PreserveSerializedEntries}";

            Assert.IsFalse(
                set.HasDuplicatesOrNulls,
                $"HasDuplicatesOrNulls should be cleared after UnionWith mutation. {diagnosticInfo}"
            );
            Assert.IsFalse(
                set.PreserveSerializedEntries,
                $"PreserveSerializedEntries should be cleared after UnionWith mutation. {diagnosticInfo}"
            );
        }

        [Test]
        public void SortedSetExceptWithMutationClearsHasDuplicatesOrNullsFlag()
        {
            SerializableSortedSet<int> set = new();
            int[] serializedItems = { 1, 1, 2 }; // Has duplicates
            set._items = serializedItems;
            set.OnAfterDeserialize();

            Assert.IsTrue(
                set.HasDuplicatesOrNulls,
                "HasDuplicatesOrNulls should be true after deserializing duplicates."
            );

            set.ExceptWith(new[] { 2 });

            string diagnosticInfo =
                $"After ExceptWith - Set.Count: {set.Count}, "
                + $"HasDuplicatesOrNulls: {set.HasDuplicatesOrNulls}, "
                + $"PreserveSerializedEntries: {set.PreserveSerializedEntries}";

            Assert.IsFalse(
                set.HasDuplicatesOrNulls,
                $"HasDuplicatesOrNulls should be cleared after ExceptWith mutation. {diagnosticInfo}"
            );
            Assert.IsFalse(
                set.PreserveSerializedEntries,
                $"PreserveSerializedEntries should be cleared after ExceptWith mutation. {diagnosticInfo}"
            );
        }

        [Test]
        public void SortedSetIntersectWithMutationClearsHasDuplicatesOrNullsFlag()
        {
            SerializableSortedSet<int> set = new();
            int[] serializedItems = { 1, 1, 2 }; // Has duplicates
            set._items = serializedItems;
            set.OnAfterDeserialize();

            Assert.IsTrue(
                set.HasDuplicatesOrNulls,
                "HasDuplicatesOrNulls should be true after deserializing duplicates."
            );

            set.IntersectWith(new[] { 1, 3 });

            string diagnosticInfo =
                $"After IntersectWith - Set.Count: {set.Count}, "
                + $"HasDuplicatesOrNulls: {set.HasDuplicatesOrNulls}, "
                + $"PreserveSerializedEntries: {set.PreserveSerializedEntries}";

            Assert.IsFalse(
                set.HasDuplicatesOrNulls,
                $"HasDuplicatesOrNulls should be cleared after IntersectWith mutation. {diagnosticInfo}"
            );
            Assert.IsFalse(
                set.PreserveSerializedEntries,
                $"PreserveSerializedEntries should be cleared after IntersectWith mutation. {diagnosticInfo}"
            );
        }

        [Test]
        public void SortedSetSymmetricExceptWithMutationClearsHasDuplicatesOrNullsFlag()
        {
            SerializableSortedSet<int> set = new();
            int[] serializedItems = { 1, 1, 2 }; // Has duplicates
            set._items = serializedItems;
            set.OnAfterDeserialize();

            Assert.IsTrue(
                set.HasDuplicatesOrNulls,
                "HasDuplicatesOrNulls should be true after deserializing duplicates."
            );

            set.SymmetricExceptWith(new[] { 2, 3 });

            string diagnosticInfo =
                $"After SymmetricExceptWith - Set.Count: {set.Count}, "
                + $"HasDuplicatesOrNulls: {set.HasDuplicatesOrNulls}, "
                + $"PreserveSerializedEntries: {set.PreserveSerializedEntries}";

            Assert.IsFalse(
                set.HasDuplicatesOrNulls,
                $"HasDuplicatesOrNulls should be cleared after SymmetricExceptWith mutation. {diagnosticInfo}"
            );
            Assert.IsFalse(
                set.PreserveSerializedEntries,
                $"PreserveSerializedEntries should be cleared after SymmetricExceptWith mutation. {diagnosticInfo}"
            );
        }

        [TestCase("Add", Description = "Add operation clears flag")]
        [TestCase("Remove", Description = "Remove operation clears flag")]
        [TestCase("Clear", Description = "Clear operation clears flag")]
        [TestCase("UnionWith", Description = "UnionWith operation clears flag")]
        [TestCase("ExceptWith", Description = "ExceptWith operation clears flag")]
        [TestCase("IntersectWith", Description = "IntersectWith operation clears flag")]
        [TestCase("SymmetricExceptWith", Description = "SymmetricExceptWith operation clears flag")]
        public void SortedSetAllMutationOperationsClearHasDuplicatesOrNullsFlag(string operation)
        {
            SerializableSortedSet<int> set = new();
            int[] serializedItems = { 1, 1, 2 }; // Has duplicates
            set._items = (int[])serializedItems.Clone();
            set.OnAfterDeserialize();

            bool hadDuplicatesBefore = set.HasDuplicatesOrNulls;
            Assert.IsTrue(
                hadDuplicatesBefore,
                $"HasDuplicatesOrNulls should be true before {operation}."
            );

            // Perform the mutation
            switch (operation)
            {
                case "Add":
                    set.Add(100);
                    break;
                case "Remove":
                    set.Remove(2);
                    break;
                case "Clear":
                    set.Clear();
                    break;
                case "UnionWith":
                    set.UnionWith(new[] { 3 });
                    break;
                case "ExceptWith":
                    set.ExceptWith(new[] { 2 });
                    break;
                case "IntersectWith":
                    set.IntersectWith(new[] { 1 });
                    break;
                case "SymmetricExceptWith":
                    set.SymmetricExceptWith(new[] { 2, 3 });
                    break;
            }

            string diagnosticInfo =
                $"Operation: {operation}, "
                + $"Had duplicates before: {hadDuplicatesBefore}, "
                + $"Has duplicates after: {set.HasDuplicatesOrNulls}, "
                + $"Set.Count: {set.Count}, "
                + $"PreserveSerializedEntries: {set.PreserveSerializedEntries}";

            Assert.IsFalse(
                set.HasDuplicatesOrNulls,
                $"HasDuplicatesOrNulls should be cleared after {operation} mutation. {diagnosticInfo}"
            );
            Assert.IsFalse(
                set.PreserveSerializedEntries,
                $"PreserveSerializedEntries should be cleared after {operation} mutation. {diagnosticInfo}"
            );
        }

        [TestCase(new[] { 1, 1 }, 2, new[] { 1, 2 }, Description = "Duplicate items with add")]
        [TestCase(new[] { 1, 2 }, 3, new[] { 1, 2, 3 }, Description = "No duplicates with add")]
        [TestCase(new[] { 5, 5, 5 }, 6, new[] { 5, 6 }, Description = "Triple duplicates with add")]
        public void SortedSetDuplicateItemHandlingAfterMutationAndSerialization(
            int[] initialItems,
            int itemToAdd,
            int[] expectedItems
        )
        {
            SerializableSortedSet<int> set = new();
            set._items = (int[])initialItems.Clone();
            set.OnAfterDeserialize();

            int uniqueCountBefore = set.Count;
            bool hasDuplicates = set.HasDuplicatesOrNulls;

            set.Add(itemToAdd);

            set.OnBeforeSerialize();

            string diagnosticInfo =
                $"Initial items: [{string.Join(", ", initialItems)}], "
                + $"Unique count before add: {uniqueCountBefore}, "
                + $"Had duplicates: {hasDuplicates}, "
                + $"Added item: {itemToAdd}, "
                + $"Result items: [{string.Join(", ", set.SerializedItems)}], "
                + $"Expected items: [{string.Join(", ", expectedItems)}]";

            Assert.AreEqual(
                expectedItems.Length,
                set.SerializedItems.Length,
                $"Item array length mismatch. {diagnosticInfo}"
            );

            for (int i = 0; i < expectedItems.Length; i++)
            {
                Assert.Contains(
                    expectedItems[i],
                    set.SerializedItems,
                    $"Missing expected item {expectedItems[i]}. {diagnosticInfo}"
                );
            }
        }

        [TestCase(new[] { 1, 1 }, Description = "Duplicate int items")]
        [TestCase(new[] { 1, 2, 1 }, Description = "Non-adjacent duplicates")]
        [TestCase(new[] { 1, 1, 1 }, Description = "Triple duplicates")]
        public void SortedSetFastPathSkipsWhenDuplicateItemsExist(int[] items)
        {
            SerializableSortedSet<int> set = new();
            set._items = (int[])items.Clone();
            set.OnAfterDeserialize();

            // Count unique items
            HashSet<int> uniqueItems = new(items);

            // Add a new item to make counts potentially match (for the edge case)
            int newItem = 1000;
            while (uniqueItems.Contains(newItem))
            {
                newItem++;
            }
            set.Add(newItem);

            set.OnBeforeSerialize();

            string diagnosticInfo =
                $"Initial items: [{string.Join(", ", items)}], "
                + $"Added item: {newItem}, "
                + $"Result items: [{string.Join(", ", set.SerializedItems)}], "
                + $"Set.Count: {set.Count}";

            // After serialization, duplicates should be resolved
            Assert.AreEqual(
                set.Count,
                set.SerializedItems.Length,
                $"Serialized array length should match set count after sync. {diagnosticInfo}"
            );

            // Verify no duplicates in result
            HashSet<int> resultItems = new(set.SerializedItems);
            Assert.AreEqual(
                set.SerializedItems.Length,
                resultItems.Count,
                $"Result should have no duplicate items. {diagnosticInfo}"
            );

            // Verify the new item is present
            Assert.Contains(
                newItem,
                set.SerializedItems,
                $"New item should be in result. {diagnosticInfo}"
            );
        }

        [Test]
        public void SortedSetSyncSerializedItemsHandlesDuplicatesWhenCountMatchesByCoincidence()
        {
            // This is the specific edge case: array has {3, 3} (length 2),
            // set has {3, 4} (count 2), so counts match but items differ
            SerializableSortedSet<int> set = new();
            int[] duplicateItems = { 3, 3 };
            set._items = (int[])duplicateItems.Clone();
            set.OnAfterDeserialize();

            Assert.AreEqual(1, set.Count, "Set should have 1 unique item initially.");
            Assert.IsTrue(set.HasDuplicatesOrNulls, "Should detect duplicate items.");

            set.Add(4);

            Assert.AreEqual(2, set.Count, "Set should have 2 items after add.");

            // Now: array length = 2 (duplicates), set count = 2 (unique)
            // The fast path in SyncSerializedItemsPreservingOrder should NOT be taken
            // because the arrays have duplicates

            set.OnBeforeSerialize();

            string diagnosticInfo =
                $"Result items: [{string.Join(", ", set.SerializedItems)}], "
                + $"Set.Count: {set.Count}";

            Assert.AreEqual(
                2,
                set.SerializedItems.Length,
                $"Should have 2 unique items after sync. {diagnosticInfo}"
            );
            Assert.Contains(3, set.SerializedItems, $"Should contain item 3. {diagnosticInfo}");
            Assert.Contains(4, set.SerializedItems, $"Should contain item 4. {diagnosticInfo}");

            // Verify no duplicates
            Assert.AreNotEqual(
                set.SerializedItems[0],
                set.SerializedItems[1],
                $"Items should be distinct. {diagnosticInfo}"
            );
        }

        [Test]
        public void NullSerializedItemsResetStateDuringDeserialization()
        {
            SerializableHashSet<string> set = new() { "alpha" };
            set._items = null;

            set.OnAfterDeserialize();

            Assert.AreEqual(0, set.Count);
            Assert.IsFalse(set.PreserveSerializedEntries);
        }

        [Test]
        public void SortedSetNullEntriesAreSkippedDuringDeserialization()
        {
            SerializableSortedSet<ScriptableSample> set = new();
            ScriptableSample valid = Track(ScriptableObject.CreateInstance<ScriptableSample>());
            set._items = new[] { null, valid };

            ExpectError(
                LogType.Warning,
                System.Text.RegularExpressions.Regex.Escape(
                    "SerializableSet<WallstopStudios.UnityHelpers.Tests.DataStructures.SerializableSortedSetTests+ScriptableSample> skipped serialized entry at index 0 because the value reference was null."
                )
            );

            set.OnAfterDeserialize();

            Assert.AreEqual(1, set.Count);
            Assert.IsTrue(set.Contains(valid));

            object cached = set._items;
            Assert.IsTrue(
                cached != null,
                "Serialized cache should be preserved for inspector review."
            );
            ScriptableSample[] cachedValues = (ScriptableSample[])cached;
            CollectionAssert.AreEqual(new[] { null, valid }, cachedValues);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void EditorAfterDeserializeSuppressesNullWarnings()
        {
            SerializableSortedSet<ScriptableSample> set = new()
            {
                _items = new ScriptableSample[] { null },
            };

            ISerializableSetEditorSync editorSync = set;
            editorSync.EditorAfterDeserialize();

            Assert.AreEqual(0, set.Count);
            Assert.AreSame(set._items, set.SerializedItems);
            Assert.IsTrue(set.PreserveSerializedEntries);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void UnityDeserializationRestoresSortOrderFromUnsortedSerializedItems()
        {
            SerializableSortedSet<string> set = new();
            string[] unsorted = { "delta", "alpha", "charlie" };
            set._items = unsorted;

            set.OnAfterDeserialize();

            // Direct enumeration via GetEnumerator should follow comparer (sorted) order
            List<string> directEnumeration = new();
            foreach (string item in set)
            {
                directEnumeration.Add(item);
            }

            CollectionAssert.AreEqual(
                new[] { "alpha", "charlie", "delta" },
                directEnumeration,
                "SortedSet direct enumeration should follow comparer order after deserialization."
            );

            // ToArray() now returns sorted order for sorted collections (matching standard collection behavior)
            string[] toArrayResult = set.ToArray();
            CollectionAssert.AreEqual(
                new[] { "alpha", "charlie", "delta" },
                toArrayResult,
                "ToArray() should return sorted order for SerializableSortedSet."
            );

            // ToPersistedOrderArray() preserves user-defined (serialized) order for inspector consistency
            string[] persistedOrderResult = set.ToPersistedOrderArray();
            CollectionAssert.AreEqual(
                unsorted,
                persistedOrderResult,
                "ToPersistedOrderArray() should preserve user-defined serialization order."
            );

            // Arrays are preserved to maintain user-defined order
            Assert.IsTrue(
                set._items != null,
                "Serialized cache should be preserved to maintain user-defined order."
            );
            Assert.IsTrue(set.PreserveSerializedEntries);
        }

        [Test]
        public void InspectorSnapshotWithDuplicatesPreservesCache()
        {
            SerializableSortedSet<int> set = new();
            ISerializableSetInspector inspector = set;
            int[] snapshot = { 2, 2, 3 };

            inspector.SetSerializedItemsSnapshot(snapshot, preserveSerializedEntries: true);
            set.OnAfterDeserialize();

            Assert.AreEqual(2, set.Count);
            Assert.IsTrue(set.Contains(2));
            Assert.IsTrue(set.Contains(3));
            CollectionAssert.AreEqual(snapshot, set.SerializedItems);
            Assert.IsTrue(set.PreserveSerializedEntries);
        }

        [Test]
        public void InspectorSnapshotWithoutPreserveMaintainsUserOrder()
        {
            SerializableSortedSet<int> set = new();
            ISerializableSetInspector inspector = set;
            int[] snapshot = { 4, 5 };

            inspector.SetSerializedItemsSnapshot(snapshot, preserveSerializedEntries: false);
            set.OnAfterDeserialize();

            Assert.AreEqual(2, set.Count);
            // After deserialization, arrays are always preserved to maintain order
            Assert.IsTrue(set.PreserveSerializedEntries);
            Assert.IsTrue(set.SerializedItems != null);
            // ToArray() returns sorted order
            CollectionAssert.AreEqual(new[] { 4, 5 }, set.ToArray());
        }

        [Test]
        public void InspectorSynchronizeSerializedStateStoresOrderedSnapshot()
        {
            SerializableSortedSet<int> set = new() { 5, 1, 3 };
            ISerializableSetInspector inspector = set;

            inspector.SynchronizeSerializedState();

            Array snapshot = inspector.GetSerializedItemsSnapshot();
            CollectionAssert.AreEqual(new[] { 1, 3, 5 }, snapshot);
            // After sync, preserve flag is set since arrays now exist
            Assert.IsTrue(set.PreserveSerializedEntries);
        }

        [Test]
        public void UnitySerializationPreservesUserDefinedOrderAfterDeserialization()
        {
            SerializableSortedSet<string> set = new();
            string[] serializedItems = { "delta", "alpha", "charlie" };
            set._items = serializedItems;

            set.OnAfterDeserialize();

            // Arrays are preserved to maintain user-defined order
            Assert.IsTrue(
                set._items != null,
                "Deserialization should preserve serialized cache to maintain user-defined order."
            );
            Assert.IsTrue(set.PreserveSerializedEntries);

            set.OnBeforeSerialize();

            string[] rebuiltItems = set._items;

            Assert.IsTrue(rebuiltItems != null);
            // Order should be preserved, not sorted
            CollectionAssert.AreEqual(
                new[] { "delta", "alpha", "charlie" },
                rebuiltItems,
                "Serialization should preserve user-defined order, not rebuild in sorted order."
            );
        }

        [Test]
        public void SortedSetInspectorReportsSortingSupport()
        {
            SerializableSortedSet<int> set = new();
            ISerializableSetInspector inspector = set;

            Assert.IsTrue(inspector.SupportsSorting);
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void JsonRoundTripPreservesUserDefinedOrder()
        {
            SerializableSortedSet<int> original = new(new[] { 5, 1, 3 });

            string json = Serializer.JsonStringify(original);
            SerializableSortedSet<int> roundTrip = Serializer.JsonDeserialize<
                SerializableSortedSet<int>
            >(json);

            Assert.AreEqual(3, roundTrip.Count);
            // After JSON deserialization, arrays are preserved for order
            Assert.IsTrue(roundTrip.SerializedItems != null);
            Assert.IsTrue(roundTrip.PreserveSerializedEntries);

            roundTrip.OnBeforeSerialize();

            // Order from JSON is preserved (JSON serialization uses sorted order from OnBeforeSerialize)
            CollectionAssert.AreEqual(new[] { 1, 3, 5 }, roundTrip.SerializedItems);
        }

        private sealed class SortedSample : IComparable<SortedSample>, IComparable
        {
            public SortedSample(string token)
            {
                Token = token;
            }

            public string Token { get; }

            public override bool Equals(object candidate)
            {
                if (candidate is SortedSample other)
                {
                    return string.Equals(Token, other.Token, StringComparison.Ordinal);
                }

                return false;
            }

            public override int GetHashCode()
            {
                if (Token == null)
                {
                    return 0;
                }

                return Token.GetHashCode(StringComparison.Ordinal);
            }

            public int CompareTo(SortedSample other)
            {
                if (other == null)
                {
                    return 1;
                }

                return string.CompareOrdinal(Token, other.Token);
            }

            int IComparable.CompareTo(object obj)
            {
                if (obj is SortedSample other)
                {
                    return CompareTo(other);
                }

                return -1;
            }
        }

        private sealed class ScriptableSample
            : ScriptableObject,
                IComparable<ScriptableSample>,
                IComparable
        {
            public int CompareTo(ScriptableSample other)
            {
                if (other == null)
                {
                    return 1;
                }

                return UnityObjectNameComparer<ScriptableSample>.Instance.Compare(this, other);
            }

            public int CompareTo(object obj)
            {
                if (obj is ScriptableSample other)
                {
                    return CompareTo(other);
                }

                return -1;
            }
        }

        [Test]
        public void EnumeratorIsValueTypeAndMaintainsSortOrder()
        {
            SerializableSortedSet<int> set = new() { 4, 1, 9 };

            using SortedSet<int>.Enumerator enumerator = set.GetEnumerator();
            Assert.IsTrue(enumerator.GetType().IsValueType);

            List<int> values = new();
            while (enumerator.MoveNext())
            {
                values.Add(enumerator.Current);
            }

            CollectionAssert.AreEqual(new[] { 1, 4, 9 }, values);
        }

        [Test]
        public void EnumeratorThrowsAfterMutationLikeSortedSet()
        {
            SerializableSortedSet<int> set = new() { 2, 5 };

            using SortedSet<int>.Enumerator enumerator = set.GetEnumerator();
            Assert.IsTrue(enumerator.MoveNext());

            set.Add(7);

            Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
        }

        [Test]
        public void TryGetValueReturnsStoredReferenceForMatchingComparable()
        {
            SerializableSortedSet<SortedSample> set = new();

            SortedSample stored = new("delta");
            set.Add(stored);

            SortedSample probe = new("delta");

            bool found = set.TryGetValue(probe, out SortedSample resolved);
            Assert.IsTrue(found);
            Assert.AreSame(stored, resolved);
        }

        [Test]
        public void MinAndMaxMatchSystemSortedSet()
        {
            SerializableSortedSet<int> serializable = new() { 8, -1, 13 };

            SortedSet<int> baseline = new() { 8, -1, 13 };

            Assert.AreEqual(baseline.Min, serializable.Min);
            Assert.AreEqual(baseline.Max, serializable.Max);
        }

        [Test]
        public void ReverseMatchesSystemSortedSet()
        {
            SerializableSortedSet<int> serializable = new() { 10, 4, 6 };

            SortedSet<int> baseline = new() { 10, 4, 6 };

            int[] serializableReverse = serializable.Reverse().ToArray();
            int[] baselineReverse = baseline.Reverse().ToArray();

            CollectionAssert.AreEqual(baselineReverse, serializableReverse);
        }

        [Test]
        public void ViewBetweenMatchesSystemSortedSet()
        {
            SerializableSortedSet<int> serializable = new() { 1, 3, 5, 7 };

            SortedSet<int> baseline = new() { 1, 3, 5, 7 };

            SortedSet<int> serializableView = serializable.GetViewBetween(2, 6);
            SortedSet<int> baselineView = baseline.GetViewBetween(2, 6);

            CollectionAssert.AreEqual(baselineView.ToArray(), serializableView.ToArray());
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void ProtoSerializationProducesIndependentCopy()
        {
            SerializableSortedSet<int> original = new() { 3, 1, 5 };

            byte[] payload = Serializer.ProtoSerialize(original);

            original.Add(7);

            SerializableSortedSet<int> roundTrip = Serializer.ProtoDeserialize<
                SerializableSortedSet<int>
            >(payload);

            Assert.IsFalse(original.SetEquals(roundTrip));
            Assert.IsTrue(original.Contains(7));
            Assert.IsFalse(roundTrip.Contains(7));

            int[] expectedOrder = { 1, 3, 5 };
            CollectionAssert.AreEqual(expectedOrder, roundTrip.ToArray());
        }

        [Test]
        public void JsonSerializationProducesIndependentCopy()
        {
            SerializableSortedSet<string> original = new() { "bravo", "alpha", "charlie" };

            string json = Serializer.JsonStringify(original);

            original.Add("delta");

            SerializableSortedSet<string> roundTrip = Serializer.JsonDeserialize<
                SerializableSortedSet<string>
            >(json);

            Assert.IsFalse(original.SetEquals(roundTrip));
            Assert.IsTrue(original.Contains("delta"));
            Assert.IsFalse(roundTrip.Contains("delta"));

            string[] expected = { "alpha", "bravo", "charlie" };
            CollectionAssert.AreEqual(expected, roundTrip.ToArray());
        }

        private sealed class CaseInsensitiveString : IComparable<CaseInsensitiveString>, IComparable
        {
            public CaseInsensitiveString(string value)
            {
                Value = value;
            }

            public string Value { get; }

            public int CompareTo(CaseInsensitiveString other)
            {
                return other == null
                    ? 1
                    : string.Compare(Value, other.Value, StringComparison.OrdinalIgnoreCase);
            }

            int IComparable.CompareTo(object obj)
            {
                if (ReferenceEquals(this, obj))
                {
                    return 0;
                }

                if (obj is CaseInsensitiveString candidate)
                {
                    return CompareTo(candidate);
                }

                return 1;
            }

            public override bool Equals(object obj)
            {
                if (obj is CaseInsensitiveString other)
                {
                    return string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
                }

                return false;
            }

            public override int GetHashCode()
            {
                return Value == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
            }

            public override string ToString()
            {
                return Value ?? string.Empty;
            }
        }

        [Test]
        public void ToArrayReturnsEmptyArrayForEmptySet()
        {
            SerializableSortedSet<string> set = new();

            string[] result = set.ToArray();

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Length);
            Assert.AreSame(Array.Empty<string>(), result);
        }

        [Test]
        public void ToArrayReturnsDefensiveCopyNotInternalArray()
        {
            SerializableSortedSet<int> set = new() { 1, 2, 3 };
            set.OnBeforeSerialize();

            int[] firstCopy = set.ToArray();
            int[] secondCopy = set.ToArray();

            Assert.AreNotSame(firstCopy, secondCopy);
            Assert.AreNotSame(firstCopy, set._items);

            firstCopy[0] = 999;
            Assert.AreNotEqual(999, set.ToArray()[0]);
        }

        [Test]
        public void ToArrayReturnsSortedOrderFromDeserialization()
        {
            SerializableSortedSet<int> set = new();
            int[] userOrder = { 5, 3, 8, 1, 9 };
            set._items = userOrder;
            set.OnAfterDeserialize();

            int[] result = set.ToArray();

            // ToArray should return sorted order for SerializableSortedSet
            CollectionAssert.AreEqual(new[] { 1, 3, 5, 8, 9 }, result);
        }

        [Test]
        public void ToPersistedOrderArrayPreservesUserDefinedOrderFromDeserialization()
        {
            SerializableSortedSet<int> set = new();
            int[] userOrder = { 5, 3, 8, 1, 9 };
            set._items = userOrder;
            set.OnAfterDeserialize();

            int[] result = set.ToPersistedOrderArray();

            // ToPersistedOrderArray should return user-defined order
            CollectionAssert.AreEqual(userOrder, result);
        }

        [Test]
        public void ToArrayReflectsCurrentStateAfterMutations()
        {
            SerializableSortedSet<string> set = new() { "alpha", "beta" };
            set.OnBeforeSerialize();

            bool removedAlpha = set.Remove("alpha");
            Assert.IsTrue(removedAlpha);
            bool addedGamma = set.Add("gamma");
            Assert.IsTrue(addedGamma);

            string[] result = set.ToArray();

            Assert.AreEqual(2, result.Length);
            Assert.IsFalse(((IList<string>)result).Contains("alpha"));
            Assert.IsTrue(((IList<string>)result).Contains("beta"));
            Assert.IsTrue(((IList<string>)result).Contains("gamma"));
        }

        [Test]
        public void ToArrayReturnsSortedOrderAfterMultipleSerializationCycles()
        {
            SerializableSortedSet<int> set = new();
            int[] userOrder = { 100, 50, 75, 25 };
            set._items = userOrder;
            set.OnAfterDeserialize();

            for (int i = 0; i < 3; i++)
            {
                set.OnBeforeSerialize();
                set.OnAfterDeserialize();
            }

            int[] result = set.ToArray();

            // ToArray should return sorted order
            CollectionAssert.AreEqual(new[] { 25, 50, 75, 100 }, result);
        }

        [Test]
        public void ToPersistedOrderArraySurvivesMultipleSerializationCycles()
        {
            SerializableSortedSet<int> set = new();
            int[] userOrder = { 100, 50, 75, 25 };
            set._items = userOrder;
            set.OnAfterDeserialize();

            for (int i = 0; i < 3; i++)
            {
                set.OnBeforeSerialize();
                set.OnAfterDeserialize();
            }

            int[] result = set.ToPersistedOrderArray();

            // ToPersistedOrderArray should preserve user-defined order
            CollectionAssert.AreEqual(userOrder, result);
        }

        [Test]
        public void ToArrayWithDuplicatesInSerializedDataReturnsUniqueElements()
        {
            SerializableSortedSet<int> set = new();
            int[] duplicateData = { 1, 1, 2, 2, 3 };
            set._items = duplicateData;
            set.OnAfterDeserialize();

            int[] result = set.ToArray();

            Assert.AreEqual(3, result.Length);
            CollectionAssert.AllItemsAreUnique(result);
            Assert.IsTrue(((IList<int>)result).Contains(1));
            Assert.IsTrue(((IList<int>)result).Contains(2));
            Assert.IsTrue(((IList<int>)result).Contains(3));
        }

        [Test]
        public void ToArrayAfterClearReturnsEmptyArray()
        {
            SerializableSortedSet<string> set = new() { "one", "two", "three" };
            set.OnBeforeSerialize();

            set.Clear();

            string[] result = set.ToArray();

            Assert.AreEqual(0, result.Length);
            Assert.AreSame(Array.Empty<string>(), result);
        }

        [Test]
        public void ToArrayContainsAllElementsFromSet()
        {
            SerializableSortedSet<int> set = new() { 10, 20, 30, 40, 50 };

            int[] result = set.ToArray();

            Assert.AreEqual(set.Count, result.Length);
            foreach (int item in set)
            {
                Assert.IsTrue(((IList<int>)result).Contains(item));
            }
        }

        [Test]
        public void ToArrayLengthMatchesCount()
        {
            SerializableSortedSet<string> set = new();

            for (int i = 0; i < 100; i++)
            {
                bool added = set.Add($"item_{i:D3}");
                Assert.IsTrue(added);
                string[] result = set.ToArray();
                Assert.AreEqual(set.Count, result.Length);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(10)]
        [TestCase(100)]
        public void ToArrayLengthMatchesCountForVariousSizes(int size)
        {
            SerializableSortedSet<int> set = new();
            for (int i = 0; i < size; i++)
            {
                bool added = set.Add(i);
                Assert.IsTrue(added);
            }

            int[] result = set.ToArray();

            Assert.AreEqual(size, result.Length);
            Assert.AreEqual(set.Count, result.Length);
        }

        [TestCase("Add")]
        [TestCase("Remove")]
        [TestCase("Clear")]
        public void ToArrayReflectsStateAfterMutation(string operation)
        {
            SerializableSortedSet<int> set = new() { 1, 2, 3 };
            set.OnBeforeSerialize();

            switch (operation)
            {
                case "Add":
                    bool added = set.Add(4);
                    Assert.IsTrue(added);
                    Assert.AreEqual(4, set.ToArray().Length);
                    Assert.IsTrue(((IList<int>)set.ToArray()).Contains(4));
                    break;
                case "Remove":
                    bool removed = set.Remove(2);
                    Assert.IsTrue(removed);
                    Assert.AreEqual(2, set.ToArray().Length);
                    Assert.IsFalse(((IList<int>)set.ToArray()).Contains(2));
                    break;
                case "Clear":
                    set.Clear();
                    Assert.AreEqual(0, set.ToArray().Length);
                    break;
            }
        }

        [Test]
        public void ToArrayReturnsSortedOrderAfterAddingNewItems()
        {
            int[] existingOrder = { 3, 1, 5 };
            SerializableSortedSet<int> set = new();
            set._items = existingOrder;
            set.OnAfterDeserialize();

            bool addedTwo = set.Add(2);
            bool addedFour = set.Add(4);
            Assert.IsTrue(addedTwo);
            Assert.IsTrue(addedFour);

            int[] result = set.ToArray();

            // ToArray should return sorted order
            Assert.AreEqual(5, result.Length);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, result);
        }

        [Test]
        public void ToPersistedOrderArrayPreservesInsertionOrderForNewItems()
        {
            int[] existingOrder = { 1, 2, 3 };
            SerializableSortedSet<int> set = new();
            set._items = existingOrder;
            set.OnAfterDeserialize();

            bool addedFour = set.Add(4);
            bool addedFive = set.Add(5);
            Assert.IsTrue(addedFour);
            Assert.IsTrue(addedFive);

            int[] result = set.ToPersistedOrderArray();

            Assert.AreEqual(5, result.Length);
            for (int i = 0; i < existingOrder.Length; i++)
            {
                Assert.AreEqual(existingOrder[i], result[i]);
            }

            Assert.AreEqual(4, result[3]);
            Assert.AreEqual(5, result[4]);
        }

        [Test]
        public void DirectEnumerationAndToArrayBothReturnSortedOrder()
        {
            SerializableSortedSet<string> set = new();
            string[] unsortedItems = { "charlie", "alpha", "bravo" };
            set._items = unsortedItems;
            set.OnAfterDeserialize();

            List<string> directEnumeration = new();
            foreach (string item in set)
            {
                directEnumeration.Add(item);
            }

            string[] toArrayResult = set.ToArray();

            CollectionAssert.AreEqual(
                new[] { "alpha", "bravo", "charlie" },
                directEnumeration,
                "Direct enumeration should be in sorted order"
            );

            // ToArray now also returns sorted order for SerializableSortedSet
            CollectionAssert.AreEqual(
                new[] { "alpha", "bravo", "charlie" },
                toArrayResult,
                "ToArray() should return sorted order for SerializableSortedSet"
            );
        }

        [Test]
        public void ToPersistedOrderArrayReturnsUserDefinedOrder()
        {
            SerializableSortedSet<string> set = new();
            string[] unsortedItems = { "charlie", "alpha", "bravo" };
            set._items = unsortedItems;
            set.OnAfterDeserialize();

            string[] persistedOrderResult = set.ToPersistedOrderArray();

            // ToPersistedOrderArray should preserve user-defined serialization order
            CollectionAssert.AreEqual(
                unsortedItems,
                persistedOrderResult,
                "ToPersistedOrderArray() should preserve user-defined serialization order"
            );
        }

        [TestCase("Add", Description = "Adding via Add method")]
        [TestCase("Remove", Description = "Removing via Remove method")]
        [TestCase("Clear", Description = "Clearing the set")]
        public void PreserveSerializedEntriesFlagBehaviorAfterMutation(string mutationType)
        {
            SerializableSortedSet<int> set = new() { 1, 2, 3 };
            set.OnBeforeSerialize();

            bool preserveFlagBefore = set.PreserveSerializedEntries;
            Assert.IsTrue(
                preserveFlagBefore,
                $"PreserveSerializedEntries should be true after OnBeforeSerialize, before {mutationType}"
            );

            switch (mutationType)
            {
                case "Add":
                    set.Add(4);
                    break;
                case "Remove":
                    set.Remove(1);
                    break;
                case "Clear":
                    set.Clear();
                    break;
            }

            bool preserveFlagAfter = set.PreserveSerializedEntries;
            Assert.IsFalse(
                preserveFlagAfter,
                $"PreserveSerializedEntries should be false after {mutationType} mutation"
            );
        }

        [Test]
        public void ToArrayReflectsAdditionAfterMutation()
        {
            SerializableSortedSet<int> set = new() { 1, 2, 3 };
            set.OnBeforeSerialize();

            set.Add(4);

            int[] result = set.ToArray();
            Assert.AreEqual(4, result.Length, "ToArray() length should reflect the addition");
            Assert.IsTrue(
                ((IList<int>)result).Contains(4),
                "ToArray() should contain the newly added item"
            );
        }

        [Test]
        public void ToArrayReflectsRemovalAfterMutation()
        {
            SerializableSortedSet<int> set = new() { 1, 2, 3 };
            set.OnBeforeSerialize();

            set.Remove(2);

            int[] result = set.ToArray();
            Assert.AreEqual(2, result.Length, "ToArray() length should reflect the removal");
            Assert.IsFalse(
                ((IList<int>)result).Contains(2),
                "ToArray() should not contain the removed item"
            );
        }

        [Test]
        public void DuplicateItemsInSerializedArrayArePreservedWhenPreserveFlagSet()
        {
            SerializableSortedSet<int> set = new();
            int[] itemsWithDuplicates = { 1, 1, 2, 3 };
            set._items = itemsWithDuplicates;

            set.OnAfterDeserialize();

            Assert.IsTrue(
                set.PreserveSerializedEntries,
                "PreserveSerializedEntries should be true after deserialization"
            );
            Assert.IsTrue(
                set.HasDuplicatesOrNulls,
                "HasDuplicatesOrNulls should be true when duplicate items exist"
            );

            set.OnBeforeSerialize();

            CollectionAssert.AreEqual(
                itemsWithDuplicates,
                set.SerializedItems,
                "Serialized items with duplicates should be preserved exactly when preserve flag is set"
            );
        }

        [Test]
        public void ToArrayReturnsEmptyForEmptySet()
        {
            SerializableSortedSet<int> set = new();

            int[] result = set.ToArray();

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void ToPersistedOrderArrayReturnsEmptyForEmptySet()
        {
            SerializableSortedSet<string> set = new();

            string[] result = set.ToPersistedOrderArray();

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void ToArrayReturnsSortedOrderForFreshlyCreatedSet()
        {
            SerializableSortedSet<int> set = new() { 5, 1, 3, 4, 2 };

            int[] result = set.ToArray();

            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, result);
        }

        [Test]
        public void ToArrayReturnsSortedOrderForStringSet()
        {
            SerializableSortedSet<string> set = new() { "zebra", "apple", "mango", "banana" };

            string[] result = set.ToArray();

            CollectionAssert.AreEqual(new[] { "apple", "banana", "mango", "zebra" }, result);
        }

        [Test]
        public void ToPersistedOrderArrayReturnsDefensiveCopy()
        {
            SerializableSortedSet<int> set = new() { 1, 2, 3 };
            set.OnBeforeSerialize();

            int[] firstCopy = set.ToPersistedOrderArray();
            int[] secondCopy = set.ToPersistedOrderArray();

            Assert.AreNotSame(firstCopy, secondCopy);

            firstCopy[0] = 999;
            Assert.AreNotEqual(999, set.ToPersistedOrderArray()[0]);
        }

        [Test]
        public void ToArrayAndToPersistedOrderArrayAreDifferentAfterDeserialization()
        {
            SerializableSortedSet<int> set = new();
            int[] unsortedItems = { 5, 1, 4, 2, 3 };
            set._items = (int[])unsortedItems.Clone();
            set.OnAfterDeserialize();

            int[] sortedResult = set.ToArray();
            int[] persistedResult = set.ToPersistedOrderArray();

            // ToArray should be sorted
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, sortedResult);

            // ToPersistedOrderArray should preserve original order
            CollectionAssert.AreEqual(unsortedItems, persistedResult);
        }

        [TestCase(1)]
        [TestCase(10)]
        [TestCase(100)]
        public void ToArrayConsistentWithEnumerationForVariousSizes(int size)
        {
            SerializableSortedSet<int> set = new();
            for (int i = size - 1; i >= 0; i--)
            {
                set.Add(i);
            }

            int[] toArrayResult = set.ToArray();
            List<int> enumerationResult = new();
            foreach (int item in set)
            {
                enumerationResult.Add(item);
            }

            Assert.AreEqual(size, toArrayResult.Length);
            Assert.AreEqual(size, enumerationResult.Count);
            CollectionAssert.AreEqual(enumerationResult, toArrayResult);
        }

        [Test]
        public void ToPersistedOrderArrayReflectsRuntimeMutations()
        {
            SerializableSortedSet<string> set = new();
            string[] initialItems = { "zebra", "alpha" };
            set._items = (string[])initialItems.Clone();
            set.OnAfterDeserialize();

            set.Add("mango");

            string[] result = set.ToPersistedOrderArray();

            Assert.AreEqual(3, result.Length);
            // Original entries should preserve their order
            Assert.AreEqual("zebra", result[0]);
            Assert.AreEqual("alpha", result[1]);
            // New entry should be appended
            Assert.AreEqual("mango", result[2]);
        }

        [Test]
        public void ToArrayAlwaysSortedEvenAfterMutations()
        {
            SerializableSortedSet<string> set = new();
            string[] initialItems = { "zebra", "alpha" };
            set._items = (string[])initialItems.Clone();
            set.OnAfterDeserialize();

            set.Add("mango");

            string[] result = set.ToArray();

            // Should always be sorted regardless of mutations
            CollectionAssert.AreEqual(new[] { "alpha", "mango", "zebra" }, result);
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void ProtoRoundTripPreservesSortedOrderInToArray()
        {
            SerializableSortedSet<int> original = new() { 5, 1, 3, 4, 2 };

            byte[] payload = Serializer.ProtoSerialize(original);
            SerializableSortedSet<int> roundTrip = Serializer.ProtoDeserialize<
                SerializableSortedSet<int>
            >(payload);

            int[] result = roundTrip.ToArray();

            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, result);
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void JsonRoundTripPreservesSortedOrderInToArray()
        {
            SerializableSortedSet<int> original = new() { 5, 1, 3, 4, 2 };

            string json = Serializer.JsonStringify(original);
            SerializableSortedSet<int> roundTrip = Serializer.JsonDeserialize<
                SerializableSortedSet<int>
            >(json);

            int[] result = roundTrip.ToArray();

            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, result);
        }

        [Test]
        public void SingleElementSetToArrayReturnsSingleElement()
        {
            SerializableSortedSet<string> set = new() { "only" };

            string[] result = set.ToArray();

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("only", result[0]);
        }

        [Test]
        public void SingleElementSetToPersistedOrderArrayReturnsSingleElement()
        {
            SerializableSortedSet<string> set = new() { "only" };
            set.OnBeforeSerialize();

            string[] result = set.ToPersistedOrderArray();

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("only", result[0]);
        }

        [Test]
        public void ToArrayWithNegativeNumbers()
        {
            SerializableSortedSet<int> set = new() { 5, -3, 0, -10, 7 };

            int[] result = set.ToArray();

            CollectionAssert.AreEqual(new[] { -10, -3, 0, 5, 7 }, result);
        }

        [Test]
        public void ToPersistedOrderArrayPreservesInsertionOrderWithNegativeNumbers()
        {
            SerializableSortedSet<int> set = new();
            int[] items = { 5, -3, 0, -10, 7 };
            set._items = (int[])items.Clone();
            set.OnAfterDeserialize();

            int[] result = set.ToPersistedOrderArray();

            CollectionAssert.AreEqual(items, result);
        }

        [Test]
        public void ToArrayAfterRemovalStillReturnsSortedOrder()
        {
            SerializableSortedSet<int> set = new() { 5, 1, 3, 4, 2 };
            set.Remove(3);

            int[] result = set.ToArray();

            CollectionAssert.AreEqual(new[] { 1, 2, 4, 5 }, result);
        }

        [Test]
        public void ToPersistedOrderArrayAfterRemovalReflectsChange()
        {
            SerializableSortedSet<int> set = new();
            int[] items = { 5, 1, 3, 4, 2 };
            set._items = (int[])items.Clone();
            set.OnAfterDeserialize();

            set.Remove(3);

            int[] result = set.ToPersistedOrderArray();

            // 3 was at index 2, removal should compact the array
            CollectionAssert.AreEqual(new[] { 5, 1, 4, 2 }, result);
        }

        [Test]
        public void MultipleMutationsToArrayRemainsConsistentlySorted()
        {
            SerializableSortedSet<int> set = new();
            int[] items = { 100, 50, 75 };
            set._items = (int[])items.Clone();
            set.OnAfterDeserialize();

            set.Add(60);
            set.Add(25);
            set.Remove(50);
            set.Add(90);

            int[] result = set.ToArray();

            // Final set should be {25, 60, 75, 90, 100}, all sorted
            CollectionAssert.AreEqual(new[] { 25, 60, 75, 90, 100 }, result);
        }

        [Test]
        public void ToArrayReturnsNewArrayEachTime()
        {
            SerializableSortedSet<int> set = new() { 1, 2, 3 };

            int[] first = set.ToArray();
            int[] second = set.ToArray();

            Assert.AreNotSame(first, second);

            first[0] = 999;
            Assert.AreEqual(1, set.ToArray()[0]);
        }
    }
}
