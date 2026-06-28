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
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializableDictionaryTests : CommonTestBase
    {
        [Test]
        public void AddRetrieveAndEnumerateValues()
        {
            SerializableDictionary<string, int> dictionary = new() { { "alpha", 1 } };
            dictionary["beta"] = 2;

            Assert.AreEqual(2, dictionary.Count);
            Assert.IsTrue(dictionary.ContainsKey("alpha"));
            Assert.IsTrue(dictionary.ContainsKey("beta"));

            Assert.AreEqual(1, dictionary["alpha"]);
            Assert.AreEqual(2, dictionary["beta"]);

            Assert.IsTrue(dictionary.TryGetValue("alpha", out int retrievedValue));
            Assert.AreEqual(1, retrievedValue);

            int keyedCount = 0;
            foreach (KeyValuePair<string, int> pair in dictionary)
            {
                keyedCount++;
                Assert.IsTrue(dictionary.ContainsKey(pair.Key));
                Assert.AreEqual(dictionary[pair.Key], pair.Value);
            }

            Assert.AreEqual(dictionary.Count, keyedCount);
        }

        [Test]
        public void ToDictionaryReturnsIndependentCopy()
        {
            SerializableDictionary<string, int> dictionary = new()
            {
                { "alpha", 1 },
                { "beta", 2 },
            };

            Dictionary<string, int> copy = dictionary.ToDictionary();

            Assert.AreEqual(dictionary.Count, copy.Count);
            Assert.AreEqual(1, copy["alpha"]);
            Assert.AreEqual(2, copy["beta"]);

            copy["gamma"] = 3;
            Assert.IsFalse(dictionary.ContainsKey("gamma"));

            dictionary["alpha"] = 10;
            Assert.AreEqual(1, copy["alpha"]);
        }

        [Test]
        public void CopyFromReplacesExistingContent()
        {
            SerializableDictionary<int, string> dictionary = new() { { 1, "one" } };

            Dictionary<int, string> source = new() { [2] = "two", [3] = "three" };

            dictionary.CopyFrom(source);

            Assert.AreEqual(2, dictionary.Count);
            Assert.IsFalse(dictionary.ContainsKey(1));
            Assert.AreEqual("two", dictionary[2]);
            Assert.AreEqual("three", dictionary[3]);
        }

        [Test]
        public void TryAddWhenKeyExistsDoesNotClearSerializedArrays()
        {
            SerializableDictionary<int, string> dictionary = new() { { 1, "one" } };

            dictionary.OnBeforeSerialize();

            int[] serializedKeys = dictionary.SerializedKeys;
            string[] serializedValues = dictionary.SerializedValues;

            Assert.IsTrue(serializedKeys != null, "Serialized keys should be produced on demand.");
            Assert.IsTrue(
                serializedValues != null,
                "Serialized values should be produced on demand."
            );

            bool added = dictionary.TryAdd(1, "duplicate");

            Assert.IsFalse(added);
            Assert.AreSame(
                serializedKeys,
                dictionary.SerializedKeys,
                "Failed TryAdd must not clear the cached keys."
            );
            Assert.AreSame(
                serializedValues,
                dictionary.SerializedValues,
                "Failed TryAdd must not clear the cached values."
            );
            Assert.AreEqual("one", dictionary[1]);
        }

        [Test]
        public void IndexerUpdateMarksSerializationDirtyButPreservesArraysForOrder()
        {
            SerializableDictionary<int, string> dictionary = new() { { 1, "one" }, { 2, "two" } };

            dictionary.OnBeforeSerialize();

            Assert.IsTrue(
                dictionary.SerializedKeys != null,
                "Keys cache should exist before update."
            );
            Assert.IsTrue(
                dictionary.SerializedValues != null,
                "Values cache should exist before update."
            );
            Assert.IsTrue(
                dictionary.PreserveSerializedEntries,
                "Preserve flag should be true after OnBeforeSerialize."
            );

            dictionary[2] = "second";

            // Arrays are preserved for order maintenance, but preserve flag is cleared
            Assert.IsTrue(
                dictionary.SerializedKeys != null,
                "Indexer mutations preserve arrays for order maintenance."
            );
            Assert.IsTrue(
                dictionary.SerializedValues != null,
                "Indexer mutations preserve arrays for order maintenance."
            );
            Assert.IsFalse(
                dictionary.PreserveSerializedEntries,
                "Indexer mutations must clear preserve flag."
            );
            Assert.AreEqual("second", dictionary[2]);

            // After OnBeforeSerialize, the new value should be reflected in the arrays
            dictionary.OnBeforeSerialize();
            Assert.AreEqual("second", dictionary.SerializedValues[1]);
        }

        [Test]
        public void RemoveOutputsPreviousValueAndMarksSerializationDirty()
        {
            SerializableDictionary<int, string> dictionary = new() { { 1, "one" }, { 2, "two" } };

            dictionary.OnBeforeSerialize();

            Assert.IsTrue(dictionary.SerializedKeys != null);
            Assert.IsTrue(dictionary.SerializedValues != null);
            Assert.IsTrue(dictionary.PreserveSerializedEntries);

            bool removed = dictionary.Remove(2, out string removedValue);

            Assert.IsTrue(removed);
            Assert.AreEqual("two", removedValue);
            // Arrays are preserved for order maintenance, but preserve flag is cleared
            Assert.IsTrue(
                dictionary.SerializedKeys != null,
                "Removal preserves arrays for order maintenance."
            );
            Assert.IsTrue(
                dictionary.SerializedValues != null,
                "Removal preserves arrays for order maintenance."
            );
            Assert.IsFalse(
                dictionary.PreserveSerializedEntries,
                "Removal should clear preserve flag."
            );
            Assert.IsFalse(dictionary.ContainsKey(2));

            // After OnBeforeSerialize, removed entry should be gone
            dictionary.OnBeforeSerialize();
            Assert.AreEqual(1, dictionary.SerializedKeys.Length);
            Assert.AreEqual(1, dictionary.SerializedKeys[0]);
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void ProtoSerializationPreservesSerializationArraysWhenNoDuplicatesExist()
        {
            SerializableDictionary<int, string> dictionary = new() { { 1, "one" }, { 3, "three" } };

            dictionary.OnBeforeSerialize();

            int[] serializedKeysBefore = dictionary.SerializedKeys;
            string[] serializedValuesBefore = dictionary.SerializedValues;
            Assert.IsTrue(serializedKeysBefore != null);
            Assert.IsTrue(serializedValuesBefore != null);

            byte[] payload = Serializer.ProtoSerialize(dictionary);

            int[] serializedKeysAfter = dictionary.SerializedKeys;
            string[] serializedValuesAfter = dictionary.SerializedValues;

            Assert.AreSame(
                serializedKeysBefore,
                serializedKeysAfter,
                "Proto serialization should not replace the serialized keys array."
            );
            Assert.AreSame(
                serializedValuesBefore,
                serializedValuesAfter,
                "Proto serialization should not replace the serialized values array."
            );
            Assert.AreEqual(2, serializedKeysAfter.Length);
            Assert.AreEqual(2, serializedValuesAfter.Length);
            Assert.AreEqual("one", dictionary[1]);
            Assert.AreEqual("three", dictionary[3]);

            SerializableDictionary<int, string> roundTripped = Serializer.ProtoDeserialize<
                SerializableDictionary<int, string>
            >(payload);

            Assert.AreEqual(2, roundTripped.Count);
            Assert.AreEqual("one", roundTripped[1]);
            Assert.AreEqual("three", roundTripped[3]);
        }

        [Test]
        public void SerializationCallbacksRoundTripEntries()
        {
            SerializableDictionary<int, string> original = new()
            {
                { 10, "ten" },
                { 20, "twenty" },
            };

            original.OnBeforeSerialize();

            int[] serializedKeys = original.SerializedKeys;
            string[] serializedValues = original.SerializedValues;

            Assert.IsTrue(serializedKeys != null);
            Assert.IsTrue(serializedValues != null);
            Assert.AreEqual(original.Count, serializedKeys.Length);
            Assert.AreEqual(serializedKeys.Length, serializedValues.Length);

            int[] keysCopy = new int[serializedKeys.Length];
            Array.Copy(serializedKeys, keysCopy, serializedKeys.Length);

            string[] valuesCopy = new string[serializedValues.Length];
            Array.Copy(serializedValues, valuesCopy, serializedValues.Length);

            SerializableDictionary<int, string> roundTripped = new()
            {
                _keys = keysCopy,
                _values = valuesCopy,
            };

            roundTripped.OnAfterDeserialize();

            Assert.AreEqual(original.Count, roundTripped.Count);
            Assert.AreEqual("ten", roundTripped[10]);
            Assert.AreEqual("twenty", roundTripped[20]);
            Assert.IsTrue(roundTripped.TryGetValue(10, out string tenValue));
            Assert.AreEqual("ten", tenValue);

            roundTripped.Clear();
            Assert.AreEqual(0, roundTripped.Count);
        }

        [Test]
        public void CacheBackedDictionarySerializesValues()
        {
            SerializableDictionary<int, int, IntCache> original = new() { { 2, 200 }, { 5, 500 } };

            original.OnBeforeSerialize();

            int[] serializedKeys = original.SerializedKeys;
            IntCache[] serializedValues = original.SerializedValues;

            Assert.IsTrue(serializedKeys != null);
            Assert.IsTrue(serializedValues != null);
            Assert.AreEqual(original.Count, serializedKeys.Length);
            Assert.AreEqual(serializedKeys.Length, serializedValues.Length);

            SerializableDictionary<int, int, IntCache> roundTripped = new();

            int[] keysCopy = new int[serializedKeys.Length];
            Array.Copy(serializedKeys, keysCopy, serializedKeys.Length);

            IntCache[] valuesCopy = new IntCache[serializedValues.Length];
            for (int index = 0; index < serializedValues.Length; index++)
            {
                IntCache cache = new() { Data = serializedValues[index].Data };
                valuesCopy[index] = cache;
            }

            roundTripped._keys = keysCopy;
            roundTripped._values = valuesCopy;

            roundTripped.OnAfterDeserialize();

            Assert.AreEqual(original.Count, roundTripped.Count);
            Assert.AreEqual(200, roundTripped[2]);
            Assert.AreEqual(500, roundTripped[5]);
        }

        [Test]
        public void EnumeratorIsValueType()
        {
            SerializableDictionary<int, string> dictionary = new() { { 1, "one" } };
            using SerializableDictionaryBase<int, string, string>.Enumerator enumerator =
                dictionary.GetEnumerator();

            Assert.IsTrue(
                typeof(SerializableDictionaryBase<int, string, string>.Enumerator).IsValueType
            );
            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(new KeyValuePair<int, string>(1, "one"), enumerator.Current);
        }

        [Test]
        public void DuplicateKeysRemainSerializedForInspector()
        {
            SerializableDictionary<int, string> dictionary = new();

            int[] serializedKeys = { 1, 1 };
            string[] serializedValues = { "first", "second" };
            dictionary._keys = serializedKeys;
            dictionary._values = serializedValues;

            dictionary.OnAfterDeserialize();

            Assert.AreEqual(1, dictionary.Count);
            Assert.AreEqual("second", dictionary[1]);

            int[] storedKeys = dictionary.SerializedKeys;
            string[] storedValues = dictionary.SerializedValues;
            bool preserveFlag = dictionary.PreserveSerializedEntries;

            Assert.IsTrue(storedKeys != null, "Serialized keys were unexpectedly cleared.");
            Assert.IsTrue(storedValues != null, "Serialized values were unexpectedly cleared.");
            Assert.AreEqual(2, storedKeys.Length);
            Assert.AreEqual(2, storedValues.Length);
            Assert.IsTrue(preserveFlag, "Preserve flag should remain true while duplicates exist.");

            dictionary.OnBeforeSerialize();

            int[] roundTripKeys = dictionary.SerializedKeys;
            string[] roundTripValues = dictionary.SerializedValues;
            bool roundTripPreserve = dictionary.PreserveSerializedEntries;

            Assert.IsTrue(roundTripKeys != null, "Round-trip keys should stay populated.");
            Assert.IsTrue(roundTripValues != null, "Round-trip values should stay populated.");
            Assert.AreEqual(2, roundTripKeys.Length);
            Assert.AreEqual(2, roundTripValues.Length);
            Assert.AreEqual("first", roundTripValues[0]);
            Assert.AreEqual("second", roundTripValues[1]);
            Assert.IsTrue(
                roundTripPreserve,
                "Preserve flag should remain true after serialization skip."
            );
        }

        [Test]
        public void NullKeysAreSkippedDuringDeserialization()
        {
            SerializableDictionary<string, string> dictionary = new();

            string[] serializedKeys = { null, "valid" };
            string[] serializedValues = { "ignored", "retained" };
            dictionary._keys = serializedKeys;
            dictionary._values = serializedValues;

            ExpectError(
                LogType.Warning,
                System.Text.RegularExpressions.Regex.Escape(
                    "SerializableDictionary<System.String, System.String> skipped serialized entry at index 0 because the key reference was null."
                )
            );

            dictionary.OnAfterDeserialize();

            Assert.AreEqual(1, dictionary.Count);
            Assert.IsTrue(dictionary.ContainsKey("valid"));
            Assert.AreEqual("retained", dictionary["valid"]);

            string[] storedKeys = dictionary._keys;
            string[] storedValues = dictionary._values;
            bool preserveFlag = dictionary.PreserveSerializedEntries;

            Assert.IsTrue(
                storedKeys != null,
                "Serialized keys should be preserved when null keys exist."
            );
            CollectionAssert.AreEqual(serializedKeys, storedKeys);
            if (storedValues != null)
            {
                CollectionAssert.AreEqual(serializedValues, storedValues);
            }
            Assert.IsTrue(preserveFlag, "Null keys should force serialized cache preservation.");

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void NullValuesArePreservedDuringDeserialization()
        {
            SerializableDictionary<string, string> dictionary = new();

            string[] serializedKeys = { "skip", "keep" };
            string[] serializedValues = { null, "retained" };
            dictionary._keys = serializedKeys;
            dictionary._values = serializedValues;

            dictionary.OnAfterDeserialize();

            Assert.AreEqual(2, dictionary.Count);
            Assert.IsTrue(dictionary.ContainsKey("keep"));
            Assert.AreEqual("retained", dictionary["keep"]);
            Assert.IsTrue(dictionary.ContainsKey("skip"));
            Assert.IsTrue(dictionary["skip"] == null);

            string[] storedKeys = dictionary._keys;
            string[] storedValues = dictionary._values;
            bool preserveFlag = dictionary.PreserveSerializedEntries;

            // Arrays are now always preserved after deserialization to maintain user-defined order
            Assert.IsTrue(
                storedKeys != null,
                "Serialized keys should be preserved to maintain user-defined order."
            );
            Assert.IsTrue(
                storedValues != null,
                "Serialized values should be preserved to maintain user-defined order."
            );
            Assert.IsTrue(preserveFlag, "Preserve flag should be true after deserialization.");

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void DuplicateSerializedKeysPreserveInspectorCache()
        {
            SerializableDictionary<string, string> dictionary = new();

            string[] serializedKeys = { "dup", "dup" };
            string[] serializedValues = { "first", "second" };
            dictionary._keys = serializedKeys;
            dictionary._values = serializedValues;

            dictionary.OnAfterDeserialize();

            Assert.AreEqual(1, dictionary.Count);
            Assert.AreEqual("second", dictionary["dup"]);

            Assert.AreSame(serializedKeys, dictionary.SerializedKeys);
            Assert.AreSame(serializedValues, dictionary.SerializedValues);
            CollectionAssert.AreEqual(new[] { "dup", "dup" }, dictionary.SerializedKeys);
            CollectionAssert.AreEqual(new[] { "first", "second" }, dictionary.SerializedValues);
            Assert.IsTrue(
                dictionary.PreserveSerializedEntries,
                "Duplicate keys should preserve serialized arrays for inspector review."
            );
        }

        [Test]
        public void UnityObjectKeysWithNullEntriesPreserveSerializedArrays()
        {
            SerializableDictionary<DummyAsset, string> dictionary = new();

            DummyAsset valid = Track(ScriptableObject.CreateInstance<DummyAsset>());
            DummyAsset[] serializedKeys = { null, valid };
            string[] serializedValues = { "omit", "keep" };
            dictionary._keys = serializedKeys;
            dictionary._values = serializedValues;

            string expectedMessage =
                $"SerializableDictionary<{typeof(DummyAsset).FullName}, {typeof(string).FullName}> skipped serialized entry at index 0 because the key reference was null.";
            ExpectError(
                LogType.Warning,
                System.Text.RegularExpressions.Regex.Escape(expectedMessage)
            );

            dictionary.OnAfterDeserialize();

            Assert.AreEqual(1, dictionary.Count);
            Assert.IsTrue(dictionary.ContainsKey(valid));
            Assert.AreEqual("keep", dictionary[valid]);
            Assert.AreSame(serializedKeys, dictionary.SerializedKeys);
            Assert.AreSame(serializedValues, dictionary.SerializedValues);
            Assert.IsTrue(dictionary.PreserveSerializedEntries);
        }

        [Test]
        public void ComparerCollisionsPreserveSerializedCache()
        {
            SerializableDictionary<CaseInsensitiveDictionaryKey, string> dictionary = new();

            CaseInsensitiveDictionaryKey loud = new("ALPHA");
            CaseInsensitiveDictionaryKey quiet = new("alpha");
            dictionary._keys = new[] { loud, quiet };
            dictionary._values = new[] { "upper", "lower" };

            dictionary.OnAfterDeserialize();

            Assert.AreEqual(1, dictionary.Count);
            Assert.AreEqual("lower", dictionary[quiet]);

            CaseInsensitiveDictionaryKey[] cachedKeys = dictionary.SerializedKeys;
            string[] cachedValues = dictionary.SerializedValues;

            Assert.IsTrue(cachedKeys != null);
            Assert.IsTrue(cachedValues != null);
            Assert.AreSame(loud, cachedKeys[0]);
            Assert.AreSame(quiet, cachedKeys[1]);
            CollectionAssert.AreEqual(new[] { "upper", "lower" }, cachedValues);
            Assert.IsTrue(dictionary.PreserveSerializedEntries);
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void JsonRoundTripPreservesArraysAndOrderAfterDeserialization()
        {
            SerializableDictionary<int, string> original = new()
            {
                { 3, "three" },
                { 1, "one" },
                { 2, "two" },
            };

            string json = Serializer.JsonStringify(original);
            SerializableDictionary<int, string> roundTrip = Serializer.JsonDeserialize<
                SerializableDictionary<int, string>
            >(json);

            Assert.AreEqual(original.Count, roundTrip.Count);
            // After JSON deserialization, arrays are preserved to maintain user-defined order
            Assert.IsTrue(
                roundTrip.SerializedKeys != null,
                "Serialized keys should be preserved after JSON deserialization."
            );
            Assert.IsTrue(
                roundTrip.SerializedValues != null,
                "Serialized values should be preserved after JSON deserialization."
            );
            Assert.IsTrue(
                roundTrip.PreserveSerializedEntries,
                "Preserve flag should be true after JSON deserialization."
            );

            roundTrip.OnBeforeSerialize();

            int[] rebuiltKeys = roundTrip.SerializedKeys;
            string[] rebuiltValues = roundTrip.SerializedValues;

            Assert.IsTrue(rebuiltKeys != null);
            Assert.IsTrue(rebuiltValues != null);
            Dictionary<int, string> snapshot = new();
            for (int index = 0; index < rebuiltKeys.Length; index++)
            {
                snapshot[rebuiltKeys[index]] = rebuiltValues[index];
            }

            foreach (KeyValuePair<int, string> pair in original)
            {
                Assert.IsTrue(snapshot.ContainsKey(pair.Key));
                Assert.AreEqual(pair.Value, snapshot[pair.Key]);
            }
        }

        [Test]
        public void MismatchedSerializedArraysAreDiscarded()
        {
            SerializableDictionary<int, string> dictionary = new()
            {
                _keys = new[] { 1, 2 },
                _values = new[] { "one" },
            };

            dictionary.OnAfterDeserialize();

            Assert.AreEqual(0, dictionary.Count);
            Assert.IsTrue(dictionary.SerializedKeys == null);
            Assert.IsTrue(dictionary.SerializedValues == null);
            Assert.IsFalse(dictionary.PreserveSerializedEntries);
        }

        [Test]
        public void EditorAfterDeserializeSuppressesWarnings()
        {
            SerializableDictionary<string, string> dictionary = new();

            string[] keys = { null, "valid" };
            string[] values = { "ignored", "retained" };
            dictionary._keys = keys;
            dictionary._values = values;

            SerializableDictionaryBase editorSync = dictionary;
            editorSync.EditorAfterDeserialize();

            Assert.AreEqual(1, dictionary.Count);
            Assert.IsTrue(dictionary.ContainsKey("valid"));
            Assert.AreEqual("retained", dictionary["valid"]);

            string[] storedKeys = dictionary.SerializedKeys;
            string[] storedValues = dictionary.SerializedValues;
            CollectionAssert.AreEqual(keys, storedKeys);
            CollectionAssert.AreEqual(values, storedValues);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void OnBeforeSerializeSkipsRebuildWhenPreservingSerializedEntries()
        {
            SerializableDictionary<string, string> dictionary = new();
            string[] serializedKeys = { "dup", "dup" };
            string[] serializedValues = { "first", "second" };
            dictionary._keys = serializedKeys;
            dictionary._values = serializedValues;

            dictionary.OnAfterDeserialize();
            Assert.IsTrue(dictionary.PreserveSerializedEntries);

            dictionary.OnBeforeSerialize();

            Assert.AreSame(serializedKeys, dictionary.SerializedKeys);
            Assert.AreSame(serializedValues, dictionary.SerializedValues);
            Assert.IsTrue(dictionary.PreserveSerializedEntries);
        }

        [Test]
        public void DictionaryMutationsClearPreserveFlagButKeepArraysForOrder()
        {
            SerializableDictionary<int, string> dictionary = new();

            int[] serializedKeys = { 3, 3 };
            string[] serializedValues = { "old", "new" };
            dictionary._keys = serializedKeys;
            dictionary._values = serializedValues;

            dictionary.OnAfterDeserialize();
            Assert.IsTrue(
                dictionary.PreserveSerializedEntries,
                "PreserveSerializedEntries should be true after OnAfterDeserialize."
            );
            Assert.IsTrue(
                dictionary.HasDuplicatesOrNulls,
                "HasDuplicatesOrNulls should be true when duplicate keys exist."
            );
            Assert.AreEqual(
                1,
                dictionary.Count,
                "Dictionary should have only 1 unique key (last-wins for duplicates)."
            );

            dictionary.Add(4, "fresh");

            bool preserveAfterAdd = dictionary.PreserveSerializedEntries;
            bool hasDuplicatesAfterAdd = dictionary.HasDuplicatesOrNulls;
            int[] storedKeysAfterAdd = dictionary.SerializedKeys;
            string[] storedValuesAfterAdd = dictionary.SerializedValues;

            Assert.IsFalse(
                preserveAfterAdd,
                "Preserve flag should clear after dictionary mutation."
            );
            Assert.IsFalse(
                hasDuplicatesAfterAdd,
                "HasDuplicatesOrNulls should be cleared after mutation (MarkSerializationCacheDirty)."
            );
            // Arrays are preserved for order maintenance, not nulled
            Assert.IsTrue(
                storedKeysAfterAdd != null,
                "Serialized keys should be preserved for order maintenance after mutation."
            );
            Assert.IsTrue(
                storedValuesAfterAdd != null,
                "Serialized values should be preserved for order maintenance after mutation."
            );

            // After OnBeforeSerialize, the new entry should be added and duplicates handled
            dictionary.OnBeforeSerialize();

            string diagnosticInfo =
                $"After OnBeforeSerialize - Keys: [{string.Join(", ", dictionary.SerializedKeys)}], "
                + $"Values: [{string.Join(", ", dictionary.SerializedValues)}], "
                + $"Dictionary.Count: {dictionary.Count}, "
                + $"PreserveSerializedEntries: {dictionary.PreserveSerializedEntries}, "
                + $"HasDuplicatesOrNulls: {dictionary.HasDuplicatesOrNulls}";

            Assert.AreEqual(
                2,
                dictionary.SerializedKeys.Length,
                $"Expected 2 serialized keys. {diagnosticInfo}"
            );
            Assert.Contains(
                3,
                dictionary.SerializedKeys,
                $"Serialized keys should contain 3. {diagnosticInfo}"
            );
            Assert.Contains(
                4,
                dictionary.SerializedKeys,
                $"Serialized keys should contain 4. {diagnosticInfo}"
            );
        }

        [TestCase(
            new[] { 1, 1 },
            new[] { "a", "b" },
            2,
            "two",
            new[] { 1, 2 },
            new[] { "b", "two" },
            Description = "Duplicate keys with add"
        )]
        [TestCase(
            new[] { 1, 2 },
            new[] { "a", "b" },
            3,
            "c",
            new[] { 1, 2, 3 },
            new[] { "a", "b", "c" },
            Description = "No duplicates with add"
        )]
        [TestCase(
            new[] { 5, 5, 5 },
            new[] { "x", "y", "z" },
            6,
            "w",
            new[] { 5, 6 },
            new[] { "z", "w" },
            Description = "Triple duplicates with add"
        )]
        public void DuplicateKeyHandlingAfterMutationAndSerialization(
            int[] initialKeys,
            string[] initialValues,
            int keyToAdd,
            string valueToAdd,
            int[] expectedKeys,
            string[] expectedValues
        )
        {
            SerializableDictionary<int, string> dictionary = new();
            dictionary._keys = (int[])initialKeys.Clone();
            dictionary._values = (string[])initialValues.Clone();
            dictionary.OnAfterDeserialize();

            int uniqueKeyCountBefore = dictionary.Count;
            bool hasDuplicates = dictionary.HasDuplicatesOrNulls;

            dictionary.Add(keyToAdd, valueToAdd);

            dictionary.OnBeforeSerialize();

            string diagnosticInfo =
                $"Initial keys: [{string.Join(", ", initialKeys)}], "
                + $"Initial values: [{string.Join(", ", initialValues)}], "
                + $"Unique count before add: {uniqueKeyCountBefore}, "
                + $"Had duplicates: {hasDuplicates}, "
                + $"Added key: {keyToAdd}, "
                + $"Result keys: [{string.Join(", ", dictionary.SerializedKeys)}], "
                + $"Result values: [{string.Join(", ", dictionary.SerializedValues)}], "
                + $"Expected keys: [{string.Join(", ", expectedKeys)}]";

            Assert.AreEqual(
                expectedKeys.Length,
                dictionary.SerializedKeys.Length,
                $"Key array length mismatch. {diagnosticInfo}"
            );

            for (int i = 0; i < expectedKeys.Length; i++)
            {
                Assert.Contains(
                    expectedKeys[i],
                    dictionary.SerializedKeys,
                    $"Missing expected key {expectedKeys[i]}. {diagnosticInfo}"
                );
            }
        }

        [TestCase(new[] { 1, 1 }, new[] { "a", "b" }, Description = "Duplicate int keys")]
        [TestCase(
            new[] { 1, 2, 1 },
            new[] { "a", "b", "c" },
            Description = "Non-adjacent duplicates"
        )]
        [TestCase(new[] { 1, 1, 1 }, new[] { "a", "b", "c" }, Description = "Triple duplicates")]
        public void FastPathSkipsWhenDuplicateKeysExist(int[] keys, string[] values)
        {
            SerializableDictionary<int, string> dictionary = new();
            dictionary._keys = (int[])keys.Clone();
            dictionary._values = (string[])values.Clone();
            dictionary.OnAfterDeserialize();

            // Count unique keys
            HashSet<int> uniqueKeys = new(keys);
            int expectedUniqueCount = uniqueKeys.Count;

            // Add a new key to make counts potentially match (for the edge case)
            int newKey = 1000;
            while (uniqueKeys.Contains(newKey))
            {
                newKey++;
            }
            dictionary.Add(newKey, "new");

            dictionary.OnBeforeSerialize();

            string diagnosticInfo =
                $"Initial keys: [{string.Join(", ", keys)}], "
                + $"Added key: {newKey}, "
                + $"Result keys: [{string.Join(", ", dictionary.SerializedKeys)}], "
                + $"Dictionary.Count: {dictionary.Count}";

            // After serialization, duplicates should be resolved
            Assert.AreEqual(
                dictionary.Count,
                dictionary.SerializedKeys.Length,
                $"Serialized array length should match dictionary count after sync. {diagnosticInfo}"
            );

            // Verify no duplicates in result
            HashSet<int> resultKeys = new(dictionary.SerializedKeys);
            Assert.AreEqual(
                dictionary.SerializedKeys.Length,
                resultKeys.Count,
                $"Result should have no duplicate keys. {diagnosticInfo}"
            );

            // Verify the new key is present
            Assert.Contains(
                newKey,
                dictionary.SerializedKeys,
                $"New key should be in result. {diagnosticInfo}"
            );
        }

        [Test]
        public void SyncSerializedArraysHandlesDuplicatesWhenCountMatchesByCoincidence()
        {
            // This is the specific edge case: array has {3, 3} (length 2),
            // dictionary has {3, 4} (count 2), so counts match but keys differ
            SerializableDictionary<int, string> dictionary = new();
            int[] duplicateKeys = { 3, 3 };
            string[] values = { "old", "new" };
            dictionary._keys = (int[])duplicateKeys.Clone();
            dictionary._values = (string[])values.Clone();
            dictionary.OnAfterDeserialize();

            Assert.AreEqual(1, dictionary.Count, "Dictionary should have 1 unique key initially.");
            Assert.IsTrue(dictionary.HasDuplicatesOrNulls, "Should detect duplicate keys.");

            dictionary.Add(4, "fresh");

            Assert.AreEqual(2, dictionary.Count, "Dictionary should have 2 keys after add.");

            // Now: array length = 2 (duplicates), dict count = 2 (unique)
            // The fast path in SyncSerializedArraysPreservingOrder should NOT be taken
            // because the arrays have duplicates

            dictionary.OnBeforeSerialize();

            string diagnosticInfo =
                $"Result keys: [{string.Join(", ", dictionary.SerializedKeys)}], "
                + $"Result values: [{string.Join(", ", dictionary.SerializedValues)}], "
                + $"Dictionary.Count: {dictionary.Count}";

            Assert.AreEqual(
                2,
                dictionary.SerializedKeys.Length,
                $"Should have 2 unique keys after sync. {diagnosticInfo}"
            );
            Assert.Contains(
                3,
                dictionary.SerializedKeys,
                $"Should contain key 3. {diagnosticInfo}"
            );
            Assert.Contains(
                4,
                dictionary.SerializedKeys,
                $"Should contain key 4. {diagnosticInfo}"
            );

            // Verify no duplicates
            Assert.AreNotEqual(
                dictionary.SerializedKeys[0],
                dictionary.SerializedKeys[1],
                $"Keys should be distinct. {diagnosticInfo}"
            );
        }

        [Serializable]
        private sealed class IntCache : SerializableDictionary.Cache<int> { }

        private sealed class CaseInsensitiveDictionaryKey : IEquatable<CaseInsensitiveDictionaryKey>
        {
            public CaseInsensitiveDictionaryKey(string token)
            {
                Token = token;
            }

            public string Token { get; }

            public bool Equals(CaseInsensitiveDictionaryKey other)
            {
                if (other == null)
                {
                    return false;
                }

                return string.Equals(Token, other.Token, StringComparison.OrdinalIgnoreCase);
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as CaseInsensitiveDictionaryKey);
            }

            public override int GetHashCode()
            {
                if (Token == null)
                {
                    return 0;
                }

                return StringComparer.OrdinalIgnoreCase.GetHashCode(Token);
            }
        }

        // Order Preservation Tests

        [Test]
        public void OrderPreservationAfterAddMaintainsExistingKeyOrder()
        {
            SerializableDictionary<string, int> dictionary = new();
            dictionary._keys = new[] { "alpha", "beta", "gamma" };
            dictionary._values = new[] { 1, 2, 3 };
            dictionary.OnAfterDeserialize();

            // Add a new key
            dictionary.Add("delta", 4);
            dictionary.OnBeforeSerialize();

            // New key should be appended, existing order preserved
            string[] expectedKeys = { "alpha", "beta", "gamma", "delta" };
            int[] expectedValues = { 1, 2, 3, 4 };

            Assert.AreEqual(
                expectedKeys.Length,
                dictionary.SerializedKeys.Length,
                $"Expected {expectedKeys.Length} keys, got {dictionary.SerializedKeys.Length}. "
                    + $"Keys: [{string.Join(", ", dictionary.SerializedKeys)}]"
            );
            CollectionAssert.AreEqual(expectedKeys, dictionary.SerializedKeys);
            CollectionAssert.AreEqual(expectedValues, dictionary.SerializedValues);
        }

        [Test]
        public void OrderPreservationAfterRemoveMaintainsRemainingKeyOrder()
        {
            SerializableDictionary<string, int> dictionary = new();
            dictionary._keys = new[] { "alpha", "beta", "gamma", "delta" };
            dictionary._values = new[] { 1, 2, 3, 4 };
            dictionary.OnAfterDeserialize();

            // Remove middle key
            dictionary.Remove("beta");
            dictionary.OnBeforeSerialize();

            // Remaining keys should maintain their relative order
            string[] expectedKeys = { "alpha", "gamma", "delta" };
            int[] expectedValues = { 1, 3, 4 };

            Assert.AreEqual(
                expectedKeys.Length,
                dictionary.SerializedKeys.Length,
                $"Expected {expectedKeys.Length} keys after removal, got {dictionary.SerializedKeys.Length}. "
                    + $"Keys: [{string.Join(", ", dictionary.SerializedKeys)}]"
            );
            CollectionAssert.AreEqual(expectedKeys, dictionary.SerializedKeys);
            CollectionAssert.AreEqual(expectedValues, dictionary.SerializedValues);
        }

        [Test]
        public void OrderPreservationAfterValueUpdateMaintainsKeyOrder()
        {
            SerializableDictionary<string, int> dictionary = new();
            dictionary._keys = new[] { "alpha", "beta", "gamma" };
            dictionary._values = new[] { 1, 2, 3 };
            dictionary.OnAfterDeserialize();

            // Update existing key's value
            dictionary["beta"] = 20;
            dictionary.OnBeforeSerialize();

            // Key order should be unchanged, only value updated
            string[] expectedKeys = { "alpha", "beta", "gamma" };
            int[] expectedValues = { 1, 20, 3 };

            Assert.AreEqual(expectedKeys.Length, dictionary.SerializedKeys.Length);
            CollectionAssert.AreEqual(expectedKeys, dictionary.SerializedKeys);
            CollectionAssert.AreEqual(expectedValues, dictionary.SerializedValues);
        }

        [TestCase("Add")]
        [TestCase("Indexer")]
        [TestCase("Remove")]
        [TestCase("Clear")]
        public void MutationOperationsClearPreserveFlagButKeepArrays(string operation)
        {
            SerializableDictionary<int, string> dictionary = new();
            dictionary._keys = new[] { 1, 2, 3 };
            dictionary._values = new[] { "one", "two", "three" };
            dictionary.OnAfterDeserialize();

            Assert.IsTrue(
                dictionary.PreserveSerializedEntries,
                "Preserve flag should be true after deserialization"
            );

            // Perform mutation
            switch (operation)
            {
                case "Add":
                    dictionary.Add(4, "four");
                    break;
                case "Indexer":
                    dictionary[2] = "TWO";
                    break;
                case "Remove":
                    dictionary.Remove(1);
                    break;
                case "Clear":
                    dictionary.Clear();
                    break;
            }

            // After mutation, preserve flag should be cleared
            Assert.IsFalse(
                dictionary.PreserveSerializedEntries,
                $"Preserve flag should be false after {operation} operation"
            );

            // Arrays should still exist for order preservation (except Clear which nulls them)
            if (operation == "Clear")
            {
                Assert.IsTrue(dictionary.SerializedKeys == null);
                Assert.IsTrue(dictionary.SerializedValues == null);
            }
            else
            {
                Assert.IsTrue(
                    dictionary.SerializedKeys != null,
                    $"Arrays should be preserved after {operation} for order maintenance"
                );
                Assert.IsTrue(dictionary.SerializedValues != null);
            }
        }

        [Test]
        public void MultipleSerializationCyclesMaintainUserDefinedOrder()
        {
            SerializableDictionary<string, int> dictionary = new();
            string[] originalOrder = { "zebra", "alpha", "mango" };
            int[] originalValues = { 1, 2, 3 };
            dictionary._keys = (string[])originalOrder.Clone();
            dictionary._values = (int[])originalValues.Clone();
            dictionary.OnAfterDeserialize();

            // Multiple serialize/deserialize cycles should maintain order
            for (int cycle = 0; cycle < 3; cycle++)
            {
                dictionary.OnBeforeSerialize();

                string[] currentKeys = dictionary.SerializedKeys;
                int[] currentValues = dictionary.SerializedValues;

                Assert.AreEqual(
                    originalOrder.Length,
                    currentKeys.Length,
                    $"Cycle {cycle}: Key count changed from {originalOrder.Length} to {currentKeys.Length}"
                );
                CollectionAssert.AreEqual(
                    originalOrder,
                    currentKeys,
                    $"Cycle {cycle}: Key order changed. Expected [{string.Join(", ", originalOrder)}], "
                        + $"got [{string.Join(", ", currentKeys)}]"
                );
                CollectionAssert.AreEqual(
                    originalValues,
                    currentValues,
                    $"Cycle {cycle}: Value order changed"
                );

                // Simulate domain reload by re-deserializing
                dictionary._keys = (string[])currentKeys.Clone();
                dictionary._values = (int[])currentValues.Clone();
                dictionary.OnAfterDeserialize();
            }
        }

        [Test]
        public void ToKeysArrayReturnsEmptyArrayForEmptyDictionary()
        {
            SerializableDictionary<string, int> dictionary = new();

            string[] result = dictionary.ToKeysArray();

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Length);
            Assert.AreSame(Array.Empty<string>(), result);
        }

        [Test]
        public void ToPersistedOrderKeysArrayReturnsEmptyArrayForEmptyDictionary()
        {
            SerializableDictionary<string, int> dictionary = new();

            string[] result = dictionary.ToPersistedOrderKeysArray();

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Length);
            Assert.AreSame(Array.Empty<string>(), result);
        }

        [Test]
        public void ToValuesArrayReturnsEmptyArrayForEmptyDictionary()
        {
            SerializableDictionary<string, int> dictionary = new();

            int[] result = dictionary.ToValuesArray();

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Length);
            Assert.AreSame(Array.Empty<int>(), result);
        }

        [Test]
        public void ToPersistedOrderValuesArrayReturnsEmptyArrayForEmptyDictionary()
        {
            SerializableDictionary<string, int> dictionary = new();

            int[] result = dictionary.ToPersistedOrderValuesArray();

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Length);
            Assert.AreSame(Array.Empty<int>(), result);
        }

        [Test]
        public void ToArrayReturnsEmptyArrayForEmptyDictionary()
        {
            SerializableDictionary<string, int> dictionary = new();

            KeyValuePair<string, int>[] result = dictionary.ToArray();

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Length);
            Assert.AreSame(Array.Empty<KeyValuePair<string, int>>(), result);
        }

        [Test]
        public void ToPersistedOrderArrayReturnsEmptyArrayForEmptyDictionary()
        {
            SerializableDictionary<string, int> dictionary = new();

            KeyValuePair<string, int>[] result = dictionary.ToPersistedOrderArray();

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Length);
            Assert.AreSame(Array.Empty<KeyValuePair<string, int>>(), result);
        }

        [Test]
        public void ToKeysArrayReturnsDefensiveCopy()
        {
            SerializableDictionary<string, int> dictionary = new() { { "a", 1 }, { "b", 2 } };
            dictionary.OnBeforeSerialize();

            string[] firstCopy = dictionary.ToKeysArray();
            string[] secondCopy = dictionary.ToKeysArray();

            Assert.AreNotSame(firstCopy, secondCopy);
            Assert.AreNotSame(firstCopy, dictionary._keys);

            firstCopy[0] = "modified";
            Assert.AreNotEqual("modified", dictionary.ToKeysArray()[0]);
        }

        [Test]
        public void ToValuesArrayReturnsDefensiveCopy()
        {
            SerializableDictionary<string, int> dictionary = new() { { "a", 1 }, { "b", 2 } };
            dictionary.OnBeforeSerialize();

            int[] firstCopy = dictionary.ToValuesArray();
            int[] secondCopy = dictionary.ToValuesArray();

            Assert.AreNotSame(firstCopy, secondCopy);

            firstCopy[0] = 999;
            Assert.AreNotEqual(999, dictionary.ToValuesArray()[0]);
        }

        [Test]
        public void ToArrayReturnsDefensiveCopy()
        {
            SerializableDictionary<string, int> dictionary = new() { { "a", 1 }, { "b", 2 } };
            dictionary.OnBeforeSerialize();

            KeyValuePair<string, int>[] firstCopy = dictionary.ToArray();
            KeyValuePair<string, int>[] secondCopy = dictionary.ToArray();

            Assert.AreNotSame(firstCopy, secondCopy);
        }

        [Test]
        public void ToPersistedOrderKeysArrayReturnsDefensiveCopy()
        {
            SerializableDictionary<string, int> dictionary = new() { { "a", 1 }, { "b", 2 } };
            dictionary.OnBeforeSerialize();

            string[] firstCopy = dictionary.ToPersistedOrderKeysArray();
            string[] secondCopy = dictionary.ToPersistedOrderKeysArray();

            Assert.AreNotSame(firstCopy, secondCopy);
            Assert.AreNotSame(firstCopy, dictionary._keys);

            firstCopy[0] = "modified";
            Assert.AreNotEqual("modified", dictionary.ToPersistedOrderKeysArray()[0]);
        }

        [Test]
        public void ToPersistedOrderValuesArrayReturnsDefensiveCopy()
        {
            SerializableDictionary<string, int> dictionary = new() { { "a", 1 }, { "b", 2 } };
            dictionary.OnBeforeSerialize();

            int[] firstCopy = dictionary.ToPersistedOrderValuesArray();
            int[] secondCopy = dictionary.ToPersistedOrderValuesArray();

            Assert.AreNotSame(firstCopy, secondCopy);

            firstCopy[0] = 999;
            Assert.AreNotEqual(999, dictionary.ToPersistedOrderValuesArray()[0]);
        }

        [Test]
        public void ToPersistedOrderArrayReturnsDefensiveCopy()
        {
            SerializableDictionary<string, int> dictionary = new() { { "a", 1 }, { "b", 2 } };
            dictionary.OnBeforeSerialize();

            KeyValuePair<string, int>[] firstCopy = dictionary.ToPersistedOrderArray();
            KeyValuePair<string, int>[] secondCopy = dictionary.ToPersistedOrderArray();

            Assert.AreNotSame(firstCopy, secondCopy);
        }

        [Test]
        public void ToKeysArrayReturnsDictionaryIterationOrder()
        {
            SerializableDictionary<string, int> dictionary = new();
            string[] userOrder = { "zebra", "alpha", "mango" };
            int[] values = { 1, 2, 3 };
            dictionary._keys = (string[])userOrder.Clone();
            dictionary._values = (int[])values.Clone();
            dictionary.OnAfterDeserialize();

            string[] result = dictionary.ToKeysArray();

            // ToKeysArray should return dictionary iteration order, not user-defined order
            // Dictionary iteration order may differ from insertion order
            Assert.AreEqual(3, result.Length);
            CollectionAssert.AreEquivalent(userOrder, result);
        }

        [Test]
        public void ToPersistedOrderKeysArrayPreservesUserDefinedOrder()
        {
            SerializableDictionary<string, int> dictionary = new();
            string[] userOrder = { "zebra", "alpha", "mango" };
            int[] values = { 1, 2, 3 };
            dictionary._keys = (string[])userOrder.Clone();
            dictionary._values = (int[])values.Clone();
            dictionary.OnAfterDeserialize();

            string[] result = dictionary.ToPersistedOrderKeysArray();

            // ToPersistedOrderKeysArray should return user-defined order
            CollectionAssert.AreEqual(userOrder, result);
        }

        [Test]
        public void ToValuesArrayReturnsDictionaryIterationOrder()
        {
            SerializableDictionary<string, int> dictionary = new();
            string[] keys = { "zebra", "alpha", "mango" };
            int[] userOrder = { 100, 200, 300 };
            dictionary._keys = (string[])keys.Clone();
            dictionary._values = (int[])userOrder.Clone();
            dictionary.OnAfterDeserialize();

            int[] result = dictionary.ToValuesArray();

            // ToValuesArray should return dictionary iteration order, not user-defined order
            Assert.AreEqual(3, result.Length);
            CollectionAssert.AreEquivalent(userOrder, result);
        }

        [Test]
        public void ToPersistedOrderValuesArrayPreservesUserDefinedOrder()
        {
            SerializableDictionary<string, int> dictionary = new();
            string[] keys = { "zebra", "alpha", "mango" };
            int[] userOrder = { 100, 200, 300 };
            dictionary._keys = (string[])keys.Clone();
            dictionary._values = (int[])userOrder.Clone();
            dictionary.OnAfterDeserialize();

            int[] result = dictionary.ToPersistedOrderValuesArray();

            // ToPersistedOrderValuesArray should return user-defined order
            CollectionAssert.AreEqual(userOrder, result);
        }

        [Test]
        public void ToArrayReturnsDictionaryIterationOrder()
        {
            SerializableDictionary<string, int> dictionary = new();
            string[] keys = { "zebra", "alpha", "mango" };
            int[] values = { 1, 2, 3 };
            dictionary._keys = (string[])keys.Clone();
            dictionary._values = (int[])values.Clone();
            dictionary.OnAfterDeserialize();

            KeyValuePair<string, int>[] result = dictionary.ToArray();

            // ToArray should return dictionary iteration order, not user-defined order
            Assert.AreEqual(3, result.Length);
            HashSet<string> resultKeys = new();
            foreach (KeyValuePair<string, int> pair in result)
            {
                resultKeys.Add(pair.Key);
            }
            Assert.IsTrue(resultKeys.Contains("zebra"));
            Assert.IsTrue(resultKeys.Contains("alpha"));
            Assert.IsTrue(resultKeys.Contains("mango"));
        }

        [Test]
        public void ToPersistedOrderArrayPreservesUserDefinedOrder()
        {
            SerializableDictionary<string, int> dictionary = new();
            string[] keys = { "zebra", "alpha", "mango" };
            int[] values = { 1, 2, 3 };
            dictionary._keys = (string[])keys.Clone();
            dictionary._values = (int[])values.Clone();
            dictionary.OnAfterDeserialize();

            KeyValuePair<string, int>[] result = dictionary.ToPersistedOrderArray();

            // ToPersistedOrderArray should return user-defined order
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual("zebra", result[0].Key);
            Assert.AreEqual(1, result[0].Value);
            Assert.AreEqual("alpha", result[1].Key);
            Assert.AreEqual(2, result[1].Value);
            Assert.AreEqual("mango", result[2].Key);
            Assert.AreEqual(3, result[2].Value);
        }

        [Test]
        public void ToArrayKeysAndValuesAreAligned()
        {
            SerializableDictionary<string, int> dictionary = new()
            {
                { "first", 10 },
                { "second", 20 },
                { "third", 30 },
            };
            dictionary.OnBeforeSerialize();

            string[] keys = dictionary.ToKeysArray();
            int[] values = dictionary.ToValuesArray();
            KeyValuePair<string, int>[] pairs = dictionary.ToArray();

            Assert.AreEqual(keys.Length, values.Length);
            Assert.AreEqual(keys.Length, pairs.Length);

            for (int i = 0; i < keys.Length; i++)
            {
                Assert.AreEqual(keys[i], pairs[i].Key);
                Assert.AreEqual(values[i], pairs[i].Value);
                Assert.AreEqual(dictionary[keys[i]], values[i]);
            }
        }

        [Test]
        public void ToPersistedOrderArrayKeysAndValuesAreAligned()
        {
            SerializableDictionary<string, int> dictionary = new();
            string[] userKeys = { "zebra", "alpha", "mango" };
            int[] userValues = { 100, 200, 300 };
            dictionary._keys = (string[])userKeys.Clone();
            dictionary._values = (int[])userValues.Clone();
            dictionary.OnAfterDeserialize();

            string[] keys = dictionary.ToPersistedOrderKeysArray();
            int[] values = dictionary.ToPersistedOrderValuesArray();
            KeyValuePair<string, int>[] pairs = dictionary.ToPersistedOrderArray();

            Assert.AreEqual(keys.Length, values.Length);
            Assert.AreEqual(keys.Length, pairs.Length);

            for (int i = 0; i < keys.Length; i++)
            {
                Assert.AreEqual(userKeys[i], keys[i]);
                Assert.AreEqual(userValues[i], values[i]);
                Assert.AreEqual(keys[i], pairs[i].Key);
                Assert.AreEqual(values[i], pairs[i].Value);
                Assert.AreEqual(dictionary[keys[i]], values[i]);
            }
        }

        [Test]
        public void ToArrayReflectsCurrentStateAfterMutations()
        {
            SerializableDictionary<string, int> dictionary = new()
            {
                { "alpha", 1 },
                { "beta", 2 },
            };
            dictionary.OnBeforeSerialize();

            bool removed = dictionary.Remove("alpha");
            Assert.IsTrue(removed);
            dictionary["gamma"] = 3;

            string[] keys = dictionary.ToKeysArray();
            int[] values = dictionary.ToValuesArray();

            Assert.AreEqual(2, keys.Length);
            Assert.IsFalse(((IList<string>)keys).Contains("alpha"));
            Assert.IsTrue(((IList<string>)keys).Contains("beta"));
            Assert.IsTrue(((IList<string>)keys).Contains("gamma"));

            Assert.AreEqual(2, values.Length);
        }

        [Test]
        public void ToPersistedOrderArrayOrderSurvivesMultipleSerializationCycles()
        {
            SerializableDictionary<string, int> dictionary = new();
            string[] userOrder = { "zebra", "alpha", "mango" };
            int[] values = { 100, 200, 300 };
            dictionary._keys = (string[])userOrder.Clone();
            dictionary._values = (int[])values.Clone();
            dictionary.OnAfterDeserialize();

            for (int i = 0; i < 3; i++)
            {
                dictionary.OnBeforeSerialize();
                dictionary.OnAfterDeserialize();
            }

            string[] resultKeys = dictionary.ToPersistedOrderKeysArray();
            int[] resultValues = dictionary.ToPersistedOrderValuesArray();

            // ToPersistedOrder* should preserve user-defined order across serialization cycles
            CollectionAssert.AreEqual(userOrder, resultKeys);
            CollectionAssert.AreEqual(values, resultValues);
        }

        [Test]
        public void ToArrayReturnsDictionaryIterationOrderAfterMultipleSerializationCycles()
        {
            SerializableDictionary<string, int> dictionary = new();
            string[] userOrder = { "zebra", "alpha", "mango" };
            int[] values = { 100, 200, 300 };
            dictionary._keys = (string[])userOrder.Clone();
            dictionary._values = (int[])values.Clone();
            dictionary.OnAfterDeserialize();

            for (int i = 0; i < 3; i++)
            {
                dictionary.OnBeforeSerialize();
                dictionary.OnAfterDeserialize();
            }

            string[] resultKeys = dictionary.ToKeysArray();
            int[] resultValues = dictionary.ToValuesArray();

            // ToKeysArray/ToValuesArray should return dictionary iteration order
            Assert.AreEqual(3, resultKeys.Length);
            Assert.AreEqual(3, resultValues.Length);
            CollectionAssert.AreEquivalent(userOrder, resultKeys);
            CollectionAssert.AreEquivalent(values, resultValues);
        }

        [Test]
        public void ToArrayWithDuplicateKeysInSerializedDataHandlesGracefully()
        {
            SerializableDictionary<string, int> dictionary = new();
            string[] duplicateKeys = { "a", "a", "b" };
            int[] values = { 1, 2, 3 };
            dictionary._keys = (string[])duplicateKeys.Clone();
            dictionary._values = (int[])values.Clone();
            dictionary.OnAfterDeserialize();

            string[] resultKeys = dictionary.ToKeysArray();
            KeyValuePair<string, int>[] pairs = dictionary.ToArray();

            Assert.AreEqual(2, resultKeys.Length);
            Assert.AreEqual(2, pairs.Length);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(10)]
        [TestCase(100)]
        public void ToArrayLengthMatchesCountForVariousSizes(int size)
        {
            SerializableDictionary<int, string> dictionary = new();
            for (int i = 0; i < size; i++)
            {
                dictionary[i] = $"value_{i}";
            }

            int[] keys = dictionary.ToKeysArray();
            string[] values = dictionary.ToValuesArray();
            KeyValuePair<int, string>[] pairs = dictionary.ToArray();

            Assert.AreEqual(size, keys.Length);
            Assert.AreEqual(size, values.Length);
            Assert.AreEqual(size, pairs.Length);
            Assert.AreEqual(dictionary.Count, keys.Length);
        }

        [TestCase("Add")]
        [TestCase("Remove")]
        [TestCase("Clear")]
        [TestCase("Indexer")]
        public void ToArrayReflectsStateAfterMutation(string operation)
        {
            SerializableDictionary<int, string> dictionary = new()
            {
                { 1, "one" },
                { 2, "two" },
                { 3, "three" },
            };
            dictionary.OnBeforeSerialize();

            switch (operation)
            {
                case "Add":
                    dictionary.Add(4, "four");
                    Assert.AreEqual(4, dictionary.ToKeysArray().Length);
                    Assert.IsTrue(((IList<int>)dictionary.ToKeysArray()).Contains(4));
                    break;
                case "Remove":
                    bool removed = dictionary.Remove(2);
                    Assert.IsTrue(removed);
                    Assert.AreEqual(2, dictionary.ToKeysArray().Length);
                    Assert.IsFalse(((IList<int>)dictionary.ToKeysArray()).Contains(2));
                    break;
                case "Clear":
                    dictionary.Clear();
                    Assert.AreEqual(0, dictionary.ToKeysArray().Length);
                    Assert.AreEqual(0, dictionary.ToValuesArray().Length);
                    Assert.AreEqual(0, dictionary.ToArray().Length);
                    break;
                case "Indexer":
                    dictionary[1] = "modified";
                    string[] values = dictionary.ToValuesArray();
                    int keyIndex = Array.IndexOf(dictionary.ToKeysArray(), 1);
                    Assert.AreEqual("modified", values[keyIndex]);
                    break;
            }
        }

        [Test]
        public void ToPersistedOrderArrayPreservesInsertionOrderForNewKeys()
        {
            SerializableDictionary<int, string> dictionary = new();
            int[] existingKeys = { 1, 2, 3 };
            string[] existingValues = { "one", "two", "three" };
            dictionary._keys = (int[])existingKeys.Clone();
            dictionary._values = (string[])existingValues.Clone();
            dictionary.OnAfterDeserialize();

            dictionary[4] = "four";
            dictionary[5] = "five";

            int[] resultKeys = dictionary.ToPersistedOrderKeysArray();

            // ToPersistedOrderKeysArray should preserve insertion order for new keys
            Assert.AreEqual(5, resultKeys.Length);
            for (int i = 0; i < existingKeys.Length; i++)
            {
                Assert.AreEqual(existingKeys[i], resultKeys[i]);
            }

            Assert.AreEqual(4, resultKeys[3]);
            Assert.AreEqual(5, resultKeys[4]);
        }

        [Test]
        public void ToArrayContainsAllKeysAfterAdditions()
        {
            SerializableDictionary<int, string> dictionary = new();
            int[] existingKeys = { 1, 2, 3 };
            string[] existingValues = { "one", "two", "three" };
            dictionary._keys = (int[])existingKeys.Clone();
            dictionary._values = (string[])existingValues.Clone();
            dictionary.OnAfterDeserialize();

            dictionary[4] = "four";
            dictionary[5] = "five";

            int[] resultKeys = dictionary.ToKeysArray();

            // ToKeysArray should contain all keys in dictionary iteration order
            Assert.AreEqual(5, resultKeys.Length);
            HashSet<int> keySet = new(resultKeys);
            Assert.IsTrue(keySet.Contains(1));
            Assert.IsTrue(keySet.Contains(2));
            Assert.IsTrue(keySet.Contains(3));
            Assert.IsTrue(keySet.Contains(4));
            Assert.IsTrue(keySet.Contains(5));
        }

        [TestCase("IndexerUpdateExisting", Description = "Updating existing key value via indexer")]
        [TestCase("IndexerAddNew", Description = "Adding new key via indexer")]
        [TestCase("AddMethod", Description = "Adding via Add method")]
        [TestCase("TryAddMethod", Description = "Adding via TryAdd method")]
        [TestCase("RemoveMethod", Description = "Removing via Remove method")]
        [TestCase("ClearMethod", Description = "Clearing the dictionary")]
        public void PreserveSerializedEntriesFlagBehaviorAfterMutation(string mutationType)
        {
            SerializableDictionary<int, string> dictionary = new() { { 1, "one" }, { 2, "two" } };
            dictionary.OnBeforeSerialize();

            bool preserveFlagBefore = dictionary.PreserveSerializedEntries;
            Assert.IsTrue(
                preserveFlagBefore,
                $"PreserveSerializedEntries should be true after OnBeforeSerialize, before {mutationType}"
            );

            switch (mutationType)
            {
                case "IndexerUpdateExisting":
                    dictionary[1] = "updated";
                    break;
                case "IndexerAddNew":
                    dictionary[3] = "three";
                    break;
                case "AddMethod":
                    dictionary.Add(4, "four");
                    break;
                case "TryAddMethod":
                    dictionary.TryAdd(5, "five");
                    break;
                case "RemoveMethod":
                    dictionary.Remove(1);
                    break;
                case "ClearMethod":
                    dictionary.Clear();
                    break;
            }

            bool preserveFlagAfter = dictionary.PreserveSerializedEntries;
            Assert.IsFalse(
                preserveFlagAfter,
                $"PreserveSerializedEntries should be false after {mutationType} mutation"
            );
        }

        [TestCase(
            "IndexerUpdateExisting",
            1,
            "updated",
            Description = "Updating existing key value"
        )]
        [TestCase("IndexerAddNew", 3, "three", Description = "Adding new key via indexer")]
        public void ToValuesArrayReflectsValueChangesAfterIndexerMutation(
            string mutationType,
            int key,
            string expectedValue
        )
        {
            SerializableDictionary<int, string> dictionary = new() { { 1, "one" }, { 2, "two" } };
            dictionary.OnBeforeSerialize();

            dictionary[key] = expectedValue;

            string[] values = dictionary.ToValuesArray();
            int[] keys = dictionary.ToKeysArray();
            int keyIndex = Array.IndexOf(keys, key);

            Assert.GreaterOrEqual(
                keyIndex,
                0,
                $"Key {key} should exist in ToKeysArray() after {mutationType}"
            );
            Assert.AreEqual(
                expectedValue,
                values[keyIndex],
                $"ToValuesArray() should reflect updated value after {mutationType}. Keys: [{string.Join(", ", keys)}], Values: [{string.Join(", ", values)}]"
            );
        }

        [Test]
        public void ToArrayMethodsDiagnosticsForMutationTracking()
        {
            SerializableDictionary<string, int> dictionary = new()
            {
                { "a", 1 },
                { "b", 2 },
                { "c", 3 },
            };

            dictionary.OnBeforeSerialize();
            string[] keysBeforeMutation = dictionary.ToKeysArray();
            int[] valuesBeforeMutation = dictionary.ToValuesArray();

            dictionary["b"] = 200;

            string[] keysAfterMutation = dictionary.ToKeysArray();
            int[] valuesAfterMutation = dictionary.ToValuesArray();

            string diagnosticInfo =
                $"Before mutation - Keys: [{string.Join(", ", keysBeforeMutation)}], Values: [{string.Join(", ", valuesBeforeMutation)}]. "
                + $"After mutation - Keys: [{string.Join(", ", keysAfterMutation)}], Values: [{string.Join(", ", valuesAfterMutation)}]. "
                + $"PreserveSerializedEntries: {dictionary.PreserveSerializedEntries}";

            int bIndexAfter = Array.IndexOf(keysAfterMutation, "b");
            Assert.AreEqual(
                200,
                valuesAfterMutation[bIndexAfter],
                $"Value for 'b' should be 200 after mutation. {diagnosticInfo}"
            );
        }

        [Test]
        public void DuplicateKeysInSerializedArraysArePreservedWhenPreserveFlagSet()
        {
            SerializableDictionary<string, int> dictionary = new();
            string[] keysWithDuplicates = { "dup", "dup", "unique" };
            int[] values = { 1, 2, 3 };
            dictionary._keys = keysWithDuplicates;
            dictionary._values = values;

            dictionary.OnAfterDeserialize();

            Assert.IsTrue(
                dictionary.PreserveSerializedEntries,
                "PreserveSerializedEntries should be true after deserialization"
            );
            Assert.IsTrue(
                dictionary.HasDuplicatesOrNulls,
                "HasDuplicatesOrNulls should be true when duplicate keys exist"
            );

            dictionary.OnBeforeSerialize();

            CollectionAssert.AreEqual(
                keysWithDuplicates,
                dictionary.SerializedKeys,
                "Serialized keys with duplicates should be preserved exactly when preserve flag is set"
            );
            CollectionAssert.AreEqual(
                values,
                dictionary.SerializedValues,
                "Serialized values should be preserved exactly when preserve flag is set"
            );
        }

        [Test]
        public void ToArrayAndToPersistedOrderArrayReturnDifferentOrdersWhenDictionaryIterationDiffers()
        {
            SerializableDictionary<string, int> dictionary = new();
            // Create a specific user-defined order that may differ from dictionary iteration
            string[] userOrder = { "charlie", "alice", "bob" };
            int[] values = { 3, 1, 2 };
            dictionary._keys = (string[])userOrder.Clone();
            dictionary._values = (int[])values.Clone();
            dictionary.OnAfterDeserialize();

            KeyValuePair<string, int>[] normalArray = dictionary.ToArray();
            KeyValuePair<string, int>[] persistedArray = dictionary.ToPersistedOrderArray();

            // Both should have same count
            Assert.AreEqual(3, normalArray.Length);
            Assert.AreEqual(3, persistedArray.Length);

            // Both should have the same key-value pairs (but potentially different order)
            HashSet<string> normalKeys = new();
            HashSet<string> persistedKeys = new();
            foreach (KeyValuePair<string, int> pair in normalArray)
            {
                normalKeys.Add(pair.Key);
            }
            foreach (KeyValuePair<string, int> pair in persistedArray)
            {
                persistedKeys.Add(pair.Key);
            }
            Assert.IsTrue(normalKeys.SetEquals(persistedKeys));

            // The persisted order array should preserve the exact user-defined order
            Assert.AreEqual("charlie", persistedArray[0].Key);
            Assert.AreEqual(3, persistedArray[0].Value);
            Assert.AreEqual("alice", persistedArray[1].Key);
            Assert.AreEqual(1, persistedArray[1].Value);
            Assert.AreEqual("bob", persistedArray[2].Key);
            Assert.AreEqual(2, persistedArray[2].Value);
        }

        [Test]
        public void ToPersistedOrderArrayReflectsCurrentStateAfterMutations()
        {
            SerializableDictionary<string, int> dictionary = new();
            string[] userOrder = { "alpha", "beta", "gamma" };
            int[] values = { 1, 2, 3 };
            dictionary._keys = (string[])userOrder.Clone();
            dictionary._values = (int[])values.Clone();
            dictionary.OnAfterDeserialize();

            bool removed = dictionary.Remove("beta");
            Assert.IsTrue(removed);
            dictionary["delta"] = 4;

            KeyValuePair<string, int>[] result = dictionary.ToPersistedOrderArray();

            // Should contain alpha, gamma, delta (beta removed)
            Assert.AreEqual(3, result.Length);
            HashSet<string> resultKeys = new();
            foreach (KeyValuePair<string, int> pair in result)
            {
                resultKeys.Add(pair.Key);
            }
            Assert.IsTrue(resultKeys.Contains("alpha"));
            Assert.IsFalse(resultKeys.Contains("beta"));
            Assert.IsTrue(resultKeys.Contains("gamma"));
            Assert.IsTrue(resultKeys.Contains("delta"));
        }

        [Test]
        public void ToArrayConsistentWithDictionaryEnumeration()
        {
            SerializableDictionary<string, int> dictionary = new()
            {
                { "one", 1 },
                { "two", 2 },
                { "three", 3 },
            };

            KeyValuePair<string, int>[] arrayResult = dictionary.ToArray();
            List<KeyValuePair<string, int>> enumerationResult = new();
            foreach (KeyValuePair<string, int> pair in dictionary)
            {
                enumerationResult.Add(pair);
            }

            Assert.AreEqual(enumerationResult.Count, arrayResult.Length);
            for (int i = 0; i < arrayResult.Length; i++)
            {
                Assert.AreEqual(enumerationResult[i].Key, arrayResult[i].Key);
                Assert.AreEqual(enumerationResult[i].Value, arrayResult[i].Value);
            }
        }

        [Test]
        public void ToKeysArrayConsistentWithKeysEnumeration()
        {
            SerializableDictionary<string, int> dictionary = new()
            {
                { "one", 1 },
                { "two", 2 },
                { "three", 3 },
            };

            string[] arrayResult = dictionary.ToKeysArray();
            List<string> enumerationResult = new();
            foreach (string key in dictionary.Keys)
            {
                enumerationResult.Add(key);
            }

            Assert.AreEqual(enumerationResult.Count, arrayResult.Length);
            for (int i = 0; i < arrayResult.Length; i++)
            {
                Assert.AreEqual(enumerationResult[i], arrayResult[i]);
            }
        }

        [Test]
        public void ToValuesArrayConsistentWithValuesEnumeration()
        {
            SerializableDictionary<string, int> dictionary = new()
            {
                { "one", 1 },
                { "two", 2 },
                { "three", 3 },
            };

            int[] arrayResult = dictionary.ToValuesArray();
            List<int> enumerationResult = new();
            foreach (int value in dictionary.Values)
            {
                enumerationResult.Add(value);
            }

            Assert.AreEqual(enumerationResult.Count, arrayResult.Length);
            for (int i = 0; i < arrayResult.Length; i++)
            {
                Assert.AreEqual(enumerationResult[i], arrayResult[i]);
            }
        }

        [Test]
        public void SingleElementToArrayBehavior()
        {
            SerializableDictionary<string, int> dictionary = new() { { "only", 42 } };

            string[] keys = dictionary.ToKeysArray();
            int[] values = dictionary.ToValuesArray();
            KeyValuePair<string, int>[] pairs = dictionary.ToArray();
            string[] persistedKeys = dictionary.ToPersistedOrderKeysArray();
            int[] persistedValues = dictionary.ToPersistedOrderValuesArray();
            KeyValuePair<string, int>[] persistedPairs = dictionary.ToPersistedOrderArray();

            Assert.AreEqual(1, keys.Length);
            Assert.AreEqual(1, values.Length);
            Assert.AreEqual(1, pairs.Length);
            Assert.AreEqual(1, persistedKeys.Length);
            Assert.AreEqual(1, persistedValues.Length);
            Assert.AreEqual(1, persistedPairs.Length);

            Assert.AreEqual("only", keys[0]);
            Assert.AreEqual(42, values[0]);
            Assert.AreEqual("only", persistedKeys[0]);
            Assert.AreEqual(42, persistedValues[0]);
        }
    }
}
