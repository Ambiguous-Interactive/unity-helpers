// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditorInternal;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SerializableDictionaryPropertyDrawerUndoTests : CommonTestBase
    {
        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            Undo.ClearAll();
        }

        [TearDown]
        public override void TearDown()
        {
            Undo.ClearAll();
            base.TearDown();
        }

        [Test]
        public void CommitEntryRegistersUndoAndCanBeReverted()
        {
            TestDictionaryHost host = CreateScriptableObject<TestDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            int initialCount = host.dictionary.Count;

            SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(int),
                typeof(string),
                key: 42,
                value: "TestValue",
                dictionaryProperty
            );

            serializedObject.Update();
            Assert.IsTrue(result.added, "CommitEntry should add a new entry.");
            Assert.AreEqual(
                initialCount + 1,
                host.dictionary.Count,
                "Dictionary should have one more entry."
            );
            Assert.IsTrue(
                host.dictionary.ContainsKey(42),
                "Dictionary should contain the committed key."
            );
            Assert.AreEqual(
                "TestValue",
                host.dictionary[42],
                "Dictionary should contain the correct value."
            );

            Undo.PerformUndo();

            serializedObject.Update();
            Assert.AreEqual(
                initialCount,
                host.dictionary.Count,
                "Dictionary count should return to initial after undo."
            );
            Assert.IsFalse(
                host.dictionary.ContainsKey(42),
                "Dictionary should not contain the key after undo."
            );
        }

        [Test]
        public void RemoveEntryAtIndexRegistersUndoAndCanBeReverted()
        {
            TestDictionaryHost host = CreateScriptableObject<TestDictionaryHost>();
            host.dictionary.Add(1, "One");
            host.dictionary.Add(2, "Two");
            host.dictionary.Add(3, "Three");

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(dictionaryProperty);
            ReorderableList list = drawer.GetOrCreateList(dictionaryProperty);

            int initialCount = host.dictionary.Count;
            int keyToRemove = keysProperty.GetArrayElementAtIndex(0).intValue;

            drawer.RemoveEntryAtIndex(
                0,
                list,
                dictionaryProperty,
                keysProperty,
                valuesProperty,
                pagination
            );

            serializedObject.Update();
            Assert.AreEqual(
                initialCount - 1,
                host.dictionary.Count,
                "Dictionary should have one less entry."
            );
            Assert.IsFalse(
                host.dictionary.ContainsKey(keyToRemove),
                "Dictionary should not contain the removed key."
            );

            Undo.PerformUndo();

            serializedObject.Update();
            Assert.AreEqual(
                initialCount,
                host.dictionary.Count,
                "Dictionary count should return to initial after undo."
            );
            Assert.IsTrue(
                host.dictionary.ContainsKey(keyToRemove),
                "Dictionary should contain the key after undo."
            );
        }

        [Test]
        public void ClearDictionaryRegistersUndoAndCanBeReverted()
        {
            TestDictionaryHost host = CreateScriptableObject<TestDictionaryHost>();
            host.dictionary.Add(10, "Ten");
            host.dictionary.Add(20, "Twenty");
            host.dictionary.Add(30, "Thirty");

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(dictionaryProperty);
            ReorderableList list = drawer.GetOrCreateList(dictionaryProperty);

            int initialCount = host.dictionary.Count;
            Assert.Greater(initialCount, 0, "Dictionary should have entries before clear.");

            drawer.InvokeClearDictionary(
                dictionaryProperty,
                keysProperty,
                valuesProperty,
                pagination,
                list
            );

            serializedObject.Update();
            Assert.AreEqual(0, host.dictionary.Count, "Dictionary should be empty after clear.");

            Undo.PerformUndo();

            serializedObject.Update();
            Assert.AreEqual(
                initialCount,
                host.dictionary.Count,
                "Dictionary count should return to initial after undo."
            );
            Assert.IsTrue(
                host.dictionary.ContainsKey(10),
                "Dictionary should contain original keys after undo."
            );
            Assert.IsTrue(
                host.dictionary.ContainsKey(20),
                "Dictionary should contain original keys after undo."
            );
            Assert.IsTrue(
                host.dictionary.ContainsKey(30),
                "Dictionary should contain original keys after undo."
            );
        }

        [Test]
        public void SortDictionaryEntriesWithNullComparisonDoesNotModifyDictionary()
        {
            TestDictionaryHost host = CreateScriptableObject<TestDictionaryHost>();
            host.dictionary.Add(30, "Thirty");
            host.dictionary.Add(10, "Ten");
            host.dictionary.Add(20, "Twenty");

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(dictionaryProperty);
            ReorderableList list = drawer.GetOrCreateList(dictionaryProperty);

            int originalFirstKey = keysProperty.GetArrayElementAtIndex(0).intValue;
            int originalSecondKey = keysProperty.GetArrayElementAtIndex(1).intValue;
            int originalThirdKey = keysProperty.GetArrayElementAtIndex(2).intValue;

            drawer.SortDictionaryEntries(
                dictionaryProperty,
                keysProperty,
                valuesProperty,
                typeof(int),
                typeof(string),
                null,
                pagination,
                list
            );

            serializedObject.Update();
            keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            int actualFirstKey = keysProperty.GetArrayElementAtIndex(0).intValue;
            int actualSecondKey = keysProperty.GetArrayElementAtIndex(1).intValue;
            int actualThirdKey = keysProperty.GetArrayElementAtIndex(2).intValue;

            Assert.AreEqual(
                originalFirstKey,
                actualFirstKey,
                "First key should remain unchanged when comparison is null."
            );
            Assert.AreEqual(
                originalSecondKey,
                actualSecondKey,
                "Second key should remain unchanged when comparison is null."
            );
            Assert.AreEqual(
                originalThirdKey,
                actualThirdKey,
                "Third key should remain unchanged when comparison is null."
            );
        }

        [Test]
        public void SortDictionaryEntriesRegistersUndoAndCanBeReverted()
        {
            TestDictionaryHost host = CreateScriptableObject<TestDictionaryHost>();
            host.dictionary.Add(30, "Thirty");
            host.dictionary.Add(10, "Ten");
            host.dictionary.Add(20, "Twenty");

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(dictionaryProperty);
            ReorderableList list = drawer.GetOrCreateList(dictionaryProperty);

            int originalFirstKey = keysProperty.GetArrayElementAtIndex(0).intValue;
            int originalSecondKey = keysProperty.GetArrayElementAtIndex(1).intValue;
            int originalThirdKey = keysProperty.GetArrayElementAtIndex(2).intValue;

            // Provide a comparison function - the production code early-returns if comparison is null
            Func<object, object, int> comparison = (a, b) => ((int)a).CompareTo((int)b);

            drawer.SortDictionaryEntries(
                dictionaryProperty,
                keysProperty,
                valuesProperty,
                typeof(int),
                typeof(string),
                comparison,
                pagination,
                list
            );

            serializedObject.Update();
            keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            int sortedFirst = keysProperty.GetArrayElementAtIndex(0).intValue;
            int sortedSecond = keysProperty.GetArrayElementAtIndex(1).intValue;
            int sortedThird = keysProperty.GetArrayElementAtIndex(2).intValue;

            Assert.LessOrEqual(sortedFirst, sortedSecond, "Keys should be sorted after sort.");
            Assert.LessOrEqual(sortedSecond, sortedThird, "Keys should be sorted after sort.");

            Undo.PerformUndo();

            serializedObject.Update();
            keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            int restoredFirst = keysProperty.GetArrayElementAtIndex(0).intValue;
            int restoredSecond = keysProperty.GetArrayElementAtIndex(1).intValue;
            int restoredThird = keysProperty.GetArrayElementAtIndex(2).intValue;

            Assert.AreEqual(
                originalFirstKey,
                restoredFirst,
                "First key should be restored after undo."
            );
            Assert.AreEqual(
                originalSecondKey,
                restoredSecond,
                "Second key should be restored after undo."
            );
            Assert.AreEqual(
                originalThirdKey,
                restoredThird,
                "Third key should be restored after undo."
            );
        }

        [Test]
        public void UndoAfterAddThenRemoveRestoresOriginalState()
        {
            TestDictionaryHost host = CreateScriptableObject<TestDictionaryHost>();
            host.dictionary.Add(1, "One");
            host.dictionary.Add(2, "Two");

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(dictionaryProperty);
            ReorderableList list = drawer.GetOrCreateList(dictionaryProperty);

            int initialCount = host.dictionary.Count;

            drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(int),
                typeof(string),
                key: 99,
                value: "NinetyNine",
                dictionaryProperty
            );
            serializedObject.Update();
            Assert.AreEqual(initialCount + 1, host.dictionary.Count, "Count after add.");

            // Unity's undo system collapses operations sharing a name within one frame.
            Undo.IncrementCurrentGroup();

            drawer.RemoveEntryAtIndex(
                0,
                list,
                dictionaryProperty,
                keysProperty,
                valuesProperty,
                pagination
            );
            serializedObject.Update();
            Assert.AreEqual(initialCount, host.dictionary.Count, "Count after add then remove.");

            Undo.PerformUndo();
            serializedObject.Update();
            Assert.AreEqual(
                initialCount + 1,
                host.dictionary.Count,
                "First undo should restore removed entry."
            );

            Undo.PerformUndo();
            serializedObject.Update();
            Assert.AreEqual(
                initialCount,
                host.dictionary.Count,
                "Second undo should restore original state."
            );
        }

        [Test]
        public void RedoAfterUndoRestoresModifiedState()
        {
            TestDictionaryHost host = CreateScriptableObject<TestDictionaryHost>();
            host.dictionary.Add(5, "Five");

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            int initialCount = host.dictionary.Count;

            drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(int),
                typeof(string),
                key: 100,
                value: "Hundred",
                dictionaryProperty
            );
            serializedObject.Update();
            int countAfterAdd = host.dictionary.Count;
            Assert.AreEqual(initialCount + 1, countAfterAdd, "Count after add.");

            Undo.PerformUndo();
            serializedObject.Update();
            Assert.AreEqual(
                initialCount,
                host.dictionary.Count,
                "Undo should restore original count."
            );

            Undo.PerformRedo();
            serializedObject.Update();
            Assert.AreEqual(
                countAfterAdd,
                host.dictionary.Count,
                "Redo should restore the added entry."
            );
            Assert.IsTrue(host.dictionary.ContainsKey(100), "Redo should restore the added key.");
        }

        [Test]
        public void MultipleUndoRedoCyclesWorkCorrectly()
        {
            TestDictionaryHost host = CreateScriptableObject<TestDictionaryHost>();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();

            Assert.AreEqual(0, host.dictionary.Count, "Dictionary should start empty.");

            drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(int),
                typeof(string),
                1,
                "One",
                dictionaryProperty
            );
            serializedObject.Update();
            Assert.AreEqual(1, host.dictionary.Count, "Count after first add.");

            // Unity's undo system collapses operations sharing a name within one frame.
            Undo.IncrementCurrentGroup();

            drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(int),
                typeof(string),
                2,
                "Two",
                dictionaryProperty
            );
            serializedObject.Update();
            Assert.AreEqual(2, host.dictionary.Count, "Count after second add.");

            Undo.IncrementCurrentGroup();

            drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(int),
                typeof(string),
                3,
                "Three",
                dictionaryProperty
            );
            serializedObject.Update();
            Assert.AreEqual(3, host.dictionary.Count, "Count after third add.");

            Undo.PerformUndo();
            serializedObject.Update();
            Assert.AreEqual(2, host.dictionary.Count, "Count after first undo.");

            Undo.PerformUndo();
            serializedObject.Update();
            Assert.AreEqual(1, host.dictionary.Count, "Count after second undo.");

            Undo.PerformRedo();
            serializedObject.Update();
            Assert.AreEqual(2, host.dictionary.Count, "Count after redo.");

            Undo.PerformUndo();
            serializedObject.Update();
            Assert.AreEqual(1, host.dictionary.Count, "Count after undo again.");

            Undo.PerformUndo();
            serializedObject.Update();
            Assert.AreEqual(0, host.dictionary.Count, "Count after final undo to empty.");

            Undo.PerformRedo();
            Undo.PerformRedo();
            Undo.PerformRedo();
            serializedObject.Update();
            Assert.AreEqual(3, host.dictionary.Count, "Count after multiple redos.");
        }

        [Test]
        public void OverwriteExistingEntryValueRegistersUndoAndCanBeReverted()
        {
            TestDictionaryHost host = CreateScriptableObject<TestDictionaryHost>();
            host.dictionary.Add(42, "OriginalValue");

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();

            Assert.AreEqual(
                "OriginalValue",
                host.dictionary[42],
                "Original value before overwrite."
            );

            SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(int),
                typeof(string),
                key: 42,
                value: "NewValue",
                dictionaryProperty,
                existingIndex: 0
            );

            serializedObject.Update();
            Assert.IsFalse(result.added, "CommitEntry should overwrite, not add.");
            Assert.AreEqual(
                1,
                host.dictionary.Count,
                "Count should remain the same after overwrite."
            );
            Assert.AreEqual("NewValue", host.dictionary[42], "Value should be updated.");

            Undo.PerformUndo();

            serializedObject.Update();
            Assert.AreEqual(
                "OriginalValue",
                host.dictionary[42],
                "Value should be restored after undo."
            );
        }
    }
}
