// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Buffers.Binary;
    using System.Text.Json;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Serialization;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class AdaptersJsonTests
    {
        [Test]
        public void FastVector2IntRoundTrips()
        {
            FastVector2Int v = new(-3, 7);
            string json = Serializer.JsonStringify(v);
            FastVector2Int again = Serializer.JsonDeserialize<FastVector2Int>(json);
            Assert.AreEqual(v, again, "FastVector2Int should round-trip by value");
        }

        [Test]
        public void FastVector3IntRoundTrips()
        {
            FastVector3Int v = new(1, -2, 3);
            string json = Serializer.JsonStringify(v);
            FastVector3Int again = Serializer.JsonDeserialize<FastVector3Int>(json);
            Assert.AreEqual(v, again, "FastVector3Int should round-trip by value");
        }

        [Test]
        public void WGuidRoundTrips()
        {
            WGuid id = WGuid.NewGuid();
            string json = Serializer.JsonStringify(id);
            WGuid again = Serializer.JsonDeserialize<WGuid>(json);
            Assert.AreEqual(id, again, "WGuid should round-trip by value");
        }

        [Test]
        public void WGuidLegacyObjectPayloadRoundTrips()
        {
            WGuid id = WGuid.NewGuid();
            Span<byte> buffer = stackalloc byte[16];
            bool success = id.TryWriteBytes(buffer);
            Assert.IsTrue(success);

            long low = (long)BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(0, 8));
            long high = (long)BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(8, 8));
            string legacy = FormattableString.Invariant(
                $"{{\"{WGuid.LowFieldName}\":{low},\"{WGuid.HighFieldName}\":{high},\"{WGuid.GuidPropertyName}\":\"{id}\"}}"
            );

            WGuid roundTripped = Serializer.JsonDeserialize<WGuid>(legacy);
            Assert.AreEqual(id, roundTripped, "WGuid legacy object should round-trip by value");
        }

        [Test]
        public void WGuidEmptyStringDeserializesToEmpty()
        {
            WGuid result = Serializer.JsonDeserialize<WGuid>("\"\"");
            Assert.IsTrue(result.IsEmpty);
        }

        [Test]
        public void WGuidNullDeserializesToEmpty()
        {
            WGuid result = Serializer.JsonDeserialize<WGuid>("null");
            Assert.IsTrue(result.IsEmpty);
        }

        [Test]
        public void WGuidInvalidStringThrows()
        {
            // The WGuidConverter rejects the payload with a JsonException; the Serializer wraps that
            // codec failure as a SerializationCorruptDataException, preserving the original as inner.
            SerializationCorruptDataException ex = Assert.Throws<SerializationCorruptDataException>(
                () =>
                    Serializer.JsonDeserialize<WGuid>("\"not-a-guid\"")
            );
            Assert.IsTrue(ex.InnerException is JsonException);
        }

        [Test]
        public void FastOptionsAdaptersRoundTrip()
        {
            FastVector2Int v2 = new(9, -4);
            FastVector3Int v3 = new(5, 6, -7);
            JsonSerializerOptions options = Serializer.CreateFastJsonOptions();

            string j2 = Serializer.JsonStringify(v2, options);
            string j3 = Serializer.JsonStringify(v3, options);

            FastVector2Int v2b = Serializer.JsonDeserialize<FastVector2Int>(j2, null, options);
            FastVector3Int v3b = Serializer.JsonDeserialize<FastVector3Int>(j3, null, options);

            Assert.AreEqual(v2, v2b, "FastVector2Int should round-trip with fast options");
            Assert.AreEqual(v3, v3b, "FastVector3Int should round-trip with fast options");
        }
    }
}
