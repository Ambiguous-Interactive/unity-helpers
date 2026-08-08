// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins <see cref="WProtoWriter"/> and <see cref="WProtoReader"/> to the protobuf wire format.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every <c>Hex</c> constant below is the literal output of protobuf-net 3.2.56 -- the runtime
    /// this package vendors -- serializing an equivalent <c>[ProtoContract]</c>, captured by a
    /// differential harness that compared 90 cases and found 90 matches. Committing the bytes
    /// rather than the oracle means the guarantee outlives the vendored DLL, which the WallstopProto
    /// migration exists to delete.
    /// </para>
    /// <para>
    /// The field numbers are load-bearing: each golden vector includes its tag byte, so a wrong tag
    /// encoding fails these cases rather than hiding behind a correct payload.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoWireFormatTests
    {
        private const int ScratchSize = 512;

        [TestCase(1, "0801")]
        [TestCase(-1, "08FFFFFFFFFFFFFFFFFF01")]
        [TestCase(127, "087F")]
        [TestCase(128, "088001")]
        [TestCase(300, "08AC02")]
        [TestCase(int.MaxValue, "08FFFFFFFF07")]
        [TestCase(int.MinValue, "0880808080F8FFFFFFFF01")]
        public void Int32MatchesProtobufNetBytes(int value, string expected)
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteTag(1, WProtoWireType.Varint));
            Assert.IsTrue(writer.TryWriteInt32(value));
            Assert.AreEqual(expected, ToHex(writer.Written));
            Assert.AreEqual(
                writer.Position - 1,
                WProtoSizes.Int32Size(value),
                "WProtoSizes disagreed with the bytes actually written."
            );

            WProtoReader reader = new(writer.Written);
            Assert.IsTrue(reader.TryReadTag(out int fieldNumber, out int wireType));
            Assert.AreEqual(1, fieldNumber);
            Assert.AreEqual(WProtoWireType.Varint, wireType);
            Assert.IsTrue(reader.TryReadInt32(out int roundTripped));
            Assert.AreEqual(value, roundTripped);
            Assert.IsTrue(reader.End);
            Assert.IsFalse(reader.Malformed);
        }

        [TestCase(1L, "1001")]
        [TestCase(-1L, "10FFFFFFFFFFFFFFFFFF01")]
        [TestCase(long.MaxValue, "10FFFFFFFFFFFFFFFF7F")]
        [TestCase(long.MinValue, "1080808080808080808001")]
        public void Int64MatchesProtobufNetBytes(long value, string expected)
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteTag(2, WProtoWireType.Varint));
            Assert.IsTrue(writer.TryWriteInt64(value));
            Assert.AreEqual(expected, ToHex(writer.Written));

            WProtoReader reader = new(writer.Written);
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadInt64(out long roundTripped));
            Assert.AreEqual(value, roundTripped);
            Assert.IsTrue(reader.End);
        }

        [TestCase(1u, "1801")]
        [TestCase(128u, "188001")]
        [TestCase(uint.MaxValue, "18FFFFFFFF0F")]
        public void UnsignedVarint32MatchesProtobufNetBytes(uint value, string expected)
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteTag(3, WProtoWireType.Varint));
            Assert.IsTrue(writer.TryWriteVarint32(value));
            Assert.AreEqual(expected, ToHex(writer.Written));

            WProtoReader reader = new(writer.Written);
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadVarint32(out uint roundTripped));
            Assert.AreEqual(value, roundTripped);
        }

        [TestCase(1ul, "2001")]
        [TestCase(ulong.MaxValue, "20FFFFFFFFFFFFFFFFFF01")]
        public void UnsignedVarint64MatchesProtobufNetBytes(ulong value, string expected)
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteTag(4, WProtoWireType.Varint));
            Assert.IsTrue(writer.TryWriteVarint64(value));
            Assert.AreEqual(expected, ToHex(writer.Written));

            WProtoReader reader = new(writer.Written);
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadVarint64(out ulong roundTripped));
            Assert.AreEqual(value, roundTripped);
        }

        [Test]
        public void BooleanMatchesProtobufNetBytes()
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteTag(5, WProtoWireType.Varint));
            Assert.IsTrue(writer.TryWriteBool(true));
            Assert.AreEqual("2801", ToHex(writer.Written));

            WProtoReader reader = new(writer.Written);
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadBool(out bool roundTripped));
            Assert.IsTrue(roundTripped);
        }

        [TestCase(1f, "0D0000803F")]
        [TestCase(-1f, "0D000080BF")]
        [TestCase(float.NaN, "0D0000C0FF")]
        [TestCase(float.PositiveInfinity, "0D0000807F")]
        [TestCase(3.14159f, "0DD00F4940")]
        public void SingleMatchesProtobufNetBytes(float value, string expected)
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteTag(1, WProtoWireType.Fixed32));
            Assert.IsTrue(writer.TryWriteSingle(value));
            Assert.AreEqual(expected, ToHex(writer.Written));

            WProtoReader reader = new(writer.Written);
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadSingle(out float roundTripped));
            Assert.AreEqual(
                BitConverter.SingleToInt32Bits(value),
                BitConverter.SingleToInt32Bits(roundTripped),
                "The round trip must preserve the exact bit pattern, NaN payload included."
            );
        }

        [TestCase(1d, "11000000000000F03F")]
        [TestCase(-1d, "11000000000000F0BF")]
        [TestCase(double.NegativeInfinity, "11000000000000F0FF")]
        public void DoubleMatchesProtobufNetBytes(double value, string expected)
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteTag(2, WProtoWireType.Fixed64));
            Assert.IsTrue(writer.TryWriteDouble(value));
            Assert.AreEqual(expected, ToHex(writer.Written));

            WProtoReader reader = new(writer.Written);
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadDouble(out double roundTripped));
            Assert.AreEqual(
                BitConverter.DoubleToInt64Bits(value),
                BitConverter.DoubleToInt64Bits(roundTripped)
            );
        }

        [Test]
        public void PiRoundTripsBitExact()
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteTag(2, WProtoWireType.Fixed64));
            Assert.IsTrue(writer.TryWriteDouble(Math.PI));
            Assert.AreEqual("11182D4454FB210940", ToHex(writer.Written));
        }

        [TestCase("", "0A00")]
        [TestCase("a", "0A0161")]
        [TestCase("hello", "0A0568656C6C6F")]
        [TestCase("héllo", "0A0668C3A96C6C6F")]
        [TestCase("😀", "0A04F09F9880")]
        public void StringMatchesProtobufNetBytes(string value, string expected)
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteTag(1, WProtoWireType.LengthDelimited));
            Assert.IsTrue(writer.TryWriteString(value));
            Assert.AreEqual(expected, ToHex(writer.Written));
            Assert.AreEqual(
                writer.Position - 1,
                WProtoSizes.StringSize(value),
                "WProtoSizes disagreed with the bytes actually written."
            );

            WProtoReader reader = new(writer.Written);
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadString(out string roundTripped));
            Assert.AreEqual(value, roundTripped);
            Assert.IsTrue(reader.End);
        }

        [Test]
        public void NullStringWritesAnEmptyFieldRatherThanFailing()
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteTag(1, WProtoWireType.LengthDelimited));
            Assert.IsTrue(writer.TryWriteString(null));
            Assert.AreEqual("0A00", ToHex(writer.Written));

            WProtoReader reader = new(writer.Written);
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadString(out string roundTripped));
            Assert.AreEqual(
                string.Empty,
                roundTripped,
                "A zero-length field must decode to an empty string, never to null."
            );
        }

        [Test]
        public void BytesMatchProtobufNetBytes()
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter empty = new(scratch);
            Assert.IsTrue(empty.TryWriteTag(2, WProtoWireType.LengthDelimited));
            Assert.IsTrue(empty.TryWriteBytes(ReadOnlySpan<byte>.Empty));
            Assert.AreEqual("1200", ToHex(empty.Written));

            byte[] payload = { 0, 255, 127, 128 };
            byte[] scratchTwo = new byte[ScratchSize];
            WProtoWriter writer = new(scratchTwo);
            Assert.IsTrue(writer.TryWriteTag(2, WProtoWireType.LengthDelimited));
            Assert.IsTrue(writer.TryWriteBytes(payload));
            Assert.AreEqual("120400FF7F80", ToHex(writer.Written));

            WProtoReader reader = new(writer.Written);
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadBytes(out ReadOnlySpan<byte> roundTripped));
            Assert.AreEqual("00FF7F80", ToHex(roundTripped));
        }

        [TestCase(1, "0802")]
        [TestCase(-1, "0801")]
        [TestCase(2, "0804")]
        [TestCase(-2, "0803")]
        [TestCase(int.MaxValue, "08FEFFFFFF0F")]
        [TestCase(int.MinValue, "08FFFFFFFF0F")]
        public void ZigZag32MatchesProtobufNetBytes(int value, string expected)
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteTag(1, WProtoWireType.Varint));
            Assert.IsTrue(writer.TryWriteZigZag32(value));
            Assert.AreEqual(expected, ToHex(writer.Written));
            Assert.AreEqual(writer.Position - 1, WProtoSizes.ZigZag32Size(value));

            WProtoReader reader = new(writer.Written);
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadZigZag32(out int roundTripped));
            Assert.AreEqual(value, roundTripped);
        }

        [TestCase(1L, "1002")]
        [TestCase(-1L, "1001")]
        [TestCase(long.MinValue, "10FFFFFFFFFFFFFFFFFF01")]
        public void ZigZag64MatchesProtobufNetBytes(long value, string expected)
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteTag(2, WProtoWireType.Varint));
            Assert.IsTrue(writer.TryWriteZigZag64(value));
            Assert.AreEqual(expected, ToHex(writer.Written));
            Assert.AreEqual(writer.Position - 1, WProtoSizes.ZigZag64Size(value));

            WProtoReader reader = new(writer.Written);
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadZigZag64(out long roundTripped));
            Assert.AreEqual(value, roundTripped);
        }

        [Test]
        public void MaximumFieldNumberMatchesProtobufNetBytes()
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteTag(1, WProtoWireType.Varint));
            Assert.IsTrue(writer.TryWriteInt32(7));
            Assert.IsTrue(writer.TryWriteTag(WProtoWireType.MaxFieldNumber, WProtoWireType.Varint));
            Assert.IsTrue(writer.TryWriteInt32(9));
            Assert.AreEqual("0807F8FFFFFF0F09", ToHex(writer.Written));

            WProtoReader reader = new(writer.Written);
            Assert.IsTrue(reader.TryReadTag(out int first, out _));
            Assert.AreEqual(1, first);
            Assert.IsTrue(reader.TryReadInt32(out _));
            Assert.IsTrue(reader.TryReadTag(out int second, out _));
            Assert.AreEqual(WProtoWireType.MaxFieldNumber, second);
        }

        [Test]
        public void NestedMessageMatchesProtobufNetBytes()
        {
            byte[] innerScratch = new byte[ScratchSize];
            WProtoWriter inner = new(innerScratch);
            Assert.IsTrue(inner.TryWriteTag(1, WProtoWireType.Varint));
            Assert.IsTrue(inner.TryWriteInt32(300));
            Assert.IsTrue(inner.TryWriteTag(5, WProtoWireType.Varint));
            Assert.IsTrue(inner.TryWriteBool(true));

            int predicted =
                WProtoSizes.TagSize(1) + WProtoSizes.Int32Size(300) + WProtoSizes.TagSize(5) + 1;
            Assert.AreEqual(
                predicted,
                inner.Position,
                "Sizing a sub-message up front is what removes the need for a scratch buffer."
            );

            byte[] outerScratch = new byte[ScratchSize];
            WProtoWriter outer = new(outerScratch);
            Assert.IsTrue(outer.TryWriteTag(1, WProtoWireType.LengthDelimited));
            Assert.IsTrue(outer.TryWriteBytes(inner.Written));
            Assert.IsTrue(outer.TryWriteTag(2, WProtoWireType.Varint));
            Assert.IsTrue(outer.TryWriteInt32(5));
            Assert.AreEqual("0A0508AC0228011005", ToHex(outer.Written));

            WProtoReader reader = new(outer.Written);
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadMessage(out WProtoReader nested));
            Assert.IsTrue(nested.TryReadTag(out int nestedField, out _));
            Assert.AreEqual(1, nestedField);
            Assert.IsTrue(nested.TryReadInt32(out int nestedValue));
            Assert.AreEqual(300, nestedValue);
            Assert.IsTrue(nested.TryReadTag(out _, out _));
            Assert.IsTrue(nested.TryReadBool(out bool nestedFlag));
            Assert.IsTrue(nestedFlag);
            Assert.IsTrue(nested.End, "A sub-reader must not see past its own payload.");
        }

        [Test]
        public void RepeatedFieldsAreUnpackedByDefault()
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            foreach (int value in new[] { 1, 2, 300 })
            {
                Assert.IsTrue(writer.TryWriteTag(1, WProtoWireType.Varint));
                Assert.IsTrue(writer.TryWriteInt32(value));
            }

            Assert.AreEqual(
                "0801080208AC02",
                ToHex(writer.Written),
                "protobuf-net leaves repeated fields unpacked unless IsPacked is set; proto3's "
                    + "packed default would produce a single length-delimited field instead."
            );
        }

        [Test]
        public void UnknownFieldsAreSkippedExactly()
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteTag(1, WProtoWireType.Varint));
            Assert.IsTrue(writer.TryWriteInt32(int.MinValue));
            Assert.IsTrue(writer.TryWriteTag(2, WProtoWireType.LengthDelimited));
            Assert.IsTrue(writer.TryWriteString("skipped"));
            Assert.IsTrue(writer.TryWriteTag(3, WProtoWireType.Fixed64));
            Assert.IsTrue(writer.TryWriteDouble(1d));
            Assert.IsTrue(writer.TryWriteTag(4, WProtoWireType.Fixed32));
            Assert.IsTrue(writer.TryWriteSingle(1f));
            Assert.IsTrue(writer.TryWriteTag(5, WProtoWireType.Varint));
            Assert.IsTrue(writer.TryWriteInt32(42));

            WProtoReader reader = new(writer.Written);
            int found = 0;
            while (reader.TryReadTag(out int fieldNumber, out int wireType))
            {
                if (fieldNumber == 5)
                {
                    Assert.IsTrue(reader.TryReadInt32(out found));
                    continue;
                }

                Assert.IsTrue(
                    reader.TrySkipField(wireType),
                    $"Field {fieldNumber} of wire type {wireType} could not be skipped."
                );
            }

            Assert.AreEqual(42, found, "Skipping must land exactly on the next field key.");
            Assert.IsFalse(reader.Malformed);
            Assert.IsTrue(reader.End);
        }

        [Test]
        public void BalancedGroupsAreSkippedAndTheFollowingFieldSurvives()
        {
            byte[] payload = { 0x0B, 0x10, 0x2A, 0x0C, 0x18, 0x07 };
            WProtoReader reader = new(payload);
            Assert.IsTrue(reader.TryReadTag(out _, out int wireType));
            Assert.AreEqual(WProtoWireType.StartGroup, wireType);
            Assert.IsTrue(reader.TrySkipField(wireType));
            Assert.IsTrue(reader.TryReadTag(out int fieldNumber, out _));
            Assert.AreEqual(3, fieldNumber);
            Assert.IsTrue(reader.TryReadInt32(out int value));
            Assert.AreEqual(7, value);
            Assert.IsFalse(reader.Malformed);
        }

        [TestCase("0880", TestName = "MalformedInputIsRejected(truncated varint)")]
        [TestCase(
            "08FFFFFFFFFFFFFFFFFFFF",
            TestName = "MalformedInputIsRejected(varint longer than ten bytes)"
        )]
        // Nine continuation bytes then 0x7F: exactly ten bytes, so the length bound is satisfied,
        // but the tenth byte carries bits above 64. Accepting it silently drops them and decodes
        // as a different number, which is why the tenth byte is range-checked separately.
        [TestCase(
            "08FFFFFFFFFFFFFFFFFF7F",
            TestName = "MalformedInputIsRejected(ten-byte varint overflowing 64 bits)"
        )]
        [TestCase("0A7F01", TestName = "MalformedInputIsRejected(length past end)")]
        [TestCase("0B0801", TestName = "MalformedInputIsRejected(unterminated group)")]
        public void MalformedInputIsRejected(string hex)
        {
            byte[] payload = FromHex(hex);
            WProtoReader reader = new(payload);
            while (reader.TryReadTag(out _, out int wireType))
            {
                if (!reader.TrySkipField(wireType))
                {
                    break;
                }
            }

            Assert.IsTrue(
                reader.Malformed,
                "A payload this broken must latch Malformed rather than decode as data."
            );
        }

        [TestCase(0x00, TestName = "InvalidFieldKeysAreRejected(field number zero)")]
        [TestCase(0x0E, TestName = "InvalidFieldKeysAreRejected(wire type 6)")]
        [TestCase(0x0F, TestName = "InvalidFieldKeysAreRejected(wire type 7)")]
        public void InvalidFieldKeysAreRejected(int firstByte)
        {
            byte[] payload = { (byte)firstByte, 0x01 };
            WProtoReader reader = new(payload);
            Assert.IsFalse(reader.TryReadTag(out int fieldNumber, out int wireType));
            Assert.AreEqual(0, fieldNumber);
            Assert.AreEqual(-1, wireType);
            Assert.IsTrue(reader.Malformed);
        }

        [Test]
        public void AnEmptyPayloadEndsCleanlyRatherThanReportingCorruption()
        {
            WProtoReader reader = new(ReadOnlySpan<byte>.Empty);
            Assert.IsTrue(reader.End);
            Assert.IsFalse(reader.TryReadTag(out _, out _));
            Assert.IsFalse(
                reader.Malformed,
                "End of input is the normal loop exit, not a decode failure."
            );
        }

        [Test]
        public void OnceMalformedTheReaderRefusesEveryLaterRead()
        {
            byte[] payload = { 0x08, 0x80 };
            WProtoReader reader = new(payload);
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsFalse(reader.TryReadVarint64(out _));
            Assert.IsTrue(reader.Malformed);
            Assert.IsFalse(reader.TryReadTag(out _, out _));
            Assert.IsFalse(reader.TryReadBool(out _));
            Assert.IsFalse(reader.TrySkipField(WProtoWireType.Varint));
        }

        [Test]
        public void AWriteThatDoesNotFitLeavesNoPartialField()
        {
            byte[] scratch = new byte[3];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteTag(1, WProtoWireType.LengthDelimited));
            Assert.IsFalse(writer.TryWriteString("too long for three bytes"));
            Assert.IsTrue(writer.Overflowed);
            Assert.AreEqual(
                1,
                writer.Position,
                "A refused write must not advance the position, or the prefix would lie about a "
                    + "payload that was never written."
            );

            Assert.IsFalse(
                writer.TryWriteBool(true),
                "Once overflowed, later writes must be refused so a truncated message cannot look "
                    + "complete."
            );
        }

        [Test]
        public void InvalidTagsAreRefusedByTheWriter()
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsFalse(writer.TryWriteTag(0, WProtoWireType.Varint));
            Assert.IsFalse(writer.TryWriteTag(-1, WProtoWireType.Varint));
            Assert.IsFalse(
                writer.TryWriteTag(WProtoWireType.MaxFieldNumber + 1, WProtoWireType.Varint)
            );
            Assert.IsFalse(writer.TryWriteTag(1, 6));
            Assert.AreEqual(0, writer.Position);
            Assert.IsFalse(
                writer.Overflowed,
                "A rejected tag is a caller error, not an out-of-space condition."
            );
        }

        [Test]
        public void VarintSizesAgreeWithTheEncoder()
        {
            ulong[] boundaries =
            {
                0ul,
                1ul,
                0x7Ful,
                0x80ul,
                0x3FFFul,
                0x4000ul,
                0x1FFFFFul,
                0x200000ul,
                0xFFFFFFFul,
                0x10000000ul,
                ulong.MaxValue,
            };

            byte[] scratch = new byte[ScratchSize];
            foreach (ulong value in boundaries)
            {
                WProtoWriter writer = new(scratch);
                Assert.IsTrue(writer.TryWriteVarint64(value));
                Assert.AreEqual(
                    writer.Position,
                    WProtoSizes.Varint64Size(value),
                    $"Varint64Size disagreed with the encoder for {value}."
                );
            }
        }

        private static string ToHex(ReadOnlySpan<byte> bytes)
        {
            StringBuilder builder = new(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++)
            {
                builder.Append(bytes[index].ToString("X2"));
            }

            return builder.ToString();
        }

        private static byte[] FromHex(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            return bytes;
        }
    }
}
