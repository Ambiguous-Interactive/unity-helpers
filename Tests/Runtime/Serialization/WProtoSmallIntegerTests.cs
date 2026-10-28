// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [TestFixture]
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class WProtoSmallIntegerTests
    {
        internal static byte[] Encode(long numeric, int tag = 0, bool zigZag = false)
        {
            byte[] bytes = new byte[32];
            WProtoWriter writer = new WProtoWriter(bytes);
            if (0 < tag)
            {
                Assert.IsTrue(writer.TryWriteTag(tag, WProtoWireType.Varint));
            }
            Assert.IsTrue(
                zigZag ? writer.TryWriteZigZag64(numeric) : writer.TryWriteInt64(numeric)
            );
            return writer.Written.ToArray();
        }

        internal static byte[] Wrap(int tag, byte[] payload)
        {
            byte[] bytes = new byte[payload.Length + 16];
            WProtoWriter writer = new WProtoWriter(bytes);
            Assert.IsTrue(writer.TryWriteTag(tag, WProtoWireType.LengthDelimited));
            Assert.IsTrue(writer.TryWriteBytes(payload));
            return writer.Written.ToArray();
        }

        internal static WProtoSmallIntegerContract CreateBoundaryValue()
        {
            return new WProtoSmallIntegerContract
            {
                Byte = byte.MaxValue,
                SignedByte = sbyte.MinValue,
                Short = short.MinValue,
                UnsignedShort = ushort.MaxValue,
                Character = char.MaxValue,
                NullableShort = short.MaxValue,
                Shorts = new System.Collections.Generic.List<short>
                {
                    short.MinValue,
                    0,
                    short.MaxValue,
                },
                Bytes = new System.Collections.Generic.List<byte> { 0, byte.MaxValue },
                Values = new System.Collections.Generic.Dictionary<short, byte>
                {
                    [short.MinValue] = byte.MaxValue,
                },
                ZigZagShort = short.MinValue,
                ZigZagSignedByte = sbyte.MinValue,
                NullableZigZagShort = short.MaxValue,
                SignedBytes = new System.Collections.Generic.List<sbyte>
                {
                    sbyte.MinValue,
                    0,
                    sbyte.MaxValue,
                },
                UnsignedShorts = new System.Collections.Generic.List<ushort> { 0, ushort.MaxValue },
                Characters = new System.Collections.Generic.List<char>
                {
                    char.MinValue,
                    char.MaxValue,
                },
                CharactersByNumber = new System.Collections.Generic.Dictionary<ushort, char>
                {
                    [ushort.MaxValue] = char.MaxValue,
                },
                ShortsBySignedByte = new System.Collections.Generic.Dictionary<sbyte, short>
                {
                    [sbyte.MinValue] = short.MaxValue,
                },
            };
        }

        private static void AssertGeneratedRefused(byte[] payload)
        {
            WProtoReader reader = new WProtoReader(payload);
            IWProtoFormatter<WProtoSmallIntegerContract> formatter =
                WProtoFormatterProvider.Get<WProtoSmallIntegerContract>();
            Assert.IsTrue(formatter != null);
            Assert.IsFalse(formatter.TryRead(ref reader, out WProtoSmallIntegerContract value));
            Assert.IsTrue(value == null);
            Assert.IsTrue(reader.Malformed);
            int position = reader.Position;
            Assert.IsFalse(reader.TryReadVarint32(out _));
            Assert.AreEqual(position, reader.Position);
        }

        private static void AssertScalarRefused<T>(long numeric)
        {
            WProtoScalarFormatters.RegisterAll();
            Assert.IsTrue(
                WProtoScalarFormatterProvider.TryGet(out IWProtoScalarFormatter<T> formatter)
            );
            WProtoReader reader = new WProtoReader(Encode(numeric));
            Assert.IsFalse(formatter.TryReadValue(ref reader, out T value));
            Assert.AreEqual(default(T), value);
            Assert.IsTrue(reader.Malformed);
            int position = reader.Position;
            Assert.IsFalse(reader.TryReadVarint32(out _));
            Assert.AreEqual(position, reader.Position);
        }

        private static void AssertMalformedScalar<T>(byte[] bytes)
        {
            WProtoScalarFormatters.RegisterAll();
            Assert.IsTrue(
                WProtoScalarFormatterProvider.TryGet(out IWProtoScalarFormatter<T> formatter)
            );
            WProtoReader reader = new WProtoReader(bytes);
            Assert.IsFalse(formatter.TryReadValue(ref reader, out T value));
            Assert.AreEqual(default(T), value);
            Assert.IsTrue(reader.Malformed);
        }

        private static void AssertScalarAccepted<T>(long numeric, T expected)
        {
            WProtoScalarFormatters.RegisterAll();
            Assert.IsTrue(
                WProtoScalarFormatterProvider.TryGet(out IWProtoScalarFormatter<T> formatter)
            );
            WProtoReader reader = new WProtoReader(Encode(numeric));
            Assert.IsTrue(formatter.TryReadValue(ref reader, out T value));
            Assert.AreEqual(expected, value);
            Assert.IsFalse(reader.Malformed);
            Assert.IsTrue(reader.End);
        }

        [TestCase(256)]
        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public void ByteOverflowIsRefused(long numeric)
        {
            AssertScalarRefused<byte>(numeric);
        }

        [TestCase(-129)]
        [TestCase(128)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void SignedByteOverflowIsRefused(long numeric)
        {
            AssertScalarRefused<sbyte>(numeric);
        }

        [TestCase(-32769)]
        [TestCase(32768)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void ShortOverflowIsRefused(long numeric)
        {
            AssertScalarRefused<short>(numeric);
        }

        [TestCase(-1)]
        [TestCase(65536)]
        [TestCase(int.MaxValue)]
        public void UnsignedShortAndCharOverflowIsRefused(long numeric)
        {
            AssertScalarRefused<ushort>(numeric);
            AssertScalarRefused<char>(numeric);
        }

        [Test]
        public void ValidBoundariesRemainReadable()
        {
            AssertScalarAccepted(0, (byte)0);
            AssertScalarAccepted(255, byte.MaxValue);
            AssertScalarAccepted(-128, sbyte.MinValue);
            AssertScalarAccepted(127, sbyte.MaxValue);
            AssertScalarAccepted(-32768, short.MinValue);
            AssertScalarAccepted(32767, short.MaxValue);
            AssertScalarAccepted(0, (ushort)0);
            AssertScalarAccepted(65535, ushort.MaxValue);
            AssertScalarAccepted(0, char.MinValue);
            AssertScalarAccepted(65535, char.MaxValue);
        }

        [TestCase(1, 256)]
        [TestCase(2, 128)]
        [TestCase(2, -129)]
        [TestCase(3, 32768)]
        [TestCase(3, -32769)]
        [TestCase(4, 65536)]
        [TestCase(5, 65536)]
        [TestCase(6, 32768)]
        [TestCase(7, 32768)]
        [TestCase(8, 256)]
        [TestCase(10, 32768)]
        [TestCase(10, -32769)]
        [TestCase(11, 128)]
        [TestCase(11, -129)]
        [TestCase(12, 32768)]
        [TestCase(13, 128)]
        [TestCase(14, 65536)]
        [TestCase(15, 65536)]
        public void GeneratedMembersRefuseOverflow(int tag, long numeric)
        {
            bool zigZag = tag == 10 || tag == 11 || tag == 12;
            AssertGeneratedRefused(Encode(numeric, tag, zigZag));
        }

        [TestCase(7, 32768)]
        [TestCase(7, -32769)]
        [TestCase(8, 256)]
        [TestCase(13, 128)]
        [TestCase(13, -129)]
        [TestCase(14, 65536)]
        public void PackedMembersRefuseOverflow(int tag, long numeric)
        {
            AssertGeneratedRefused(Wrap(tag, Encode(numeric)));
        }

        [TestCase(9, 1, 32768)]
        [TestCase(9, 2, 256)]
        [TestCase(16, 1, 65536)]
        [TestCase(16, 2, 65536)]
        [TestCase(17, 1, 128)]
        [TestCase(17, 1, -129)]
        [TestCase(17, 2, 32768)]
        [TestCase(17, 2, -32769)]
        public void MapKeysAndValuesRefuseOverflow(int tag, int entryTag, long numeric)
        {
            AssertGeneratedRefused(Wrap(tag, Encode(numeric, entryTag)));
        }

        [Test]
        public void CharacterMessageFormatterRefusesOverflow()
        {
            WProtoReader reader = new WProtoReader(Encode(65536));
            Assert.IsFalse(WProtoCharFormatter.Instance.TryRead(ref reader, out char value));
            Assert.AreEqual(default(char), value);
            Assert.IsTrue(reader.Malformed);
        }

        [Test]
        public void GeneratedBoundariesPreserveWireBytes()
        {
            WProtoSmallIntegerContract value = CreateBoundaryValue();
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out byte[] bytes));
            Assert.IsTrue(WProtoFacade.TryDeserialize(bytes, out WProtoSmallIntegerContract read));
            Assert.AreEqual(value.Byte, read.Byte);
            Assert.AreEqual(value.SignedByte, read.SignedByte);
            Assert.AreEqual(value.Short, read.Short);
            Assert.AreEqual(value.UnsignedShort, read.UnsignedShort);
            Assert.AreEqual(value.Character, read.Character);
            Assert.AreEqual(value.NullableShort, read.NullableShort);
            Assert.AreEqual(value.ZigZagShort, read.ZigZagShort);
            Assert.AreEqual(value.ZigZagSignedByte, read.ZigZagSignedByte);
            Assert.AreEqual(value.NullableZigZagShort, read.NullableZigZagShort);
            CollectionAssert.AreEqual(value.Shorts, read.Shorts);
            CollectionAssert.AreEqual(value.Bytes, read.Bytes);
            CollectionAssert.AreEqual(value.Values, read.Values);
            CollectionAssert.AreEqual(value.SignedBytes, read.SignedBytes);
            CollectionAssert.AreEqual(value.UnsignedShorts, read.UnsignedShorts);
            CollectionAssert.AreEqual(value.Characters, read.Characters);
            CollectionAssert.AreEqual(value.CharactersByNumber, read.CharactersByNumber);
            CollectionAssert.AreEqual(value.ShortsBySignedByte, read.ShortsBySignedByte);
            Assert.IsTrue(WProtoFacade.TrySerialize(read, out byte[] rewritten));
            CollectionAssert.AreEqual(bytes, rewritten);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EmptyAndTruncatedSmallScalarsRefuseAndClearOutput(bool empty)
        {
            byte[] bytes = empty ? Array.Empty<byte>() : new byte[] { 0x80 };
            AssertMalformedScalar<byte>(bytes);
            AssertMalformedScalar<sbyte>(bytes);
            AssertMalformedScalar<short>(bytes);
            AssertMalformedScalar<ushort>(bytes);
            AssertMalformedScalar<char>(bytes);
        }

        [TestCase(-129, false)]
        [TestCase(128, false)]
        [TestCase(-128, true)]
        [TestCase(127, true)]
        public void ZigZagSignedByteChecksItsRange(int numeric, bool accepted)
        {
            WProtoReader reader = new WProtoReader(Encode(numeric, zigZag: true));
            Assert.AreEqual(accepted, reader.TryReadZigZagSByte(out sbyte value));
            Assert.AreEqual(accepted ? (sbyte)numeric : default(sbyte), value);
            Assert.AreEqual(!accepted, reader.Malformed);
        }

        [TestCase(-32769, false)]
        [TestCase(32768, false)]
        [TestCase(-32768, true)]
        [TestCase(32767, true)]
        public void ZigZagShortChecksItsRange(int numeric, bool accepted)
        {
            WProtoReader reader = new WProtoReader(Encode(numeric, zigZag: true));
            Assert.AreEqual(accepted, reader.TryReadZigZagInt16(out short value));
            Assert.AreEqual(accepted ? (short)numeric : default(short), value);
            Assert.AreEqual(!accepted, reader.Malformed);
        }

        [Test]
        public void FailedReaderRefusesEverySmallIntegerReadWithoutConsumption()
        {
            WProtoReader reader = new WProtoReader(new byte[] { 0x01 });
            reader.MarkMalformed();
            Assert.IsFalse(reader.TryReadByte(out byte unsignedByte));
            Assert.IsFalse(reader.TryReadSByte(out sbyte signedByte));
            Assert.IsFalse(reader.TryReadInt16(out short signedShort));
            Assert.IsFalse(reader.TryReadUInt16(out ushort unsignedShort));
            Assert.IsFalse(reader.TryReadChar(out char character));
            Assert.IsFalse(reader.TryReadZigZagSByte(out sbyte zigZagByte));
            Assert.IsFalse(reader.TryReadZigZagInt16(out short zigZagShort));
            Assert.AreEqual(0, unsignedByte);
            Assert.AreEqual(0, signedByte);
            Assert.AreEqual(0, signedShort);
            Assert.AreEqual(0, unsignedShort);
            Assert.AreEqual(default(char), character);
            Assert.AreEqual(0, zigZagByte);
            Assert.AreEqual(0, zigZagShort);
            Assert.AreEqual(0, reader.Position);
        }
    }
}
