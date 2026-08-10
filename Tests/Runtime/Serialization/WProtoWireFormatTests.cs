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
        public void AnUnpairedSurrogateMeasuresAndWritesTheSameNumberOfBytes()
        {
            // The one input where GetByteCount and GetBytes can disagree, and a disagreement there
            // corrupts the length prefix of every enclosing message. Built at runtime on purpose:
            // a lone surrogate cannot survive a [TestCase] argument, because attribute values are
            // stored as UTF-8 in metadata and the compiler substitutes replacement characters, so
            // the string reaching the test would not be the one written here.
            string loneSurrogate = new(new[] { '\ud800' });

            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteTag(1, WProtoWireType.LengthDelimited));
            Assert.IsTrue(writer.TryWriteString(loneSurrogate));
            Assert.AreEqual(
                writer.Position - 1,
                WProtoSizes.StringSize(loneSurrogate),
                "WProtoSizes and WProtoWriter must agree byte for byte on an unpaired surrogate."
            );

            // Measured against protobuf-net 3.2.56: it encodes the surrogate as U+FFFD too.
            Assert.AreEqual("0A03EFBFBD", ToHex(writer.Written));

            WProtoReader reader = new(writer.Written);
            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadString(out string roundTripped));
            Assert.AreEqual("\ufffd", roundTripped);
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
                    reader.TrySkipField(fieldNumber, wireType),
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
            Assert.IsTrue(reader.TryReadTag(out int groupField, out int wireType));
            Assert.AreEqual(WProtoWireType.StartGroup, wireType);
            Assert.IsTrue(reader.TrySkipField(groupField, wireType));
            Assert.IsTrue(reader.TryReadTag(out int fieldNumber, out _));
            Assert.AreEqual(3, fieldNumber);
            Assert.IsTrue(reader.TryReadInt32(out int value));
            Assert.AreEqual(7, value);
            Assert.IsFalse(reader.Malformed);
        }

        // 0B = field 1 START_GROUP, 10 2A = field 2 varint 42, then a terminator naming a DIFFERENT
        // field, then field 3 varint 7. protobuf-net rejects both of these; accepting them lets a
        // group be closed by a terminator belonging to an enclosing one, and lets crossed groups
        // (1< 2< /1> /2>) read as well formed.
        [TestCase("0B102A4C1807", TestName = "MismatchedGroupTerminatorsAreRejected(wrong field)")]
        [TestCase("0B130C14", TestName = "MismatchedGroupTerminatorsAreRejected(crossed groups)")]
        public void MismatchedGroupTerminatorsAreRejected(string hex)
        {
            byte[] payload = FromHex(hex);
            WProtoReader reader = new(payload);
            Assert.IsTrue(reader.TryReadTag(out int groupField, out int wireType));
            Assert.AreEqual(WProtoWireType.StartGroup, wireType);
            Assert.IsFalse(reader.TrySkipField(groupField, wireType));
            Assert.IsTrue(reader.Malformed);
        }

        // A key or a length carries no meaning above 32 bits, so a wider encoding is a second
        // spelling of a smaller number. Truncating it would let two different byte sequences decode
        // to the same message.
        [TestCase(
            "888080808001",
            TestName = "OverWideStructuralVarintsAreRejected(field key above 32 bits)"
        )]
        // The three trailing payload bytes matter. Without payload bytes behind it, a length TRUNCATED to 3
        // also exceeds Remaining and the truncating reader rejects it too -- the case would pass
        // against the very bug it names. With three bytes present, only the strict read rejects.
        [TestCase(
            "0A8380808010A1B2C3",
            TestName = "OverWideStructuralVarintsAreRejected(length above 32 bits)"
        )]
        public void OverWideStructuralVarintsAreRejected(string hex)
        {
            byte[] payload = FromHex(hex);
            WProtoReader reader = new(payload);
            if (reader.TryReadTag(out _, out int wireType))
            {
                Assert.AreEqual(
                    WProtoWireType.LengthDelimited,
                    wireType,
                    "The key case must be rejected outright; only the length case gets this far."
                );
                Assert.IsFalse(reader.TryReadLength(out _));
            }

            Assert.IsTrue(reader.Malformed);
        }

        [Test]
        public void ANegativeLengthPrefixIsRefusedAndMeasuresAsZeroBytes()
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsFalse(writer.TryWriteLengthPrefix(-1));
            Assert.IsTrue(writer.Faulted);
            Assert.AreEqual(0, writer.Position);
            Assert.AreEqual(
                writer.Position,
                WProtoSizes.LengthDelimitedSize(-1),
                "Measure and Write must agree on the byte count even for an invalid length, or a "
                    + "measured-then-written message carries a prefix that does not fit its payload."
            );
        }

        [Test]
        public void RawAndFixedWritesRoundTrip()
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteFixed32(0xDEADBEEF));
            Assert.IsTrue(writer.TryWriteFixed64(0x0123456789ABCDEFul));
            Assert.IsTrue(writer.TryWriteRaw(new byte[] { 0xAA, 0xBB }));
            Assert.IsTrue(writer.TryWriteLengthPrefix(2));
            Assert.IsTrue(writer.TryWriteRaw(new byte[] { 0x01, 0x02 }));
            Assert.IsFalse(writer.Faulted);
            Assert.AreEqual("EFBEADDEEFCDAB8967452301AABB020102", ToHex(writer.Written));
            Assert.AreEqual(ScratchSize - writer.Position, writer.Remaining);

            WProtoReader reader = new(writer.Written);
            Assert.IsTrue(reader.TryReadFixed32(out uint fixed32));
            Assert.AreEqual(0xDEADBEEF, fixed32);
            Assert.IsTrue(reader.TryReadFixed64(out ulong fixed64));
            Assert.AreEqual(0x0123456789ABCDEFul, fixed64);
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
            while (reader.TryReadTag(out int fieldNumber, out int wireType))
            {
                if (!reader.TrySkipField(fieldNumber, wireType))
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
            Assert.IsFalse(reader.TrySkipField(1, WProtoWireType.Varint));
        }

        [Test]
        public void AWriteThatDoesNotFitLeavesNoPartialField()
        {
            byte[] scratch = new byte[3];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteTag(1, WProtoWireType.LengthDelimited));
            Assert.IsFalse(writer.TryWriteString("too long for three bytes"));
            Assert.IsTrue(writer.Faulted);
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

        [TestCase(
            0,
            WProtoWireType.Varint,
            TestName = "ARefusedTagFaultsTheWriter(field number 0)"
        )]
        [TestCase(
            -1,
            WProtoWireType.Varint,
            TestName = "ARefusedTagFaultsTheWriter(negative field)"
        )]
        [TestCase(
            WProtoWireType.MaxFieldNumber + 1,
            WProtoWireType.Varint,
            TestName = "ARefusedTagFaultsTheWriter(field number too large)"
        )]
        [TestCase(1, 6, TestName = "ARefusedTagFaultsTheWriter(undefined wire type)")]
        public void ARefusedTagFaultsTheWriter(int fieldNumber, int wireType)
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsFalse(writer.TryWriteTag(fieldNumber, wireType));
            Assert.AreEqual(0, writer.Position);
            // Only when the FIELD NUMBER is what was rejected. TagSize is given a field number and
            // nothing else, so it cannot know the wire type is bad -- for a valid number it
            // correctly reports the size it would occupy, and holding it to the writer's verdict
            // there would be asserting against an input it never saw.
            if (WProtoWireType.IsDefined(wireType))
            {
                Assert.AreEqual(
                    writer.Position,
                    WProtoSizes.TagSize(fieldNumber),
                    "Measure and Write must agree on the byte count for a rejected field number "
                        + "too, or a caller sizing a message with an invalid tag under-allocates."
                );
            }

            // A skipped tag costs zero bytes, so WProtoSizes still agrees with the writer and an
            // enclosing length prefix is still correct -- the payload just decodes as a different
            // message. Nothing but the latch can report it, which is why a caller error has to latch
            // exactly like running out of room.
            Assert.IsTrue(writer.Faulted);
            Assert.IsFalse(
                writer.TryWriteFixed64(0x0108010801080108ul),
                "A faulted writer must refuse later writes; otherwise the value that lost its tag is "
                    + "emitted anyway and reads back as a well-formed, wrong message."
            );
            Assert.AreEqual(0, writer.Position);
        }

        [Test]
        public void RunningOutOfRoomMidMessageFaultsTheWriter()
        {
            // The reservation path is the writer's real latch -- TryWriteTag and TryWriteString
            // both pre-check and latch before reaching it, so nothing else exercises it. Without
            // it a refused value write is silently skipped and the NEXT field's tag byte is read
            // as the missing value: a truncated message that reports success and decodes as a
            // different, plausible one.
            byte[] scratch = new byte[5];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(writer.TryWriteTag(1, WProtoWireType.Varint));
            Assert.IsFalse(
                writer.TryWriteVarint64(ulong.MaxValue),
                "Ten bytes cannot fit in the four that are left."
            );
            Assert.IsTrue(writer.Faulted);
            Assert.AreEqual(1, writer.Position);

            Assert.IsFalse(writer.TryWriteTag(2, WProtoWireType.Varint));
            Assert.IsFalse(writer.TryWriteBool(true));
            Assert.IsFalse(
                writer.TryWriteRaw(ReadOnlySpan<byte>.Empty),
                "Even a zero-byte write must be refused once faulted; returning true would let a "
                    + "caller chaining writes conclude the message completed."
            );
            Assert.AreEqual(
                "08",
                ToHex(writer.Written),
                "Nothing may be appended after the fault."
            );
        }

        [Test]
        public void FixedWritesFaultWhenTheBufferIsTooSmall()
        {
            byte[] scratch = new byte[1];
            WProtoWriter writer = new(scratch);
            Assert.IsFalse(writer.TryWriteFixed64(0ul));
            Assert.IsTrue(writer.Faulted);
            Assert.AreEqual(0, writer.Position);
        }

        // BALANCED nesting, at and just past the bound. Unclosed openers would prove nothing:
        // they run out of input and latch whatever the bound is, so the case would pass against
        // an unbounded reader too. Only a well-formed payload separates "refused at depth 64"
        // from "recursed happily", and unbounded recursion here is a stack overflow, which cannot
        // be caught -- the bound is the entire guarantee.
        [TestCase(64, true, TestName = "GroupNestingIsBoundedAtTheDocumentedDepth(64 accepted)")]
        [TestCase(65, false, TestName = "GroupNestingIsBoundedAtTheDocumentedDepth(65 rejected)")]
        public void GroupNestingIsBoundedAtTheDocumentedDepth(int depth, bool expectSkipped)
        {
            byte[] payload = new byte[depth * 2];
            for (int index = 0; index < depth; index++)
            {
                payload[index] = 0x0B;
                payload[payload.Length - 1 - index] = 0x0C;
            }

            WProtoReader reader = new(payload);
            Assert.IsTrue(reader.TryReadTag(out int fieldNumber, out int wireType));
            Assert.AreEqual(WProtoWireType.StartGroup, wireType);
            Assert.AreEqual(expectSkipped, reader.TrySkipField(fieldNumber, wireType));
            Assert.AreEqual(!expectSkipped, reader.Malformed);
        }

        [Test]
        public void AFormatterThatIgnoresARefusedWriteStillFaultsTheMessage()
        {
            // A refused write leaves Position where it was, so a formatter that returns true anyway
            // produces a length prefix that correctly describes a payload with a member missing --
            // internally consistent and silently wrong, which is worse than a truncation. The
            // formatter interface is public and hand-implementable, so this is not hypothetical.
            byte[] scratch = new byte[6];
            WProtoWriter writer = new(scratch);

            Assert.IsFalse(writer.TryWriteMessage(1, new OptimisticFormatter(), 0));
            Assert.IsTrue(writer.Faulted);
        }

        [Test]
        public void AFormatterThatThrowsLeavesTheNestingDepthWhereItFoundIt()
        {
            // A formatter is contractually not allowed to throw, but this writer can outlive one
            // that does -- a caller may catch and keep writing. A depth left one too high lowers the
            // nesting bound for the rest of the message, silently, and only for deep payloads.
            // Assert.Throws is unusable here: a lambda cannot capture a ref struct.
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.AreEqual(0, writer.Depth);

            bool threw = false;
            try
            {
                writer.TryWriteMessage(1, new ThrowingFormatter(), 0);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            Assert.IsTrue(threw, "The test double did not throw, so this proves nothing.");
            Assert.AreEqual(0, writer.Depth, "The nesting depth was not restored.");
        }

        [TestCase(63, true, TestName = "TryWriteMessageIsBoundedAtTheDocumentedDepth(63 accepted)")]
        [TestCase(
            64,
            false,
            TestName = "TryWriteMessageIsBoundedAtTheDocumentedDepth(64 rejected)"
        )]
        public void TryWriteMessageIsBoundedAtTheDocumentedDepth(int depth, bool expectWritten)
        {
            // Symmetric with the reader's bound and with the one measurement applies: descending is
            // stack depth, and a stack overflow cannot be caught.
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.AreEqual(expectWritten, writer.TryWriteMessage(1, new ChainFormatter(depth), 0));
            Assert.AreEqual(!expectWritten, writer.Faulted);
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

        /// <summary>A formatter that reports success whether or not its writes were accepted.</summary>
        private sealed class OptimisticFormatter : IWProtoFormatter<int>
        {
            public int Measure(in int value)
            {
                return 32;
            }

            public bool Write(ref WProtoWriter writer, in int value)
            {
                writer.TryWriteTag(1, WProtoWireType.LengthDelimited);
                writer.TryWriteBytes(new byte[32]);
                return true;
            }

            public bool TryRead(ref WProtoReader reader, out int value)
            {
                value = 0;
                return true;
            }
        }

        /// <summary>A formatter that violates the no-throw contract, to prove the depth unwinds.</summary>
        private sealed class ThrowingFormatter : IWProtoFormatter<int>
        {
            public int Measure(in int value)
            {
                return 0;
            }

            public bool Write(ref WProtoWriter writer, in int value)
            {
                throw new InvalidOperationException("formatters are not allowed to do this");
            }

            public bool TryRead(ref WProtoReader reader, out int value)
            {
                value = 0;
                return true;
            }
        }

        /// <summary>Nests itself a fixed number of levels, to probe the writer's depth bound.</summary>
        private sealed class ChainFormatter : IWProtoFormatter<int>
        {
            private readonly int _remaining;

            internal ChainFormatter(int remaining)
            {
                _remaining = remaining;
            }

            public int Measure(in int value)
            {
                return 0;
            }

            public bool Write(ref WProtoWriter writer, in int value)
            {
                if (_remaining <= 0)
                {
                    return true;
                }

                return writer.TryWriteMessage(1, new ChainFormatter(_remaining - 1), value);
            }

            public bool TryRead(ref WProtoReader reader, out int value)
            {
                value = 0;
                return true;
            }
        }
    }
}
