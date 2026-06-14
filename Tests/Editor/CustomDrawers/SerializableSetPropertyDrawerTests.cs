// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditorInternal;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using Object = UnityEngine.Object;
    using TestData = WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.TestData;

    // TODO: Consolidate
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SerializableSetPropertyDrawerTests : BatchedEditorTestBase
    {
        private StringSetHost _sharedHost;
        private SerializedObject _sharedSerializedObject;

        [SetUp]
        public override void BaseSetUp()
        {
            _sharedHost = CreateScriptableObject<StringSetHost>();
            _sharedSerializedObject = new SerializedObject(_sharedHost);
            GroupGUIWidthUtility.ResetForTests();
            SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();
            SerializableSetPropertyDrawer.ClearMainFoldoutAnimCacheForTests();
            ResetHostState();
            base.BaseSetUp();
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            if (_sharedSerializedObject != null)
            {
                _sharedSerializedObject.Dispose();
                _sharedSerializedObject = null;
            }

            _sharedHost.Destroy();
        }

        /// <summary>
        /// Resets the shared host state between tests to ensure test isolation.
        /// </summary>
        private void ResetHostState()
        {
            // Diagnostic: Verify SerializedObject is still valid
            Assert.IsTrue(
                _sharedSerializedObject != null,
                "SerializedObject was disposed or null - check OneTimeTearDown ordering"
            );
            Assert.IsTrue(
                _sharedSerializedObject.targetObject != null,
                "SerializedObject's target was destroyed - check disposal order"
            );

            _sharedHost.set.Clear();
            _sharedSerializedObject.Update();
            SerializedProperty setProperty = _sharedSerializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            if (setProperty != null)
            {
                setProperty.isExpanded = false;
                _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Serializable]
        private sealed class CloneableSample
        {
            public int number = 5;
            public string label = "alpha";
        }

        [Test]
        public void GetPropertyHeightClampsPageSize()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            for (int i = 0; i < 128; i++)
            {
                host.set.Add(i);
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            pagination.pageSize = 4096;

            drawer.GetPropertyHeight(setProperty, GUIContent.none);

            Assert.AreEqual(
                UnityHelpersSettings.GetSerializableSetPageSize(),
                pagination.pageSize,
                "Page size should track the configured Unity Helpers setting."
            );
        }

        [Test]
        public void GetPropertyHeightAutoExpandsComplexRowsOnFirstDraw()
        {
            ComplexSetHost host = CreateScriptableObject<ComplexSetHost>();
            host.set.Add(new ComplexSetElement());

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(ComplexSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.Greater(itemsProperty.arraySize, 0, "Set should contain test entries.");
            SerializedProperty elementProperty = itemsProperty.GetArrayElementAtIndex(0);
            elementProperty.isExpanded = false;

            SerializableSetPropertyDrawer drawer = new();
            drawer.GetPropertyHeight(setProperty, GUIContent.none);

            serializedObject.Update();
            SerializedProperty refreshedItems = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            SerializedProperty refreshedElement = refreshedItems.GetArrayElementAtIndex(0);
            Assert.IsTrue(
                refreshedElement.isExpanded,
                "GetPropertyHeight should expand complex set rows before the first draw so layout reserves enough space."
            );
        }

        [UnityTest]
        public IEnumerator SetRowComplexValueChildControlsHaveSpaceOnFirstDraw()
        {
            ComplexSetHost host = CreateScriptableObject<ComplexSetHost>();
            host.set.Add(new ComplexSetElement());

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(ComplexSetHost.set)
            );
            setProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.Greater(itemsProperty.arraySize, 0, "Set should contain test entries.");
            SerializedProperty elementProperty = itemsProperty.GetArrayElementAtIndex(0);
            elementProperty.isExpanded = false;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 360f, 520f);
            GUIContent label = new("Set");

            SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();
            bool heightSetExpandedBefore = setProperty.isExpanded;
            bool heightRowExpandedBefore = elementProperty.isExpanded;

            drawer.GetPropertyHeight(setProperty, label);

            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            elementProperty = itemsProperty.GetArrayElementAtIndex(0);
            bool heightSetExpandedAfter = setProperty.isExpanded;
            bool heightRowExpandedAfter = elementProperty.isExpanded;
            TestContext.WriteLine(
                $"Complex row GetPropertyHeight expansion states -> set: {heightSetExpandedBefore}->{heightSetExpandedAfter}, row: {heightRowExpandedBefore}->{heightRowExpandedAfter}"
            );

            bool drawSetExpandedBefore = setProperty.isExpanded;
            bool drawRowExpandedBefore = elementProperty.isExpanded;

            yield return TestIMGUIExecutor.Run(() =>
            {
                setProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, setProperty, label);
            });

            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            elementProperty = itemsProperty.GetArrayElementAtIndex(0);
            bool drawSetExpandedAfter = setProperty.isExpanded;
            bool drawRowExpandedAfter = elementProperty.isExpanded;
            TestContext.WriteLine(
                $"Complex row OnGUI expansion states -> set: {drawSetExpandedBefore}->{drawSetExpandedAfter}, row: {drawRowExpandedBefore}->{drawRowExpandedAfter}"
            );

            Assert.IsTrue(
                SerializableSetPropertyDrawer.HasLastRowContentRect,
                "First draw should capture row content rect for complex set values."
            );
            Assert.Greater(
                SerializableSetPropertyDrawer.LastRowContentRect.height,
                EditorGUIUtility.singleLineHeight * 1.5f,
                "Complex set elements should render at full height on the first draw."
            );
            Assert.Greater(
                SerializableSetPropertyDrawer.LastRowContentRect.width,
                180f,
                "Complex set elements should render at full width on the first draw."
            );
        }

        [Test]
        public void ManualEntryAddsElementToSet()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            pending.value = "ManualEntry";

            ISerializableSetInspector inspector = host.set;
            Assert.IsTrue(
                inspector != null,
                "Expected inspector implementation on SerializableHashSet."
            );

            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Manual entry should commit successfully.");
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.AreEqual(1, itemsProperty.arraySize);
            Assert.AreEqual("ManualEntry", itemsProperty.GetArrayElementAtIndex(0).stringValue);
            Assert.IsTrue(pending.errorMessage == null);
            Assert.IsFalse(pending.isExpanded);
        }

        [Test]
        public void TryCommitPendingEntryResetsPendingValueToDefaultForStrings()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            pending.value = "TestString";

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Commit should succeed.");
            Assert.AreEqual(
                string.Empty,
                pending.value,
                "Pending value should reset to empty string after successful Add."
            );
        }

        [Test]
        public void TryCommitPendingEntryResetsPendingValueToDefaultForInts()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(int),
                isSortedSet: false
            );
            pending.value = 42;

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Commit should succeed.");
            Assert.AreEqual(
                0,
                pending.value,
                "Pending value should reset to 0 after successful Add."
            );
        }

        [Test]
        public void TryCommitPendingEntryResetsPendingValueToDefaultForComplexTypes()
        {
            ComplexSetHost host = CreateScriptableObject<ComplexSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(ComplexSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(ComplexSetElement),
                isSortedSet: false
            );
            ComplexSetElement customElement = new() { primary = Color.red };
            pending.value = customElement;

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Commit should succeed.");
            Assert.IsTrue(
                pending.value != null,
                "Pending value should be a new default instance, not null."
            );
            Assert.AreNotSame(
                customElement,
                pending.value,
                "Pending value should be a different instance after reset."
            );
        }

        [Test]
        public void TryCommitPendingEntryAllowsMultipleConsecutiveAdds()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            ISerializableSetInspector inspector = host.set;

            pending.value = "First";
            bool firstCommit = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );
            Assert.IsTrue(firstCommit, "First commit should succeed.");
            Assert.AreEqual(
                string.Empty,
                pending.value,
                "Pending value should reset after first Add."
            );

            pending.value = "Second";
            bool secondCommit = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );
            Assert.IsTrue(secondCommit, "Second commit should succeed.");
            Assert.AreEqual(
                string.Empty,
                pending.value,
                "Pending value should reset after second Add."
            );

            pending.value = "Third";
            bool thirdCommit = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );
            Assert.IsTrue(thirdCommit, "Third commit should succeed.");
            Assert.AreEqual(
                string.Empty,
                pending.value,
                "Pending value should reset after third Add."
            );

            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.AreEqual(3, itemsProperty.arraySize, "Set should contain all three entries.");
        }

        [Test]
        public void TryCommitPendingEntryPreservesIsExpandedState()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            pending.value = "TestValue";
            pending.isExpanded = true;

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Commit should succeed.");
            Assert.IsTrue(
                pending.isExpanded,
                "isExpanded should be preserved after successful commit."
            );
        }

        [Test]
        public void TryCommitPendingEntryDoesNotResetOnDuplicateFailure()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            host.set.Add("Existing");
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            pending.value = "Existing";

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsFalse(committed, "Commit should fail for duplicate value.");
            Assert.AreEqual(
                "Existing",
                pending.value,
                "Pending value should NOT be reset when commit fails."
            );
            Assert.IsTrue(pending.errorMessage != null, "Error message should be set on failure.");
        }

        [Test]
        public void TryCommitPendingEntryDoesNotResetOnNullValueFailure()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(int),
                isSortedSet: false
            );
            pending.value = null;
            pending.elementType = typeof(int);

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsFalse(committed, "Commit should fail for null value on non-nullable type.");
            Assert.IsTrue(
                pending.value == null,
                "Pending value should remain null when commit fails due to null check."
            );
        }

        [Test]
        public void TryCommitPendingEntryClearsErrorMessageOnSuccess()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            pending.value = "ValidEntry";
            pending.errorMessage = "Previous error that should be cleared";

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Commit should succeed.");
            Assert.IsTrue(
                pending.errorMessage == null,
                "Error message should be cleared after successful commit."
            );
        }

        [Test]
        public void TryCommitPendingEntryMarksValueWrapperDirty()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            pending.value = "TestValue";
            pending.valueWrapperDirty = false;

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Commit should succeed.");
            Assert.IsTrue(
                pending.valueWrapperDirty,
                "valueWrapperDirty should be true after reset to force UI sync."
            );
        }

        [Test]
        public void TryCommitPendingEntryResetsPendingValueToDefaultForFloats()
        {
            FloatSetHost host = CreateScriptableObject<FloatSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(FloatSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(float),
                isSortedSet: false
            );
            pending.value = 3.14f;

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Commit should succeed.");
            Assert.AreEqual(
                0f,
                pending.value,
                "Pending value should reset to 0f after successful Add."
            );
        }

        [Test]
        public void TryCommitPendingEntryResetsPendingValueToDefaultForBools()
        {
            BoolSetHost host = CreateScriptableObject<BoolSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(BoolSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(bool),
                isSortedSet: false
            );
            pending.value = true;

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Commit should succeed.");
            Assert.AreEqual(
                false,
                pending.value,
                "Pending value should reset to false after successful Add."
            );
        }

        [Test]
        public void TryCommitPendingEntryResetsPendingValueForSortedSets()
        {
            SortedStringSetHost host = CreateScriptableObject<SortedStringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SortedStringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: true
            );
            pending.value = "SortedEntry";

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Commit should succeed for sorted set.");
            Assert.AreEqual(
                string.Empty,
                pending.value,
                "Pending value should reset to empty string for sorted sets too."
            );
        }

        [Test]
        public void TryCommitPendingEntryResetsPendingValueForSortedIntSets()
        {
            SortedSetHost host = CreateScriptableObject<SortedSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SortedSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(int),
                isSortedSet: true
            );
            pending.value = 999;

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Commit should succeed for sorted int set.");
            Assert.AreEqual(
                0,
                pending.value,
                "Pending value should reset to 0 for sorted int sets."
            );
        }

        [Test]
        public void TryCommitPendingEntryAllowsAddingPreviousValueAfterReset()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            ISerializableSetInspector inspector = host.set;

            pending.value = "TestValue";
            bool firstCommit = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );
            Assert.IsTrue(firstCommit, "First commit should succeed.");
            Assert.AreEqual(string.Empty, pending.value, "Value should reset after first add.");

            pending.value = "TestValue";
            bool duplicateCommit = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );
            Assert.IsFalse(duplicateCommit, "Adding the same value again should fail (duplicate).");
            Assert.AreEqual(
                "TestValue",
                pending.value,
                "Value should NOT reset on failed duplicate add."
            );
        }

        [Test]
        public void TryCommitPendingEntryHandlesEmptyStringCorrectly()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            ISerializableSetInspector inspector = host.set;

            pending.value = string.Empty;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Empty string should be a valid value to add.");
            Assert.AreEqual(
                string.Empty,
                pending.value,
                "Value should remain empty string (default) after add."
            );
        }

        [Test]
        public void TryCommitPendingEntryHandlesZeroCorrectly()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(int),
                isSortedSet: false
            );
            ISerializableSetInspector inspector = host.set;

            pending.value = 0;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Zero should be a valid value to add.");
            Assert.AreEqual(0, pending.value, "Value should remain 0 (default) after add.");
        }

        [Test]
        public void TryCommitPendingEntryRejectsSecondZeroAsDuplicate()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(int),
                isSortedSet: false
            );
            ISerializableSetInspector inspector = host.set;

            pending.value = 0;
            bool firstCommit = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );
            Assert.IsTrue(firstCommit, "First zero commit should succeed.");

            pending.value = 0;
            bool secondCommit = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );
            Assert.IsFalse(secondCommit, "Second zero commit should fail as duplicate.");
            Assert.IsTrue(
                pending.errorMessage != null,
                "Error message should be set for duplicate."
            );
        }

        [Test]
        public void TryCommitPendingEntryRejectsSecondEmptyStringAsDuplicate()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            ISerializableSetInspector inspector = host.set;

            pending.value = string.Empty;
            bool firstCommit = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );
            Assert.IsTrue(firstCommit, "First empty string commit should succeed.");

            pending.value = string.Empty;
            bool secondCommit = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );
            Assert.IsFalse(secondCommit, "Second empty string commit should fail as duplicate.");
            Assert.IsTrue(
                pending.errorMessage != null,
                "Error message should be set for duplicate."
            );
        }

        [Test]
        public void TryCommitPendingEntryHandlesNegativeNumbers()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(int),
                isSortedSet: false
            );
            ISerializableSetInspector inspector = host.set;

            pending.value = -42;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Negative number should be valid.");
            Assert.AreEqual(0, pending.value, "Value should reset to 0 after add.");

            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.AreEqual(1, itemsProperty.arraySize);
            Assert.AreEqual(-42, itemsProperty.GetArrayElementAtIndex(0).intValue);
        }

        [Test]
        public void TryCommitPendingEntryHandlesWhitespaceStrings()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            ISerializableSetInspector inspector = host.set;

            pending.value = "   ";
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Whitespace string should be valid.");
            Assert.AreEqual(string.Empty, pending.value, "Value should reset to empty after add.");

            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.AreEqual(1, itemsProperty.arraySize);
            Assert.AreEqual("   ", itemsProperty.GetArrayElementAtIndex(0).stringValue);
        }

        [Test]
        public void TryCommitPendingEntryHandlesUnicodeStrings()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            ISerializableSetInspector inspector = host.set;

            pending.value = "こんにちは世界🌍";
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Unicode string should be valid.");
            Assert.AreEqual(string.Empty, pending.value, "Value should reset to empty after add.");

            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.AreEqual(1, itemsProperty.arraySize);
            Assert.AreEqual(
                "こんにちは世界🌍",
                itemsProperty.GetArrayElementAtIndex(0).stringValue
            );
        }

        [Test]
        public void ManualEntryDefaultsSupportPrivateConstructors()
        {
            PrivateCtorSetHost host = CreateScriptableObject<PrivateCtorSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(PrivateCtorSetHost.set)
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(PrivateCtorElement),
                isSortedSet: false
            );

            Assert.IsTrue(pending.value != null);
            Assert.IsInstanceOf<PrivateCtorElement>(pending.value);
        }

        [Test]
        public void ManualEntryDefaultsRemainNullForUnityObjects()
        {
            ObjectSetHost host = CreateScriptableObject<ObjectSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(ObjectSetHost.set)
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(TestData),
                isSortedSet: false
            );

            Assert.IsTrue(
                pending.value == null,
                "UnityEngine.Object entries should start null so inspectors rely on object pickers."
            );
        }

        [UnityTest]
        public IEnumerator ManualEntryUsesObjectPickerForScriptableObjectValues()
        {
            SerializableSetPropertyDrawer.PendingEntry pending = new();
            Rect rect = new(0f, 0f, 200f, EditorGUIUtility.singleLineHeight);

            yield return TestIMGUIExecutor.Run(() =>
            {
                SerializableSetPropertyDrawer.DrawFieldForType(
                    rect,
                    new GUIContent("Value"),
                    null,
                    typeof(TestData),
                    pending
                );
            });

            Assert.IsTrue(
                pending.valueWrapper == null,
                "ScriptableObject values should rely on object pickers rather than PendingValueWrappers."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryPrimitiveValueDrawsInlineField()
        {
            SerializableSetPropertyDrawer.PendingEntry pending = new();
            Rect rect = new(0f, 0f, 200f, EditorGUIUtility.singleLineHeight);

            yield return TestIMGUIExecutor.Run(() =>
            {
                pending.value = 7;
                pending.value = SerializableSetPropertyDrawer.DrawFieldForType(
                    rect,
                    new GUIContent("Value"),
                    pending.value,
                    typeof(int),
                    pending
                );
            });

            Assert.IsInstanceOf<int>(pending.value, "Primitive values should remain ints.");
            Assert.IsTrue(
                pending.valueWrapper == null,
                "Primitive values should not allocate wrappers."
            );
            Assert.IsTrue(
                pending.valueWrapperSerialized == null,
                "Primitive values should not allocate serialized wrappers."
            );
            Assert.IsTrue(
                pending.valueWrapperProperty == null,
                "Primitive values should not allocate wrapper properties."
            );
        }

        [Test]
        public void CloneComplexValueReturnsSameReferenceForStrings()
        {
            string original = Guid.NewGuid().ToString();
            object clone = SerializableSetPropertyDrawer.CloneComplexValue(
                original,
                typeof(string)
            );

            Assert.AreSame(
                original,
                clone,
                "Strings should bypass deep cloning to preserve edits."
            );
        }

        [Test]
        public void CloneComplexValueDeepClonesSerializableReferenceTypes()
        {
            CloneableSample sample = new() { number = 42, label = "sample" };
            CloneableSample clone =
                SerializableSetPropertyDrawer.CloneComplexValue(sample, typeof(CloneableSample))
                as CloneableSample;

            Assert.IsTrue(clone != null, "Deep clone should produce an instance of the same type.");
            Assert.AreNotSame(sample, clone, "Clone should not reference the original object.");
            Assert.AreEqual(sample.number, clone.number);
            Assert.AreEqual(sample.label, clone.label);

            sample.number = 99;
            sample.label = "mutated";

            Assert.AreEqual(42, clone.number, "Deep clone should decouple numeric fields.");
            Assert.AreEqual("sample", clone.label, "Deep clone should decouple string fields.");
        }

        [Test]
        public void PendingEntryRetainsStringValueAcrossFrames()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                SerializableSetPropertyDrawer.IsSortedSet(setProperty)
            );

            pending.value = "Alpha";
            SerializableSetPropertyDrawer.PendingEntry fetched = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                SerializableSetPropertyDrawer.IsSortedSet(setProperty)
            );

            Assert.AreEqual("Alpha", fetched.value);
        }

        [Test]
        public void PendingEntriesAreIsolatedPerProperty()
        {
            DualStringSetHost host = CreateScriptableObject<DualStringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty firstSet = serializedObject.FindProperty(
                nameof(DualStringSetHost.firstSet)
            );
            SerializedProperty secondSet = serializedObject.FindProperty(
                nameof(DualStringSetHost.secondSet)
            );

            SerializableSetPropertyDrawer drawer = new();

            SerializableSetPropertyDrawer.PendingEntry firstPending =
                drawer.GetOrCreatePendingEntry(
                    firstSet,
                    firstSet.propertyPath,
                    typeof(string),
                    SerializableSetPropertyDrawer.IsSortedSet(firstSet)
                );
            SerializableSetPropertyDrawer.PendingEntry secondPending =
                drawer.GetOrCreatePendingEntry(
                    secondSet,
                    secondSet.propertyPath,
                    typeof(string),
                    SerializableSetPropertyDrawer.IsSortedSet(secondSet)
                );

            Assert.IsFalse(
                ReferenceEquals(firstPending, secondPending),
                "Each serialized property should have a dedicated pending entry."
            );

            firstPending.value = "alpha";
            secondPending.value = "beta";

            Assert.AreEqual("alpha", firstPending.value);
            Assert.AreEqual("beta", secondPending.value);
        }

        [Test]
        public void ManualEntryRejectsDuplicateValues()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            ISerializableSetInspector inspector = host.set;
            Assert.IsTrue(
                inspector != null,
                "Expected inspector implementation on SerializableHashSet."
            );

            Array snapshot = new string[1];
            snapshot.SetValue("Existing", 0);
            inspector.SetSerializedItemsSnapshot(snapshot, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            pending.value = "Existing";

            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsFalse(committed, "Duplicate entries should be rejected.");
            StringAssert.Contains("exists", pending.errorMessage);
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.AreEqual(1, itemsProperty.arraySize);
        }

        [Test]
        public void EvaluateDuplicateStateDetectsDuplicateEntries()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array duplicates = Array.CreateInstance(inspector.ElementType, 3);
            duplicates.SetValue(2, 0);
            duplicates.SetValue(2, 1);
            duplicates.SetValue(4, 2);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(state.hasDuplicates);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, state.duplicateIndices);
            StringAssert.Contains("Duplicate entry 2", state.summary);
        }

        [Test]
        public void SetSerializedItemsSnapshotPreservesDuplicatesInItemsArray()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;

            Array duplicates = Array.CreateInstance(inspector.ElementType, 3);
            duplicates.SetValue(7, 0);
            duplicates.SetValue(7, 1);
            duplicates.SetValue(8, 2);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);

            Assert.AreEqual(
                3,
                inspector.SerializedCount,
                "SerializedCount should preserve all entries including duplicates."
            );
            Assert.AreEqual(
                2,
                inspector.UniqueCount,
                "UniqueCount should only count unique entries."
            );

            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.AreEqual(
                3,
                itemsProperty.arraySize,
                "Items array should preserve duplicate entries after synchronization."
            );
        }

        [Test]
        [TestCase(typeof(HashSetHost), "set", 42, 42, 99)]
        [TestCase(typeof(SortedSetHost), "set", 15, 15, 25)]
        public void SetSerializedItemsSnapshotPreservesDuplicatesAcrossSetTypes(
            Type hostType,
            string setFieldName,
            object duplicateValue,
            object duplicateValue2,
            object uniqueValue
        )
        {
            ScriptableObject host = CreateScriptableObject(hostType);
            System.Reflection.FieldInfo setField = hostType.GetField(setFieldName);
            Assert.IsTrue(setField != null, $"Field '{setFieldName}' not found on host type.");

            object setInstance = setField.GetValue(host);
            ISerializableSetInspector inspector = setInstance as ISerializableSetInspector;
            Assert.IsTrue(
                inspector != null,
                "Set field should implement ISerializableSetInspector."
            );

            Array duplicates = Array.CreateInstance(inspector.ElementType, 3);
            duplicates.SetValue(Convert.ChangeType(duplicateValue, inspector.ElementType), 0);
            duplicates.SetValue(Convert.ChangeType(duplicateValue2, inspector.ElementType), 1);
            duplicates.SetValue(Convert.ChangeType(uniqueValue, inspector.ElementType), 2);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(setFieldName);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.AreEqual(
                3,
                itemsProperty.arraySize,
                $"Items array should preserve duplicates for {hostType.Name}."
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(
                state.hasDuplicates,
                $"Duplicate detection should work for {hostType.Name}."
            );
            CollectionAssert.AreEquivalent(
                new[] { 0, 1 },
                state.duplicateIndices,
                $"Duplicate indices should be correct for {hostType.Name}."
            );
        }

        [Test]
        public void SetSerializedItemsSnapshotPreservesNullEntriesInItemsArray()
        {
            ObjectSetHost host = CreateScriptableObject<ObjectSetHost>();
            ISerializableSetInspector inspector = host.set;

            TestData validObject = CreateScriptableObject<TestData>();
            Array values = Array.CreateInstance(inspector.ElementType, 3);
            values.SetValue(null, 0);
            values.SetValue(validObject, 1);
            values.SetValue(null, 2);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(ObjectSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.AreEqual(
                3,
                itemsProperty.arraySize,
                "Items array should preserve null entries after synchronization."
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.NullEntryState nullState = drawer.EvaluateNullEntryState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(
                nullState.hasNullEntries,
                "Null entry detection should find the null entries."
            );
            Assert.IsTrue(
                nullState.nullIndices.Contains(0) && nullState.nullIndices.Contains(2),
                "Both null indices (0 and 2) should be detected."
            );
        }

        [Test]
        public void DuplicateStateDoesNotReportDistinctValuesAsDuplicates()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array values = Array.CreateInstance(inspector.ElementType, 2);
            values.SetValue(3, 0);
            values.SetValue(1, 1);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();

            SerializableSetPropertyDrawer.DuplicateState firstPass = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );
            Assert.IsFalse(firstPass.hasDuplicates);

            SerializableSetPropertyDrawer.DuplicateState secondPass = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );
            Assert.IsFalse(secondPass.hasDuplicates);
        }

        [Test]
        public void DuplicateStateReportsDuplicatesAfterCacheReuse()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array values = Array.CreateInstance(inspector.ElementType, 2);
            values.SetValue(5, 0);
            values.SetValue(5, 1);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(state.hasDuplicates);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, state.duplicateIndices);
            StringAssert.Contains("Duplicate entry 5", state.summary);
        }

        [Test]
        public void NullEntriesProduceInspectorWarnings()
        {
            ObjectSetHost host = CreateScriptableObject<ObjectSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array values = Array.CreateInstance(inspector.ElementType, 2);
            values.SetValue(null, 0);
            values.SetValue(CreateScriptableObject<TestData>(), 1);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(ObjectSetHost.set)
            );
            setProperty.isExpanded = true;
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.NullEntryState state = drawer.EvaluateNullEntryState(
                setProperty,
                itemsProperty
            );

            Assert.IsTrue(state.hasNullEntries);
            CollectionAssert.AreEquivalent(new[] { 0 }, state.nullIndices);
            Assert.IsTrue(state.tooltips.ContainsKey(0));
            StringAssert.Contains("Null entry", state.summary);
        }

        [Test]
        public void DuplicateStateHandlesDistinctStrings()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array values = Array.CreateInstance(inspector.ElementType, 2);
            values.SetValue("999", 0);
            values.SetValue("ddd", 1);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsFalse(state.hasDuplicates);
            CollectionAssert.IsEmpty(state.duplicateIndices);
            Assert.IsTrue(string.IsNullOrEmpty(state.summary));
        }

        [Test]
        public void SortedSetManualReorderShowsSortButton()
        {
            SortedSetHost host = CreateScriptableObject<SortedSetHost>();
            host.set.Add(1);
            host.set.Add(2);
            host.set.Add(3);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SortedSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            itemsProperty.MoveArrayElement(0, 2);
            serializedObject.ApplyModifiedProperties();

            bool beforeDeserialize = SerializableSetPropertyDrawer.ShouldShowSortButton(
                SerializableSetPropertyDrawer.IsSortedSet(setProperty),
                typeof(int),
                itemsProperty
            );
            Assert.IsTrue(
                beforeDeserialize,
                $"Sort button should be visible before the sorted set rehydrates. Items: {DumpIntArray(itemsProperty)}"
            );

            host.set.OnAfterDeserialize();
            host.set.OnBeforeSerialize();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(SortedSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            bool showSortAfterDeserialize = SerializableSetPropertyDrawer.ShouldShowSortButton(
                SerializableSetPropertyDrawer.IsSortedSet(setProperty),
                typeof(int),
                itemsProperty
            );

            TestContext.WriteLine(
                $"After OnAfterDeserialize + OnBeforeSerialize: Items={DumpIntArray(itemsProperty)}, "
                    + $"PreserveSerializedEntries={host.set.PreserveSerializedEntries}, "
                    + $"ShouldShowSortButton={showSortAfterDeserialize}"
            );

            Assert.IsTrue(
                showSortAfterDeserialize,
                $"Sorted sets preserve user-specified order; sort button should remain visible until user clicks it. Items: {DumpIntArray(itemsProperty)}"
            );
            Assert.IsTrue(
                host.set.PreserveSerializedEntries,
                "PreserveSerializedEntries should be true to maintain user-specified inspector order."
            );
        }

        [Test]
        public void SetSortButtonVisibilityReflectsOrdering()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array values = Array.CreateInstance(inspector.ElementType, 2);
            values.SetValue(5, 0);
            values.SetValue(1, 1);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            bool showBefore = SerializableSetPropertyDrawer.ShouldShowSortButton(
                SerializableSetPropertyDrawer.IsSortedSet(setProperty),
                inspector.ElementType,
                itemsProperty
            );
            Assert.IsTrue(showBefore);

            values.SetValue(1, 0);
            values.SetValue(5, 1);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            bool showAfter = SerializableSetPropertyDrawer.ShouldShowSortButton(
                SerializableSetPropertyDrawer.IsSortedSet(setProperty),
                inspector.ElementType,
                itemsProperty
            );
            Assert.IsFalse(showAfter);
        }

        [Test]
        public void PageEntriesNeedSortingOnlyFlagsVisiblePage()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array values = new int[4];
            values.SetValue(1, 0);
            values.SetValue(2, 1);
            values.SetValue(4, 2);
            values.SetValue(3, 3);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            pagination.pageSize = 2;

            string listKey = drawer.GetListKey(setProperty);
            SerializableSetPropertyDrawer.ListPageCache cache = drawer.EnsurePageCache(
                listKey,
                itemsProperty,
                pagination
            );

            bool firstPageNeedsSorting = SerializableSetPropertyDrawer.PageEntriesNeedSorting(
                cache,
                itemsProperty,
                allowSort: true
            );
            Assert.IsFalse(
                firstPageNeedsSorting,
                $"First page should already be sorted. Indexes: {DumpPageEntries(cache)} Values: {DumpIntArray(itemsProperty)}"
            );

            pagination.page = 1;
            cache = drawer.EnsurePageCache(listKey, itemsProperty, pagination);
            bool secondPageNeedsSorting = SerializableSetPropertyDrawer.PageEntriesNeedSorting(
                cache,
                itemsProperty,
                allowSort: true
            );
            Assert.IsTrue(
                secondPageNeedsSorting,
                $"Second page should require sorting. Indexes: {DumpPageEntries(cache)} Values: {DumpIntArray(itemsProperty)}"
            );
        }

        [Test]
        public void ReorderableListReordersHashSetEntries()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array values = Array.CreateInstance(inspector.ElementType, 3);
            values.SetValue(1, 0);
            values.SetValue(2, 1);
            values.SetValue(3, 2);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));

            SerializableSetPropertyDrawer drawer = new();
            ReorderableList list = drawer.GetOrCreateList(setProperty);

            Assert.IsTrue(list.onReorderCallbackWithDetails != null, "Expected reorder callback.");
            SimulateReorderableListMove(list, 0, 2);
            list.onReorderCallbackWithDetails.Invoke(list, 0, 2);

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.AreEqual(3, itemsProperty.arraySize);
            Assert.AreEqual(2, itemsProperty.GetArrayElementAtIndex(0).intValue);
            Assert.AreEqual(3, itemsProperty.GetArrayElementAtIndex(1).intValue);
            Assert.AreEqual(1, itemsProperty.GetArrayElementAtIndex(2).intValue);
        }

        [Test]
        public void ReorderableListReordersSortedSetEntries()
        {
            SortedSetHost host = CreateScriptableObject<SortedSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array values = Array.CreateInstance(inspector.ElementType, 3);
            values.SetValue(10, 0);
            values.SetValue(20, 1);
            values.SetValue(30, 2);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SortedSetHost.set)
            );

            SerializableSetPropertyDrawer drawer = new();
            ReorderableList list = drawer.GetOrCreateList(setProperty);

            Assert.IsTrue(list.onReorderCallbackWithDetails != null, "Expected reorder callback.");
            SimulateReorderableListMove(list, 2, 0);
            list.onReorderCallbackWithDetails.Invoke(list, 2, 0);

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.AreEqual(3, itemsProperty.arraySize);
            Assert.AreEqual(30, itemsProperty.GetArrayElementAtIndex(0).intValue);
            Assert.AreEqual(10, itemsProperty.GetArrayElementAtIndex(1).intValue);
            Assert.AreEqual(20, itemsProperty.GetArrayElementAtIndex(2).intValue);
        }

        private static void SimulateReorderableListMove(
            ReorderableList list,
            int oldIndex,
            int newIndex
        )
        {
            if (list?.list is not { } backing || backing.Count == 0)
            {
                return;
            }

            if (oldIndex < 0 || oldIndex >= backing.Count)
            {
                return;
            }

            object element = backing[oldIndex];
            backing.RemoveAt(oldIndex);

            int clampedIndex = Mathf.Clamp(newIndex, 0, backing.Count);
            if (clampedIndex >= backing.Count)
            {
                backing.Add(element);
            }
            else
            {
                backing.Insert(clampedIndex, element);
            }
        }

        [Test]
        public void EditingStringSetEntryAffectsOnlyTarget()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array values = Array.CreateInstance(inspector.ElementType, 3);
            values.SetValue("first", 0);
            values.SetValue("second", 1);
            values.SetValue("third", 2);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            itemsProperty.GetArrayElementAtIndex(2).stringValue = "updated";
            Assert.IsTrue(serializedObject.ApplyModifiedProperties());

            SerializableSetPropertyDrawer.SyncRuntimeSet(setProperty);
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(StringSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            string[] snapshot = new string[itemsProperty.arraySize];
            for (int index = 0; index < snapshot.Length; index++)
            {
                snapshot[index] = itemsProperty.GetArrayElementAtIndex(index).stringValue;
            }

            CollectionAssert.AreEqual(new[] { "first", "second", "updated" }, snapshot);
        }

        [Test]
        public void SortedSetEditingPreservesSerializedEntry()
        {
            SortedStringSetHost host = CreateScriptableObject<SortedStringSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array values = Array.CreateInstance(inspector.ElementType, 1);
            values.SetValue(string.Empty, 0);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SortedStringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            itemsProperty.GetArrayElementAtIndex(0).stringValue = "delta";
            Assert.IsTrue(serializedObject.ApplyModifiedProperties());

            SerializableSetPropertyDrawer.SyncRuntimeSet(setProperty);

            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(SortedStringSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.AreEqual(1, itemsProperty.arraySize, "Entry should remain serialized.");
            Assert.AreEqual("delta", itemsProperty.GetArrayElementAtIndex(0).stringValue);
        }

        [Test]
        public void SortedSetStringEditingRetainsEntryWhenAddedThroughDrawer()
        {
            SortedStringSetHost host = CreateScriptableObject<SortedStringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializableSetPropertyDrawer drawer = new();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SortedStringSetHost.set)
            );
            string propertyPath = setProperty.propertyPath;
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.IsTrue(
                drawer.TryAddNewElement(
                    ref setProperty,
                    propertyPath,
                    ref itemsProperty,
                    pagination
                ),
                "Drawer failed to add string entry."
            );

            serializedObject.Update();
            setProperty = serializedObject.FindProperty(propertyPath);
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.AreEqual(1, itemsProperty.arraySize);

            itemsProperty.GetArrayElementAtIndex(0).stringValue = "delta";
            Assert.IsTrue(serializedObject.ApplyModifiedProperties());

            SerializableSetPropertyDrawer.SyncRuntimeSet(setProperty);
            serializedObject.Update();

            setProperty = serializedObject.FindProperty(propertyPath);
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.AreEqual(1, itemsProperty.arraySize);
            Assert.AreEqual("delta", itemsProperty.GetArrayElementAtIndex(0).stringValue);
        }

        [Test]
        public void SortedSetEditingMaintainsElementOrder()
        {
            SortedStringSetHost host = CreateScriptableObject<SortedStringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializableSetPropertyDrawer drawer = new();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SortedStringSetHost.set)
            );
            string propertyPath = setProperty.propertyPath;
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.IsTrue(
                drawer.TryAddNewElement(
                    ref setProperty,
                    propertyPath,
                    ref itemsProperty,
                    pagination
                )
            );
            Assert.IsTrue(
                drawer.TryAddNewElement(
                    ref setProperty,
                    propertyPath,
                    ref itemsProperty,
                    pagination
                )
            );

            serializedObject.Update();
            setProperty = serializedObject.FindProperty(propertyPath);
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            string[] initialOrder = ReadStringValues(itemsProperty);
            Assert.AreEqual(2, initialOrder.Length);
            Assert.That(initialOrder[0], Is.EqualTo(string.Empty));

            itemsProperty.GetArrayElementAtIndex(0).stringValue = "delta";
            itemsProperty.GetArrayElementAtIndex(1).stringValue = "alpha";
            Assert.IsTrue(serializedObject.ApplyModifiedProperties());

            SerializableSetPropertyDrawer.SyncRuntimeSet(setProperty);
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            CollectionAssert.AreEqual(new[] { "delta", "alpha" }, ReadStringValues(itemsProperty));
        }

        [Test]
        public void SortedSetSortButtonReordersEntries()
        {
            SortedStringSetHost host = CreateScriptableObject<SortedStringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializableSetPropertyDrawer drawer = new();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SortedStringSetHost.set)
            );
            string propertyPath = setProperty.propertyPath;
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.IsTrue(
                drawer.TryAddNewElement(
                    ref setProperty,
                    propertyPath,
                    ref itemsProperty,
                    pagination
                )
            );
            Assert.IsTrue(
                drawer.TryAddNewElement(
                    ref setProperty,
                    propertyPath,
                    ref itemsProperty,
                    pagination
                )
            );

            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            itemsProperty.GetArrayElementAtIndex(0).stringValue = "zeta";
            itemsProperty.GetArrayElementAtIndex(1).stringValue = "beta";
            Assert.IsTrue(serializedObject.ApplyModifiedProperties());

            SerializableSetPropertyDrawer.SyncRuntimeSet(setProperty);
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            CollectionAssert.AreEqual(new[] { "zeta", "beta" }, ReadStringValues(itemsProperty));

            drawer.SortElements(setProperty, itemsProperty);
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            CollectionAssert.AreEqual(new[] { "beta", "zeta" }, ReadStringValues(itemsProperty));
        }

        [Test]
        public void IsSortedSetRecognizesDerivedTypes()
        {
            DerivedSortedSetHost host = CreateScriptableObject<DerivedSortedSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(DerivedSortedSetHost.set)
            );

            bool result = SerializableSetPropertyDrawer.IsSortedSet(setProperty);
            Assert.IsTrue(
                result,
                "Derived SerializableSortedSet types should be detected for sorting."
            );
        }

        [Test]
        public void HashSetStringEditingRetainsEntryWhenAddedThroughDrawer()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializableSetPropertyDrawer drawer = new();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            string propertyPath = setProperty.propertyPath;
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.IsTrue(
                drawer.TryAddNewElement(
                    ref setProperty,
                    propertyPath,
                    ref itemsProperty,
                    pagination
                ),
                "Drawer failed to add string entry."
            );

            serializedObject.Update();
            setProperty = serializedObject.FindProperty(propertyPath);
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.AreEqual(1, itemsProperty.arraySize);

            itemsProperty.GetArrayElementAtIndex(0).stringValue = "gamma";
            Assert.IsTrue(serializedObject.ApplyModifiedProperties());

            SerializableSetPropertyDrawer.SyncRuntimeSet(setProperty);
            serializedObject.Update();

            setProperty = serializedObject.FindProperty(propertyPath);
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.AreEqual(1, itemsProperty.arraySize);
            Assert.AreEqual("gamma", itemsProperty.GetArrayElementAtIndex(0).stringValue);
        }

        [Test]
        public void HashSetEditingMaintainsElementOrder()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializableSetPropertyDrawer drawer = new();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            string propertyPath = setProperty.propertyPath;
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.IsTrue(
                drawer.TryAddNewElement(
                    ref setProperty,
                    propertyPath,
                    ref itemsProperty,
                    pagination
                )
            );
            Assert.IsTrue(
                drawer.TryAddNewElement(
                    ref setProperty,
                    propertyPath,
                    ref itemsProperty,
                    pagination
                )
            );

            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            string[] initialOrder = ReadStringValues(itemsProperty);
            Assert.AreEqual(2, initialOrder.Length);
            Assert.That(initialOrder[0], Is.EqualTo(string.Empty));

            itemsProperty.GetArrayElementAtIndex(0).stringValue = "delta";
            itemsProperty.GetArrayElementAtIndex(1).stringValue = "alpha";
            Assert.IsTrue(serializedObject.ApplyModifiedProperties());

            SerializableSetPropertyDrawer.SyncRuntimeSet(setProperty);
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            CollectionAssert.AreEqual(new[] { "delta", "alpha" }, ReadStringValues(itemsProperty));
        }

        [Test]
        public void ExpandRowRectVerticallyExtendsSelectionBounds()
        {
            Rect baseRect = new(5f, 10f, 25f, 16f);
            Rect expanded = SerializableSetPropertyDrawer.ExpandRowRectVertically(baseRect);

            Assert.That(expanded.yMin, Is.LessThan(baseRect.yMin));
            Assert.That(expanded.yMax, Is.GreaterThan(baseRect.yMax));
            Assert.That(expanded.width, Is.EqualTo(baseRect.width));
        }

        [Test]
        public void TryAddNewElementAllowsMultipleNullPlaceholders()
        {
            ObjectSetHost host = CreateScriptableObject<ObjectSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(ObjectSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);

            Assert.IsTrue(
                drawer.TryAddNewElement(
                    ref setProperty,
                    setProperty.propertyPath,
                    ref itemsProperty,
                    pagination
                )
            );

            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(ObjectSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.IsTrue(
                drawer.TryAddNewElement(
                    ref setProperty,
                    setProperty.propertyPath,
                    ref itemsProperty,
                    pagination
                )
            );

            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(ObjectSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.IsTrue(itemsProperty != null);
            Assert.AreEqual(2, itemsProperty.arraySize);
            AssertPlaceholderIsNull(itemsProperty.GetArrayElementAtIndex(0));
            AssertPlaceholderIsNull(itemsProperty.GetArrayElementAtIndex(1));
        }

        [Test]
        public void TryAddNewElementAppendsDefaultValue()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            bool added = drawer.TryAddNewElement(
                ref setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination
            );

            Assert.IsTrue(added, "Drawer failed to append a new element.");

            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.IsTrue(itemsProperty != null);
            Assert.AreEqual(1, itemsProperty.arraySize);
            Assert.AreEqual(1, host.set.Count);
        }

        [Test]
        public void TryAddNewElementPreservesSerializedDuplicates()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array duplicates = Array.CreateInstance(inspector.ElementType, 3);
            duplicates.SetValue(1, 0);
            duplicates.SetValue(1, 1);
            duplicates.SetValue(2, 2);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.IsTrue(itemsProperty != null);
            Assert.AreEqual(3, itemsProperty.arraySize);

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            bool added = drawer.TryAddNewElement(
                ref setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination
            );

            Assert.IsTrue(
                added,
                "Drawer failed to append a new element when duplicates were present."
            );

            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.AreEqual(4, itemsProperty.arraySize);
            Assert.AreEqual(1, itemsProperty.GetArrayElementAtIndex(0).intValue);
            Assert.AreEqual(1, itemsProperty.GetArrayElementAtIndex(1).intValue);
            Assert.AreEqual(0, itemsProperty.GetArrayElementAtIndex(3).intValue);
            Assert.AreEqual(3, host.set.Count);
            SerializableSetPropertyDrawer.DuplicateState duplicateState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, duplicateState.duplicateIndices);
        }

        [Test]
        public void TryAddNewElementPreservesSerializedDuplicatesForSortedSet()
        {
            SortedSetHost host = CreateScriptableObject<SortedSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array duplicates = Array.CreateInstance(inspector.ElementType, 3);
            duplicates.SetValue(3, 0);
            duplicates.SetValue(3, 1);
            duplicates.SetValue(5, 2);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SortedSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.IsTrue(itemsProperty != null);
            Assert.AreEqual(3, itemsProperty.arraySize);

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            bool added = drawer.TryAddNewElement(
                ref setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination
            );

            Assert.IsTrue(
                added,
                "Drawer failed to append a new element to the sorted set when duplicates were present."
            );

            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(SortedSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.AreEqual(4, itemsProperty.arraySize);
            Assert.AreEqual(3, itemsProperty.GetArrayElementAtIndex(0).intValue);
            Assert.AreEqual(3, itemsProperty.GetArrayElementAtIndex(1).intValue);
            Assert.AreEqual(0, itemsProperty.GetArrayElementAtIndex(3).intValue);
            Assert.AreEqual(3, host.set.Count);
            SerializableSetPropertyDrawer.DuplicateState duplicateState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, duplicateState.duplicateIndices);
        }

        [Test]
        public void SortElementsOrdersSerializedIntegers()
        {
            SortedSetHost host = CreateScriptableObject<SortedSetHost>();
            host.set.Add(5);
            host.set.Add(1);
            host.set.Add(3);
            host.set.OnBeforeSerialize();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SortedSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            itemsProperty.GetArrayElementAtIndex(0).intValue = 5;
            itemsProperty.GetArrayElementAtIndex(1).intValue = 1;
            itemsProperty.GetArrayElementAtIndex(2).intValue = 3;
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();

            SerializableSetPropertyDrawer drawer = new();
            drawer.SortElements(setProperty, itemsProperty);

            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(SortedSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int[] serialized = Enumerable
                .Range(0, itemsProperty.arraySize)
                .Select(i => itemsProperty.GetArrayElementAtIndex(i).intValue)
                .ToArray();

            CollectionAssert.AreEqual(new[] { 1, 3, 5 }, serialized);
            CollectionAssert.AreEqual(new[] { 1, 3, 5 }, host.set.ToArray());
        }

        private static string[] ReadStringValues(SerializedProperty itemsProperty)
        {
            if (itemsProperty == null || !itemsProperty.isArray)
            {
                return Array.Empty<string>();
            }

            string[] values = new string[itemsProperty.arraySize];
            for (int index = 0; index < itemsProperty.arraySize; index++)
            {
                values[index] = itemsProperty.GetArrayElementAtIndex(index).stringValue;
            }

            return values;
        }

        private static void AssertPlaceholderIsNull(SerializedProperty element)
        {
            switch (element.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    Assert.IsTrue(
                        element.objectReferenceValue == null,
                        "Placeholder object reference should remain null."
                    );
                    break;
                default:
                    Assert.Fail($"Unsupported placeholder property type {element.propertyType}.");
                    break;
            }
        }

        [Test]
        public void RemoveEntryShrinksSerializedArray()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            host.set.Add(10);
            host.set.Add(20);
            host.set.Add(30);
            host.set.OnBeforeSerialize();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            int removedValue = itemsProperty.GetArrayElementAtIndex(1).intValue;
            SerializableSetPropertyDrawer.RemoveEntry(itemsProperty, 1);
            drawer.RemoveValueFromSet(setProperty, setProperty.propertyPath, removedValue);
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();

            setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.AreEqual(2, itemsProperty.arraySize);
            int[] remaining = Enumerable
                .Range(0, itemsProperty.arraySize)
                .Select(i => itemsProperty.GetArrayElementAtIndex(i).intValue)
                .ToArray();
            CollectionAssert.AreEquivalent(new[] { 10, 30 }, remaining);
            Assert.AreEqual(2, host.set.Count);
        }

        [UnityTest]
        public IEnumerator HonorsGroupPaddingWithinGroups()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            host.set.Add(10);
            host.set.Add(20);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            setProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 360f, 400f);
            GUIContent label = new("Set");
            const int IndentDepth = 1;

            yield return TestIMGUIExecutor.Run(() =>
            {
                setProperty.serializedObject.UpdateIfRequiredOrScript();
                setProperty.isExpanded = true;
                int previousIndent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = IndentDepth;
                try
                {
                    drawer.OnGUI(controlRect, setProperty, label);
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndent;
                }
            });

            Assert.IsTrue(
                drawer.HasItemsContainerRect,
                "Baseline draw should capture the rendered container."
            );

            int snapshotIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = IndentDepth;
            Rect expectedBaselineRect = EditorGUI.IndentedRect(controlRect);
            EditorGUI.indentLevel = snapshotIndent;

            // When outside a WGroup (scopeDepth == 0), UnityListAlignmentOffset is applied.
            // The offset shifts xMin left by 1.25f, but it's clamped to not go below 0.
            const float UnityListAlignmentOffset = -1.25f;
            float expectedXMin = Mathf.Max(
                0f,
                expectedBaselineRect.xMin + UnityListAlignmentOffset
            );

            Assert.That(
                drawer.LastResolvedPosition.xMin,
                Is.EqualTo(expectedXMin).Within(0.0001f),
                $"Indentation should influence the resolved content rectangle. "
                    + $"IndentedRect.xMin={expectedBaselineRect.xMin}, "
                    + $"AlignmentOffset={UnityListAlignmentOffset}, "
                    + $"ExpectedXMin={expectedXMin}, "
                    + $"ActualXMin={drawer.LastResolvedPosition.xMin}"
            );

            const float LeftPadding = 16f;
            const float RightPadding = 8f;
            const float HorizontalPadding = LeftPadding + RightPadding;

            yield return TestIMGUIExecutor.Run(() =>
            {
                setProperty.serializedObject.UpdateIfRequiredOrScript();
                setProperty.isExpanded = true;
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        HorizontalPadding,
                        LeftPadding,
                        RightPadding
                    )
                )
                {
                    int previousIndent = EditorGUI.indentLevel;
                    EditorGUI.indentLevel = IndentDepth;
                    try
                    {
                        drawer.OnGUI(controlRect, setProperty, label);
                    }
                    finally
                    {
                        EditorGUI.indentLevel = previousIndent;
                    }
                }
            });

            Rect paddedRect = drawer.LastResolvedPosition;
            Assert.That(
                paddedRect.xMin,
                Is.EqualTo(expectedBaselineRect.xMin + LeftPadding).Within(0.0001f),
                "Group padding should offset the rendered content."
            );
            Assert.That(
                paddedRect.width,
                Is.EqualTo(Mathf.Max(0f, expectedBaselineRect.width - HorizontalPadding))
                    .Within(0.0001f),
                "Group padding should reduce usable width."
            );
            Assert.IsTrue(drawer.HasItemsContainerRect, "Container rect should be recorded.");
            Assert.That(
                drawer.LastItemsContainerRect.xMin,
                Is.EqualTo(paddedRect.xMin).Within(0.0001f),
                "Container rect should align with the padded content."
            );
        }

        [UnityTest]
        public IEnumerator ManualEntryHeaderHonorsGroupPadding()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            setProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            pending.isExpanded = true;

            Rect controlRect = new(0f, 0f, 360f, 420f);
            GUIContent label = new("Set");

            SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();
            bool baselineSetExpandedBeforeDraw = setProperty.isExpanded;
            bool baselinePendingExpandedBeforeDraw = pending.isExpanded;

            int baselineIndentBefore = 0;
            int baselineIndentAfter = 0;
            Rect baselineResolvedPosition = default;

            yield return TestIMGUIExecutor.Run(() =>
            {
                setProperty.serializedObject.UpdateIfRequiredOrScript();
                pending.isExpanded = true;
                baselineIndentBefore = EditorGUI.indentLevel;
                int previousIndent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                try
                {
                    drawer.OnGUI(controlRect, setProperty, label);
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndent;
                }
                baselineIndentAfter = EditorGUI.indentLevel;
                baselineResolvedPosition = drawer.LastResolvedPosition;
            });

            serializedObject.Update();
            bool baselineSetExpandedAfterDraw = setProperty.isExpanded;
            TestContext.WriteLine(
                $"ManualEntryHeader baseline foldout states -> set: {baselineSetExpandedBeforeDraw}->{baselineSetExpandedAfterDraw}, pending: {baselinePendingExpandedBeforeDraw}->{pending.isExpanded}"
            );
            TestContext.WriteLine(
                $"ManualEntryHeader baseline context -> indentLevel: {baselineIndentBefore}->{baselineIndentAfter}, "
                    + $"resolvedPosition: ({baselineResolvedPosition.x:F2}, {baselineResolvedPosition.y:F2}, {baselineResolvedPosition.width:F2}, {baselineResolvedPosition.height:F2})"
            );

            Assert.IsTrue(
                SerializableSetPropertyDrawer.HasLastManualEntryHeaderRect,
                "Baseline draw should capture the manual entry header layout."
            );

            Rect baselineHeader = SerializableSetPropertyDrawer.LastManualEntryHeaderRect;

            const float LeftPadding = 20f;
            const float RightPadding = 12f;
            float horizontalPadding = LeftPadding + RightPadding;

            // Reset padding state after baseline draw, before grouped draw (consistent with dictionary tests)
            GroupGUIWidthUtility.ResetForTests();

            bool groupedSetExpandedBeforeDraw = setProperty.isExpanded;
            bool groupedPendingExpandedBeforeDraw = pending.isExpanded;

            int groupedIndentBefore = 0;
            int groupedIndentAfter = 0;
            Rect groupedResolvedPosition = default;
            float groupedCurrentLeftPadding = 0f;
            float groupedCurrentRightPadding = 0f;

            yield return TestIMGUIExecutor.Run(() =>
            {
                setProperty.serializedObject.UpdateIfRequiredOrScript();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        LeftPadding,
                        RightPadding
                    )
                )
                {
                    groupedCurrentLeftPadding = GroupGUIWidthUtility.CurrentLeftPadding;
                    groupedCurrentRightPadding = GroupGUIWidthUtility.CurrentRightPadding;
                    SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();
                    pending.isExpanded = true;
                    groupedIndentBefore = EditorGUI.indentLevel;
                    int previousIndent = EditorGUI.indentLevel;
                    EditorGUI.indentLevel = 0;
                    try
                    {
                        drawer.OnGUI(controlRect, setProperty, label);
                    }
                    finally
                    {
                        EditorGUI.indentLevel = previousIndent;
                    }
                    groupedIndentAfter = EditorGUI.indentLevel;
                    groupedResolvedPosition = drawer.LastResolvedPosition;
                }
            });

            serializedObject.Update();
            bool groupedSetExpandedAfterDraw = setProperty.isExpanded;
            TestContext.WriteLine(
                $"ManualEntryHeader grouped foldout states -> set: {groupedSetExpandedBeforeDraw}->{groupedSetExpandedAfterDraw}, pending: {groupedPendingExpandedBeforeDraw}->{pending.isExpanded}"
            );
            TestContext.WriteLine(
                $"ManualEntryHeader grouped context -> indentLevel: {groupedIndentBefore}->{groupedIndentAfter}, "
                    + $"resolvedPosition: ({groupedResolvedPosition.x:F2}, {groupedResolvedPosition.y:F2}, {groupedResolvedPosition.width:F2}, {groupedResolvedPosition.height:F2}), "
                    + $"currentPadding: left={groupedCurrentLeftPadding:F2}, right={groupedCurrentRightPadding:F2}"
            );

            Assert.IsTrue(
                SerializableSetPropertyDrawer.HasLastManualEntryHeaderRect,
                "Grouped draw should capture the manual entry header layout."
            );

            Rect groupedHeader = SerializableSetPropertyDrawer.LastManualEntryHeaderRect;

            float contentDeltaX = groupedResolvedPosition.x - baselineResolvedPosition.x;
            float contentDeltaWidth =
                baselineResolvedPosition.width - groupedResolvedPosition.width;

            TestContext.WriteLine(
                $"ManualEntryHeader diagnostics -> baseline.xMin={baselineHeader.xMin:F2}, baseline.width={baselineHeader.width:F2}, "
                    + $"grouped.xMin={groupedHeader.xMin:F2}, grouped.width={groupedHeader.width:F2}, "
                    + $"LeftPadding={LeftPadding:F2}, RightPadding={RightPadding:F2}, "
                    + $"contentDeltaX={contentDeltaX:F2}, contentDeltaWidth={contentDeltaWidth:F2}, "
                    + $"expected.xMin={baselineHeader.xMin + contentDeltaX:F2}, "
                    + $"expected.width={Mathf.Max(0f, baselineHeader.width - contentDeltaWidth):F2}"
            );

            Assert.That(
                groupedHeader.xMin,
                Is.EqualTo(baselineHeader.xMin + contentDeltaX).Within(0.5f),
                "Manual entry header should shift by the content rect's x delta."
            );
            Assert.That(
                groupedHeader.width,
                Is.EqualTo(Mathf.Max(0f, baselineHeader.width - contentDeltaWidth)).Within(0.5f),
                "Manual entry header width should shrink by the content rect's width delta."
            );
        }

        [UnityTest]
        public IEnumerator ManualEntryValueFieldHonorsGroupPadding()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            setProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            pending.isExpanded = true;

            Rect controlRect = new(0f, 0f, 360f, 420f);
            GUIContent label = new("Set");

            SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();
            bool baselineSetExpandedBeforeDraw = setProperty.isExpanded;
            bool baselinePendingExpandedBeforeDraw = pending.isExpanded;
            Rect baselineResolvedPosition = default;

            yield return TestIMGUIExecutor.Run(() =>
            {
                setProperty.serializedObject.UpdateIfRequiredOrScript();
                pending.isExpanded = true;
                int previousIndent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                try
                {
                    drawer.OnGUI(controlRect, setProperty, label);
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndent;
                }
                baselineResolvedPosition = drawer.LastResolvedPosition;
            });

            serializedObject.Update();
            bool baselineSetExpandedAfterDraw = setProperty.isExpanded;
            TestContext.WriteLine(
                $"ManualEntryValue baseline foldout states -> set: {baselineSetExpandedBeforeDraw}->{baselineSetExpandedAfterDraw}, pending: {baselinePendingExpandedBeforeDraw}->{pending.isExpanded}"
            );

            Assert.IsTrue(
                SerializableSetPropertyDrawer.HasLastManualEntryValueRect,
                "Baseline draw should capture the manual entry value layout."
            );

            Rect baselineValue = SerializableSetPropertyDrawer.LastManualEntryValueRect;

            const float LeftPadding = 18f;
            const float RightPadding = 14f;
            float horizontalPadding = LeftPadding + RightPadding;

            GroupGUIWidthUtility.ResetForTests();

            bool groupedSetExpandedBeforeDraw = setProperty.isExpanded;
            bool groupedPendingExpandedBeforeDraw = pending.isExpanded;
            Rect groupedResolvedPosition = default;

            yield return TestIMGUIExecutor.Run(() =>
            {
                setProperty.serializedObject.UpdateIfRequiredOrScript();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        LeftPadding,
                        RightPadding
                    )
                )
                {
                    SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();
                    pending.isExpanded = true;
                    int previousIndent = EditorGUI.indentLevel;
                    EditorGUI.indentLevel = 0;
                    try
                    {
                        drawer.OnGUI(controlRect, setProperty, label);
                    }
                    finally
                    {
                        EditorGUI.indentLevel = previousIndent;
                    }
                    groupedResolvedPosition = drawer.LastResolvedPosition;
                }
            });

            serializedObject.Update();
            bool groupedSetExpandedAfterDraw = setProperty.isExpanded;
            TestContext.WriteLine(
                $"ManualEntryValue grouped foldout states -> set: {groupedSetExpandedBeforeDraw}->{groupedSetExpandedAfterDraw}, pending: {groupedPendingExpandedBeforeDraw}->{pending.isExpanded}"
            );

            Assert.IsTrue(
                SerializableSetPropertyDrawer.HasLastManualEntryValueRect,
                "Grouped draw should capture the manual entry value layout."
            );

            Rect groupedValue = SerializableSetPropertyDrawer.LastManualEntryValueRect;

            float contentDeltaX = groupedResolvedPosition.x - baselineResolvedPosition.x;
            float contentDeltaWidth =
                baselineResolvedPosition.width - groupedResolvedPosition.width;

            Assert.That(
                groupedValue.xMin,
                Is.EqualTo(baselineValue.xMin + contentDeltaX).Within(0.5f),
                "Manual entry value field should shift by the content rect's x delta."
            );
            Assert.That(
                groupedValue.width,
                Is.EqualTo(Mathf.Max(0f, baselineValue.width - contentDeltaWidth)).Within(0.5f),
                "Manual entry value width should shrink by the content rect's width delta."
            );
        }

        [UnityTest]
        public IEnumerator ManualEntryFoldoutRespectsExplicitExpansionBeforeInitialization()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            setProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 360f, 420f);
            GUIContent label = new("Set");

            SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();
            bool setExpandedBeforeDraw = setProperty.isExpanded;

            yield return TestIMGUIExecutor.Run(() =>
            {
                setProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, setProperty, label);
            });

            serializedObject.Update();
            bool setExpandedAfterDraw = setProperty.isExpanded;
            TestContext.WriteLine(
                $"ManualEntry explicit expansion state -> {setExpandedBeforeDraw}->{setExpandedAfterDraw}"
            );

            Assert.IsTrue(
                setExpandedAfterDraw,
                "Set foldout should remain expanded when explicitly set before the first draw."
            );
            Assert.IsTrue(
                SerializableSetPropertyDrawer.HasLastManualEntryHeaderRect,
                "Manual entry header should render when the foldout remains expanded."
            );
        }

        [UnityTest]
        public IEnumerator ManualEntryAnimBoolSynchronizesWithExpandedState()
        {
            // This test verifies that when pending.isExpanded is set programmatically,
            // the AnimBool target synchronizes correctly so the animation starts toward
            // the new state. We then force the animation to complete to verify content renders.
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            setProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 360f, 420f);
            GUIContent label = new("Set");

            // Create pending entry and get reference to it - initially collapsed
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            pending.isExpanded = false;

            // First draw with collapsed state to create AnimBool
            yield return TestIMGUIExecutor.Run(() =>
            {
                setProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, setProperty, label);
            });

            bool rectsAfterCollapsed = SerializableSetPropertyDrawer.HasLastManualEntryValueRect;
            TestContext.WriteLine(
                $"[ManualEntryAnimBoolSynchronizesWithExpandedState] after collapsed draw: "
                    + $"hasValueRect={rectsAfterCollapsed}"
            );

            // Now expand programmatically (simulating what tests do)
            pending.isExpanded = true;

            // Force animation to complete immediately for test purposes
            // (in real usage, this animates smoothly over multiple frames)
            if (pending.foldoutAnim != null)
            {
                pending.foldoutAnim.value = pending.isExpanded;
            }

            SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

            // Draw again - content should now render since animation is complete
            yield return TestIMGUIExecutor.Run(() =>
            {
                setProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, setProperty, label);
            });

            bool rectsAfterExpanded = SerializableSetPropertyDrawer.HasLastManualEntryValueRect;
            TestContext.WriteLine(
                $"[ManualEntryAnimBoolSynchronizesWithExpandedState] after expanded draw: "
                    + $"hasValueRect={rectsAfterExpanded}, isExpanded={pending.isExpanded}"
            );

            Assert.IsTrue(
                rectsAfterExpanded,
                "Manual entry value rect should be captured after expansion when animation completes."
            );
        }

        [UnityTest]
        public IEnumerator RowContentHonorsGroupPadding()
        {
            ComplexSetHost host = CreateScriptableObject<ComplexSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array snapshot = new ComplexSetElement[1];
            snapshot.SetValue(
                new ComplexSetElement
                {
                    primary = Color.yellow,
                    nested = new NestedComplexElement
                    {
                        intensity = 2.5f,
                        offset = new Vector2(3f, 1f),
                    },
                },
                0
            );
            inspector.SetSerializedItemsSnapshot(snapshot, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(ComplexSetHost.set)
            );
            setProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 420f, 520f);
            GUIContent label = new("Set");

            SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();
            bool baselineSetExpandedBeforeDraw = setProperty.isExpanded;
            Rect baselineResolvedPosition = default;

            yield return TestIMGUIExecutor.Run(() =>
            {
                setProperty.serializedObject.UpdateIfRequiredOrScript();
                int previousIndent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                try
                {
                    drawer.OnGUI(controlRect, setProperty, label);
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndent;
                }
                baselineResolvedPosition = drawer.LastResolvedPosition;
            });

            serializedObject.Update();
            bool baselineSetExpandedAfterDraw = setProperty.isExpanded;
            TestContext.WriteLine(
                $"RowContent baseline set foldout state -> {baselineSetExpandedBeforeDraw}->{baselineSetExpandedAfterDraw}"
            );

            Assert.IsTrue(
                SerializableSetPropertyDrawer.HasLastRowContentRect,
                "Baseline draw should capture the row content layout."
            );

            Rect baselineRow = SerializableSetPropertyDrawer.LastRowContentRect;

            const float LeftPadding = 24f;
            const float RightPadding = 10f;
            float horizontalPadding = LeftPadding + RightPadding;

            GroupGUIWidthUtility.ResetForTests();

            bool groupedSetExpandedBeforeDraw = setProperty.isExpanded;
            Rect groupedResolvedPosition = default;

            yield return TestIMGUIExecutor.Run(() =>
            {
                setProperty.serializedObject.UpdateIfRequiredOrScript();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        LeftPadding,
                        RightPadding
                    )
                )
                {
                    SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();
                    int previousIndent = EditorGUI.indentLevel;
                    EditorGUI.indentLevel = 0;
                    try
                    {
                        drawer.OnGUI(controlRect, setProperty, label);
                    }
                    finally
                    {
                        EditorGUI.indentLevel = previousIndent;
                    }
                    groupedResolvedPosition = drawer.LastResolvedPosition;
                }
            });

            serializedObject.Update();
            bool groupedSetExpandedAfterDraw = setProperty.isExpanded;
            TestContext.WriteLine(
                $"RowContent grouped set foldout state -> {groupedSetExpandedBeforeDraw}->{groupedSetExpandedAfterDraw}"
            );

            Assert.IsTrue(
                SerializableSetPropertyDrawer.HasLastRowContentRect,
                "Grouped draw should capture the row content layout."
            );

            Rect groupedRow = SerializableSetPropertyDrawer.LastRowContentRect;

            float contentDeltaX = groupedResolvedPosition.x - baselineResolvedPosition.x;
            float contentDeltaWidth =
                baselineResolvedPosition.width - groupedResolvedPosition.width;

            Assert.That(
                groupedRow.xMin,
                Is.EqualTo(baselineRow.xMin + contentDeltaX).Within(0.5f),
                "Row content should shift by the content rect's x delta."
            );
            Assert.That(
                groupedRow.width,
                Is.EqualTo(Mathf.Max(0f, baselineRow.width - contentDeltaWidth)).Within(0.5f),
                "Row content width should shrink by the content rect's width delta."
            );
        }

        [UnityTest]
        public IEnumerator SetDrawerDoesNotResetUnappliedScalarChanges()
        {
            MixedFieldsSetHost host = Track(ScriptableObject.CreateInstance<MixedFieldsSetHost>());
            SerializedObject serializedHost = TrackDisposable(new SerializedObject(host));
            serializedHost.Update();

            SerializedProperty scalarProperty = serializedHost.FindProperty(
                nameof(MixedFieldsSetHost.scalarValue)
            );
            SerializedProperty setProperty = serializedHost.FindProperty(
                nameof(MixedFieldsSetHost.set)
            );

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 320f, 280f);
            GUIContent label = new("Set");

            drawer.GetPropertyHeight(setProperty, label);

            scalarProperty.intValue = 4242;

            yield return TestIMGUIExecutor.Run(() =>
            {
                drawer.OnGUI(controlRect, setProperty, label);
            });

            serializedHost.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(host.scalarValue, Is.EqualTo(4242));
        }

        [UnityTest]
        public IEnumerator SetDrawerMultiObjectEditKeepsScalarChanges()
        {
            MixedFieldsSetHost first = Track(ScriptableObject.CreateInstance<MixedFieldsSetHost>());
            MixedFieldsSetHost second = Track(
                ScriptableObject.CreateInstance<MixedFieldsSetHost>()
            );
            SerializedObject serializedHosts = TrackDisposable(
                new SerializedObject(new Object[] { first, second })
            );
            serializedHosts.Update();

            SerializedProperty scalarProperty = serializedHosts.FindProperty(
                nameof(MixedFieldsSetHost.scalarValue)
            );
            SerializedProperty setProperty = serializedHosts.FindProperty(
                nameof(MixedFieldsSetHost.set)
            );

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 320f, 280f);
            GUIContent label = new("Set");

            drawer.GetPropertyHeight(setProperty, label);

            scalarProperty.intValue = 5150;

            yield return TestIMGUIExecutor.Run(() =>
            {
                drawer.OnGUI(controlRect, setProperty, label);
            });

            serializedHosts.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(
                first.scalarValue,
                Is.EqualTo(5150),
                "First target should retain scalar edit."
            );
            Assert.That(
                second.scalarValue,
                Is.EqualTo(5150),
                "Second target should retain scalar edit."
            );
        }

        [UnityTest]
        public IEnumerator SetDrawerDoesNotResetTrailingScalarField()
        {
            SetScalarAfterHost host = Track(ScriptableObject.CreateInstance<SetScalarAfterHost>());
            SerializedObject serializedHost = TrackDisposable(new SerializedObject(host));
            serializedHost.Update();

            SerializedProperty setProperty = serializedHost.FindProperty(
                nameof(SetScalarAfterHost.set)
            );
            SerializedProperty trailingScalar = serializedHost.FindProperty(
                nameof(SetScalarAfterHost.trailingScalar)
            );

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 320f, 280f);
            GUIContent label = new("Set");

            drawer.GetPropertyHeight(setProperty, label);

            trailingScalar.intValue = 777;

            yield return TestIMGUIExecutor.Run(() =>
            {
                drawer.OnGUI(controlRect, setProperty, label);
            });

            serializedHost.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(host.trailingScalar, Is.EqualTo(777));
        }

        private static string DumpIntArray(SerializedProperty property)
        {
            if (property == null || !property.isArray)
            {
                return "<null>";
            }

            List<int> values = new(property.arraySize);
            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                values.Add(element?.intValue ?? 0);
            }

            return string.Join(", ", values);
        }

        private static string DumpPageEntries(SerializableSetPropertyDrawer.ListPageCache cache)
        {
            if (cache?.entries == null || cache.entries.Count == 0)
            {
                return "[]";
            }

            List<int> indices = new(cache.entries.Count);
            foreach (SerializableSetPropertyDrawer.PageEntry cacheEntry in cache.entries)
            {
                indices.Add(cacheEntry?.arrayIndex ?? -1);
            }

            return $"[{string.Join(", ", indices)}]";
        }

        [Test]
        public void GetPropertyHeightIncreasesWhenPendingEntryIsExpanded()
        {
            using (new SetTweenDisabledScope())
            {
                bool tweenEnabled = SerializableSetPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedSet: false
                );
                Assert.IsFalse(
                    tweenEnabled,
                    "Tween should be disabled by SetTweenDisabledScope for accurate height tests."
                );

                StringSetHost host = CreateScriptableObject<StringSetHost>();
                SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
                serializedObject.Update();
                SerializedProperty setProperty = serializedObject.FindProperty(
                    nameof(StringSetHost.set)
                );
                setProperty.isExpanded = true;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                SerializableSetPropertyDrawer drawer = new();
                SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                    setProperty,
                    setProperty.propertyPath,
                    typeof(string),
                    isSortedSet: false
                );

                pending.isExpanded = false;
                float collapsedHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pending.isExpanded = true;
                float expandedHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                string diagnostics =
                    $"collapsedHeight={collapsedHeight}, expandedHeight={expandedHeight}, "
                    + $"pendingIsExpanded={pending.isExpanded}, foldoutAnimExists={pending.foldoutAnim != null}, "
                    + $"tweenEnabled={tweenEnabled}";
                Assert.Greater(
                    expandedHeight,
                    collapsedHeight,
                    $"Property height should increase when the pending New Entry section is expanded. Diagnostics: {diagnostics}"
                );
            }
        }

        [Test]
        public void GetPropertyHeightDecreasesWhenPendingEntryIsCollapsed()
        {
            using (new SetTweenDisabledScope())
            {
                bool tweenEnabled = SerializableSetPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedSet: false
                );
                Assert.IsFalse(
                    tweenEnabled,
                    "Tween should be disabled by SetTweenDisabledScope for accurate height tests."
                );

                StringSetHost host = CreateScriptableObject<StringSetHost>();
                SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
                serializedObject.Update();
                SerializedProperty setProperty = serializedObject.FindProperty(
                    nameof(StringSetHost.set)
                );
                setProperty.isExpanded = true;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                SerializableSetPropertyDrawer drawer = new();
                SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                    setProperty,
                    setProperty.propertyPath,
                    typeof(string),
                    isSortedSet: false
                );

                pending.isExpanded = true;
                float expandedHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pending.isExpanded = false;
                float collapsedHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                string diagnostics =
                    $"expandedHeight={expandedHeight}, collapsedHeight={collapsedHeight}, "
                    + $"pendingIsExpanded={pending.isExpanded}, foldoutAnimExists={pending.foldoutAnim != null}, "
                    + $"tweenEnabled={tweenEnabled}";
                Assert.Less(
                    collapsedHeight,
                    expandedHeight,
                    $"Property height should decrease when the pending New Entry section is collapsed. Diagnostics: {diagnostics}"
                );
            }
        }

        [Test]
        public void GetPropertyHeightCacheInvalidatesWhenPendingExpandStateChanges()
        {
            using (new SetTweenDisabledScope())
            {
                bool tweenEnabled = SerializableSetPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedSet: false
                );
                Assert.IsFalse(
                    tweenEnabled,
                    "Tween should be disabled by SetTweenDisabledScope for accurate height tests."
                );

                StringSetHost host = CreateScriptableObject<StringSetHost>();
                SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
                serializedObject.Update();
                SerializedProperty setProperty = serializedObject.FindProperty(
                    nameof(StringSetHost.set)
                );
                setProperty.isExpanded = true;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                SerializableSetPropertyDrawer drawer = new();
                SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                    setProperty,
                    setProperty.propertyPath,
                    typeof(string),
                    isSortedSet: false
                );

                pending.isExpanded = false;
                float heightBeforeToggle = drawer.GetPropertyHeight(setProperty, GUIContent.none);
                float heightCachedSame = drawer.GetPropertyHeight(setProperty, GUIContent.none);
                Assert.AreEqual(
                    heightBeforeToggle,
                    heightCachedSame,
                    0.001f,
                    "Height should be cached and return the same value when nothing changes."
                );

                pending.isExpanded = true;
                float heightAfterExpand = drawer.GetPropertyHeight(setProperty, GUIContent.none);
                string diagnostics =
                    $"heightBeforeToggle={heightBeforeToggle}, heightCachedSame={heightCachedSame}, "
                    + $"heightAfterExpand={heightAfterExpand}, pendingIsExpanded={pending.isExpanded}, "
                    + $"foldoutAnimExists={pending.foldoutAnim != null}, tweenEnabled={tweenEnabled}";
                Assert.AreNotEqual(
                    heightBeforeToggle,
                    heightAfterExpand,
                    $"Height cache should invalidate when pending isExpanded state changes. Diagnostics: {diagnostics}"
                );
            }
        }

        [Test]
        public void GetPropertyHeightReturnsConsistentHeightsForEmptySetWithPendingExpanded()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            setProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            pending.isExpanded = true;

            float height1 = drawer.GetPropertyHeight(setProperty, GUIContent.none);
            float height2 = drawer.GetPropertyHeight(setProperty, GUIContent.none);
            float height3 = drawer.GetPropertyHeight(setProperty, GUIContent.none);

            Assert.AreEqual(
                height1,
                height2,
                0.001f,
                "Height should remain consistent across multiple calls with same state."
            );
            Assert.AreEqual(
                height2,
                height3,
                0.001f,
                "Height should remain consistent across multiple calls with same state."
            );
        }

        [Test]
        public void GetPropertyHeightDiffersBetweenExpandedAndCollapsedPendingForEmptySet()
        {
            using (new SetTweenDisabledScope())
            {
                bool tweenEnabled = SerializableSetPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedSet: false
                );
                Assert.IsFalse(
                    tweenEnabled,
                    "Tween should be disabled by SetTweenDisabledScope for accurate height tests."
                );

                StringSetHost host = CreateScriptableObject<StringSetHost>();
                SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
                serializedObject.Update();
                SerializedProperty setProperty = serializedObject.FindProperty(
                    nameof(StringSetHost.set)
                );
                setProperty.isExpanded = true;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                SerializableSetPropertyDrawer drawer = new();
                SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                    setProperty,
                    setProperty.propertyPath,
                    typeof(string),
                    isSortedSet: false
                );

                pending.isExpanded = false;
                float collapsedHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pending.isExpanded = true;
                float expandedHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                float heightDifference = expandedHeight - collapsedHeight;
                float minimumExpectedDifference = EditorGUIUtility.singleLineHeight;
                string diagnostics =
                    $"collapsedHeight={collapsedHeight}, expandedHeight={expandedHeight}, "
                    + $"heightDifference={heightDifference}, pendingIsExpanded={pending.isExpanded}, "
                    + $"foldoutAnimExists={pending.foldoutAnim != null}, tweenEnabled={tweenEnabled}";
                Assert.Greater(
                    heightDifference,
                    minimumExpectedDifference,
                    $"Expanded pending entry height should be at least {minimumExpectedDifference}px larger than collapsed. Actual difference: {heightDifference}px. Diagnostics: {diagnostics}"
                );
            }
        }

        [Test]
        public void GetPropertyHeightPendingExpandAffectsHeightEvenWithSetEntries()
        {
            using (new SetTweenDisabledScope())
            {
                bool tweenEnabled = SerializableSetPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedSet: false
                );
                Assert.IsFalse(
                    tweenEnabled,
                    "Tween should be disabled by SetTweenDisabledScope for accurate height tests."
                );

                StringSetHost host = CreateScriptableObject<StringSetHost>();
                host.set.Add("one");
                host.set.Add("two");
                host.set.Add("three");

                SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
                serializedObject.Update();
                SerializedProperty setProperty = serializedObject.FindProperty(
                    nameof(StringSetHost.set)
                );
                setProperty.isExpanded = true;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                SerializableSetPropertyDrawer drawer = new();
                SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                    setProperty,
                    setProperty.propertyPath,
                    typeof(string),
                    isSortedSet: false
                );

                pending.isExpanded = false;
                float collapsedHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pending.isExpanded = true;
                float expandedHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                string diagnostics =
                    $"collapsedHeight={collapsedHeight}, expandedHeight={expandedHeight}, "
                    + $"pendingIsExpanded={pending.isExpanded}, foldoutAnimExists={pending.foldoutAnim != null}, "
                    + $"tweenEnabled={tweenEnabled}";
                Assert.Greater(
                    expandedHeight,
                    collapsedHeight,
                    $"Expanding the pending entry should increase height even when the set has existing entries. Diagnostics: {diagnostics}"
                );
            }
        }

        [Test]
        public void GetPropertyHeightTogglingPendingMultipleTimesUpdatesHeightCorrectly()
        {
            using (new SetTweenDisabledScope())
            {
                bool tweenEnabled = SerializableSetPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedSet: false
                );
                Assert.IsFalse(
                    tweenEnabled,
                    "Tween should be disabled by SetTweenDisabledScope for accurate height tests."
                );

                StringSetHost host = CreateScriptableObject<StringSetHost>();
                SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
                serializedObject.Update();
                SerializedProperty setProperty = serializedObject.FindProperty(
                    nameof(StringSetHost.set)
                );
                setProperty.isExpanded = true;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                SerializableSetPropertyDrawer drawer = new();
                SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                    setProperty,
                    setProperty.propertyPath,
                    typeof(string),
                    isSortedSet: false
                );

                pending.isExpanded = false;
                float collapsed1 = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pending.isExpanded = true;
                float expanded1 = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pending.isExpanded = false;
                float collapsed2 = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pending.isExpanded = true;
                float expanded2 = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                string diagnostics =
                    $"collapsed1={collapsed1}, expanded1={expanded1}, "
                    + $"collapsed2={collapsed2}, expanded2={expanded2}, "
                    + $"pendingIsExpanded={pending.isExpanded}, foldoutAnimExists={pending.foldoutAnim != null}, "
                    + $"tweenEnabled={tweenEnabled}";
                Assert.AreEqual(
                    collapsed1,
                    collapsed2,
                    0.001f,
                    $"Collapsed heights should be consistent across multiple toggles. Diagnostics: {diagnostics}"
                );
                Assert.AreEqual(
                    expanded1,
                    expanded2,
                    0.001f,
                    $"Expanded heights should be consistent across multiple toggles. Diagnostics: {diagnostics}"
                );
                Assert.Greater(
                    expanded1,
                    collapsed1,
                    $"Expanded height should always be greater than collapsed height. Diagnostics: {diagnostics}"
                );
            }
        }

        [Test]
        public void GetPropertyHeightSortedSetPendingExpandBehavesCorrectly()
        {
            using (new SortedSetTweenDisabledScope())
            {
                bool tweenEnabled = SerializableSetPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedSet: true
                );
                Assert.IsFalse(
                    tweenEnabled,
                    "Tween should be disabled by SortedSetTweenDisabledScope for accurate height tests."
                );

                SortedStringSetHost host = CreateScriptableObject<SortedStringSetHost>();
                SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
                serializedObject.Update();
                SerializedProperty setProperty = serializedObject.FindProperty(
                    nameof(SortedStringSetHost.set)
                );
                setProperty.isExpanded = true;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                SerializableSetPropertyDrawer drawer = new();
                SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                    setProperty,
                    setProperty.propertyPath,
                    typeof(string),
                    isSortedSet: true
                );

                pending.isExpanded = false;
                float collapsedHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pending.isExpanded = true;
                float expandedHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                string diagnostics =
                    $"collapsedHeight={collapsedHeight}, expandedHeight={expandedHeight}, "
                    + $"pendingIsExpanded={pending.isExpanded}, foldoutAnimExists={pending.foldoutAnim != null}, "
                    + $"tweenEnabled={tweenEnabled}";
                Assert.Greater(
                    expandedHeight,
                    collapsedHeight,
                    $"Sorted set should also update height when pending entry is expanded. Diagnostics: {diagnostics}"
                );
            }
        }

        [Test]
        public void GetPropertyHeightComplexSetPendingExpandUpdatesHeight()
        {
            using (new SetTweenDisabledScope())
            {
                bool tweenEnabled = SerializableSetPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedSet: false
                );
                Assert.IsFalse(
                    tweenEnabled,
                    "Tween should be disabled by SetTweenDisabledScope for accurate height tests."
                );

                ComplexSetHost host = CreateScriptableObject<ComplexSetHost>();
                SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
                serializedObject.Update();
                SerializedProperty setProperty = serializedObject.FindProperty(
                    nameof(ComplexSetHost.set)
                );
                setProperty.isExpanded = true;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                SerializableSetPropertyDrawer drawer = new();
                SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                    setProperty,
                    setProperty.propertyPath,
                    typeof(ComplexSetElement),
                    isSortedSet: false
                );

                pending.isExpanded = false;
                float collapsedHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pending.isExpanded = true;
                float expandedHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                string complexDiagnostics =
                    $"collapsedHeight={collapsedHeight}, expandedHeight={expandedHeight}, "
                    + $"pendingIsExpanded={pending.isExpanded}, foldoutAnimExists={pending.foldoutAnim != null}, "
                    + $"tweenEnabled={tweenEnabled}";
                Assert.Greater(
                    expandedHeight,
                    collapsedHeight,
                    $"Complex set should update height when pending entry is expanded. Diagnostics: {complexDiagnostics}"
                );
            }
        }

        [Test]
        public void GetPropertyHeightMainFoldoutCollapsedIgnoresPendingState()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            setProperty.isExpanded = false;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();

            float collapsedHeight1 = drawer.GetPropertyHeight(setProperty, GUIContent.none);

            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            pending.isExpanded = true;

            float collapsedHeight2 = drawer.GetPropertyHeight(setProperty, GUIContent.none);

            Assert.AreEqual(
                collapsedHeight1,
                collapsedHeight2,
                0.001f,
                "When the main set foldout is collapsed, pending entry state should not affect height."
            );
        }

        private sealed class SetTweenDisabledScope : IDisposable
        {
            private readonly bool originalValue;
            private bool disposed;

            public SetTweenDisabledScope()
            {
                originalValue = UnityHelpersSettings.ShouldTweenSerializableSetFoldouts();
                UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(false);
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(originalValue);
            }
        }

        private sealed class SortedSetTweenDisabledScope : IDisposable
        {
            private readonly bool originalValue;
            private bool disposed;

            public SortedSetTweenDisabledScope()
            {
                originalValue = UnityHelpersSettings.ShouldTweenSerializableSortedSetFoldouts();
                UnityHelpersSettings.SetSerializableSortedSetFoldoutTweenEnabled(false);
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                UnityHelpersSettings.SetSerializableSortedSetFoldoutTweenEnabled(originalValue);
            }
        }

        [Test]
        public void DuplicateDetectionTriggersImmediatelyWhenElementEditedToMatchAnother()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            ISerializableSetInspector inspector = host.set;

            Array initial = new string[] { "Alpha", "Beta" };
            inspector.SetSerializedItemsSnapshot(initial, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();

            SerializableSetPropertyDrawer.DuplicateState initialState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);
            Assert.IsFalse(
                initialState.hasDuplicates,
                "Initial state should have no duplicates with distinct values."
            );

            SerializedProperty firstElement = itemsProperty.GetArrayElementAtIndex(0);
            firstElement.stringValue = "Beta";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableSetPropertyDrawer.DuplicateState afterEditState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);
            Assert.IsTrue(
                afterEditState.hasDuplicates,
                "Duplicate detection should trigger immediately after editing an element to match another."
            );
            CollectionAssert.AreEquivalent(
                new[] { 0, 1 },
                afterEditState.duplicateIndices,
                "Both indices should be marked as duplicates."
            );
        }

        [Test]
        public void DuplicateDetectionClearsWhenDuplicateElementIsEditedToBeUnique()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            ISerializableSetInspector inspector = host.set;

            Array duplicated = new string[] { "Same", "Same" };
            inspector.SetSerializedItemsSnapshot(duplicated, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int itemsArraySize = itemsProperty != null ? itemsProperty.arraySize : -1;
            TestContext.WriteLine(
                $"After SetSerializedItemsSnapshot: itemsProperty.arraySize={itemsArraySize}, inspector.SerializedCount={inspector.SerializedCount}, inspector.UniqueCount={inspector.UniqueCount}"
            );

            SerializableSetPropertyDrawer drawer = new();

            SerializableSetPropertyDrawer.DuplicateState initialState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);
            Assert.IsTrue(
                initialState.hasDuplicates,
                $"Initial state should have duplicates. arraySize={itemsArraySize}, SerializedCount={inspector.SerializedCount}, UniqueCount={inspector.UniqueCount}"
            );

            SerializedProperty secondElement = itemsProperty.GetArrayElementAtIndex(1);
            secondElement.stringValue = "Different";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableSetPropertyDrawer.DuplicateState afterEditState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);
            Assert.IsFalse(
                afterEditState.hasDuplicates,
                "Duplicate detection should clear when duplicate is edited to be unique."
            );
            Assert.AreEqual(
                0,
                afterEditState.duplicateIndices.Count,
                "No indices should be marked as duplicates after fix."
            );
        }

        [Test]
        public void DuplicateDetectionHandlesMultipleEditCyclesWithoutStaleState()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            ISerializableSetInspector inspector = host.set;

            Array initial = new string[] { "A", "B", "C" };
            inspector.SetSerializedItemsSnapshot(initial, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();

            SerializableSetPropertyDrawer.DuplicateState state1 = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );
            Assert.IsFalse(state1.hasDuplicates, "Cycle 1: No duplicates expected.");

            itemsProperty.GetArrayElementAtIndex(0).stringValue = "B";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableSetPropertyDrawer.DuplicateState state2 = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );
            Assert.IsTrue(state2.hasDuplicates, "Cycle 2: A and B should now be duplicates.");
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, state2.duplicateIndices);

            itemsProperty.GetArrayElementAtIndex(2).stringValue = "B";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableSetPropertyDrawer.DuplicateState state3 = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );
            Assert.IsTrue(state3.hasDuplicates, "Cycle 3: All three should be duplicates.");
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, state3.duplicateIndices);

            itemsProperty.GetArrayElementAtIndex(0).stringValue = "X";
            itemsProperty.GetArrayElementAtIndex(1).stringValue = "Y";
            itemsProperty.GetArrayElementAtIndex(2).stringValue = "Z";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableSetPropertyDrawer.DuplicateState state4 = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );
            Assert.IsFalse(state4.hasDuplicates, "Cycle 4: All unique, no duplicates.");
        }

        [Test]
        public void DuplicateDetectionWorksWithIntegerSets()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;

            Array initial = Array.CreateInstance(typeof(int), 3);
            initial.SetValue(10, 0);
            initial.SetValue(20, 1);
            initial.SetValue(30, 2);
            inspector.SetSerializedItemsSnapshot(initial, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();

            SerializableSetPropertyDrawer.DuplicateState initialState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);
            Assert.IsFalse(initialState.hasDuplicates);

            itemsProperty.GetArrayElementAtIndex(2).intValue = 10;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableSetPropertyDrawer.DuplicateState afterEditState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);
            Assert.IsTrue(
                afterEditState.hasDuplicates,
                "Integer duplicate should be detected after edit."
            );
            CollectionAssert.AreEquivalent(new[] { 0, 2 }, afterEditState.duplicateIndices);
        }

        [Test]
        public void DuplicateDetectionHandlesEmptyStringsAsValidDuplicates()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            ISerializableSetInspector inspector = host.set;

            Array initial = new string[] { "", "NonEmpty" };
            inspector.SetSerializedItemsSnapshot(initial, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();

            SerializableSetPropertyDrawer.DuplicateState initialState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);
            Assert.IsFalse(initialState.hasDuplicates);

            itemsProperty.GetArrayElementAtIndex(1).stringValue = "";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableSetPropertyDrawer.DuplicateState afterEditState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);
            Assert.IsTrue(
                afterEditState.hasDuplicates,
                "Empty string duplicates should be detected."
            );
        }

        [Test]
        public void DuplicateDetectionHandlesCaseSensitiveStrings()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            ISerializableSetInspector inspector = host.set;

            Array initial = new string[] { "test", "TEST" };
            inspector.SetSerializedItemsSnapshot(initial, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();

            SerializableSetPropertyDrawer.DuplicateState initialState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);
            Assert.IsFalse(
                initialState.hasDuplicates,
                "Case-sensitive comparison should treat 'test' and 'TEST' as different."
            );

            itemsProperty.GetArrayElementAtIndex(1).stringValue = "test";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableSetPropertyDrawer.DuplicateState afterEditState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);
            Assert.IsTrue(
                afterEditState.hasDuplicates,
                "Exact match after edit should be detected as duplicate."
            );
        }

        [Test]
        public void NullEntryRefreshTriggersWhenElementChangesToNull()
        {
            ObjectSetHost host = CreateScriptableObject<ObjectSetHost>();
            ISerializableSetInspector inspector = host.set;

            TestData testData = CreateScriptableObject<TestData>();
            Array initial = Array.CreateInstance(inspector.ElementType, 2);
            initial.SetValue(testData, 0);
            initial.SetValue(CreateScriptableObject<TestData>(), 1);
            inspector.SetSerializedItemsSnapshot(initial, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(ObjectSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();

            SerializableSetPropertyDrawer.NullEntryState initialState =
                drawer.EvaluateNullEntryState(setProperty, itemsProperty, force: true);
            Assert.IsFalse(
                initialState.hasNullEntries,
                "Initial state should have no null entries."
            );

            itemsProperty.GetArrayElementAtIndex(0).objectReferenceValue = null;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableSetPropertyDrawer.NullEntryState afterEditState =
                drawer.EvaluateNullEntryState(setProperty, itemsProperty, force: true);
            Assert.IsTrue(
                afterEditState.hasNullEntries,
                "Null entry should be detected after setting element to null."
            );
            Assert.IsTrue(afterEditState.nullIndices.Contains(0));
        }

        [Test]
        public void DuplicateAndNullEntryDetectionWorkIndependently()
        {
            ObjectSetHost host = CreateScriptableObject<ObjectSetHost>();
            ISerializableSetInspector inspector = host.set;

            TestData sharedData = CreateScriptableObject<TestData>();
            Array initial = Array.CreateInstance(inspector.ElementType, 3);
            initial.SetValue(sharedData, 0);
            initial.SetValue(sharedData, 1);
            initial.SetValue(null, 2);
            inspector.SetSerializedItemsSnapshot(initial, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(ObjectSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int itemsArraySize = itemsProperty != null ? itemsProperty.arraySize : -1;
            TestContext.WriteLine(
                $"After SetSerializedItemsSnapshot: itemsProperty.arraySize={itemsArraySize}, inspector.SerializedCount={inspector.SerializedCount}, inspector.UniqueCount={inspector.UniqueCount}"
            );

            SerializableSetPropertyDrawer drawer = new();

            SerializableSetPropertyDrawer.DuplicateState dupState = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );
            SerializableSetPropertyDrawer.NullEntryState nullState = drawer.EvaluateNullEntryState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(
                dupState.hasDuplicates,
                $"Duplicate detection should find the shared object. arraySize={itemsArraySize}, SerializedCount={inspector.SerializedCount}, UniqueCount={inspector.UniqueCount}"
            );
            Assert.IsTrue(nullState.hasNullEntries, "Null detection should find the null entry.");
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, dupState.duplicateIndices);
            Assert.IsTrue(nullState.nullIndices.Contains(2));
        }

        [Test]
        public void NeedsDuplicateRefreshFlagIsConsumedAfterEvaluation()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            ISerializableSetInspector inspector = host.set;

            Array initial = new string[] { "Alpha", "Beta" };
            inspector.SetSerializedItemsSnapshot(initial, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );

            SerializableSetPropertyDrawer drawer = new();
            string listKey = drawer.GetPropertyCacheKey(setProperty);

            SerializableSetPropertyDrawer.SetListRenderContext context =
                drawer.GetOrCreateListContext(listKey);
            Assert.IsTrue(context != null, "Context should be created.");

            context.needsDuplicateRefresh = true;
            Assert.IsTrue(context.needsDuplicateRefresh, "Flag should be set to true.");
        }

        [Test]
        public void ClearAllClearsDuplicateWarningsWhenSetHasDuplicates()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;

            Array duplicates = Array.CreateInstance(inspector.ElementType, 3);
            duplicates.SetValue(42, 0);
            duplicates.SetValue(42, 1);
            duplicates.SetValue(99, 2);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int itemsArraySize = itemsProperty != null ? itemsProperty.arraySize : -1;
            TestContext.WriteLine(
                $"After SetSerializedItemsSnapshot: itemsProperty.arraySize={itemsArraySize}, inspector.SerializedCount={inspector.SerializedCount}, inspector.UniqueCount={inspector.UniqueCount}"
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState duplicateState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);

            Assert.IsTrue(
                duplicateState.hasDuplicates,
                $"Set with duplicate entries should report hasDuplicates=true. arraySize={itemsArraySize}, SerializedCount={inspector.SerializedCount}, UniqueCount={inspector.UniqueCount}"
            );
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, duplicateState.duplicateIndices);
            StringAssert.Contains("Duplicate entry 42", duplicateState.summary);

            inspector.ClearElements();
            inspector.SynchronizeSerializedState();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer.DuplicateState clearedState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);

            Assert.IsFalse(
                clearedState.hasDuplicates,
                "After clearing, hasDuplicates should be false."
            );
            CollectionAssert.IsEmpty(
                clearedState.duplicateIndices,
                "After clearing, duplicateIndices should be empty."
            );
            Assert.IsTrue(
                string.IsNullOrEmpty(clearedState.summary),
                "After clearing, duplicate summary should be empty."
            );
            CollectionAssert.IsEmpty(
                clearedState.animationStartTimes,
                "After clearing, animation start times should be cleared."
            );
            CollectionAssert.IsEmpty(
                clearedState.primaryFlags,
                "After clearing, primary flags should be cleared."
            );
        }

        [Test]
        public void ClearAllClearsNullEntryWarningsWhenSetHasNullEntries()
        {
            ObjectSetHost host = CreateScriptableObject<ObjectSetHost>();
            ISerializableSetInspector inspector = host.set;

            Array values = Array.CreateInstance(inspector.ElementType, 2);
            values.SetValue(null, 0);
            values.SetValue(CreateScriptableObject<TestData>(), 1);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(ObjectSetHost.set)
            );
            setProperty.isExpanded = true;
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.NullEntryState nullState = drawer.EvaluateNullEntryState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(
                nullState.hasNullEntries,
                "Set with null entry should report hasNullEntries=true."
            );
            Assert.IsTrue(
                nullState.nullIndices.Contains(0),
                "Null index 0 should be in nullIndices."
            );
            StringAssert.Contains("Null entry", nullState.summary);

            inspector.ClearElements();
            inspector.SynchronizeSerializedState();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(ObjectSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer.NullEntryState clearedState =
                drawer.EvaluateNullEntryState(setProperty, itemsProperty, force: true);

            Assert.IsFalse(
                clearedState.hasNullEntries,
                "After clearing, hasNullEntries should be false."
            );
            CollectionAssert.IsEmpty(
                clearedState.nullIndices,
                "After clearing, nullIndices should be empty."
            );
            Assert.IsTrue(
                string.IsNullOrEmpty(clearedState.summary),
                "After clearing, null entry summary should be empty."
            );
            CollectionAssert.IsEmpty(
                clearedState.tooltips,
                "After clearing, tooltips should be cleared."
            );
        }

        [Test]
        public void ClearAllOnEmptySetDoesNotProduceDuplicateWarnings()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState initialState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);

            Assert.IsFalse(
                initialState.hasDuplicates,
                "Empty set should not have duplicates initially."
            );

            ISerializableSetInspector inspector = host.set;
            inspector.ClearElements();
            inspector.SynchronizeSerializedState();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer.DuplicateState afterClearState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);

            Assert.IsFalse(
                afterClearState.hasDuplicates,
                "Empty set should not have duplicates after clear."
            );
            CollectionAssert.IsEmpty(afterClearState.duplicateIndices);
            Assert.IsTrue(string.IsNullOrEmpty(afterClearState.summary));
        }

        [Test]
        public void ClearAllClearsBothDuplicateAndNullEntryWarningsSimultaneously()
        {
            ObjectSetHost host = CreateScriptableObject<ObjectSetHost>();
            ISerializableSetInspector inspector = host.set;

            TestData sharedObject = CreateScriptableObject<TestData>();
            Array values = Array.CreateInstance(inspector.ElementType, 4);
            values.SetValue(sharedObject, 0);
            values.SetValue(sharedObject, 1);
            values.SetValue(null, 2);
            values.SetValue(CreateScriptableObject<TestData>(), 3);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(ObjectSetHost.set)
            );
            setProperty.isExpanded = true;
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState dupState = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );
            SerializableSetPropertyDrawer.NullEntryState nullState = drawer.EvaluateNullEntryState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(dupState.hasDuplicates, "Set should have duplicates.");
            Assert.IsTrue(nullState.hasNullEntries, "Set should have null entries.");
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, dupState.duplicateIndices);
            Assert.IsTrue(nullState.nullIndices.Contains(2));

            inspector.ClearElements();
            inspector.SynchronizeSerializedState();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(ObjectSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer.DuplicateState dupCleared = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );
            SerializableSetPropertyDrawer.NullEntryState nullCleared =
                drawer.EvaluateNullEntryState(setProperty, itemsProperty, force: true);

            Assert.IsFalse(
                dupCleared.hasDuplicates,
                "Duplicates should be cleared after Clear All."
            );
            Assert.IsFalse(
                nullCleared.hasNullEntries,
                "Null entries should be cleared after Clear All."
            );
            CollectionAssert.IsEmpty(dupCleared.duplicateIndices);
            CollectionAssert.IsEmpty(nullCleared.nullIndices);
            Assert.IsTrue(string.IsNullOrEmpty(dupCleared.summary));
            Assert.IsTrue(string.IsNullOrEmpty(nullCleared.summary));
        }

        [Test]
        public void MultipleClearAllCallsDoNotCauseDuplicateWarnings()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;

            Array duplicates = Array.CreateInstance(inspector.ElementType, 2);
            duplicates.SetValue(7, 0);
            duplicates.SetValue(7, 1);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();

            for (int i = 0; i < 3; i++)
            {
                inspector.ClearElements();
                inspector.SynchronizeSerializedState();
                serializedObject.Update();
                setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
                itemsProperty = setProperty.FindPropertyRelative(
                    SerializableHashSetSerializedPropertyNames.Items
                );

                SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                    setProperty,
                    itemsProperty,
                    force: true
                );

                Assert.IsFalse(
                    state.hasDuplicates,
                    $"After clear iteration {i + 1}, hasDuplicates should be false."
                );
                CollectionAssert.IsEmpty(
                    state.duplicateIndices,
                    $"After clear iteration {i + 1}, duplicateIndices should be empty."
                );
            }
        }

        [Test]
        public void ClearAllThenAddDuplicatesProperlyDetectsDuplicates()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;

            Array initialDuplicates = Array.CreateInstance(inspector.ElementType, 2);
            initialDuplicates.SetValue(10, 0);
            initialDuplicates.SetValue(10, 1);
            inspector.SetSerializedItemsSnapshot(
                initialDuplicates,
                preserveSerializedEntries: true
            );
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int itemsArraySize = itemsProperty != null ? itemsProperty.arraySize : -1;
            TestContext.WriteLine(
                $"After SetSerializedItemsSnapshot (initial): itemsProperty.arraySize={itemsArraySize}, inspector.SerializedCount={inspector.SerializedCount}, inspector.UniqueCount={inspector.UniqueCount}"
            );

            SerializableSetPropertyDrawer drawer = new();

            SerializableSetPropertyDrawer.DuplicateState initialState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);
            Assert.IsTrue(
                initialState.hasDuplicates,
                $"Initial duplicates should be detected. arraySize={itemsArraySize}, SerializedCount={inspector.SerializedCount}, UniqueCount={inspector.UniqueCount}"
            );

            inspector.ClearElements();
            inspector.SynchronizeSerializedState();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer.DuplicateState afterClear = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );
            Assert.IsFalse(afterClear.hasDuplicates, "After clear, no duplicates should exist.");

            Array newDuplicates = Array.CreateInstance(inspector.ElementType, 3);
            newDuplicates.SetValue(20, 0);
            newDuplicates.SetValue(20, 1);
            newDuplicates.SetValue(30, 2);
            inspector.SetSerializedItemsSnapshot(newDuplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer.DuplicateState afterNewDuplicates =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);

            Assert.IsTrue(
                afterNewDuplicates.hasDuplicates,
                "New duplicates should be properly detected after clear."
            );
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, afterNewDuplicates.duplicateIndices);
            StringAssert.Contains("Duplicate entry 20", afterNewDuplicates.summary);
        }

        [Test]
        public void ClearAllAfterAddAndRemoveOperationsProperlyResetsState()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;

            Array initial = Array.CreateInstance(inspector.ElementType, 2);
            initial.SetValue(100, 0);
            initial.SetValue(200, 1);
            inspector.SetSerializedItemsSnapshot(initial, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);

            Array duplicates = Array.CreateInstance(inspector.ElementType, 3);
            duplicates.SetValue(100, 0);
            duplicates.SetValue(100, 1);
            duplicates.SetValue(200, 2);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer.DuplicateState withDuplicates =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);
            Assert.IsTrue(withDuplicates.hasDuplicates, "Duplicates should be detected.");

            inspector.RemoveElement(100);
            inspector.SynchronizeSerializedState();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer.DuplicateState afterRemove =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);

            inspector.ClearElements();
            inspector.SynchronizeSerializedState();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer.DuplicateState afterClear = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsFalse(
                afterClear.hasDuplicates,
                "After clear, no duplicates should be reported."
            );
            CollectionAssert.IsEmpty(afterClear.duplicateIndices);
            Assert.IsTrue(string.IsNullOrEmpty(afterClear.summary));
        }

        [Test]
        public void ClearAllWithSingleElementSetClearsDuplicateState()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;

            Array single = Array.CreateInstance(inspector.ElementType, 1);
            single.SetValue(42, 0);
            inspector.SetSerializedItemsSnapshot(single, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState beforeClear =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);

            Assert.IsFalse(
                beforeClear.hasDuplicates,
                "Single element set should not have duplicates."
            );

            inspector.ClearElements();
            inspector.SynchronizeSerializedState();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer.DuplicateState afterClear = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsFalse(afterClear.hasDuplicates, "Empty set should have no duplicates.");
            CollectionAssert.IsEmpty(afterClear.duplicateIndices);
        }

        [Test]
        public void ClearAllRemovesDuplicateAnimationStartTimes()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;

            Array duplicates = Array.CreateInstance(inspector.ElementType, 2);
            duplicates.SetValue(5, 0);
            duplicates.SetValue(5, 1);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState initialState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);

            Assert.IsTrue(initialState.hasDuplicates);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, initialState.duplicateIndices);

            inspector.ClearElements();
            inspector.SynchronizeSerializedState();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer.DuplicateState clearedState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);

            Assert.IsFalse(clearedState.hasDuplicates);
            CollectionAssert.IsEmpty(
                clearedState.animationStartTimes,
                "Animation start times should be cleared when set is cleared."
            );
            CollectionAssert.IsEmpty(
                clearedState.primaryFlags,
                "Primary flags should be cleared when set is cleared."
            );
        }

        [Test]
        public void ClearAllOnSortedSetClearsDuplicateWarnings()
        {
            SortedSetHost host = CreateScriptableObject<SortedSetHost>();
            ISerializableSetInspector inspector = host.set;

            Array duplicates = Array.CreateInstance(inspector.ElementType, 3);
            duplicates.SetValue(15, 0);
            duplicates.SetValue(15, 1);
            duplicates.SetValue(25, 2);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SortedSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int itemsArraySize = itemsProperty != null ? itemsProperty.arraySize : -1;
            TestContext.WriteLine(
                $"After SetSerializedItemsSnapshot: itemsProperty.arraySize={itemsArraySize}, inspector.SerializedCount={inspector.SerializedCount}, inspector.UniqueCount={inspector.UniqueCount}"
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState beforeClear =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);

            Assert.IsTrue(
                beforeClear.hasDuplicates,
                $"Sorted set with duplicates should report hasDuplicates. arraySize={itemsArraySize}, SerializedCount={inspector.SerializedCount}, UniqueCount={inspector.UniqueCount}"
            );

            inspector.ClearElements();
            inspector.SynchronizeSerializedState();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(SortedSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer.DuplicateState afterClear = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsFalse(
                afterClear.hasDuplicates,
                "Cleared sorted set should have no duplicates."
            );
            CollectionAssert.IsEmpty(afterClear.duplicateIndices);
            Assert.IsTrue(string.IsNullOrEmpty(afterClear.summary));
        }

        [Test]
        public void ClearAllOnStringSetClearsDuplicateWarnings()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            ISerializableSetInspector inspector = host.set;

            Array duplicates = new string[] { "Alpha", "Alpha", "Beta" };
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int itemsArraySize = itemsProperty != null ? itemsProperty.arraySize : -1;
            TestContext.WriteLine(
                $"After SetSerializedItemsSnapshot: itemsProperty.arraySize={itemsArraySize}, inspector.SerializedCount={inspector.SerializedCount}, inspector.UniqueCount={inspector.UniqueCount}"
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState beforeClear =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);

            Assert.IsTrue(
                beforeClear.hasDuplicates,
                $"String set with duplicates should report hasDuplicates. arraySize={itemsArraySize}, SerializedCount={inspector.SerializedCount}, UniqueCount={inspector.UniqueCount}"
            );
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, beforeClear.duplicateIndices);
            StringAssert.Contains("Alpha", beforeClear.summary);

            inspector.ClearElements();
            inspector.SynchronizeSerializedState();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(StringSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer.DuplicateState afterClear = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsFalse(
                afterClear.hasDuplicates,
                "Cleared string set should have no duplicates."
            );
            CollectionAssert.IsEmpty(afterClear.duplicateIndices);
            Assert.IsTrue(string.IsNullOrEmpty(afterClear.summary));
        }

        [Test]
        public void DuplicateStateIsDirtyOnNewStateCreation()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                null,
                force: false
            );

            Assert.IsTrue(state.IsDirty, "Newly created DuplicateState should be dirty.");
        }

        [Test]
        public void DuplicateStateIsNotDirtyAfterRefresh()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array values = Array.CreateInstance(inspector.ElementType, 2);
            values.SetValue(10, 0);
            values.SetValue(20, 1);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsFalse(
                state.IsDirty,
                "DuplicateState should not be dirty after forced refresh."
            );
        }

        [Test]
        public void DuplicateStateMarkDirtySetsIsDirtyTrue()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array values = Array.CreateInstance(inspector.ElementType, 2);
            values.SetValue(10, 0);
            values.SetValue(20, 1);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );
            Assert.IsFalse(state.IsDirty, "State should not be dirty after refresh.");

            state.MarkDirty();

            Assert.IsTrue(state.IsDirty, "State should be dirty after calling MarkDirty.");
        }

        [Test]
        public void DuplicateStateIsAnimatingReturnsTrueWhenDuplicatesExistAndNotCompleted()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array duplicates = Array.CreateInstance(inspector.ElementType, 2);
            duplicates.SetValue(5, 0);
            duplicates.SetValue(5, 1);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(state.hasDuplicates, "State should have duplicates.");
            Assert.IsTrue(
                state.IsAnimating,
                "State should report IsAnimating when duplicates exist and animations not completed."
            );
        }

        [Test]
        public void DuplicateStateIsAnimatingReturnsFalseWhenNoDuplicates()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array values = Array.CreateInstance(inspector.ElementType, 2);
            values.SetValue(10, 0);
            values.SetValue(20, 1);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsFalse(state.hasDuplicates, "State should not have duplicates.");
            Assert.IsFalse(
                state.IsAnimating,
                "State should not report IsAnimating when no duplicates."
            );
        }

        [Test]
        public void DuplicateStateCheckAnimationCompletionMarksAnimationsComplete()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array duplicates = Array.CreateInstance(inspector.ElementType, 2);
            duplicates.SetValue(5, 0);
            duplicates.SetValue(5, 1);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(state.IsAnimating, "State should be animating before completion check.");

            double farFutureTime = EditorApplication.timeSinceStartup + 60.0;
            state.CheckAnimationCompletion(farFutureTime, cycleLimit: 3);

            Assert.IsFalse(
                state.IsAnimating,
                "State should not be animating after completion with far future time."
            );
        }

        [Test]
        public void DuplicateStateCheckAnimationCompletionDoesNothingWithZeroCycleLimit()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array duplicates = Array.CreateInstance(inspector.ElementType, 2);
            duplicates.SetValue(5, 0);
            duplicates.SetValue(5, 1);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(state.IsAnimating, "State should be animating.");

            double farFutureTime = EditorApplication.timeSinceStartup + 60.0;
            state.CheckAnimationCompletion(farFutureTime, cycleLimit: 0);

            Assert.IsTrue(state.IsAnimating, "State should remain animating when cycleLimit is 0.");
        }

        [Test]
        public void DuplicateStateClearAnimationTrackingResetsDirtyState()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array duplicates = Array.CreateInstance(inspector.ElementType, 2);
            duplicates.SetValue(5, 0);
            duplicates.SetValue(5, 1);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsFalse(state.IsDirty, "State should not be dirty after refresh.");
            Assert.Greater(
                state.animationStartTimes.Count,
                0,
                "Animation start times should be populated."
            );

            state.ClearAnimationTracking();

            Assert.IsTrue(state.IsDirty, "State should be dirty after ClearAnimationTracking.");
            Assert.AreEqual(
                0,
                state.animationStartTimes.Count,
                "Animation start times should be cleared."
            );
        }

        [Test]
        public void DuplicateStatePopulatesAnimationTimesOnInitialDetection()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array duplicates = Array.CreateInstance(inspector.ElementType, 3);
            duplicates.SetValue(7, 0);
            duplicates.SetValue(7, 1);
            duplicates.SetValue(9, 2);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(state.hasDuplicates, "State should have duplicates.");
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, state.duplicateIndices);
            Assert.IsTrue(
                state.animationStartTimes.ContainsKey(0),
                "Animation time for index 0 should be set."
            );
            Assert.IsTrue(
                state.animationStartTimes.ContainsKey(1),
                "Animation time for index 1 should be set."
            );
            Assert.IsFalse(
                state.animationStartTimes.ContainsKey(2),
                "Animation time for non-duplicate index 2 should not be set."
            );
        }

        [Test]
        public void NewDrawerInstanceDetectsDuplicatesOnFirstEvaluation()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array duplicates = Array.CreateInstance(inspector.ElementType, 2);
            duplicates.SetValue(42, 0);
            duplicates.SetValue(42, 1);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer1 = new();
            SerializableSetPropertyDrawer.DuplicateState state1 = drawer1.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(state1.hasDuplicates, "First drawer should detect duplicates.");
            Assert.Greater(
                state1.animationStartTimes.Count,
                0,
                "First drawer should have animation start times."
            );

            SerializableSetPropertyDrawer drawer2 = new();
            SerializableSetPropertyDrawer.DuplicateState state2 = drawer2.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(
                state2.hasDuplicates,
                "Second drawer instance should also detect duplicates."
            );
            Assert.Greater(
                state2.animationStartTimes.Count,
                0,
                "Second drawer instance should also have animation start times."
            );
        }

        [Test]
        public void DuplicateStateIsDirtyAllowsEvaluationDuringNonRepaintEvents()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array duplicates = Array.CreateInstance(inspector.ElementType, 2);
            duplicates.SetValue(99, 0);
            duplicates.SetValue(99, 1);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(
                state.hasDuplicates,
                "Duplicates should be detected even if called outside Repaint context."
            );
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, state.duplicateIndices);
            Assert.Greater(
                state.animationStartTimes.Count,
                0,
                "Animation times should be populated."
            );
        }

        [Test]
        public void DuplicateStateShouldSkipRefreshReturnsTrueForUnchangedNonDuplicateSet()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array values = Array.CreateInstance(inspector.ElementType, 2);
            values.SetValue(10, 0);
            values.SetValue(20, 1);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsFalse(state.hasDuplicates);
            Assert.IsTrue(
                state.ShouldSkipRefresh(itemsProperty.arraySize),
                "Should skip refresh for unchanged non-duplicate set."
            );
        }

        [Test]
        public void DuplicateStateShouldSkipRefreshReturnsFalseForDifferentArraySize()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array values = Array.CreateInstance(inspector.ElementType, 2);
            values.SetValue(10, 0);
            values.SetValue(20, 1);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsFalse(
                state.ShouldSkipRefresh(5),
                "Should not skip refresh when array size differs."
            );
        }

        [Test]
        public void DuplicateStateUpdateLastHadDuplicatesResetsAnimationCompletedOnTransition()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array duplicates = Array.CreateInstance(inspector.ElementType, 2);
            duplicates.SetValue(5, 0);
            duplicates.SetValue(5, 1);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(
                state.hasDuplicates,
                "State should have duplicates with [5, 5] initial data."
            );
            Assert.IsTrue(
                state.IsAnimating,
                $"State should be animating initially. hasDuplicates={state.hasDuplicates}"
            );

            double farFutureTime = EditorApplication.timeSinceStartup + 60.0;
            state.CheckAnimationCompletion(farFutureTime, cycleLimit: 3);
            Assert.IsFalse(
                state.IsAnimating,
                $"State should not be animating after completion. hasDuplicates={state.hasDuplicates}"
            );

            inspector.ClearElements();
            inspector.SynchronizeSerializedState();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            Array newDuplicates = Array.CreateInstance(inspector.ElementType, 2);
            newDuplicates.SetValue(99, 0);
            newDuplicates.SetValue(99, 1);
            inspector.SetSerializedItemsSnapshot(newDuplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer.DuplicateState newState = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(
                newState.hasDuplicates,
                "New state should have duplicates with [99, 99] data."
            );
            Assert.IsTrue(
                newState.IsAnimating,
                $"New state should be animating after new duplicates detected. hasDuplicates={newState.hasDuplicates}"
            );
        }

        [Test]
        public void MultipleEvaluationsWithSameArraySizeAndNoDuplicatesSkipRefresh()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array values = Array.CreateInstance(inspector.ElementType, 3);
            values.SetValue(1, 0);
            values.SetValue(2, 1);
            values.SetValue(3, 2);
            inspector.SetSerializedItemsSnapshot(values, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state1 = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsFalse(state1.hasDuplicates);
            Assert.IsFalse(state1.IsDirty);

            Assert.IsTrue(
                state1.ShouldSkipRefresh(3),
                "Subsequent check with same size and no duplicates should skip."
            );
        }

        [Test]
        public void DuplicateStatePreservesAnimationTimesForExistingDuplicates()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array duplicates = Array.CreateInstance(inspector.ElementType, 2);
            duplicates.SetValue(5, 0);
            duplicates.SetValue(5, 1);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state1 = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            double originalStartTime0 = state1.animationStartTimes[0];
            double originalStartTime1 = state1.animationStartTimes[1];

            SerializableSetPropertyDrawer.DuplicateState state2 = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: false
            );

            Assert.IsTrue(
                state2.animationStartTimes.ContainsKey(0),
                "Animation time for index 0 should be preserved."
            );
            Assert.IsTrue(
                state2.animationStartTimes.ContainsKey(1),
                "Animation time for index 1 should be preserved."
            );
            Assert.AreEqual(
                originalStartTime0,
                state2.animationStartTimes[0],
                "Animation start time for index 0 should not change."
            );
            Assert.AreEqual(
                originalStartTime1,
                state2.animationStartTimes[1],
                "Animation start time for index 1 should not change."
            );
        }

        [Test]
        public void DuplicateStateHandlesTransitionFromNoDuplicatesToDuplicates()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array noDuplicates = Array.CreateInstance(inspector.ElementType, 2);
            noDuplicates.SetValue(1, 0);
            noDuplicates.SetValue(2, 1);
            inspector.SetSerializedItemsSnapshot(noDuplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState stateNoDuplicates =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);

            Assert.IsFalse(stateNoDuplicates.hasDuplicates);
            Assert.AreEqual(0, stateNoDuplicates.animationStartTimes.Count);

            Array withDuplicates = Array.CreateInstance(inspector.ElementType, 2);
            withDuplicates.SetValue(1, 0);
            withDuplicates.SetValue(1, 1);
            inspector.SetSerializedItemsSnapshot(withDuplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer.DuplicateState stateWithDuplicates =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);

            Assert.IsTrue(stateWithDuplicates.hasDuplicates);
            Assert.Greater(
                stateWithDuplicates.animationStartTimes.Count,
                0,
                "Animation times should be populated on transition to duplicates."
            );
            Assert.IsTrue(
                stateWithDuplicates.IsAnimating,
                "Should be animating after duplicates detected."
            );
        }

        [Test]
        public void DuplicateStateHandlesTransitionFromDuplicatesToNoDuplicates()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array duplicates = Array.CreateInstance(inspector.ElementType, 2);
            duplicates.SetValue(5, 0);
            duplicates.SetValue(5, 1);
            inspector.SetSerializedItemsSnapshot(duplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState stateWithDuplicates =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);

            Assert.IsTrue(stateWithDuplicates.hasDuplicates);

            Array noDuplicates = Array.CreateInstance(inspector.ElementType, 2);
            noDuplicates.SetValue(5, 0);
            noDuplicates.SetValue(10, 1);
            inspector.SetSerializedItemsSnapshot(noDuplicates, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer.DuplicateState stateNoDuplicates =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);

            Assert.IsFalse(stateNoDuplicates.hasDuplicates);
            Assert.AreEqual(
                0,
                stateNoDuplicates.animationStartTimes.Count,
                "Animation times should be cleared when no duplicates."
            );
        }

        [Test]
        public void DuplicateStateRemovesAnimationTimesForResolvedDuplicates()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array twoGroups = Array.CreateInstance(inspector.ElementType, 4);
            twoGroups.SetValue(5, 0);
            twoGroups.SetValue(5, 1);
            twoGroups.SetValue(7, 2);
            twoGroups.SetValue(7, 3);
            inspector.SetSerializedItemsSnapshot(twoGroups, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState initialState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);

            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, initialState.duplicateIndices);
            Assert.AreEqual(4, initialState.animationStartTimes.Count);

            Array oneGroup = Array.CreateInstance(inspector.ElementType, 4);
            oneGroup.SetValue(5, 0);
            oneGroup.SetValue(5, 1);
            oneGroup.SetValue(7, 2);
            oneGroup.SetValue(8, 3);
            inspector.SetSerializedItemsSnapshot(oneGroup, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();
            serializedObject.Update();
            setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer.DuplicateState updatedState =
                drawer.EvaluateDuplicateState(setProperty, itemsProperty, force: true);

            CollectionAssert.AreEquivalent(new[] { 0, 1 }, updatedState.duplicateIndices);
            Assert.AreEqual(2, updatedState.animationStartTimes.Count);
            Assert.IsTrue(updatedState.animationStartTimes.ContainsKey(0));
            Assert.IsTrue(updatedState.animationStartTimes.ContainsKey(1));
            Assert.IsFalse(updatedState.animationStartTimes.ContainsKey(2));
            Assert.IsFalse(updatedState.animationStartTimes.ContainsKey(3));
        }

        [Test]
        public void DuplicateStateHandlesNullItemsProperty()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                null,
                force: true
            );

            Assert.IsFalse(state.hasDuplicates);
            Assert.AreEqual(0, state.duplicateIndices.Count);
            Assert.AreEqual(0, state.animationStartTimes.Count);
        }

        [Test]
        public void DuplicateStateHandlesSingleItemArray()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array singleItem = Array.CreateInstance(inspector.ElementType, 1);
            singleItem.SetValue(42, 0);
            inspector.SetSerializedItemsSnapshot(singleItem, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsFalse(state.hasDuplicates, "Single item set cannot have duplicates.");
            Assert.AreEqual(0, state.duplicateIndices.Count);
            Assert.AreEqual(0, state.animationStartTimes.Count);
        }

        [Test]
        public void DuplicateStateHandlesEmptyArray()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            inspector.ClearElements();
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsFalse(state.hasDuplicates, "Empty set cannot have duplicates.");
            Assert.AreEqual(0, state.duplicateIndices.Count);
            Assert.AreEqual(0, state.animationStartTimes.Count);
        }

        [Test]
        public void DuplicateStateWithStringSetDetectsDuplicates()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array duplicateStrings = new string[] { "hello", "world", "hello" };
            inspector.SetSerializedItemsSnapshot(duplicateStrings, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(state.hasDuplicates);
            CollectionAssert.AreEquivalent(new[] { 0, 2 }, state.duplicateIndices);
            Assert.IsTrue(state.IsAnimating);
            Assert.Greater(state.animationStartTimes.Count, 0);
        }

        [Test]
        public void MultipleDuplicateGroupsAllGetAnimationTimes()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            ISerializableSetInspector inspector = host.set;
            Array multipleGroups = Array.CreateInstance(inspector.ElementType, 6);
            multipleGroups.SetValue(1, 0);
            multipleGroups.SetValue(1, 1);
            multipleGroups.SetValue(2, 2);
            multipleGroups.SetValue(2, 3);
            multipleGroups.SetValue(3, 4);
            multipleGroups.SetValue(3, 5);
            inspector.SetSerializedItemsSnapshot(multipleGroups, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(state.hasDuplicates);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5 }, state.duplicateIndices);
            Assert.AreEqual(
                6,
                state.animationStartTimes.Count,
                "All duplicate indices should have animation times."
            );
            for (int i = 0; i < 6; i++)
            {
                Assert.IsTrue(
                    state.animationStartTimes.ContainsKey(i),
                    $"Index {i} should have animation start time."
                );
            }
        }
    }
}
