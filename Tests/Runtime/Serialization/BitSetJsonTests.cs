// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Serialization;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class BitSetJsonTests
    {
        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(65, false)]
        [TestCase(129, false)]
        [TestCase(10001, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(65, true)]
        [TestCase(129, true)]
        [TestCase(10001, true)]
        public void JsonIndicesMatchSetIndexEnumerationAcrossWordBoundaries(
            int length,
            bool immutable
        )
        {
            const string capacityProperty = "capacity";
            const string indicesProperty = "setIndices";
            BitSet source = new BitSet(Math.Max(1, length));
            for (int index = 0; index < length; index += 31)
            {
                Assert.IsTrue(source.TrySet(index));
            }
            List<int> expected = new List<int>(source.EnumerateSetIndices());
            string json = immutable
                ? Serializer.JsonStringify(source.ToImmutable())
                : Serializer.JsonStringify(source);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            Assert.AreEqual(source.Capacity, root.GetProperty(capacityProperty).GetInt32());
            JsonElement indices = root.GetProperty(indicesProperty);
            Assert.AreEqual(expected.Count, indices.GetArrayLength());
            for (int index = 0; index < expected.Count; ++index)
            {
                Assert.AreEqual(expected[index], indices[index].GetInt32());
            }
        }

        [Test]
        public void DefaultImmutableBitSetWritesZeroCapacityAndNoIndices()
        {
            string json = Serializer.JsonStringify(default(ImmutableBitSet));
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.AreEqual(0, document.RootElement.GetProperty("capacity").GetInt32());
            Assert.AreEqual(0, document.RootElement.GetProperty("setIndices").GetArrayLength());
        }

        [Test]
        public void BitSetRoundTripsWithSetIndices()
        {
            BitSet original = new(16);
            original.TrySet(0);
            original.TrySet(3);
            original.TrySet(7);
            string json = Serializer.JsonStringify(original);
            BitSet deserialized = Serializer.JsonDeserialize<BitSet>(json);
            Assert.AreEqual(original.Capacity, deserialized.Capacity, "Capacity should match");
            for (int i = 0; i < original.Capacity; ++i)
            {
                Assert.AreEqual(original[i], deserialized[i], $"Bit {i} should match");
            }
        }

        [Test]
        public void BitSetRoundTripsWithFastOptions()
        {
            BitSet original = new(8);
            original.TrySet(1);
            original.TrySet(6);
            JsonSerializerOptions options = Serializer.CreateFastJsonOptions();
            string json = Serializer.JsonStringify(original, options);
            BitSet deserialized = Serializer.JsonDeserialize<BitSet>(json, null, options);
            Assert.AreEqual(
                original.Capacity,
                deserialized.Capacity,
                "Capacity should match under fast options"
            );
            for (int i = 0; i < original.Capacity; ++i)
            {
                Assert.AreEqual(
                    original[i],
                    deserialized[i],
                    $"Bit {i} should match under fast options"
                );
            }
        }
    }
}
