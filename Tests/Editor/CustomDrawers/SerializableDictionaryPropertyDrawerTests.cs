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

        internal static void AssignDictionaryFieldInfo(
            SerializableDictionaryPropertyDrawer drawer,
            Type hostType,
            string fieldName
        )
        {
            PropertyDrawerTestHelper.AssignFieldInfo(drawer, hostType, fieldName);
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

            for (int i = 0; i < entries.Count; ++i)
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

            for (int i = 0; i < entries.Count; ++i)
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
            for (int i = 0; i < property.arraySize; ++i)
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

            for (int i = 0; i < keysProperty.arraySize; ++i)
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

        [SetUp]
        public override void BaseSetUp()
        {
            _sharedHost = CreateScriptableObject<TestDictionaryHost>();
            _sharedSerializedObject = new SerializedObject(_sharedHost);
            GroupGUIWidthUtilityTestAccess.Reset();
            SerializableDictionaryPropertyDrawerTestAccess.ResetLayoutTracking();
            SerializableDictionaryPropertyDrawerTestAccess.ClearMainFoldoutAnimCache();
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

        [TestCase(false)]
        [TestCase(true)]
        public void PendingWrapperReleaseDisposesViewWhileUnownedTargetRemainsAlive(
            bool isValueField
        )
        {
            SerializableDictionaryPropertyDrawer.PendingEntry pending = new();
            try
            {
                SerializableDictionaryPropertyDrawer.PendingWrapperContext context =
                    SerializableDictionaryPropertyDrawer.EnsurePendingWrapper(
                        pending,
                        typeof(ColorData),
                        isValueField
                    );
                PendingValueWrapper wrapper = Track(context.Wrapper);
                SerializedObject serialized = TrackDisposable(context.Serialized);
                if (isValueField)
                {
                    pending.valueWrapper = null;
                }
                else
                {
                    pending.keyWrapper = null;
                }

                SerializableDictionaryPropertyDrawer.ReleasePendingWrapper(pending, isValueField);

                Assert.IsTrue(
                    wrapper != null,
                    "A missing owner reference must not destroy a borrowed target."
                );
                Assert.Catch(
                    () => serialized.Update(),
                    "The native view must be disposed even when its target remains alive."
                );
            }
            finally
            {
                SerializableDictionaryPropertyDrawer.ReleasePendingWrapper(pending, isValueField);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PendingWrapperReleaseDisposesOwnedViewAndPreservesBorrowedContext(
            bool isValueField
        )
        {
            SerializableDictionaryPropertyDrawer.PendingEntry pending = new();
            try
            {
                SerializableDictionaryPropertyDrawer.PendingWrapperContext context =
                    SerializableDictionaryPropertyDrawer.EnsurePendingWrapper(
                        pending,
                        typeof(ColorData),
                        isValueField
                    );
                Assert.IsTrue(context.Wrapper != null);
                Assert.IsTrue(context.Serialized != null);
                SerializedObject serialized = TrackDisposable(context.Serialized);
                PendingValueWrapper wrapper = context.Wrapper;
                SerializableDictionaryPropertyDrawer.PendingWrapperContext borrowed =
                    SerializableDictionaryPropertyDrawer.EnsurePendingWrapper(
                        pending,
                        typeof(ColorData),
                        isValueField
                    );
                Assert.AreSame(serialized, borrowed.Serialized);
                Assert.AreSame(context.Property, borrowed.Property);
                Assert.DoesNotThrow(() => borrowed.Serialized.Update());

                SerializableDictionaryPropertyDrawer.ReleasePendingWrapper(pending, isValueField);

                Assert.IsTrue(wrapper == null);
                Assert.IsTrue((isValueField ? pending.valueWrapper : pending.keyWrapper) == null);
                Assert.IsTrue(
                    (isValueField ? pending.valueWrapperSerialized : pending.keyWrapperSerialized)
                        == null
                );
                Assert.IsTrue(
                    (isValueField ? pending.valueWrapperProperty : pending.keyWrapperProperty)
                        == null
                );
                Assert.Catch(() => serialized.Update());
                Assert.DoesNotThrow(() =>
                    SerializableDictionaryPropertyDrawer.ReleasePendingWrapper(
                        pending,
                        isValueField
                    )
                );
            }
            finally
            {
                SerializableDictionaryPropertyDrawer.ReleasePendingWrapper(pending, isValueField);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PendingWrapperMissingPropertyReleasesIncompleteOwnership(bool isValueField)
        {
            SerializableDictionaryPropertyDrawer.PendingEntry pending = new();
            try
            {
                SerializableDictionaryPropertyDrawer.PendingWrapperContext context =
                    SerializableDictionaryPropertyDrawer.EnsurePendingWrapper(
                        pending,
                        typeof(ColorData),
                        isValueField
                    );
                SerializedObject serialized = TrackDisposable(context.Serialized);
                PendingValueWrapper wrapper = context.Wrapper;
                context.Property.Dispose();
                if (isValueField)
                {
                    pending.valueWrapperProperty = null;
                }
                else
                {
                    pending.keyWrapperProperty = null;
                }

                SerializableDictionaryPropertyDrawer.PendingWrapperContext failed =
                    SerializableDictionaryPropertyDrawer.EnsurePendingWrapper(
                        pending,
                        typeof(ColorData),
                        isValueField
                    );

                Assert.IsTrue(failed.Serialized == null);
                Assert.IsTrue(wrapper == null);
                Assert.IsTrue(
                    (isValueField ? pending.valueWrapperSerialized : pending.keyWrapperSerialized)
                        == null
                );
                Assert.Catch(() => serialized.Update());
            }
            finally
            {
                SerializableDictionaryPropertyDrawer.ReleasePendingWrapper(pending, isValueField);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PendingWrapperUpdateFailureReleasesOwnershipAndAllowsRetry(bool isValueField)
        {
            SerializableDictionaryPropertyDrawer.PendingEntry pending = new();
            try
            {
                SerializableDictionaryPropertyDrawer.PendingWrapperContext context =
                    SerializableDictionaryPropertyDrawer.EnsurePendingWrapper(
                        pending,
                        typeof(ColorData),
                        isValueField
                    );
                PendingValueWrapper wrapper = context.Wrapper;
                context.Serialized.Dispose();

                Assert.Catch(() =>
                    SerializableDictionaryPropertyDrawer.EnsurePendingWrapper(
                        pending,
                        typeof(ColorData),
                        isValueField
                    )
                );

                Assert.IsTrue(wrapper == null);
                Assert.IsTrue(
                    (isValueField ? pending.valueWrapperSerialized : pending.keyWrapperSerialized)
                        == null
                );
                SerializableDictionaryPropertyDrawer.PendingWrapperContext retry =
                    SerializableDictionaryPropertyDrawer.EnsurePendingWrapper(
                        pending,
                        typeof(ColorData),
                        isValueField
                    );
                Assert.IsTrue(retry.Wrapper != null);
                Assert.DoesNotThrow(() => retry.Serialized.Update());
            }
            finally
            {
                SerializableDictionaryPropertyDrawer.ReleasePendingWrapper(pending, isValueField);
            }
        }

        [Test]
        public void PageSizeClampPreventsExcessiveCacheGrowth()
        {
            for (int i = 0; i < 512; ++i)
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
            for (int i = 0; i < 64; ++i)
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
            for (int i = 0; i < 120; ++i)
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
            for (int i = 0; i < 30; ++i)
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
            for (int i = 0; i < 20; ++i)
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
            for (int i = 0; i < 30; ++i)
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
            for (int i = 0; i < 40; ++i)
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
            for (int i = 0; i < 21; ++i)
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
                ++index;
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
                ++index;
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

            SerializableDictionaryPropertyDrawerTestAccess.ResetLayoutTracking();
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

        [Test]
        public void GetPropertyHeightIncreasesWhenPendingEntryIsExpanded()
        {
            using (new DictionaryTweenDisabledScope())
            {
                bool tweenEnabled = SerializableDictionaryPropertyDrawer.ShouldTweenPendingFoldout(
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
                bool tweenEnabled = SerializableDictionaryPropertyDrawer.ShouldTweenPendingFoldout(
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
                bool tweenEnabled = SerializableDictionaryPropertyDrawer.ShouldTweenPendingFoldout(
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
                bool tweenEnabled = SerializableDictionaryPropertyDrawer.ShouldTweenPendingFoldout(
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
                bool tweenEnabled = SerializableDictionaryPropertyDrawer.ShouldTweenPendingFoldout(
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
                bool tweenEnabled = SerializableDictionaryPropertyDrawer.ShouldTweenPendingFoldout(
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
                bool tweenEnabled = SerializableDictionaryPropertyDrawer.ShouldTweenPendingFoldout(
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
                bool tweenEnabled = SerializableDictionaryPropertyDrawer.ShouldTweenPendingFoldout(
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

            bool foundTestKey = false;
            for (int i = 0; i < keysProperty.arraySize; ++i)
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

            for (int i = 0; i < 3; ++i)
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

            SerializableDictionaryPropertyDrawer.DuplicateKeyState initialState =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(int));
            Assert.IsTrue(initialState != null, "Initial state should be returned.");
            Assert.IsTrue(initialState.HasDuplicates, "HasDuplicates should be true initially.");

            keysProperty.GetArrayElementAtIndex(1).intValue = 2;
            _sharedSerializedObject.ApplyModifiedPropertiesWithoutUndo();
            _sharedSerializedObject.Update();

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

            SerializableDictionaryPropertyDrawer.NullKeyState initialState =
                drawer.RefreshNullKeyState(cacheKey, keysProperty, typeof(GameObject));
            Assert.IsTrue(
                initialState == null,
                "Initial state should be null when no null keys exist (RefreshNullKeyState returns null for clean dictionaries)."
            );

            Assert.AreEqual(
                2,
                keysProperty.arraySize,
                $"Keys array should have 2 elements before modification."
            );
            Assert.IsTrue(
                keysProperty.GetArrayElementAtIndex(0).objectReferenceValue != null,
                "First key should not be null before modification."
            );

            keysProperty.GetArrayElementAtIndex(0).objectReferenceValue = null;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            Assert.IsTrue(
                keysProperty.GetArrayElementAtIndex(0).objectReferenceValue == null,
                "First key should be null after modification."
            );

            drawer.InvalidateKeyCache(cacheKey);

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

            SerializableDictionaryPropertyDrawer.NullKeyState state1 = drawer.RefreshNullKeyState(
                cacheKey,
                keysProperty,
                typeof(GameObject)
            );
            Assert.IsTrue(state1 == null, "Initial state should be null (no null keys).");

            drawer.InvalidateKeyCache(cacheKey);
            drawer.InvalidateKeyCache(cacheKey);
            drawer.InvalidateKeyCache(cacheKey);

            SerializableDictionaryPropertyDrawer.NullKeyState state2 = drawer.RefreshNullKeyState(
                cacheKey,
                keysProperty,
                typeof(GameObject)
            );
            Assert.IsTrue(
                state2 == null,
                "State should be null after multiple invalidations with no null keys."
            );

            keysProperty.GetArrayElementAtIndex(0).objectReferenceValue = null;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

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

            for (int cycle = 0; cycle < cycles; ++cycle)
            {
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

            SerializableDictionaryPropertyDrawer.DuplicateKeyState dupState1 =
                drawerDuplicate.RefreshDuplicateState(
                    cacheKeyDuplicate,
                    keysPropDuplicate,
                    typeof(int)
                );
            Assert.IsTrue(dupState1 != null, "DuplicateKeyState is always returned.");
            Assert.IsFalse(dupState1.HasDuplicates, "Should not have duplicates initially.");

            keysPropDuplicate.GetArrayElementAtIndex(1).intValue = 1;
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

            SerializableDictionaryPropertyDrawer.NullKeyState nullState1 =
                drawerNull.RefreshNullKeyState(cacheKeyNull, keysPropNull, typeof(GameObject));
            Assert.IsTrue(nullState1 == null, "NullKeyState returns null when no null keys exist.");

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

            SerializableDictionaryPropertyDrawer.NullKeyState initialState =
                drawer.RefreshNullKeyState(cacheKey, keysProperty, typeof(GameObject));
            Assert.IsTrue(
                initialState != null,
                "Initial state should be returned when null key exists."
            );
            Assert.IsTrue(initialState.HasNullKeys, "HasNullKeys should be true initially.");

            GameObject validKey = NewGameObject("ValidKey");
            keysProperty.GetArrayElementAtIndex(0).objectReferenceValue = validKey;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            drawer.InvalidateKeyCache(cacheKey);

            SerializableDictionaryPropertyDrawer.NullKeyState afterFixState =
                drawer.RefreshNullKeyState(cacheKey, keysProperty, typeof(GameObject));

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

        [TestCase("", nameof(GameObject))]
        [TestCase(" ", nameof(GameObject))]
        [TestCase(" \t\r\n", nameof(GameObject))]
        [TestCase("\u00a0", nameof(GameObject))]
        [TestCase("\u2003", nameof(GameObject))]
        [TestCase("\u3000", nameof(GameObject))]
        [TestCase("Visible", "Visible")]
        [TestCase(" Visible ", " Visible ")]
        [TestCase("\u200b", "\u200b")]
        public void DuplicateObjectKeyWarningsUseVisibleNamesWithoutChangingIdentity(
            string objectName,
            string expectedDisplay
        )
        {
            UnityObjectDictionaryHost host = CreateScriptableObject<UnityObjectDictionaryHost>();
            GameObject duplicate = NewGameObject(objectName);
            GameObject distinct = NewGameObject(objectName);
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
            keysProperty.arraySize = 3;
            valuesProperty.arraySize = 3;
            keysProperty.GetArrayElementAtIndex(0).objectReferenceValue = duplicate;
            keysProperty.GetArrayElementAtIndex(1).objectReferenceValue = duplicate;
            keysProperty.GetArrayElementAtIndex(2).objectReferenceValue = distinct;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(UnityObjectDictionaryHost),
                nameof(UnityObjectDictionaryHost.dictionary)
            );
            string cacheKey = drawer.GetListKey(dictionaryProperty);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState state =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(GameObject));

            Assert.IsTrue(state.HasDuplicates);
            StringAssert.Contains(
                $"Duplicate key {expectedDisplay} at entries 1, 2",
                state.SummaryTooltip
            );
            Assert.IsTrue(
                state.TryGetInfo(0, out SerializableDictionaryPropertyDrawer.DuplicateKeyInfo first)
            );
            Assert.IsTrue(
                state.TryGetInfo(
                    1,
                    out SerializableDictionaryPropertyDrawer.DuplicateKeyInfo second
                )
            );
            StringAssert.Contains($"\"{expectedDisplay}\"", first.tooltip);
            Assert.AreEqual(first.tooltip, second.tooltip);
            Assert.IsTrue(first.isPrimary);
            Assert.IsFalse(second.isPrimary);
            Assert.IsFalse(state.TryGetInfo(2, out _));
            Assert.AreSame(duplicate, keysProperty.GetArrayElementAtIndex(0).objectReferenceValue);
            Assert.AreSame(duplicate, keysProperty.GetArrayElementAtIndex(1).objectReferenceValue);
            Assert.AreSame(distinct, keysProperty.GetArrayElementAtIndex(2).objectReferenceValue);
            Assert.AreEqual(objectName, duplicate.name);
            Assert.AreEqual(objectName, distinct.name);
            Assert.IsFalse(serializedObject.hasModifiedProperties);
        }

        [TestCase("", "<empty>")]
        [TestCase(" ", "<empty>")]
        [TestCase(" \t\r\n", "<empty>")]
        [TestCase("\u00a0", "<empty>")]
        [TestCase("\u2003", "<empty>")]
        [TestCase("\u3000", "<empty>")]
        [TestCase("Visible", "Visible")]
        [TestCase(" Visible ", " Visible ")]
        [TestCase("\u200b", "\u200b")]
        public void DuplicateStringKeyWarningsPreserveLiteralKeyIdentity(
            string literalKey,
            string expectedDisplay
        )
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
            string distinctKey = literalKey + " ";
            keysProperty.arraySize = 3;
            valuesProperty.arraySize = 3;
            keysProperty.GetArrayElementAtIndex(0).stringValue = literalKey;
            keysProperty.GetArrayElementAtIndex(1).stringValue = literalKey;
            keysProperty.GetArrayElementAtIndex(2).stringValue = distinctKey;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializableDictionaryPropertyDrawer drawer = new();
            AssignDictionaryFieldInfo(
                drawer,
                typeof(StringDictionaryHost),
                nameof(StringDictionaryHost.dictionary)
            );
            string cacheKey = drawer.GetListKey(dictionaryProperty);
            SerializableDictionaryPropertyDrawer.DuplicateKeyState state =
                drawer.RefreshDuplicateState(cacheKey, keysProperty, typeof(string));

            Assert.IsTrue(state.HasDuplicates);
            StringAssert.Contains(
                $"Duplicate key {expectedDisplay} at entries 1, 2",
                state.SummaryTooltip
            );
            Assert.IsTrue(
                state.TryGetInfo(0, out SerializableDictionaryPropertyDrawer.DuplicateKeyInfo first)
            );
            Assert.IsTrue(
                state.TryGetInfo(
                    1,
                    out SerializableDictionaryPropertyDrawer.DuplicateKeyInfo second
                )
            );
            StringAssert.Contains($"\"{expectedDisplay}\"", first.tooltip);
            Assert.AreEqual(first.tooltip, second.tooltip);
            Assert.IsTrue(first.isPrimary);
            Assert.IsFalse(second.isPrimary);
            Assert.IsFalse(state.TryGetInfo(2, out _));
            Assert.AreEqual(literalKey, keysProperty.GetArrayElementAtIndex(0).stringValue);
            Assert.AreEqual(literalKey, keysProperty.GetArrayElementAtIndex(1).stringValue);
            Assert.AreEqual(distinctKey, keysProperty.GetArrayElementAtIndex(2).stringValue);
            Assert.IsFalse(serializedObject.hasModifiedProperties);
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
            for (int i = 0; i < testKeys.Length; ++i)
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

        /// <summary>
        /// Resets the shared host state between tests to ensure test isolation.
        /// </summary>
        private void ResetHostState()
        {
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

        private sealed class DictionaryTweenDisabledScope : IDisposable
        {
            private readonly bool _originalValue;
            private bool _disposed;

            public DictionaryTweenDisabledScope()
            {
                _originalValue = UnityHelpersSettings.ShouldTweenSerializableDictionaryFoldouts();
                UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(false);
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(_originalValue);
            }
        }

        private sealed class SortedDictionaryTweenDisabledScope : IDisposable
        {
            private readonly bool _originalValue;
            private bool _disposed;

            public SortedDictionaryTweenDisabledScope()
            {
                _originalValue =
                    UnityHelpersSettings.ShouldTweenSerializableSortedDictionaryFoldouts();
                UnityHelpersSettings.SetSerializableSortedDictionaryFoldoutTweenEnabled(false);
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                UnityHelpersSettings.SetSerializableSortedDictionaryFoldoutTweenEnabled(
                    _originalValue
                );
            }
        }
    }
}
