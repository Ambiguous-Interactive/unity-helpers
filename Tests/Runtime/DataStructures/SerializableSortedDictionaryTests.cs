// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializableSortedDictionaryTests
    {
        [Test]
        public void EntriesEnumerateInAscendingOrder()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                { 3, "three" },
                { 1, "one" },
                { 2, "two" },
            };

            int[] expectedKeys = { 1, 2, 3 };
            int index = 0;
            foreach (KeyValuePair<int, string> pair in dictionary)
            {
                Assert.Less(index, expectedKeys.Length);
                Assert.AreEqual(expectedKeys[index], pair.Key);
                index++;
            }

            Assert.AreEqual(expectedKeys.Length, index);
        }

        [Test]
        public void CopyFromOrdersSourceDictionary()
        {
            SerializableSortedDictionary<int, string> dictionary = new();
            Dictionary<int, string> source = new()
            {
                { 7, "seven" },
                { 4, "four" },
                { 5, "five" },
            };

            dictionary.CopyFrom(source);

            int[] expectedKeys = { 4, 5, 7 };
            int index = 0;
            foreach (KeyValuePair<int, string> pair in dictionary)
            {
                Assert.Less(index, expectedKeys.Length);
                Assert.AreEqual(expectedKeys[index], pair.Key);
                index++;
            }

            Assert.AreEqual(expectedKeys.Length, index);
        }

        [Test]
        public void CopyFromClearsPreservedSerializedArrays()
        {
            SerializableSortedDictionary<string, string> dictionary = new();
            string[] duplicateKeys = { "dup", "dup" };
            string[] duplicateValues = { "first", "second" };
            dictionary._keys = duplicateKeys;
            dictionary._values = duplicateValues;

            dictionary.OnAfterDeserialize();
            Assert.IsTrue(
                dictionary.PreserveSerializedEntries,
                "Duplicate serialized data should set preserve flag."
            );

            Dictionary<string, string> source = new() { { "alpha", "1" }, { "bravo", "2" } };

            dictionary.CopyFrom(source);

            Assert.IsFalse(
                dictionary.PreserveSerializedEntries,
                "CopyFrom should reset preserve flag for clean data."
            );
            Assert.IsTrue(dictionary.SerializedKeys == null);
            Assert.IsTrue(dictionary.SerializedValues == null);
            CollectionAssert.AreEqual(source.Keys, dictionary.Select(pair => pair.Key));
        }

        [Test]
        public void CopyFromRespectsCustomComparerOrdering()
        {
            SerializableSortedDictionary<CaseInsensitiveKey, int> dictionary = new();
            Dictionary<CaseInsensitiveKey, int> source = new()
            {
                { new("bravo"), 2 },
                { new("Alpha"), 1 },
                { new("charlie"), 3 },
            };

            dictionary.CopyFrom(source);

            CaseInsensitiveKey[] expectedOrder = { new("Alpha"), new("bravo"), new("charlie") };
            int index = 0;
            foreach (KeyValuePair<CaseInsensitiveKey, int> pair in dictionary)
            {
                Assert.AreEqual(expectedOrder[index].Token, pair.Key.Token);
                index++;
            }

            Assert.AreEqual(expectedOrder.Length, index);
            Assert.AreEqual(1, dictionary[new("alpha")]);
            Assert.IsTrue(dictionary.ContainsKey(new("CHARLIE")));
        }

        [Test]
        public void InspectorSnapshotWithoutDuplicatesPreservesArraysForOrder()
        {
            SerializableSortedDictionary<string, string> dictionary = new();
            string[] serializedKeys = { "alpha", "bravo" };
            string[] serializedValues = { "1", "2" };
            dictionary._keys = serializedKeys;
            dictionary._values = serializedValues;

            dictionary.OnAfterDeserialize();

            Assert.AreEqual(2, dictionary.Count);
            // Arrays are now always preserved after deserialization to maintain user-defined order
            Assert.IsTrue(
                dictionary.PreserveSerializedEntries,
                "Preserve flag should be true after deserialization."
            );
            Assert.IsTrue(
                dictionary.SerializedKeys != null,
                "Serialized keys should be preserved for order maintenance."
            );
            Assert.IsTrue(
                dictionary.SerializedValues != null,
                "Serialized values should be preserved for order maintenance."
            );
        }

        [Test]
        public void OnBeforeSerializePreservesOriginalArraysWhenPreserveFlagSet()
        {
            SerializableSortedDictionary<string, string> dictionary = new();
            string[] serializedKeys = { "dup", "dup" };
            string[] serializedValues = { "old", "new" };
            dictionary._keys = serializedKeys;
            dictionary._values = serializedValues;

            dictionary.OnAfterDeserialize();
            Assert.IsTrue(dictionary.PreserveSerializedEntries);
            Assert.IsTrue(
                dictionary.HasDuplicatesOrNulls,
                "HasDuplicatesOrNulls should be true for duplicate keys."
            );

            dictionary.OnBeforeSerialize();

            // With duplicates, the original arrays should be preserved exactly
            Assert.AreSame(serializedKeys, dictionary.SerializedKeys);
            Assert.AreSame(serializedValues, dictionary.SerializedValues);
            Assert.IsTrue(dictionary.PreserveSerializedEntries);
        }

        [Test]
        public void ToSortedDictionaryReturnsIndependentCopy()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                { 2, "two" },
                { 1, "one" },
            };

            SortedDictionary<int, string> copy = dictionary.ToSortedDictionary();

            Assert.AreEqual(dictionary.Count, copy.Count);
            Assert.AreEqual("two", copy[2]);

            bool firstAssigned = false;
            KeyValuePair<int, string> firstPair = default;
            foreach (KeyValuePair<int, string> pair in copy)
            {
                if (!firstAssigned)
                {
                    firstPair = pair;
                    firstAssigned = true;
                }
            }

            Assert.IsTrue(firstAssigned);
            Assert.AreEqual(1, firstPair.Key);
            Assert.AreEqual("one", firstPair.Value);

            copy.Add(3, "three");
            Assert.IsFalse(dictionary.ContainsKey(3));

            dictionary[2] = "two-updated";
            Assert.AreEqual("two", copy[2]);
        }

        [Test]
        public void TryAddWhenKeyExistsDoesNotInvalidateCache()
        {
            SerializableSortedDictionary<int, string> dictionary = new() { { 5, "five" } };

            dictionary.OnBeforeSerialize();

            int[] serializedKeys = dictionary.SerializedKeys;
            string[] serializedValues = dictionary.SerializedValues;

            Assert.IsTrue(serializedKeys != null, "Serialized keys should be generated.");
            Assert.IsTrue(serializedValues != null, "Serialized values should be generated.");
            Assert.IsFalse(dictionary.SerializationArraysDirty);

            bool added = dictionary.TryAdd(5, "duplicate");

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
            Assert.IsFalse(
                dictionary.SerializationArraysDirty,
                "Failed TryAdd must not mark arrays dirty."
            );
            Assert.AreEqual("five", dictionary[5]);
        }

        [Test]
        public void IndexerUpdateMarksDirtyButPreservesArraysForOrder()
        {
            SerializableSortedDictionary<int, string> dictionary = new() { { 7, "seven" } };

            dictionary.OnBeforeSerialize();

            Assert.IsTrue(dictionary.SerializedKeys != null);
            Assert.IsTrue(dictionary.SerializedValues != null);
            Assert.IsFalse(dictionary.SerializationArraysDirty);

            dictionary[7] = "updated";

            // Arrays are preserved for order maintenance, but dirty flag is set
            Assert.IsTrue(
                dictionary.SerializedKeys != null,
                "Indexer mutations preserve arrays for order maintenance."
            );
            Assert.IsTrue(
                dictionary.SerializedValues != null,
                "Indexer mutations preserve arrays for order maintenance."
            );
            Assert.IsTrue(
                dictionary.SerializationArraysDirty,
                "Indexer mutations must mark arrays dirty."
            );
            Assert.AreEqual("updated", dictionary[7]);

            // After OnBeforeSerialize, the new value should be reflected
            dictionary.OnBeforeSerialize();
            Assert.AreEqual("updated", dictionary.SerializedValues[0]);
        }

        [Test]
        public void OnBeforeSerializeSkipsRebuildWhenCacheFresh()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                { 2, "two" },
                { 4, "four" },
            };

            dictionary.OnBeforeSerialize();

            int[] initialKeys = dictionary.SerializedKeys;
            string[] initialValues = dictionary.SerializedValues;

            Assert.IsTrue(initialKeys != null);
            Assert.IsTrue(initialValues != null);
            Assert.IsFalse(dictionary.SerializationArraysDirty);

            dictionary.OnBeforeSerialize();

            Assert.AreSame(initialKeys, dictionary.SerializedKeys);
            Assert.AreSame(initialValues, dictionary.SerializedValues);
            Assert.IsFalse(dictionary.SerializationArraysDirty);
        }

        [Test]
        public void CopyFromNullThrowsArgumentNullException()
        {
            SerializableSortedDictionary<int, string> dictionary = new();

            Assert.Throws<ArgumentNullException>(() => dictionary.CopyFrom(null));
        }

        [Test]
        public void ProtoSerializationPreservesTemporaryArraysWhenNoDuplicatesExist()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                { 1, "one" },
                { 3, "three" },
            };

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

            SerializableSortedDictionary<int, string> roundTripped = Serializer.ProtoDeserialize<
                SerializableSortedDictionary<int, string>
            >(payload);

            int[] expectedKeys = { 1, 3 };
            int index = 0;
            foreach (KeyValuePair<int, string> pair in roundTripped)
            {
                Assert.Less(index, expectedKeys.Length);
                Assert.AreEqual(expectedKeys[index], pair.Key);
                index++;
            }

            Assert.AreEqual(expectedKeys.Length, index);
        }

        [Test]
        public void OnBeforeSerializeProducesSortedArrays()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                { 10, "ten" },
                { 2, "two" },
                { 7, "seven" },
            };

            dictionary.OnBeforeSerialize();

            int[] serializedKeys = dictionary.SerializedKeys;
            string[] serializedValues = dictionary.SerializedValues;

            int[] expectedKeys = { 2, 7, 10 };
            string[] expectedValues = { "two", "seven", "ten" };

            CollectionAssert.AreEqual(expectedKeys, serializedKeys);
            CollectionAssert.AreEqual(expectedValues, serializedValues);
        }

        [Test]
        public void OnAfterDeserializeSortsEntries()
        {
            SerializableSortedDictionary<int, string> dictionary = new();

            int[] unsortedKeys = { 5, 1, 3 };
            string[] unsortedValues = { "five", "one", "three" };
            dictionary._keys = unsortedKeys;
            dictionary._values = unsortedValues;

            dictionary.OnAfterDeserialize();

            int[] expectedKeys = { 1, 3, 5 };
            string[] expectedValues = { "one", "three", "five" };

            int index = 0;
            foreach (KeyValuePair<int, string> pair in dictionary)
            {
                Assert.Less(index, expectedKeys.Length);
                Assert.AreEqual(expectedKeys[index], pair.Key);
                Assert.AreEqual(expectedValues[index], pair.Value);
                index++;
            }

            Assert.AreEqual(expectedKeys.Length, index);
        }

        [Test]
        public void NullKeysAreSkippedDuringDeserialization()
        {
            SerializableSortedDictionary<string, string> dictionary = new();

            string[] serializedKeys = { null, "valid" };
            string[] serializedValues = { "ignored", "retained" };
            dictionary._keys = serializedKeys;
            dictionary._values = serializedValues;

            LogAssert.Expect(
                LogType.Warning,
                "SerializableSortedDictionary<System.String, System.String> skipped serialized entry at index 0 because the key reference was null."
            );

            dictionary.OnAfterDeserialize();

            Assert.AreEqual(1, dictionary.Count);
            Assert.IsTrue(dictionary.ContainsKey("valid"));
            Assert.AreEqual("retained", dictionary["valid"]);

            string[] storedKeys = dictionary._keys;
            string[] storedValues = dictionary._values;
            bool preserveFlag = dictionary.PreserveSerializedEntries;

            Assert.IsTrue(storedKeys != null);
            CollectionAssert.AreEqual(serializedKeys, storedKeys);
            if (storedValues != null)
            {
                CollectionAssert.AreEqual(serializedValues, storedValues);
            }

            Assert.IsTrue(preserveFlag);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void NullValuesArePreservedDuringDeserialization()
        {
            SerializableSortedDictionary<string, string> dictionary = new();

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

            // Arrays are always preserved to maintain user-defined order
            Assert.IsTrue(storedKeys != null);
            Assert.IsTrue(storedValues != null);
            CollectionAssert.AreEqual(serializedKeys, storedKeys);
            CollectionAssert.AreEqual(serializedValues, storedValues);
            Assert.IsTrue(preserveFlag);
        }

        [Test]
        public void DuplicateSerializedKeysPreserveInspectorCache()
        {
            SerializableSortedDictionary<string, string> dictionary = new();

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
        public void ComparerCollisionsPreserveSerializedCache()
        {
            SerializableSortedDictionary<CaseInsensitiveKey, string> dictionary = new();

            CaseInsensitiveKey loud = new("ALPHA");
            CaseInsensitiveKey quiet = new("alpha");
            dictionary._keys = new[] { loud, quiet };
            dictionary._values = new[] { "upper", "lower" };

            dictionary.OnAfterDeserialize();

            Assert.AreEqual(1, dictionary.Count);
            Assert.AreEqual("lower", dictionary[quiet]);

            CaseInsensitiveKey[] cachedKeys = dictionary.SerializedKeys;
            string[] cachedValues = dictionary.SerializedValues;

            Assert.IsTrue(cachedKeys != null);
            Assert.IsTrue(cachedValues != null);
            Assert.AreSame(dictionary._keys, cachedKeys);
            Assert.AreSame(dictionary._values, cachedValues);
            Assert.AreSame(loud, cachedKeys[0]);
            Assert.AreSame(quiet, cachedKeys[1]);
            CollectionAssert.AreEqual(new[] { "upper", "lower" }, cachedValues);
            Assert.IsTrue(dictionary.PreserveSerializedEntries);
        }

        [Test]
        public void UnitySerializationPreservesUserDefinedOrderAfterDeserialization()
        {
            SerializableSortedDictionary<string, int> dictionary = new();

            string[] serializedKeys = { "delta", "alpha", "charlie" };
            int[] serializedValues = { 3, 1, 2 };
            dictionary._keys = serializedKeys;
            dictionary._values = serializedValues;

            dictionary.OnAfterDeserialize();

            // After deserialization, arrays are preserved to maintain user-defined order
            Assert.IsTrue(
                dictionary._keys != null,
                "Deserialization should preserve serialized keys to maintain user-defined order."
            );
            Assert.IsTrue(
                dictionary._values != null,
                "Deserialization should preserve serialized values to maintain user-defined order."
            );
            Assert.IsTrue(dictionary.PreserveSerializedEntries);

            dictionary.OnBeforeSerialize();

            string[] rebuiltKeys = dictionary._keys;
            int[] rebuiltValues = dictionary._values;

            Assert.IsTrue(rebuiltKeys != null);
            Assert.IsTrue(rebuiltValues != null);
            // Order should be preserved from deserialization, NOT sorted
            CollectionAssert.AreEqual(
                new[] { "delta", "alpha", "charlie" },
                rebuiltKeys,
                "Serialized keys should preserve user-defined order, not sorted order."
            );
            CollectionAssert.AreEqual(
                new[] { 3, 1, 2 },
                rebuiltValues,
                "Serialized values should stay aligned with their original key positions."
            );
        }

        [Test]
        public void JsonRoundTripPreservesArraysAndOrderAfterDeserialization()
        {
            SerializableSortedDictionary<int, string> original = new()
            {
                { 3, "three" },
                { 1, "one" },
                { 2, "two" },
            };

            string json = Serializer.JsonStringify(original);
            SerializableSortedDictionary<int, string> roundTrip = Serializer.JsonDeserialize<
                SerializableSortedDictionary<int, string>
            >(json);

            Assert.AreEqual(3, roundTrip.Count);
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

            // Order should be preserved from the JSON (which came from sorted order originally)
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, roundTrip.SerializedKeys);
            CollectionAssert.AreEqual(new[] { "one", "two", "three" }, roundTrip.SerializedValues);
        }

        [Test]
        public void MismatchedSerializedArraysAreDiscardedAndMarkCacheDirty()
        {
            SerializableSortedDictionary<string, string> dictionary = new()
            {
                _keys = new[] { "alpha" },
                _values = new[] { "one", "extra" },
            };

            dictionary.OnAfterDeserialize();

            Assert.AreEqual(0, dictionary.Count);
            Assert.IsTrue(dictionary.SerializedKeys == null);
            Assert.IsTrue(dictionary.SerializedValues == null);
            Assert.IsTrue(
                dictionary.SerializationArraysDirty,
                "Cache should be marked dirty when serialized arrays are invalid."
            );
        }

        [Test]
        public void ProtoSerializationRoundTrips()
        {
            SerializableSortedDictionary<int, string> original = new()
            {
                { 4, "four" },
                { 2, "two" },
                { 9, "nine" },
            };

            byte[] data = Serializer.ProtoSerialize(original);
            SerializableSortedDictionary<int, string> deserialized = Serializer.ProtoDeserialize<
                SerializableSortedDictionary<int, string>
            >(data);

            int[] expectedKeys = { 2, 4, 9 };
            int index = 0;
            foreach (KeyValuePair<int, string> pair in deserialized)
            {
                Assert.Less(index, expectedKeys.Length);
                Assert.AreEqual(expectedKeys[index], pair.Key);
                index++;
            }

            Assert.AreEqual(expectedKeys.Length, index);
        }

        [Test]
        public void ProtoSerializationRoundTripRestoresInternalArrays()
        {
            // Arrange: Create dictionary with specific entries
            SerializableSortedDictionary<int, string> original = new()
            {
                { 4, "four" },
                { 2, "two" },
                { 9, "nine" },
            };
            original.OnBeforeSerialize();

            // Diagnostic: Verify original state
            Assert.IsTrue(original._keys != null, "Original _keys should not be null");
            Assert.IsTrue(original._values != null, "Original _values should not be null");
            string originalKeysStr = string.Join(", ", original._keys);
            string originalValuesStr = string.Join(", ", original._values);

            // Act: Protobuf round-trip
            byte[] data = Serializer.ProtoSerialize(original);

            Assert.IsTrue(data != null, "Serialized data should not be null");
            Assert.Greater(
                data.Length,
                0,
                $"Serialized data should not be empty. Keys: [{originalKeysStr}], Values: [{originalValuesStr}]"
            );

            string hexDump = string.Join(" ", data.Take(30).Select(b => b.ToString("X2")));

            SerializableSortedDictionary<int, string> deserialized = Serializer.ProtoDeserialize<
                SerializableSortedDictionary<int, string>
            >(data);

            // Assert: Internal arrays should be restored
            Assert.IsTrue(deserialized != null, "Deserialized object should not be null");
            Assert.IsTrue(
                deserialized._keys != null,
                $"Deserialized _keys should not be null. "
                    + $"Original keys: [{originalKeysStr}], Bytes: {data.Length}, Hex: {hexDump}"
            );
            Assert.IsTrue(
                deserialized._values != null,
                $"Deserialized _values should not be null. "
                    + $"Original values: [{originalValuesStr}], Bytes: {data.Length}, Hex: {hexDump}"
            );
            Assert.AreEqual(
                original._keys.Length,
                deserialized._keys.Length,
                "Keys array length should match"
            );
            Assert.AreEqual(
                original._values.Length,
                deserialized._values.Length,
                "Values array length should match"
            );

            // Verify contents
            CollectionAssert.AreEquivalent(
                original._keys,
                deserialized._keys,
                "Keys should contain the same elements"
            );
            CollectionAssert.AreEquivalent(
                original._values,
                deserialized._values,
                "Values should contain the same elements"
            );
        }

        private static IEnumerable<TestCaseData> SortedDictionaryProtoArraysTestCases()
        {
            yield return new TestCaseData(new[] { 1 }, new[] { "one" }).SetName("SingleEntry");
            yield return new TestCaseData(
                new[] { 4, 2, 9, 1 },
                new[] { "four", "two", "nine", "one" }
            ).SetName("MultipleEntries");
            yield return new TestCaseData(new[] { -5 }, new[] { "negative" }).SetName(
                "NegativeKey"
            );
            yield return new TestCaseData(
                new[] { int.MaxValue, int.MinValue, 0 },
                new[] { "max", "min", "zero" }
            ).SetName("ExtremeBoundaryKeys");
        }

        [TestCaseSource(nameof(SortedDictionaryProtoArraysTestCases))]
        public void ProtoSerializationRoundTripRestoresArraysDataDriven(int[] keys, string[] values)
        {
            // Arrange
            SerializableSortedDictionary<int, string> original = new();
            for (int i = 0; i < keys.Length; i++)
            {
                original.Add(keys[i], values[i]);
            }
            original.OnBeforeSerialize();

            // Diagnostic
            Assert.IsTrue(original._keys != null, "Original _keys should not be null");
            Assert.IsTrue(original._values != null, "Original _values should not be null");
            Assert.AreEqual(keys.Length, original._keys.Length, "Original _keys length mismatch");
            Assert.AreEqual(
                values.Length,
                original._values.Length,
                "Original _values length mismatch"
            );

            // Act
            byte[] data = Serializer.ProtoSerialize(original);
            Assert.Greater(data.Length, 0, "Serialized data should not be empty");

            string hexDump = string.Join(" ", data.Take(30).Select(b => b.ToString("X2")));

            SerializableSortedDictionary<int, string> deserialized = Serializer.ProtoDeserialize<
                SerializableSortedDictionary<int, string>
            >(data);

            // Assert
            Assert.IsTrue(deserialized != null, "Deserialized object should not be null");
            Assert.IsTrue(
                deserialized._keys != null,
                $"Deserialized _keys should not be null. "
                    + $"Input keys: [{string.Join(", ", keys)}], Bytes: {data.Length}, Hex: {hexDump}"
            );
            Assert.IsTrue(
                deserialized._values != null,
                $"Deserialized _values should not be null. "
                    + $"Input values: [{string.Join(", ", values)}], Bytes: {data.Length}, Hex: {hexDump}"
            );
            Assert.AreEqual(keys.Length, deserialized._keys.Length, "Keys length should match");
            Assert.AreEqual(
                values.Length,
                deserialized._values.Length,
                "Values length should match"
            );
            CollectionAssert.AreEquivalent(keys, deserialized._keys, "Keys content should match");
            CollectionAssert.AreEquivalent(
                values,
                deserialized._values,
                "Values content should match"
            );
        }

        private sealed class CaseInsensitiveKey : IComparable<CaseInsensitiveKey>, IComparable
        {
            public CaseInsensitiveKey(string token)
            {
                Token = token;
            }

            public string Token { get; }

            public int CompareTo(CaseInsensitiveKey other)
            {
                return other == null
                    ? 1
                    : string.Compare(Token, other.Token, StringComparison.OrdinalIgnoreCase);
            }

            int IComparable.CompareTo(object obj)
            {
                if (ReferenceEquals(this, obj))
                {
                    return 0;
                }

                if (obj is CaseInsensitiveKey candidate)
                {
                    return CompareTo(candidate);
                }

                return 1;
            }

            public override bool Equals(object obj)
            {
                if (obj is CaseInsensitiveKey other)
                {
                    return string.Equals(Token, other.Token, StringComparison.OrdinalIgnoreCase);
                }

                return false;
            }

            public override int GetHashCode()
            {
                if (Token == null)
                {
                    return 0;
                }

                return StringComparer.OrdinalIgnoreCase.GetHashCode(Token);
            }

            public override string ToString()
            {
                return Token ?? string.Empty;
            }
        }

        [Test]
        public void ToKeysArrayReturnsEmptyArrayForEmptyDictionary()
        {
            SerializableSortedDictionary<string, int> dictionary = new();

            string[] result = dictionary.ToKeysArray();

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Length);
            Assert.AreSame(Array.Empty<string>(), result);
        }

        [Test]
        public void ToValuesArrayReturnsEmptyArrayForEmptyDictionary()
        {
            SerializableSortedDictionary<string, int> dictionary = new();

            int[] result = dictionary.ToValuesArray();

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Length);
            Assert.AreSame(Array.Empty<int>(), result);
        }

        [Test]
        public void ToArrayReturnsEmptyArrayForEmptyDictionary()
        {
            SerializableSortedDictionary<string, int> dictionary = new();

            KeyValuePair<string, int>[] result = dictionary.ToArray();

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Length);
            Assert.AreSame(Array.Empty<KeyValuePair<string, int>>(), result);
        }

        [Test]
        public void ToKeysArrayReturnsDefensiveCopy()
        {
            SerializableSortedDictionary<string, int> dictionary = new() { { "a", 1 }, { "b", 2 } };
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
            SerializableSortedDictionary<string, int> dictionary = new() { { "a", 1 }, { "b", 2 } };
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
            SerializableSortedDictionary<string, int> dictionary = new() { { "a", 1 }, { "b", 2 } };
            dictionary.OnBeforeSerialize();

            KeyValuePair<string, int>[] firstCopy = dictionary.ToArray();
            KeyValuePair<string, int>[] secondCopy = dictionary.ToArray();

            Assert.AreNotSame(firstCopy, secondCopy);
        }

        [Test]
        public void ToKeysArrayReturnsSortedOrder()
        {
            SerializableSortedDictionary<string, int> dictionary = new();
            string[] userOrder = { "zebra", "alpha", "mango" };
            int[] values = { 1, 2, 3 };
            dictionary._keys = (string[])userOrder.Clone();
            dictionary._values = (int[])values.Clone();
            dictionary.OnAfterDeserialize();

            string[] result = dictionary.ToKeysArray();

            // ToKeysArray should return sorted order, not user-defined order
            CollectionAssert.AreEqual(new[] { "alpha", "mango", "zebra" }, result);
        }

        [Test]
        public void ToPersistedOrderKeysArrayPreservesUserDefinedOrder()
        {
            SerializableSortedDictionary<string, int> dictionary = new();
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
        public void ToValuesArrayReturnsSortedKeyOrder()
        {
            SerializableSortedDictionary<string, int> dictionary = new();
            string[] keys = { "zebra", "alpha", "mango" };
            int[] userOrder = { 100, 200, 300 };
            dictionary._keys = (string[])keys.Clone();
            dictionary._values = (int[])userOrder.Clone();
            dictionary.OnAfterDeserialize();

            int[] result = dictionary.ToValuesArray();

            // ToValuesArray should return values in sorted key order: alpha=200, mango=300, zebra=100
            CollectionAssert.AreEqual(new[] { 200, 300, 100 }, result);
        }

        [Test]
        public void ToPersistedOrderValuesArrayPreservesUserDefinedOrder()
        {
            SerializableSortedDictionary<string, int> dictionary = new();
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
        public void ToArrayReturnsSortedOrder()
        {
            SerializableSortedDictionary<string, int> dictionary = new();
            string[] keys = { "zebra", "alpha", "mango" };
            int[] values = { 1, 2, 3 };
            dictionary._keys = (string[])keys.Clone();
            dictionary._values = (int[])values.Clone();
            dictionary.OnAfterDeserialize();

            KeyValuePair<string, int>[] result = dictionary.ToArray();

            // ToArray should return sorted key order: alpha, mango, zebra
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual("alpha", result[0].Key);
            Assert.AreEqual(2, result[0].Value);
            Assert.AreEqual("mango", result[1].Key);
            Assert.AreEqual(3, result[1].Value);
            Assert.AreEqual("zebra", result[2].Key);
            Assert.AreEqual(1, result[2].Value);
        }

        [Test]
        public void ToPersistedOrderArrayPreservesUserDefinedOrder()
        {
            SerializableSortedDictionary<string, int> dictionary = new();
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
            SerializableSortedDictionary<string, int> dictionary = new()
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
        public void ToArrayReflectsCurrentStateAfterMutations()
        {
            SerializableSortedDictionary<string, int> dictionary = new()
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
            SerializableSortedDictionary<string, int> dictionary = new();
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

            CollectionAssert.AreEqual(userOrder, resultKeys);
            CollectionAssert.AreEqual(values, resultValues);
        }

        [Test]
        public void ToArrayReturnsSortedOrderAfterMultipleSerializationCycles()
        {
            SerializableSortedDictionary<string, int> dictionary = new();
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

            // ToKeysArray/ToValuesArray should return sorted order
            CollectionAssert.AreEqual(new[] { "alpha", "mango", "zebra" }, resultKeys);
            CollectionAssert.AreEqual(new[] { 200, 300, 100 }, resultValues);
        }

        [Test]
        public void ToArrayWithDuplicateKeysInSerializedDataHandlesGracefully()
        {
            SerializableSortedDictionary<string, int> dictionary = new();
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
            SerializableSortedDictionary<int, string> dictionary = new();
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
            SerializableSortedDictionary<int, string> dictionary = new()
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
                    string[] valuesArray = dictionary.ToValuesArray();
                    int keyIndex = Array.IndexOf(dictionary.ToKeysArray(), 1);
                    Assert.AreEqual("modified", valuesArray[keyIndex]);
                    break;
            }
        }

        [Test]
        public void ToPersistedOrderArrayPreservesInsertionOrderForNewKeys()
        {
            SerializableSortedDictionary<int, string> dictionary = new();
            int[] existingKeys = { 1, 2, 3 };
            string[] existingValues = { "one", "two", "three" };
            dictionary._keys = (int[])existingKeys.Clone();
            dictionary._values = (string[])existingValues.Clone();
            dictionary.OnAfterDeserialize();

            dictionary[4] = "four";
            dictionary[5] = "five";

            int[] resultKeys = dictionary.ToPersistedOrderKeysArray();

            Assert.AreEqual(5, resultKeys.Length);
            for (int i = 0; i < existingKeys.Length; i++)
            {
                Assert.AreEqual(existingKeys[i], resultKeys[i]);
            }

            Assert.AreEqual(4, resultKeys[3]);
            Assert.AreEqual(5, resultKeys[4]);
        }

        [Test]
        public void ToKeysArrayReturnsSortedOrderAfterAddingNewKeys()
        {
            SerializableSortedDictionary<int, string> dictionary = new();
            int[] existingKeys = { 3, 1, 5 };
            string[] existingValues = { "three", "one", "five" };
            dictionary._keys = (int[])existingKeys.Clone();
            dictionary._values = (string[])existingValues.Clone();
            dictionary.OnAfterDeserialize();

            dictionary[2] = "two";
            dictionary[4] = "four";

            int[] resultKeys = dictionary.ToKeysArray();

            // ToKeysArray should always return sorted order
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, resultKeys);
        }

        [TestCase("IndexerUpdateExisting", Description = "Updating existing key value via indexer")]
        [TestCase("IndexerAddNew", Description = "Adding new key via indexer")]
        [TestCase("AddMethod", Description = "Adding via Add method")]
        [TestCase("TryAddMethod", Description = "Adding via TryAdd method")]
        [TestCase("RemoveMethod", Description = "Removing via Remove method")]
        [TestCase("ClearMethod", Description = "Clearing the dictionary")]
        public void PreserveSerializedEntriesFlagBehaviorAfterMutation(string mutationType)
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                { 1, "one" },
                { 2, "two" },
            };
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

            bool arraysDirtyAfter = dictionary.SerializationArraysDirty;
            Assert.IsTrue(
                arraysDirtyAfter,
                $"SerializationArraysDirty should be true after {mutationType} mutation"
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
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                { 1, "one" },
                { 2, "two" },
            };
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
            SerializableSortedDictionary<string, int> dictionary = new()
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
                + $"PreserveSerializedEntries: {dictionary.PreserveSerializedEntries}, ArraysDirty: {dictionary.SerializationArraysDirty}";

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
            SerializableSortedDictionary<string, string> dictionary = new();
            string[] keysWithDuplicates = { "dup", "dup", "unique" };
            string[] values = { "first", "second", "third" };
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
        public void ToArrayReturnsEmptyForEmptyDictionary()
        {
            SerializableSortedDictionary<int, string> dictionary = new();

            KeyValuePair<int, string>[] result = dictionary.ToArray();

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void ToPersistedOrderArrayReturnsEmptyForEmptyDictionary()
        {
            SerializableSortedDictionary<int, string> dictionary = new();

            KeyValuePair<int, string>[] result = dictionary.ToPersistedOrderArray();

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void ToPersistedOrderKeysArrayReturnsEmptyForEmptyDictionary()
        {
            SerializableSortedDictionary<string, int> dictionary = new();

            string[] result = dictionary.ToPersistedOrderKeysArray();

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void ToPersistedOrderValuesArrayReturnsEmptyForEmptyDictionary()
        {
            SerializableSortedDictionary<string, int> dictionary = new();

            int[] result = dictionary.ToPersistedOrderValuesArray();

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void ToArrayReturnsSortedOrderForFreshlyCreatedDictionary()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                { 5, "five" },
                { 1, "one" },
                { 3, "three" },
            };

            KeyValuePair<int, string>[] result = dictionary.ToArray();

            Assert.AreEqual(3, result.Length);
            Assert.AreEqual(1, result[0].Key);
            Assert.AreEqual("one", result[0].Value);
            Assert.AreEqual(3, result[1].Key);
            Assert.AreEqual("three", result[1].Value);
            Assert.AreEqual(5, result[2].Key);
            Assert.AreEqual("five", result[2].Value);
        }

        [Test]
        public void ToKeysArrayReturnsSortedOrderForFreshlyCreatedDictionary()
        {
            SerializableSortedDictionary<string, int> dictionary = new()
            {
                { "zebra", 1 },
                { "apple", 2 },
                { "mango", 3 },
            };

            string[] result = dictionary.ToKeysArray();

            CollectionAssert.AreEqual(new[] { "apple", "mango", "zebra" }, result);
        }

        [Test]
        public void ToValuesArrayReturnsSortedKeyOrderForFreshlyCreatedDictionary()
        {
            SerializableSortedDictionary<string, int> dictionary = new()
            {
                { "zebra", 1 },
                { "apple", 2 },
                { "mango", 3 },
            };

            int[] result = dictionary.ToValuesArray();

            // Values in sorted key order: apple=2, mango=3, zebra=1
            CollectionAssert.AreEqual(new[] { 2, 3, 1 }, result);
        }

        [Test]
        public void ToPersistedOrderArrayReturnsDefensiveCopy()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                { 1, "one" },
                { 2, "two" },
            };
            dictionary.OnBeforeSerialize();

            KeyValuePair<int, string>[] firstCopy = dictionary.ToPersistedOrderArray();
            KeyValuePair<int, string>[] secondCopy = dictionary.ToPersistedOrderArray();

            Assert.AreNotSame(firstCopy, secondCopy);
        }

        [Test]
        public void ToPersistedOrderKeysArrayReturnsDefensiveCopy()
        {
            SerializableSortedDictionary<string, int> dictionary = new() { { "a", 1 }, { "b", 2 } };
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
            SerializableSortedDictionary<string, int> dictionary = new() { { "a", 1 }, { "b", 2 } };
            dictionary.OnBeforeSerialize();

            int[] firstCopy = dictionary.ToPersistedOrderValuesArray();
            int[] secondCopy = dictionary.ToPersistedOrderValuesArray();

            Assert.AreNotSame(firstCopy, secondCopy);

            firstCopy[0] = 999;
            Assert.AreNotEqual(999, dictionary.ToPersistedOrderValuesArray()[0]);
        }

        [Test]
        public void ToArrayAndToPersistedOrderArrayAreDifferentAfterDeserialization()
        {
            SerializableSortedDictionary<string, int> dictionary = new();
            string[] unsortedKeys = { "charlie", "alpha", "bravo" };
            int[] values = { 1, 2, 3 };
            dictionary._keys = (string[])unsortedKeys.Clone();
            dictionary._values = (int[])values.Clone();
            dictionary.OnAfterDeserialize();

            KeyValuePair<string, int>[] sortedResult = dictionary.ToArray();
            KeyValuePair<string, int>[] persistedResult = dictionary.ToPersistedOrderArray();

            // ToArray should be sorted
            Assert.AreEqual("alpha", sortedResult[0].Key);
            Assert.AreEqual("bravo", sortedResult[1].Key);
            Assert.AreEqual("charlie", sortedResult[2].Key);

            // ToPersistedOrderArray should preserve original order
            Assert.AreEqual("charlie", persistedResult[0].Key);
            Assert.AreEqual("alpha", persistedResult[1].Key);
            Assert.AreEqual("bravo", persistedResult[2].Key);
        }

        [Test]
        public void ToKeysArrayAndToPersistedOrderKeysArrayAreDifferentAfterDeserialization()
        {
            SerializableSortedDictionary<int, string> dictionary = new();
            int[] unsortedKeys = { 5, 1, 3 };
            string[] values = { "five", "one", "three" };
            dictionary._keys = (int[])unsortedKeys.Clone();
            dictionary._values = (string[])values.Clone();
            dictionary.OnAfterDeserialize();

            int[] sortedResult = dictionary.ToKeysArray();
            int[] persistedResult = dictionary.ToPersistedOrderKeysArray();

            // ToKeysArray should be sorted
            CollectionAssert.AreEqual(new[] { 1, 3, 5 }, sortedResult);

            // ToPersistedOrderKeysArray should preserve original order
            CollectionAssert.AreEqual(unsortedKeys, persistedResult);
        }

        [Test]
        public void ToValuesArrayAndToPersistedOrderValuesArrayAreDifferentAfterDeserialization()
        {
            SerializableSortedDictionary<int, string> dictionary = new();
            int[] unsortedKeys = { 5, 1, 3 };
            string[] values = { "five", "one", "three" };
            dictionary._keys = (int[])unsortedKeys.Clone();
            dictionary._values = (string[])values.Clone();
            dictionary.OnAfterDeserialize();

            string[] sortedResult = dictionary.ToValuesArray();
            string[] persistedResult = dictionary.ToPersistedOrderValuesArray();

            // ToValuesArray should return values in sorted key order: 1="one", 3="three", 5="five"
            CollectionAssert.AreEqual(new[] { "one", "three", "five" }, sortedResult);

            // ToPersistedOrderValuesArray should preserve original order
            CollectionAssert.AreEqual(values, persistedResult);
        }

        [TestCase(1)]
        [TestCase(10)]
        [TestCase(100)]
        public void ToArrayConsistentWithEnumerationForVariousSizes(int size)
        {
            SerializableSortedDictionary<int, string> dictionary = new();
            for (int i = size - 1; i >= 0; i--)
            {
                dictionary[i] = $"value_{i}";
            }

            KeyValuePair<int, string>[] toArrayResult = dictionary.ToArray();
            List<KeyValuePair<int, string>> enumerationResult = new();
            foreach (KeyValuePair<int, string> pair in dictionary)
            {
                enumerationResult.Add(pair);
            }

            Assert.AreEqual(size, toArrayResult.Length);
            Assert.AreEqual(size, enumerationResult.Count);
            for (int i = 0; i < size; i++)
            {
                Assert.AreEqual(enumerationResult[i].Key, toArrayResult[i].Key);
                Assert.AreEqual(enumerationResult[i].Value, toArrayResult[i].Value);
            }
        }

        [Test]
        public void ToArrayMatchesKeysPropertyOrder()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                { 10, "ten" },
                { 1, "one" },
                { 5, "five" },
            };

            int[] toKeysResult = dictionary.ToKeysArray();
            int[] keysPropertyResult = dictionary.Keys.ToArray();

            CollectionAssert.AreEqual(keysPropertyResult, toKeysResult);
        }

        [Test]
        public void ToArrayMatchesValuesPropertyOrder()
        {
            SerializableSortedDictionary<int, string> dictionary = new()
            {
                { 10, "ten" },
                { 1, "one" },
                { 5, "five" },
            };

            string[] toValuesResult = dictionary.ToValuesArray();
            string[] valuesPropertyResult = dictionary.Values.ToArray();

            CollectionAssert.AreEqual(valuesPropertyResult, toValuesResult);
        }

        [Test]
        public void ToPersistedOrderArrayReflectsRuntimeMutations()
        {
            SerializableSortedDictionary<string, int> dictionary = new();
            string[] initialKeys = { "zebra", "alpha" };
            int[] initialValues = { 1, 2 };
            dictionary._keys = (string[])initialKeys.Clone();
            dictionary._values = (int[])initialValues.Clone();
            dictionary.OnAfterDeserialize();

            dictionary["mango"] = 3;

            KeyValuePair<string, int>[] result = dictionary.ToPersistedOrderArray();

            Assert.AreEqual(3, result.Length);
            // Original entries should preserve their order
            Assert.AreEqual("zebra", result[0].Key);
            Assert.AreEqual("alpha", result[1].Key);
            // New entry should be appended
            Assert.AreEqual("mango", result[2].Key);
        }

        [Test]
        public void ToArrayAlwaysSortedEvenAfterMutations()
        {
            SerializableSortedDictionary<string, int> dictionary = new();
            string[] initialKeys = { "zebra", "alpha" };
            int[] initialValues = { 1, 2 };
            dictionary._keys = (string[])initialKeys.Clone();
            dictionary._values = (int[])initialValues.Clone();
            dictionary.OnAfterDeserialize();

            dictionary["mango"] = 3;

            KeyValuePair<string, int>[] result = dictionary.ToArray();

            // Should always be sorted regardless of mutations
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual("alpha", result[0].Key);
            Assert.AreEqual("mango", result[1].Key);
            Assert.AreEqual("zebra", result[2].Key);
        }

        [Test]
        public void ToPersistedOrderArrayAlignedWithToPersistedOrderKeysAndValuesArrays()
        {
            SerializableSortedDictionary<string, int> dictionary = new();
            string[] keys = { "delta", "alpha", "charlie" };
            int[] values = { 1, 2, 3 };
            dictionary._keys = (string[])keys.Clone();
            dictionary._values = (int[])values.Clone();
            dictionary.OnAfterDeserialize();

            KeyValuePair<string, int>[] pairs = dictionary.ToPersistedOrderArray();
            string[] keysArray = dictionary.ToPersistedOrderKeysArray();
            int[] valuesArray = dictionary.ToPersistedOrderValuesArray();

            Assert.AreEqual(pairs.Length, keysArray.Length);
            Assert.AreEqual(pairs.Length, valuesArray.Length);

            for (int i = 0; i < pairs.Length; i++)
            {
                Assert.AreEqual(keysArray[i], pairs[i].Key);
                Assert.AreEqual(valuesArray[i], pairs[i].Value);
            }
        }

        [Test]
        public void ToArrayAlignedWithToKeysAndValuesArrays()
        {
            SerializableSortedDictionary<string, int> dictionary = new()
            {
                { "delta", 1 },
                { "alpha", 2 },
                { "charlie", 3 },
            };

            KeyValuePair<string, int>[] pairs = dictionary.ToArray();
            string[] keysArray = dictionary.ToKeysArray();
            int[] valuesArray = dictionary.ToValuesArray();

            Assert.AreEqual(pairs.Length, keysArray.Length);
            Assert.AreEqual(pairs.Length, valuesArray.Length);

            for (int i = 0; i < pairs.Length; i++)
            {
                Assert.AreEqual(keysArray[i], pairs[i].Key);
                Assert.AreEqual(valuesArray[i], pairs[i].Value);
            }
        }

        [Test]
        public void ProtoRoundTripPreservesSortedOrderInToArray()
        {
            SerializableSortedDictionary<int, string> original = new()
            {
                { 5, "five" },
                { 1, "one" },
                { 3, "three" },
            };

            byte[] payload = Serializer.ProtoSerialize(original);
            SerializableSortedDictionary<int, string> roundTrip = Serializer.ProtoDeserialize<
                SerializableSortedDictionary<int, string>
            >(payload);

            KeyValuePair<int, string>[] result = roundTrip.ToArray();

            Assert.AreEqual(3, result.Length);
            Assert.AreEqual(1, result[0].Key);
            Assert.AreEqual(3, result[1].Key);
            Assert.AreEqual(5, result[2].Key);
        }

        [Test]
        public void JsonRoundTripPreservesSortedOrderInToArray()
        {
            SerializableSortedDictionary<int, string> original = new()
            {
                { 5, "five" },
                { 1, "one" },
                { 3, "three" },
            };

            string json = Serializer.JsonStringify(original);
            SerializableSortedDictionary<int, string> roundTrip = Serializer.JsonDeserialize<
                SerializableSortedDictionary<int, string>
            >(json);

            KeyValuePair<int, string>[] result = roundTrip.ToArray();

            Assert.AreEqual(3, result.Length);
            Assert.AreEqual(1, result[0].Key);
            Assert.AreEqual(3, result[1].Key);
            Assert.AreEqual(5, result[2].Key);
        }

        [Test]
        public void SingleElementDictionaryToArrayReturnsSingleElement()
        {
            SerializableSortedDictionary<string, int> dictionary = new() { { "only", 42 } };

            KeyValuePair<string, int>[] result = dictionary.ToArray();

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("only", result[0].Key);
            Assert.AreEqual(42, result[0].Value);
        }

        [Test]
        public void SingleElementDictionaryToPersistedOrderArrayReturnsSingleElement()
        {
            SerializableSortedDictionary<string, int> dictionary = new() { { "only", 42 } };
            dictionary.OnBeforeSerialize();

            KeyValuePair<string, int>[] result = dictionary.ToPersistedOrderArray();

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("only", result[0].Key);
            Assert.AreEqual(42, result[0].Value);
        }
    }
}
