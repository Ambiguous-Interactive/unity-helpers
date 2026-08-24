// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SerializableSetPropertyDrawerUndoTests : CommonTestBase
    {
        private static string FormatSetContents<T>(IEnumerable<T> set)
        {
            return string.Join(", ", set.Select(item => item?.ToString() ?? "null"));
        }

        private static string FormatArrayContents(SerializedProperty itemsProperty)
        {
            if (itemsProperty == null || !itemsProperty.isArray)
            {
                return "<null or non-array>";
            }

            List<string> values = new(itemsProperty.arraySize);
            for (int i = 0; i < itemsProperty.arraySize; i++)
            {
                SerializedProperty element = itemsProperty.GetArrayElementAtIndex(i);
                values.Add(
                    element.propertyType switch
                    {
                        SerializedPropertyType.Integer => element.intValue.ToString(),
                        SerializedPropertyType.String => element.stringValue ?? "null",
                        _ => element.propertyType.ToString(),
                    }
                );
            }

            return string.Join(", ", values);
        }

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
        public void TryCommitPendingEntryRegistersUndoAndCanBeReverted()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );

            SerializableSetPropertyDrawer drawer = new();
            string propertyPath = setProperty.propertyPath;
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                propertyPath,
                typeof(string),
                isSortedSet: false
            );
            pending.value = "TestValue";

            ISerializableSetInspector inspector = host.set;
            Assert.IsTrue(inspector != null, "Expected ISerializableSetInspector implementation.");

            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int initialCount = host.set.Count;
            string initialContents = FormatSetContents(host.set);

            bool result = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(
                result,
                $"TryCommitPendingEntry should succeed. Initial state: [{initialContents}], pending value: 'TestValue'."
            );
            string afterCommitContents = FormatSetContents(host.set);
            Assert.AreEqual(
                initialCount + 1,
                host.set.Count,
                $"Set should have one more entry after commit. Initial ({initialCount}): [{initialContents}], After commit ({host.set.Count}): [{afterCommitContents}]."
            );
            Assert.IsTrue(
                host.set.Contains("TestValue"),
                $"Set should contain the committed value 'TestValue'. After commit: [{afterCommitContents}]."
            );

            Undo.PerformUndo();

            serializedObject.Update();
            string afterUndoContents = FormatSetContents(host.set);
            Assert.AreEqual(
                initialCount,
                host.set.Count,
                $"Set count should return to initial after undo. Initial ({initialCount}): [{initialContents}], After commit ({initialCount + 1}): [{afterCommitContents}], After undo ({host.set.Count}): [{afterUndoContents}]."
            );
            Assert.IsFalse(
                host.set.Contains("TestValue"),
                $"Set should not contain 'TestValue' after undo. After undo: [{afterUndoContents}]."
            );
        }

        [Test]
        [TestCase(0, TestName = "InitialSize.Empty")]
        [TestCase(1, TestName = "InitialSize.Single")]
        [TestCase(3, TestName = "InitialSize.Three")]
        [TestCase(5, TestName = "InitialSize.Five")]
        public void TryAddNewElementUndoWorksWithVaryingInitialSetSizes(int initialCount)
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            for (int i = 0; i < initialCount; i++)
            {
                host.set.Add(i * 100);
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));

            SerializableSetPropertyDrawer drawer = new();
            string propertyPath = setProperty.propertyPath;
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);

            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int beforeAddCount = host.set.Count;
            string beforeAddContents = FormatSetContents(host.set);
            Assert.AreEqual(
                initialCount,
                beforeAddCount,
                $"Initial set size should be {initialCount}. Contents: [{beforeAddContents}]."
            );

            bool result = drawer.TryAddNewElement(
                ref setProperty,
                propertyPath,
                ref itemsProperty,
                pagination
            );

            Assert.IsTrue(
                result,
                $"TryAddNewElement should succeed for initial size {initialCount}."
            );
            int afterAddCount = host.set.Count;
            string afterAddContents = FormatSetContents(host.set);
            Assert.AreEqual(
                beforeAddCount + 1,
                afterAddCount,
                $"Set should have one more entry after add. Before ({beforeAddCount}): [{beforeAddContents}], After ({afterAddCount}): [{afterAddContents}]."
            );

            Undo.PerformUndo();

            serializedObject.Update();
            int afterUndoCount = host.set.Count;
            string afterUndoContents = FormatSetContents(host.set);
            Assert.AreEqual(
                beforeAddCount,
                afterUndoCount,
                $"Undo should restore original count for initial size {initialCount}. Before add ({beforeAddCount}): [{beforeAddContents}], After add ({afterAddCount}): [{afterAddContents}], After undo ({afterUndoCount}): [{afterUndoContents}]."
            );
        }

        [Test]
        [TestCase(2, TestName = "InitialSize.Two")]
        [TestCase(3, TestName = "InitialSize.Three")]
        [TestCase(5, TestName = "InitialSize.Five")]
        [TestCase(10, TestName = "InitialSize.Ten")]
        public void TryClearSetUndoWorksWithVaryingSetSizes(int initialCount)
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            for (int i = 0; i < initialCount; i++)
            {
                host.set.Add(i * 10);
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int beforeClearCount = host.set.Count;
            string beforeClearContents = FormatSetContents(host.set);
            Assert.AreEqual(
                initialCount,
                beforeClearCount,
                $"Initial set size should be {initialCount}. Contents: [{beforeClearContents}]."
            );

            bool result = drawer.InvokeTryClearSet(
                ref setProperty,
                propertyPath,
                ref itemsProperty
            );

            Assert.IsTrue(result, $"TryClearSet should succeed for initial size {initialCount}.");
            serializedObject.Update();
            Assert.AreEqual(
                0,
                host.set.Count,
                $"Set should be empty after clear. Before ({beforeClearCount}): [{beforeClearContents}]."
            );

            Undo.PerformUndo();

            serializedObject.Update();
            int afterUndoCount = host.set.Count;
            string afterUndoContents = FormatSetContents(host.set);
            Assert.AreEqual(
                beforeClearCount,
                afterUndoCount,
                $"Undo should restore original count for initial size {initialCount}. Before clear ({beforeClearCount}): [{beforeClearContents}], After undo ({afterUndoCount}): [{afterUndoContents}]."
            );
        }

        [Test]
        public void RemoveSelectedEntryRegistersUndoAndCanBeReverted()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            host.set.Add(100);
            host.set.Add(200);
            host.set.Add(300);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            pagination.selectedIndex = 0;

            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int initialCount = host.set.Count;
            string initialContents = FormatSetContents(host.set);

            drawer.InvokeTryRemoveSelectedEntry(
                ref setProperty,
                propertyPath,
                ref itemsProperty,
                pagination
            );

            serializedObject.Update();
            int afterRemoveCount = host.set.Count;
            string afterRemoveContents = FormatSetContents(host.set);
            Assert.AreEqual(
                initialCount - 1,
                afterRemoveCount,
                $"Set should have one less entry after removal. Initial ({initialCount}): [{initialContents}], After remove ({afterRemoveCount}): [{afterRemoveContents}]."
            );

            Undo.PerformUndo();

            serializedObject.Update();
            int afterUndoCount = host.set.Count;
            string afterUndoContents = FormatSetContents(host.set);
            Assert.AreEqual(
                initialCount,
                afterUndoCount,
                $"Set count should return to initial after undo. Initial ({initialCount}): [{initialContents}], After remove ({afterRemoveCount}): [{afterRemoveContents}], After undo ({afterUndoCount}): [{afterUndoContents}]."
            );
        }

        [Test]
        public void MoveSelectedEntryRegistersUndoAndCanBeReverted()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            host.set.Add(1);
            host.set.Add(2);
            host.set.Add(3);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            pagination.selectedIndex = 0;

            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializedProperty firstElement = itemsProperty.GetArrayElementAtIndex(0);
            int originalFirstValue = firstElement.intValue;
            string originalOrder = FormatArrayContents(itemsProperty);

            drawer.InvokeTryMoveSelectedEntry(
                ref setProperty,
                propertyPath,
                ref itemsProperty,
                pagination,
                1
            );

            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            string afterMoveOrder = FormatArrayContents(itemsProperty);
            SerializedProperty elementAfterMove = itemsProperty.GetArrayElementAtIndex(1);
            Assert.AreEqual(
                originalFirstValue,
                elementAfterMove.intValue,
                $"Element should have moved to index 1. Original order: [{originalOrder}], After move: [{afterMoveOrder}]."
            );

            Undo.PerformUndo();

            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            string afterUndoOrder = FormatArrayContents(itemsProperty);
            SerializedProperty elementAfterUndo = itemsProperty.GetArrayElementAtIndex(0);
            Assert.AreEqual(
                originalFirstValue,
                elementAfterUndo.intValue,
                $"Element should return to index 0 after undo. Original: [{originalOrder}], After move: [{afterMoveOrder}], After undo: [{afterUndoOrder}]."
            );
        }

        [Test]
        [TestCase(new[] { 30, 10, 20 }, TestName = "Order.Unsorted")]
        [TestCase(new[] { 10, 20, 30 }, TestName = "Order.AlreadySorted")]
        [TestCase(new[] { 30, 20, 10 }, TestName = "Order.ReverseSorted")]
        [TestCase(new[] { 5, 5, 10 }, TestName = "Order.AlreadySortedAfterDedup")]
        public void SortElementsUndoRestoresOriginalOrder(int[] initialValues)
        {
            SortedSetHost host = CreateScriptableObject<SortedSetHost>();
            foreach (int value in initialValues)
            {
                host.set.Add(value);
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SortedSetHost.set)
            );
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int arraySize = itemsProperty.arraySize;
            List<int> originalOrder = new();
            for (int i = 0; i < arraySize; i++)
            {
                originalOrder.Add(itemsProperty.GetArrayElementAtIndex(i).intValue);
            }
            string originalOrderStr = string.Join(", ", originalOrder);

            bool sorted = drawer.InvokeTrySortElements(
                ref setProperty,
                propertyPath,
                itemsProperty
            );

            Assert.IsTrue(
                sorted,
                $"TrySortElements should succeed. Original order: [{originalOrderStr}]."
            );
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            string afterSortOrder = FormatArrayContents(itemsProperty);
            for (int i = 0; i < itemsProperty.arraySize - 1; i++)
            {
                Assert.LessOrEqual(
                    itemsProperty.GetArrayElementAtIndex(i).intValue,
                    itemsProperty.GetArrayElementAtIndex(i + 1).intValue,
                    $"Elements should be sorted after sort at index {i}. After sort: [{afterSortOrder}]."
                );
            }

            Undo.PerformUndo();

            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            string afterUndoOrder = FormatArrayContents(itemsProperty);

            for (int i = 0; i < originalOrder.Count && i < itemsProperty.arraySize; i++)
            {
                Assert.AreEqual(
                    originalOrder[i],
                    itemsProperty.GetArrayElementAtIndex(i).intValue,
                    $"Element at index {i} should return to original value after undo. Original: [{originalOrderStr}], After sort: [{afterSortOrder}], After undo: [{afterUndoOrder}]."
                );
            }
        }

        [Test]
        public void UndoAfterAddThenRemoveRestoresOriginalState()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            host.set.Add(1);
            host.set.Add(2);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int initialCount = host.set.Count;
            string initialContents = FormatSetContents(host.set);

            bool addResult = drawer.TryAddNewElement(
                ref setProperty,
                propertyPath,
                ref itemsProperty,
                pagination
            );
            Assert.IsTrue(addResult, "Add should succeed.");
            serializedObject.Update();
            int afterAddCount = host.set.Count;
            string afterAddContents = FormatSetContents(host.set);
            Assert.AreEqual(
                initialCount + 1,
                afterAddCount,
                $"Set should have one more entry. Initial ({initialCount}): [{initialContents}], After add ({afterAddCount}): [{afterAddContents}]."
            );

            Undo.IncrementCurrentGroup();

            pagination.selectedIndex = 0;
            drawer.InvokeTryRemoveSelectedEntry(
                ref setProperty,
                propertyPath,
                ref itemsProperty,
                pagination
            );
            serializedObject.Update();
            int afterRemoveCount = host.set.Count;
            string afterRemoveContents = FormatSetContents(host.set);
            Assert.AreEqual(
                initialCount,
                afterRemoveCount,
                $"Set count should match after add then remove. After add ({afterAddCount}): [{afterAddContents}], After remove ({afterRemoveCount}): [{afterRemoveContents}]."
            );

            Undo.PerformUndo();
            serializedObject.Update();
            int afterFirstUndoCount = host.set.Count;
            string afterFirstUndo = FormatSetContents(host.set);
            Assert.AreEqual(
                initialCount + 1,
                afterFirstUndoCount,
                $"First undo should restore the added entry. After remove ({afterRemoveCount}): [{afterRemoveContents}], After undo ({afterFirstUndoCount}): [{afterFirstUndo}]."
            );

            Undo.PerformUndo();
            serializedObject.Update();
            int afterSecondUndoCount = host.set.Count;
            string afterSecondUndo = FormatSetContents(host.set);
            Assert.AreEqual(
                initialCount,
                afterSecondUndoCount,
                $"Second undo should restore original state. Initial ({initialCount}): [{initialContents}], After first undo ({afterFirstUndoCount}): [{afterFirstUndo}], After second undo ({afterSecondUndoCount}): [{afterSecondUndo}]."
            );
        }

        [Test]
        public void RedoAfterUndoRestoresModifiedState()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            host.set.Add(5);
            host.set.Add(10);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int initialCount = host.set.Count;
            string initialContents = FormatSetContents(host.set);

            bool addResult = drawer.TryAddNewElement(
                ref setProperty,
                propertyPath,
                ref itemsProperty,
                pagination
            );
            Assert.IsTrue(addResult, "Add should succeed.");
            serializedObject.Update();
            int countAfterAdd = host.set.Count;
            string afterAddContents = FormatSetContents(host.set);
            Assert.AreEqual(
                initialCount + 1,
                countAfterAdd,
                $"Set should have one more entry. Initial ({initialCount}): [{initialContents}], After add ({countAfterAdd}): [{afterAddContents}]."
            );

            Undo.PerformUndo();
            serializedObject.Update();
            int afterUndoCount = host.set.Count;
            string afterUndoContents = FormatSetContents(host.set);
            Assert.AreEqual(
                initialCount,
                afterUndoCount,
                $"Undo should restore original count. Initial ({initialCount}): [{initialContents}], After add ({countAfterAdd}): [{afterAddContents}], After undo ({afterUndoCount}): [{afterUndoContents}]."
            );

            Undo.PerformRedo();
            serializedObject.Update();
            int afterRedoCount = host.set.Count;
            string afterRedoContents = FormatSetContents(host.set);
            Assert.AreEqual(
                countAfterAdd,
                afterRedoCount,
                $"Redo should restore the added entry. After add ({countAfterAdd}): [{afterAddContents}], After undo ({afterUndoCount}): [{afterUndoContents}], After redo ({afterRedoCount}): [{afterRedoContents}]."
            );
        }

        [Test]
        public void MultipleUndoRedoCyclesWorkCorrectly()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.AreEqual(0, host.set.Count, "Set should start empty.");

            drawer.TryAddNewElement(ref setProperty, propertyPath, ref itemsProperty, pagination);
            serializedObject.Update();
            Assert.AreEqual(
                1,
                host.set.Count,
                $"Count after first add. Contents: [{FormatSetContents(host.set)}]."
            );

            Undo.IncrementCurrentGroup();

            drawer.TryAddNewElement(ref setProperty, propertyPath, ref itemsProperty, pagination);
            serializedObject.Update();
            Assert.AreEqual(
                2,
                host.set.Count,
                $"Count after second add. Contents: [{FormatSetContents(host.set)}]."
            );

            Undo.IncrementCurrentGroup();

            drawer.TryAddNewElement(ref setProperty, propertyPath, ref itemsProperty, pagination);
            serializedObject.Update();
            string afterThirdAdd = FormatSetContents(host.set);
            Assert.AreEqual(
                3,
                host.set.Count,
                $"Count after third add. Contents: [{afterThirdAdd}]."
            );

            Undo.PerformUndo();
            serializedObject.Update();
            string afterFirstUndo = FormatSetContents(host.set);
            Assert.AreEqual(
                2,
                host.set.Count,
                $"Count after first undo. Before ({3}): [{afterThirdAdd}], After ({host.set.Count}): [{afterFirstUndo}]."
            );

            Undo.PerformUndo();
            serializedObject.Update();
            string afterSecondUndo = FormatSetContents(host.set);
            Assert.AreEqual(
                1,
                host.set.Count,
                $"Count after second undo. Before ({2}): [{afterFirstUndo}], After ({host.set.Count}): [{afterSecondUndo}]."
            );

            Undo.PerformRedo();
            serializedObject.Update();
            string afterRedo = FormatSetContents(host.set);
            Assert.AreEqual(
                2,
                host.set.Count,
                $"Count after redo. Before ({1}): [{afterSecondUndo}], After ({host.set.Count}): [{afterRedo}]."
            );

            Undo.PerformUndo();
            serializedObject.Update();
            string afterUndoAgain = FormatSetContents(host.set);
            Assert.AreEqual(
                1,
                host.set.Count,
                $"Count after undo again. Before ({2}): [{afterRedo}], After ({host.set.Count}): [{afterUndoAgain}]."
            );

            Undo.PerformUndo();
            serializedObject.Update();
            string afterFinalUndo = FormatSetContents(host.set);
            Assert.AreEqual(
                0,
                host.set.Count,
                $"Count after final undo to empty. Before ({1}): [{afterUndoAgain}], After ({host.set.Count}): [{afterFinalUndo}]."
            );

            Undo.PerformRedo();
            Undo.PerformRedo();
            Undo.PerformRedo();
            serializedObject.Update();
            string afterMultipleRedos = FormatSetContents(host.set);
            Assert.AreEqual(
                3,
                host.set.Count,
                $"Count after multiple redos. Expected 3, got {host.set.Count}. Contents: [{afterMultipleRedos}]."
            );
        }

        [Test]
        public void UndoOnEmptySetDoesNotCauseErrors()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            Assert.AreEqual(0, host.set.Count, "Set should start empty.");

            Undo.PerformUndo();
            serializedObject.Update();

            Assert.AreEqual(0, host.set.Count, "Set should remain empty after undo on empty set.");
        }

        [Test]
        public void ClearEmptySetDoesNotCorruptState()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.AreEqual(0, host.set.Count, "Set should start empty.");

            bool result = drawer.InvokeTryClearSet(
                ref setProperty,
                propertyPath,
                ref itemsProperty
            );

            Assert.IsTrue(result, "TryClearSet should return true even for empty set.");
            serializedObject.Update();
            Assert.AreEqual(0, host.set.Count, "Set should remain empty after clearing empty set.");

            Undo.PerformUndo();
            serializedObject.Update();
            Assert.AreEqual(
                0,
                host.set.Count,
                "Set should remain empty after undo of clear on empty set."
            );

            Undo.IncrementCurrentGroup();

            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            bool addResult = drawer.TryAddNewElement(
                ref setProperty,
                propertyPath,
                ref itemsProperty,
                pagination
            );

            Assert.IsTrue(
                addResult,
                "TryAddNewElement should succeed after clear+undo on empty set."
            );
            serializedObject.Update();
            Assert.AreEqual(
                1,
                host.set.Count,
                $"Set should have 1 element after add following clear+undo on empty set. Contents: [{FormatSetContents(host.set)}]."
            );
        }

        [Test]
        public void UndoAfterMultipleOperationsPreservesCorrectOrder()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            host.set.Add(1);
            host.set.Add(2);
            host.set.Add(3);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            string initialContents = FormatSetContents(host.set);
            drawer.TryAddNewElement(ref setProperty, propertyPath, ref itemsProperty, pagination);
            serializedObject.Update();
            string afterAdd = FormatSetContents(host.set);
            Assert.AreEqual(
                4,
                host.set.Count,
                $"Count after add. Initial: [{initialContents}], After add: [{afterAdd}]."
            );

            Undo.IncrementCurrentGroup();

            pagination.selectedIndex = 0;
            drawer.InvokeTryRemoveSelectedEntry(
                ref setProperty,
                propertyPath,
                ref itemsProperty,
                pagination
            );
            serializedObject.Update();
            string afterRemove = FormatSetContents(host.set);
            Assert.AreEqual(
                3,
                host.set.Count,
                $"Count after remove. After add: [{afterAdd}], After remove: [{afterRemove}]."
            );

            Undo.IncrementCurrentGroup();

            drawer.InvokeTryClearSet(ref setProperty, propertyPath, ref itemsProperty);
            serializedObject.Update();
            string afterClear = FormatSetContents(host.set);
            Assert.AreEqual(
                0,
                host.set.Count,
                $"Count after clear. After remove: [{afterRemove}], After clear: [{afterClear}]."
            );

            Undo.PerformUndo();
            serializedObject.Update();
            string afterFirstUndo = FormatSetContents(host.set);
            Assert.AreEqual(
                3,
                host.set.Count,
                $"First undo restores before clear. After clear ({0}): [{afterClear}], After undo ({host.set.Count}): [{afterFirstUndo}]."
            );

            Undo.PerformUndo();
            serializedObject.Update();
            string afterSecondUndo = FormatSetContents(host.set);
            Assert.AreEqual(
                4,
                host.set.Count,
                $"Second undo restores removed entry. After first undo ({3}): [{afterFirstUndo}], After second undo ({host.set.Count}): [{afterSecondUndo}]."
            );

            Undo.PerformUndo();
            serializedObject.Update();
            string afterThirdUndo = FormatSetContents(host.set);
            Assert.AreEqual(
                3,
                host.set.Count,
                $"Third undo restores before add. After second undo ({4}): [{afterSecondUndo}], After third undo ({host.set.Count}): [{afterThirdUndo}]."
            );
        }

        [Test]
        public void MoveEntryDownThenUpThenUndoRestoresOriginalOrder()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            host.set.Add(100);
            host.set.Add(200);
            host.set.Add(300);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int originalFirst = itemsProperty.GetArrayElementAtIndex(0).intValue;
            int originalSecond = itemsProperty.GetArrayElementAtIndex(1).intValue;
            int originalThird = itemsProperty.GetArrayElementAtIndex(2).intValue;
            string originalOrder = $"[{originalFirst}, {originalSecond}, {originalThird}]";

            pagination.selectedIndex = 0;
            drawer.InvokeTryMoveSelectedEntry(
                ref setProperty,
                propertyPath,
                ref itemsProperty,
                pagination,
                1
            );
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int afterMoveDown0 = itemsProperty.GetArrayElementAtIndex(0).intValue;
            int afterMoveDown1 = itemsProperty.GetArrayElementAtIndex(1).intValue;
            int afterMoveDown2 = itemsProperty.GetArrayElementAtIndex(2).intValue;
            string afterMoveDownOrder = $"[{afterMoveDown0}, {afterMoveDown1}, {afterMoveDown2}]";

            Assert.AreEqual(
                originalFirst,
                afterMoveDown1,
                $"First element moved down. Original: {originalOrder}, After move down: {afterMoveDownOrder}."
            );

            Undo.IncrementCurrentGroup();

            pagination.selectedIndex = 1;
            drawer.InvokeTryMoveSelectedEntry(
                ref setProperty,
                propertyPath,
                ref itemsProperty,
                pagination,
                -1
            );
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int afterMoveUp0 = itemsProperty.GetArrayElementAtIndex(0).intValue;
            int afterMoveUp1 = itemsProperty.GetArrayElementAtIndex(1).intValue;
            int afterMoveUp2 = itemsProperty.GetArrayElementAtIndex(2).intValue;
            string afterMoveUpOrder = $"[{afterMoveUp0}, {afterMoveUp1}, {afterMoveUp2}]";

            Assert.AreEqual(
                originalFirst,
                afterMoveUp0,
                $"First element moved back up. After move down: {afterMoveDownOrder}, After move up: {afterMoveUpOrder}."
            );

            Undo.PerformUndo();
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            int afterUndo1Pos0 = itemsProperty.GetArrayElementAtIndex(0).intValue;
            int afterUndo1Pos1 = itemsProperty.GetArrayElementAtIndex(1).intValue;
            int afterUndo1Pos2 = itemsProperty.GetArrayElementAtIndex(2).intValue;
            string afterUndo1Order = $"[{afterUndo1Pos0}, {afterUndo1Pos1}, {afterUndo1Pos2}]";

            Assert.AreEqual(
                originalSecond,
                afterUndo1Pos0,
                $"Undo restores after first move. Original: {originalOrder}, After move down: {afterMoveDownOrder}, After move up: {afterMoveUpOrder}, After first undo: {afterUndo1Order}."
            );

            Undo.PerformUndo();
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            int afterUndo2Pos0 = itemsProperty.GetArrayElementAtIndex(0).intValue;
            int afterUndo2Pos1 = itemsProperty.GetArrayElementAtIndex(1).intValue;
            int afterUndo2Pos2 = itemsProperty.GetArrayElementAtIndex(2).intValue;
            string afterUndo2Order = $"[{afterUndo2Pos0}, {afterUndo2Pos1}, {afterUndo2Pos2}]";

            Assert.AreEqual(
                originalFirst,
                afterUndo2Pos0,
                $"Second undo restores original order. Original: {originalOrder}, After first undo: {afterUndo1Order}, After second undo: {afterUndo2Order}."
            );
        }

        [Test]
        public void UndoAfterConsecutiveAddsRestoresEachStateCorrectly()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.AreEqual(0, host.set.Count, "Set should start empty.");

            int numberOfAdds = 5;
            for (int i = 0; i < numberOfAdds; i++)
            {
                if (0 < i)
                {
                    Undo.IncrementCurrentGroup();
                }

                drawer.TryAddNewElement(
                    ref setProperty,
                    propertyPath,
                    ref itemsProperty,
                    pagination
                );
                serializedObject.Update();
                Assert.AreEqual(
                    i + 1,
                    host.set.Count,
                    $"Count after add {i + 1} of {numberOfAdds}: [{FormatSetContents(host.set)}]."
                );
            }

            for (int i = numberOfAdds - 1; 0 <= i; i--)
            {
                Undo.PerformUndo();
                serializedObject.Update();
                Assert.AreEqual(
                    i,
                    host.set.Count,
                    $"Count after undo to {i} (undo {numberOfAdds - i} of {numberOfAdds}): [{FormatSetContents(host.set)}]."
                );
            }
        }

        [Test]
        [TestCase(1, TestName = "RemovalCount.One")]
        [TestCase(2, TestName = "RemovalCount.Two")]
        [TestCase(3, TestName = "RemovalCount.Three")]
        [TestCase(5, TestName = "RemovalCount.Five")]
        public void ConsecutiveRemovesCanBeUndoneIndividually(int removalCount)
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            for (int i = 0; i < removalCount + 2; i++)
            {
                host.set.Add(i * 100);
            }

            int initialCount = host.set.Count;
            string initialContents = FormatSetContents(host.set);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            for (int i = 0; i < removalCount; i++)
            {
                if (0 < i)
                {
                    Undo.IncrementCurrentGroup();
                }

                pagination.selectedIndex = 0;
                drawer.InvokeTryRemoveSelectedEntry(
                    ref setProperty,
                    propertyPath,
                    ref itemsProperty,
                    pagination
                );
                serializedObject.Update();
                Assert.AreEqual(
                    initialCount - (i + 1),
                    host.set.Count,
                    $"Count after removal {i + 1} of {removalCount}. Initial ({initialCount}): [{initialContents}], Current ({host.set.Count}): [{FormatSetContents(host.set)}]."
                );
            }

            for (int i = removalCount - 1; 0 <= i; i--)
            {
                Undo.PerformUndo();
                serializedObject.Update();
                Assert.AreEqual(
                    initialCount - i,
                    host.set.Count,
                    $"Count after undo {removalCount - i} of {removalCount}. Expected {initialCount - i}, got {host.set.Count}. Contents: [{FormatSetContents(host.set)}]."
                );
            }
        }

        [Test]
        [TestCase(1, 1, TestName = "Adds.One.Removes.One")]
        [TestCase(2, 1, TestName = "Adds.Two.Removes.One")]
        [TestCase(1, 2, TestName = "Adds.One.Removes.Two")]
        [TestCase(3, 2, TestName = "Adds.Three.Removes.Two")]
        public void MixedAddRemoveOperationsCanBeUndone(int addCount, int removeCount)
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            host.set.Add(100);
            host.set.Add(200);

            int initialCount = host.set.Count;
            string initialContents = FormatSetContents(host.set);
            int safeRemoveCount = Math.Min(removeCount, initialCount + addCount);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int expectedCountAfterAdds = initialCount + addCount;
            for (int i = 0; i < addCount; i++)
            {
                if (0 < i)
                {
                    Undo.IncrementCurrentGroup();
                }

                drawer.TryAddNewElement(
                    ref setProperty,
                    propertyPath,
                    ref itemsProperty,
                    pagination
                );
                serializedObject.Update();
            }
            string afterAdds = FormatSetContents(host.set);
            Assert.AreEqual(
                expectedCountAfterAdds,
                host.set.Count,
                $"Count after {addCount} adds. Initial ({initialCount}): [{initialContents}], After adds ({host.set.Count}): [{afterAdds}]."
            );

            Undo.IncrementCurrentGroup();

            int expectedCountAfterRemoves = expectedCountAfterAdds - safeRemoveCount;
            for (int i = 0; i < safeRemoveCount; i++)
            {
                if (0 < i)
                {
                    Undo.IncrementCurrentGroup();
                }

                pagination.selectedIndex = 0;
                drawer.InvokeTryRemoveSelectedEntry(
                    ref setProperty,
                    propertyPath,
                    ref itemsProperty,
                    pagination
                );
                serializedObject.Update();
            }
            string afterRemoves = FormatSetContents(host.set);
            Assert.AreEqual(
                expectedCountAfterRemoves,
                host.set.Count,
                $"Count after {safeRemoveCount} removes. After adds ({expectedCountAfterAdds}): [{afterAdds}], After removes ({host.set.Count}): [{afterRemoves}]."
            );

            for (int i = 0; i < safeRemoveCount; i++)
            {
                Undo.PerformUndo();
                serializedObject.Update();
            }
            string afterUndoRemoves = FormatSetContents(host.set);
            Assert.AreEqual(
                expectedCountAfterAdds,
                host.set.Count,
                $"Count after undoing {safeRemoveCount} removes. Expected {expectedCountAfterAdds}, got {host.set.Count}. After removes: [{afterRemoves}], After undo removes: [{afterUndoRemoves}]."
            );

            for (int i = 0; i < addCount; i++)
            {
                Undo.PerformUndo();
                serializedObject.Update();
            }
            string afterUndoAdds = FormatSetContents(host.set);
            Assert.AreEqual(
                initialCount,
                host.set.Count,
                $"Count after undoing {addCount} adds. Initial ({initialCount}): [{initialContents}], After undo all ({host.set.Count}): [{afterUndoAdds}]."
            );
        }

        [Test]
        [TestCase(2, TestName = "MoveCount.Two")]
        [TestCase(3, TestName = "MoveCount.Three")]
        [TestCase(4, TestName = "MoveCount.Four")]
        public void ConsecutiveMovesCanBeUndoneIndividually(int moveCount)
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            for (int i = 0; i < moveCount + 2; i++)
            {
                host.set.Add(i * 100);
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            List<string> stateHistory = new() { FormatSetContents(host.set) };

            for (int i = 0; i < moveCount; i++)
            {
                if (0 < i)
                {
                    Undo.IncrementCurrentGroup();
                }

                pagination.selectedIndex = 0;
                drawer.InvokeTryMoveSelectedEntry(
                    ref setProperty,
                    propertyPath,
                    ref itemsProperty,
                    pagination,
                    1
                );
                serializedObject.Update();
                itemsProperty = setProperty.FindPropertyRelative(
                    SerializableHashSetSerializedPropertyNames.Items
                );
                stateHistory.Add(FormatSetContents(host.set));
            }

            for (int i = moveCount - 1; 0 <= i; i--)
            {
                Undo.PerformUndo();
                serializedObject.Update();
                itemsProperty = setProperty.FindPropertyRelative(
                    SerializableHashSetSerializedPropertyNames.Items
                );
                string currentState = FormatSetContents(host.set);
                string expectedState = stateHistory[i];
                Assert.AreEqual(
                    expectedState,
                    currentState,
                    $"State after undo {moveCount - i} of {moveCount} should match state {i}. "
                        + $"History: [{string.Join(" -> ", stateHistory)}], Current: [{currentState}]."
                );
            }
        }

        [Test]
        public void UndoAfterClearThenAddRestoresCorrectly()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            host.set.Add(10);
            host.set.Add(20);
            host.set.Add(30);

            int initialCount = host.set.Count;
            string initialContents = FormatSetContents(host.set);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            drawer.InvokeTryClearSet(ref setProperty, propertyPath, ref itemsProperty);
            serializedObject.Update();
            string afterClear = FormatSetContents(host.set);
            Assert.AreEqual(
                0,
                host.set.Count,
                $"Set should be empty after clear. Initial ({initialCount}): [{initialContents}], After clear: [{afterClear}]."
            );

            Undo.IncrementCurrentGroup();

            drawer.TryAddNewElement(ref setProperty, propertyPath, ref itemsProperty, pagination);
            serializedObject.Update();
            string afterAdd = FormatSetContents(host.set);
            Assert.AreEqual(
                1,
                host.set.Count,
                $"Set should have 1 entry after add. After clear: [{afterClear}], After add: [{afterAdd}]."
            );

            Undo.PerformUndo();
            serializedObject.Update();
            string afterUndoAdd = FormatSetContents(host.set);
            Assert.AreEqual(
                0,
                host.set.Count,
                $"Undoing add should leave empty set. After add ({1}): [{afterAdd}], After undo ({host.set.Count}): [{afterUndoAdd}]."
            );

            Undo.PerformUndo();
            serializedObject.Update();
            string afterUndoClear = FormatSetContents(host.set);
            Assert.AreEqual(
                initialCount,
                host.set.Count,
                $"Undoing clear should restore initial state. Initial ({initialCount}): [{initialContents}], After undo clear ({host.set.Count}): [{afterUndoClear}]."
            );
        }

        [Test]
        public void RedoAfterMultipleUndosRestoresCorrectSequence()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int operationCount = 4;
            List<int> expectedCounts = new() { 0 };

            for (int i = 0; i < operationCount; i++)
            {
                if (0 < i)
                {
                    Undo.IncrementCurrentGroup();
                }

                drawer.TryAddNewElement(
                    ref setProperty,
                    propertyPath,
                    ref itemsProperty,
                    pagination
                );
                serializedObject.Update();
                expectedCounts.Add(host.set.Count);
            }

            Assert.AreEqual(
                operationCount,
                host.set.Count,
                $"Should have {operationCount} entries after adds. Contents: [{FormatSetContents(host.set)}]."
            );

            for (int i = 0; i < operationCount; i++)
            {
                Undo.PerformUndo();
                serializedObject.Update();
                int expectedCount = expectedCounts[operationCount - 1 - i];
                Assert.AreEqual(
                    expectedCount,
                    host.set.Count,
                    $"Undo {i + 1} of {operationCount} should restore count to {expectedCount}. "
                        + $"Got {host.set.Count}. Contents: [{FormatSetContents(host.set)}]."
                );
            }

            for (int i = 0; i < operationCount; i++)
            {
                Undo.PerformRedo();
                serializedObject.Update();
                int expectedCount = expectedCounts[i + 1];
                Assert.AreEqual(
                    expectedCount,
                    host.set.Count,
                    $"Redo {i + 1} of {operationCount} should restore count to {expectedCount}. "
                        + $"Got {host.set.Count}. Contents: [{FormatSetContents(host.set)}]."
                );
            }
        }

        [Test]
        public void RedoAfterUndoClearRestoresEmptyState()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            host.set.Add(10);
            host.set.Add(20);
            host.set.Add(30);

            int initialCount = host.set.Count;
            string initialContents = FormatSetContents(host.set);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            drawer.InvokeTryClearSet(ref setProperty, propertyPath, ref itemsProperty);
            serializedObject.Update();
            Assert.AreEqual(
                0,
                host.set.Count,
                $"Set should be empty after clear. Initial ({initialCount}): [{initialContents}]."
            );

            Undo.PerformUndo();
            serializedObject.Update();
            string afterUndo = FormatSetContents(host.set);
            Assert.AreEqual(
                initialCount,
                host.set.Count,
                $"Undo should restore original count. Initial ({initialCount}): [{initialContents}], After undo ({host.set.Count}): [{afterUndo}]."
            );

            Undo.PerformRedo();
            serializedObject.Update();
            string afterRedo = FormatSetContents(host.set);
            Assert.AreEqual(
                0,
                host.set.Count,
                $"Redo should restore empty state after clear. After undo ({initialCount}): [{afterUndo}], After redo ({host.set.Count}): [{afterRedo}]."
            );
        }

        [Test]
        public void RedoAfterUndoSortRestoresSortedOrder()
        {
            SortedSetHost host = CreateScriptableObject<SortedSetHost>();
            host.set.Add(30);
            host.set.Add(10);
            host.set.Add(20);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SortedSetHost.set)
            );
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            string originalOrder = FormatArrayContents(itemsProperty);

            bool sorted = drawer.InvokeTrySortElements(
                ref setProperty,
                propertyPath,
                itemsProperty
            );

            Assert.IsTrue(sorted, $"TrySortElements should succeed. Original: [{originalOrder}].");
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            string sortedOrder = FormatArrayContents(itemsProperty);

            Undo.PerformUndo();
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            string afterUndoOrder = FormatArrayContents(itemsProperty);
            Assert.AreEqual(
                originalOrder,
                afterUndoOrder,
                $"Undo should restore original order. Original: [{originalOrder}], Sorted: [{sortedOrder}], After undo: [{afterUndoOrder}]."
            );

            Undo.PerformRedo();
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            string afterRedoOrder = FormatArrayContents(itemsProperty);
            Assert.AreEqual(
                sortedOrder,
                afterRedoOrder,
                $"Redo should restore sorted order. Sorted: [{sortedOrder}], After undo: [{afterUndoOrder}], After redo: [{afterRedoOrder}]."
            );
        }

        [Test]
        [TestCase(3, TestName = "Cycles.Three")]
        [TestCase(5, TestName = "Cycles.Five")]
        public void RepeatedUndoRedoCyclesAreStable(int cycleCount)
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            host.set.Add(42);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int initialCount = host.set.Count;
            string initialContents = FormatSetContents(host.set);

            drawer.TryAddNewElement(ref setProperty, propertyPath, ref itemsProperty, pagination);
            serializedObject.Update();
            int afterAddCount = host.set.Count;
            string afterAddContents = FormatSetContents(host.set);
            Assert.AreEqual(
                initialCount + 1,
                afterAddCount,
                $"Set should have one more entry. Initial ({initialCount}): [{initialContents}], After add ({afterAddCount}): [{afterAddContents}]."
            );

            for (int cycle = 0; cycle < cycleCount; cycle++)
            {
                Undo.PerformUndo();
                serializedObject.Update();
                Assert.AreEqual(
                    initialCount,
                    host.set.Count,
                    $"Undo in cycle {cycle + 1} of {cycleCount} should restore initial count {initialCount}. Got {host.set.Count}. Contents: [{FormatSetContents(host.set)}]."
                );

                Undo.PerformRedo();
                serializedObject.Update();
                Assert.AreEqual(
                    afterAddCount,
                    host.set.Count,
                    $"Redo in cycle {cycle + 1} of {cycleCount} should restore count {afterAddCount}. Got {host.set.Count}. Contents: [{FormatSetContents(host.set)}]."
                );
            }
        }

        [Test]
        public void UndoAddToPrePopulatedSetRestoresExactContents()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            host.set.Add(10);
            host.set.Add(20);
            host.set.Add(30);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            string propertyPath = setProperty.propertyPath;

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            HashSet<int> originalElements = new(host.set);
            string originalContents = FormatSetContents(host.set);

            drawer.TryAddNewElement(ref setProperty, propertyPath, ref itemsProperty, pagination);
            serializedObject.Update();
            string afterAddContents = FormatSetContents(host.set);
            Assert.AreEqual(
                originalElements.Count + 1,
                host.set.Count,
                $"Set should grow by one. Original ({originalElements.Count}): [{originalContents}], After add ({host.set.Count}): [{afterAddContents}]."
            );

            Undo.PerformUndo();
            serializedObject.Update();
            string afterUndoContents = FormatSetContents(host.set);

            Assert.AreEqual(
                originalElements.Count,
                host.set.Count,
                $"Count should match original after undo. Original ({originalElements.Count}): [{originalContents}], After add: [{afterAddContents}], After undo ({host.set.Count}): [{afterUndoContents}]."
            );

            foreach (int element in originalElements)
            {
                Assert.IsTrue(
                    host.set.Contains(element),
                    $"Set should contain original element {element} after undo. Original: [{originalContents}], After undo: [{afterUndoContents}]."
                );
            }
        }
    }
}
