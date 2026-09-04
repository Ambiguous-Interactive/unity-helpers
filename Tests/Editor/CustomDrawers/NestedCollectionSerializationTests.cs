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

        /// <summary>
        /// The defect this whole file exists for: Unity drops a List&lt;T&gt;[] outright. The plain values
        /// array staying null is what forces the boxed array to exist at all, so if this ever starts
        /// resolving the boxing can be deleted.
        /// </summary>
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

        /// <summary>
        /// The property that matters to a consumer: the values survive a real Unity serialization
        /// cycle. Resolving a SerializedProperty only proves Unity accepted the field.
        /// </summary>
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

        /// <summary>
        /// The Inspector path writes the managed values array from its SerializedProperties and then
        /// asks for the runtime dictionary to be rebuilt. Rehydrating from the boxed array there
        /// would overwrite that fresh write with a copy that is stale until the next serialize, which
        /// is how an edit silently reverts. Pinned because nothing else would notice.
        /// </summary>
        [Test]
        public void EditorAfterDeserializeFromManagedArraysDoesNotOverwriteAFreshWrite()
        {
            _host.droppedValues["dash"] = new List<float> { 1f };
            _host.droppedValues.OnBeforeSerialize();

            // Stand in for the drawer: the managed arrays now hold the edit, the boxed array does not.
            _host.droppedValues._keys = new[] { "dash" };
            _host.droppedValues._values = new[] { new List<float> { 42f } };

            _host.droppedValues.EditorAfterDeserializeFromManagedArrays();

            Assert.That(
                _host.droppedValues["dash"],
                Is.EqualTo(new List<float> { 42f }),
                "EditorAfterDeserializeFromManagedArrays must read the array the caller just wrote."
            );
        }

        /// <summary>
        /// The mirror image, and the reason the two entry points had to be split. Most editor callers
        /// reach EditorAfterDeserialize WITHOUT having written the values array -- for a collection
        /// value Unity only restored the boxed one -- so this path must refill it, or the runtime map
        /// rebuilds from a stale array and the next serialize writes that staleness back over the
        /// good boxed data.
        /// </summary>
        [Test]
        public void EditorAfterDeserializeRefillsTheValuesArrayFromTheBoxedOne()
        {
            _host.droppedValues["dash"] = new List<float> { 7f };
            _host.droppedValues.OnBeforeSerialize();

            // Stand in for a domain reload: Unity restored the boxed array and nothing else.
            _host.droppedValues._values = null;

            _host.droppedValues.EditorAfterDeserialize();

            Assert.That(
                _host.droppedValues["dash"],
                Is.EqualTo(new List<float> { 7f }),
                "EditorAfterDeserialize must refill the values array from the boxed one; without "
                    + "that the dictionary rebuilds empty and the next save writes the emptiness back."
            );
        }

        /// <summary>
        /// Recursive nesting composes with the boxing rather than competing with it: a dictionary
        /// VALUE that is another serializable dictionary is a class, which is the shape Unity has
        /// always accepted, so it must keep using the plain array and gain no box.
        /// </summary>
        [Test]
        public void ADictionaryValuedDictionaryNeedsNoBoxingAndRoundTrips()
        {
            IntFloatDictionary inner = new();
            inner[7] = 1.25f;
            _host.nestedDictionaryValues["inner"] = inner;

            NestedCollectionSerializationHost restored = RoundTrip();

            Assert.That(
                FindArray(
                    nameof(NestedCollectionSerializationHost.nestedDictionaryValues),
                    SerializableDictionarySerializedPropertyNames.Values
                ),
                Is.Not.Null,
                "A dictionary-valued dictionary is a class-valued dictionary; Unity serializes it."
            );
            Assert.That(restored.nestedDictionaryValues.Count, Is.EqualTo(1));
            Assert.That(restored.nestedDictionaryValues["inner"][7], Is.EqualTo(1.25f));
        }

        /// <summary>
        /// Both mechanisms at once: the value is a raw collection (so it is boxed) whose element is
        /// itself a serializable dictionary (so it relies on ordinary class nesting inside the box).
        /// </summary>
        [Test]
        public void AListOfDictionariesValueRoundTrips()
        {
            IntFloatDictionary inner = new();
            inner[3] = 9.5f;
            _host.listOfDictionaryValues["curves"] = new List<IntFloatDictionary> { inner };

            NestedCollectionSerializationHost restored = RoundTrip();

            Assert.That(restored.listOfDictionaryValues.Count, Is.EqualTo(1));
            Assert.That(restored.listOfDictionaryValues["curves"].Count, Is.EqualTo(1));
            Assert.That(restored.listOfDictionaryValues["curves"][0][3], Is.EqualTo(9.5f));
        }

        /// <summary>
        /// How deep the recursion actually goes. Each dictionary costs Unity roughly two nesting
        /// levels (the class, then its arrays), and Unity stops at a fixed depth of its own -- so the
        /// limit is Unity's, not this package's, and no amount of boxing lifts it. Three dictionaries
        /// deep is asserted as supported; if Unity ever tightens the limit, this is where it shows up
        /// rather than in a consumer's save file.
        /// </summary>
        [Test]
        public void ThreeDictionariesDeepRoundTrips()
        {
            IntFloatDictionary innermost = new();
            innermost[5] = 0.5f;
            NestedDictionaryDictionary middle = new();
            middle["mid"] = innermost;
            _host.deeplyNestedValues["outer"] = middle;

            NestedCollectionSerializationHost restored = RoundTrip();

            Assert.That(restored.deeplyNestedValues.Count, Is.EqualTo(1));
            Assert.That(restored.deeplyNestedValues["outer"].Count, Is.EqualTo(1));
            Assert.That(restored.deeplyNestedValues["outer"]["mid"][5], Is.EqualTo(0.5f));
        }

        /// <summary>
        /// A set of dictionaries is the set shape that does work, because the element is a class
        /// rather than a raw collection. Pinned so the set diagnostic is not mistaken for "sets
        /// cannot nest at all".
        /// </summary>
        [Test]
        public void ADictionaryElementedSetRoundTrips()
        {
            IntFloatDictionary inner = new();
            inner[1] = 2f;
            _host.dictionaryItems.Add(inner);

            NestedCollectionSerializationHost restored = RoundTrip();

            Assert.That(
                FindArray(
                    nameof(NestedCollectionSerializationHost.dictionaryItems),
                    SerializableHashSetSerializedPropertyNames.Items
                ),
                Is.Not.Null
            );
            Assert.That(restored.dictionaryItems.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Boxing repairs exactly one level of nesting. Unity refuses List&lt;List&lt;T&gt;&gt; as a class field
        /// just as firmly as as an array element, so the box's own Data field is dropped and boxing
        /// would store nothing. The predicate must decline to box it.
        ///
        /// Asserted on the MANAGED arrays after a real OnBeforeSerialize, because that is the call
        /// that would fill the boxed array if the predicate regressed -- checking a SerializedProperty
        /// on a dictionary nobody populated would pass whether the predicate is right or wrong.
        /// </summary>
        [Test]
        public void AMultiLevelCollectionValueIsNotBoxed()
        {
            _host.nestedListValues["curves"] = new List<List<float>> { new() { 1f } };

            _host.nestedListValues.OnBeforeSerialize();

            Assert.That(
                _host.nestedListValues._values,
                Is.Not.Null.And.Length.EqualTo(1),
                "The mirror source must exist, or this asserts nothing about the mirror."
            );
            Assert.That(
                _host.nestedListValues._boxedValues,
                Is.Null,
                "Boxing a List<List<float>> produces a box whose Data field Unity also refuses, so "
                    + "the predicate must decline to box it rather than fill an array that stores "
                    + "nothing."
            );
            Assert.That(
                FindArray(
                    nameof(NestedCollectionSerializationHost.nestedListValues),
                    SerializableDictionarySerializedPropertyNames.Values
                ),
                Is.Null,
                "Unity refuses List<List<float>>[] as it always has -- which is why nothing this "
                    + "package does makes the shape work. The Inspector reports it; see "
                    + "DictionaryDrawerReportsAValueTypeBoxingCannotRepair."
            );
        }

        /// <summary>
        /// An ordinary value type must keep storing its values in the plain array. Unity declares
        /// the boxed field on every dictionary -- a serialized field cannot be conditional -- so the
        /// guarantee that matters is that it stays EMPTY: the values still live where older package
        /// versions look for them, and the addition costs one empty array per dictionary rather than
        /// rewriting anyone's data.
        /// </summary>
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

        /// <summary>
        /// The mirror image, and the reason the boxed array exists: for a collection value the plain
        /// array is the one that must stay empty, because Unity never writes it.
        /// </summary>
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

        /// <summary>
        /// The documentation offers this form as the escape hatch that needs no per-value-type
        /// subclass. It shipped with nothing testing it, and it is the shape most likely to be
        /// wrong, because Unity's support for a generic serialized field is version-dependent.
        /// </summary>
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

        /// <summary>
        /// The whole point of shipping SerializableList&lt;T&gt;: the fix for a collection-valued
        /// dictionary is a type change, with no cache subclass per value type.
        /// </summary>
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

        /// <summary>
        /// Reporting an error on a shape that now round-trips is the failure this change had to
        /// avoid: the Inspector would refuse to draw a dictionary whose data is fine.
        /// </summary>
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

        /// <summary>
        /// #357. The boxed array is DECLARED on every dictionary, so for a shape boxing cannot repair
        /// the drawer resolves a values property that is merely empty and used to report nothing --
        /// losing the only explanation a consumer gets for a column that persists no data. The
        /// runtime computes the answer per closed generic, so the drawer asks it.
        /// </summary>
        [Test]
        public void DictionaryDrawerReportsAValueTypeBoxingCannotRepair()
        {
            Assert.That(
                SerializableDictionaryPropertyDrawer.HasDroppedValuesArrayForTests(
                    FindCollection(nameof(NestedCollectionSerializationHost.nestedListValues))
                ),
                Is.True,
                "A List<List<float>> value serializes nowhere -- not in the plain array, not in a "
                    + "box -- so the Inspector must say so instead of drawing an empty column."
            );
        }

        [Test]
        public void SortedDictionaryDrawerReportsAValueTypeBoxingCannotRepair()
        {
            Assert.That(
                SerializableDictionaryPropertyDrawer.HasDroppedValuesArrayForTests(
                    FindCollection(nameof(NestedCollectionSerializationHost.sortedNestedListValues))
                ),
                Is.True,
                "The sorted base has its own boxing predicate and no shared hierarchy with the "
                    + "unsorted one, so it needs its own proof that the report reaches it."
            );
        }

        /// <summary>
        /// The other half of the #357 predicate, and the one #348 was filed about: an EMPTY
        /// dictionary of a supported collection value must stay silent. Both cases present the
        /// drawer with an empty boxed array, which is why the runtime has to be asked at all.
        /// </summary>
        [Test]
        public void DictionaryDrawerStaysSilentOnAnEmptyBoxedCollectionValue()
        {
            Assert.That(
                SerializableDictionaryPropertyDrawer.HasDroppedValuesArrayForTests(
                    FindCollection(nameof(NestedCollectionSerializationHost.sortedDroppedValues))
                ),
                Is.False,
                "An empty dictionary whose values DO round-trip must not be reported as broken."
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

        /// <summary>
        /// An unresolvable property is a different problem, and reporting it as a serialization
        /// failure would put the error box on fields that are merely not sets.
        /// </summary>
        [Test]
        public void SetDrawerIgnoresAPropertyThatIsNotASet()
        {
            Assert.That(
                SerializableSetPropertyDrawer.HasDroppedItemsArrayForTests(false, null),
                Is.False
            );
        }

        /// <summary>
        /// The message must no longer send a list-valued dictionary anywhere, because that shape is
        /// now handled; naming a remedy for a case that cannot reach this error is how the previous
        /// message ended up recommending a form nothing tested.
        /// </summary>
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

        /// <summary>
        /// #354. Naming SerializableList&lt;T&gt; as the fix is only half true -- it serializes, but it
        /// keeps the reference equality that makes a set of collections the wrong model in the first
        /// place. The message has to point at the element type and say why, or it sends the reader
        /// to a wrapper that produces the same silent failure one layer down.
        /// </summary>
        [Test]
        public void DroppedItemsMessagePointsAtTheElementTypeAndExplainsIdentity()
        {
            string message =
                SerializableCollectionSerializationDiagnostics.BuildDroppedSetItemsMessage(
                    "Dropped Items"
                );

            Assert.That(message, Does.Contain("Dropped Items"));
            Assert.That(message, Does.Contain("element type is the thing to reconsider"));
            Assert.That(message, Does.Contain("reference"));
            Assert.That(message, Does.Contain("identity semantics"));
        }

        /// <summary>
        /// #354. The consequence the guidance is built on, pinned so a later session cannot "fix"
        /// sets by giving them the dictionaries' boxing: the wrapper serializes, and the set is
        /// STILL useless, because every restored element is a new instance and SerializableList&lt;T&gt;
        /// compares by reference. Boxing would turn a shape that fails loudly at author time into
        /// one that fails silently at runtime.
        /// </summary>
        [Test]
        public void ARestoredSetOfListsDoesNotContainAnEqualContentList()
        {
            _host.wrappedItems.Add(new SerializableList<float> { 1f, 2f });

            NestedCollectionSerializationHost restored = RoundTrip();

            Assert.That(
                restored.wrappedItems.Count,
                Is.EqualTo(1),
                "The wrapper is what makes the items array serialize at all; if this is 0 the test "
                    + "below proves nothing about equality."
            );
            Assert.That(
                restored.wrappedItems.Contains(new SerializableList<float> { 1f, 2f }),
                Is.False,
                "SerializableList<T> declares no value equality, so a restored set answers "
                    + "Contains with reference identity. This is why sets of collections are "
                    + "reported rather than repaired -- see #354 before adding boxing here."
            );
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
