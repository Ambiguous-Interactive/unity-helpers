// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;

    /// <summary>
    /// Covers the collection-valued shapes Unity refuses to serialize. A dictionary whose value
    /// type is itself a collection used to record its keys and no values, which made an asset look
    /// authored while every runtime lookup came back empty. The dictionaries now box those values
    /// into a parallel array Unity accepts; the sets still report the shape as unsupported, so both
    /// behaviors are pinned here.
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

        // The defect this whole file exists for: Unity drops a List<T>[] outright. The plain values
        // array staying null is what forces the boxed array to exist at all, so if this ever starts
        // resolving the boxing can be deleted.
        [Test]
        public void UnityStillDropsThePlainValuesArrayForACollectionValueType()
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
                    + "serializes, the boxed values array is no longer needed."
            );
        }

        [Test]
        public void UnitySerializesTheBoxedValuesArrayForACollectionValueType()
        {
            SerializedProperty boxed = FindArray(
                nameof(NestedCollectionSerializationHost.droppedValues),
                SerializableDictionarySerializedPropertyNames.BoxedValues
            );

            Assert.That(
                boxed,
                Is.Not.Null,
                "The boxed values array is what makes a collection-valued dictionary round-trip."
            );
        }

        // The property that matters to a consumer: the values survive a real Unity serialization
        // cycle. Resolving a SerializedProperty only proves Unity accepted the field.
        [Test]
        public void ACollectionValuedDictionaryRoundTripsThroughUnitySerialization()
        {
            _host.droppedValues["dash"] = new List<float> { 1.5f, 2.5f };
            _host.droppedValues["slam"] = new List<float> { 9f };

            NestedCollectionSerializationHost restored = RoundTrip();

            Assert.That(restored.droppedValues.Count, Is.EqualTo(2));
            Assert.That(restored.droppedValues["dash"], Is.EqualTo(new List<float> { 1.5f, 2.5f }));
            Assert.That(restored.droppedValues["slam"], Is.EqualTo(new List<float> { 9f }));
        }

        [Test]
        public void ACollectionValuedSortedDictionaryRoundTripsThroughUnitySerialization()
        {
            _host.sortedDroppedValues["b"] = new List<float> { 2f };
            _host.sortedDroppedValues["a"] = new List<float> { 1f, 1.5f };

            NestedCollectionSerializationHost restored = RoundTrip();

            Assert.That(restored.sortedDroppedValues.Count, Is.EqualTo(2));
            Assert.That(
                restored.sortedDroppedValues["a"],
                Is.EqualTo(new List<float> { 1f, 1.5f })
            );
            Assert.That(restored.sortedDroppedValues["b"], Is.EqualTo(new List<float> { 2f }));
        }

        // The Inspector path writes the managed values array from its SerializedProperties and then
        // asks for the runtime dictionary to be rebuilt. Rehydrating from the boxed array there
        // would overwrite that fresh write with a copy that is stale until the next serialize, which
        // is how an edit silently reverts. Pinned because nothing else would notice.
        [Test]
        public void EditorAfterDeserializeDoesNotOverwriteAFreshlyWrittenValuesArray()
        {
            _host.droppedValues["dash"] = new List<float> { 1f };
            _host.droppedValues.OnBeforeSerialize();

            // Stand in for the drawer: the managed arrays now hold the edit, the boxed array does not.
            _host.droppedValues._keys = new[] { "dash" };
            _host.droppedValues._values = new[] { new List<float> { 42f } };

            _host.droppedValues.EditorAfterDeserialize();

            Assert.That(
                _host.droppedValues["dash"],
                Is.EqualTo(new List<float> { 42f }),
                "EditorAfterDeserialize must read the managed values array the caller just wrote."
            );
        }

        // An ordinary value type must keep storing its values in the plain array. Unity declares
        // the boxed field on every dictionary -- a serialized field cannot be conditional -- so the
        // guarantee that matters is that it stays EMPTY: the values still live where older package
        // versions look for them, and the addition costs one empty array per dictionary rather than
        // rewriting anyone's data.
        [Test]
        public void ASupportedValueTypeKeepsItsValuesInThePlainArray()
        {
            _host.control["level"] = "forest";
            _host.control.OnBeforeSerialize();
            _serializedObject.Update();

            SerializedProperty values = FindArray(
                nameof(NestedCollectionSerializationHost.control),
                SerializableDictionarySerializedPropertyNames.Values
            );
            SerializedProperty boxed = FindArray(
                nameof(NestedCollectionSerializationHost.control),
                SerializableDictionarySerializedPropertyNames.BoxedValues
            );

            Assert.That(values, Is.Not.Null);
            Assert.That(values.arraySize, Is.EqualTo(1));
            Assert.That(
                boxed == null || boxed.arraySize == 0,
                Is.True,
                "A populated boxed array for an ordinary value type would mean every consumer "
                    + "asset was rewritten into a format older package versions cannot read."
            );
        }

        // The mirror image, and the reason the boxed array exists: for a collection value the plain
        // array is the one that must stay empty, because Unity never writes it.
        [Test]
        public void ACollectionValueTypeKeepsItsValuesInTheBoxedArray()
        {
            _host.droppedValues["dash"] = new List<float> { 1f };
            _host.droppedValues.OnBeforeSerialize();
            _serializedObject.Update();

            SerializedProperty boxed = FindArray(
                nameof(NestedCollectionSerializationHost.droppedValues),
                SerializableDictionarySerializedPropertyNames.BoxedValues
            );

            Assert.That(boxed, Is.Not.Null);
            Assert.That(boxed.arraySize, Is.EqualTo(1));
        }

        [Test]
        public void ASupportedValueTypeStillRoundTrips()
        {
            _host.control["level"] = "forest";

            NestedCollectionSerializationHost restored = RoundTrip();

            Assert.That(restored.control.Count, Is.EqualTo(1));
            Assert.That(restored.control["level"], Is.EqualTo("forest"));
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

        // The documentation offers this form as the escape hatch that needs no per-value-type
        // subclass. It shipped with nothing testing it, and it is the shape most likely to be
        // wrong, because Unity's support for a generic serialized field is version-dependent.
        [Test]
        public void UnitySerializesValuesRoutedThroughTheOpenGenericCacheBox()
        {
            SerializedProperty values = FindArray(
                nameof(NestedCollectionSerializationHost.openGenericCachedValues),
                SerializableDictionarySerializedPropertyNames.Values
            );

            Assert.That(
                values,
                Is.Not.Null,
                "docs/features/serialization/serialization-types.md tells consumers the open "
                    + "generic cache box is enough; if this is null that guidance loses data."
            );
        }

        [Test]
        public void TheOpenGenericCacheBoxRoundTrips()
        {
            _host.openGenericCachedValues["dash"] = new List<float> { 3f, 4f };

            NestedCollectionSerializationHost restored = RoundTrip();

            Assert.That(restored.openGenericCachedValues.Count, Is.EqualTo(1));
            Assert.That(
                restored.openGenericCachedValues["dash"],
                Is.EqualTo(new List<float> { 3f, 4f })
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

        // Reporting an error on a shape that now round-trips is the failure this change had to
        // avoid: the Inspector would refuse to draw a dictionary whose data is fine.
        [Test]
        public void DictionaryDrawerAcceptsACollectionValueType()
        {
            Assert.That(
                SerializableDictionaryPropertyDrawer.HasDroppedValuesArrayForTests(
                    FindCollection(nameof(NestedCollectionSerializationHost.droppedValues))
                ),
                Is.False
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

        // The message must no longer send a list-valued dictionary anywhere, because that shape is
        // now handled; naming a remedy for a case that cannot reach this error is how the previous
        // message ended up recommending a form nothing tested.
        [Test]
        public void DroppedValuesMessageNamesTheFieldAndDoesNotRecommendAListWrapper()
        {
            string message =
                SerializableCollectionSerializationDiagnostics.BuildDroppedDictionaryValuesMessage(
                    "Dropped Values"
                );

            Assert.That(message, Does.Contain("Dropped Values"));
            Assert.That(message, Does.Contain("[Serializable]"));
            Assert.That(message, Does.Not.Contain("SerializableList<T>"));
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

        private NestedCollectionSerializationHost RoundTrip()
        {
            string json = EditorJsonUtility.ToJson(_host, false);
            NestedCollectionSerializationHost restored =
                CreateScriptableObject<NestedCollectionSerializationHost>();
            EditorJsonUtility.FromJsonOverwrite(json, restored);
            return restored;
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
