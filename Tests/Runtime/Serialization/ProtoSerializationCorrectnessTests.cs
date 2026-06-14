// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Random;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ProtoSerializationCorrectnessTests
    {
        [ProtoContract]
        private sealed class EdgeCaseMessage
        {
            [ProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            public string Name { get; set; }

            [ProtoMember(3)]
            public List<int> Values { get; set; } = new();

            [ProtoMember(4)]
            public byte[] Data { get; set; } = Array.Empty<byte>();
        }

        [ProtoContract]
        private sealed class NestedMessage
        {
            [ProtoMember(1)]
            public string Title { get; set; }

            [ProtoMember(2)]
            public EdgeCaseMessage Inner { get; set; }
        }

        [ProtoContract]
        private sealed class NumericBoundariesMessage
        {
            [ProtoMember(1)]
            public int IntMin { get; set; }

            [ProtoMember(2)]
            public int IntMax { get; set; }

            [ProtoMember(3)]
            public long LongMin { get; set; }

            [ProtoMember(4)]
            public long LongMax { get; set; }

            [ProtoMember(5)]
            public float FloatMin { get; set; }

            [ProtoMember(6)]
            public float FloatMax { get; set; }

            [ProtoMember(7)]
            public double DoubleMin { get; set; }

            [ProtoMember(8)]
            public double DoubleMax { get; set; }

            [ProtoMember(9)]
            public float NaN { get; set; }

            [ProtoMember(10)]
            public float PositiveInfinity { get; set; }

            [ProtoMember(11)]
            public float NegativeInfinity { get; set; }

            [ProtoMember(12)]
            public double DoubleNaN { get; set; }

            [ProtoMember(13)]
            public double DoublePositiveInfinity { get; set; }

            [ProtoMember(14)]
            public double DoubleNegativeInfinity { get; set; }
        }

        [ProtoContract]
        private sealed class UnicodeStringMessage
        {
            [ProtoMember(1)]
            public string Ascii { get; set; }

            [ProtoMember(2)]
            public string Unicode { get; set; }

            [ProtoMember(3)]
            public string Emoji { get; set; }

            [ProtoMember(4)]
            public string ControlChars { get; set; }

            [ProtoMember(5)]
            public string VeryLongString { get; set; }
        }

        [ProtoContract]
        private sealed class DeeplyNestedMessage
        {
            [ProtoMember(1)]
            public int Level { get; set; }

            [ProtoMember(2)]
            public DeeplyNestedMessage Child { get; set; }
        }

        [Test]
        public void RoundTripEmptyAndDefaults()
        {
            EdgeCaseMessage msg = new()
            {
                Id = 0,
                Name = string.Empty,
                Values = new List<int>(),
                Data = Array.Empty<byte>(),
            };
            byte[] bytes = Serializer.ProtoSerialize(msg);
            EdgeCaseMessage clone = Serializer.ProtoDeserialize<EdgeCaseMessage>(bytes);

            Assert.AreEqual(msg.Id, clone.Id);
            Assert.AreEqual(msg.Name, clone.Name);
            // With OverwriteList = true, empty collections are preserved
            Assert.IsTrue(
                clone.Values != null,
                "Values should not be null after round-trip of empty collection"
            );
            Assert.AreEqual(0, clone.Values.Count);
            Assert.IsTrue(
                clone.Data != null,
                "Data should not be null after round-trip of empty array"
            );
            Assert.AreEqual(0, clone.Data.Length);
        }

        [Test]
        public void RoundTripNulls()
        {
            EdgeCaseMessage msg = new()
            {
                Id = 5,
                Name = null,
                Values = null,
                Data = null,
            };
            byte[] bytes = Serializer.ProtoSerialize(msg);
            EdgeCaseMessage clone = Serializer.ProtoDeserialize<EdgeCaseMessage>(bytes);

            Assert.AreEqual(msg.Id, clone.Id);
            Assert.AreEqual(msg.Name, clone.Name);
            // With property initializers, null collections deserialize as empty
            Assert.IsTrue(
                clone.Values != null,
                "Values should not be null after round-trip with property initializers"
            );
            Assert.AreEqual(0, clone.Values.Count);
            Assert.IsTrue(
                clone.Data != null,
                "Data should not be null after round-trip with property initializers"
            );
            Assert.AreEqual(0, clone.Data.Length);
        }

        [Test]
        public void RoundTripNestedObjectAndLargePayload()
        {
            EdgeCaseMessage inner = new()
            {
                Id = 42,
                Name = new string('x', 128),
                Values = new List<int>(64),
                Data = MakeBytes(32 * 1024),
            };
            for (int i = 0; i < 64; ++i)
            {
                inner.Values.Add(i * i);
            }

            NestedMessage msg = new() { Title = "Nested Title", Inner = inner };
            byte[] bytes = Serializer.ProtoSerialize(msg);
            NestedMessage clone = Serializer.ProtoDeserialize<NestedMessage>(bytes);

            Assert.NotNull(clone);
            Assert.AreEqual(msg.Title, clone.Title);
            Assert.NotNull(clone.Inner);
            Assert.AreEqual(inner.Id, clone.Inner.Id);
            Assert.AreEqual(inner.Name, clone.Inner.Name);
            CollectionAssert.AreEqual(inner.Values, clone.Inner.Values);
            CollectionAssert.AreEqual(inner.Data, clone.Inner.Data);
        }

        [Test]
        public void ManySequentialSerializeDeserializeMixedMessagesNoStateLeakage()
        {
            // This stresses the pooled streams by doing many back-to-back operations with different payload sizes
            IRandom rng = new PcgRandom(12345);
            for (int i = 0; i < 2_000; ++i)
            {
                EdgeCaseMessage msg = new()
                {
                    Id = i,
                    Name = i % 3 == 0 ? null : ("N_" + i),
                    Values = i % 4 == 0 ? null : new List<int> { i, i + 1, i + 2 },
                    Data = i % 5 == 0 ? null : MakeBytes(rng.Next(0, 8192)),
                };

                byte[] bytes = Serializer.ProtoSerialize(msg);
                EdgeCaseMessage clone = Serializer.ProtoDeserialize<EdgeCaseMessage>(bytes);

                Assert.AreEqual(msg.Id, clone.Id);
                Assert.AreEqual(msg.Name, clone.Name);
                // With property initializers, null collections deserialize as empty
                if (msg.Values == null)
                {
                    Assert.IsTrue(
                        clone.Values != null,
                        "Values should not be null with property initializers"
                    );
                    Assert.AreEqual(0, clone.Values.Count);
                }
                else
                {
                    CollectionAssert.AreEqual(msg.Values, clone.Values);
                }

                if (msg.Data == null)
                {
                    Assert.IsTrue(
                        clone.Data != null,
                        "Data should not be null with property initializers"
                    );
                    Assert.AreEqual(0, clone.Data.Length);
                }
                else
                {
                    CollectionAssert.AreEqual(msg.Data, clone.Data);
                }
            }
        }

        [Test]
        public void DeserializeWithExplicitTypeMatchesGeneric()
        {
            EdgeCaseMessage msg = new()
            {
                Id = 9,
                Name = "X",
                Values = new List<int> { 1, 2, 3 },
                Data = MakeBytes(256),
            };
            byte[] bytes = Serializer.ProtoSerialize(msg);

            EdgeCaseMessage a = Serializer.ProtoDeserialize<EdgeCaseMessage>(bytes);
            object bObj = Serializer.ProtoDeserialize<object>(bytes, typeof(EdgeCaseMessage));
            Assert.IsInstanceOf<EdgeCaseMessage>(bObj);
            EdgeCaseMessage b = (EdgeCaseMessage)bObj;

            Assert.AreEqual(a.Id, b.Id);
            Assert.AreEqual(a.Name, b.Name);
            CollectionAssert.AreEqual(a.Values, b.Values);
            CollectionAssert.AreEqual(a.Data, b.Data);
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

            byte[] bytes = Serializer.ProtoSerialize(msg);
            NumericBoundariesMessage clone = Serializer.ProtoDeserialize<NumericBoundariesMessage>(
                bytes
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
        public void RoundTripUnicodeAndSpecialCharacters()
        {
            UnicodeStringMessage msg = new()
            {
                Ascii = "Simple ASCII text 123!@#",
                Unicode = "Hello 世界 🌍 Привет мир Γειά σου κόσμε",
                Emoji = "😀😃😄😁🎉🎊🎈🎁💯🔥✨⭐🌟💫",
                ControlChars = "Tab\tNewline\nReturn\rBackspace\bFormFeed\f",
                VeryLongString = new string('A', 100_000),
            };

            byte[] bytes = Serializer.ProtoSerialize(msg);
            UnicodeStringMessage clone = Serializer.ProtoDeserialize<UnicodeStringMessage>(bytes);

            Assert.AreEqual(msg.Ascii, clone.Ascii);
            Assert.AreEqual(msg.Unicode, clone.Unicode);
            Assert.AreEqual(msg.Emoji, clone.Emoji);
            Assert.AreEqual(msg.ControlChars, clone.ControlChars);
            Assert.AreEqual(msg.VeryLongString, clone.VeryLongString);
            Assert.AreEqual(100_000, clone.VeryLongString.Length);
        }

        [Test]
        public void RoundTripDeeplyNestedStructure()
        {
            // Create a deeply nested structure (50 levels)
            const int depth = 50;
            DeeplyNestedMessage root = new() { Level = 0 };
            DeeplyNestedMessage current = root;

            for (int i = 1; i < depth; ++i)
            {
                current.Child = new DeeplyNestedMessage { Level = i };
                current = current.Child;
            }

            byte[] bytes = Serializer.ProtoSerialize(root);
            DeeplyNestedMessage clone = Serializer.ProtoDeserialize<DeeplyNestedMessage>(bytes);

            // Verify the structure
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
        public void BufferReuseMultipleOperationsNoDataCorruption()
        {
            // This tests that the buffer pooling mechanism correctly resets and doesn't leak data
            byte[] buffer = null;

            // First serialization with specific data
            EdgeCaseMessage msg1 = new()
            {
                Id = 111,
                Name = "FirstMessage",
                Values = new List<int> { 1, 2, 3, 4, 5 },
                Data = MakeBytes(1024),
            };

            int bytes = Serializer.ProtoSerialize(msg1, ref buffer);
            byte[] data1 = buffer.Take(bytes).ToArray();

            // Second serialization with different data
            EdgeCaseMessage msg2 = new()
            {
                Id = 222,
                Name = "SecondMessage",
                Values = new List<int> { 10, 20, 30 },
                Data = MakeBytes(512),
            };

            bytes = Serializer.ProtoSerialize(msg2, ref buffer);
            byte[] data2 = buffer.Take(bytes).ToArray();

            // Deserialize and verify both are correct
            EdgeCaseMessage clone1 = Serializer.ProtoDeserialize<EdgeCaseMessage>(data1);
            EdgeCaseMessage clone2 = Serializer.ProtoDeserialize<EdgeCaseMessage>(data2);

            Assert.AreEqual(msg1.Id, clone1.Id);
            Assert.AreEqual(msg1.Name, clone1.Name);
            CollectionAssert.AreEqual(msg1.Values, clone1.Values);
            CollectionAssert.AreEqual(msg1.Data, clone1.Data);

            Assert.AreEqual(msg2.Id, clone2.Id);
            Assert.AreEqual(msg2.Name, clone2.Name);
            CollectionAssert.AreEqual(msg2.Values, clone2.Values);
            CollectionAssert.AreEqual(msg2.Data, clone2.Data);
        }

        [Test]
        public void RoundTripEmptyCollectionsVsNullCollections()
        {
            // Test that empty and null collections are handled correctly
            // With OverwriteList = true, empty collections remain empty and null remains null
            EdgeCaseMessage emptyMsg = new()
            {
                Id = 1,
                Name = string.Empty,
                Values = new List<int>(),
                Data = Array.Empty<byte>(),
            };

            EdgeCaseMessage nullMsg = new()
            {
                Id = 2,
                Name = null,
                Values = null,
                Data = null,
            };

            byte[] emptyBytes = Serializer.ProtoSerialize(emptyMsg);
            byte[] nullBytes = Serializer.ProtoSerialize(nullMsg);

            EdgeCaseMessage emptyClone = Serializer.ProtoDeserialize<EdgeCaseMessage>(emptyBytes);
            EdgeCaseMessage nullClone = Serializer.ProtoDeserialize<EdgeCaseMessage>(nullBytes);

            // Empty collections should remain empty (with property initializers)
            Assert.IsTrue(
                emptyClone.Values != null,
                "Empty collection should be preserved as non-null"
            );
            Assert.AreEqual(0, emptyClone.Values.Count);
            Assert.IsTrue(emptyClone.Data != null, "Empty array should be preserved as non-null");
            Assert.AreEqual(0, emptyClone.Data.Length);
            Assert.AreEqual(string.Empty, emptyClone.Name);

            // With property initializers, null collections also deserialize as empty
            // (property initializers create the collection before protobuf can set null)
            Assert.IsTrue(
                nullClone.Values != null,
                "Null collection deserializes as empty with property initializers"
            );
            Assert.AreEqual(0, nullClone.Values.Count);
            Assert.IsTrue(
                nullClone.Data != null,
                "Null array deserializes as empty with property initializers"
            );
            Assert.AreEqual(0, nullClone.Data.Length);
            Assert.IsTrue(nullClone.Name == null, "Null string should remain null");
        }

        [Test]
        public void RoundTripLargeCollections()
        {
            // Test with large collections to stress memory and performance
            EdgeCaseMessage msg = new()
            {
                Id = 999,
                Name = "LargeCollection",
                Values = new List<int>(10_000),
                Data = MakeBytes(1024 * 1024), // 1 MB
            };

            // 10k still exercises large-collection round-trip paths while keeping
            // this correctness test in the fast suite (full-scale throughput is
            // covered by the Performance-categorized benchmark suite).
            for (int i = 0; i < 10_000; ++i)
            {
                msg.Values.Add(i);
            }

            byte[] bytes = Serializer.ProtoSerialize(msg);
            EdgeCaseMessage clone = Serializer.ProtoDeserialize<EdgeCaseMessage>(bytes);

            Assert.AreEqual(msg.Id, clone.Id);
            Assert.AreEqual(msg.Name, clone.Name);
            Assert.AreEqual(10_000, clone.Values.Count);
            CollectionAssert.AreEqual(msg.Values, clone.Values);
            Assert.AreEqual(1024 * 1024, clone.Data.Length);
            CollectionAssert.AreEqual(msg.Data, clone.Data);
        }

        private static byte[] MakeBytes(int len)
        {
            byte[] b = new byte[len];
            for (int i = 0; i < b.Length; ++i)
            {
                b[i] = (byte)(i * 31);
            }

            return b;
        }
    }
}
