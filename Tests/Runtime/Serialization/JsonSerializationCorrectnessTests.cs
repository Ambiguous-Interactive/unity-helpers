// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class JsonSerializationCorrectnessTests
    {
        private sealed class SimpleMessage
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public List<int> Values { get; set; }
        }

        private sealed class NumericBoundariesMessage
        {
            public int IntMin { get; set; }
            public int IntMax { get; set; }
            public long LongMin { get; set; }
            public long LongMax { get; set; }
            public float FloatMin { get; set; }
            public float FloatMax { get; set; }
            public double DoubleMin { get; set; }
            public double DoubleMax { get; set; }
            public float NaN { get; set; }
            public float PositiveInfinity { get; set; }
            public float NegativeInfinity { get; set; }
            public double DoubleNaN { get; set; }
            public double DoublePositiveInfinity { get; set; }
            public double DoubleNegativeInfinity { get; set; }
        }

        private sealed class NestedMessage
        {
            public string Title { get; set; }
            public SimpleMessage Inner { get; set; }
        }

        private sealed class DeeplyNestedMessage
        {
            public int Level { get; set; }
            public DeeplyNestedMessage Child { get; set; }
        }

        private sealed class CollectionsMessage
        {
            public List<string> StringList { get; set; }
            public Dictionary<string, int> StringIntMap { get; set; }
            public int[] IntArray { get; set; }
            public List<SimpleMessage> ObjectList { get; set; }
        }

        [Test]
        public void RoundTripSimpleObject()
        {
            SimpleMessage msg = new()
            {
                Id = 42,
                Name = "Test",
                Values = new List<int> { 1, 2, 3, 4, 5 },
            };

            string json = Serializer.JsonStringify(msg);
            SimpleMessage clone = Serializer.JsonDeserialize<SimpleMessage>(json);

            Assert.AreEqual(msg.Id, clone.Id);
            Assert.AreEqual(msg.Name, clone.Name);
            CollectionAssert.AreEqual(msg.Values, clone.Values);
        }

        [Test]
        public void RoundTripNullValues()
        {
            SimpleMessage msg = new()
            {
                Id = 0,
                Name = null,
                Values = null,
            };

            string json = Serializer.JsonStringify(msg);
            SimpleMessage clone = Serializer.JsonDeserialize<SimpleMessage>(json);

            Assert.AreEqual(msg.Id, clone.Id);
            Assert.IsTrue(clone.Name == null, "Name should be null after round-trip of null value");
            Assert.IsTrue(
                clone.Values == null,
                "Values should be null after round-trip of null collection"
            );
        }

        [Test]
        public void RoundTripEmptyStringsAndCollections()
        {
            SimpleMessage msg = new()
            {
                Id = 1,
                Name = string.Empty,
                Values = new List<int>(),
            };

            string json = Serializer.JsonStringify(msg);
            SimpleMessage clone = Serializer.JsonDeserialize<SimpleMessage>(json);

            Assert.AreEqual(msg.Id, clone.Id);
            Assert.AreEqual(string.Empty, clone.Name);
            Assert.NotNull(clone.Values);
            Assert.IsEmpty(clone.Values);
        }

        [Test]
        public void RoundTripNumericBoundaries()
        {
            NumericBoundariesMessage msg = new()
            {
                IntMin = int.MinValue,
                IntMax = int.MaxValue,
                LongMin = long.MinValue,
                LongMax = long.MaxValue,
                FloatMin = float.MinValue,
                FloatMax = float.MaxValue,
                DoubleMin = double.MinValue,
                DoubleMax = double.MaxValue,
                NaN = float.NaN,
                PositiveInfinity = float.PositiveInfinity,
                NegativeInfinity = float.NegativeInfinity,
                DoubleNaN = double.NaN,
                DoublePositiveInfinity = double.PositiveInfinity,
                DoubleNegativeInfinity = double.NegativeInfinity,
            };

            // JSON doesn't natively support NaN/Infinity, but System.Text.Json handles it
            JsonSerializerOptions options = new()
            {
                NumberHandling = System
                    .Text
                    .Json
                    .Serialization
                    .JsonNumberHandling
                    .AllowNamedFloatingPointLiterals,
            };

            string json = Serializer.JsonStringify(msg, options);
            NumericBoundariesMessage clone = Serializer.JsonDeserialize<NumericBoundariesMessage>(
                json,
                null,
                options
            );

            Assert.AreEqual(msg.IntMin, clone.IntMin);
            Assert.AreEqual(msg.IntMax, clone.IntMax);
            Assert.AreEqual(msg.LongMin, clone.LongMin);
            Assert.AreEqual(msg.LongMax, clone.LongMax);
            Assert.AreEqual(msg.FloatMin, clone.FloatMin);
            Assert.AreEqual(msg.FloatMax, clone.FloatMax);
            Assert.AreEqual(msg.DoubleMin, clone.DoubleMin);
            Assert.AreEqual(msg.DoubleMax, clone.DoubleMax);
            Assert.IsTrue(float.IsNaN(clone.NaN));
            Assert.IsTrue(float.IsPositiveInfinity(clone.PositiveInfinity));
            Assert.IsTrue(float.IsNegativeInfinity(clone.NegativeInfinity));
            Assert.IsTrue(double.IsNaN(clone.DoubleNaN));
            Assert.IsTrue(double.IsPositiveInfinity(clone.DoublePositiveInfinity));
            Assert.IsTrue(double.IsNegativeInfinity(clone.DoubleNegativeInfinity));
        }

        [Test]
        public void RoundTripSpecialCharactersAndUnicode()
        {
            SimpleMessage msg = new()
            {
                Id = 123,
                Name = "Hello 世界 🌍 \"quotes\" \\ backslash \n newline \t tab \r return",
                Values = new List<int> { 1 },
            };

            string json = Serializer.JsonStringify(msg);
            SimpleMessage clone = Serializer.JsonDeserialize<SimpleMessage>(json);

            Assert.AreEqual(msg.Id, clone.Id);
            Assert.AreEqual(msg.Name, clone.Name);
            CollectionAssert.AreEqual(msg.Values, clone.Values);
        }

        [Test]
        public void RoundTripVeryLongStrings()
        {
            SimpleMessage msg = new()
            {
                Id = 999,
                Name = new string('X', 100_000),
                Values = new List<int> { 1, 2, 3 },
            };

            string json = Serializer.JsonStringify(msg);
            SimpleMessage clone = Serializer.JsonDeserialize<SimpleMessage>(json);

            Assert.AreEqual(msg.Id, clone.Id);
            Assert.AreEqual(100_000, clone.Name.Length);
            Assert.AreEqual(msg.Name, clone.Name);
            CollectionAssert.AreEqual(msg.Values, clone.Values);
        }

        [Test]
        public void RoundTripNestedObjects()
        {
            NestedMessage msg = new()
            {
                Title = "Parent",
                Inner = new SimpleMessage
                {
                    Id = 77,
                    Name = "Child",
                    Values = new List<int> { 10, 20, 30 },
                },
            };

            string json = Serializer.JsonStringify(msg);
            NestedMessage clone = Serializer.JsonDeserialize<NestedMessage>(json);

            Assert.AreEqual(msg.Title, clone.Title);
            Assert.NotNull(clone.Inner);
            Assert.AreEqual(msg.Inner.Id, clone.Inner.Id);
            Assert.AreEqual(msg.Inner.Name, clone.Inner.Name);
            CollectionAssert.AreEqual(msg.Inner.Values, clone.Inner.Values);
        }

        [Test]
        public void RoundTripDeeplyNestedStructure()
        {
            const int depth = 50;
            DeeplyNestedMessage root = new() { Level = 0 };
            DeeplyNestedMessage current = root;

            for (int i = 1; i < depth; ++i)
            {
                current.Child = new DeeplyNestedMessage { Level = i };
                current = current.Child;
            }

            string json = Serializer.JsonStringify(root);
            DeeplyNestedMessage clone = Serializer.JsonDeserialize<DeeplyNestedMessage>(json);

            DeeplyNestedMessage cloneCurrent = clone;
            for (int i = 0; i < depth; ++i)
            {
                Assert.NotNull(cloneCurrent, $"Level {i} should not be null");
                Assert.AreEqual(i, cloneCurrent.Level, $"Level {i} value mismatch");
                cloneCurrent = cloneCurrent.Child;
            }
            Assert.IsTrue(cloneCurrent == null, "Should be null after last level");
        }

        [Test]
        public void RoundTripComplexCollections()
        {
            CollectionsMessage msg = new()
            {
                StringList = new List<string> { "apple", "banana", "cherry", null, string.Empty },
                StringIntMap = new Dictionary<string, int>
                {
                    ["one"] = 1,
                    ["two"] = 2,
                    ["three"] = 3,
                    ["negative"] = -100,
                },
                IntArray = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
                ObjectList = new List<SimpleMessage>
                {
                    new()
                    {
                        Id = 1,
                        Name = "First",
                        Values = new List<int> { 1 },
                    },
                    new()
                    {
                        Id = 2,
                        Name = "Second",
                        Values = null,
                    },
                    new()
                    {
                        Id = 3,
                        Name = null,
                        Values = new List<int>(),
                    },
                },
            };

            string json = Serializer.JsonStringify(msg);
            CollectionsMessage clone = Serializer.JsonDeserialize<CollectionsMessage>(json);

            CollectionAssert.AreEqual(msg.StringList, clone.StringList);
            CollectionAssert.AreEqual(msg.StringIntMap, clone.StringIntMap);
            CollectionAssert.AreEqual(msg.IntArray, clone.IntArray);
            Assert.AreEqual(msg.ObjectList.Count, clone.ObjectList.Count);
            for (int i = 0; i < msg.ObjectList.Count; ++i)
            {
                Assert.AreEqual(msg.ObjectList[i].Id, clone.ObjectList[i].Id);
                Assert.AreEqual(msg.ObjectList[i].Name, clone.ObjectList[i].Name);
            }
        }

        [Test]
        public void RoundTripLargeCollections()
        {
            SimpleMessage msg = new()
            {
                Id = 888,
                Name = "LargeCollection",
                Values = new List<int>(10_000),
            };

            // 10k still exercises large-collection round-trip paths while keeping
            // this correctness test in the fast suite (full-scale throughput is
            // covered by the Performance-categorized benchmark suite).
            for (int i = 0; i < 10_000; ++i)
            {
                msg.Values.Add(i);
            }

            string json = Serializer.JsonStringify(msg);
            SimpleMessage clone = Serializer.JsonDeserialize<SimpleMessage>(json);

            Assert.AreEqual(msg.Id, clone.Id);
            Assert.AreEqual(msg.Name, clone.Name);
            Assert.AreEqual(10_000, clone.Values.Count);
            CollectionAssert.AreEqual(msg.Values, clone.Values);
        }

        [Test]
        public void DeserializeInvalidJsonThrowsException()
        {
            string invalidJson = "{ invalid json }";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<SimpleMessage>(invalidJson)
            );
        }

        [Test]
        public void DeserializeMalformedJsonThrowsException()
        {
            string malformedJson = "{\"Id\": 123, \"Name\": \"Test\""; // Missing closing brace
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<SimpleMessage>(malformedJson)
            );
        }

        [Test]
        public void DeserializeEmptyStringThrowsException()
        {
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<SimpleMessage>(string.Empty)
            );
        }

        [Test]
        public void DeserializeNullStringThrowsException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                Serializer.JsonDeserialize<SimpleMessage>((string)null)
            );
        }

        [Test]
        public void RoundTripPrettyPrint()
        {
            SimpleMessage msg = new()
            {
                Id = 42,
                Name = "Pretty",
                Values = new List<int> { 1, 2, 3 },
            };

            string prettyJson = Serializer.JsonStringify(msg, pretty: true);
            string compactJson = Serializer.JsonStringify(msg, pretty: false);

            // Pretty printed should be longer due to whitespace
            Assert.Greater(prettyJson.Length, compactJson.Length);
            Assert.IsTrue(prettyJson.Contains("\n") || prettyJson.Contains("\r"));

            // Both should deserialize to the same object
            SimpleMessage prettyClone = Serializer.JsonDeserialize<SimpleMessage>(prettyJson);
            SimpleMessage compactClone = Serializer.JsonDeserialize<SimpleMessage>(compactJson);

            Assert.AreEqual(msg.Id, prettyClone.Id);
            Assert.AreEqual(msg.Id, compactClone.Id);
            Assert.AreEqual(prettyClone.Id, compactClone.Id);
        }

        [Test]
        public void BufferReuseMultipleOperationsNoDataCorruption()
        {
            byte[] buffer = null;

            SimpleMessage msg1 = new()
            {
                Id = 111,
                Name = "First",
                Values = new List<int> { 1, 2, 3 },
            };

            int bytes = Serializer.JsonSerialize(msg1, ref buffer);
            byte[] data1 = buffer.Take(bytes).ToArray();

            SimpleMessage msg2 = new()
            {
                Id = 222,
                Name = "Second",
                Values = new List<int> { 4, 5, 6 },
            };

            bytes = Serializer.JsonSerialize(msg2, ref buffer);
            byte[] data2 = buffer.Take(bytes).ToArray();

            string json1 = System.Text.Encoding.UTF8.GetString(data1);
            string json2 = System.Text.Encoding.UTF8.GetString(data2);

            SimpleMessage clone1 = Serializer.JsonDeserialize<SimpleMessage>(json1);
            SimpleMessage clone2 = Serializer.JsonDeserialize<SimpleMessage>(json2);

            Assert.AreEqual(msg1.Id, clone1.Id);
            Assert.AreEqual(msg1.Name, clone1.Name);
            CollectionAssert.AreEqual(msg1.Values, clone1.Values);

            Assert.AreEqual(msg2.Id, clone2.Id);
            Assert.AreEqual(msg2.Name, clone2.Name);
            CollectionAssert.AreEqual(msg2.Values, clone2.Values);
        }

        [Test]
        public void ManySequentialSerializeDeserializeNoStateLeakage()
        {
            for (int i = 0; i < 1_000; ++i)
            {
                SimpleMessage msg = new()
                {
                    Id = i,
                    Name = i % 3 == 0 ? null : ("N_" + i),
                    Values = i % 4 == 0 ? null : new List<int> { i, i + 1, i + 2 },
                };

                string json = Serializer.JsonStringify(msg);
                SimpleMessage clone = Serializer.JsonDeserialize<SimpleMessage>(json);

                Assert.AreEqual(msg.Id, clone.Id);
                Assert.AreEqual(msg.Name, clone.Name);
                if (msg.Values == null)
                {
                    Assert.IsTrue(
                        clone.Values == null,
                        "Null collection should remain null after round-trip"
                    );
                }
                else
                {
                    CollectionAssert.AreEqual(msg.Values, clone.Values);
                }
            }
        }
    }
}
