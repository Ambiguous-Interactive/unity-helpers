// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Buffers.Binary;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class WGuidTests
    {
        private const string NonVersionFourGuid = "00000000-0000-1000-8000-000000000000";

        [Test]
        public void DefaultValueIsEmpty()
        {
            WGuid guid = default;
            Assert.IsTrue(guid.IsEmpty);
            Assert.AreEqual(WGuid.EmptyGuid, guid);
        }

        [Test]
        public void NewGuidProducesVersion4()
        {
            WGuid guid = WGuid.NewGuid();
            Assert.IsFalse(guid.IsEmpty);
            Assert.IsTrue(guid.IsVersion4);
        }

        [Test]
        public void ConstructorFromGuidPreservesValue()
        {
            Guid expected = Guid.NewGuid();
            WGuid guid = new(expected);
            Assert.AreEqual(expected, guid.ToGuid());
            Assert.IsTrue(guid.IsVersion4);
        }

        [Test]
        public void ImplicitConversionRoundTripsGuid()
        {
            Guid source = Guid.NewGuid();
            WGuid wrapper = source;
            Guid converted = wrapper;
            Assert.AreEqual(source, converted);
            Assert.AreEqual(source, wrapper.ToGuid());
        }

        [Test]
        public void ToByteArrayMatchesGuidBytes()
        {
            Guid source = Guid.NewGuid();
            WGuid guid = new(source);
            byte[] expected = source.ToByteArray();
            byte[] actual = guid.ToByteArray();
            CollectionAssert.AreEqual(expected, actual);
        }

        [Test]
        public void TryWriteBytesWritesIntoSpan()
        {
            Guid source = Guid.NewGuid();
            WGuid guid = new(source);
            Span<byte> destination = stackalloc byte[16];
            bool success = guid.TryWriteBytes(destination);
            Assert.IsTrue(success);
            byte[] expected = source.ToByteArray();
            byte[] actual = new byte[16];
            destination.CopyTo(actual);
            CollectionAssert.AreEqual(expected, actual);
        }

        [Test]
        public void TryParseAcceptsValidString()
        {
            Guid source = Guid.NewGuid();
            bool success = WGuid.TryParse(source.ToString("D"), out WGuid parsed);
            Assert.IsTrue(success);
            Assert.AreEqual(source, parsed.ToGuid());
        }

        [Test]
        public void TryParseRejectsInvalidString()
        {
            bool success = WGuid.TryParse("not-a-guid", out WGuid parsed);
            Assert.IsFalse(success);
            Assert.AreEqual(WGuid.EmptyGuid, parsed);
        }

        [Test]
        public void TryParseRejectsNonVersion4Guid()
        {
            bool success = WGuid.TryParse(NonVersionFourGuid, out WGuid parsed);
            Assert.IsFalse(success);
            Assert.AreEqual(WGuid.EmptyGuid, parsed);
        }

        [Test]
        public void IsValidReturnsTrueForVersionFourGuid()
        {
            WGuid guid = WGuid.NewGuid();
            Assert.IsTrue(guid.IsValid);
        }

        [Test]
        public void IsValidReturnsFalseForLegacyGuid()
        {
            WGuid legacy = CreateLegacyWGuid(NonVersionFourGuid);
            Assert.IsFalse(legacy.IsValid);
        }

        [Test]
        public void StringConstructorRejectsNonVersionFourGuid()
        {
            FormatException exception = Assert.Throws<FormatException>(() =>
                _ = new WGuid(NonVersionFourGuid)
            );
            StringAssert.Contains("version 1", exception.Message);
        }

        [Test]
        public void GuidConstructorRejectsNonVersionFourGuid()
        {
            Guid invalid = Guid.Parse(NonVersionFourGuid);
            FormatException exception = Assert.Throws<FormatException>(() =>
                _ = new WGuid(invalid)
            );
            StringAssert.Contains("version 1", exception.Message);
        }

        [Test]
        public void ByteConstructorRejectsNonVersionFourGuid()
        {
            Guid invalid = Guid.Parse(NonVersionFourGuid);
            byte[] bytes = invalid.ToByteArray();
            FormatException exception = Assert.Throws<FormatException>(() => _ = new WGuid(bytes));
            StringAssert.Contains("version 1", exception.Message);
        }

        [Test]
        public void HasVersionFourLayoutDetectsInvalidBytes()
        {
            Guid invalid = Guid.Parse(NonVersionFourGuid);
            byte[] bytes = invalid.ToByteArray();
            long low = unchecked((long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(0, 8)));
            long high = unchecked(
                (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(8, 8))
            );
            Assert.IsFalse(WGuid.HasVersionFourLayout(low, high));
        }

        [Test]
        public void ParseThrowsForInvalidGuid()
        {
            Assert.Throws<FormatException>(() => WGuid.Parse(NonVersionFourGuid));
        }

        [Test]
        public void TryFormatWritesGuidString()
        {
            WGuid guid = WGuid.NewGuid();
            Span<char> destination = stackalloc char[36];
            bool formatted = guid.TryFormat(
                destination,
                out int charsWritten,
                ReadOnlySpan<char>.Empty
            );
            Assert.IsTrue(formatted);
            Assert.AreEqual(36, charsWritten);
            string formattedString = new(destination.Slice(0, charsWritten));
            Assert.IsTrue(Guid.TryParse(formattedString, out Guid parsed));
            Assert.AreEqual(guid.ToGuid(), parsed);
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void JsonSerializationRoundTripsGuid()
        {
            WGuid original = WGuid.NewGuid();
            string json = JsonSerializer.Serialize(original);
            WGuid roundTripped = JsonSerializer.Deserialize<WGuid>(json);

            Assert.AreEqual(original, roundTripped);
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void ProtoSerializationRoundTripsGuid()
        {
            WGuid original = WGuid.NewGuid();
            using MemoryStream stream = new();
            ProtoBuf.Serializer.Serialize(stream, original);
            stream.Position = 0;

            WGuid roundTripped = ProtoBuf.Serializer.Deserialize<WGuid>(stream);
            Assert.AreEqual(original, roundTripped);
        }

        [Test]
        public void TryParseSpanAcceptsValidGuid()
        {
            Guid source = Guid.NewGuid();
            bool parsed = WGuid.TryParse(source.ToString("D").AsSpan(), out WGuid wrapper);

            Assert.IsTrue(parsed);
            Assert.AreEqual(source, wrapper.ToGuid());
        }

        [Test]
        public void TryWriteBytesFailsWhenDestinationTooSmall()
        {
            WGuid guid = WGuid.NewGuid();
            Span<byte> destination = stackalloc byte[8];

            bool success = guid.TryWriteBytes(destination);

            Assert.IsFalse(success);
        }

        [Test]
        public void CompareToMatchesGuidComparison()
        {
            WGuid left = WGuid.NewGuid();
            WGuid right = WGuid.NewGuid();
            int expected = left.ToGuid().CompareTo(right.ToGuid());
            int actual = left.CompareTo(right);
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void EqualityUsesUnderlyingValue()
        {
            Guid source = Guid.NewGuid();
            WGuid first = new(source);
            WGuid second = new(source);
            Assert.IsTrue(first.Equals(second));
            Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        }

        [Test]
        public void ByteConstructorRejectsInvalidLength()
        {
            byte[] bytes = Encoding.ASCII.GetBytes("too_short");
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new WGuid(bytes));
        }

        private static WGuid CreateLegacyWGuid(string value)
        {
            Guid legacyGuid = Guid.Parse(value);
            byte[] bytes = legacyGuid.ToByteArray();
            long low = unchecked((long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(0, 8)));
            long high = unchecked(
                (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(8, 8))
            );
            return WGuid.CreateUnchecked(low, high);
        }
    }
}
