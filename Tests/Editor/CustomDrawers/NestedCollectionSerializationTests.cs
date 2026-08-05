// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using NUnit.Framework;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;

    /// <summary>
    /// Covers the collection-valued shapes Unity refuses to serialize. A dictionary whose value
    /// type is itself a collection records its keys and no values, which makes an asset look
    /// authored while every runtime lookup comes back empty. The drawers are the only component
    /// that can see it, because a dropped field is exactly what SerializedProperty reports as null.
    /// </summary>
    [TestFixture]
    public sealed class NestedCollectionSerializationTests : BatchedEditorTestBase
    {
        private NestedCollectionSerializationHost _host;
        private SerializedObject _serializedObject;

        [SetUp]
        public override void BaseSetUp()
        {
            _host = CreateScriptableObject<NestedCollectionSerializationHost>();
            _serializedObject = new SerializedObject(_host);
            base.BaseSetUp();
        }

        [TearDown]
        public override void TearDown()
        {
            _serializedObject?.Dispose();
            _serializedObject = null;
            _host = null;
            base.TearDown();
        }

        [Test]
        public void UnitySerializesValuesForASupportedValueType()
        {
            SerializedProperty values = FindArray(
                nameof(NestedCollectionSerializationHost.control),
                SerializableDictionarySerializedPropertyNames.Values
            );

            Assert.That(values, Is.Not.Null, "Unity must serialize a string-valued dictionary.");
        }

        [Test]
        public void UnityDropsValuesForACollectionValueType()
        {
            SerializedProperty keys = FindArray(
                nameof(NestedCollectionSerializationHost.droppedValues),
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty values = FindArray(
                nameof(NestedCollectionSerializationHost.droppedValues),
                SerializableDictionarySerializedPropertyNames.Values
            );

            Assert.That(keys, Is.Not.Null, "The keys array is a TKey[] and must still serialize.");
            Assert.That(
                values,
                Is.Null,
                "A List<float>[] values array is expected to be dropped by Unity; if this now "
                    + "serializes, the drawer's dropped-array reporting is no longer needed."
            );
        }

        [Test]
        public void UnitySerializesValuesRoutedThroughACacheBox()
        {
            SerializedProperty values = FindArray(
                nameof(NestedCollectionSerializationHost.cachedValues),
                SerializableDictionarySerializedPropertyNames.Values
            );

            Assert.That(
                values,
                Is.Not.Null,
                "The documented cache form is the supported way to serialize a collection value."
            );
        }

        // The whole point of shipping SerializableList<T>: the fix for a collection-valued
        // dictionary is a type change, with no cache subclass per value type.
        [Test]
        public void UnitySerializesValuesWrappedInASerializableList()
        {
            SerializedProperty values = FindArray(
                nameof(NestedCollectionSerializationHost.wrappedValues),
                SerializableDictionarySerializedPropertyNames.Values
            );

            Assert.That(
                values,
                Is.Not.Null,
                "SerializableList<T> exists so that a list-valued dictionary serializes; if this "
                    + "is null the wrapper does not add the indirection Unity needs."
            );
        }

        [Test]
        public void UnitySerializesItemsWrappedInASerializableList()
        {
            SerializedProperty items = FindArray(
                nameof(NestedCollectionSerializationHost.wrappedItems),
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.That(items, Is.Not.Null);
        }

        [Test]
        public void DictionaryDrawerAcceptsTheListWrapper()
        {
            Assert.That(
                SerializableDictionaryPropertyDrawer.HasDroppedValuesArrayForTests(
                    FindCollection(nameof(NestedCollectionSerializationHost.wrappedValues))
                ),
                Is.False
            );
        }

        [Test]
        public void DictionaryDrawerReportsADroppedValuesArray()
        {
            Assert.That(
                SerializableDictionaryPropertyDrawer.HasDroppedValuesArrayForTests(
                    FindCollection(nameof(NestedCollectionSerializationHost.droppedValues))
                ),
                Is.True
            );
        }

        [Test]
        public void DictionaryDrawerAcceptsASupportedValueType()
        {
            Assert.That(
                SerializableDictionaryPropertyDrawer.HasDroppedValuesArrayForTests(
                    FindCollection(nameof(NestedCollectionSerializationHost.control))
                ),
                Is.False
            );
        }

        [Test]
        public void DictionaryDrawerAcceptsTheCacheForm()
        {
            Assert.That(
                SerializableDictionaryPropertyDrawer.HasDroppedValuesArrayForTests(
                    FindCollection(nameof(NestedCollectionSerializationHost.cachedValues))
                ),
                Is.False
            );
        }

        [Test]
        public void UnityDropsItemsForACollectionElementType()
        {
            SerializedProperty items = FindArray(
                nameof(NestedCollectionSerializationHost.droppedItems),
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.That(items, Is.Null);
        }

        [Test]
        public void UnitySerializesItemsForASupportedElementType()
        {
            SerializedProperty items = FindArray(
                nameof(NestedCollectionSerializationHost.controlSet),
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.That(items, Is.Not.Null);
        }

        [Test]
        public void SetDrawerReportsADroppedItemsArray()
        {
            SerializedProperty items = FindArray(
                nameof(NestedCollectionSerializationHost.droppedItems),
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.That(
                SerializableSetPropertyDrawer.HasDroppedItemsArrayForTests(true, items),
                Is.True
            );
        }

        [Test]
        public void SetDrawerAcceptsASupportedElementType()
        {
            SerializedProperty items = FindArray(
                nameof(NestedCollectionSerializationHost.controlSet),
                SerializableHashSetSerializedPropertyNames.Items
            );

            Assert.That(
                SerializableSetPropertyDrawer.HasDroppedItemsArrayForTests(true, items),
                Is.False
            );
        }

        // An unresolvable property is a different problem, and reporting it as a serialization
        // failure would put the error box on fields that are merely not sets.
        [Test]
        public void SetDrawerIgnoresAPropertyThatIsNotASet()
        {
            Assert.That(
                SerializableSetPropertyDrawer.HasDroppedItemsArrayForTests(false, null),
                Is.False
            );
        }

        [Test]
        public void DroppedValuesMessageNamesTheFieldAndTheSupportedForm()
        {
            string message =
                SerializableCollectionSerializationDiagnostics.BuildDroppedDictionaryValuesMessage(
                    "Dropped Values"
                );

            Assert.That(message, Does.Contain("Dropped Values"));
            Assert.That(message, Does.Contain("SerializableList<T>"));
            Assert.That(message, Does.Contain("SerializableDictionary.Cache"));
        }

        [Test]
        public void DroppedItemsMessageNamesTheField()
        {
            string message =
                SerializableCollectionSerializationDiagnostics.BuildDroppedSetItemsMessage(
                    "Dropped Items"
                );

            Assert.That(message, Does.Contain("Dropped Items"));
            Assert.That(message, Does.Contain("SerializableList<T>"));
        }

        private SerializedProperty FindCollection(string fieldName)
        {
            return _serializedObject.FindProperty(fieldName);
        }

        private SerializedProperty FindArray(string fieldName, string arrayName)
        {
            return FindCollection(fieldName).FindPropertyRelative(arrayName);
        }
    }
}
