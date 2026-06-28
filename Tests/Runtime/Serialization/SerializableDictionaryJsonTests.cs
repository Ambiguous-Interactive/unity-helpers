// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using System.Text.Json;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
    public sealed class SerializableDictionaryJsonTests
    {
        [Test]
        public void SerializableDictionaryRoundTripsJson()
        {
            SerializableDictionary<string, int> original = new()
            {
                { "alpha", 1 },
                { "beta", 2 },
                { "gamma", 3 },
            };

            string json = Serializer.JsonStringify(original);
            SerializableDictionary<string, int> deserialized = Serializer.JsonDeserialize<
                SerializableDictionary<string, int>
            >(json);

            Assert.AreEqual(original.Count, deserialized.Count);
            foreach (KeyValuePair<string, int> pair in original)
            {
                Assert.IsTrue(deserialized.TryGetValue(pair.Key, out int value), pair.Key);
                Assert.AreEqual(pair.Value, value, pair.Key);
            }
        }

        [Test]
        public void SerializableDictionaryRoundTripsJsonWithFastOptions()
        {
            SerializableDictionary<int, string> original = new()
            {
                { 1, "one" },
                { 2, "two" },
                { 3, "three" },
            };

            JsonSerializerOptions options = Serializer.CreateFastJsonOptions();
            string json = Serializer.JsonStringify(original, options);
            SerializableDictionary<int, string> deserialized = Serializer.JsonDeserialize<
                SerializableDictionary<int, string>
            >(json, null, options);

            Assert.AreEqual(original.Count, deserialized.Count);
            foreach (KeyValuePair<int, string> pair in original)
            {
                Assert.IsTrue(deserialized.TryGetValue(pair.Key, out string value));
                Assert.AreEqual(pair.Value, value);
            }
        }

        [Test]
        public void SerializableDictionaryCacheRoundTripsJson()
        {
            SerializableDictionary<
                string,
                SerializablePayload,
                SerializableDictionary.Cache<SerializablePayload>
            > original = new()
            {
                {
                    "primary",
                    new SerializablePayload { Id = 1, Name = "Primary" }
                },
                {
                    "secondary",
                    new SerializablePayload { Id = 2, Name = "Secondary" }
                },
            };

            JsonSerializerOptions options = Serializer.CreatePrettyJsonOptions();
            string json = Serializer.JsonStringify(original, options);
            SerializableDictionary<
                string,
                SerializablePayload,
                SerializableDictionary.Cache<SerializablePayload>
            > deserialized = Serializer.JsonDeserialize<
                SerializableDictionary<
                    string,
                    SerializablePayload,
                    SerializableDictionary.Cache<SerializablePayload>
                >
            >(json, null, options);

            Assert.AreEqual(original.Count, deserialized.Count);
            foreach (KeyValuePair<string, SerializablePayload> pair in original)
            {
                Assert.IsTrue(deserialized.TryGetValue(pair.Key, out SerializablePayload value));
                Assert.AreEqual(pair.Value.Id, value.Id);
                Assert.AreEqual(pair.Value.Name, value.Name);
            }
        }

        [Test]
        public void SerializableSortedDictionaryRoundTripsJson()
        {
            SerializableSortedDictionary<int, string> original = new()
            {
                { 5, "five" },
                { 1, "one" },
                { 3, "three" },
            };

            string json = Serializer.JsonStringify(original);
            SerializableSortedDictionary<int, string> deserialized = Serializer.JsonDeserialize<
                SerializableSortedDictionary<int, string>
            >(json);

            int[] expectedKeys = { 1, 3, 5 };
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
        public void SerializableSortedDictionaryCacheRoundTripsJson()
        {
            SerializableSortedDictionary<
                string,
                SerializablePayload,
                SerializableDictionary.Cache<SerializablePayload>
            > original = new()
            {
                {
                    "delta",
                    new SerializablePayload { Id = 4, Name = "Delta" }
                },
                {
                    "alpha",
                    new SerializablePayload { Id = 1, Name = "Alpha" }
                },
                {
                    "charlie",
                    new SerializablePayload { Id = 3, Name = "Charlie" }
                },
            };

            JsonSerializerOptions options = Serializer.CreatePrettyJsonOptions();
            string json = Serializer.JsonStringify(original, options);
            SerializableSortedDictionary<
                string,
                SerializablePayload,
                SerializableDictionary.Cache<SerializablePayload>
            > deserialized = Serializer.JsonDeserialize<
                SerializableSortedDictionary<
                    string,
                    SerializablePayload,
                    SerializableDictionary.Cache<SerializablePayload>
                >
            >(json, null, options);

            string[] expectedKeys = { "alpha", "charlie", "delta" };
            int index = 0;
            foreach (KeyValuePair<string, SerializablePayload> pair in deserialized)
            {
                Assert.Less(index, expectedKeys.Length);
                Assert.AreEqual(expectedKeys[index], pair.Key);
                index++;
            }

            Assert.AreEqual(expectedKeys.Length, index);
        }

        [Test]
        public void SerializableDictionaryHandlesMultipleJsonMutations()
        {
            SerializableDictionary<string, int> dictionary = new()
            {
                { "alpha", 1 },
                { "beta", 2 },
            };

            string firstSnapshot = Serializer.JsonStringify(dictionary);

            dictionary["alpha"] = 10;
            dictionary.Add("gamma", 3);
            dictionary.Remove("beta");

            string secondSnapshot = Serializer.JsonStringify(dictionary);

            dictionary.Clear();
            dictionary.Add("delta", 4);

            string thirdSnapshot = Serializer.JsonStringify(dictionary);

            SerializableDictionary<string, int> firstRoundTrip = Serializer.JsonDeserialize<
                SerializableDictionary<string, int>
            >(firstSnapshot);
            SerializableDictionary<string, int> secondRoundTrip = Serializer.JsonDeserialize<
                SerializableDictionary<string, int>
            >(secondSnapshot);
            SerializableDictionary<string, int> thirdRoundTrip = Serializer.JsonDeserialize<
                SerializableDictionary<string, int>
            >(thirdSnapshot);

            Assert.That(firstRoundTrip.Count, Is.EqualTo(2));
            Assert.That(firstRoundTrip["alpha"], Is.EqualTo(1));
            Assert.That(firstRoundTrip["beta"], Is.EqualTo(2));

            Assert.That(secondRoundTrip.Count, Is.EqualTo(2));
            Assert.IsTrue(secondRoundTrip.ContainsKey("alpha"));
            Assert.IsTrue(secondRoundTrip.ContainsKey("gamma"));
            Assert.IsFalse(secondRoundTrip.ContainsKey("beta"));
            Assert.That(secondRoundTrip["alpha"], Is.EqualTo(10));
            Assert.That(secondRoundTrip["gamma"], Is.EqualTo(3));

            Assert.That(thirdRoundTrip.Count, Is.EqualTo(1));
            Assert.That(thirdRoundTrip["delta"], Is.EqualTo(4));

            secondRoundTrip["alpha"] = 42;
            secondRoundTrip.Add("epsilon", 5);

            string fourthSnapshot = Serializer.JsonStringify(secondRoundTrip);
            SerializableDictionary<string, int> fourthRoundTrip = Serializer.JsonDeserialize<
                SerializableDictionary<string, int>
            >(fourthSnapshot);

            Assert.That(fourthRoundTrip.Count, Is.EqualTo(3));
            Assert.That(fourthRoundTrip["alpha"], Is.EqualTo(42));
            Assert.That(fourthRoundTrip["epsilon"], Is.EqualTo(5));
            Assert.That(fourthRoundTrip["gamma"], Is.EqualTo(3));
        }

        [Test]
        public void CacheDictionaryHandlesMultipleJsonMutations()
        {
            SerializableDictionary<
                string,
                SerializablePayload,
                SerializableDictionary.Cache<SerializablePayload>
            > dictionary = new()
            {
                {
                    "primary",
                    new SerializablePayload { Id = 1, Name = "Primary" }
                },
                {
                    "secondary",
                    new SerializablePayload { Id = 2, Name = "Secondary" }
                },
            };

            JsonSerializerOptions options = Serializer.CreateNormalJsonOptions();
            string firstSnapshot = Serializer.JsonStringify(dictionary, options);

            dictionary["primary"] = new SerializablePayload { Id = 11, Name = "Primary Updated" };
            dictionary.Remove("secondary");
            dictionary.Add("tertiary", new SerializablePayload { Id = 3, Name = "Tertiary" });

            string secondSnapshot = Serializer.JsonStringify(dictionary, options);

            dictionary.Clear();
            dictionary.Add("quaternary", new SerializablePayload { Id = 4, Name = "Quaternary" });

            string thirdSnapshot = Serializer.JsonStringify(dictionary, options);

            SerializableDictionary<
                string,
                SerializablePayload,
                SerializableDictionary.Cache<SerializablePayload>
            > firstRoundTrip = Serializer.JsonDeserialize<
                SerializableDictionary<
                    string,
                    SerializablePayload,
                    SerializableDictionary.Cache<SerializablePayload>
                >
            >(firstSnapshot, null, options);

            SerializableDictionary<
                string,
                SerializablePayload,
                SerializableDictionary.Cache<SerializablePayload>
            > secondRoundTrip = Serializer.JsonDeserialize<
                SerializableDictionary<
                    string,
                    SerializablePayload,
                    SerializableDictionary.Cache<SerializablePayload>
                >
            >(secondSnapshot, null, options);

            SerializableDictionary<
                string,
                SerializablePayload,
                SerializableDictionary.Cache<SerializablePayload>
            > thirdRoundTrip = Serializer.JsonDeserialize<
                SerializableDictionary<
                    string,
                    SerializablePayload,
                    SerializableDictionary.Cache<SerializablePayload>
                >
            >(thirdSnapshot, null, options);

            Assert.That(firstRoundTrip.Count, Is.EqualTo(2));
            Assert.That(firstRoundTrip["primary"].Name, Is.EqualTo("Primary"));
            Assert.That(firstRoundTrip["secondary"].Name, Is.EqualTo("Secondary"));

            Assert.That(secondRoundTrip.Count, Is.EqualTo(2));
            Assert.That(secondRoundTrip["primary"].Name, Is.EqualTo("Primary Updated"));
            Assert.That(secondRoundTrip["tertiary"].Name, Is.EqualTo("Tertiary"));

            Assert.That(thirdRoundTrip.Count, Is.EqualTo(1));
            Assert.That(thirdRoundTrip["quaternary"].Name, Is.EqualTo("Quaternary"));

            secondRoundTrip["tertiary"] = new SerializablePayload
            {
                Id = 33,
                Name = "Tertiary Updated",
            };
            secondRoundTrip.Add("extra", new SerializablePayload { Id = 99, Name = "Extra" });

            string finalSnapshot = Serializer.JsonStringify(secondRoundTrip, options);
            SerializableDictionary<
                string,
                SerializablePayload,
                SerializableDictionary.Cache<SerializablePayload>
            > finalRoundTrip = Serializer.JsonDeserialize<
                SerializableDictionary<
                    string,
                    SerializablePayload,
                    SerializableDictionary.Cache<SerializablePayload>
                >
            >(finalSnapshot, null, options);

            Assert.That(finalRoundTrip.Count, Is.EqualTo(3));
            Assert.That(finalRoundTrip["primary"].Name, Is.EqualTo("Primary Updated"));
            Assert.That(finalRoundTrip["tertiary"].Name, Is.EqualTo("Tertiary Updated"));
            Assert.That(finalRoundTrip["extra"].Name, Is.EqualTo("Extra"));
        }

        [ProtoContract]
        private sealed class SerializablePayload
        {
            [ProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            public string Name { get; set; }
        }
    }
}
