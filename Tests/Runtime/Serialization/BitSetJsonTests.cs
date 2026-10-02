// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Text.Json;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Serialization;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class BitSetJsonTests
    {
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
