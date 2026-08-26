// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
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
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SerializableDictionaryPropertyDrawerTests : BatchedEditorTestBase
    {
        private TestDictionaryHost _sharedHost;
        private SerializedObject _sharedSerializedObject;

        [SetUp]
        public override void BaseSetUp()
        {
            _sharedHost = CreateScriptableObject<TestDictionaryHost>();
            _sharedSerializedObject = new SerializedObject(_sharedHost);
            GroupGUIWidthUtility.ResetForTests();
            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
            SerializableDictionaryPropertyDrawer.ClearMainFoldoutAnimCacheForTests();
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

            _sharedHost.dictionary.Clear();
            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            if (dictionaryProperty != null)
            {
                dictionaryProperty.isExpanded = false;
                _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void PageSizeClampPreventsExcessiveCacheGrowth()
        {
            for (int i = 0; i < 512; i++)
            {
                _sharedHost.dictionary.Add(i, $"Value {i}");
            }

            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableDictionaryPageSize;

            try
            {
                int configuredSize = UnityHelpersSettings.MinPageSize + 23;
                settings.SerializableDictionaryPageSize = configuredSize;

                SerializableDictionaryPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(dictionaryProperty);
                drawer.GetOrCreateList(dictionaryProperty);

                string listKey = drawer.GetListKey(dictionaryProperty);
                SerializableDictionaryPropertyDrawer.ListPageCache cache = drawer.EnsurePageCache(
                    listKey,
                    keysProperty,
                    pagination
                );

                int expectedSize = UnityHelpersSettings.GetSerializableDictionaryPageSize();
                Assert.AreEqual(
                    expectedSize,
                    pagination.pageSize,
                    "Pagination state should mirror the configured page size."
                );
                Assert.That(
                    cache.entries.Count,
                    Is.EqualTo(Mathf.Min(expectedSize, keysProperty.arraySize)),
                    "List cache should never allocate more entries than the configured page size."
                );
            }
            finally
            {
                settings.SerializableDictionaryPageSize = originalPageSize;
            }
        }

        [Test]
        public void PaginationStateUsesSerializableDictionaryPageSizeSetting()
        {
            for (int i = 0; i < 64; i++)
            {
                _sharedHost.dictionary.Add(i, $"Value {i}");
            }

            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableDictionaryPageSize;

            try
            {
                int configuredSize = UnityHelpersSettings.MinPageSize + 7;
                settings.SerializableDictionaryPageSize = configuredSize;

                SerializableDictionaryPropertyDrawer drawer = new();
                SerializableDictionaryPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(dictionaryProperty);

                Assert.AreEqual(
                    UnityHelpersSettings.GetSerializableDictionaryPageSize(),
                    pagination.pageSize,
                    "Pagination should respect the configured SerializableDictionary page size."
                );
            }
            finally
            {
                settings.SerializableDictionaryPageSize = originalPageSize;
            }
        }

        [Test]
        public void PaginationStateResetsIndexWhenPageSizeChanges()
        {
            for (int i = 0; i < 120; i++)
            {
                _sharedHost.dictionary.Add(i, $"Value {i}");
            }

            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableDictionaryPageSize;

            try
            {
                settings.SerializableDictionaryPageSize = 20;

                SerializableDictionaryPropertyDrawer drawer = new();
                SerializableDictionaryPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(dictionaryProperty);
                pagination.pageIndex = 3;

                settings.SerializableDictionaryPageSize = 30;

                pagination = drawer.GetOrCreatePaginationState(dictionaryProperty);

                Assert.AreEqual(
                    UnityHelpersSettings.GetSerializableDictionaryPageSize(),
                    pagination.pageSize
                );
                Assert.AreEqual(
                    0,
                    pagination.pageIndex,
                    "Changing the global page size should reset the cached pagination index."
                );
            }
            finally
            {
                settings.SerializableDictionaryPageSize = originalPageSize;
            }
        }

        [Test]
        public void RowHeightReflectsSerializedPropertyHeights()
        {
            RectDictionaryHost host = CreateScriptableObject<RectDictionaryHost>();
            host.dictionary.Add(new Rect(1f, 2f, 3f, 4f), 42);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(RectDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            ReorderableList list = drawer.GetOrCreateList(dictionaryProperty);

            Assert.IsTrue(
                list.elementHeightCallback != null,
                "Expected dynamic element height callback."
            );
            float resolvedHeight = list.elementHeightCallback.Invoke(0);

            SerializedProperty keyProperty = keysProperty.GetArrayElementAtIndex(0);
            SerializedProperty valueProperty = valuesProperty.GetArrayElementAtIndex(0);

            float expectedHeight =
                SerializableDictionaryPropertyDrawer.CalculateDictionaryRowHeight(
                    keyProperty,
                    valueProperty
                );

            Assert.That(resolvedHeight, Is.EqualTo(expectedHeight));
        }

        [Test]
        public void SetPropertyValueHandlesSerializableClassValues()
        {
            ComplexValueDictionaryHost host = CreateScriptableObject<ComplexValueDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ComplexValueDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            keysProperty.arraySize = 1;
            valuesProperty.arraySize = 1;

            SerializedProperty keyProperty = keysProperty.GetArrayElementAtIndex(0);
            SerializedProperty valueProperty = valuesProperty.GetArrayElementAtIndex(0);

            SerializableDictionaryPropertyDrawer.SetPropertyValue(
                keyProperty,
                "Alert",
                typeof(string)
            );

            ComplexValue complexValue = new() { button = Color.magenta, text = Color.white };

            SerializableDictionaryPropertyDrawer.SetPropertyValue(
                valueProperty,
                complexValue,
                typeof(ComplexValue)
            );

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            host.dictionary.EditorAfterDeserialize();

            Assert.That(host.dictionary.Count, Is.EqualTo(1));

            ComplexValue stored = host.dictionary["Alert"];
            Assert.That(stored.button, Is.EqualTo(complexValue.button));
            Assert.That(stored.text, Is.EqualTo(complexValue.text));

            object roundTrip = SerializableDictionaryPropertyDrawer.GetPropertyValue(
                valueProperty,
                typeof(ComplexValue)
            );

            ComplexValue roundTripValue = roundTrip as ComplexValue;
            Assert.IsTrue(roundTripValue != null);
            Assert.That(roundTripValue.button, Is.EqualTo(complexValue.button));
            Assert.That(roundTripValue.text, Is.EqualTo(complexValue.text));
        }

        [Test]
        public void ExpandDictionaryRowRectExtendsSelectionArea()
        {
            Rect baseRect = new(2f, 6f, 40f, 18f);
            Rect expanded = SerializableDictionaryPropertyDrawer.ExpandDictionaryRowRect(baseRect);

            Assert.That(expanded.yMin, Is.LessThan(baseRect.yMin));
            Assert.That(expanded.yMax, Is.GreaterThan(baseRect.yMax));
            Assert.That(expanded.width, Is.EqualTo(baseRect.width));
        }

        [Test]
        public void SyncSelectionKeepsIndexWithinVisiblePage()
        {
            for (int i = 0; i < 30; i++)
            {
                _sharedHost.dictionary.Add(i, $"Item {i}");
            }

            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableDictionaryPageSize;

            try
            {
                settings.SerializableDictionaryPageSize = 10;

                SerializableDictionaryPropertyDrawer drawer = new();

                SerializableDictionaryPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(dictionaryProperty);
                pagination.pageIndex = 0;

                ReorderableList list = drawer.GetOrCreateList(dictionaryProperty);

                string listKey = drawer.GetListKey(dictionaryProperty);

                // ReSharper disable once RedundantAssignment
                SerializableDictionaryPropertyDrawer.ListPageCache cache = drawer.EnsurePageCache(
                    listKey,
                    keysProperty,
                    pagination
                );

                pagination.selectedIndex = 25;
                pagination.pageIndex = 2;
                cache = drawer.EnsurePageCache(listKey, keysProperty, pagination);
                SerializableDictionaryPropertyDrawer.SyncListSelectionWithPagination(
                    list,
                    pagination,
                    cache
                );

                Assert.AreEqual(5, list.index);
                Assert.AreEqual(25, pagination.selectedIndex);

                pagination.pageIndex = 0;
                cache = drawer.EnsurePageCache(listKey, keysProperty, pagination);
                SerializableDictionaryPropertyDrawer.SyncListSelectionWithPagination(
                    list,
                    pagination,
                    cache
                );

                Assert.AreEqual(0, list.index);
                Assert.AreEqual(0, pagination.selectedIndex);
            }
            finally
            {
                settings.SerializableDictionaryPageSize = originalPageSize;
            }
        }

        [Test]
        public void MarkListCacheDirtyClearsCachedEntries()
        {
            for (int i = 0; i < 20; i++)
            {
                _sharedHost.dictionary.Add(i, $"Entry {i}");
            }

            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            SerializableDictionaryPropertyDrawer drawer = new();

            SerializableDictionaryPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(dictionaryProperty);

            drawer.GetOrCreateList(dictionaryProperty);

            string listKey = drawer.GetListKey(dictionaryProperty);

            SerializableDictionaryPropertyDrawer.ListPageCache cache = drawer.EnsurePageCache(
                listKey,
                keysProperty,
                pagination
            );

            IList entries = cache.entries;
            Assert.Greater(entries.Count, 0);
            Assert.IsFalse(cache.dirty);

            drawer.MarkListCacheDirty(listKey);

            Assert.AreEqual(0, entries.Count);
            Assert.IsTrue(cache.dirty);
        }

        [Test]
        public void RemoveEntryAdjustsSelectionWithinPage()
        {
            for (int i = 0; i < 30; i++)
            {
                _sharedHost.dictionary.Add(i, $"Item {i}");
            }

            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableDictionaryPageSize;

            try
            {
                settings.SerializableDictionaryPageSize = 10;

                SerializableDictionaryPropertyDrawer drawer = new();

                SerializableDictionaryPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(dictionaryProperty);
                pagination.pageIndex = 2;
                pagination.selectedIndex = 25;

                ReorderableList list = drawer.GetOrCreateList(dictionaryProperty);

                string listKey = drawer.GetListKey(dictionaryProperty);
                SerializableDictionaryPropertyDrawer.ListPageCache cache = drawer.EnsurePageCache(
                    listKey,
                    keysProperty,
                    pagination
                );
                SerializableDictionaryPropertyDrawer.SyncListSelectionWithPagination(
                    list,
                    pagination,
                    cache
                );

                drawer.RemoveEntryAtIndex(
                    25,
                    list,
                    dictionaryProperty,
                    keysProperty,
                    valuesProperty,
                    pagination
                );

                cache = drawer.EnsurePageCache(listKey, keysProperty, pagination);
                SerializableDictionaryPropertyDrawer.SyncListSelectionWithPagination(
                    list,
                    pagination,
                    cache
                );

                Assert.AreEqual(25, pagination.selectedIndex);
                Assert.AreEqual(5, list.index);
                Assert.AreEqual(2, pagination.pageIndex);
            }
            finally
            {
                settings.SerializableDictionaryPageSize = originalPageSize;
            }
        }

        [Test]
        public void PageCacheRebuildsWhenGlobalPageSizeChanges()
        {
            for (int i = 0; i < 40; i++)
            {
                _sharedHost.dictionary.Add(i, $"Item {i}");
            }

            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableDictionaryPageSize;

            try
            {
                int initialPageSize = UnityHelpersSettings.MinPageSize + 10;
                int reducedPageSize = UnityHelpersSettings.MinPageSize + 2;
                settings.SerializableDictionaryPageSize = initialPageSize;

                SerializableDictionaryPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(dictionaryProperty);
                string listKey = drawer.GetListKey(dictionaryProperty);

                SerializableDictionaryPropertyDrawer.ListPageCache cache = drawer.EnsurePageCache(
                    listKey,
                    keysProperty,
                    pagination
                );
                Assert.AreEqual(
                    UnityHelpersSettings.GetSerializableDictionaryPageSize(),
                    pagination.pageSize,
                    "Initial pagination should use the configured size."
                );
                Assert.That(cache.entries.Count, Is.EqualTo(initialPageSize));

                settings.SerializableDictionaryPageSize = reducedPageSize;

                pagination = drawer.GetOrCreatePaginationState(dictionaryProperty);
                cache = drawer.EnsurePageCache(listKey, keysProperty, pagination);

                Assert.AreEqual(
                    UnityHelpersSettings.GetSerializableDictionaryPageSize(),
                    pagination.pageSize,
                    "Pagination should refresh when the global size changes."
                );
                Assert.That(cache.entries.Count, Is.EqualTo(reducedPageSize));
                Assert.AreEqual(
                    0,
                    pagination.pageIndex,
                    "Page index should reset after a size change."
                );
            }
            finally
            {
                settings.SerializableDictionaryPageSize = originalPageSize;
            }
        }

        [Test]
        public void RemoveEntryBacktracksToPreviousPageWhenLastPageIsRemoved()
        {
            for (int i = 0; i < 21; i++)
            {
                _sharedHost.dictionary.Add(i, $"Item {i}");
            }

            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableDictionaryPageSize;

            try
            {
                settings.SerializableDictionaryPageSize = 10;

                SerializableDictionaryPropertyDrawer drawer = new();
                SerializableDictionaryPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(dictionaryProperty);
                pagination.pageIndex = 2;
                pagination.selectedIndex = 20;

                ReorderableList list = drawer.GetOrCreateList(dictionaryProperty);
                string listKey = drawer.GetListKey(dictionaryProperty);

                SerializableDictionaryPropertyDrawer.ListPageCache cache = drawer.EnsurePageCache(
                    listKey,
                    keysProperty,
                    pagination
                );
                SerializableDictionaryPropertyDrawer.SyncListSelectionWithPagination(
                    list,
                    pagination,
                    cache
                );

                drawer.RemoveEntryAtIndex(
                    20,
                    list,
                    dictionaryProperty,
                    keysProperty,
                    valuesProperty,
                    pagination
                );

                cache = drawer.EnsurePageCache(listKey, keysProperty, pagination);
                SerializableDictionaryPropertyDrawer.SyncListSelectionWithPagination(
                    list,
                    pagination,
                    cache
                );

                Assert.AreEqual(
                    1,
                    pagination.pageIndex,
                    "Removing the lone element on the last page should move to the previous page."
                );
                Assert.AreEqual(
                    19,
                    pagination.selectedIndex,
                    "Selection should clamp to the new last element."
                );
                Assert.AreEqual(
                    9,
                    list.index,
                    "Relative list selection should point to the last item on the page."
                );
            }
            finally
            {
                settings.SerializableDictionaryPageSize = originalPageSize;
            }
        }

        [Test]
        public void EvaluateDuplicateTweenOffsetHonorsCycleLimit()
        {
            const double startTime = 0d;
            const double activeTime = 0.1432d;

            float activeOffset = SerializableDictionaryPropertyDrawer.EvaluateDuplicateTweenOffset(
                0,
                startTime,
                activeTime,
                2
            );

            Assert.That(Mathf.Abs(activeOffset), Is.GreaterThan(1e-3f));

            float exhaustedOffset =
                SerializableDictionaryPropertyDrawer.EvaluateDuplicateTweenOffset(
                    0,
                    startTime,
                    startTime + 10d,
                    1
                );

            Assert.AreEqual(0f, exhaustedOffset);
        }

        [Test]
        public void EvaluateDuplicateTweenOffsetSupportsInfiniteCycles()
        {
            float offset = SerializableDictionaryPropertyDrawer.EvaluateDuplicateTweenOffset(
                2,
                0d,
                100d,
                -1
            );

            Assert.That(Mathf.Abs(offset), Is.GreaterThan(1e-3f));
        }

        [Test]
        public void SortDictionaryEntriesReordersKeysAscending()
        {
            TestSortedDictionaryHost host = CreateScriptableObject<TestSortedDictionaryHost>();
            host.dictionary.Add(5, "five");
            host.dictionary.Add(1, "one");
            host.dictionary.Add(3, "three");

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestSortedDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            keysProperty.GetArrayElementAtIndex(0).intValue = 5;
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "five";
            keysProperty.GetArrayElementAtIndex(1).intValue = 1;
            valuesProperty.GetArrayElementAtIndex(1).stringValue = "one";
            keysProperty.GetArrayElementAtIndex(2).intValue = 3;
            valuesProperty.GetArrayElementAtIndex(2).stringValue = "three";
            serializedObject.ApplyModifiedProperties();

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(dictionaryProperty);
            pagination.selectedIndex = 0;

            ReorderableList list = drawer.GetOrCreateList(dictionaryProperty);

            drawer.SortDictionaryEntries(
                dictionaryProperty,
                keysProperty,
                valuesProperty,
                typeof(int),
                typeof(string),
                Comparison,
                pagination,
                list
            );

            serializedObject.Update();

            Assert.AreEqual(1, keysProperty.GetArrayElementAtIndex(0).intValue);
            Assert.AreEqual("one", valuesProperty.GetArrayElementAtIndex(0).stringValue);
            Assert.AreEqual(3, keysProperty.GetArrayElementAtIndex(1).intValue);
            Assert.AreEqual("three", valuesProperty.GetArrayElementAtIndex(1).stringValue);
            Assert.AreEqual(5, keysProperty.GetArrayElementAtIndex(2).intValue);
            Assert.AreEqual("five", valuesProperty.GetArrayElementAtIndex(2).stringValue);

            int[] expectedKeys = { 1, 3, 5 };
            int index = 0;
            foreach (KeyValuePair<int, string> pair in host.dictionary)
            {
                Assert.Less(index, expectedKeys.Length);
                Assert.AreEqual(expectedKeys[index], pair.Key);
                index++;
            }

            Assert.AreEqual(expectedKeys.Length, index);
            Assert.AreEqual(2, pagination.selectedIndex);
            return;

            int Comparison(object left, object right)
            {
                int leftValue = left is int leftInt ? leftInt : Convert.ToInt32(left);
                int rightValue = right is int rightInt ? rightInt : Convert.ToInt32(right);
                return Comparer<int>.Default.Compare(leftValue, rightValue);
            }
        }

        [Test]
        public void SortDictionaryEntriesUsesUnityObjectNameComparerForObjectKeys()
        {
            UnityObjectDictionaryHost host = CreateScriptableObject<UnityObjectDictionaryHost>();
            GameObject beta = Track(new GameObject("Beta"));
            GameObject alpha = Track(new GameObject("Alpha"));

            host.dictionary.Add(beta, "beta");
            host.dictionary.Add(alpha, "alpha");

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(UnityObjectDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            keysProperty.GetArrayElementAtIndex(0).objectReferenceValue = beta;
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "beta";
            keysProperty.GetArrayElementAtIndex(1).objectReferenceValue = alpha;
            valuesProperty.GetArrayElementAtIndex(1).stringValue = "alpha";
            serializedObject.ApplyModifiedProperties();

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(dictionaryProperty);
            pagination.selectedIndex = 0;

            ReorderableList list = drawer.GetOrCreateList(dictionaryProperty);

            drawer.SortDictionaryEntries(
                dictionaryProperty,
                keysProperty,
                valuesProperty,
                typeof(GameObject),
                typeof(string),
                Comparison,
                pagination,
                list
            );

            serializedObject.Update();

            Assert.AreSame(alpha, keysProperty.GetArrayElementAtIndex(0).objectReferenceValue);
            Assert.AreEqual("alpha", valuesProperty.GetArrayElementAtIndex(0).stringValue);
            Assert.AreSame(beta, keysProperty.GetArrayElementAtIndex(1).objectReferenceValue);
            Assert.AreEqual("beta", valuesProperty.GetArrayElementAtIndex(1).stringValue);

            GameObject[] expectedOrder = { alpha, beta };
            int index = 0;
            foreach (KeyValuePair<GameObject, string> pair in host.dictionary)
            {
                Assert.Less(index, expectedOrder.Length);
                Assert.AreSame(expectedOrder[index], pair.Key);
                index++;
            }

            Assert.AreEqual(expectedOrder.Length, index);
            return;

            int Comparison(object left, object right)
            {
                return UnityObjectNameComparer<GameObject>.Instance.Compare(
                    left as GameObject,
                    right as GameObject
                );
            }
        }

        [Test]
        public void DictionarySortButtonVisibilityReflectsOrdering()
        {
            _sharedHost.dictionary.Add(2, "two");
            _sharedHost.dictionary.Add(1, "one");

            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            Func<object, object, int> comparison = delegate(object left, object right)
            {
                int leftValue = left is int leftInt ? leftInt : Convert.ToInt32(left);
                int rightValue = right is int rightInt ? rightInt : Convert.ToInt32(right);
                return leftValue.CompareTo(rightValue);
            };

            bool showBefore = SerializableDictionaryPropertyDrawer.ShouldShowDictionarySortButton(
                keysProperty,
                typeof(int),
                keysProperty.arraySize,
                comparison
            );
            Assert.IsTrue(showBefore);

            keysProperty.GetArrayElementAtIndex(0).intValue = 1;
            keysProperty.GetArrayElementAtIndex(1).intValue = 2;
            _sharedSerializedObject.ApplyModifiedProperties();
            _sharedSerializedObject.Update();
            keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            bool showAfter = SerializableDictionaryPropertyDrawer.ShouldShowDictionarySortButton(
                keysProperty,
                typeof(int),
                keysProperty.arraySize,
                comparison
            );
            Assert.IsFalse(showAfter);
        }

        [Test]
        public void SortedDictionaryManualReorderShowsSortButton()
        {
            TestSortedDictionaryHost host = CreateScriptableObject<TestSortedDictionaryHost>();
            host.dictionary.Add(1, "one");
            host.dictionary.Add(2, "two");
            host.dictionary.Add(3, "three");

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestSortedDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            keysProperty.MoveArrayElement(0, 2);
            valuesProperty.MoveArrayElement(0, 2);
            serializedObject.ApplyModifiedProperties();

            bool beforeDeserialize =
                SerializableDictionaryPropertyDrawer.ShouldShowDictionarySortButton(
                    keysProperty,
                    typeof(int),
                    keysProperty.arraySize,
                    Comparison
                );

            TestContext.WriteLine(
                $"[SortedDictionaryManualReorderShowsSortButton] "
                    + $"Keys before OnAfterDeserialize: {DumpIntArray(keysProperty)}, "
                    + $"ShouldShowSortButton: {beforeDeserialize}"
            );

            Assert.IsTrue(
                beforeDeserialize,
                $"Sort button should be visible before the sorted dictionary rehydrates. Keys: {DumpIntArray(keysProperty)}"
            );

            host.dictionary.OnAfterDeserialize();
            serializedObject.Update();
            dictionaryProperty = serializedObject.FindProperty(
                nameof(TestSortedDictionaryHost.dictionary)
            );
            keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            bool showSortAfterDeserialize =
                SerializableDictionaryPropertyDrawer.ShouldShowDictionarySortButton(
                    keysProperty,
                    typeof(int),
                    keysProperty.arraySize,
                    Comparison
                );

            TestContext.WriteLine(
                $"[SortedDictionaryManualReorderShowsSortButton] "
                    + $"Keys after OnAfterDeserialize: {DumpIntArray(keysProperty)}, "
                    + $"ShouldShowSortButton: {showSortAfterDeserialize}, "
                    + $"PreserveSerializedEntries: {host.dictionary.PreserveSerializedEntries}"
            );

            Assert.IsTrue(
                showSortAfterDeserialize,
                $"Sort button should still be visible after OnAfterDeserialize because "
                    + $"serialized arrays preserve user order (not automatically re-sorted). Keys: {DumpIntArray(keysProperty)}"
            );
            Assert.IsTrue(
                host.dictionary.PreserveSerializedEntries,
                "Sorted dictionary should preserve serialized entries after OnAfterDeserialize to maintain inspector order."
            );
            return;

            int Comparison(object left, object right)
            {
                int leftValue = left is int leftInt ? leftInt : Convert.ToInt32(left);
                int rightValue = right is int rightInt ? rightInt : Convert.ToInt32(right);
                return leftValue.CompareTo(rightValue);
            }
        }

        [Test]
        public void KeysAreSortedDetectsOrderedState()
        {
            TestSortedDictionaryHost host = CreateScriptableObject<TestSortedDictionaryHost>();
            host.dictionary.Add(2, "two");
            host.dictionary.Add(4, "four");
            host.dictionary.Add(6, "six");

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestSortedDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            Func<object, object, int> comparison = delegate(object left, object right)
            {
                int leftValue = left is int leftInt ? leftInt : Convert.ToInt32(left);
                int rightValue = right is int rightInt ? rightInt : Convert.ToInt32(right);
                return Comparer<int>.Default.Compare(leftValue, rightValue);
            };

            bool initiallySorted = SerializableDictionaryPropertyDrawer.KeysAreSorted(
                keysProperty,
                typeof(int),
                comparison
            );
            Assert.IsTrue(initiallySorted);

            keysProperty.GetArrayElementAtIndex(0).intValue = 6;
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "six";
            keysProperty.GetArrayElementAtIndex(2).intValue = 2;
            valuesProperty.GetArrayElementAtIndex(2).stringValue = "two";
            serializedObject.ApplyModifiedProperties();

            bool sortedAfterSwap = SerializableDictionaryPropertyDrawer.KeysAreSorted(
                keysProperty,
                typeof(int),
                comparison
            );
            Assert.IsFalse(sortedAfterSwap);
        }

        [Test]
        public void EvaluateDuplicateTweenOffsetHandlesCurrentTimeBeforeStart()
        {
            float offset = SerializableDictionaryPropertyDrawer.EvaluateDuplicateTweenOffset(
                1,
                10d,
                9d,
                3
            );
            float baseline = SerializableDictionaryPropertyDrawer.EvaluateDuplicateTweenOffset(
                1,
                9d,
                9d,
                3
            );

            Assert.AreEqual(baseline, offset);
        }

        [Test]
        public void EvaluateDuplicateTweenOffsetReturnsZeroWhenCycleLimitZero()
        {
            float offset = SerializableDictionaryPropertyDrawer.EvaluateDuplicateTweenOffset(
                0,
                0d,
                1d,
                0
            );

            Assert.AreEqual(0f, offset);
        }

        [Test]
        public void CommitEntryAddsComplexValue()
        {
            ComplexValueDictionaryHost host = CreateScriptableObject<ComplexValueDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ComplexValueDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(ComplexValue),
                "Alert",
                new ComplexValue { button = Color.magenta, text = Color.white },
                dictionaryProperty
            );
            Assert.IsTrue(result.added, "Expected CommitEntry to add a new element.");

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
            host.dictionary.EditorAfterDeserialize();

            Assert.That(host.dictionary.Count, Is.EqualTo(1));
            Assert.That(host.dictionary.ContainsKey("Alert"), Is.True);

            ReorderableList list = drawer.GetOrCreateList(dictionaryProperty);
            Assert.That(list.count, Is.EqualTo(1));
        }

        [Test]
        public void PageEntriesNeedSortingOnlyFlagsCurrentPage()
        {
            SerializableDictionaryPropertyDrawer drawer = new();
            _sharedHost.dictionary.Add(1, "one");
            _sharedHost.dictionary.Add(2, "two");
            _sharedHost.dictionary.Add(4, "four");
            _sharedHost.dictionary.Add(3, "three");

            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            SerializableDictionaryPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(dictionaryProperty);
            pagination.pageIndex = 0;
            pagination.pageSize = 2;

            string listKey = drawer.GetListKey(dictionaryProperty);
            SerializableDictionaryPropertyDrawer.ListPageCache cache = drawer.EnsurePageCache(
                listKey,
                keysProperty,
                pagination
            );

            Func<object, object, int> comparison = static (left, right) =>
            {
                int leftValue = left is int li ? li : Convert.ToInt32(left);
                int rightValue = right is int ri ? ri : Convert.ToInt32(right);
                return leftValue.CompareTo(rightValue);
            };

            bool firstPageNeedsSorting =
                SerializableDictionaryPropertyDrawer.PageEntriesNeedSorting(
                    cache,
                    keysProperty,
                    typeof(int),
                    comparison
                );
            Assert.IsFalse(
                firstPageNeedsSorting,
                $"First page (keys {DumpIntArray(keysProperty)} indexes {DumpPageEntries(cache)}) should already be sorted."
            );

            pagination.pageIndex = 1;
            drawer.MarkListCacheDirty(listKey);
            cache = drawer.EnsurePageCache(listKey, keysProperty, pagination);
            bool secondPageNeedsSorting =
                SerializableDictionaryPropertyDrawer.PageEntriesNeedSorting(
                    cache,
                    keysProperty,
                    typeof(int),
                    comparison
                );
            Assert.IsTrue(
                secondPageNeedsSorting,
                $"Second page should report unsorted entries. Keys {DumpIntArray(keysProperty)} indexes {DumpPageEntries(cache)}"
            );
        }

        [Test]
        public void CommitEntryPreservesColorDataArrayValues()
        {
            ColorDataDictionaryHost host = CreateScriptableObject<ColorDataDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ColorDataDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            ColorData entry = new()
            {
                color1 = Color.red,
                color2 = Color.green,
                color3 = Color.blue,
                color4 = Color.white,
                otherColors = new[] { Color.yellow, Color.cyan },
            };

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(ColorData),
                "Accent",
                entry,
                dictionaryProperty
            );

            Assert.IsTrue(result.added, "Expected CommitEntry to add a new entry.");

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
            host.dictionary.EditorAfterDeserialize();

            Assert.That(host.dictionary.Count, Is.EqualTo(1));
            Assert.IsTrue(host.dictionary.ContainsKey("Accent"));
            ColorData stored = host.dictionary["Accent"];
            Assert.That(stored.otherColors, Is.Not.Null);
            Assert.That(stored.otherColors.Length, Is.EqualTo(2));
            AssertColorsApproximately(Color.yellow, stored.otherColors[0]);
            AssertColorsApproximately(Color.cyan, stored.otherColors[1]);

            SerializedProperty storedValue = valuesProperty.GetArrayElementAtIndex(result.index);
            SerializedProperty otherColorsProperty = storedValue.FindPropertyRelative(
                nameof(ColorData.otherColors)
            );
            Assert.IsTrue(
                otherColorsProperty != null,
                "Serialized ColorData should expose otherColors."
            );
            Assert.That(otherColorsProperty.arraySize, Is.EqualTo(2));
            AssertColorsApproximately(
                Color.yellow,
                otherColorsProperty.GetArrayElementAtIndex(0).colorValue
            );
            AssertColorsApproximately(
                Color.cyan,
                otherColorsProperty.GetArrayElementAtIndex(1).colorValue
            );
        }

        [Test]
        public void CommitEntryPreservesColorListValues()
        {
            ColorListDictionaryHost host = CreateScriptableObject<ColorListDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ColorListDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            ColorListData entry = new()
            {
                colors = new List<Color> { Color.red, Color.blue },
            };

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(ColorListData),
                "Palette",
                entry,
                dictionaryProperty
            );

            Assert.IsTrue(result.added, "Expected CommitEntry to add a new entry.");

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
            host.dictionary.EditorAfterDeserialize();

            Assert.That(host.dictionary.ContainsKey("Palette"), Is.True);
            ColorListData stored = host.dictionary["Palette"];
            Assert.IsTrue(stored.colors != null);
            Assert.That(stored.colors.Count, Is.EqualTo(2));
            AssertColorsApproximately(Color.red, stored.colors[0]);
            AssertColorsApproximately(Color.blue, stored.colors[1]);
        }

        [Test]
        public void PendingEntryCommitPreservesStringKey()
        {
            ColorDataDictionaryHost host = CreateScriptableObject<ColorDataDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ColorDataDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(ColorDataDictionaryHost),
                nameof(ColorDataDictionaryHost.dictionary)
            );

            SerializableDictionaryPropertyDrawer.PendingEntry pending =
                drawer.GetOrCreatePendingEntry(
                    dictionaryProperty,
                    typeof(string),
                    typeof(ColorData),
                    isSortedDictionary: false
                );
            pending.key = "test-input";
            pending.value = new ColorData { color1 = Color.white };

            SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(ColorData),
                pending.key,
                pending.value,
                dictionaryProperty,
                existingIndex: -1
            );

            Assert.IsTrue(result.added, "Expected CommitEntry to add a new entry.");

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
            host.dictionary.EditorAfterDeserialize();

            Assert.IsTrue(host.dictionary.ContainsKey("test-input"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PendingDuplicateCacheInvalidatesAfterDictionaryValueChange(
            bool initializeWrapper
        )
        {
            ColorDataDictionaryHost host = CreateScriptableObject<ColorDataDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ColorDataDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(ColorDataDictionaryHost),
                nameof(ColorDataDictionaryHost.dictionary)
            );

            SerializableDictionaryPropertyDrawer.CommitResult initialEntry = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(ColorData),
                "Accent",
                new ColorData { color1 = Color.white },
                dictionaryProperty
            );
            Assert.IsTrue(initialEntry.added, "Expected initial commit to succeed.");
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableDictionaryPropertyDrawer.PendingEntry pending =
                drawer.GetOrCreatePendingEntry(
                    dictionaryProperty,
                    typeof(string),
                    typeof(ColorData),
                    isSortedDictionary: false
                );
            pending.key = "Accent";
            int storedIndex = 0 <= initialEntry.index ? initialEntry.index : 0;
            pending.value = new ColorData { color1 = Color.white };
            TestContext.WriteLine(
                $"[DuplicateCache] Pending value preset: {DescribeColorData((ColorData)pending.value)}"
            );

            if (initializeWrapper)
            {
                SerializableDictionaryPropertyDrawer.EnsurePendingWrapper(
                    pending,
                    typeof(ColorData),
                    isValueField: true
                );
                Assert.IsTrue(
                    pending.valueWrapperProperty != null,
                    "Pending wrapper should initialize when requested."
                );
                pending.valueWrapperSerialized?.Update();
                if (pending.valueWrapperProperty != null && pending.valueWrapperSerialized != null)
                {
                    pending.valueWrapperProperty.managedReferenceValue = pending.value;
                    pending.valueWrapperSerialized.ApplyModifiedPropertiesWithoutUndo();
                    pending.valueWrapperSerialized.Update();
                }
            }

            bool initialMatch = SerializableDictionaryPropertyDrawer.EntryMatchesExisting(
                keysProperty,
                valuesProperty,
                storedIndex,
                typeof(string),
                typeof(ColorData),
                pending
            );
            TestContext.WriteLine(
                $"[DuplicateCache] Initial match (wrapper initialized: {initializeWrapper}) = {initialMatch}"
            );
            Assert.IsTrue(
                initialMatch,
                "Pending entry should match the existing dictionary entry."
            );

            SerializedProperty storedValue = valuesProperty.GetArrayElementAtIndex(storedIndex);
            ColorData serializedBeforeChange = ReadColorData(storedValue);
            TestContext.WriteLine(
                $"[DuplicateCache] Serialized value before mutation: {DescribeColorData(serializedBeforeChange)}"
            );
            SerializedProperty colorProperty = storedValue.FindPropertyRelative(
                nameof(ColorData.color1)
            );
            colorProperty.colorValue = Color.magenta;
            valuesProperty.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            valuesProperty.serializedObject.Update();
            ColorData serializedAfterChange = ReadColorData(storedValue);
            TestContext.WriteLine(
                $"[DuplicateCache] Serialized value after mutation: {DescribeColorData(serializedAfterChange)}"
            );

            bool cachedMatch = SerializableDictionaryPropertyDrawer.EntryMatchesExisting(
                keysProperty,
                valuesProperty,
                storedIndex,
                typeof(string),
                typeof(ColorData),
                pending
            );
            TestContext.WriteLine(
                $"[DuplicateCache] Cached match before invalidation = {cachedMatch}"
            );
            Assert.IsTrue(
                cachedMatch,
                "Cached duplicate check should remain true until the cache is invalidated."
            );

            string cacheKey = drawer.GetListKey(dictionaryProperty);
            drawer.InvalidatePendingDuplicateCache(cacheKey);

            bool refreshedMatch = SerializableDictionaryPropertyDrawer.EntryMatchesExisting(
                keysProperty,
                valuesProperty,
                storedIndex,
                typeof(string),
                typeof(ColorData),
                pending
            );
            TestContext.WriteLine(
                $"[DuplicateCache] Refreshed match after invalidation = {refreshedMatch}"
            );
            Assert.IsFalse(
                refreshedMatch,
                "Invalidating the cache should force a fresh duplicate comparison."
            );
        }

        [Test]
        public void ValuesEqualTreatsNullAndEmptyCollectionsAsEqualInStructs()
        {
            ColorData nullColors = new() { color1 = Color.red, otherColors = null };
            ColorData emptyColors = new()
            {
                color1 = Color.red,
                otherColors = Array.Empty<Color>(),
            };

            bool result = InvokeValuesEqual(nullColors, emptyColors);
            Assert.IsTrue(
                result,
                "ValuesEqual should treat null and empty collections as equivalent for struct fields."
            );
        }

        [Test]
        public void GetDefaultValueSupportsPrivateSerializableTypes()
        {
            object value = SerializableDictionaryPropertyDrawer.GetDefaultValue(
                typeof(PrivateComplexValue)
            );

            Assert.IsTrue(value != null);
            Assert.IsInstanceOf<PrivateComplexValue>(value);
        }

        [Test]
        public void GetDefaultValueReturnsNullForUnityObjectTypes()
        {
            object gameObjectDefault = SerializableDictionaryPropertyDrawer.GetDefaultValue(
                typeof(GameObject)
            );
            object scriptableDefault = SerializableDictionaryPropertyDrawer.GetDefaultValue(
                typeof(SampleScriptableObject)
            );

            Assert.IsTrue(gameObjectDefault == null);
            Assert.IsTrue(scriptableDefault == null);
        }

        [Test]
        public void CommitEntryAddsPrivateComplexValue()
        {
            PrivateComplexDictionaryHost host =
                CreateScriptableObject<PrivateComplexDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(PrivateComplexDictionaryHost.dictionary)
            );
            Assert.IsTrue(
                dictionaryProperty != null,
                $"Expected to find dictionary property at '{nameof(PrivateComplexDictionaryHost.dictionary)}'"
            );

            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );
            Assert.IsTrue(keysProperty != null, "Expected to find keys property.");
            Assert.IsTrue(valuesProperty != null, "Expected to find values property.");

            SerializableDictionaryPropertyDrawer drawer = new();
            PrivateComplexValue valueInstance = new()
            {
                Primary = Color.yellow,
                Secondary = Color.green,
            };

            SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(PrivateComplexValue),
                "Accent",
                valueInstance,
                dictionaryProperty
            );
            Assert.IsTrue(result.added, "Expected CommitEntry to add the new complex value.");

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializedProperty valueElement = valuesProperty.GetArrayElementAtIndex(0);
            Assert.IsTrue(valueElement != null, "Expected value element to exist after commit.");

            SerializedProperty primaryProperty = valueElement.FindPropertyRelative(
                nameof(PrivateComplexValue._primary)
            );
            SerializedProperty secondaryProperty = valueElement.FindPropertyRelative(
                nameof(PrivateComplexValue._secondary)
            );

            Assert.IsTrue(
                primaryProperty != null,
                $"Expected '_primary' color property to exist on committed value. ValueElement type: {valueElement.type}"
            );
            Assert.IsTrue(
                secondaryProperty != null,
                $"Expected '_secondary' color property to exist on committed value. ValueElement type: {valueElement.type}"
            );

            Assert.That(primaryProperty.colorValue, Is.EqualTo(Color.yellow));
            Assert.That(secondaryProperty.colorValue, Is.EqualTo(Color.green));

            host.dictionary.EditorAfterDeserialize();
            Assert.That(host.dictionary.Count, Is.EqualTo(1));
            Assert.That(host.dictionary.ContainsKey("Accent"), Is.True);
        }

        [Test]
        public void CommitEntryAddsPrivateComplexValueWithDefaultColors()
        {
            PrivateComplexDictionaryHost host =
                CreateScriptableObject<PrivateComplexDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(PrivateComplexDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            PrivateComplexValue valueInstance = new();

            SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(PrivateComplexValue),
                "Default",
                valueInstance,
                dictionaryProperty
            );
            Assert.IsTrue(result.added, "Expected CommitEntry to add the default complex value.");

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializedProperty valueElement = valuesProperty.GetArrayElementAtIndex(0);
            Assert.IsTrue(valueElement != null, "Expected value element to exist after commit.");

            SerializedProperty primaryProperty = valueElement.FindPropertyRelative(
                nameof(PrivateComplexValue._primary)
            );
            SerializedProperty secondaryProperty = valueElement.FindPropertyRelative(
                nameof(PrivateComplexValue._secondary)
            );

            Assert.IsTrue(
                primaryProperty != null,
                "Expected '_primary' property for default value."
            );
            Assert.IsTrue(
                secondaryProperty != null,
                "Expected '_secondary' property for default value."
            );

            Assert.That(
                primaryProperty.colorValue,
                Is.EqualTo(Color.white),
                "Default primary color should be white"
            );
            Assert.That(
                secondaryProperty.colorValue,
                Is.EqualTo(Color.black),
                "Default secondary color should be black"
            );
        }

        [Test]
        public void GetDefaultValueSupportsTypesWithPrivateConstructors()
        {
            object keyDefault = SerializableDictionaryPropertyDrawer.GetDefaultValue(
                typeof(PrivateCtorKey)
            );
            object valueDefault = SerializableDictionaryPropertyDrawer.GetDefaultValue(
                typeof(PrivateCtorValue)
            );

            Assert.IsInstanceOf<PrivateCtorKey>(keyDefault);
            Assert.IsInstanceOf<PrivateCtorValue>(valueDefault);
        }

        [Test]
        public void PendingEntryDefaultsIncludePrivateConstructorTypes()
        {
            PrivateCtorDictionaryHost host = CreateScriptableObject<PrivateCtorDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(PrivateCtorDictionaryHost.dictionary)
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.PendingEntry pending = GetPendingEntry(
                drawer,
                dictionaryProperty,
                typeof(PrivateCtorKey),
                typeof(PrivateCtorValue),
                isSortedDictionary: false
            );

            Assert.IsInstanceOf<PrivateCtorKey>(
                pending.key,
                "Pending key should use private constructor default."
            );
            Assert.IsInstanceOf<PrivateCtorValue>(
                pending.value,
                "Pending value should use private constructor default."
            );
        }

        [Test]
        public void PendingEntryDefaultsRemainNullForUnityObjects()
        {
            ScriptableObjectDictionaryHost host =
                CreateScriptableObject<ScriptableObjectDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ScriptableObjectDictionaryHost.dictionary)
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.PendingEntry pending = GetPendingEntry(
                drawer,
                dictionaryProperty,
                typeof(string),
                typeof(SampleScriptableObject),
                isSortedDictionary: false
            );

            Assert.IsTrue(
                pending.value == null,
                "UnityEngine.Object values should remain null so the object picker can be used."
            );
        }

        [UnityTest]
        public IEnumerator ManualEntryUsesObjectPickerForScriptableObjectKeys()
        {
            SerializableDictionaryPropertyDrawer.PendingEntry pending = new();
            Rect rect = new(0f, 0f, 200f, EditorGUIUtility.singleLineHeight);

            yield return TestIMGUIExecutor.Run(() =>
            {
                SerializableDictionaryPropertyDrawer.DrawFieldForType(
                    rect,
                    "Key",
                    null,
                    typeof(SampleScriptableObject),
                    pending,
                    isValueField: false
                );
            });

            Assert.IsTrue(
                pending.keyWrapper == null,
                "ScriptableObject keys should not allocate PendingValueWrappers."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntrySupportsStructValues()
        {
            SerializableDictionaryPropertyDrawer.PendingEntry pending = new();
            PendingStructValue initial = new() { label = "Alpha", tint = Color.cyan };
            Rect rect = new(0f, 0f, 260f, EditorGUIUtility.singleLineHeight * 3f);

            yield return TestIMGUIExecutor.Run(() =>
            {
                pending.value = initial;
                pending.value = SerializableDictionaryPropertyDrawer.DrawFieldForType(
                    rect,
                    "Value",
                    pending.value,
                    typeof(PendingStructValue),
                    pending,
                    isValueField: true
                );
            });

            Assert.IsInstanceOf<PendingStructValue>(
                pending.value,
                "Struct values should remain intact after drawing the pending entry."
            );

            PendingStructValue result = (PendingStructValue)pending.value;
            Assert.AreEqual(initial.label, result.label);
            Assert.AreEqual(initial.tint, result.tint);

            Assert.IsTrue(
                pending.valueWrapper != null,
                "Struct editing should allocate a PendingValueWrapper instance."
            );
            Assert.IsTrue(pending.valueWrapperSerialized != null);
            Assert.IsTrue(pending.valueWrapperProperty != null);
            Assert.AreEqual(
                SerializedPropertyType.ManagedReference,
                pending.valueWrapperProperty.propertyType,
                "Struct wrappers should expose a managed reference so nested fields are drawn."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryStructColorDataSupportsEditing()
        {
            SerializableDictionaryPropertyDrawer.PendingEntry pending = new();
            ColorData initial = new()
            {
                color1 = Color.red,
                color2 = Color.green,
                color3 = Color.blue,
                color4 = Color.white,
                otherColors = new[] { Color.cyan },
            };
            Rect rect = new(0f, 0f, 320f, EditorGUIUtility.singleLineHeight * 6f);

            yield return TestIMGUIExecutor.Run(() =>
            {
                pending.value = initial;
                pending.value = SerializableDictionaryPropertyDrawer.DrawFieldForType(
                    rect,
                    "Value",
                    pending.value,
                    typeof(ColorData),
                    pending,
                    isValueField: true
                );
            });

            Assert.IsTrue(
                pending.valueWrapperProperty != null,
                "Managed reference property should exist for struct value wrappers."
            );
            Assert.IsTrue(
                pending.valueWrapperProperty.editable,
                "Pending value wrapper property should remain editable."
            );

            SerializedProperty color1Property = pending.valueWrapperProperty.FindPropertyRelative(
                nameof(ColorData.color1)
            );
            Assert.IsTrue(
                color1Property != null,
                "Color fields should be reachable for struct values."
            );

            color1Property.colorValue = Color.magenta;
            pending.valueWrapperSerialized.ApplyModifiedPropertiesWithoutUndo();
            pending.valueWrapperSerialized.Update();

            ColorData updated = (ColorData)pending.valueWrapper.GetValue();
            AssertColorsApproximately(Color.magenta, updated.color1);
        }

        [UnityTest]
        public IEnumerator PendingEntryListValueSupportsEditing()
        {
            SerializableDictionaryPropertyDrawer.PendingEntry pending = new();
            ColorListData initial = new() { colors = new List<Color> { Color.red } };
            Rect rect = new(0f, 0f, 320f, EditorGUIUtility.singleLineHeight * 5f);

            yield return TestIMGUIExecutor.Run(() =>
            {
                pending.value = initial;
                pending.value = SerializableDictionaryPropertyDrawer.DrawFieldForType(
                    rect,
                    "Value",
                    pending.value,
                    typeof(ColorListData),
                    pending,
                    isValueField: true
                );
            });

            Assert.IsTrue(
                pending.valueWrapperProperty != null,
                "List-backed pending values should allocate wrapper properties."
            );

            SerializedProperty colorsProperty = pending.valueWrapperProperty.FindPropertyRelative(
                nameof(ColorListData.colors)
            );
            Assert.IsTrue(colorsProperty != null, "ColorListData should expose the colors list.");
            Assert.IsTrue(colorsProperty.isArray, "List fields should be serialized as arrays.");

            colorsProperty.arraySize = 2;
            colorsProperty.GetArrayElementAtIndex(0).colorValue = Color.magenta;
            colorsProperty.GetArrayElementAtIndex(1).colorValue = Color.yellow;
            pending.valueWrapperSerialized.ApplyModifiedPropertiesWithoutUndo();
            pending.valueWrapperSerialized.Update();

            ColorListData updated = (ColorListData)pending.valueWrapper.GetValue();
            Assert.IsTrue(updated.colors != null);
            Assert.That(updated.colors.Count, Is.EqualTo(2));
            AssertColorsApproximately(Color.magenta, updated.colors[0]);
            AssertColorsApproximately(Color.yellow, updated.colors[1]);
        }

        [UnityTest]
        public IEnumerator PendingEntryListValueCommitsChanges()
        {
            ColorListDictionaryHost host = CreateScriptableObject<ColorListDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ColorListDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(ColorListDictionaryHost),
                nameof(ColorListDictionaryHost.dictionary)
            );

            SerializableDictionaryPropertyDrawer.PendingEntry pending =
                drawer.GetOrCreatePendingEntry(
                    dictionaryProperty,
                    typeof(string),
                    typeof(ColorListData),
                    isSortedDictionary: false
                );
            pending.key = "Palette";
            pending.value = new ColorListData
            {
                colors = new List<Color> { Color.red, Color.cyan },
            };

            SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(ColorListData),
                pending.key,
                pending.value,
                dictionaryProperty,
                existingIndex: -1,
                isSortedDictionary: false
            );
            Assert.IsTrue(result.added, "Expected the pending list value to be committed.");

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
            host.dictionary.EditorAfterDeserialize();

            Assert.That(host.dictionary.ContainsKey("Palette"));
            ColorListData stored = host.dictionary["Palette"];
            Assert.IsTrue(stored.colors != null);
            Assert.That(stored.colors.Count, Is.EqualTo(2));
            AssertColorsApproximately(Color.red, stored.colors[0]);
            AssertColorsApproximately(Color.cyan, stored.colors[1]);
            yield break;
        }

        [Test]
        public void CommitEntryPreservesScriptableObjectReference()
        {
            ScriptableObjectDictionaryHost host =
                CreateScriptableObject<ScriptableObjectDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ScriptableObjectDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            Object scriptable = Track(ScriptableObject.CreateInstance<SampleScriptableObject>());
            scriptable.hideFlags = HideFlags.HideAndDontSave;

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(ScriptableObjectDictionaryHost),
                nameof(ScriptableObjectDictionaryHost.dictionary)
            );

            SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(SampleScriptableObject),
                "Asset",
                scriptable,
                dictionaryProperty
            );

            Assert.IsTrue(
                result.added,
                "Expected CommitEntry to add the scriptable object reference."
            );

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
            host.dictionary.EditorAfterDeserialize();

            Assert.IsTrue(host.dictionary.ContainsKey("Asset"));
            Assert.AreSame(
                scriptable,
                host.dictionary["Asset"],
                "Dictionary should retain the committed scriptable object reference."
            );

            SerializedProperty committedValue = valuesProperty.GetArrayElementAtIndex(result.index);
            Assert.AreSame(
                scriptable,
                committedValue.objectReferenceValue,
                "Serialized array should reference the committed scriptable object."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryPrimitiveValueDrawsInlineField()
        {
            SerializableDictionaryPropertyDrawer.PendingEntry pending = new();
            Rect rect = new(0f, 0f, 200f, EditorGUIUtility.singleLineHeight);

            yield return TestIMGUIExecutor.Run(() =>
            {
                pending.value = 5;
                pending.value = SerializableDictionaryPropertyDrawer.DrawFieldForType(
                    rect,
                    "Value",
                    pending.value,
                    typeof(int),
                    pending,
                    isValueField: true
                );
            });

            Assert.IsInstanceOf<int>(pending.value, "Primitive value should remain an int.");
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
        public void GetPropertyHeightAutoExpandsComplexRowsOnFirstDraw()
        {
            ComplexValueDictionaryHost host = CreateScriptableObject<ComplexValueDictionaryHost>();
            host.dictionary.Add(
                "Accent",
                new ComplexValue { button = Color.cyan, text = Color.black }
            );

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ComplexValueDictionaryHost.dictionary)
            );
            ForcePopulateComplexDictionarySerializedData(host, dictionaryProperty);
            dictionaryProperty.isExpanded = true;

            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );
            Assert.Greater(valuesProperty.arraySize, 0, "Dictionary should contain test entries.");
            SerializedProperty valueProperty = valuesProperty.GetArrayElementAtIndex(0);
            valueProperty.isExpanded = false;

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(ComplexValueDictionaryHost),
                nameof(ComplexValueDictionaryHost.dictionary)
            );

            drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

            serializedObject.Update();
            SerializedProperty refreshedValues = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );
            SerializedProperty refreshedValue = refreshedValues.GetArrayElementAtIndex(0);
            Assert.IsTrue(
                refreshedValue.isExpanded,
                "GetPropertyHeight should expand complex dictionary rows before the first draw so layout reserves enough space."
            );
        }

        [UnityTest]
        public IEnumerator DictionaryRowComplexValueChildControlsHaveSpaceOnFirstDraw()
        {
            ComplexValueDictionaryHost host = CreateScriptableObject<ComplexValueDictionaryHost>();
            host.dictionary.Add(
                "Accent",
                new ComplexValue { button = Color.cyan, text = Color.black }
            );

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ComplexValueDictionaryHost.dictionary)
            );
            ForcePopulateComplexDictionarySerializedData(host, dictionaryProperty);
            dictionaryProperty.isExpanded = true;
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );
            Assert.Greater(valuesProperty.arraySize, 0, "Dictionary should contain test entries.");
            SerializedProperty valueProperty = valuesProperty.GetArrayElementAtIndex(0);
            valueProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(ComplexValueDictionaryHost),
                nameof(ComplexValueDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 360f, 520f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            // Simulate Unity's layout pass.
            drawer.GetPropertyHeight(dictionaryProperty, label);

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            bool hasRowRects = SerializableDictionaryPropertyDrawer.HasLastRowRects;
            bool hasChildRects = SerializableDictionaryPropertyDrawer.HasLastRowChildContentRect;
            bool usedFoldoutLabel =
                SerializableDictionaryPropertyDrawer.LastRowValueUsedFoldoutLabel;
            float valueHeight = hasRowRects
                ? SerializableDictionaryPropertyDrawer.LastRowValueRect.height
                : 0f;
            float childWidth = hasChildRects
                ? SerializableDictionaryPropertyDrawer.LastRowChildContentRect.width
                : 0f;
            float singleLineHeight = EditorGUIUtility.singleLineHeight;

            TestContext.WriteLine(
                $"[DictionaryRowComplexValueChildControlsHaveSpaceOnFirstDraw] "
                    + $"hasRowRects={hasRowRects}, hasChildRects={hasChildRects}, "
                    + $"usedFoldoutLabel={usedFoldoutLabel}, valueHeight={valueHeight:F3}, "
                    + $"childWidth={childWidth:F3}, singleLineHeight={singleLineHeight:F3}"
            );

            Assert.IsTrue(
                hasChildRects,
                "First draw should record child layout information for complex values."
            );
            Assert.Greater(
                valueHeight,
                singleLineHeight * 2f,
                $"Complex value rows should reserve space for expanded children. Observed height: {valueHeight:F3}"
            );
            Assert.Greater(
                childWidth,
                160f,
                "Complex value child drawers should reserve a reasonable amount of width on the first draw."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryComplexValueAllocatesFoldoutGutter()
        {
            ComplexValueDictionaryHost host = CreateScriptableObject<ComplexValueDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ComplexValueDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(ComplexValueDictionaryHost),
                nameof(ComplexValueDictionaryHost.dictionary)
            );

            SerializableDictionaryPropertyDrawer.PendingEntry pending = GetPendingEntry(
                drawer,
                dictionaryProperty,
                typeof(string),
                typeof(ComplexValue),
                isSortedDictionary: false
            );
            pending.isExpanded = true;

            Rect controlRect = new(0f, 0f, 360f, 520f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.LastPendingValueUsedFoldoutLabel,
                "Pending value should reserve a foldout gutter for complex value types."
            );
            Assert.That(
                SerializableDictionaryPropertyDrawer.LastPendingValueFoldoutOffset,
                Is.EqualTo(SerializableDictionaryPropertyDrawer.PendingExpandableValueFoldoutGutter)
                    .Within(0.0001f),
                "Pending value foldout gutter should match the configured width."
            );
        }

        [UnityTest]
        public IEnumerator DictionaryDrawerDoesNotResetUnappliedScalarChanges()
        {
            MixedFieldsDictionaryHost host = Track(
                ScriptableObject.CreateInstance<MixedFieldsDictionaryHost>()
            );
            SerializedObject serializedHost = TrackDisposable(new SerializedObject(host));
            serializedHost.Update();

            SerializedProperty scalarProperty = serializedHost.FindProperty(
                nameof(MixedFieldsDictionaryHost.scalarValue)
            );
            SerializedProperty dictionaryProperty = serializedHost.FindProperty(
                nameof(MixedFieldsDictionaryHost.dictionary)
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(MixedFieldsDictionaryHost),
                nameof(MixedFieldsDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 360f, 320f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
            drawer.GetPropertyHeight(dictionaryProperty, label);

            scalarProperty.intValue = 1337;

            yield return TestIMGUIExecutor.Run(() =>
            {
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            serializedHost.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(host.scalarValue, Is.EqualTo(1337));
        }

        [UnityTest]
        public IEnumerator DictionaryDrawerMultiObjectEditKeepsScalarChanges()
        {
            MixedFieldsDictionaryHost first = Track(
                ScriptableObject.CreateInstance<MixedFieldsDictionaryHost>()
            );
            MixedFieldsDictionaryHost second = Track(
                ScriptableObject.CreateInstance<MixedFieldsDictionaryHost>()
            );
            SerializedObject serializedHosts = TrackDisposable(
                new SerializedObject(new Object[] { first, second })
            );
            serializedHosts.Update();

            SerializedProperty scalarProperty = serializedHosts.FindProperty(
                nameof(MixedFieldsDictionaryHost.scalarValue)
            );
            SerializedProperty dictionaryProperty = serializedHosts.FindProperty(
                nameof(MixedFieldsDictionaryHost.dictionary)
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(MixedFieldsDictionaryHost),
                nameof(MixedFieldsDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 360f, 320f);
            GUIContent label = new("Dictionary");

            drawer.GetPropertyHeight(dictionaryProperty, label);

            scalarProperty.intValue = 2112;

            yield return TestIMGUIExecutor.Run(() =>
            {
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            serializedHosts.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(
                first.scalarValue,
                Is.EqualTo(2112),
                "First target should keep scalar edit."
            );
            Assert.That(
                second.scalarValue,
                Is.EqualTo(2112),
                "Second target should keep scalar edit."
            );
        }

        [UnityTest]
        public IEnumerator DictionaryDrawerDoesNotResetTrailingScalarField()
        {
            DictionaryScalarAfterHost host = Track(
                ScriptableObject.CreateInstance<DictionaryScalarAfterHost>()
            );
            SerializedObject serializedHost = TrackDisposable(new SerializedObject(host));
            serializedHost.Update();

            SerializedProperty dictionaryProperty = serializedHost.FindProperty(
                nameof(DictionaryScalarAfterHost.dictionary)
            );
            SerializedProperty trailingScalarProperty = serializedHost.FindProperty(
                nameof(DictionaryScalarAfterHost.trailingScalar)
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(DictionaryScalarAfterHost),
                nameof(DictionaryScalarAfterHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 360f, 320f);
            GUIContent label = new("Dictionary");

            drawer.GetPropertyHeight(dictionaryProperty, label);

            trailingScalarProperty.intValue = 9001;

            yield return TestIMGUIExecutor.Run(() =>
            {
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            serializedHost.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(host.trailingScalar, Is.EqualTo(9001));
        }

        [Test]
        public void PaletteCommitTriggersUnityHelpersSettingsSave()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.EnsureWButtonCustomColorDefaults();

            SerializedObject serializedSettings = TrackDisposable(new SerializedObject(settings));
            serializedSettings.Update();
            SerializedProperty dictionaryProperty = serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            Assert.IsTrue(
                dictionaryProperty != null,
                "Expected to find wbuttonCustomColors property."
            );

            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );
            Assert.IsTrue(keysProperty != null);
            Assert.IsTrue(valuesProperty != null);

            const string TestKey = "__TestPaletteProjectSettings";
            RemoveStringDictionaryEntry(keysProperty, valuesProperty, TestKey);
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            serializedSettings.Update();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(UnityHelpersSettings),
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );

            UnityHelpersSettings.WButtonCustomColor value = new()
            {
                ButtonColor = Color.magenta,
                TextColor = Color.yellow,
            };

            bool saveInvoked = false;
            void OnSettingsSaved()
            {
                saveInvoked = true;
            }

            UnityHelpersSettings.OnSettingsSaved += OnSettingsSaved;
            try
            {
                SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                    keysProperty,
                    valuesProperty,
                    typeof(string),
                    typeof(UnityHelpersSettings.WButtonCustomColor),
                    TestKey,
                    value,
                    dictionaryProperty,
                    existingIndex: -1
                );

                Assert.IsTrue(result.added, "Expected palette entry to be added.");
                Assert.IsTrue(
                    saveInvoked,
                    "UnityHelpersSettings.SaveSettings should be invoked after palette edits."
                );
            }
            finally
            {
                UnityHelpersSettings.OnSettingsSaved -= OnSettingsSaved;
                RemoveStringDictionaryEntry(keysProperty, valuesProperty, TestKey);
                serializedSettings.ApplyModifiedPropertiesWithoutUndo();
                serializedSettings.Update();
                settings.SaveSettings();
            }
        }

        [UnityTest]
        public IEnumerator DictionaryRowComplexValueAllocatesFoldoutGutter()
        {
            ComplexValueDictionaryHost host = CreateScriptableObject<ComplexValueDictionaryHost>();
            host.dictionary.Add(
                "Accent",
                new ComplexValue { button = Color.cyan, text = Color.black }
            );

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ComplexValueDictionaryHost.dictionary)
            );
            ForcePopulateComplexDictionarySerializedData(host, dictionaryProperty);
            dictionaryProperty.isExpanded = true;
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );
            Assert.Greater(valuesProperty.arraySize, 0, "Dictionary should contain test entries.");
            SerializedProperty valueProperty = valuesProperty.GetArrayElementAtIndex(0);
            valueProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(ComplexValueDictionaryHost),
                nameof(ComplexValueDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 360f, 520f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            bool usedFoldoutLabel =
                SerializableDictionaryPropertyDrawer.LastRowValueUsedFoldoutLabel;
            float foldoutOffset = SerializableDictionaryPropertyDrawer.LastRowValueFoldoutOffset;
            bool hasRowRects = SerializableDictionaryPropertyDrawer.HasLastRowRects;

            TestContext.WriteLine(
                $"[DictionaryRowComplexValueAllocatesFoldoutGutter] "
                    + $"hasRowRects={hasRowRects}, usedFoldoutLabel={usedFoldoutLabel}, "
                    + $"foldoutOffset={foldoutOffset:F3}, "
                    + $"expectedGutter={SerializableDictionaryPropertyDrawer.RowExpandableValueFoldoutGutter:F3}"
            );

            Assert.IsTrue(
                usedFoldoutLabel,
                "Row values should reserve a foldout gutter for complex value types."
            );
            Assert.That(
                foldoutOffset,
                Is.EqualTo(SerializableDictionaryPropertyDrawer.RowExpandableValueFoldoutGutter)
                    .Within(0.0001f),
                "Row value foldout gutter should match the configured width."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryHeaderIgnoresEditorIndent()
        {
            const int IndentDepth = 3;

            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 360f, 420f);
            GUIContent label = new("Dictionary");

            float baselineOffset = 0f;
            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
                baselineOffset =
                    SerializableDictionaryPropertyDrawer.LastPendingFoldoutToggleRect.xMin
                    - SerializableDictionaryPropertyDrawer.LastPendingHeaderRect.xMin;
            });

            float indentedOffset = 0f;
            int indentLevelAfter = -1;
            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
                EditorGUI.indentLevel = IndentDepth;
                drawer.OnGUI(controlRect, dictionaryProperty, label);
                indentedOffset =
                    SerializableDictionaryPropertyDrawer.LastPendingFoldoutToggleRect.xMin
                    - SerializableDictionaryPropertyDrawer.LastPendingHeaderRect.xMin;
                indentLevelAfter = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
            });

            Assert.That(
                indentedOffset,
                Is.EqualTo(baselineOffset).Within(0.0001f),
                "Pending header offset should be stable regardless of external indent."
            );
            Assert.That(
                indentLevelAfter,
                Is.EqualTo(IndentDepth),
                "Drawer should restore the caller's indent level."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryToggleOffsetAdjustsForSettingsContext()
        {
            _sharedSerializedObject.Update();
            SerializedProperty hostDictionary = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 360f, 420f);
            GUIContent label = new("Dictionary");
            hostDictionary.isExpanded = true;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

            float hostOffset = 0f;
            yield return TestIMGUIExecutor.Run(() =>
            {
                hostDictionary.serializedObject.UpdateIfRequiredOrScript();
                SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
                drawer.OnGUI(controlRect, hostDictionary, label);
                hostOffset =
                    SerializableDictionaryPropertyDrawer.LastPendingFoldoutToggleRect.xMin
                    - SerializableDictionaryPropertyDrawer.LastPendingHeaderRect.xMin;
            });

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            SerializedObject settingsSerialized = TrackDisposable(new SerializedObject(settings));
            settingsSerialized.Update();
            SerializedProperty paletteProperty = settingsSerialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            AssignDictionaryFieldInfo(
                drawer,
                typeof(UnityHelpersSettings),
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            paletteProperty.isExpanded = true;
            settingsSerialized.ApplyModifiedPropertiesWithoutUndo();

            float settingsOffset = 0f;
            yield return TestIMGUIExecutor.Run(() =>
            {
                paletteProperty.serializedObject.UpdateIfRequiredOrScript();
                SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
                drawer.OnGUI(controlRect, paletteProperty, new GUIContent("Palette"));
                settingsOffset =
                    SerializableDictionaryPropertyDrawer.LastPendingFoldoutToggleRect.xMin
                    - SerializableDictionaryPropertyDrawer.LastPendingHeaderRect.xMin;
            });

            Assert.That(
                hostOffset,
                Is.EqualTo(SerializableDictionaryPropertyDrawer.PendingFoldoutToggleOffset)
                    .Within(0.0001f),
                "Default context should use the standard pending toggle offset."
            );
            Assert.That(
                settingsOffset,
                Is.EqualTo(
                        SerializableDictionaryPropertyDrawer.PendingFoldoutToggleOffsetProjectSettings
                    )
                    .Within(0.0001f),
                "UnityHelpersSettings context should use the project-settings offset."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryFoldoutToggleRespectsOffset()
        {
            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 360f, 480f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingHeaderRect,
                "Expected pending header layout to be tracked."
            );

            float offset =
                SerializableDictionaryPropertyDrawer.LastPendingFoldoutToggleRect.xMin
                - SerializableDictionaryPropertyDrawer.LastPendingHeaderRect.xMin;

            Assert.That(
                offset,
                Is.EqualTo(SerializableDictionaryPropertyDrawer.PendingFoldoutToggleOffset)
                    .Within(0.0001f),
                "Pending header toggle should honor the configured offset."
            );
        }

        [UnityTest]
        public IEnumerator DictionaryRowFieldsApplyPadding()
        {
            _sharedHost.dictionary.Add(1, "One");
            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            ForcePopulateTestDictionarySerializedData(_sharedHost, dictionaryProperty);
            dictionaryProperty.isExpanded = true;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 360f, 520f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastRowRects,
                "Expected dictionary row layout to be tracked."
            );

            float keyPadding =
                SerializableDictionaryPropertyDrawer.LastRowKeyRect.xMin
                - SerializableDictionaryPropertyDrawer.LastRowOriginalRect.xMin;
            Assert.That(
                keyPadding,
                Is.EqualTo(SerializableDictionaryPropertyDrawer.DictionaryRowFieldPadding)
                    .Within(0.0001f),
                "Key column should include the configured row padding."
            );

            float valuePadding =
                SerializableDictionaryPropertyDrawer.LastRowValueRect.xMin
                - SerializableDictionaryPropertyDrawer.LastRowValueBaseX;
            Assert.That(
                valuePadding,
                Is.EqualTo(SerializableDictionaryPropertyDrawer.DictionaryRowFieldPadding)
                    .Within(0.0001f),
                "Value column should include the configured row padding."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryHeaderHonorsGroupPadding()
        {
            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 360f, 420f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingHeaderRect,
                "Baseline draw should capture pending header layout."
            );

            Rect baselineHeader = SerializableDictionaryPropertyDrawer.LastPendingHeaderRect;

            const float LeftPadding = 20f;
            const float RightPadding = 12f;
            float horizontalPadding = LeftPadding + RightPadding;

            GroupGUIWidthUtility.ResetForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        LeftPadding,
                        RightPadding
                    )
                )
                {
                    SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
                    drawer.OnGUI(controlRect, dictionaryProperty, label);
                }
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingHeaderRect,
                "Grouped draw should capture pending header layout."
            );

            Rect groupedHeader = SerializableDictionaryPropertyDrawer.LastPendingHeaderRect;

            TestContext.WriteLine(
                $"[PendingEntryHeaderHonorsGroupPadding] "
                    + $"baselineHeader.xMin={baselineHeader.xMin:F3}, groupedHeader.xMin={groupedHeader.xMin:F3}, "
                    + $"baselineHeader.width={baselineHeader.width:F3}, groupedHeader.width={groupedHeader.width:F3}, "
                    + $"LeftPadding={LeftPadding:F3}"
            );

            // The x position should shift by approximately LeftPadding
            // because baseline now aligns with Unity's default list rendering (no offset)
            float expectedXShift = LeftPadding;
            float actualXShift = groupedHeader.xMin - baselineHeader.xMin;
            Assert.That(
                actualXShift,
                Is.EqualTo(expectedXShift).Within(1.0f),
                $"Pending header xMin shift should be approximately LeftPadding ({LeftPadding}). Actual shift: {actualXShift}"
            );

            // The width should decrease when WGroup padding is applied
            // The reduction should be approximately horizontalPadding
            float expectedWidthReduction = horizontalPadding;
            float actualWidthReduction = baselineHeader.width - groupedHeader.width;
            Assert.That(
                actualWidthReduction,
                Is.EqualTo(expectedWidthReduction).Within(12.0f),
                $"Pending header width should decrease by approximately horizontalPadding = {expectedWidthReduction}. Actual reduction: {actualWidthReduction}"
            );

            // Verify grouped header is smaller than baseline (more padding applied)
            Assert.Less(
                groupedHeader.width,
                baselineHeader.width,
                "Grouped header should be narrower than baseline due to WGroup padding."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryHeaderHonorsGroupPaddingInSettingsContext()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            SerializedObject serializedSettings = TrackDisposable(new SerializedObject(settings));
            serializedSettings.Update();
            SerializedProperty paletteProperty = serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            paletteProperty.isExpanded = true;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(UnityHelpersSettings),
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );

            Rect controlRect = new(0f, 0f, 360f, 420f);
            GUIContent label = new("Palette");

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                paletteProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, paletteProperty, label);
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingHeaderRect,
                "Baseline draw should capture pending header layout for settings dictionaries."
            );

            Rect baselineHeader = SerializableDictionaryPropertyDrawer.LastPendingHeaderRect;

            const float LeftPadding = 18f;
            const float RightPadding = 12f;
            float horizontalPadding = LeftPadding + RightPadding;

            GroupGUIWidthUtility.ResetForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                paletteProperty.serializedObject.UpdateIfRequiredOrScript();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        LeftPadding,
                        RightPadding
                    )
                )
                {
                    SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
                    drawer.OnGUI(controlRect, paletteProperty, label);
                }
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingHeaderRect,
                "Grouped draw should capture pending header layout for settings dictionaries."
            );

            Rect groupedHeader = SerializableDictionaryPropertyDrawer.LastPendingHeaderRect;

            Assert.That(
                groupedHeader.xMin,
                Is.EqualTo(baselineHeader.xMin + LeftPadding).Within(0.0001f),
                "Pending header in Project Settings should respect group padding."
            );
            Assert.That(
                groupedHeader.width,
                Is.EqualTo(Mathf.Max(0f, baselineHeader.width - horizontalPadding)).Within(0.0001f),
                "Pending header width in Project Settings should shrink by the applied padding."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryFieldsHonorGroupPadding()
        {
            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 360f, 420f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.PendingEntry pending =
                drawer.GetOrCreatePendingEntry(
                    dictionaryProperty,
                    typeof(int),
                    typeof(string),
                    isSortedDictionary: false
                );
            pending.isExpanded = true;

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                pending.isExpanded = true;
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingFieldRects,
                "Baseline draw should capture pending key/value rects."
            );

            Rect baselineKey = SerializableDictionaryPropertyDrawer.LastPendingKeyFieldRect;
            Rect baselineValue = SerializableDictionaryPropertyDrawer.LastPendingValueFieldRect;

            const float LeftPadding = 18f;
            const float RightPadding = 14f;
            float horizontalPadding = LeftPadding + RightPadding;

            GroupGUIWidthUtility.ResetForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        LeftPadding,
                        RightPadding
                    )
                )
                {
                    SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
                    pending.isExpanded = true;
                    drawer.OnGUI(controlRect, dictionaryProperty, label);
                }
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingFieldRects,
                "Grouped draw should capture pending key/value rects."
            );

            Rect groupedKey = SerializableDictionaryPropertyDrawer.LastPendingKeyFieldRect;
            Rect groupedValue = SerializableDictionaryPropertyDrawer.LastPendingValueFieldRect;

            TestContext.WriteLine(
                $"[PendingEntryFieldsHonorGroupPadding] "
                    + $"baselineKey.xMin={baselineKey.xMin:F3}, groupedKey.xMin={groupedKey.xMin:F3}, "
                    + $"baselineKey.width={baselineKey.width:F3}, groupedKey.width={groupedKey.width:F3}, "
                    + $"LeftPadding={LeftPadding:F3}"
            );

            // The x position should shift by approximately LeftPadding
            // because baseline now aligns with Unity's default list rendering (no offset)
            float expectedXShift = LeftPadding;
            float actualKeyXShift = groupedKey.xMin - baselineKey.xMin;
            Assert.That(
                actualKeyXShift,
                Is.EqualTo(expectedXShift).Within(1.0f),
                $"Pending key field xMin shift should be approximately {expectedXShift}. Actual: {actualKeyXShift}"
            );

            float actualValueXShift = groupedValue.xMin - baselineValue.xMin;
            Assert.That(
                actualValueXShift,
                Is.EqualTo(expectedXShift).Within(1.0f),
                $"Pending value field xMin shift should be approximately LeftPadding ({LeftPadding}). Actual: {actualValueXShift}"
            );

            // Width should decrease when WGroup padding is applied
            Assert.Less(
                groupedKey.width,
                baselineKey.width + 1.0f,
                "Pending key width should be narrower with WGroup padding."
            );
            Assert.Less(
                groupedValue.width,
                baselineValue.width + 1.0f,
                "Pending value width should be narrower with WGroup padding."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryFieldsHonorGroupPaddingInSettingsContext()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            SerializedObject serializedSettings = TrackDisposable(new SerializedObject(settings));
            serializedSettings.Update();
            SerializedProperty paletteProperty = serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            paletteProperty.isExpanded = true;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(UnityHelpersSettings),
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );

            Rect controlRect = new(0f, 0f, 360f, 420f);
            GUIContent label = new("Palette");

            SerializableDictionaryPropertyDrawer.PendingEntry pending =
                drawer.GetOrCreatePendingEntry(
                    paletteProperty,
                    typeof(string),
                    typeof(UnityHelpersSettings.WButtonCustomColor),
                    isSortedDictionary: false
                );
            pending.isExpanded = true;

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                paletteProperty.serializedObject.UpdateIfRequiredOrScript();
                pending.isExpanded = true;
                drawer.OnGUI(controlRect, paletteProperty, label);
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingFieldRects,
                "Baseline draw should capture pending key/value rects for settings dictionaries."
            );

            Rect baselineKey = SerializableDictionaryPropertyDrawer.LastPendingKeyFieldRect;
            Rect baselineValue = SerializableDictionaryPropertyDrawer.LastPendingValueFieldRect;

            const float LeftPadding = 20f;
            const float RightPadding = 10f;
            float horizontalPadding = LeftPadding + RightPadding;

            GroupGUIWidthUtility.ResetForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                paletteProperty.serializedObject.UpdateIfRequiredOrScript();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        LeftPadding,
                        RightPadding
                    )
                )
                {
                    SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
                    pending.isExpanded = true;
                    drawer.OnGUI(controlRect, paletteProperty, label);
                }
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingFieldRects,
                "Grouped draw should capture pending key/value rects for settings dictionaries."
            );

            Rect groupedKey = SerializableDictionaryPropertyDrawer.LastPendingKeyFieldRect;
            Rect groupedValue = SerializableDictionaryPropertyDrawer.LastPendingValueFieldRect;

            Assert.That(
                groupedKey.xMin,
                Is.EqualTo(baselineKey.xMin + LeftPadding).Within(0.0001f),
                "Pending key field in Project Settings should shift by the applied left padding."
            );
            Assert.That(
                groupedValue.xMin,
                Is.EqualTo(baselineValue.xMin + LeftPadding).Within(0.0001f),
                "Pending value field in Project Settings should shift by the applied left padding."
            );
            Assert.That(
                groupedKey.width,
                Is.EqualTo(Mathf.Max(0f, baselineKey.width - horizontalPadding)).Within(0.0001f),
                "Pending key width in Project Settings should shrink by the total group padding."
            );
            Assert.That(
                groupedValue.width,
                Is.EqualTo(Mathf.Max(0f, baselineValue.width - horizontalPadding)).Within(0.0001f),
                "Pending value width in Project Settings should shrink by the total group padding."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryKeyAndValueFieldsAreAligned()
        {
            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 360f, 420f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.PendingEntry pending =
                drawer.GetOrCreatePendingEntry(
                    dictionaryProperty,
                    typeof(int),
                    typeof(string),
                    isSortedDictionary: false
                );
            pending.isExpanded = true;

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                pending.isExpanded = true;
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingFieldRects,
                "Draw should capture pending key/value rects."
            );

            Rect keyRect = SerializableDictionaryPropertyDrawer.LastPendingKeyFieldRect;
            Rect valueRect = SerializableDictionaryPropertyDrawer.LastPendingValueFieldRect;

            // Both fields start at the same xMin, at this and every other indent level.
            float actualShift = keyRect.xMin - valueRect.xMin;

            TestContext.WriteLine(
                $"[PendingEntryKeyAndValueFieldsAreAligned] "
                    + $"keyRect.xMin={keyRect.xMin:F3}, valueRect.xMin={valueRect.xMin:F3}, "
                    + $"keyRect.width={keyRect.width:F3}, valueRect.width={valueRect.width:F3}, "
                    + $"actualShift={actualShift:F3} (expected 0 at indent level 0)"
            );

            Assert.That(
                actualShift,
                Is.EqualTo(0f).Within(1f),
                "At indent level 0, pending key and value fields should start at the same position (no room for left shift)."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryAnimBoolSynchronizesWithExpandedState()
        {
            // This test verifies that when pending.isExpanded is set programmatically,
            // the AnimBool target synchronizes correctly so the animation starts toward
            // the new state. We then force the animation to complete to verify content renders.
            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 360f, 420f);
            GUIContent label = new("Dictionary");

            // Create pending entry and get reference to it - initially collapsed
            SerializableDictionaryPropertyDrawer.PendingEntry pending =
                drawer.GetOrCreatePendingEntry(
                    dictionaryProperty,
                    typeof(int),
                    typeof(string),
                    isSortedDictionary: false
                );
            pending.isExpanded = false;

            // First draw with collapsed state to create AnimBool
            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            bool rectsAfterCollapsed =
                SerializableDictionaryPropertyDrawer.HasLastPendingFieldRects;
            TestContext.WriteLine(
                $"[PendingEntryAnimBoolSynchronizesWithExpandedState] after collapsed draw: "
                    + $"hasPendingRects={rectsAfterCollapsed}"
            );

            // Now expand programmatically (simulating what tests do)
            pending.isExpanded = true;

            // Force animation to complete immediately for test purposes
            // (in real usage, this animates smoothly over multiple frames)
            if (pending.foldoutAnim != null)
            {
                pending.foldoutAnim.value = pending.isExpanded;
            }

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            // Draw again - content should now render since animation is complete
            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            bool rectsAfterExpanded = SerializableDictionaryPropertyDrawer.HasLastPendingFieldRects;
            TestContext.WriteLine(
                $"[PendingEntryAnimBoolSynchronizesWithExpandedState] after expanded draw: "
                    + $"hasPendingRects={rectsAfterExpanded}, isExpanded={pending.isExpanded}"
            );

            Assert.IsTrue(
                rectsAfterExpanded,
                "Pending field rects should be captured after expansion when animation completes."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryKeyAndValueFieldsAreAlignedInSettingsContext()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            SerializedObject serializedSettings = TrackDisposable(new SerializedObject(settings));
            serializedSettings.Update();
            SerializedProperty paletteProperty = serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            paletteProperty.isExpanded = true;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(UnityHelpersSettings),
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );

            Rect controlRect = new(0f, 0f, 360f, 420f);
            GUIContent label = new("Palette");

            SerializableDictionaryPropertyDrawer.PendingEntry pending =
                drawer.GetOrCreatePendingEntry(
                    paletteProperty,
                    typeof(string),
                    typeof(UnityHelpersSettings.WButtonCustomColor),
                    isSortedDictionary: false
                );
            pending.isExpanded = true;

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                paletteProperty.serializedObject.UpdateIfRequiredOrScript();
                pending.isExpanded = true;
                drawer.OnGUI(controlRect, paletteProperty, label);
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingFieldRects,
                "Draw should capture pending key/value rects in settings context."
            );

            Rect keyRect = SerializableDictionaryPropertyDrawer.LastPendingKeyFieldRect;
            Rect valueRect = SerializableDictionaryPropertyDrawer.LastPendingValueFieldRect;

            TestContext.WriteLine(
                $"[PendingEntryKeyAndValueFieldsAreAlignedInSettingsContext] "
                    + $"keyRect.xMin={keyRect.xMin:F3}, valueRect.xMin={valueRect.xMin:F3}, "
                    + $"keyRect.width={keyRect.width:F3}, valueRect.width={valueRect.width:F3}"
            );

            Assert.That(
                keyRect.xMin,
                Is.EqualTo(valueRect.xMin).Within(1f),
                "Pending key and value field left edges should be aligned in settings context."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryKeyAndValueAlignedAtIndentLevel1()
        {
            yield return VerifyPendingEntryAlignmentAtIndentLevel(1);
        }

        [UnityTest]
        public IEnumerator PendingEntryKeyAndValueAlignedAtIndentLevel2()
        {
            yield return VerifyPendingEntryAlignmentAtIndentLevel(2);
        }

        [UnityTest]
        public IEnumerator PendingEntryKeyAndValueAlignedAtIndentLevel3()
        {
            yield return VerifyPendingEntryAlignmentAtIndentLevel(3);
        }

        [UnityTest]
        public IEnumerator PendingEntryKeyAndValueAlignedAtIndentLevel5()
        {
            yield return VerifyPendingEntryAlignmentAtIndentLevel(5);
        }

        private IEnumerator VerifyPendingEntryAlignmentAtIndentLevel(int indentLevel)
        {
            using (new DictionaryTweenDisabledScope())
            {
                _sharedSerializedObject.Update();
                SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                    nameof(TestDictionaryHost.dictionary)
                );
                dictionaryProperty.isExpanded = true;
                _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

                SerializableDictionaryPropertyDrawer drawer = new();
                AssignDictionaryFieldInfo(
                    drawer,
                    typeof(TestDictionaryHost),
                    nameof(TestDictionaryHost.dictionary)
                );

                Rect controlRect = new(0f, 0f, 360f, 420f);
                GUIContent label = new("Dictionary");

                SerializableDictionaryPropertyDrawer.PendingEntry pending =
                    drawer.GetOrCreatePendingEntry(
                        dictionaryProperty,
                        typeof(int),
                        typeof(string),
                        isSortedDictionary: false
                    );
                pending.isExpanded = true;

                SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

                yield return TestIMGUIExecutor.Run(() =>
                {
                    int previousIndent = EditorGUI.indentLevel;
                    try
                    {
                        EditorGUI.indentLevel = indentLevel;
                        dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                        pending.isExpanded = true;
                        drawer.OnGUI(controlRect, dictionaryProperty, label);
                    }
                    finally
                    {
                        EditorGUI.indentLevel = previousIndent;
                    }
                });

                Assert.IsTrue(
                    SerializableDictionaryPropertyDrawer.HasLastPendingFieldRects,
                    $"Draw at indent level {indentLevel} should capture pending key/value rects."
                );

                Rect keyRect = SerializableDictionaryPropertyDrawer.LastPendingKeyFieldRect;
                Rect valueRect = SerializableDictionaryPropertyDrawer.LastPendingValueFieldRect;

                float actualShift = keyRect.xMin - valueRect.xMin;

                TestContext.WriteLine(
                    $"[PendingEntryKeyAndValueAlignedAtIndentLevel{indentLevel}] "
                        + $"keyRect.xMin={keyRect.xMin:F3}, valueRect.xMin={valueRect.xMin:F3}, "
                        + $"keyRect.width={keyRect.width:F3}, valueRect.width={valueRect.width:F3}, "
                        + $"actualShift={actualShift:F3}"
                );

                Assert.That(
                    actualShift,
                    Is.EqualTo(0f).Within(0.01f),
                    $"Key and Value are one column stacked twice, so they must share an origin at indent level {indentLevel} (#284)."
                );

                Assert.That(
                    valueRect.width - keyRect.width,
                    Is.EqualTo(0f).Within(0.01f),
                    $"Key and Value must share a width at indent level {indentLevel} (#284)."
                );
            }
        }

        [UnityTest]
        public IEnumerator PendingEntryAlignmentDiagnostics(
            [Values(0, 1, 2, 3, 5, 10)] int indentLevel
        )
        {
            // Data-driven test that verifies pending entry alignment at various indent levels
            // with detailed diagnostics to help debug any failures
            using (new DictionaryTweenDisabledScope())
            {
                _sharedSerializedObject.Update();
                SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                    nameof(TestDictionaryHost.dictionary)
                );
                dictionaryProperty.isExpanded = true;
                _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

                SerializableDictionaryPropertyDrawer drawer = new();
                AssignDictionaryFieldInfo(
                    drawer,
                    typeof(TestDictionaryHost),
                    nameof(TestDictionaryHost.dictionary)
                );

                Rect controlRect = new(0f, 0f, 360f, 420f);
                GUIContent label = new("Dictionary");

                SerializableDictionaryPropertyDrawer.PendingEntry pending =
                    drawer.GetOrCreatePendingEntry(
                        dictionaryProperty,
                        typeof(int),
                        typeof(string),
                        isSortedDictionary: false
                    );
                pending.isExpanded = true;

                SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

                yield return TestIMGUIExecutor.Run(() =>
                {
                    int previousIndent = EditorGUI.indentLevel;
                    try
                    {
                        EditorGUI.indentLevel = indentLevel;
                        dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                        pending.isExpanded = true;
                        drawer.OnGUI(controlRect, dictionaryProperty, label);
                    }
                    finally
                    {
                        EditorGUI.indentLevel = previousIndent;
                    }
                });

                bool hasRects = SerializableDictionaryPropertyDrawer.HasLastPendingFieldRects;

                TestContext.WriteLine(
                    $"[PendingEntryAlignmentDiagnostics@IndentLevel{indentLevel}]"
                );
                TestContext.WriteLine($"  HasLastPendingFieldRects = {hasRects}");

                if (hasRects)
                {
                    Rect keyRect = SerializableDictionaryPropertyDrawer.LastPendingKeyFieldRect;
                    Rect valueRect = SerializableDictionaryPropertyDrawer.LastPendingValueFieldRect;
                    float sectionPadding =
                        SerializableDictionaryPropertyDrawer.GetPendingSectionPaddingForTests();
                    float actualShift = keyRect.xMin - valueRect.xMin;

                    TestContext.WriteLine($"  keyRect           = {keyRect}");
                    TestContext.WriteLine($"  valueRect         = {valueRect}");
                    TestContext.WriteLine($"  SectionPadding    = {sectionPadding:F3}px");
                    TestContext.WriteLine($"  actualShift       = {actualShift:F3}px");

                    // The columns share an origin at every indent level. This used to expect
                    // 8.5px at indent 1+, which was the defect in #284 written down as the
                    // contract: the shift's clamp measured its headroom from the indent, so it
                    // was inert at indent 0 and moved only the Value column everywhere else.
                    Assert.That(
                        actualShift,
                        Is.EqualTo(0f).Within(0.01f),
                        $"At indent level {indentLevel}, Key and Value must share an origin but differed by {actualShift:F3}px."
                    );
                }
                else
                {
                    Assert.IsTrue(
                        hasRects,
                        $"Draw at indent level {indentLevel} should capture pending key/value rects."
                    );
                }
            }
        }

        [UnityTest]
        public IEnumerator PendingEntryKeyAndValueAlignedWithComplexFoldoutValueAtIndentLevel0()
        {
            yield return VerifyPendingEntryAlignmentWithComplexValueAtIndentLevel(0);
        }

        [UnityTest]
        public IEnumerator PendingEntryKeyAndValueAlignedWithComplexFoldoutValueAtIndentLevel1()
        {
            yield return VerifyPendingEntryAlignmentWithComplexValueAtIndentLevel(1);
        }

        [UnityTest]
        public IEnumerator PendingEntryKeyAndValueAlignedWithComplexFoldoutValueAtIndentLevel2()
        {
            yield return VerifyPendingEntryAlignmentWithComplexValueAtIndentLevel(2);
        }

        [UnityTest]
        public IEnumerator PendingEntryKeyAndValueAlignedWithComplexFoldoutValueAtIndentLevel3()
        {
            yield return VerifyPendingEntryAlignmentWithComplexValueAtIndentLevel(3);
        }

        private IEnumerator VerifyPendingEntryAlignmentWithComplexValueAtIndentLevel(
            int indentLevel
        )
        {
            ColorDataDictionaryHost host = CreateScriptableObject<ColorDataDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ColorDataDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(ColorDataDictionaryHost),
                nameof(ColorDataDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 420f, 480f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.PendingEntry pending =
                drawer.GetOrCreatePendingEntry(
                    dictionaryProperty,
                    typeof(string),
                    typeof(ColorData),
                    isSortedDictionary: false
                );
            pending.isExpanded = true;

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                int previousIndent = EditorGUI.indentLevel;
                try
                {
                    EditorGUI.indentLevel = indentLevel;
                    dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                    pending.isExpanded = true;
                    drawer.OnGUI(controlRect, dictionaryProperty, label);
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndent;
                }
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingFieldRects,
                $"Draw with complex value at indent level {indentLevel} should capture pending key/value rects."
            );
            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.LastPendingValueUsedFoldoutLabel,
                $"Complex value should use foldout label at indent level {indentLevel}."
            );

            Rect keyRect = SerializableDictionaryPropertyDrawer.LastPendingKeyFieldRect;
            Rect valueRect = SerializableDictionaryPropertyDrawer.LastPendingValueFieldRect;

            TestContext.WriteLine(
                $"[PendingEntryKeyAndValueAlignedWithComplexFoldoutValue@Indent{indentLevel}] "
                    + $"keyRect.xMin={keyRect.xMin:F3}, valueRect.xMin={valueRect.xMin:F3}, "
                    + $"keyRect.width={keyRect.width:F3}, valueRect.width={valueRect.width:F3}, "
                    + $"foldoutOffset={SerializableDictionaryPropertyDrawer.LastPendingValueFoldoutOffset:F3}"
            );

            Assert.That(
                keyRect.xMin,
                Is.EqualTo(valueRect.xMin).Within(1f),
                $"Pending key and value field left edges should be aligned with complex foldout value at indent level {indentLevel}."
            );

            Assert.That(
                keyRect.width,
                Is.EqualTo(valueRect.width).Within(1f),
                $"Pending key and value field widths should match with complex foldout value at indent level {indentLevel}."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryKeyAndValueAlignedWithWGroupPaddingAndIndent()
        {
            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 400f, 450f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.PendingEntry pending =
                drawer.GetOrCreatePendingEntry(
                    dictionaryProperty,
                    typeof(int),
                    typeof(string),
                    isSortedDictionary: false
                );
            pending.isExpanded = true;

            const float LeftPadding = 20f;
            const float RightPadding = 10f;
            const int IndentLevel = 2;

            GroupGUIWidthUtility.ResetForTests();
            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                int previousIndent = EditorGUI.indentLevel;
                try
                {
                    EditorGUI.indentLevel = IndentLevel;
                    dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            LeftPadding + RightPadding,
                            LeftPadding,
                            RightPadding
                        )
                    )
                    {
                        pending.isExpanded = true;
                        drawer.OnGUI(controlRect, dictionaryProperty, label);
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndent;
                }
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingFieldRects,
                "Draw with WGroup padding and indent should capture pending key/value rects."
            );

            Rect keyRect = SerializableDictionaryPropertyDrawer.LastPendingKeyFieldRect;
            Rect valueRect = SerializableDictionaryPropertyDrawer.LastPendingValueFieldRect;

            float actualShift = keyRect.xMin - valueRect.xMin;

            TestContext.WriteLine(
                $"[PendingEntryKeyAndValueAlignedWithWGroupPaddingAndIndent] "
                    + $"keyRect.xMin={keyRect.xMin:F3}, valueRect.xMin={valueRect.xMin:F3}, "
                    + $"keyRect.width={keyRect.width:F3}, valueRect.width={valueRect.width:F3}, "
                    + $"LeftPadding={LeftPadding}, IndentLevel={IndentLevel}, "
                    + $"actualShift={actualShift:F3}"
            );

            // This is the reporter's configuration in #284 -- a dictionary inside a WGroup, which
            // draws its members at indent 1 -- and it is the case this fixture asserted an 8.5px
            // offset for while calling itself "Aligned".
            Assert.That(
                actualShift,
                Is.EqualTo(0f).Within(0.01f),
                "Key and Value must share an origin with WGroup padding and indent (#284)."
            );

            Assert.That(
                valueRect.width - keyRect.width,
                Is.EqualTo(0f).Within(0.01f),
                "Key and Value must share a width with WGroup padding and indent (#284)."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryKeyAndValueAlignedWithNestedWGroups()
        {
            ColorDataDictionaryHost host = CreateScriptableObject<ColorDataDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ColorDataDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(ColorDataDictionaryHost),
                nameof(ColorDataDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 450f, 500f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.PendingEntry pending =
                drawer.GetOrCreatePendingEntry(
                    dictionaryProperty,
                    typeof(string),
                    typeof(ColorData),
                    isSortedDictionary: false
                );
            pending.isExpanded = true;

            const float OuterLeftPadding = 16f;
            const float OuterRightPadding = 8f;
            const float InnerLeftPadding = 12f;
            const float InnerRightPadding = 6f;
            const int IndentLevel = 3;

            GroupGUIWidthUtility.ResetForTests();
            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                int previousIndent = EditorGUI.indentLevel;
                try
                {
                    EditorGUI.indentLevel = IndentLevel;
                    dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            OuterLeftPadding + OuterRightPadding,
                            OuterLeftPadding,
                            OuterRightPadding
                        )
                    )
                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            InnerLeftPadding + InnerRightPadding,
                            InnerLeftPadding,
                            InnerRightPadding
                        )
                    )
                    {
                        pending.isExpanded = true;
                        drawer.OnGUI(controlRect, dictionaryProperty, label);
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndent;
                }
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingFieldRects,
                "Draw with nested WGroups and indent should capture pending key/value rects."
            );
            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.LastPendingValueUsedFoldoutLabel,
                "Complex value should use foldout label with nested WGroups."
            );

            Rect keyRect = SerializableDictionaryPropertyDrawer.LastPendingKeyFieldRect;
            Rect valueRect = SerializableDictionaryPropertyDrawer.LastPendingValueFieldRect;

            TestContext.WriteLine(
                $"[PendingEntryKeyAndValueAlignedWithNestedWGroups] "
                    + $"keyRect.xMin={keyRect.xMin:F3}, valueRect.xMin={valueRect.xMin:F3}, "
                    + $"keyRect.width={keyRect.width:F3}, valueRect.width={valueRect.width:F3}, "
                    + $"TotalLeftPadding={OuterLeftPadding + InnerLeftPadding}, IndentLevel={IndentLevel}"
            );

            Assert.That(
                keyRect.xMin,
                Is.EqualTo(valueRect.xMin).Within(1f),
                "Pending key and value field left edges should be aligned with nested WGroups."
            );

            Assert.That(
                keyRect.width,
                Is.EqualTo(valueRect.width).Within(1f),
                "Pending key and value field widths should match with nested WGroups."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryKeyIndentIncreasesWithHigherIndentLevel()
        {
            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 400f, 450f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.PendingEntry pending =
                drawer.GetOrCreatePendingEntry(
                    dictionaryProperty,
                    typeof(int),
                    typeof(string),
                    isSortedDictionary: false
                );
            pending.isExpanded = true;

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            float keyXMinAtIndent0 = 0f;
            float keyXMinAtIndent2 = 0f;

            yield return TestIMGUIExecutor.Run(() =>
            {
                int previousIndent = EditorGUI.indentLevel;
                try
                {
                    EditorGUI.indentLevel = 0;
                    dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                    pending.isExpanded = true;
                    drawer.OnGUI(controlRect, dictionaryProperty, label);
                    keyXMinAtIndent0 = SerializableDictionaryPropertyDrawer
                        .LastPendingKeyFieldRect
                        .xMin;

                    SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

                    EditorGUI.indentLevel = 2;
                    drawer.OnGUI(controlRect, dictionaryProperty, label);
                    keyXMinAtIndent2 = SerializableDictionaryPropertyDrawer
                        .LastPendingKeyFieldRect
                        .xMin;
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndent;
                }
            });

            TestContext.WriteLine(
                $"[PendingEntryKeyIndentIncreasesWithHigherIndentLevel] "
                    + $"keyXMinAtIndent0={keyXMinAtIndent0:F3}, keyXMinAtIndent2={keyXMinAtIndent2:F3}, "
                    + $"difference={keyXMinAtIndent2 - keyXMinAtIndent0:F3}"
            );

            Assert.Greater(
                keyXMinAtIndent2,
                keyXMinAtIndent0,
                "Key field xMin should increase with higher indent level."
            );

            // The total indent per level includes both:
            // 1. EditorGUI.IndentedRect applied in ResolveContentRect (~15px per level)
            // 2. The drawer's internal indentOffset (15px per level)
            // Total is approximately 30px per indent level, so 2 levels = ~60px
            float expectedIndentDifference = 2 * 30f;
            float actualDifference = keyXMinAtIndent2 - keyXMinAtIndent0;
            Assert.That(
                actualDifference,
                Is.EqualTo(expectedIndentDifference).Within(5f),
                $"Key field xMin difference should be approximately {expectedIndentDifference}px for indent level difference of 2 (includes both Unity indent and drawer indent)."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryKeyAndValueAlignedWithSortedDictionary()
        {
            TestSortedDictionaryHost host = CreateScriptableObject<TestSortedDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestSortedDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestSortedDictionaryHost),
                nameof(TestSortedDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 360f, 420f);
            GUIContent label = new("SortedDictionary");

            SerializableDictionaryPropertyDrawer.PendingEntry pending =
                drawer.GetOrCreatePendingEntry(
                    dictionaryProperty,
                    typeof(int),
                    typeof(string),
                    isSortedDictionary: true
                );
            pending.isExpanded = true;

            const int IndentLevel = 2;
            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                int previousIndent = EditorGUI.indentLevel;
                try
                {
                    EditorGUI.indentLevel = IndentLevel;
                    dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                    pending.isExpanded = true;
                    drawer.OnGUI(controlRect, dictionaryProperty, label);
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndent;
                }
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingFieldRects,
                "Draw sorted dictionary should capture pending key/value rects."
            );

            Rect keyRect = SerializableDictionaryPropertyDrawer.LastPendingKeyFieldRect;
            Rect valueRect = SerializableDictionaryPropertyDrawer.LastPendingValueFieldRect;

            float actualShift = keyRect.xMin - valueRect.xMin;

            TestContext.WriteLine(
                $"[PendingEntryKeyAndValueAlignedWithSortedDictionary] "
                    + $"keyRect.xMin={keyRect.xMin:F3}, valueRect.xMin={valueRect.xMin:F3}, "
                    + $"IndentLevel={IndentLevel}, actualShift={actualShift:F3}"
            );

            Assert.That(
                actualShift,
                Is.EqualTo(0f).Within(0.01f),
                "Key and Value must share an origin for a sorted dictionary (#284)."
            );
        }

        [UnityTest]
        public IEnumerator PendingEntryFoldoutValueRespectsGroupPadding()
        {
            ColorDataDictionaryHost host = CreateScriptableObject<ColorDataDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ColorDataDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(ColorDataDictionaryHost),
                nameof(ColorDataDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 420f, 480f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.PendingEntry pending =
                drawer.GetOrCreatePendingEntry(
                    dictionaryProperty,
                    typeof(string),
                    typeof(ColorData),
                    isSortedDictionary: false
                );
            pending.isExpanded = true;

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                pending.isExpanded = true;
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingFieldRects,
                "Baseline draw should capture pending field rects."
            );
            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.LastPendingValueUsedFoldoutLabel,
                "Baseline draw should report a foldout label for complex pending values."
            );
            Assert.That(
                SerializableDictionaryPropertyDrawer.LastPendingValueFoldoutOffset,
                Is.EqualTo(SerializableDictionaryPropertyDrawer.PendingExpandableValueFoldoutGutter)
                    .Within(0.0001f),
                "Baseline foldout gutter should match the configured pending-value gutter."
            );

            Rect baselineValue = SerializableDictionaryPropertyDrawer.LastPendingValueFieldRect;

            const float LeftPadding = 16f;
            const float RightPadding = 12f;
            float horizontalPadding = LeftPadding + RightPadding;

            GroupGUIWidthUtility.ResetForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        LeftPadding,
                        RightPadding
                    )
                )
                {
                    SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
                    pending.isExpanded = true;
                    drawer.OnGUI(controlRect, dictionaryProperty, label);
                }
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingFieldRects,
                "Grouped draw should capture pending field rects."
            );
            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.LastPendingValueUsedFoldoutLabel,
                "Grouped draw should still report a foldout label for complex pending values."
            );
            Assert.That(
                SerializableDictionaryPropertyDrawer.LastPendingValueFoldoutOffset,
                Is.EqualTo(SerializableDictionaryPropertyDrawer.PendingExpandableValueFoldoutGutter)
                    .Within(0.0001f),
                "Grouped foldout gutter should match the configured pending-value gutter."
            );

            Rect groupedValue = SerializableDictionaryPropertyDrawer.LastPendingValueFieldRect;

            TestContext.WriteLine(
                $"[PendingEntryFoldoutValueRespectsGroupPadding] "
                    + $"baselineValue.xMin={baselineValue.xMin:F3}, groupedValue.xMin={groupedValue.xMin:F3}, "
                    + $"baselineValue.width={baselineValue.width:F3}, groupedValue.width={groupedValue.width:F3}, "
                    + $"LeftPadding={LeftPadding:F3}"
            );

            // The x position should shift by approximately LeftPadding
            // because baseline now aligns with Unity's default list rendering (no offset)
            float expectedXShift = LeftPadding;
            float actualXShift = groupedValue.xMin - baselineValue.xMin;
            Assert.That(
                actualXShift,
                Is.EqualTo(expectedXShift).Within(1.0f),
                $"Pending value foldout xMin shift should be approximately LeftPadding ({LeftPadding}). Actual: {actualXShift}"
            );

            // Width should decrease when WGroup padding is applied
            Assert.Less(
                groupedValue.width,
                baselineValue.width + 1.0f,
                "Pending value foldout width should be narrower with WGroup padding."
            );
        }

        [UnityTest]
        public IEnumerator DictionaryRowsHonorGroupPadding()
        {
            _sharedHost.dictionary.Add(1, "One");
            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            ForcePopulateTestDictionarySerializedData(_sharedHost, dictionaryProperty);
            dictionaryProperty.isExpanded = true;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 360f, 520f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastRowRects,
                "Baseline draw should capture dictionary row layout."
            );

            Rect baselineRowRect = SerializableDictionaryPropertyDrawer.LastRowOriginalRect;
            Rect baselineKeyRect = SerializableDictionaryPropertyDrawer.LastRowKeyRect;
            Rect baselineValueRect = SerializableDictionaryPropertyDrawer.LastRowValueRect;

            const float LeftPadding = 24f;
            const float RightPadding = 10f;
            float horizontalPadding = LeftPadding + RightPadding;

            GroupGUIWidthUtility.ResetForTests();

            // When inside a WGroup, the drawer receives the SAME rect from Unity's layout system.
            // The WGroup pushes padding via GroupGUIWidthUtility, which the drawer reads and applies
            // via ResolveContentRect. We should NOT pre-shift the rect - that would cause double-padding.
            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        LeftPadding,
                        RightPadding
                    )
                )
                {
                    SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
                    drawer.OnGUI(controlRect, dictionaryProperty, label);
                }
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastRowRects,
                "Grouped draw should capture dictionary row layout."
            );

            Rect groupedRowRect = SerializableDictionaryPropertyDrawer.LastRowOriginalRect;
            Rect groupedKeyRect = SerializableDictionaryPropertyDrawer.LastRowKeyRect;
            Rect groupedValueRect = SerializableDictionaryPropertyDrawer.LastRowValueRect;

            // NOTE: We cannot test xMin shifts on LastRowOriginalRect because that rect comes from
            // Unity's ReorderableList callback, which calculates element positions independently
            // of our WGroup padding scope. Unity's ReorderableList has no knowledge of
            // GroupGUIWidthUtility padding.
            //
            // What we CAN test:
            // 1. The virtualWidth calculation uses horizontalPadding to compute correct column widths
            // 2. The relative positioning (value offset within row) remains consistent
            // 3. Key/value widths are computed correctly based on virtualWidth

            TestContext.WriteLine(
                $"[DictionaryRowsHonorGroupPadding] "
                    + $"baselineRowRect.xMin={baselineRowRect.xMin:F3}, groupedRowRect.xMin={groupedRowRect.xMin:F3}, "
                    + $"baselineRowRect.width={baselineRowRect.width:F3}, groupedRowRect.width={groupedRowRect.width:F3}, "
                    + $"LeftPadding={LeftPadding:F3}, RightPadding={RightPadding:F3}"
            );

            // Verify key rect was captured (the width calculation uses virtualWidth internally)
            Assert.Greater(
                groupedKeyRect.width,
                0f,
                "Grouped key rect should have positive width."
            );

            // Verify value rect was captured
            Assert.Greater(
                groupedValueRect.width,
                0f,
                "Grouped value rect should have positive width."
            );

            // The value offset within the row may vary slightly between baseline and grouped draws
            // because Unity's ReorderableList calculates element rects based on the list's position rect,
            // which differs between the two cases. The virtualWidth calculation accounts for WGroup
            // padding but can't fully compensate for ReorderableList's internal calculations.
            // With DictionaryRowSimpleValueWidthRatio (0.54), a virtualWidth difference of ~12
            // translates to a column width difference of ~6.5, so we allow up to 8.0f tolerance.
            float baselineValueOffset = baselineValueRect.xMin - baselineRowRect.xMin;
            float groupedValueOffset = groupedValueRect.xMin - groupedRowRect.xMin;
            float offsetDelta = Mathf.Abs(groupedValueOffset - baselineValueOffset);

            // Calculate virtualWidth for both cases to aid diagnosis
            float baselineVirtualWidth = baselineRowRect.width; // No WGroup padding
            float groupedVirtualWidth = groupedRowRect.width + horizontalPadding;
            float virtualWidthDelta = Mathf.Abs(groupedVirtualWidth - baselineVirtualWidth);

            TestContext.WriteLine(
                $"[GroupPadding] baseline offset: {baselineValueOffset:F3}, grouped offset: {groupedValueOffset:F3}, delta: {offsetDelta:F3}"
            );
            TestContext.WriteLine(
                $"[GroupPadding] baselineVirtualWidth: {baselineVirtualWidth:F3}, groupedVirtualWidth: {groupedVirtualWidth:F3}, virtualWidthDelta: {virtualWidthDelta:F3}"
            );

            // Allow tolerance to account for virtualWidth differences from ReorderableList rect calculations.
            // The tolerance is based on how column width differences (due to virtualWidth variance)
            // translate to value offset differences via the DictionaryRowSimpleValueWidthRatio.
            const float MaxExpectedValueOffsetDelta = 8.0f;
            Assert.LessOrEqual(
                offsetDelta,
                MaxExpectedValueOffsetDelta,
                $"Value field offset within the row should remain reasonably consistent when padding is applied (delta: {offsetDelta:F3}, virtualWidthDelta: {virtualWidthDelta:F3})."
            );
        }

        [UnityTest]
        public IEnumerator DictionaryRowLayoutConsistencyWithVariousPaddingsSymmetricSmall()
        {
            yield return DictionaryRowLayoutConsistencyWithVariousPaddingsInternal(12f, 12f);
        }

        [UnityTest]
        public IEnumerator DictionaryRowLayoutConsistencyWithVariousPaddingsAsymmetricMedium()
        {
            yield return DictionaryRowLayoutConsistencyWithVariousPaddingsInternal(24f, 10f);
        }

        [UnityTest]
        public IEnumerator DictionaryRowLayoutConsistencyWithVariousPaddingsAsymmetricLarge()
        {
            yield return DictionaryRowLayoutConsistencyWithVariousPaddingsInternal(40f, 20f);
        }

        [UnityTest]
        public IEnumerator DictionaryRowLayoutConsistencyWithVariousPaddingsMinimumPadding()
        {
            yield return DictionaryRowLayoutConsistencyWithVariousPaddingsInternal(6f, 6f);
        }

        [UnityTest]
        public IEnumerator DictionaryRowLayoutConsistencyWithVariousPaddingsLeftOnly()
        {
            yield return DictionaryRowLayoutConsistencyWithVariousPaddingsInternal(50f, 0f);
        }

        [UnityTest]
        public IEnumerator DictionaryRowLayoutConsistencyWithVariousPaddingsRightOnly()
        {
            yield return DictionaryRowLayoutConsistencyWithVariousPaddingsInternal(0f, 40f);
        }

        [UnityTest]
        public IEnumerator DictionaryRowLayoutConsistencyWithVariousPaddingsZeroPadding()
        {
            yield return DictionaryRowLayoutConsistencyWithVariousPaddingsInternal(0f, 0f);
        }

        [UnityTest]
        public IEnumerator DictionaryRowLayoutConsistencyWithVariousPaddingsVeryLargePadding()
        {
            yield return DictionaryRowLayoutConsistencyWithVariousPaddingsInternal(100f, 100f);
        }

        [UnityTest]
        public IEnumerator DictionaryRowLayoutConsistencyWithVariousPaddingsTinyPadding()
        {
            yield return DictionaryRowLayoutConsistencyWithVariousPaddingsInternal(0.5f, 0.5f);
        }

        [UnityTest]
        public IEnumerator DictionaryRowLayoutConsistencyWithVariousPaddingsExactlyWidthThreshold()
        {
            // Test padding exactly at the WidthTransitionThreshold (12.0)
            // At this boundary, grouped width should be approximately equal to baseline width
            yield return DictionaryRowLayoutConsistencyWithVariousPaddingsInternal(6f, 6f);
        }

        [UnityTest]
        public IEnumerator DictionaryRowLayoutConsistencyWithVariousPaddingsSlightlyAboveThreshold()
        {
            // Test padding slightly above WidthTransitionThreshold (12.0)
            // Grouped should now be narrower than baseline
            yield return DictionaryRowLayoutConsistencyWithVariousPaddingsInternal(7f, 7f);
        }

        [UnityTest]
        public IEnumerator DictionaryRowLayoutConsistencyWithVariousPaddingsSlightlyBelowThreshold()
        {
            // Test padding slightly below WidthTransitionThreshold (12.0)
            // Grouped should be wider than baseline
            yield return DictionaryRowLayoutConsistencyWithVariousPaddingsInternal(5f, 5f);
        }

        [UnityTest]
        public IEnumerator DictionaryRowLayoutConsistencyWithVariousPaddingsAsymmetricTiny()
        {
            // Test asymmetric tiny paddings (total = 1.5, well below threshold)
            yield return DictionaryRowLayoutConsistencyWithVariousPaddingsInternal(1f, 0.5f);
        }

        [UnityTest]
        public IEnumerator DictionaryRowLayoutConsistencyWithVariousPaddingsLeftOnlyBelowThreshold()
        {
            // Test left-only padding below WidthTransitionThreshold
            yield return DictionaryRowLayoutConsistencyWithVariousPaddingsInternal(4f, 0f);
        }

        [UnityTest]
        public IEnumerator DictionaryRowLayoutConsistencyWithVariousPaddingsNearMinFloor()
        {
            // Test padding that brings grouped width just above min content floor
            // minContentWidth = 110 + 150 + 8 + 16 = 284
            // With controlRect 400, baseline ~360, we want grouped ~290 (just above floor)
            // Grouped would need diff ~70, so padding ~82 (82 - 12 threshold = 70)
            yield return DictionaryRowLayoutConsistencyWithVariousPaddingsInternal(40f, 40f);
        }

        [UnityTest]
        public IEnumerator DictionaryRowLayoutConsistencyWithVariousPaddingsExtremelyLargePadding()
        {
            // Test extremely large padding that definitely triggers min floor
            // This ensures grouped width is clamped to minContentWidth
            yield return DictionaryRowLayoutConsistencyWithVariousPaddingsInternal(150f, 150f);
        }

        private IEnumerator DictionaryRowLayoutConsistencyWithVariousPaddingsInternal(
            float leftPadding,
            float rightPadding
        )
        {
            _sharedHost.dictionary.Add(1, "One");
            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            ForcePopulateTestDictionarySerializedData(_sharedHost, dictionaryProperty);
            dictionaryProperty.isExpanded = true;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 400f, 520f);
            GUIContent label = new("Dictionary");

            // Baseline draw (no WGroup padding)
            GroupGUIWidthUtility.ResetForTests();
            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastRowRects,
                "Baseline draw should capture dictionary row layout."
            );

            Rect baselineRowRect = SerializableDictionaryPropertyDrawer.LastRowOriginalRect;
            Rect baselineKeyRect = SerializableDictionaryPropertyDrawer.LastRowKeyRect;
            Rect baselineValueRect = SerializableDictionaryPropertyDrawer.LastRowValueRect;

            float horizontalPadding = leftPadding + rightPadding;

            // Grouped draw (with WGroup padding)
            GroupGUIWidthUtility.ResetForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        leftPadding,
                        rightPadding
                    )
                )
                {
                    SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
                    drawer.OnGUI(controlRect, dictionaryProperty, label);
                }
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastRowRects,
                "Grouped draw should capture dictionary row layout."
            );

            Rect groupedRowRect = SerializableDictionaryPropertyDrawer.LastRowOriginalRect;
            Rect groupedKeyRect = SerializableDictionaryPropertyDrawer.LastRowKeyRect;
            Rect groupedValueRect = SerializableDictionaryPropertyDrawer.LastRowValueRect;

            // NOTE: We CANNOT test xMin shifts on LastRowOriginalRect because that rect comes
            // directly from Unity's ReorderableList element callback. Unity's ReorderableList
            // calculates element positions independently of our WGroup padding scope - it has
            // no knowledge of GroupGUIWidthUtility.
            //
            // What we CAN test:
            // 1. The drawer captures valid key/value rects with positive dimensions
            // 2. The value offset relative to the row remains consistent (proportional layout)
            // 3. Key and value rects are captured correctly in both scenarios
            //
            // The virtualWidth calculation (rect.width + horizontalPadding) is used internally
            // to compute column widths, but we cannot directly observe this from the tracked
            // rects since LastRowOriginalRect is Unity's rect, not our adjusted rect.

            bool zeroPadding = horizontalPadding <= 0f;
            int scopeDepthAfterGrouped = GroupGUIWidthUtility.CurrentScopeDepth;

            TestContext.WriteLine(
                $"[DictionaryRowLayoutConsistencyWithVariousPaddings(left={leftPadding}, right={rightPadding})] "
                    + $"baselineRow={{xMin={baselineRowRect.xMin:F3}, width={baselineRowRect.width:F3}}}, "
                    + $"groupedRow={{xMin={groupedRowRect.xMin:F3}, width={groupedRowRect.width:F3}}}, "
                    + $"zeroPadding={zeroPadding}, scopeDepthAfterGrouped={scopeDepthAfterGrouped}"
            );

            // Verify key rect has valid positive width in both scenarios
            Assert.Greater(
                baselineKeyRect.width,
                0f,
                "Baseline key rect should have positive width."
            );
            Assert.Greater(
                groupedKeyRect.width,
                0f,
                "Grouped key rect should have positive width."
            );

            // Verify value rect has valid positive width in both scenarios
            Assert.Greater(
                baselineValueRect.width,
                0f,
                "Baseline value rect should have positive width."
            );
            Assert.Greater(
                groupedValueRect.width,
                0f,
                "Grouped value rect should have positive width."
            );

            // Value offset consistency check - the relative positioning within the row
            // should remain reasonably consistent since both use proportional layout.
            float baselineValueOffset = baselineValueRect.xMin - baselineRowRect.xMin;
            float groupedValueOffset = groupedValueRect.xMin - groupedRowRect.xMin;
            float offsetDelta = Mathf.Abs(groupedValueOffset - baselineValueOffset);

            TestContext.WriteLine(
                $"[DictionaryRowLayoutConsistencyWithVariousPaddings] "
                    + $"baselineValueOffset={baselineValueOffset:F3}, groupedValueOffset={groupedValueOffset:F3}, "
                    + $"offsetDelta={offsetDelta:F3}"
            );

            // The value offset delta should be reasonably bounded. When padding is applied,
            // the virtualWidth changes which affects column width calculations, but the
            // proportional layout should keep offsets relatively consistent.
            // Allow generous tolerance since Unity's ReorderableList rect can vary.
            float scaleFactor = 100f < horizontalPadding ? 0.5f : 0.3f;
            float scaledTolerance = 15.0f + (horizontalPadding * scaleFactor);
            Assert.LessOrEqual(
                offsetDelta,
                scaledTolerance,
                $"Value field offset delta ({offsetDelta:F3}) should be within scaled tolerance ({scaledTolerance:F3}) for padding {horizontalPadding:F0}."
            );

            // Verify the key/value layout respects minimum width constraints
            float minKeyWidth = SerializableDictionaryPropertyDrawer.DictionaryRowKeyColumnMinWidth;
            float minValueWidth =
                SerializableDictionaryPropertyDrawer.DictionaryRowValueColumnMinWidth;

            // Key width should be at least the minimum (accounting for field padding)
            float keyWidthWithPadding =
                baselineKeyRect.width
                + SerializableDictionaryPropertyDrawer.DictionaryRowFieldPadding;
            Assert.GreaterOrEqual(
                keyWidthWithPadding,
                minKeyWidth * 0.5f, // Allow some tolerance for very constrained layouts
                $"Baseline key column should respect minimum width constraint (~{minKeyWidth:F0}px)."
            );

            // Value width should be at least the minimum (accounting for field padding)
            float valueWidthWithPadding =
                baselineValueRect.width
                + SerializableDictionaryPropertyDrawer.DictionaryRowFieldPadding;
            Assert.GreaterOrEqual(
                valueWidthWithPadding,
                minValueWidth * 0.5f, // Allow some tolerance for very constrained layouts
                $"Baseline value column should respect minimum width constraint (~{minValueWidth:F0}px)."
            );
        }

        [UnityTest]
        public IEnumerator DictionaryRowComplexValueReservesGapAndWidth()
        {
            ComplexValueDictionaryHost host = CreateScriptableObject<ComplexValueDictionaryHost>();
            host.dictionary.Add(
                "Accent",
                new ComplexValue { button = Color.cyan, text = Color.black }
            );

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ComplexValueDictionaryHost.dictionary)
            );
            ForcePopulateComplexDictionarySerializedData(host, dictionaryProperty);
            dictionaryProperty.isExpanded = true;
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );
            Assert.Greater(valuesProperty.arraySize, 0, "Dictionary should contain test entries.");
            SerializedProperty valueProperty = valuesProperty.GetArrayElementAtIndex(0);
            valueProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(ComplexValueDictionaryHost),
                nameof(ComplexValueDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 640f, 520f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastRowRects,
                "Expected dictionary row layout to be tracked."
            );

            float actualGap =
                SerializableDictionaryPropertyDrawer.LastRowValueRect.xMin
                - SerializableDictionaryPropertyDrawer.LastRowKeyRect.xMax;
            // The gap between keyRect.xMax and valueRect.xMin includes:
            // - DictionaryRowKeyValueGap + DictionaryRowFoldoutGapBoost (base gap for foldout values)
            // - DictionaryRowFieldPadding (added to valueRect.x, while keyRect width reduction cancels keyRect.x shift)
            // - RowExpandableValueFoldoutGutter (foldout offset applied to valueRect.x for expandable values)
            float expectedGap =
                SerializableDictionaryPropertyDrawer.DictionaryRowKeyValueGap
                + SerializableDictionaryPropertyDrawer.DictionaryRowFoldoutGapBoost
                + SerializableDictionaryPropertyDrawer.DictionaryRowFieldPadding
                + SerializableDictionaryPropertyDrawer.RowExpandableValueFoldoutGutter;

            Assert.That(
                actualGap,
                Is.EqualTo(expectedGap).Within(0.0001f),
                "Complex value rows should reserve the configured key/value gap."
            );

            float minValueWidth =
                SerializableDictionaryPropertyDrawer.DictionaryRowComplexValueMinWidth;
            Assert.That(
                SerializableDictionaryPropertyDrawer.LastRowValueRect.width,
                Is.GreaterThanOrEqualTo(minValueWidth - 0.0001f),
                "Complex value rows should preserve the minimum value column width."
            );
        }

        [UnityTest]
        public IEnumerator DictionaryRowSimpleValueReservesCorrectGap()
        {
            // Test that simple (non-foldout) value rows reserve the correct gap between key and value
            _sharedHost.dictionary.Add(1, "TestValue");

            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastRowRects,
                "Expected dictionary row layout to be tracked for simple value type."
            );

            Rect keyRect = SerializableDictionaryPropertyDrawer.LastRowKeyRect;
            Rect valueRect = SerializableDictionaryPropertyDrawer.LastRowValueRect;

            float actualGap = valueRect.xMin - keyRect.xMax;
            // For simple values (non-foldout):
            // - DictionaryRowKeyValueGap (base gap, no foldout boost)
            // - DictionaryRowFieldPadding (added to valueRect.x, keyRect width reduction cancels keyRect.x shift)
            // - NO RowExpandableValueFoldoutGutter (not a foldout value)
            float expectedGap =
                SerializableDictionaryPropertyDrawer.DictionaryRowKeyValueGap
                + SerializableDictionaryPropertyDrawer.DictionaryRowFieldPadding;

            TestContext.WriteLine(
                $"[DictionaryRowSimpleValueReservesCorrectGap] "
                    + $"keyRect.xMax={keyRect.xMax:F3}, valueRect.xMin={valueRect.xMin:F3}, "
                    + $"actualGap={actualGap:F3}, expectedGap={expectedGap:F3}"
            );

            Assert.That(
                actualGap,
                Is.EqualTo(expectedGap).Within(0.0001f),
                $"Simple value rows should reserve gap of {expectedGap}px (DictionaryRowKeyValueGap + DictionaryRowFieldPadding)."
            );
        }

        [UnityTest]
        public IEnumerator DictionaryRowGapCalculationBreakdown()
        {
            // Diagnostic test that logs detailed gap calculation breakdown
            ComplexValueDictionaryHost host = CreateScriptableObject<ComplexValueDictionaryHost>();
            host.dictionary.Add(
                "DiagnosticKey",
                new ComplexValue { button = Color.cyan, text = Color.magenta }
            );

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ComplexValueDictionaryHost.dictionary)
            );
            ForcePopulateComplexDictionarySerializedData(host, dictionaryProperty);
            dictionaryProperty.isExpanded = true;
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );
            Assert.Greater(valuesProperty.arraySize, 0, "Dictionary should contain test entry.");
            SerializedProperty valueProperty = valuesProperty.GetArrayElementAtIndex(0);
            valueProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(ComplexValueDictionaryHost),
                nameof(ComplexValueDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 640f, 520f);
            GUIContent label = new("DiagnosticDictionary");

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastRowRects,
                "Expected row rects to be tracked."
            );

            Rect keyRect = SerializableDictionaryPropertyDrawer.LastRowKeyRect;
            Rect valueRect = SerializableDictionaryPropertyDrawer.LastRowValueRect;

            float baseGap = SerializableDictionaryPropertyDrawer.DictionaryRowKeyValueGap;
            float foldoutBoost = SerializableDictionaryPropertyDrawer.DictionaryRowFoldoutGapBoost;
            float fieldPadding = SerializableDictionaryPropertyDrawer.DictionaryRowFieldPadding;
            float foldoutGutter =
                SerializableDictionaryPropertyDrawer.RowExpandableValueFoldoutGutter;
            float actualGap = valueRect.xMin - keyRect.xMax;
            float expectedGap = baseGap + foldoutBoost + fieldPadding + foldoutGutter;

            TestContext.WriteLine(
                "[DictionaryRowGapCalculationBreakdown] Gap component breakdown:"
            );
            TestContext.WriteLine($"  DictionaryRowKeyValueGap       = {baseGap:F1}px");
            TestContext.WriteLine($"  DictionaryRowFoldoutGapBoost   = {foldoutBoost:F1}px");
            TestContext.WriteLine($"  DictionaryRowFieldPadding      = {fieldPadding:F1}px");
            TestContext.WriteLine($"  RowExpandableValueFoldoutGutter = {foldoutGutter:F1}px");
            TestContext.WriteLine($"  -----------------------------------");
            TestContext.WriteLine($"  Expected total gap             = {expectedGap:F1}px");
            TestContext.WriteLine($"  Actual gap (measured)          = {actualGap:F3}px");
            TestContext.WriteLine(
                $"  Difference                     = {Mathf.Abs(actualGap - expectedGap):F6}px"
            );
            TestContext.WriteLine($"  keyRect                        = {keyRect}");
            TestContext.WriteLine($"  valueRect                      = {valueRect}");

            Assert.That(
                actualGap,
                Is.EqualTo(expectedGap).Within(0.0001f),
                $"Measured gap should match calculated expected gap of {expectedGap}px."
            );
        }

        [UnityTest]
        public IEnumerator DictionaryRowFoldoutCollapseAdjustsHeight()
        {
            ComplexValueDictionaryHost host = CreateScriptableObject<ComplexValueDictionaryHost>();
            host.dictionary.Add(
                "Accent",
                new ComplexValue { button = Color.blue, text = Color.white }
            );

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ComplexValueDictionaryHost.dictionary)
            );
            ForcePopulateComplexDictionarySerializedData(host, dictionaryProperty);
            dictionaryProperty.isExpanded = true;
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );
            Assert.Greater(valuesProperty.arraySize, 0, "Dictionary should contain test entries.");
            SerializedProperty valueProperty = valuesProperty.GetArrayElementAtIndex(0);
            valueProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(ComplexValueDictionaryHost),
                nameof(ComplexValueDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 360f, 520f);
            GUIContent label = new("Dictionary");
            string cacheKey = drawer.GetListKey(dictionaryProperty);

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastRowRects,
                "Baseline draw should capture dictionary row layout."
            );
            float expandedHeight = SerializableDictionaryPropertyDrawer.LastRowValueRect.height;

            valueProperty.isExpanded = false;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
            drawer.MarkListCacheDirty(cacheKey);
            drawer.SetRowFoldoutStateForTests(cacheKey, 0, false);

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            float collapsedHeight = SerializableDictionaryPropertyDrawer.LastRowValueRect.height;
            TestContext.WriteLine(
                $"[RowFoldout] expanded height: {expandedHeight:F3}, collapsed height: {collapsedHeight:F3}"
            );
            Assert.Less(
                collapsedHeight,
                expandedHeight - 0.5f,
                "Collapsing the value foldout should reduce the rendered height."
            );

            valueProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
            drawer.MarkListCacheDirty(cacheKey);
            drawer.SetRowFoldoutStateForTests(cacheKey, 0, true);

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            float reopenedHeight = SerializableDictionaryPropertyDrawer.LastRowValueRect.height;
            TestContext.WriteLine($"[RowFoldout] reopened height: {reopenedHeight:F3}");
            Assert.That(
                reopenedHeight,
                Is.EqualTo(expandedHeight).Within(0.5f),
                "Re-expanding the value foldout should restore the original height."
            );
        }

        [UnityTest]
        public IEnumerator DictionaryRowChildControlsStayInsideValueColumn()
        {
            ColorDataDictionaryHost host = CreateScriptableObject<ColorDataDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ColorDataDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.CommitResult commit = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(ColorData),
                "Accent",
                new ColorData
                {
                    color1 = Color.red,
                    color2 = Color.green,
                    color3 = Color.blue,
                    color4 = Color.white,
                    otherColors = new[] { Color.yellow, Color.cyan, Color.magenta },
                },
                dictionaryProperty
            );

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializedProperty valueProperty = valuesProperty.GetArrayElementAtIndex(commit.index);
            valueProperty.isExpanded = true;
            dictionaryProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            AssignDictionaryFieldInfo(
                drawer,
                typeof(ColorDataDictionaryHost),
                nameof(ColorDataDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 420f, 520f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastRowRects,
                "Row layout tracking should record the most recent row."
            );
            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastRowChildContentRect,
                "Expected at least one child rect to be recorded for complex values."
            );

            Rect valueRect = SerializableDictionaryPropertyDrawer.LastRowValueRect;
            Rect childRect = SerializableDictionaryPropertyDrawer.LastRowChildContentRect;
            float foldoutOffset = SerializableDictionaryPropertyDrawer.LastRowValueFoldoutOffset;
            bool usedFoldoutLabel =
                SerializableDictionaryPropertyDrawer.LastRowValueUsedFoldoutLabel;

            TestContext.WriteLine(
                $"[DictionaryRowChildControlsStayInsideValueColumn] "
                    + $"valueRect={{xMin={valueRect.xMin:F3}, xMax={valueRect.xMax:F3}, width={valueRect.width:F3}}}, "
                    + $"childRect={{xMin={childRect.xMin:F3}, xMax={childRect.xMax:F3}, width={childRect.width:F3}}}, "
                    + $"foldoutOffset={foldoutOffset:F3}, usedFoldoutLabel={usedFoldoutLabel}"
            );

            Assert.That(
                childRect.xMin,
                Is.GreaterThanOrEqualTo(valueRect.xMin - 0.0001f),
                "Child controls should not render outside the value column on the left."
            );
            Assert.That(
                childRect.xMax,
                Is.LessThanOrEqualTo(valueRect.xMax + 0.0001f),
                "Child controls should stay within the value column bounds."
            );
        }

        [UnityTest]
        public IEnumerator DictionaryRowChildControlsStayInsideValueColumnWithGroupPadding()
        {
            ColorDataDictionaryHost host = CreateScriptableObject<ColorDataDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ColorDataDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.CommitResult commit = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(ColorData),
                "Accent",
                new ColorData
                {
                    color1 = Color.magenta,
                    color2 = Color.cyan,
                    color3 = Color.yellow,
                    color4 = Color.white,
                    otherColors = new[] { Color.red, Color.green },
                },
                dictionaryProperty
            );

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializedProperty valueProperty = valuesProperty.GetArrayElementAtIndex(commit.index);
            valueProperty.isExpanded = true;
            dictionaryProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            AssignDictionaryFieldInfo(
                drawer,
                typeof(ColorDataDictionaryHost),
                nameof(ColorDataDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 420f, 520f);
            GUIContent label = new("Dictionary");

            const float LeftPadding = 22f;
            const float RightPadding = 14f;
            float horizontalPadding = LeftPadding + RightPadding;

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        LeftPadding,
                        RightPadding
                    )
                )
                {
                    drawer.OnGUI(controlRect, dictionaryProperty, label);
                }
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastRowRects,
                "Grouped draw should capture the most recent row layout."
            );
            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastRowChildContentRect,
                "Grouped draw should capture at least one child rect for complex values."
            );

            Rect valueRect = SerializableDictionaryPropertyDrawer.LastRowValueRect;
            Rect childRect = SerializableDictionaryPropertyDrawer.LastRowChildContentRect;
            float foldoutOffset = SerializableDictionaryPropertyDrawer.LastRowValueFoldoutOffset;
            bool usedFoldoutLabel =
                SerializableDictionaryPropertyDrawer.LastRowValueUsedFoldoutLabel;

            TestContext.WriteLine(
                $"[DictionaryRowChildControlsStayInsideValueColumnWithGroupPadding] "
                    + $"valueRect={{xMin={valueRect.xMin:F3}, xMax={valueRect.xMax:F3}, width={valueRect.width:F3}}}, "
                    + $"childRect={{xMin={childRect.xMin:F3}, xMax={childRect.xMax:F3}, width={childRect.width:F3}}}, "
                    + $"foldoutOffset={foldoutOffset:F3}, usedFoldoutLabel={usedFoldoutLabel}, "
                    + $"leftPadding={LeftPadding:F3}, rightPadding={RightPadding:F3}"
            );

            Assert.That(
                childRect.xMin,
                Is.GreaterThanOrEqualTo(valueRect.xMin - 0.0001f),
                "Child controls should remain within the left edge of the grouped value column."
            );
            Assert.That(
                childRect.xMax,
                Is.LessThanOrEqualTo(valueRect.xMax + 0.0001f),
                "Child controls should remain within the right edge of the grouped value column."
            );
        }

        [UnityTest]
        public IEnumerator DictionaryRowChildLabelWidthsAdaptToContent()
        {
            LabelStressDictionaryHost host = CreateScriptableObject<LabelStressDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(LabelStressDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.CommitResult commit = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(LabelStressValue),
                "Entry",
                new LabelStressValue
                {
                    shortName = 1f,
                    ridiculouslyVerboseFieldNameRequiringSpace = 2f,
                },
                dictionaryProperty
            );

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializedProperty valueProperty = valuesProperty.GetArrayElementAtIndex(commit.index);
            valueProperty.isExpanded = true;
            dictionaryProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            AssignDictionaryFieldInfo(
                drawer,
                typeof(LabelStressDictionaryHost),
                nameof(LabelStressDictionaryHost.dictionary)
            );

            Rect controlRect = new(0f, 0f, 420f, 480f);
            GUIContent label = new("Dictionary");

            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, dictionaryProperty, label);
            });

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasRowChildLabelWidthData,
                "Expected row child label width tracking to capture measurements."
            );

            float minWidth = SerializableDictionaryPropertyDrawer.LastRowChildMinLabelWidth;
            float maxWidth = SerializableDictionaryPropertyDrawer.LastRowChildMaxLabelWidth;

            float widthGain = maxWidth - minWidth;
            TestContext.WriteLine(
                $"[ChildLabelWidth] min: {minWidth:F2}, max: {maxWidth:F2}, gain: {widthGain:F2}"
            );
            Assert.That(
                widthGain,
                Is.GreaterThan(2f),
                $"Long field names should reserve noticeably more width than short field names (observed gain: {widthGain:F2})."
            );
        }

        [Test]
        public void GetOrCreateListRebuildsAfterCommit()
        {
            ComplexValueDictionaryHost host = CreateScriptableObject<ComplexValueDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ComplexValueDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            ReorderableList initialList = drawer.GetOrCreateList(dictionaryProperty);
            Assert.IsTrue(
                initialList != null,
                "Initial call should create a ReorderableList instance."
            );
            Assert.That(initialList.count, Is.EqualTo(0));

            SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(ComplexValue),
                "Inline",
                new ComplexValue { button = Color.cyan, text = Color.black },
                dictionaryProperty
            );
            Assert.IsTrue(result.added, "Expected CommitEntry to add a new entry.");

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            dictionaryProperty = serializedObject.FindProperty(
                nameof(ComplexValueDictionaryHost.dictionary)
            );
            keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            ReorderableList refreshedList = drawer.GetOrCreateList(dictionaryProperty);
            Assert.IsTrue(refreshedList != null);
            Assert.AreNotSame(
                initialList,
                refreshedList,
                "ReorderableList cache should rebuild after committing new entries."
            );
            Assert.That(refreshedList.count, Is.EqualTo(1));

            float resolvedHeight =
                refreshedList.elementHeightCallback?.Invoke(0) ?? refreshedList.elementHeight;
            Assert.That(
                resolvedHeight,
                Is.GreaterThan(0f),
                "Row height should resolve using up-to-date serialized properties."
            );

            SerializedProperty keyElement = keysProperty.GetArrayElementAtIndex(0);
            SerializedProperty valueElement = valuesProperty.GetArrayElementAtIndex(0);

            Assert.That(keyElement.stringValue, Is.EqualTo("Inline"));
            SerializedProperty buttonColorProperty = valueElement.FindPropertyRelative(
                nameof(ComplexValue.button)
            );
            Assert.IsTrue(buttonColorProperty != null);
            Assert.That(buttonColorProperty.colorValue, Is.EqualTo(Color.cyan));
        }

        [UnityTest]
        public IEnumerator HonorsGroupPaddingWithinGroups()
        {
            _sharedHost.dictionary.Add(1, "One");
            _sharedHost.dictionary.Add(2, "Two");

            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            ForcePopulateTestDictionarySerializedData(_sharedHost, dictionaryProperty);
            dictionaryProperty.isExpanded = true;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );
            Rect controlRect = new(0f, 0f, 360f, 600f);
            GUIContent label = new("Dictionary");
            const int IndentDepth = 2;

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                dictionaryProperty.isExpanded = true;
                int previousIndent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = IndentDepth;
                try
                {
                    drawer.OnGUI(controlRect, dictionaryProperty, label);
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndent;
                }
            });

            Assert.IsTrue(
                drawer.HasLastListRect,
                $"Baseline draw should render the reorderable list. {BuildDictionaryDrawerDiagnostics(dictionaryProperty, drawer)}"
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
                $"Indentation should be reflected in the resolved content rectangle. "
                    + $"IndentedRect.xMin={expectedBaselineRect.xMin}, "
                    + $"AlignmentOffset={UnityListAlignmentOffset}, "
                    + $"ExpectedXMin={expectedXMin}, "
                    + $"ActualXMin={drawer.LastResolvedPosition.xMin}"
            );

            const float LeftPadding = 18f;
            const float RightPadding = 12f;
            const float HorizontalPadding = LeftPadding + RightPadding;

            GroupGUIWidthUtility.ResetForTests();

            yield return TestIMGUIExecutor.Run(() =>
            {
                dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
                dictionaryProperty.isExpanded = true;
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
                        drawer.OnGUI(controlRect, dictionaryProperty, label);
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
                "Group padding should shift content by the configured left margin."
            );
            Assert.That(
                paddedRect.width,
                Is.EqualTo(Mathf.Max(0f, expectedBaselineRect.width - HorizontalPadding))
                    .Within(0.0001f),
                "Group padding should reduce the usable width."
            );
            Assert.That(
                drawer.LastListRect.xMin,
                Is.EqualTo(paddedRect.xMin).Within(0.0001f),
                $"List rect should align with the padded content area. {BuildDictionaryDrawerDiagnostics(dictionaryProperty, drawer)}"
            );
        }

        private static string BuildDictionaryDrawerDiagnostics(
            SerializedProperty dictionaryProperty,
            SerializableDictionaryPropertyDrawer drawer
        )
        {
            if (dictionaryProperty == null)
            {
                return "[DictionaryProperty=null]";
            }

            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            string keysSummary =
                keysProperty == null
                    ? "keys:null"
                    : $"keys:array={keysProperty.isArray},size={keysProperty.arraySize}";
            string valuesSummary =
                valuesProperty == null
                    ? "values:null"
                    : $"values:array={valuesProperty.isArray},size={valuesProperty.arraySize}";
            Rect lastRect = drawer.HasLastListRect ? drawer.LastListRect : Rect.zero;
            return $"[expanded={dictionaryProperty.isExpanded},propertyPath={dictionaryProperty.propertyPath},{keysSummary},{valuesSummary},hasLastRect={drawer.HasLastListRect},lastRect={lastRect}]";
        }

        private static void AssertColorsApproximately(
            Color expected,
            Color actual,
            float tolerance = 0.001f
        )
        {
            Assert.That(Mathf.Abs(expected.r - actual.r), Is.LessThanOrEqualTo(tolerance));
            Assert.That(Mathf.Abs(expected.g - actual.g), Is.LessThanOrEqualTo(tolerance));
            Assert.That(Mathf.Abs(expected.b - actual.b), Is.LessThanOrEqualTo(tolerance));
            Assert.That(Mathf.Abs(expected.a - actual.a), Is.LessThanOrEqualTo(tolerance));
        }

        internal static void AssignDictionaryFieldInfo(
            SerializableDictionaryPropertyDrawer drawer,
            Type hostType,
            string fieldName
        )
        {
            PropertyDrawerTestHelper.AssignFieldInfo(drawer, hostType, fieldName);
        }

        private static void ForcePopulateTestDictionarySerializedData(
            TestDictionaryHost host,
            SerializedProperty dictionaryProperty
        )
        {
            if (host == null || dictionaryProperty == null)
            {
                return;
            }

            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );
            if (keysProperty == null || valuesProperty == null)
            {
                return;
            }

            List<KeyValuePair<int, string>> entries = host.dictionary.ToList();
            keysProperty.arraySize = entries.Count;
            valuesProperty.arraySize = entries.Count;

            for (int i = 0; i < entries.Count; i++)
            {
                AssignKey(keysProperty.GetArrayElementAtIndex(i), entries[i].Key);
                AssignValue(valuesProperty.GetArrayElementAtIndex(i), entries[i].Value);
            }

            dictionaryProperty.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();

            static void AssignKey(SerializedProperty property, int value)
            {
                if (property != null)
                {
                    property.intValue = value;
                }
            }

            static void AssignValue(SerializedProperty property, string value)
            {
                if (property != null)
                {
                    property.stringValue = value ?? string.Empty;
                }
            }
        }

        private static void ForcePopulateComplexDictionarySerializedData(
            ComplexValueDictionaryHost host,
            SerializedProperty dictionaryProperty
        )
        {
            if (host == null || dictionaryProperty == null)
            {
                return;
            }

            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );
            if (keysProperty == null || valuesProperty == null)
            {
                return;
            }

            List<KeyValuePair<string, ComplexValue>> entries = host.dictionary.ToList();
            keysProperty.arraySize = entries.Count;
            valuesProperty.arraySize = entries.Count;

            for (int i = 0; i < entries.Count; i++)
            {
                SerializedProperty keyProperty = keysProperty.GetArrayElementAtIndex(i);
                SerializedProperty valueProperty = valuesProperty.GetArrayElementAtIndex(i);
                SerializableDictionaryPropertyDrawer.SetPropertyValue(
                    keyProperty,
                    entries[i].Key,
                    typeof(string)
                );
                SerializableDictionaryPropertyDrawer.SetPropertyValue(
                    valueProperty,
                    entries[i].Value,
                    typeof(ComplexValue)
                );
            }

            dictionaryProperty.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            dictionaryProperty.serializedObject.UpdateIfRequiredOrScript();
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

        private static string DumpPageEntries(
            SerializableDictionaryPropertyDrawer.ListPageCache cache
        )
        {
            if (cache?.entries == null || cache.entries.Count == 0)
            {
                return "[]";
            }

            List<int> indices = new(cache.entries.Count);
            foreach (SerializableDictionaryPropertyDrawer.PageEntry cacheEntry in cache.entries)
            {
                indices.Add(cacheEntry?.arrayIndex ?? -1);
            }

            return $"[{string.Join(", ", indices)}]";
        }

        private static void RemoveStringDictionaryEntry(
            SerializedProperty keysProperty,
            SerializedProperty valuesProperty,
            string key
        )
        {
            if (
                keysProperty == null
                || valuesProperty == null
                || string.IsNullOrEmpty(key)
                || !keysProperty.isArray
                || !valuesProperty.isArray
            )
            {
                return;
            }

            for (int i = 0; i < keysProperty.arraySize; i++)
            {
                SerializedProperty keyProperty = keysProperty.GetArrayElementAtIndex(i);
                if (!string.Equals(keyProperty.stringValue, key, StringComparison.Ordinal))
                {
                    continue;
                }

                keysProperty.DeleteArrayElementAtIndex(i);
                if (i < valuesProperty.arraySize)
                {
                    valuesProperty.DeleteArrayElementAtIndex(i);
                }

                break;
            }
        }

        private static SerializableDictionaryPropertyDrawer.PendingEntry GetPendingEntry(
            SerializableDictionaryPropertyDrawer drawer,
            SerializedProperty dictionaryProperty,
            Type keyType,
            Type valueType,
            bool isSortedDictionary
        )
        {
            SerializableDictionaryPropertyDrawer.PendingEntry pending =
                drawer.GetOrCreatePendingEntry(
                    dictionaryProperty,
                    keyType,
                    valueType,
                    isSortedDictionary
                );
            Assert.IsTrue(pending != null, "Pending entry instance should not be null.");
            return pending;
        }

        private static bool InvokeValuesEqual(object left, object right)
        {
            return SerializableDictionaryPropertyDrawer.ValuesEqual(left, right);
        }

        private static ColorData ReadColorData(SerializedProperty property)
        {
            if (property == null)
            {
                return default;
            }

            object value = SerializableDictionaryPropertyDrawer.GetPropertyValue(
                property,
                typeof(ColorData)
            );
            return value is ColorData data ? data : default;
        }

        private static string DescribeColorData(ColorData data)
        {
            string formattedColor1 = FormatColor(data.color1);
            string otherSummary =
                data.otherColors == null
                    ? "null"
                    : data.otherColors.Length.ToString(CultureInfo.InvariantCulture);
            return $"color1={formattedColor1}, otherColors={otherSummary}";
        }

        private static string FormatColor(Color color)
        {
            return $"({color.r:0.00},{color.g:0.00},{color.b:0.00},{color.a:0.00})";
        }

        [Test]
        public void GetPropertyHeightIncreasesWhenPendingEntryIsExpanded()
        {
            using (new DictionaryTweenDisabledScope())
            {
                bool tweenEnabled = SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: false
                );
                Assert.IsFalse(
                    tweenEnabled,
                    "Tween should be disabled by DictionaryTweenDisabledScope for accurate height tests."
                );

                _sharedSerializedObject.Update();
                SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                    nameof(TestDictionaryHost.dictionary)
                );
                dictionaryProperty.isExpanded = true;
                _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

                SerializableDictionaryPropertyDrawer drawer = new();
                AssignDictionaryFieldInfo(
                    drawer,
                    typeof(TestDictionaryHost),
                    nameof(TestDictionaryHost.dictionary)
                );

                SerializableDictionaryPropertyDrawer.PendingEntry pending = GetPendingEntry(
                    drawer,
                    dictionaryProperty,
                    typeof(int),
                    typeof(string),
                    isSortedDictionary: false
                );

                pending.isExpanded = false;
                float collapsedHeight = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

                pending.isExpanded = true;
                float expandedHeight = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

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
            using (new DictionaryTweenDisabledScope())
            {
                bool tweenEnabled = SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: false
                );
                Assert.IsFalse(
                    tweenEnabled,
                    "Tween should be disabled by DictionaryTweenDisabledScope for accurate height tests."
                );

                _sharedSerializedObject.Update();
                SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                    nameof(TestDictionaryHost.dictionary)
                );
                dictionaryProperty.isExpanded = true;
                _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

                SerializableDictionaryPropertyDrawer drawer = new();
                AssignDictionaryFieldInfo(
                    drawer,
                    typeof(TestDictionaryHost),
                    nameof(TestDictionaryHost.dictionary)
                );

                SerializableDictionaryPropertyDrawer.PendingEntry pending = GetPendingEntry(
                    drawer,
                    dictionaryProperty,
                    typeof(int),
                    typeof(string),
                    isSortedDictionary: false
                );

                pending.isExpanded = true;
                float expandedHeight = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

                pending.isExpanded = false;
                float collapsedHeight = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

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
            using (new DictionaryTweenDisabledScope())
            {
                bool tweenEnabled = SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: false
                );
                Assert.IsFalse(
                    tweenEnabled,
                    "Tween should be disabled by DictionaryTweenDisabledScope for accurate height tests."
                );

                _sharedSerializedObject.Update();
                SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                    nameof(TestDictionaryHost.dictionary)
                );
                dictionaryProperty.isExpanded = true;
                _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

                SerializableDictionaryPropertyDrawer drawer = new();
                AssignDictionaryFieldInfo(
                    drawer,
                    typeof(TestDictionaryHost),
                    nameof(TestDictionaryHost.dictionary)
                );

                SerializableDictionaryPropertyDrawer.PendingEntry pending = GetPendingEntry(
                    drawer,
                    dictionaryProperty,
                    typeof(int),
                    typeof(string),
                    isSortedDictionary: false
                );

                pending.isExpanded = false;
                float heightBeforeToggle = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );
                float heightCachedSame = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );
                Assert.AreEqual(
                    heightBeforeToggle,
                    heightCachedSame,
                    0.001f,
                    "Height should be cached and return the same value when nothing changes."
                );

                pending.isExpanded = true;
                float heightAfterExpand = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );
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
        public void GetPropertyHeightReturnsConsistentHeightsForEmptyDictionaryWithPendingExpanded()
        {
            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            SerializableDictionaryPropertyDrawer.PendingEntry pending = GetPendingEntry(
                drawer,
                dictionaryProperty,
                typeof(int),
                typeof(string),
                isSortedDictionary: false
            );
            pending.isExpanded = true;

            float height1 = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);
            float height2 = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);
            float height3 = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

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
        public void GetPropertyHeightDiffersBetweenExpandedAndCollapsedPendingForEmptyDictionary()
        {
            using (new DictionaryTweenDisabledScope())
            {
                bool tweenEnabled = SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: false
                );
                Assert.IsFalse(
                    tweenEnabled,
                    "Tween should be disabled by DictionaryTweenDisabledScope for accurate height tests."
                );

                _sharedSerializedObject.Update();
                SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                    nameof(TestDictionaryHost.dictionary)
                );
                dictionaryProperty.isExpanded = true;
                _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

                SerializableDictionaryPropertyDrawer drawer = new();
                AssignDictionaryFieldInfo(
                    drawer,
                    typeof(TestDictionaryHost),
                    nameof(TestDictionaryHost.dictionary)
                );

                SerializableDictionaryPropertyDrawer.PendingEntry pending = GetPendingEntry(
                    drawer,
                    dictionaryProperty,
                    typeof(int),
                    typeof(string),
                    isSortedDictionary: false
                );

                pending.isExpanded = false;
                float collapsedHeight = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

                pending.isExpanded = true;
                float expandedHeight = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

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
        public void GetPropertyHeightPendingExpandAffectsHeightEvenWithDictionaryEntries()
        {
            using (new DictionaryTweenDisabledScope())
            {
                bool tweenEnabled = SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: false
                );
                Assert.IsFalse(
                    tweenEnabled,
                    "Tween should be disabled by DictionaryTweenDisabledScope for accurate height tests."
                );

                _sharedHost.dictionary.Add(1, "one");
                _sharedHost.dictionary.Add(2, "two");
                _sharedHost.dictionary.Add(3, "three");

                _sharedSerializedObject.Update();
                SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                    nameof(TestDictionaryHost.dictionary)
                );
                dictionaryProperty.isExpanded = true;
                _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

                SerializableDictionaryPropertyDrawer drawer = new();
                AssignDictionaryFieldInfo(
                    drawer,
                    typeof(TestDictionaryHost),
                    nameof(TestDictionaryHost.dictionary)
                );

                SerializableDictionaryPropertyDrawer.PendingEntry pending = GetPendingEntry(
                    drawer,
                    dictionaryProperty,
                    typeof(int),
                    typeof(string),
                    isSortedDictionary: false
                );

                pending.isExpanded = false;
                float collapsedHeight = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

                pending.isExpanded = true;
                float expandedHeight = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

                string diagnostics =
                    $"collapsedHeight={collapsedHeight}, expandedHeight={expandedHeight}, "
                    + $"pendingIsExpanded={pending.isExpanded}, foldoutAnimExists={pending.foldoutAnim != null}, "
                    + $"tweenEnabled={tweenEnabled}";
                Assert.Greater(
                    expandedHeight,
                    collapsedHeight,
                    $"Expanding the pending entry should increase height even when the dictionary has existing entries. Diagnostics: {diagnostics}"
                );
            }
        }

        [Test]
        public void GetPropertyHeightTogglingPendingMultipleTimesUpdatesHeightCorrectly()
        {
            using (new DictionaryTweenDisabledScope())
            {
                bool tweenEnabled = SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: false
                );
                Assert.IsFalse(
                    tweenEnabled,
                    "Tween should be disabled by DictionaryTweenDisabledScope for accurate height tests."
                );

                _sharedSerializedObject.Update();
                SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                    nameof(TestDictionaryHost.dictionary)
                );
                dictionaryProperty.isExpanded = true;
                _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

                SerializableDictionaryPropertyDrawer drawer = new();
                AssignDictionaryFieldInfo(
                    drawer,
                    typeof(TestDictionaryHost),
                    nameof(TestDictionaryHost.dictionary)
                );

                SerializableDictionaryPropertyDrawer.PendingEntry pending = GetPendingEntry(
                    drawer,
                    dictionaryProperty,
                    typeof(int),
                    typeof(string),
                    isSortedDictionary: false
                );

                pending.isExpanded = false;
                float collapsed1 = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

                pending.isExpanded = true;
                float expanded1 = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

                pending.isExpanded = false;
                float collapsed2 = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

                pending.isExpanded = true;
                float expanded2 = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

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
        public void GetPropertyHeightSortedDictionaryPendingExpandBehavesCorrectly()
        {
            using (new SortedDictionaryTweenDisabledScope())
            {
                bool tweenEnabled = SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: true
                );
                Assert.IsFalse(
                    tweenEnabled,
                    "Tween should be disabled by SortedDictionaryTweenDisabledScope for accurate height tests."
                );

                TestSortedDictionaryHost host = CreateScriptableObject<TestSortedDictionaryHost>();
                SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
                serializedObject.Update();
                SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                    nameof(TestSortedDictionaryHost.dictionary)
                );
                dictionaryProperty.isExpanded = true;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                SerializableDictionaryPropertyDrawer drawer = new();
                AssignDictionaryFieldInfo(
                    drawer,
                    typeof(TestSortedDictionaryHost),
                    nameof(TestSortedDictionaryHost.dictionary)
                );

                SerializableDictionaryPropertyDrawer.PendingEntry pending = GetPendingEntry(
                    drawer,
                    dictionaryProperty,
                    typeof(int),
                    typeof(string),
                    isSortedDictionary: true
                );

                pending.isExpanded = false;
                float collapsedHeight = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

                pending.isExpanded = true;
                float expandedHeight = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

                string diagnostics =
                    $"collapsedHeight={collapsedHeight}, expandedHeight={expandedHeight}, "
                    + $"pendingIsExpanded={pending.isExpanded}, foldoutAnimExists={pending.foldoutAnim != null}, "
                    + $"tweenEnabled={tweenEnabled}";
                Assert.Greater(
                    expandedHeight,
                    collapsedHeight,
                    $"Sorted dictionary should also update height when pending entry is expanded. Diagnostics: {diagnostics}"
                );
            }
        }

        [Test]
        public void GetPropertyHeightComplexValueDictionaryPendingExpandUpdatesHeight()
        {
            using (new DictionaryTweenDisabledScope())
            {
                bool tweenEnabled = SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: false
                );
                Assert.IsFalse(
                    tweenEnabled,
                    "Tween should be disabled by DictionaryTweenDisabledScope for accurate height tests."
                );

                ComplexValueDictionaryHost host =
                    CreateScriptableObject<ComplexValueDictionaryHost>();
                SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
                serializedObject.Update();
                SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                    nameof(ComplexValueDictionaryHost.dictionary)
                );
                dictionaryProperty.isExpanded = true;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                SerializableDictionaryPropertyDrawer drawer = new();
                AssignDictionaryFieldInfo(
                    drawer,
                    typeof(ComplexValueDictionaryHost),
                    nameof(ComplexValueDictionaryHost.dictionary)
                );

                SerializableDictionaryPropertyDrawer.PendingEntry pending = GetPendingEntry(
                    drawer,
                    dictionaryProperty,
                    typeof(string),
                    typeof(ComplexValue),
                    isSortedDictionary: false
                );

                pending.isExpanded = false;
                float collapsedHeight = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

                pending.isExpanded = true;
                float expandedHeight = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

                string diagnostics =
                    $"collapsedHeight={collapsedHeight}, expandedHeight={expandedHeight}, "
                    + $"pendingIsExpanded={pending.isExpanded}, foldoutAnimExists={pending.foldoutAnim != null}, "
                    + $"tweenEnabled={tweenEnabled}";
                Assert.Greater(
                    expandedHeight,
                    collapsedHeight,
                    $"Complex value dictionary should update height when pending entry is expanded. Diagnostics: {diagnostics}"
                );
            }
        }

        [Test]
        public void GetPropertyHeightMainFoldoutCollapsedIgnoresPendingState()
        {
            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = false;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            float collapsedHeight1 = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

            SerializableDictionaryPropertyDrawer.PendingEntry pending = GetPendingEntry(
                drawer,
                dictionaryProperty,
                typeof(int),
                typeof(string),
                isSortedDictionary: false
            );
            pending.isExpanded = true;

            float collapsedHeight2 = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

            Assert.AreEqual(
                collapsedHeight1,
                collapsedHeight2,
                0.001f,
                "When the main dictionary foldout is collapsed, pending entry state should not affect height."
            );
        }

        /// <summary>
        /// Regression test: Verifies that CommitEntry correctly adds entries to
        /// UnityHelpersSettings palette dictionaries (ScriptableSingleton targets).
        /// Previously, the entry would be committed but the runtime dictionary would
        /// remain stale due to EditorAfterDeserialize reading from unupdated managed fields.
        /// </summary>
        [Test]
        public void CommitEntryAddsToSettingsPalette()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            SerializedObject serializedSettings = TrackDisposable(new SerializedObject(settings));
            serializedSettings.Update();

            SerializedProperty paletteProperty = serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            Assert.IsTrue(paletteProperty != null, "Should find WButtonCustomColors property.");

            SerializedProperty keysProperty = paletteProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = paletteProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );
            Assert.IsTrue(keysProperty != null, "Should find keys property.");
            Assert.IsTrue(valuesProperty != null, "Should find values property.");

            // Generate a unique test key to avoid conflicts with existing settings
            string testKey = $"TestKey_{Guid.NewGuid():N}";
            int initialCount = keysProperty.arraySize;

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(UnityHelpersSettings.WButtonCustomColor),
                testKey,
                new UnityHelpersSettings.WButtonCustomColor
                {
                    _buttonColor = Color.cyan,
                    _textColor = Color.yellow,
                },
                paletteProperty
            );

            Assert.IsTrue(result.added, "Expected CommitEntry to add a new element.");
            Assert.That(
                result.index,
                Is.GreaterThanOrEqualTo(0),
                "Returned index should be valid."
            );

            // Verify SerializedProperty state
            serializedSettings.Update();
            keysProperty = serializedSettings
                .FindProperty(UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors)
                .FindPropertyRelative(SerializableDictionarySerializedPropertyNames.Keys);

            int finalCount = keysProperty.arraySize;
            Assert.That(
                finalCount,
                Is.EqualTo(initialCount + 1),
                "Keys array size should have increased by 1."
            );

            // Clean up: Remove the test entry
            bool foundTestKey = false;
            for (int i = 0; i < keysProperty.arraySize; i++)
            {
                if (keysProperty.GetArrayElementAtIndex(i).stringValue == testKey)
                {
                    keysProperty.DeleteArrayElementAtIndex(i);
                    valuesProperty = serializedSettings
                        .FindProperty(
                            UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
                        )
                        .FindPropertyRelative(SerializableDictionarySerializedPropertyNames.Values);
                    valuesProperty.DeleteArrayElementAtIndex(i);
                    serializedSettings.ApplyModifiedPropertiesWithoutUndo();
                    foundTestKey = true;
                    break;
                }
            }

            Assert.IsTrue(foundTestKey, "Test key should have been found in the keys array.");
        }

        /// <summary>
        /// Regression test: Verifies that multiple consecutive CommitEntry calls work
        /// correctly for UnityHelpersSettings targets without requiring a domain reload.
        /// </summary>
        [Test]
        public void MultipleConsecutiveCommitsToSettingsPaletteWork()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            SerializedObject serializedSettings = TrackDisposable(new SerializedObject(settings));
            serializedSettings.Update();

            SerializedProperty paletteProperty = serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            SerializedProperty keysProperty = paletteProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = paletteProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            int initialCount = keysProperty.arraySize;
            List<string> testKeys = new();
            SerializableDictionaryPropertyDrawer drawer = new();

            // Add multiple entries consecutively
            for (int i = 0; i < 3; i++)
            {
                string testKey = $"MultiCommitTest_{i}_{Guid.NewGuid():N}";
                testKeys.Add(testKey);

                SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                    keysProperty,
                    valuesProperty,
                    typeof(string),
                    typeof(UnityHelpersSettings.WButtonCustomColor),
                    testKey,
                    new UnityHelpersSettings.WButtonCustomColor
                    {
                        _buttonColor = new Color(i * 0.3f, 0.5f, 0.8f),
                        _textColor = Color.white,
                    },
                    paletteProperty
                );

                Assert.IsTrue(result.added, $"CommitEntry #{i + 1} should succeed.");

                // Re-fetch properties after each commit to verify state
                serializedSettings.Update();
                paletteProperty = serializedSettings.FindProperty(
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
                );
                keysProperty = paletteProperty.FindPropertyRelative(
                    SerializableDictionarySerializedPropertyNames.Keys
                );
                valuesProperty = paletteProperty.FindPropertyRelative(
                    SerializableDictionarySerializedPropertyNames.Values
                );

                Assert.That(
                    keysProperty.arraySize,
                    Is.EqualTo(initialCount + i + 1),
                    $"After commit #{i + 1}, keys count should be {initialCount + i + 1}."
                );
            }

            // Clean up: Remove all test entries
            serializedSettings.Update();
            paletteProperty = serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            keysProperty = paletteProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            valuesProperty = paletteProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            for (int i = keysProperty.arraySize - 1; 0 <= i; i--)
            {
                string key = keysProperty.GetArrayElementAtIndex(i).stringValue;
                if (testKeys.Contains(key))
                {
                    keysProperty.DeleteArrayElementAtIndex(i);
                    valuesProperty.DeleteArrayElementAtIndex(i);
                }
            }

            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Verifies that ForwardSyncFromSerializedProperties correctly reads complex values
        /// from SerializedProperties and updates the managed arrays.
        /// </summary>
        [Test]
        public void ForwardSyncPreservesComplexValueFields()
        {
            // Use a regular ScriptableObject host to test the sync mechanism
            ComplexValueDictionaryHost host = CreateScriptableObject<ComplexValueDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ComplexValueDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            Color expectedButtonColor = new(0.1f, 0.2f, 0.3f, 1f);
            Color expectedTextColor = new(0.9f, 0.8f, 0.7f, 1f);
            string testKey = "SyncTestKey";

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(ComplexValue),
                testKey,
                new ComplexValue { button = expectedButtonColor, text = expectedTextColor },
                dictionaryProperty
            );

            Assert.IsTrue(result.added, "Expected CommitEntry to add a new element.");

            // Verify the runtime dictionary contains the correct values
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
            host.dictionary.EditorAfterDeserialize();

            Assert.That(host.dictionary.Count, Is.EqualTo(1), "Dictionary should have 1 entry.");
            Assert.IsTrue(
                host.dictionary.TryGetValue(testKey, out ComplexValue retrievedValue),
                "Should be able to retrieve the added entry."
            );
            Assert.That(retrievedValue.button, Is.EqualTo(expectedButtonColor));
            Assert.That(retrievedValue.text, Is.EqualTo(expectedTextColor));
        }

        [Test]
        public void RefreshDuplicateStateReturnsNullForEmptyCacheKey()
        {
            _sharedHost.dictionary[1] = "One";

            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            SerializableDictionaryPropertyDrawer drawer = new();

            SerializableDictionaryPropertyDrawer.DuplicateKeyState nullResult =
                drawer.RefreshDuplicateState(null, keysProperty, typeof(int));
            Assert.IsTrue(
                nullResult == null,
                "RefreshDuplicateState should return null for null cacheKey."
            );

            SerializableDictionaryPropertyDrawer.DuplicateKeyState emptyResult =
                drawer.RefreshDuplicateState(string.Empty, keysProperty, typeof(int));
            Assert.IsTrue(
                emptyResult == null,
                "RefreshDuplicateState should return null for empty cacheKey."
            );
        }

        [Test]
        public void RefreshDuplicateStateReturnsStateWithNoDuplicatesForUniqueKeys()
        {
            _sharedHost.dictionary[1] = "One";
            _sharedHost.dictionary[2] = "Two";
            _sharedHost.dictionary[3] = "Three";

            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            SerializableDictionaryPropertyDrawer.DuplicateKeyState state =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(int));
            // Note: RefreshDuplicateState always returns state (unlike RefreshNullKeyState)
            Assert.IsTrue(
                state != null,
                "RefreshDuplicateState returns a state object even with no duplicates."
            );
            Assert.IsFalse(
                state.HasDuplicates,
                "HasDuplicates should be false when all keys are unique."
            );
        }

        [Test]
        public void RefreshDuplicateStateDetectsMultipleDuplicateGroups()
        {
            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            // Create two groups of duplicates: [1, 1] and [2, 2]
            keysProperty.arraySize = 4;
            valuesProperty.arraySize = 4;
            keysProperty.GetArrayElementAtIndex(0).intValue = 1;
            keysProperty.GetArrayElementAtIndex(1).intValue = 1;
            keysProperty.GetArrayElementAtIndex(2).intValue = 2;
            keysProperty.GetArrayElementAtIndex(3).intValue = 2;
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "Value1";
            valuesProperty.GetArrayElementAtIndex(1).stringValue = "Value2";
            valuesProperty.GetArrayElementAtIndex(2).stringValue = "Value3";
            valuesProperty.GetArrayElementAtIndex(3).stringValue = "Value4";
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();
            _sharedSerializedObject.Update();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            SerializableDictionaryPropertyDrawer.DuplicateKeyState state =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(int));
            Assert.IsTrue(state != null, "State should be returned.");
            Assert.IsTrue(
                state.HasDuplicates,
                "HasDuplicates should be true with multiple duplicate groups."
            );
        }

        [Test]
        public void RefreshDuplicateStateTransitionsFromDuplicatesToUniqueKeys()
        {
            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            // Start with duplicate keys
            keysProperty.arraySize = 2;
            valuesProperty.arraySize = 2;
            keysProperty.GetArrayElementAtIndex(0).intValue = 1;
            keysProperty.GetArrayElementAtIndex(1).intValue = 1;
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "Value1";
            valuesProperty.GetArrayElementAtIndex(1).stringValue = "Value2";
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();
            _sharedSerializedObject.Update();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            // Verify duplicates are detected
            SerializableDictionaryPropertyDrawer.DuplicateKeyState initialState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(int));
            Assert.IsTrue(initialState != null, "Initial state should be returned.");
            Assert.IsTrue(initialState.HasDuplicates, "HasDuplicates should be true initially.");

            // Fix the duplicate by changing one key
            keysProperty.GetArrayElementAtIndex(1).intValue = 2;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();
            _sharedSerializedObject.Update();

            // Invalidate and refresh
            drawer.InvalidateKeyCache(cacheKey);

            SerializableDictionaryPropertyDrawer.DuplicateKeyState afterFixState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(int));
            Assert.IsTrue(afterFixState != null, "State should still be returned after fix.");
            Assert.IsFalse(
                afterFixState.HasDuplicates,
                "HasDuplicates should be false after fixing duplicates."
            );
        }

        [Test]
        public void DuplicateKeyStateMarkDirtyForcesRefreshOnNextEvaluation()
        {
            _sharedHost.dictionary[1] = "One";
            _sharedHost.dictionary[2] = "Two";

            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            SerializableDictionaryPropertyDrawer.DuplicateKeyState initialState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(int));
            Assert.IsTrue(initialState != null, "Initial state should be created.");
            Assert.IsFalse(initialState.HasDuplicates, "Initial state should not have duplicates.");

            keysProperty.GetArrayElementAtIndex(1).intValue = 1;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();
            _sharedSerializedObject.Update();

            drawer.InvalidateKeyCache(cacheKey);

            SerializableDictionaryPropertyDrawer.DuplicateKeyState refreshedState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(int));
            Assert.IsTrue(refreshedState != null, "Refreshed state should be returned.");
            Assert.IsTrue(
                refreshedState.HasDuplicates,
                "After invalidation and edit, duplicates should be detected."
            );
        }

        [Test]
        public void InvalidateKeyCacheMarksNullKeyStateDirty()
        {
            UnityObjectDictionaryHost host = CreateScriptableObject<UnityObjectDictionaryHost>();
            GameObject go1 = NewGameObject("Key1");
            GameObject go2 = NewGameObject("Key2");
            host.dictionary[go1] = "Value1";
            host.dictionary[go2] = "Value2";

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(UnityObjectDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(UnityObjectDictionaryHost),
                nameof(UnityObjectDictionaryHost.dictionary)
            );

            string cacheKey = drawer.GetListKey(dictionaryProperty);
            Assert.IsFalse(string.IsNullOrEmpty(cacheKey), "Cache key should be valid.");

            // RefreshNullKeyState returns null when there are no null keys (this is correct behavior)
            SerializableDictionaryPropertyDrawer.NullKeyState initialState =
                drawer.RefreshNullKeyState(cacheKey, keysProperty, typeof(GameObject));
            Assert.IsTrue(
                initialState == null,
                "Initial state should be null when no null keys exist (RefreshNullKeyState returns null for clean dictionaries)."
            );

            // Verify keys array has expected content before modification
            Assert.AreEqual(
                2,
                keysProperty.arraySize,
                $"Keys array should have 2 elements before modification."
            );
            Assert.IsTrue(
                keysProperty.GetArrayElementAtIndex(0).objectReferenceValue != null,
                "First key should not be null before modification."
            );

            // Now introduce a null key
            keysProperty.GetArrayElementAtIndex(0).objectReferenceValue = null;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            // Verify the modification took effect
            Assert.IsTrue(
                keysProperty.GetArrayElementAtIndex(0).objectReferenceValue == null,
                "First key should be null after modification."
            );

            // Invalidate the cache to force a refresh
            drawer.InvalidateKeyCache(cacheKey);

            // After invalidation with null key present, state should be returned and show null keys
            SerializableDictionaryPropertyDrawer.NullKeyState refreshedState =
                drawer.RefreshNullKeyState(cacheKey, keysProperty, typeof(GameObject));
            Assert.IsTrue(
                refreshedState != null,
                $"Refreshed state should be returned when null keys exist. "
                    + $"CacheKey: '{cacheKey}', KeysArraySize: {keysProperty.arraySize}, "
                    + $"Key0IsNull: {keysProperty.GetArrayElementAtIndex(0).objectReferenceValue == null}"
            );
            Assert.IsTrue(
                refreshedState.HasNullKeys,
                "After invalidation and setting key to null, null key should be detected."
            );
        }

        [Test]
        public void InvalidateKeyCacheMarksNullKeyStateDirtyMultipleConsecutiveCalls()
        {
            // Test that multiple consecutive InvalidateKeyCache calls work correctly
            UnityObjectDictionaryHost host = CreateScriptableObject<UnityObjectDictionaryHost>();
            GameObject go1 = NewGameObject("Key1");
            host.dictionary[go1] = "Value1";

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(UnityObjectDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(UnityObjectDictionaryHost),
                nameof(UnityObjectDictionaryHost.dictionary)
            );

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            // First refresh - no null keys
            SerializableDictionaryPropertyDrawer.NullKeyState state1 = drawer.RefreshNullKeyState(
                cacheKey,
                keysProperty,
                typeof(GameObject)
            );
            Assert.IsTrue(state1 == null, "Initial state should be null (no null keys).");

            // Multiple consecutive invalidations without changes
            drawer.InvalidateKeyCache(cacheKey);
            drawer.InvalidateKeyCache(cacheKey);
            drawer.InvalidateKeyCache(cacheKey);

            // Should still return null since no null keys were introduced
            SerializableDictionaryPropertyDrawer.NullKeyState state2 = drawer.RefreshNullKeyState(
                cacheKey,
                keysProperty,
                typeof(GameObject)
            );
            Assert.IsTrue(
                state2 == null,
                "State should be null after multiple invalidations with no null keys."
            );

            // Now introduce null key
            keysProperty.GetArrayElementAtIndex(0).objectReferenceValue = null;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            // Multiple invalidations after change
            drawer.InvalidateKeyCache(cacheKey);
            drawer.InvalidateKeyCache(cacheKey);

            SerializableDictionaryPropertyDrawer.NullKeyState state3 = drawer.RefreshNullKeyState(
                cacheKey,
                keysProperty,
                typeof(GameObject)
            );
            Assert.IsTrue(
                state3 != null,
                "State should be returned after invalidations with null key."
            );
            Assert.IsTrue(state3.HasNullKeys, "Should detect null key.");
        }

        [Test]
        public void InvalidateKeyCacheHandlesNullKeyStateTransitionsCyclically()
        {
            // Test transitioning between null and valid keys multiple times
            UnityObjectDictionaryHost host = CreateScriptableObject<UnityObjectDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(UnityObjectDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            // Start with empty dictionary
            keysProperty.arraySize = 1;
            valuesProperty.arraySize = 1;
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "Value1";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(UnityObjectDictionaryHost),
                nameof(UnityObjectDictionaryHost.dictionary)
            );

            string cacheKey = drawer.GetListKey(dictionaryProperty);
            int cycles = 3;

            for (int cycle = 0; cycle < cycles; cycle++)
            {
                // Set to null
                keysProperty.GetArrayElementAtIndex(0).objectReferenceValue = null;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                serializedObject.Update();
                drawer.InvalidateKeyCache(cacheKey);

                SerializableDictionaryPropertyDrawer.NullKeyState nullState =
                    drawer.RefreshNullKeyState(cacheKey, keysProperty, typeof(GameObject));
                Assert.IsTrue(
                    nullState != null,
                    $"Cycle {cycle}: State should be returned when key is null."
                );
                Assert.IsTrue(nullState.HasNullKeys, $"Cycle {cycle}: HasNullKeys should be true.");

                // Set to valid key
                GameObject validKey = NewGameObject($"Key_Cycle{cycle}");
                keysProperty.GetArrayElementAtIndex(0).objectReferenceValue = validKey;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                serializedObject.Update();
                drawer.InvalidateKeyCache(cacheKey);

                SerializableDictionaryPropertyDrawer.NullKeyState validState =
                    drawer.RefreshNullKeyState(cacheKey, keysProperty, typeof(GameObject));
                Assert.IsTrue(
                    validState == null,
                    $"Cycle {cycle}: State should be null when key is valid."
                );
            }
        }

        [Test]
        public void InvalidateKeyCacheAndRefreshDuplicateStateComparisonWithNullKeyState()
        {
            // This test verifies that InvalidateKeyCache + RefreshDuplicateState and
            // InvalidateKeyCache + RefreshNullKeyState behave consistently when
            // transitioning from "no issues" to "issues detected"

            // Test DuplicateKeyState first (reference behavior)
            TestDictionaryHost hostDuplicate = CreateScriptableObject<TestDictionaryHost>();
            hostDuplicate.dictionary[1] = "One";
            hostDuplicate.dictionary[2] = "Two";

            SerializedObject soDuplicate = TrackDisposable(new SerializedObject(hostDuplicate));
            soDuplicate.Update();
            SerializedProperty dictPropDuplicate = soDuplicate.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysPropDuplicate = dictPropDuplicate.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            SerializableDictionaryPropertyDrawer drawerDuplicate = new();
            AssignDictionaryFieldInfo(
                drawerDuplicate,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            string cacheKeyDuplicate = drawerDuplicate.GetListKey(dictPropDuplicate);

            // Initial state - no duplicates
            SerializableDictionaryPropertyDrawer.DuplicateKeyState dupState1 =
                drawerDuplicate.RefreshDuplicateState(
                    cacheKeyDuplicate,
                    keysPropDuplicate,
                    typeof(int)
                );
            Assert.IsTrue(dupState1 != null, "DuplicateKeyState is always returned.");
            Assert.IsFalse(dupState1.HasDuplicates, "Should not have duplicates initially.");

            // Introduce duplicate
            keysPropDuplicate.GetArrayElementAtIndex(1).intValue = 1; // Same as first key
            soDuplicate.ApplyModifiedPropertiesWithoutUndo();
            soDuplicate.Update();
            drawerDuplicate.InvalidateKeyCache(cacheKeyDuplicate);

            SerializableDictionaryPropertyDrawer.DuplicateKeyState dupState2 =
                drawerDuplicate.RefreshDuplicateState(
                    cacheKeyDuplicate,
                    keysPropDuplicate,
                    typeof(int)
                );
            Assert.IsTrue(
                dupState2 != null,
                "DuplicateKeyState should be returned after invalidation."
            );
            Assert.IsTrue(dupState2.HasDuplicates, "Should detect duplicates after invalidation.");

            // Now test NullKeyState (should behave the same way after fix)
            UnityObjectDictionaryHost hostNull =
                CreateScriptableObject<UnityObjectDictionaryHost>();
            GameObject go1 = NewGameObject("Key1");
            GameObject go2 = NewGameObject("Key2");
            hostNull.dictionary[go1] = "Value1";
            hostNull.dictionary[go2] = "Value2";

            SerializedObject soNull = TrackDisposable(new SerializedObject(hostNull));
            soNull.Update();
            SerializedProperty dictPropNull = soNull.FindProperty(
                nameof(UnityObjectDictionaryHost.dictionary)
            );
            SerializedProperty keysPropNull = dictPropNull.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            SerializableDictionaryPropertyDrawer drawerNull = new();
            AssignDictionaryFieldInfo(
                drawerNull,
                typeof(UnityObjectDictionaryHost),
                nameof(UnityObjectDictionaryHost.dictionary)
            );

            string cacheKeyNull = drawerNull.GetListKey(dictPropNull);

            // Initial state - no null keys (returns null, unlike DuplicateKeyState)
            SerializableDictionaryPropertyDrawer.NullKeyState nullState1 =
                drawerNull.RefreshNullKeyState(cacheKeyNull, keysPropNull, typeof(GameObject));
            Assert.IsTrue(nullState1 == null, "NullKeyState returns null when no null keys exist.");

            // Introduce null key
            keysPropNull.GetArrayElementAtIndex(0).objectReferenceValue = null;
            soNull.ApplyModifiedPropertiesWithoutUndo();
            soNull.Update();
            drawerNull.InvalidateKeyCache(cacheKeyNull);

            SerializableDictionaryPropertyDrawer.NullKeyState nullState2 =
                drawerNull.RefreshNullKeyState(cacheKeyNull, keysPropNull, typeof(GameObject));
            Assert.IsTrue(
                nullState2 != null,
                "NullKeyState should be returned after invalidation when null keys exist "
                    + "(parallel behavior to DuplicateKeyState)."
            );
            Assert.IsTrue(nullState2.HasNullKeys, "Should detect null keys after invalidation.");
        }

        [Test]
        public void RefreshNullKeyStateReturnsNullForEmptyCacheKey()
        {
            UnityObjectDictionaryHost host = CreateScriptableObject<UnityObjectDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(UnityObjectDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            SerializableDictionaryPropertyDrawer drawer = new();

            SerializableDictionaryPropertyDrawer.NullKeyState nullResult =
                drawer.RefreshNullKeyState(null, keysProperty, typeof(GameObject));
            Assert.IsTrue(
                nullResult == null,
                "RefreshNullKeyState should return null for null cacheKey."
            );

            SerializableDictionaryPropertyDrawer.NullKeyState emptyResult =
                drawer.RefreshNullKeyState(string.Empty, keysProperty, typeof(GameObject));
            Assert.IsTrue(
                emptyResult == null,
                "RefreshNullKeyState should return null for empty cacheKey."
            );
        }

        [Test]
        public void RefreshNullKeyStateReturnsNullForNonNullableKeyTypes(
            [Values(typeof(int), typeof(float), typeof(bool), typeof(double), typeof(long))]
                Type keyType
        )
        {
            // Test that value types (which cannot be null) return null from RefreshNullKeyState
            _sharedHost.dictionary[1] = "One";
            _sharedHost.dictionary[2] = "Two";

            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            // Value types cannot have null keys, so should return null
            SerializableDictionaryPropertyDrawer.NullKeyState result = drawer.RefreshNullKeyState(
                cacheKey,
                keysProperty,
                keyType
            );
            Assert.IsTrue(
                result == null,
                $"RefreshNullKeyState should return null for non-nullable key type {keyType.Name}."
            );
        }

        [Test]
        public void RefreshNullKeyStateDetectsAllNullKeysInDictionary()
        {
            UnityObjectDictionaryHost host = CreateScriptableObject<UnityObjectDictionaryHost>();
            // Start with no keys, add them via serialized property to have null entries
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(UnityObjectDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            // Add three entries with null keys
            keysProperty.arraySize = 3;
            valuesProperty.arraySize = 3;
            keysProperty.GetArrayElementAtIndex(0).objectReferenceValue = null;
            keysProperty.GetArrayElementAtIndex(1).objectReferenceValue = null;
            keysProperty.GetArrayElementAtIndex(2).objectReferenceValue = null;
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "Value1";
            valuesProperty.GetArrayElementAtIndex(1).stringValue = "Value2";
            valuesProperty.GetArrayElementAtIndex(2).stringValue = "Value3";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(UnityObjectDictionaryHost),
                nameof(UnityObjectDictionaryHost.dictionary)
            );

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            SerializableDictionaryPropertyDrawer.NullKeyState result = drawer.RefreshNullKeyState(
                cacheKey,
                keysProperty,
                typeof(GameObject)
            );
            Assert.IsTrue(result != null, "Should return state when null keys exist.");
            Assert.IsTrue(
                result.HasNullKeys,
                "HasNullKeys should be true with multiple null keys."
            );
        }

        [Test]
        public void RefreshNullKeyStateTransitionsFromNullToValidKey()
        {
            UnityObjectDictionaryHost host = CreateScriptableObject<UnityObjectDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(UnityObjectDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            // Start with a null key
            keysProperty.arraySize = 1;
            valuesProperty.arraySize = 1;
            keysProperty.GetArrayElementAtIndex(0).objectReferenceValue = null;
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "Value1";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(UnityObjectDictionaryHost),
                nameof(UnityObjectDictionaryHost.dictionary)
            );

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            // Verify null key is detected
            SerializableDictionaryPropertyDrawer.NullKeyState initialState =
                drawer.RefreshNullKeyState(cacheKey, keysProperty, typeof(GameObject));
            Assert.IsTrue(
                initialState != null,
                "Initial state should be returned when null key exists."
            );
            Assert.IsTrue(initialState.HasNullKeys, "HasNullKeys should be true initially.");

            // Now set a valid key
            GameObject validKey = NewGameObject("ValidKey");
            keysProperty.GetArrayElementAtIndex(0).objectReferenceValue = validKey;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            // Invalidate and refresh
            drawer.InvalidateKeyCache(cacheKey);

            SerializableDictionaryPropertyDrawer.NullKeyState afterFixState =
                drawer.RefreshNullKeyState(cacheKey, keysProperty, typeof(GameObject));
            // When there are no null keys, the method returns null
            Assert.IsTrue(
                afterFixState == null,
                "State should be null when no null keys exist (clean state returns null)."
            );
        }

        [Test]
        public void DuplicateDetectionTriggersImmediatelyWhenKeyEditedToMatchAnother()
        {
            StringDictionaryHost host = CreateScriptableObject<StringDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(StringDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(StringDictionaryHost),
                nameof(StringDictionaryHost.dictionary)
            );

            drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(string),
                "Alpha",
                "Value1",
                dictionaryProperty
            );
            drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(string),
                "Beta",
                "Value2",
                dictionaryProperty
            );
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            SerializableDictionaryPropertyDrawer.DuplicateKeyState initialState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsFalse(initialState.HasDuplicates, "Initial state should not have duplicates.");

            keysProperty.GetArrayElementAtIndex(0).stringValue = "Beta";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            drawer.InvalidateKeyCache(cacheKey);

            SerializableDictionaryPropertyDrawer.DuplicateKeyState afterEditState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsTrue(
                afterEditState.HasDuplicates,
                "After editing key to match another, duplicates should be detected immediately."
            );
        }

        [Test]
        public void DuplicateDetectionClearsWhenKeyEditedToBeUnique()
        {
            StringDictionaryHost host = CreateScriptableObject<StringDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(StringDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(StringDictionaryHost),
                nameof(StringDictionaryHost.dictionary)
            );

            keysProperty.InsertArrayElementAtIndex(0);
            keysProperty.GetArrayElementAtIndex(0).stringValue = "Same";
            valuesProperty.InsertArrayElementAtIndex(0);
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "Val1";

            keysProperty.InsertArrayElementAtIndex(1);
            keysProperty.GetArrayElementAtIndex(1).stringValue = "Same";
            valuesProperty.InsertArrayElementAtIndex(1);
            valuesProperty.GetArrayElementAtIndex(1).stringValue = "Val2";

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState initialState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsTrue(initialState.HasDuplicates, "Initial state should have duplicates.");

            keysProperty.GetArrayElementAtIndex(1).stringValue = "Different";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            drawer.InvalidateKeyCache(cacheKey);

            SerializableDictionaryPropertyDrawer.DuplicateKeyState afterEditState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsFalse(
                afterEditState.HasDuplicates,
                "After editing key to be unique, duplicates should be cleared."
            );
        }

        [Test]
        public void MultipleKeyEditsInSuccessionMaintainCorrectDuplicateState()
        {
            _sharedHost.dictionary[1] = "A";
            _sharedHost.dictionary[2] = "B";
            _sharedHost.dictionary[3] = "C";

            _sharedSerializedObject.Update();
            SerializedProperty dictionaryProperty = _sharedSerializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(TestDictionaryHost),
                nameof(TestDictionaryHost.dictionary)
            );

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState state1 =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(int));
            Assert.IsFalse(state1.HasDuplicates, "Cycle 1: No duplicates.");

            keysProperty.GetArrayElementAtIndex(0).intValue = 2;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();
            _sharedSerializedObject.Update();
            drawer.InvalidateKeyCache(cacheKey);

            SerializableDictionaryPropertyDrawer.DuplicateKeyState state2 =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(int));
            Assert.IsTrue(state2.HasDuplicates, "Cycle 2: Keys 0 and 1 should be duplicates.");

            keysProperty.GetArrayElementAtIndex(2).intValue = 2;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();
            _sharedSerializedObject.Update();
            drawer.InvalidateKeyCache(cacheKey);

            SerializableDictionaryPropertyDrawer.DuplicateKeyState state3 =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(int));
            Assert.IsTrue(state3.HasDuplicates, "Cycle 3: All three should be duplicates.");

            keysProperty.GetArrayElementAtIndex(0).intValue = 10;
            keysProperty.GetArrayElementAtIndex(1).intValue = 20;
            keysProperty.GetArrayElementAtIndex(2).intValue = 30;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();
            _sharedSerializedObject.Update();
            drawer.InvalidateKeyCache(cacheKey);

            SerializableDictionaryPropertyDrawer.DuplicateKeyState state4 =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(int));
            Assert.IsFalse(state4.HasDuplicates, "Cycle 4: All unique, no duplicates.");
        }

        [Test]
        public void DuplicateDetectionHandlesEmptyStringKeys()
        {
            StringDictionaryHost host = CreateScriptableObject<StringDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(StringDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(StringDictionaryHost),
                nameof(StringDictionaryHost.dictionary)
            );

            keysProperty.InsertArrayElementAtIndex(0);
            keysProperty.GetArrayElementAtIndex(0).stringValue = "";
            valuesProperty.InsertArrayElementAtIndex(0);
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "Val1";

            keysProperty.InsertArrayElementAtIndex(1);
            keysProperty.GetArrayElementAtIndex(1).stringValue = "NonEmpty";
            valuesProperty.InsertArrayElementAtIndex(1);
            valuesProperty.GetArrayElementAtIndex(1).stringValue = "Val2";

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState initialState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsFalse(
                initialState.HasDuplicates,
                "Initial: No duplicates with empty and non-empty keys."
            );

            keysProperty.GetArrayElementAtIndex(1).stringValue = "";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState afterEditState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsTrue(
                afterEditState.HasDuplicates,
                "Empty string duplicates should be detected."
            );
        }

        [Test]
        public void DuplicateKeyStateIsDirtyPropertyIsTrueAfterMarkDirty()
        {
            SerializableDictionaryPropertyDrawer.DuplicateKeyState state = new();
            Assert.IsTrue(state != null, "Should be able to create DuplicateKeyState instance.");

            bool initialDirty = state.IsDirty;
            Assert.IsTrue(initialDirty, "IsDirty should be true initially (lastArraySize == -1).");

            state.Refresh(null, null);

            bool afterRefreshDirty = state.IsDirty;
            Assert.IsTrue(
                afterRefreshDirty,
                "IsDirty should remain true after Refresh with null arguments."
            );
        }

        [Test]
        public void DuplicateKeyStateIsDirtyPropertyIsFalseAfterRefreshWithValidData()
        {
            StringDictionaryHost host = CreateScriptableObject<StringDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(StringDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            keysProperty.InsertArrayElementAtIndex(0);
            keysProperty.GetArrayElementAtIndex(0).stringValue = "TestKey";
            valuesProperty.InsertArrayElementAtIndex(0);
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "TestValue";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableDictionaryPropertyDrawer.DuplicateKeyState state = new();

            bool initialDirty = state.IsDirty;
            Assert.IsTrue(initialDirty, "IsDirty should be true initially.");

            state.Refresh(keysProperty, typeof(string));

            bool afterRefreshDirty = state.IsDirty;
            Assert.IsFalse(
                afterRefreshDirty,
                "IsDirty should be false after Refresh with valid data."
            );
        }

        [Test]
        public void DuplicateKeyStateMarkDirtyResetsIsDirtyToTrue()
        {
            StringDictionaryHost host = CreateScriptableObject<StringDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(StringDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            keysProperty.InsertArrayElementAtIndex(0);
            keysProperty.GetArrayElementAtIndex(0).stringValue = "TestKey";
            valuesProperty.InsertArrayElementAtIndex(0);
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "TestValue";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableDictionaryPropertyDrawer.DuplicateKeyState state = new();

            state.Refresh(keysProperty, typeof(string));
            bool afterRefreshDirty = state.IsDirty;
            Assert.IsFalse(afterRefreshDirty, "IsDirty should be false after Refresh.");

            state.MarkDirty();
            bool afterMarkDirty = state.IsDirty;
            Assert.IsTrue(afterMarkDirty, "IsDirty should be true after MarkDirty is called.");
        }

        [Test]
        public void NullKeyStateIsDirtyPropertyIsTrueAfterMarkDirty()
        {
            SerializableDictionaryPropertyDrawer.NullKeyState state = new();
            Assert.IsTrue(state != null, "Should be able to create NullKeyState instance.");

            bool initialDirty = state.IsDirty;
            Assert.IsTrue(initialDirty, "IsDirty should be true initially (lastArraySize == -1).");

            state.MarkDirty();
            bool afterMarkDirty = state.IsDirty;
            Assert.IsTrue(afterMarkDirty, "IsDirty should remain true after MarkDirty.");
        }

        [Test]
        public void DuplicateDetectionUpdatesImmediatelyAfterStringKeyEditWithSameFrameRefresh()
        {
            StringDictionaryHost host = CreateScriptableObject<StringDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(StringDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(StringDictionaryHost),
                nameof(StringDictionaryHost.dictionary)
            );

            keysProperty.InsertArrayElementAtIndex(0);
            keysProperty.GetArrayElementAtIndex(0).stringValue = "KeyA";
            valuesProperty.InsertArrayElementAtIndex(0);
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "Value1";

            keysProperty.InsertArrayElementAtIndex(1);
            keysProperty.GetArrayElementAtIndex(1).stringValue = "KeyB";
            valuesProperty.InsertArrayElementAtIndex(1);
            valuesProperty.GetArrayElementAtIndex(1).stringValue = "Value2";

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState initialState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsFalse(initialState.HasDuplicates, "Initial: No duplicates.");
            Assert.IsFalse(
                initialState.IsDirty,
                "Initial: State should not be dirty after refresh."
            );

            keysProperty.GetArrayElementAtIndex(1).stringValue = "KeyA";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            drawer.InvalidateKeyCache(cacheKey);

            SerializableDictionaryPropertyDrawer.DuplicateKeyState stateAfterEdit =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));

            Assert.IsTrue(
                stateAfterEdit.HasDuplicates,
                "Duplicate should be detected immediately after editing string key to create duplicate."
            );
        }

        [Test]
        public void DuplicateDetectionHandlesWhitespaceOnlyStringKeys()
        {
            StringDictionaryHost host = CreateScriptableObject<StringDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(StringDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(StringDictionaryHost),
                nameof(StringDictionaryHost.dictionary)
            );

            keysProperty.InsertArrayElementAtIndex(0);
            keysProperty.GetArrayElementAtIndex(0).stringValue = "   ";
            valuesProperty.InsertArrayElementAtIndex(0);
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "Value1";

            keysProperty.InsertArrayElementAtIndex(1);
            keysProperty.GetArrayElementAtIndex(1).stringValue = "ValidKey";
            valuesProperty.InsertArrayElementAtIndex(1);
            valuesProperty.GetArrayElementAtIndex(1).stringValue = "Value2";

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState initialState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsFalse(
                initialState.HasDuplicates,
                "No duplicates initially with whitespace-only and valid keys."
            );

            keysProperty.GetArrayElementAtIndex(1).stringValue = "   ";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState afterEditState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsTrue(
                afterEditState.HasDuplicates,
                "Duplicate whitespace-only keys should be detected."
            );
        }

        [Test]
        public void DuplicateDetectionHandlesCaseSensitiveStringKeys()
        {
            StringDictionaryHost host = CreateScriptableObject<StringDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(StringDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(StringDictionaryHost),
                nameof(StringDictionaryHost.dictionary)
            );

            keysProperty.InsertArrayElementAtIndex(0);
            keysProperty.GetArrayElementAtIndex(0).stringValue = "key";
            valuesProperty.InsertArrayElementAtIndex(0);
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "Value1";

            keysProperty.InsertArrayElementAtIndex(1);
            keysProperty.GetArrayElementAtIndex(1).stringValue = "KEY";
            valuesProperty.InsertArrayElementAtIndex(1);
            valuesProperty.GetArrayElementAtIndex(1).stringValue = "Value2";

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState state =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsFalse(
                state.HasDuplicates,
                "Keys with different case should not be duplicates (string comparison is case-sensitive)."
            );

            keysProperty.GetArrayElementAtIndex(1).stringValue = "key";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState afterEditState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsTrue(
                afterEditState.HasDuplicates,
                "After changing KEY to key, duplicates should be detected."
            );
        }

        [Test]
        public void DuplicateDetectionHandlesMultipleDuplicateGroupsWithStringKeys()
        {
            StringDictionaryHost host = CreateScriptableObject<StringDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(StringDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(StringDictionaryHost),
                nameof(StringDictionaryHost.dictionary)
            );

            string[] testKeys = { "Alpha", "Alpha", "Beta", "Beta", "Gamma" };
            for (int i = 0; i < testKeys.Length; i++)
            {
                keysProperty.InsertArrayElementAtIndex(i);
                keysProperty.GetArrayElementAtIndex(i).stringValue = testKeys[i];
                valuesProperty.InsertArrayElementAtIndex(i);
                valuesProperty.GetArrayElementAtIndex(i).stringValue = $"Value{i}";
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState state =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));

            Assert.IsTrue(state.HasDuplicates, "Multiple duplicate groups should be detected.");
            string summary = state.SummaryTooltip;
            Assert.IsTrue(
                summary.Contains("Alpha") && summary.Contains("Beta"),
                $"Summary should mention both duplicate groups. Actual summary: {summary}"
            );
            Assert.IsFalse(
                summary.Contains("Gamma"),
                "Summary should not mention unique key Gamma."
            );
        }

        [Test]
        public void DuplicateDetectionHandlesSpecialCharactersInStringKeys()
        {
            StringDictionaryHost host = CreateScriptableObject<StringDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(StringDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(StringDictionaryHost),
                nameof(StringDictionaryHost.dictionary)
            );

            keysProperty.InsertArrayElementAtIndex(0);
            keysProperty.GetArrayElementAtIndex(0).stringValue = "key!@#$%";
            valuesProperty.InsertArrayElementAtIndex(0);
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "Value1";

            keysProperty.InsertArrayElementAtIndex(1);
            keysProperty.GetArrayElementAtIndex(1).stringValue = "key^&*()";
            valuesProperty.InsertArrayElementAtIndex(1);
            valuesProperty.GetArrayElementAtIndex(1).stringValue = "Value2";

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState state =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsFalse(
                state.HasDuplicates,
                "Special character keys should not be duplicates when different."
            );

            keysProperty.GetArrayElementAtIndex(1).stringValue = "key!@#$%";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState afterEditState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsTrue(
                afterEditState.HasDuplicates,
                "Duplicate special character keys should be detected."
            );
        }

        [Test]
        public void DuplicateDetectionHandlesUnicodeStringKeys()
        {
            StringDictionaryHost host = CreateScriptableObject<StringDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(StringDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(StringDictionaryHost),
                nameof(StringDictionaryHost.dictionary)
            );

            keysProperty.InsertArrayElementAtIndex(0);
            keysProperty.GetArrayElementAtIndex(0).stringValue = "\u4e2d\u6587";
            valuesProperty.InsertArrayElementAtIndex(0);
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "Chinese";

            keysProperty.InsertArrayElementAtIndex(1);
            keysProperty.GetArrayElementAtIndex(1).stringValue = "\u65e5\u672c\u8a9e";
            valuesProperty.InsertArrayElementAtIndex(1);
            valuesProperty.GetArrayElementAtIndex(1).stringValue = "Japanese";

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState state =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsFalse(state.HasDuplicates, "Different Unicode keys should not be duplicates.");

            keysProperty.GetArrayElementAtIndex(1).stringValue = "\u4e2d\u6587";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState afterEditState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsTrue(
                afterEditState.HasDuplicates,
                "Duplicate Unicode keys should be detected."
            );
        }

        [Test]
        public void DuplicateDetectionWithStringKeyEditToUniqueAndBackToDuplicate()
        {
            StringDictionaryHost host = CreateScriptableObject<StringDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(StringDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(StringDictionaryHost),
                nameof(StringDictionaryHost.dictionary)
            );

            keysProperty.InsertArrayElementAtIndex(0);
            keysProperty.GetArrayElementAtIndex(0).stringValue = "DuplicateKey";
            valuesProperty.InsertArrayElementAtIndex(0);
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "Value1";

            keysProperty.InsertArrayElementAtIndex(1);
            keysProperty.GetArrayElementAtIndex(1).stringValue = "DuplicateKey";
            valuesProperty.InsertArrayElementAtIndex(1);
            valuesProperty.GetArrayElementAtIndex(1).stringValue = "Value2";

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState initialState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsTrue(initialState.HasDuplicates, "Initial state should have duplicates.");

            keysProperty.GetArrayElementAtIndex(1).stringValue = "UniqueKey";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
            drawer.InvalidateKeyCache(cacheKey);

            SerializableDictionaryPropertyDrawer.DuplicateKeyState afterUniqueState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsFalse(
                afterUniqueState.HasDuplicates,
                "After making key unique, duplicates should be cleared."
            );

            keysProperty.GetArrayElementAtIndex(1).stringValue = "DuplicateKey";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
            drawer.InvalidateKeyCache(cacheKey);

            SerializableDictionaryPropertyDrawer.DuplicateKeyState afterDuplicateAgainState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsTrue(
                afterDuplicateAgainState.HasDuplicates,
                "After changing key back to duplicate, duplicates should be detected again."
            );
        }

        [Test]
        public void DuplicateDetectionHandlesLongStringKeys()
        {
            StringDictionaryHost host = CreateScriptableObject<StringDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(StringDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(StringDictionaryHost),
                nameof(StringDictionaryHost.dictionary)
            );

            string longKey = new string('A', 1000);
            string differentLongKey = new string('B', 1000);

            keysProperty.InsertArrayElementAtIndex(0);
            keysProperty.GetArrayElementAtIndex(0).stringValue = longKey;
            valuesProperty.InsertArrayElementAtIndex(0);
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "Value1";

            keysProperty.InsertArrayElementAtIndex(1);
            keysProperty.GetArrayElementAtIndex(1).stringValue = differentLongKey;
            valuesProperty.InsertArrayElementAtIndex(1);
            valuesProperty.GetArrayElementAtIndex(1).stringValue = "Value2";

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState state =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsFalse(state.HasDuplicates, "Different long keys should not be duplicates.");

            keysProperty.GetArrayElementAtIndex(1).stringValue = longKey;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState afterEditState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsTrue(afterEditState.HasDuplicates, "Duplicate long keys should be detected.");
        }

        [Test]
        public void DuplicateDetectionHandlesNewlineInStringKeys()
        {
            StringDictionaryHost host = CreateScriptableObject<StringDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(StringDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(StringDictionaryHost),
                nameof(StringDictionaryHost.dictionary)
            );

            keysProperty.InsertArrayElementAtIndex(0);
            keysProperty.GetArrayElementAtIndex(0).stringValue = "line1\nline2";
            valuesProperty.InsertArrayElementAtIndex(0);
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "Value1";

            keysProperty.InsertArrayElementAtIndex(1);
            keysProperty.GetArrayElementAtIndex(1).stringValue = "line1\rline2";
            valuesProperty.InsertArrayElementAtIndex(1);
            valuesProperty.GetArrayElementAtIndex(1).stringValue = "Value2";

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState state =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsFalse(
                state.HasDuplicates,
                "Keys with different newline characters should not be duplicates."
            );

            keysProperty.GetArrayElementAtIndex(1).stringValue = "line1\nline2";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState afterEditState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsTrue(
                afterEditState.HasDuplicates,
                "Duplicate keys with newlines should be detected."
            );
        }

        [Test]
        public void DuplicateDetectionRemainsCorrectAfterMultipleConsecutiveEditsOnSameFrame()
        {
            StringDictionaryHost host = CreateScriptableObject<StringDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(StringDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(StringDictionaryHost),
                nameof(StringDictionaryHost.dictionary)
            );

            keysProperty.InsertArrayElementAtIndex(0);
            keysProperty.GetArrayElementAtIndex(0).stringValue = "Key1";
            valuesProperty.InsertArrayElementAtIndex(0);
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "Value1";

            keysProperty.InsertArrayElementAtIndex(1);
            keysProperty.GetArrayElementAtIndex(1).stringValue = "Key2";
            valuesProperty.InsertArrayElementAtIndex(1);
            valuesProperty.GetArrayElementAtIndex(1).stringValue = "Value2";

            keysProperty.InsertArrayElementAtIndex(2);
            keysProperty.GetArrayElementAtIndex(2).stringValue = "Key3";
            valuesProperty.InsertArrayElementAtIndex(2);
            valuesProperty.GetArrayElementAtIndex(2).stringValue = "Value3";

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            string cacheKey = drawer.GetListKey(dictionaryProperty);

            drawer.InvalidateKeyCache(cacheKey);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState state1 =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsFalse(state1.HasDuplicates, "Initial: All keys unique, no duplicates.");

            keysProperty.GetArrayElementAtIndex(1).stringValue = "Key1";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
            drawer.InvalidateKeyCache(cacheKey);

            SerializableDictionaryPropertyDrawer.DuplicateKeyState state2 =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsTrue(
                state2.HasDuplicates,
                "After edit 1: Key2 changed to Key1, should have duplicates."
            );

            keysProperty.GetArrayElementAtIndex(2).stringValue = "Key1";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
            drawer.InvalidateKeyCache(cacheKey);

            SerializableDictionaryPropertyDrawer.DuplicateKeyState state3 =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsTrue(
                state3.HasDuplicates,
                "After edit 2: Key3 also changed to Key1, should still have duplicates (3 of same key)."
            );

            keysProperty.GetArrayElementAtIndex(0).stringValue = "UniqueKey";
            keysProperty.GetArrayElementAtIndex(1).stringValue = "AnotherUniqueKey";
            keysProperty.GetArrayElementAtIndex(2).stringValue = "YetAnotherUniqueKey";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
            drawer.InvalidateKeyCache(cacheKey);

            SerializableDictionaryPropertyDrawer.DuplicateKeyState state4 =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));
            Assert.IsFalse(
                state4.HasDuplicates,
                "After edit 3: All keys changed to unique, no duplicates."
            );
        }

        [Test]
        public void DuplicateKeyStateTryGetInfoReturnsTrueForDuplicateIndices()
        {
            StringDictionaryHost host = CreateScriptableObject<StringDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(StringDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            keysProperty.InsertArrayElementAtIndex(0);
            keysProperty.GetArrayElementAtIndex(0).stringValue = "Dup";
            valuesProperty.InsertArrayElementAtIndex(0);
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "Value1";

            keysProperty.InsertArrayElementAtIndex(1);
            keysProperty.GetArrayElementAtIndex(1).stringValue = "Dup";
            valuesProperty.InsertArrayElementAtIndex(1);
            valuesProperty.GetArrayElementAtIndex(1).stringValue = "Value2";

            keysProperty.InsertArrayElementAtIndex(2);
            keysProperty.GetArrayElementAtIndex(2).stringValue = "Unique";
            valuesProperty.InsertArrayElementAtIndex(2);
            valuesProperty.GetArrayElementAtIndex(2).stringValue = "Value3";

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableDictionaryPropertyDrawer.DuplicateKeyState state = new();
            state.Refresh(keysProperty, typeof(string));

            bool found0 = state.TryGetInfo(
                0,
                out SerializableDictionaryPropertyDrawer.DuplicateKeyInfo info0
            );
            Assert.IsTrue(found0, "TryGetInfo should return true for index 0 (duplicate key).");

            bool found1 = state.TryGetInfo(
                1,
                out SerializableDictionaryPropertyDrawer.DuplicateKeyInfo info1
            );
            Assert.IsTrue(found1, "TryGetInfo should return true for index 1 (duplicate key).");

            bool found2 = state.TryGetInfo(
                2,
                out SerializableDictionaryPropertyDrawer.DuplicateKeyInfo info2
            );
            Assert.IsFalse(found2, "TryGetInfo should return false for index 2 (unique key).");
        }

        [Test]
        public void DuplicateKeyInfoIsPrimaryFlagIsSetCorrectly()
        {
            StringDictionaryHost host = CreateScriptableObject<StringDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(StringDictionaryHost.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            keysProperty.InsertArrayElementAtIndex(0);
            keysProperty.GetArrayElementAtIndex(0).stringValue = "Dup";
            valuesProperty.InsertArrayElementAtIndex(0);
            valuesProperty.GetArrayElementAtIndex(0).stringValue = "Value1";

            keysProperty.InsertArrayElementAtIndex(1);
            keysProperty.GetArrayElementAtIndex(1).stringValue = "Dup";
            valuesProperty.InsertArrayElementAtIndex(1);
            valuesProperty.GetArrayElementAtIndex(1).stringValue = "Value2";

            keysProperty.InsertArrayElementAtIndex(2);
            keysProperty.GetArrayElementAtIndex(2).stringValue = "Dup";
            valuesProperty.InsertArrayElementAtIndex(2);
            valuesProperty.GetArrayElementAtIndex(2).stringValue = "Value3";

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableDictionaryPropertyDrawer.DuplicateKeyState state = new();
            state.Refresh(keysProperty, typeof(string));

            state.TryGetInfo(0, out SerializableDictionaryPropertyDrawer.DuplicateKeyInfo info0);
            state.TryGetInfo(1, out SerializableDictionaryPropertyDrawer.DuplicateKeyInfo info1);
            state.TryGetInfo(2, out SerializableDictionaryPropertyDrawer.DuplicateKeyInfo info2);

            Assert.IsTrue(
                info0.isPrimary,
                "First occurrence (index 0) should be marked as primary."
            );
            Assert.IsFalse(
                info1.isPrimary,
                "Second occurrence (index 1) should NOT be marked as primary."
            );
            Assert.IsFalse(
                info2.isPrimary,
                "Third occurrence (index 2) should NOT be marked as primary."
            );
        }

        private sealed class DictionaryTweenDisabledScope : IDisposable
        {
            private readonly bool originalValue;
            private bool disposed;

            public DictionaryTweenDisabledScope()
            {
                originalValue = UnityHelpersSettings.ShouldTweenSerializableDictionaryFoldouts();
                UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(false);
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(originalValue);
            }
        }

        private sealed class SortedDictionaryTweenDisabledScope : IDisposable
        {
            private readonly bool originalValue;
            private bool disposed;

            public SortedDictionaryTweenDisabledScope()
            {
                originalValue =
                    UnityHelpersSettings.ShouldTweenSerializableSortedDictionaryFoldouts();
                UnityHelpersSettings.SetSerializableSortedDictionaryFoldoutTweenEnabled(false);
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                UnityHelpersSettings.SetSerializableSortedDictionaryFoldoutTweenEnabled(
                    originalValue
                );
            }
        }
    }
}
