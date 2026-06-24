// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializableHashSetTests : CommonTestBase
    {
        [Test]
        public void AddContainsAndEnumerateValues()
        {
            SerializableHashSet<string> set = new();
            bool addedAlpha = set.Add("alpha");
            bool addedBeta = set.Add("beta");
            bool duplicateAlpha = set.Add("alpha");

            Assert.IsTrue(addedAlpha);
            Assert.IsTrue(addedBeta);
            Assert.IsFalse(duplicateAlpha);
            Assert.IsTrue(set.Contains("alpha"));
            Assert.IsTrue(set.Contains("beta"));
            Assert.IsFalse(set.Contains("gamma"));

            List<string> values = new();
            foreach (string value in set)
            {
                values.Add(value);
            }

            Assert.AreEqual(2, values.Count);
            Assert.Contains("alpha", values);
            Assert.Contains("beta", values);
        }

        [Test]
        public void ToHashSetReturnsIndependentCopy()
        {
            SerializableHashSet<int> set = new();
            bool addedOne = set.Add(1);
            bool addedTwo = set.Add(2);

            Assert.IsTrue(addedOne);
            Assert.IsTrue(addedTwo);

            HashSet<int> copy = set.ToHashSet();

            Assert.AreEqual(set.Count, copy.Count);
            Assert.IsTrue(copy.Contains(1));
            Assert.IsTrue(copy.Contains(2));

            bool copyAddedThree = copy.Add(3);
            Assert.IsTrue(copyAddedThree);
            Assert.IsFalse(set.Contains(3));

            bool setAddedFour = set.Add(4);
            Assert.IsTrue(setAddedFour);
            Assert.IsFalse(copy.Contains(4));
        }

        [Test]
        public void NullEntriesAreSkippedDuringDeserialization()
        {
            SerializableHashSet<string> set = new();
            string[] source = { null, "valid" };
            set._items = source;

            LogAssert.Expect(
                LogType.Warning,
                "SerializableSet<System.String> skipped serialized entry at index 0 because the value reference was null."
            );

            set.OnAfterDeserialize();

            Assert.AreEqual(1, set.Count);
            Assert.IsTrue(set.Contains("valid"));

            string[] stored = set._items;
            bool preserve = set.PreserveSerializedEntries;

            Assert.IsTrue(
                stored != null,
                "Serialized items should remain when null entries exist."
            );
            CollectionAssert.AreEqual(source, stored);
            Assert.IsTrue(preserve, "Null entries should preserve serialized cache.");

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void NullEntriesSetHasDuplicatesOrNullsFlagAndMutationClearsIt()
        {
            SerializableHashSet<string> set = new();
            string[] source = { null, "valid" };
            set._items = source;

            LogAssert.Expect(
                LogType.Warning,
                "SerializableSet<System.String> skipped serialized entry at index 0 because the value reference was null."
            );

            set.OnAfterDeserialize();

            string diagnosticInfoBefore =
                $"After OnAfterDeserialize - Count: {set.Count}, "
                + $"HasDuplicatesOrNulls: {set.HasDuplicatesOrNulls}, "
                + $"PreserveSerializedEntries: {set.PreserveSerializedEntries}";

            Assert.IsTrue(
                set.HasDuplicatesOrNulls,
                $"HasDuplicatesOrNulls should be true when null entries exist. {diagnosticInfoBefore}"
            );

            set.Add("newItem");

            string diagnosticInfoAfter =
                $"After Add - Count: {set.Count}, "
                + $"HasDuplicatesOrNulls: {set.HasDuplicatesOrNulls}, "
                + $"PreserveSerializedEntries: {set.PreserveSerializedEntries}";

            Assert.IsFalse(
                set.HasDuplicatesOrNulls,
                $"HasDuplicatesOrNulls should be cleared after mutation. {diagnosticInfoAfter}"
            );
            Assert.IsFalse(
                set.PreserveSerializedEntries,
                $"PreserveSerializedEntries should be cleared after mutation. {diagnosticInfoAfter}"
            );

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void UnitySerializationPreservesDuplicateEntriesInBackingArray()
        {
            SerializableHashSet<int> set = new();
            int[] duplicateSource = { 1, 1, 2 };
            set._items = duplicateSource;

            set.OnAfterDeserialize();

            object preservedItems = set._items;
            Assert.IsTrue(
                preservedItems != null,
                "Duplicate entries should keep serialized cache."
            );
            Assert.AreSame(
                duplicateSource,
                preservedItems,
                "Duplicate cache should not be replaced."
            );
            Assert.AreEqual(2, set.Count);
            Assert.IsTrue(set.Contains(1));
            Assert.IsTrue(set.Contains(2));
        }

        [Test]
        public void ComparerDuplicatesPreserveSerializedItems()
        {
            SerializableHashSet<string> set = new(StringComparer.OrdinalIgnoreCase);
            string[] serializedItems = { "ALPHA", "alpha", "beta" };
            set._items = serializedItems;

            set.OnAfterDeserialize();

            Assert.AreEqual(2, set.Count);
            Assert.IsTrue(set.Contains("alpha"));
            Assert.IsTrue(set.Contains("beta"));

            Assert.AreSame(
                serializedItems,
                set.SerializedItems,
                "Comparer-driven duplicates must keep serialized cache for inspector review."
            );
            Assert.IsTrue(set.PreserveSerializedEntries);
        }

        [Test]
        public void SetMutationsClearPreserveFlagButKeepArraysForOrder()
        {
            SerializableHashSet<int> set = new();

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
        public void RemoveMutationClearsHasDuplicatesOrNullsFlag()
        {
            SerializableHashSet<int> set = new();
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
        public void ClearMutationClearsHasDuplicatesOrNullsFlag()
        {
            SerializableHashSet<int> set = new();
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
        public void UnionWithMutationClearsHasDuplicatesOrNullsFlag()
        {
            SerializableHashSet<int> set = new();
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
        public void ExceptWithMutationClearsHasDuplicatesOrNullsFlag()
        {
            SerializableHashSet<int> set = new();
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
        public void IntersectWithMutationClearsHasDuplicatesOrNullsFlag()
        {
            SerializableHashSet<int> set = new();
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
        public void SymmetricExceptWithMutationClearsHasDuplicatesOrNullsFlag()
        {
            SerializableHashSet<int> set = new();
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
        public void AllMutationOperationsClearHasDuplicatesOrNullsFlag(string operation)
        {
            SerializableHashSet<int> set = new();
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
        public void DuplicateItemHandlingAfterMutationAndSerialization(
            int[] initialItems,
            int itemToAdd,
            int[] expectedItems
        )
        {
            SerializableHashSet<int> set = new();
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
        public void FastPathSkipsWhenDuplicateItemsExist(int[] items)
        {
            SerializableHashSet<int> set = new();
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
        public void SyncSerializedItemsHandlesDuplicatesWhenCountMatchesByCoincidence()
        {
            // This is the specific edge case: array has {3, 3} (length 2),
            // set has {3, 4} (count 2), so counts match but items differ
            SerializableHashSet<int> set = new();
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
        public void UnitySerializationPreservesOrderAfterMutation()
        {
            SerializableHashSet<int> set = new() { 5, 10 };
            set.OnBeforeSerialize();

            int[] serializedBefore = set._items;
            Assert.IsTrue(serializedBefore != null);
            CollectionAssert.AreEquivalent(new[] { 5, 10 }, serializedBefore);

            set.OnAfterDeserialize();
            object cachedAfterDeserialize = set._items;
            // Arrays are now preserved to maintain user-defined order
            Assert.IsTrue(
                cachedAfterDeserialize != null,
                "Serialized cache should be preserved to maintain user-defined order."
            );
            Assert.IsTrue(set.PreserveSerializedEntries);

            bool addedNew = set.Add(20);
            Assert.IsTrue(addedNew);

            set.OnBeforeSerialize();
            int[] serializedAfterMutation = set._items;
            Assert.IsTrue(serializedAfterMutation != null);
            CollectionAssert.AreEquivalent(new[] { 5, 10, 20 }, serializedAfterMutation);
        }

        [Test]
        public void UnityObjectEntriesWithNullPreserveSerializedCache()
        {
            SerializableHashSet<DummyAsset> set = new();
            DummyAsset valid = Track(ScriptableObject.CreateInstance<DummyAsset>());
            DummyAsset[] serializedItems = { null, valid };
            set._items = serializedItems;

            string expectedMessage =
                $"SerializableSet<{typeof(DummyAsset).FullName}> skipped serialized entry at index 0 because the value reference was null.";
            LogAssert.Expect(LogType.Warning, expectedMessage);

            set.OnAfterDeserialize();

            Assert.AreEqual(1, set.Count);
            Assert.IsTrue(set.Contains(valid));
            Assert.AreSame(serializedItems, set.SerializedItems);
            Assert.IsTrue(set.PreserveSerializedEntries);
        }

        [Test]
        public void EditorAfterDeserializeSuppressesWarnings()
        {
            SerializableHashSet<DummyAsset> set = new();
            DummyAsset[] serializedItems = { null };
            set._items = serializedItems;

            ISerializableSetEditorSync editorSync = set;
            editorSync.EditorAfterDeserialize();

            Assert.AreEqual(0, set.Count);
            Assert.AreSame(serializedItems, set.SerializedItems);
            Assert.IsTrue(set.PreserveSerializedEntries);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ProtoSerializationRoundTripsValues()
        {
            SerializableHashSet<int> original = new(new[] { 1, 3, 5 });
            byte[] payload = Serializer.ProtoSerialize(original);

            object cachedItems = original._items;
            // After proto serialization, arrays are preserved
            Assert.IsTrue(
                cachedItems != null,
                "Proto serialization should preserve cached arrays for order stability."
            );

            SerializableHashSet<int> roundTrip = Serializer.ProtoDeserialize<
                SerializableHashSet<int>
            >(payload);

            Assert.AreEqual(original.Count, roundTrip.Count);
            foreach (int value in original)
            {
                Assert.IsTrue(
                    roundTrip.Contains(value),
                    $"Missing value {value} after Proto round-trip."
                );
            }
        }

        [Test]
        public void JsonSerializationRoundTripsValues()
        {
            SerializableHashSet<string> original = new(new[] { "delta", "alpha", "gamma" });

            string json = Serializer.JsonStringify(original);
            SerializableHashSet<string> roundTrip = Serializer.JsonDeserialize<
                SerializableHashSet<string>
            >(json);

            Assert.AreEqual(original.Count, roundTrip.Count);
            foreach (string token in original)
            {
                Assert.IsTrue(roundTrip.Contains(token), token);
            }
        }

        [Test]
        public void InspectorSnapshotRoundTripsSerializedItems()
        {
            SerializableHashSet<int> set = new();
            ISerializableSetInspector inspector = set;
            int[] snapshot = { 5, 10, 15 };

            inspector.SetSerializedItemsSnapshot(snapshot, preserveSerializedEntries: true);

            Array stored = inspector.GetSerializedItemsSnapshot();
            CollectionAssert.AreEqual(snapshot, stored);
            Assert.IsTrue(set.PreserveSerializedEntries);

            set.OnAfterDeserialize();

            Assert.AreEqual(3, set.Count);
            Assert.IsTrue(set.Contains(5));
            Assert.IsTrue(set.Contains(10));
            Assert.IsTrue(set.Contains(15));
        }

        [Test]
        public void InspectorSnapshotWithoutPreserveMaintainsOrder()
        {
            SerializableHashSet<int> set = new();
            ISerializableSetInspector inspector = set;
            int[] snapshot = { 7, 14 };

            inspector.SetSerializedItemsSnapshot(snapshot, preserveSerializedEntries: false);
            set.OnAfterDeserialize();

            Assert.AreEqual(2, set.Count);
            // After deserialization, arrays are always preserved to maintain order
            Assert.IsTrue(set.PreserveSerializedEntries);
            Assert.IsTrue(
                set.SerializedItems != null,
                "Serialized cache should be preserved to maintain user-defined order."
            );
            Assert.IsTrue(set.Contains(7));
            Assert.IsTrue(set.Contains(14));
        }

        [Test]
        public void InspectorTryAddElementConvertsStrings()
        {
            SerializableHashSet<int> set = new();
            ISerializableSetInspector inspector = set;

            Assert.IsTrue(inspector.TryAddElement("42", out object normalized));
            Assert.AreEqual(42, normalized);
            Assert.IsTrue(set.Contains(42));

            Assert.IsFalse(
                inspector.TryAddElement("42", out normalized),
                "Duplicate entries should not be added via inspector."
            );
        }

        [Test]
        public void InspectorSynchronizeSerializedStateStoresSnapshot()
        {
            SerializableHashSet<int> set = new() { 3, 6 };
            ISerializableSetInspector inspector = set;

            inspector.SynchronizeSerializedState();

            Array snapshot = inspector.GetSerializedItemsSnapshot();
            CollectionAssert.AreEquivalent(new[] { 3, 6 }, snapshot);
            // After sync, preserve flag is set since arrays now exist
            Assert.IsTrue(set.PreserveSerializedEntries);
        }

        [Test]
        public void InspectorRemoveElementMarksDirtyButPreservesArraysForOrder()
        {
            SerializableHashSet<int> set = new(new[] { 10 });
            set.OnBeforeSerialize();
            ISerializableSetInspector inspector = set;

            Assert.IsTrue(inspector.RemoveElement(10));
            Assert.AreEqual(0, set.Count);
            // Arrays are preserved for order maintenance, but preserve flag is cleared
            Assert.IsTrue(
                set.SerializedItems != null,
                "Serialized items should be preserved for order maintenance."
            );
            Assert.IsFalse(
                set.PreserveSerializedEntries,
                "Preserve flag should be cleared after mutation."
            );

            // After OnBeforeSerialize, the removed entry should be gone
            set.OnBeforeSerialize();
            Assert.AreEqual(0, set.SerializedItems.Length);
        }

        [Test]
        public void InspectorClearElementsEmptiesSet()
        {
            SerializableHashSet<int> set = new(new[] { 1, 2, 3 });
            ISerializableSetInspector inspector = set;

            inspector.ClearElements();

            Assert.AreEqual(0, set.Count);
            Assert.IsTrue(set.SerializedItems == null);
            Assert.IsFalse(set.PreserveSerializedEntries);
        }

        [Test]
        public void InspectorTryAddElementRejectsNullForValueTypes()
        {
            SerializableHashSet<int> set = new();
            ISerializableSetInspector inspector = set;

            Assert.IsFalse(inspector.TryAddElement(null, out object _));
            Assert.AreEqual(0, set.Count);
        }

        [Test]
        public void JsonRoundTripPreservesUserDefinedOrder()
        {
            SerializableHashSet<int> original = new(new[] { 9, 4, 7 });

            string json = Serializer.JsonStringify(original);
            SerializableHashSet<int> roundTrip = Serializer.JsonDeserialize<
                SerializableHashSet<int>
            >(json);

            Assert.AreEqual(original.Count, roundTrip.Count);
            // After JSON deserialization, arrays are preserved for order
            Assert.IsTrue(roundTrip.SerializedItems != null);
            Assert.IsTrue(roundTrip.PreserveSerializedEntries);

            roundTrip.OnBeforeSerialize();

            int[] rebuiltItems = roundTrip.SerializedItems;
            Assert.IsTrue(rebuiltItems != null);
            CollectionAssert.AreEquivalent(new[] { 4, 7, 9 }, rebuiltItems);
        }

        [Test]
        public void UnionWithPopulatesSetWithoutDuplicates()
        {
            SerializableHashSet<int> set = new(new[] { 1, 3, 5 });
            set.UnionWith(new[] { 3, 5, 7, 9 });

            Assert.AreEqual(5, set.Count);
            int[] expected = { 1, 3, 5, 7, 9 };
            foreach (int value in expected)
            {
                Assert.IsTrue(set.Contains(value), $"Expected value {value} to be present.");
            }
        }

        private sealed class SampleValue
        {
            public SampleValue(string identifier)
            {
                Identifier = identifier;
            }

            public string Identifier { get; }

            public override bool Equals(object candidate)
            {
                if (candidate is SampleValue other)
                {
                    return string.Equals(Identifier, other.Identifier, StringComparison.Ordinal);
                }

                return false;
            }

            public override int GetHashCode()
            {
                if (Identifier == null)
                {
                    return 0;
                }

                return Identifier.GetHashCode(StringComparison.Ordinal);
            }
        }

        [Test]
        public void EnumeratorIsValueTypeAndSupportsForEach()
        {
            SerializableHashSet<int> set = new(new[] { 1, 2, 3 });
            using HashSet<int>.Enumerator enumerator = set.GetEnumerator();

            Assert.IsTrue(enumerator.GetType().IsValueType);

            List<int> values = new();
            while (enumerator.MoveNext())
            {
                values.Add(enumerator.Current);
            }

            Assert.AreEqual(set.Count, values.Count);
            foreach (int value in values)
            {
                Assert.IsTrue(set.Contains(value));
            }
        }

        [Test]
        public void EnumeratorThrowsAfterMutationLikeHashSet()
        {
            SerializableHashSet<int> set = new(new[] { 1, 2, 3 });
            using HashSet<int>.Enumerator enumerator = set.GetEnumerator();

            Assert.IsTrue(enumerator.MoveNext());
            set.Add(4);

            Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
        }

        [Test]
        public void TryGetValueReturnsStoredInstance()
        {
            SerializableHashSet<SampleValue> set = new();
            SampleValue stored = new("gamma");
            set.Add(stored);

            SampleValue probe = new("gamma");
            bool found = set.TryGetValue(probe, out SampleValue resolved);

            Assert.IsTrue(found);
            Assert.AreSame(stored, resolved);
        }

        [Test]
        public void CopyToWithCountMatchesSystemHashSet()
        {
            SerializableHashSet<int> serializable = new(new[] { 5, 7, 11, 13, 17 });

            HashSet<int> baseline = new(new[] { 5, 7, 11, 13, 17 });

            int[] serializableTarget = new int[8];
            int[] baselineTarget = new int[8];

            serializable.CopyTo(serializableTarget, 1, 4);
            baseline.CopyTo(baselineTarget, 1, 4);

            CollectionAssert.AreEqual(baselineTarget, serializableTarget);
        }

        [Test]
        public void SetOperationsMatchSystemHashSet()
        {
            SerializableHashSet<int> serializable = new(new[] { 1, 3, 5, 7 });
            HashSet<int> baseline = new(new[] { 1, 3, 5, 7 });

            int[] unionSource = { 5, 6, 9 };
            serializable.UnionWith(unionSource);
            baseline.UnionWith(unionSource);

            int[] exceptSource = { 1, 9 };
            serializable.ExceptWith(exceptSource);
            baseline.ExceptWith(exceptSource);

            int[] symmetricSource = { 3, 4, 6 };
            serializable.SymmetricExceptWith(symmetricSource);
            baseline.SymmetricExceptWith(symmetricSource);

            int[] intersectSource = { 4, 5, 6 };
            serializable.IntersectWith(intersectSource);
            baseline.IntersectWith(intersectSource);

            Assert.IsTrue(serializable.SetEquals(baseline));
            Assert.AreEqual(baseline.Count, serializable.Count);
        }

        [Test]
        public void ProtoSerializationProducesIndependentCopy()
        {
            SerializableHashSet<int> original = new(new[] { 2, 4, 6 });

            byte[] payload = Serializer.ProtoSerialize(original);

            original.Add(8);

            SerializableHashSet<int> roundTrip = Serializer.ProtoDeserialize<
                SerializableHashSet<int>
            >(payload);

            Assert.IsFalse(original.SetEquals(roundTrip));
            Assert.IsTrue(original.Contains(8));
            Assert.IsFalse(roundTrip.Contains(8));

            roundTrip.Add(10);
            Assert.IsFalse(original.Contains(10));
        }

        [Test]
        public void JsonSerializationProducesIndependentCopy()
        {
            SerializableHashSet<string> original = new(new[] { "alpha", "beta", "delta" });

            string json = Serializer.JsonStringify(original);

            original.Add("epsilon");

            SerializableHashSet<string> roundTrip = Serializer.JsonDeserialize<
                SerializableHashSet<string>
            >(json);

            Assert.IsFalse(original.SetEquals(roundTrip));
            Assert.IsTrue(original.Contains("epsilon"));
            Assert.IsFalse(roundTrip.Contains("epsilon"));

            roundTrip.Add("omega");
            Assert.IsFalse(original.Contains("omega"));
        }

        [Test]
        public void ToArrayReturnsEmptyArrayForEmptySet()
        {
            SerializableHashSet<string> set = new();

            string[] result = set.ToArray();

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Length);
            Assert.AreSame(Array.Empty<string>(), result);
        }

        [Test]
        public void ToPersistedOrderArrayReturnsEmptyArrayForEmptySet()
        {
            SerializableHashSet<string> set = new();

            string[] result = set.ToPersistedOrderArray();

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Length);
            Assert.AreSame(Array.Empty<string>(), result);
        }

        [Test]
        public void ToArrayReturnsDefensiveCopyNotInternalArray()
        {
            SerializableHashSet<int> set = new() { 1, 2, 3 };
            set.OnBeforeSerialize();

            int[] firstCopy = set.ToArray();
            int[] secondCopy = set.ToArray();

            Assert.AreNotSame(firstCopy, secondCopy);
            Assert.AreNotSame(firstCopy, set._items);

            firstCopy[0] = 999;
            Assert.AreNotEqual(999, set.ToArray()[0]);
        }

        [Test]
        public void ToPersistedOrderArrayReturnsDefensiveCopyNotInternalArray()
        {
            SerializableHashSet<int> set = new() { 1, 2, 3 };
            set.OnBeforeSerialize();

            int[] firstCopy = set.ToPersistedOrderArray();
            int[] secondCopy = set.ToPersistedOrderArray();

            Assert.AreNotSame(firstCopy, secondCopy);
            Assert.AreNotSame(firstCopy, set._items);

            firstCopy[0] = 999;
            Assert.AreNotEqual(999, set.ToPersistedOrderArray()[0]);
        }

        [Test]
        public void ToArrayReturnsSetIterationOrder()
        {
            SerializableHashSet<int> set = new();
            int[] userOrder = { 5, 3, 8, 1, 9 };
            set._items = userOrder;
            set.OnAfterDeserialize();

            int[] result = set.ToArray();

            // ToArray should return set iteration order, not user-defined order
            Assert.AreEqual(5, result.Length);
            CollectionAssert.AreEquivalent(userOrder, result);
        }

        [Test]
        public void ToPersistedOrderArrayPreservesUserDefinedOrderFromDeserialization()
        {
            SerializableHashSet<int> set = new();
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
            SerializableHashSet<string> set = new() { "alpha", "beta" };
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
        public void ToPersistedOrderArrayOrderSurvivesMultipleSerializationCycles()
        {
            SerializableHashSet<int> set = new();
            int[] userOrder = { 100, 50, 75, 25 };
            set._items = userOrder;
            set.OnAfterDeserialize();

            for (int i = 0; i < 3; i++)
            {
                set.OnBeforeSerialize();
                set.OnAfterDeserialize();
            }

            int[] result = set.ToPersistedOrderArray();

            // ToPersistedOrderArray should preserve user-defined order across serialization cycles
            CollectionAssert.AreEqual(userOrder, result);
        }

        [Test]
        public void ToArrayReturnsSetIterationOrderAfterMultipleSerializationCycles()
        {
            SerializableHashSet<int> set = new();
            int[] userOrder = { 100, 50, 75, 25 };
            set._items = userOrder;
            set.OnAfterDeserialize();

            for (int i = 0; i < 3; i++)
            {
                set.OnBeforeSerialize();
                set.OnAfterDeserialize();
            }

            int[] result = set.ToArray();

            // ToArray should return set iteration order
            Assert.AreEqual(4, result.Length);
            CollectionAssert.AreEquivalent(userOrder, result);
        }

        [Test]
        public void ToArrayWithDuplicatesInSerializedDataReturnsUniqueElements()
        {
            SerializableHashSet<int> set = new();
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
            SerializableHashSet<string> set = new() { "one", "two", "three" };
            set.OnBeforeSerialize();

            set.Clear();

            string[] result = set.ToArray();

            Assert.AreEqual(0, result.Length);
            Assert.AreSame(Array.Empty<string>(), result);
        }

        [Test]
        public void ToArrayContainsAllElementsFromSet()
        {
            SerializableHashSet<int> set = new() { 10, 20, 30, 40, 50 };

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
            SerializableHashSet<string> set = new();

            for (int i = 0; i < 100; i++)
            {
                bool added = set.Add($"item_{i}");
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
            SerializableHashSet<int> set = new();
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
            SerializableHashSet<int> set = new() { 1, 2, 3 };
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
        public void ToPersistedOrderArrayPreservesInsertionOrderForNewItems()
        {
            SerializableHashSet<string> set = new();
            int[] existingOrder = { 1, 2, 3 };
            SerializableHashSet<int> intSet = new();
            intSet._items = existingOrder;
            intSet.OnAfterDeserialize();

            bool addedFour = intSet.Add(4);
            bool addedFive = intSet.Add(5);
            Assert.IsTrue(addedFour);
            Assert.IsTrue(addedFive);

            int[] result = intSet.ToPersistedOrderArray();

            // ToPersistedOrderArray should preserve insertion order for new items
            Assert.AreEqual(5, result.Length);
            for (int i = 0; i < existingOrder.Length; i++)
            {
                Assert.AreEqual(existingOrder[i], result[i]);
            }

            Assert.AreEqual(4, result[3]);
            Assert.AreEqual(5, result[4]);
        }

        [Test]
        public void ToArrayContainsAllElementsAfterAdditions()
        {
            SerializableHashSet<int> intSet = new();
            int[] existingOrder = { 1, 2, 3 };
            intSet._items = existingOrder;
            intSet.OnAfterDeserialize();

            bool addedFour = intSet.Add(4);
            bool addedFive = intSet.Add(5);
            Assert.IsTrue(addedFour);
            Assert.IsTrue(addedFive);

            int[] result = intSet.ToArray();

            // ToArray should contain all elements in set iteration order
            Assert.AreEqual(5, result.Length);
            HashSet<int> resultSet = new(result);
            Assert.IsTrue(resultSet.Contains(1));
            Assert.IsTrue(resultSet.Contains(2));
            Assert.IsTrue(resultSet.Contains(3));
            Assert.IsTrue(resultSet.Contains(4));
            Assert.IsTrue(resultSet.Contains(5));
        }

        [Test]
        public void ToArrayReturnsDefensiveCopy()
        {
            SerializableHashSet<int> intSet = new() { 1, 2, 3 };

            int[] firstCall = intSet.ToArray();
            int[] secondCall = intSet.ToArray();

            Assert.IsFalse(ReferenceEquals(firstCall, secondCall));
            Assert.AreEqual(firstCall.Length, secondCall.Length);
        }

        [Test]
        public void ToPersistedOrderArrayReturnsDefensiveCopy()
        {
            SerializableHashSet<int> intSet = new() { 1, 2, 3 };
            intSet.OnBeforeSerialize();

            int[] firstCall = intSet.ToPersistedOrderArray();
            int[] secondCall = intSet.ToPersistedOrderArray();

            Assert.IsFalse(ReferenceEquals(firstCall, secondCall));
            Assert.AreEqual(firstCall.Length, secondCall.Length);
        }

        [Test]
        public void ToArrayConsistentWithSetEnumeration()
        {
            SerializableHashSet<string> stringSet = new() { "apple", "banana", "cherry" };

            string[] toArrayResult = stringSet.ToArray();
            List<string> enumeratedResult = new();
            foreach (string item in stringSet)
            {
                enumeratedResult.Add(item);
            }

            CollectionAssert.AreEqual(enumeratedResult, toArrayResult);
        }

        [Test]
        public void ToArrayAndToPersistedOrderArrayContainSameElements()
        {
            SerializableHashSet<int> intSet = new() { 5, 10, 15, 20, 25 };
            intSet.OnBeforeSerialize();

            int[] toArrayResult = intSet.ToArray();
            int[] persistedResult = intSet.ToPersistedOrderArray();

            Assert.AreEqual(toArrayResult.Length, persistedResult.Length);
            CollectionAssert.AreEquivalent(toArrayResult, persistedResult);
        }

        [Test]
        public void SingleElementToArrayBehavior()
        {
            SerializableHashSet<int> singleSet = new() { 42 };
            singleSet.OnBeforeSerialize();

            int[] toArrayResult = singleSet.ToArray();
            int[] persistedResult = singleSet.ToPersistedOrderArray();

            Assert.AreEqual(1, toArrayResult.Length);
            Assert.AreEqual(42, toArrayResult[0]);
            Assert.AreEqual(1, persistedResult.Length);
            Assert.AreEqual(42, persistedResult[0]);
        }

        [Test]
        public void ToPersistedOrderArrayReflectsCurrentStateAfterMutations()
        {
            SerializableHashSet<int> intSet = new() { 1, 2, 3 };
            intSet.OnBeforeSerialize();
            int[] initialPersisted = intSet.ToPersistedOrderArray();
            Assert.AreEqual(3, initialPersisted.Length);

            bool addedFour = intSet.Add(4);
            Assert.IsTrue(addedFour);
            intSet.OnBeforeSerialize();

            int[] afterAddPersisted = intSet.ToPersistedOrderArray();
            Assert.AreEqual(4, afterAddPersisted.Length);
            Assert.IsTrue(afterAddPersisted.Contains(4));

            bool removed = intSet.Remove(1);
            Assert.IsTrue(removed);
            intSet.OnBeforeSerialize();

            int[] afterRemovePersisted = intSet.ToPersistedOrderArray();
            Assert.AreEqual(3, afterRemovePersisted.Length);
            Assert.IsFalse(afterRemovePersisted.Contains(1));
        }

        [Test]
        public void ToArrayDoesNotRequireOnBeforeSerialize()
        {
            SerializableHashSet<int> intSet = new() { 10, 20, 30 };

            int[] result = intSet.ToArray();

            Assert.AreEqual(3, result.Length);
            CollectionAssert.AreEquivalent(new[] { 10, 20, 30 }, result);
        }

        [Test]
        public void ToPersistedOrderArrayWithNullValues()
        {
            SerializableHashSet<string> stringSet = new() { "a", null, "b" };
            stringSet.OnBeforeSerialize();

            string[] result = stringSet.ToPersistedOrderArray();

            Assert.AreEqual(3, result.Length);
            Assert.IsTrue(result.Contains(null));
            Assert.IsTrue(result.Contains("a"));
            Assert.IsTrue(result.Contains("b"));
        }

        [Test]
        public void ToArrayWithNullValues()
        {
            SerializableHashSet<string> stringSet = new() { "a", null, "b" };

            string[] result = stringSet.ToArray();

            Assert.AreEqual(3, result.Length);
            Assert.IsTrue(result.Contains(null));
            Assert.IsTrue(result.Contains("a"));
            Assert.IsTrue(result.Contains("b"));
        }

        [Test]
        public void LargeSetToArrayPerformance()
        {
            SerializableHashSet<int> largeSet = new();
            const int count = 10000;
            for (int i = 0; i < count; i++)
            {
                largeSet.Add(i);
            }
            largeSet.OnBeforeSerialize();

            int[] toArrayResult = largeSet.ToArray();
            int[] persistedResult = largeSet.ToPersistedOrderArray();

            Assert.AreEqual(count, toArrayResult.Length);
            Assert.AreEqual(count, persistedResult.Length);
            CollectionAssert.AreEquivalent(toArrayResult, persistedResult);
        }

        [Test]
        public void ToArrayAfterClearReturnsEmpty()
        {
            SerializableHashSet<int> intSet = new() { 1, 2, 3 };
            intSet.Clear();

            int[] result = intSet.ToArray();

            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void ToPersistedOrderArrayAfterClearReturnsEmpty()
        {
            SerializableHashSet<int> intSet = new() { 1, 2, 3 };
            intSet.Clear();
            intSet.OnBeforeSerialize();

            int[] result = intSet.ToPersistedOrderArray();

            Assert.AreEqual(0, result.Length);
        }
    }
}
