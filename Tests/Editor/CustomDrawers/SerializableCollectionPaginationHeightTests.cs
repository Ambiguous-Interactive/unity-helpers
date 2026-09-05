// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    /// <summary>
    /// Tests for pagination height recalculation in SerializableDictionary and SerializableHashSet drawers.
    /// These tests ensure that when switching between pages with different numbers of items,
    /// the inspector height is recalculated immediately to prevent layout overlap issues.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SerializableCollectionPaginationHeightTests : CommonTestBase
    {
        private const int SmallPageSize = 5;
        private const int LargePageCount = 15;
        private const int SmallPageCount = 2;

        [Test]
        public void DictionaryHeightRecalculatesWhenPageIndexChanges()
        {
            DictionaryPageTestHost host = CreateScriptableObject<DictionaryPageTestHost>();
            for (int i = 0; i < LargePageCount + SmallPageCount; i++)
            {
                host.dictionary.Add(i, $"Value {i}");
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(DictionaryPageTestHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableDictionaryPageSize;

            try
            {
                settings.SerializableDictionaryPageSize = SmallPageSize;

                SerializableDictionaryPropertyDrawer drawer = new();
                PropertyDrawerTestHelper.AssignFieldInfo(
                    drawer,
                    typeof(DictionaryPageTestHost),
                    nameof(DictionaryPageTestHost.dictionary)
                );

                SerializableDictionaryPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(dictionaryProperty);
                pagination.pageIndex = 0;
                pagination.pageSize = SmallPageSize;

                float page0Height = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

                pagination.pageIndex = 1;

                float page1Height = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

                pagination.pageIndex = 2;

                float page2Height = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

                Assert.AreEqual(
                    page0Height,
                    page1Height,
                    0.01f,
                    "Pages with same item count should have same height."
                );
                Assert.IsTrue(
                    page2Height < page0Height || Mathf.Approximately(page0Height, page2Height),
                    $"Page 0 (full) should have height >= page 2 (partial). Page0: {page0Height}, Page2: {page2Height}"
                );
            }
            finally
            {
                settings.SerializableDictionaryPageSize = originalPageSize;
            }
        }

        [Test]
        public void DictionaryHeightRecalculatesImmediatelyOnPageSwitch()
        {
            DictionaryPageTestHost host = CreateScriptableObject<DictionaryPageTestHost>();
            for (int i = 0; i < LargePageCount + SmallPageCount; i++)
            {
                host.dictionary.Add(i, $"Value {i}");
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(DictionaryPageTestHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableDictionaryPageSize;

            try
            {
                settings.SerializableDictionaryPageSize = SmallPageSize;

                SerializableDictionaryPropertyDrawer drawer = new();
                PropertyDrawerTestHelper.AssignFieldInfo(
                    drawer,
                    typeof(DictionaryPageTestHost),
                    nameof(DictionaryPageTestHost.dictionary)
                );

                SerializableDictionaryPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(dictionaryProperty);
                pagination.pageIndex = 0;
                pagination.pageSize = SmallPageSize;

                float initialHeight = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

                float collapsedHeight = EditorGUIUtility.singleLineHeight;
                Assert.IsTrue(
                    collapsedHeight < initialHeight,
                    $"Initial height ({initialHeight}) should be greater than collapsed height ({collapsedHeight}). This suggests the drawer's fieldInfo may not be set correctly."
                );

                pagination.pageIndex = 3;

                float newHeight = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

                Assert.AreNotEqual(
                    initialHeight,
                    newHeight,
                    $"Height should change when switching to a page with different item count. Initial (page 0, {SmallPageSize} items): {initialHeight}, New (page 3, partial): {newHeight}"
                );
            }
            finally
            {
                settings.SerializableDictionaryPageSize = originalPageSize;
            }
        }

        [Test]
        public void DictionaryHeightCacheInvalidatesOnPageIndexChange()
        {
            DictionaryPageTestHost host = CreateScriptableObject<DictionaryPageTestHost>();
            for (int i = 0; i < LargePageCount + SmallPageCount; i++)
            {
                host.dictionary.Add(i, $"Value {i}");
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(DictionaryPageTestHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableDictionaryPageSize;

            try
            {
                settings.SerializableDictionaryPageSize = SmallPageSize;

                SerializableDictionaryPropertyDrawer drawer = new();
                PropertyDrawerTestHelper.AssignFieldInfo(
                    drawer,
                    typeof(DictionaryPageTestHost),
                    nameof(DictionaryPageTestHost.dictionary)
                );

                SerializableDictionaryPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(dictionaryProperty);
                pagination.pageIndex = 0;
                pagination.pageSize = SmallPageSize;

                float height1 = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);
                float height2 = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

                float collapsedHeight = EditorGUIUtility.singleLineHeight;
                Assert.IsTrue(
                    collapsedHeight < height1,
                    $"Height ({height1}) should be greater than collapsed height ({collapsedHeight}). This suggests the drawer's fieldInfo may not be set correctly."
                );

                Assert.AreEqual(
                    height1,
                    height2,
                    0.01f,
                    "Consecutive calls with same page index should return cached height."
                );

                pagination.pageIndex = 3;

                float height3 = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);
                Assert.AreNotEqual(
                    height2,
                    height3,
                    $"Height cache should invalidate when page index changes. Page 0 height: {height2}, Page 3 height: {height3}"
                );
            }
            finally
            {
                settings.SerializableDictionaryPageSize = originalPageSize;
            }
        }

        [Test]
        public void DictionaryHeightStaysConsistentWithinSamePage()
        {
            DictionaryPageTestHost host = CreateScriptableObject<DictionaryPageTestHost>();
            for (int i = 0; i < LargePageCount; i++)
            {
                host.dictionary.Add(i, $"Value {i}");
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(DictionaryPageTestHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableDictionaryPageSize;

            try
            {
                settings.SerializableDictionaryPageSize = SmallPageSize;

                SerializableDictionaryPropertyDrawer drawer = new();
                PropertyDrawerTestHelper.AssignFieldInfo(
                    drawer,
                    typeof(DictionaryPageTestHost),
                    nameof(DictionaryPageTestHost.dictionary)
                );

                SerializableDictionaryPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(dictionaryProperty);
                pagination.pageIndex = 0;
                pagination.pageSize = SmallPageSize;

                float height1 = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);
                float height2 = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);
                float height3 = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

                Assert.AreEqual(height1, height2, 0.01f, "Heights should be consistent.");
                Assert.AreEqual(height2, height3, 0.01f, "Heights should be consistent.");
            }
            finally
            {
                settings.SerializableDictionaryPageSize = originalPageSize;
            }
        }

        [Test]
        public void DictionaryHeightCorrectAfterRapidPageSwitching()
        {
            DictionaryPageTestHost host = CreateScriptableObject<DictionaryPageTestHost>();
            for (int i = 0; i < LargePageCount + SmallPageCount; i++)
            {
                host.dictionary.Add(i, $"Value {i}");
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(DictionaryPageTestHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableDictionaryPageSize;

            try
            {
                settings.SerializableDictionaryPageSize = SmallPageSize;

                SerializableDictionaryPropertyDrawer drawer = new();
                PropertyDrawerTestHelper.AssignFieldInfo(
                    drawer,
                    typeof(DictionaryPageTestHost),
                    nameof(DictionaryPageTestHost.dictionary)
                );

                SerializableDictionaryPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(dictionaryProperty);
                pagination.pageSize = SmallPageSize;

                pagination.pageIndex = 0;
                float heightPage0First = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

                pagination.pageIndex = 1;
                drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

                pagination.pageIndex = 2;
                drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

                pagination.pageIndex = 3;
                float heightPage3 = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

                pagination.pageIndex = 0;
                float heightPage0Second = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

                Assert.AreEqual(
                    heightPage0First,
                    heightPage0Second,
                    0.01f,
                    "Height should be the same when returning to the same page."
                );
                Assert.IsTrue(
                    heightPage3 < heightPage0First
                        || Mathf.Approximately(heightPage0First, heightPage3),
                    "Full page should have height >= partial page."
                );
            }
            finally
            {
                settings.SerializableDictionaryPageSize = originalPageSize;
            }
        }

        [Test]
        public void DictionaryWithComplexValuesHeightRecalculatesOnPageSwitch()
        {
            ComplexDictionaryPageTestHost host =
                CreateScriptableObject<ComplexDictionaryPageTestHost>();
            for (int i = 0; i < LargePageCount + SmallPageCount; i++)
            {
                host.dictionary.Add(
                    i,
                    new ComplexDictionaryPageTestValue
                    {
                        primaryColor = Color.red,
                        secondaryColor = Color.blue,
                        label = $"Item {i}",
                    }
                );
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(ComplexDictionaryPageTestHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableDictionaryPageSize;

            try
            {
                settings.SerializableDictionaryPageSize = SmallPageSize;

                SerializableDictionaryPropertyDrawer drawer = new();
                PropertyDrawerTestHelper.AssignFieldInfo(
                    drawer,
                    typeof(ComplexDictionaryPageTestHost),
                    nameof(ComplexDictionaryPageTestHost.dictionary)
                );

                SerializableDictionaryPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(dictionaryProperty);
                pagination.pageIndex = 0;
                pagination.pageSize = SmallPageSize;

                float fullPageHeight = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

                pagination.pageIndex = 3;

                float partialPageHeight = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

                Assert.IsTrue(
                    partialPageHeight < fullPageHeight
                        || Mathf.Approximately(fullPageHeight, partialPageHeight),
                    $"Full page with complex values should have height >= partial page. Full: {fullPageHeight}, Partial: {partialPageHeight}"
                );
            }
            finally
            {
                settings.SerializableDictionaryPageSize = originalPageSize;
            }
        }

        [Test]
        public void HashSetHeightRecalculatesWhenPageIndexChanges()
        {
            SetPageTestHost host = CreateScriptableObject<SetPageTestHost>();
            for (int i = 0; i < LargePageCount + SmallPageCount; i++)
            {
                host.hashSet.Add(i);
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SetPageTestHost.hashSet)
            );
            setProperty.isExpanded = true;

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableSetPageSize;

            try
            {
                settings.SerializableSetPageSize = SmallPageSize;

                SerializableSetPropertyDrawer drawer = new();

                SerializableSetPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(setProperty);
                pagination.page = 0;
                pagination.pageSize = SmallPageSize;

                float page0Height = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pagination.page = 1;

                float page1Height = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pagination.page = 2;

                float page2Height = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                Assert.AreEqual(
                    page0Height,
                    page1Height,
                    0.01f,
                    "Pages with same item count should have same height."
                );
                Assert.IsTrue(
                    page2Height < page0Height || Mathf.Approximately(page0Height, page2Height),
                    $"Page 0 (full) should have height >= page 2 (partial). Page0: {page0Height}, Page2: {page2Height}"
                );
            }
            finally
            {
                settings.SerializableSetPageSize = originalPageSize;
            }
        }

        [Test]
        public void HashSetHeightRecalculatesImmediatelyOnPageSwitch()
        {
            SetPageTestHost host = CreateScriptableObject<SetPageTestHost>();
            for (int i = 0; i < LargePageCount + SmallPageCount; i++)
            {
                host.hashSet.Add(i);
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SetPageTestHost.hashSet)
            );
            setProperty.isExpanded = true;

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableSetPageSize;

            try
            {
                settings.SerializableSetPageSize = SmallPageSize;

                SerializableSetPropertyDrawer drawer = new();

                SerializableSetPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(setProperty);
                pagination.page = 0;
                pagination.pageSize = SmallPageSize;

                float initialHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pagination.page = 3;

                float newHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                Assert.AreNotEqual(
                    initialHeight,
                    newHeight,
                    "Height should change when switching to a page with different item count."
                );
            }
            finally
            {
                settings.SerializableSetPageSize = originalPageSize;
            }
        }

        [Test]
        public void HashSetHeightCacheInvalidatesOnPageIndexChange()
        {
            SetPageTestHost host = CreateScriptableObject<SetPageTestHost>();
            for (int i = 0; i < LargePageCount + SmallPageCount; i++)
            {
                host.hashSet.Add(i);
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SetPageTestHost.hashSet)
            );
            setProperty.isExpanded = true;

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableSetPageSize;

            try
            {
                settings.SerializableSetPageSize = SmallPageSize;

                SerializableSetPropertyDrawer drawer = new();

                SerializableSetPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(setProperty);
                pagination.page = 0;
                pagination.pageSize = SmallPageSize;

                float height1 = drawer.GetPropertyHeight(setProperty, GUIContent.none);
                float height2 = drawer.GetPropertyHeight(setProperty, GUIContent.none);
                Assert.AreEqual(
                    height1,
                    height2,
                    0.01f,
                    "Consecutive calls with same page index should return cached height."
                );

                pagination.page = 3;

                float height3 = drawer.GetPropertyHeight(setProperty, GUIContent.none);
                Assert.AreNotEqual(
                    height2,
                    height3,
                    "Height cache should invalidate when page index changes."
                );
            }
            finally
            {
                settings.SerializableSetPageSize = originalPageSize;
            }
        }

        [Test]
        public void HashSetHeightStaysConsistentWithinSamePage()
        {
            SetPageTestHost host = CreateScriptableObject<SetPageTestHost>();
            for (int i = 0; i < LargePageCount; i++)
            {
                host.hashSet.Add(i);
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SetPageTestHost.hashSet)
            );
            setProperty.isExpanded = true;

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableSetPageSize;

            try
            {
                settings.SerializableSetPageSize = SmallPageSize;

                SerializableSetPropertyDrawer drawer = new();

                SerializableSetPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(setProperty);
                pagination.page = 0;
                pagination.pageSize = SmallPageSize;

                float height1 = drawer.GetPropertyHeight(setProperty, GUIContent.none);
                float height2 = drawer.GetPropertyHeight(setProperty, GUIContent.none);
                float height3 = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                Assert.AreEqual(height1, height2, 0.01f, "Heights should be consistent.");
                Assert.AreEqual(height2, height3, 0.01f, "Heights should be consistent.");
            }
            finally
            {
                settings.SerializableSetPageSize = originalPageSize;
            }
        }

        [Test]
        public void HashSetHeightCorrectAfterRapidPageSwitching()
        {
            SetPageTestHost host = CreateScriptableObject<SetPageTestHost>();
            for (int i = 0; i < LargePageCount + SmallPageCount; i++)
            {
                host.hashSet.Add(i);
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SetPageTestHost.hashSet)
            );
            setProperty.isExpanded = true;

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableSetPageSize;

            try
            {
                settings.SerializableSetPageSize = SmallPageSize;

                SerializableSetPropertyDrawer drawer = new();

                SerializableSetPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(setProperty);
                pagination.pageSize = SmallPageSize;

                pagination.page = 0;
                float heightPage0First = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pagination.page = 1;
                drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pagination.page = 2;
                drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pagination.page = 3;
                float heightPage3 = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pagination.page = 0;
                float heightPage0Second = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                Assert.AreEqual(
                    heightPage0First,
                    heightPage0Second,
                    0.01f,
                    "Height should be the same when returning to the same page."
                );
                Assert.IsTrue(
                    heightPage3 < heightPage0First
                        || Mathf.Approximately(heightPage0First, heightPage3),
                    "Full page should have height >= partial page."
                );
            }
            finally
            {
                settings.SerializableSetPageSize = originalPageSize;
            }
        }

        [Test]
        public void HashSetWithComplexValuesHeightRecalculatesOnPageSwitch()
        {
            ComplexSetPageTestHost host = CreateScriptableObject<ComplexSetPageTestHost>();
            for (int i = 0; i < LargePageCount + SmallPageCount; i++)
            {
                host.hashSet.Add(
                    new ComplexSetPageTestValue
                    {
                        primaryColor = Color.red,
                        secondaryColor = Color.blue,
                        label = $"Item {i}",
                    }
                );
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(ComplexSetPageTestHost.hashSet)
            );
            setProperty.isExpanded = true;

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableSetPageSize;

            try
            {
                settings.SerializableSetPageSize = SmallPageSize;

                SerializableSetPropertyDrawer drawer = new();

                SerializableSetPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(setProperty);
                pagination.page = 0;
                pagination.pageSize = SmallPageSize;

                float fullPageHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pagination.page = 3;

                float partialPageHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                Assert.IsTrue(
                    partialPageHeight < fullPageHeight
                        || Mathf.Approximately(fullPageHeight, partialPageHeight),
                    $"Full page with complex values should have height >= partial page. Full: {fullPageHeight}, Partial: {partialPageHeight}"
                );
            }
            finally
            {
                settings.SerializableSetPageSize = originalPageSize;
            }
        }

        [Test]
        public void DictionaryHeightCorrectWhenSwitchingFromFullToEmptyishPage()
        {
            DictionaryPageTestHost host = CreateScriptableObject<DictionaryPageTestHost>();
            for (int i = 0; i < SmallPageSize + 1; i++)
            {
                host.dictionary.Add(i, $"Value {i}");
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(DictionaryPageTestHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableDictionaryPageSize;

            try
            {
                settings.SerializableDictionaryPageSize = SmallPageSize;

                SerializableDictionaryPropertyDrawer drawer = new();
                PropertyDrawerTestHelper.AssignFieldInfo(
                    drawer,
                    typeof(DictionaryPageTestHost),
                    nameof(DictionaryPageTestHost.dictionary)
                );

                SerializableDictionaryPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(dictionaryProperty);
                pagination.pageIndex = 0;
                pagination.pageSize = SmallPageSize;

                float fullPageHeight = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

                float collapsedHeight = EditorGUIUtility.singleLineHeight;
                Assert.IsTrue(
                    collapsedHeight < fullPageHeight,
                    $"Full page height ({fullPageHeight}) should be greater than collapsed height ({collapsedHeight}). This suggests the drawer's fieldInfo may not be set correctly."
                );

                pagination.pageIndex = 1;

                float singleItemPageHeight = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

                Assert.IsTrue(
                    singleItemPageHeight < fullPageHeight,
                    $"Full page ({SmallPageSize} items) should be taller than single-item page. Full: {fullPageHeight}, Single: {singleItemPageHeight}"
                );
            }
            finally
            {
                settings.SerializableDictionaryPageSize = originalPageSize;
            }
        }

        [Test]
        public void HashSetHeightCorrectWhenSwitchingFromFullToEmptyishPage()
        {
            SetPageTestHost host = CreateScriptableObject<SetPageTestHost>();
            for (int i = 0; i < SmallPageSize + 1; i++)
            {
                host.hashSet.Add(i);
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SetPageTestHost.hashSet)
            );
            setProperty.isExpanded = true;

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableSetPageSize;

            try
            {
                settings.SerializableSetPageSize = SmallPageSize;

                SerializableSetPropertyDrawer drawer = new();

                SerializableSetPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(setProperty);
                pagination.page = 0;
                pagination.pageSize = SmallPageSize;

                float fullPageHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pagination.page = 1;

                float singleItemPageHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                Assert.IsTrue(
                    singleItemPageHeight < fullPageHeight,
                    $"Full page ({SmallPageSize} items) should be taller than single-item page. Full: {fullPageHeight}, Single: {singleItemPageHeight}"
                );
            }
            finally
            {
                settings.SerializableSetPageSize = originalPageSize;
            }
        }

        [Test]
        public void DictionaryHeightCorrectWithSingleItemOnLastPage()
        {
            DictionaryPageTestHost host = CreateScriptableObject<DictionaryPageTestHost>();
            int totalItems = SmallPageSize * 2 + 1;
            for (int i = 0; i < totalItems; i++)
            {
                host.dictionary.Add(i, $"Value {i}");
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(DictionaryPageTestHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableDictionaryPageSize;

            try
            {
                settings.SerializableDictionaryPageSize = SmallPageSize;

                SerializableDictionaryPropertyDrawer drawer = new();
                PropertyDrawerTestHelper.AssignFieldInfo(
                    drawer,
                    typeof(DictionaryPageTestHost),
                    nameof(DictionaryPageTestHost.dictionary)
                );

                SerializableDictionaryPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(dictionaryProperty);
                pagination.pageSize = SmallPageSize;

                pagination.pageIndex = 0;
                float page0Height = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

                float collapsedHeight = EditorGUIUtility.singleLineHeight;
                Assert.IsTrue(
                    collapsedHeight < page0Height,
                    $"Page 0 height ({page0Height}) should be greater than collapsed height ({collapsedHeight}). This suggests the drawer's fieldInfo may not be set correctly."
                );

                pagination.pageIndex = 1;
                float page1Height = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

                pagination.pageIndex = 2;
                float page2Height = drawer.GetPropertyHeight(dictionaryProperty, GUIContent.none);

                Assert.AreEqual(
                    page0Height,
                    page1Height,
                    0.01f,
                    $"Full pages should have the same height. Page 0: {page0Height}, Page 1: {page1Height}"
                );
                Assert.IsTrue(
                    page2Height < page0Height,
                    $"Last page with 1 item should be shorter than full pages. Page 0: {page0Height}, Page 2: {page2Height}"
                );
            }
            finally
            {
                settings.SerializableDictionaryPageSize = originalPageSize;
            }
        }

        [Test]
        public void HashSetHeightCorrectWithSingleItemOnLastPage()
        {
            SetPageTestHost host = CreateScriptableObject<SetPageTestHost>();
            int totalItems = SmallPageSize * 2 + 1;
            for (int i = 0; i < totalItems; i++)
            {
                host.hashSet.Add(i);
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SetPageTestHost.hashSet)
            );
            setProperty.isExpanded = true;

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableSetPageSize;

            try
            {
                settings.SerializableSetPageSize = SmallPageSize;

                SerializableSetPropertyDrawer drawer = new();

                SerializableSetPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(setProperty);
                pagination.pageSize = SmallPageSize;

                pagination.page = 0;
                float page0Height = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pagination.page = 1;
                float page1Height = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pagination.page = 2;
                float page2Height = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                Assert.AreEqual(
                    page0Height,
                    page1Height,
                    0.01f,
                    "Full pages should have the same height."
                );
                Assert.IsTrue(
                    page2Height < page0Height,
                    "Last page with 1 item should be shorter than full pages."
                );
            }
            finally
            {
                settings.SerializableSetPageSize = originalPageSize;
            }
        }

        [Test]
        public void DictionaryHeightDoesNotUseStalePageHeightAfterPageChange()
        {
            DictionaryPageTestHost host = CreateScriptableObject<DictionaryPageTestHost>();
            int totalItems = SmallPageSize * 3 + 1;
            for (int i = 0; i < totalItems; i++)
            {
                host.dictionary.Add(i, $"Value {i}");
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(DictionaryPageTestHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableDictionaryPageSize;

            try
            {
                settings.SerializableDictionaryPageSize = SmallPageSize;

                SerializableDictionaryPropertyDrawer drawer = new();
                PropertyDrawerTestHelper.AssignFieldInfo(
                    drawer,
                    typeof(DictionaryPageTestHost),
                    nameof(DictionaryPageTestHost.dictionary)
                );

                SerializableDictionaryPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(dictionaryProperty);
                pagination.pageSize = SmallPageSize;

                pagination.pageIndex = 0;
                float fullPageHeight = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

                pagination.pageIndex = 3;
                float lastPageHeight = drawer.GetPropertyHeight(
                    dictionaryProperty,
                    GUIContent.none
                );

                Assert.IsTrue(
                    lastPageHeight < fullPageHeight,
                    $"Last page height should be calculated fresh, not use cached full page height. Full: {fullPageHeight}, Last: {lastPageHeight}"
                );
            }
            finally
            {
                settings.SerializableDictionaryPageSize = originalPageSize;
            }
        }

        [Test]
        public void HashSetHeightDoesNotUseStalePageHeightAfterPageChange()
        {
            SetPageTestHost host = CreateScriptableObject<SetPageTestHost>();
            int totalItems = SmallPageSize * 3 + 1;
            for (int i = 0; i < totalItems; i++)
            {
                host.hashSet.Add(i);
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(SetPageTestHost.hashSet)
            );
            setProperty.isExpanded = true;

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.SerializableSetPageSize;

            try
            {
                settings.SerializableSetPageSize = SmallPageSize;

                SerializableSetPropertyDrawer drawer = new();

                SerializableSetPropertyDrawer.PaginationState pagination =
                    drawer.GetOrCreatePaginationState(setProperty);
                pagination.pageSize = SmallPageSize;

                pagination.page = 0;
                float fullPageHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                pagination.page = 3;
                float lastPageHeight = drawer.GetPropertyHeight(setProperty, GUIContent.none);

                Assert.IsTrue(
                    lastPageHeight < fullPageHeight,
                    $"Last page height should be calculated fresh, not use cached full page height. Full: {fullPageHeight}, Last: {lastPageHeight}"
                );
            }
            finally
            {
                settings.SerializableSetPageSize = originalPageSize;
            }
        }
    }
}
