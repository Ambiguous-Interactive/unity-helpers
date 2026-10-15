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
    public sealed class WProtoEnumFormatterTests
    {
        private static void AssertShape<T>(int size, bool signed)
            where T : struct
        {
            Assert.Throws<ArgumentException>(() => WProtoScalarFormatters.Enum<T>(size, !signed));
            foreach (int invalidSize in new[] { 1, 2, 4, 8 })
            {
                if (invalidSize != size)
                {
                    Assert.Throws<ArgumentException>(() =>
                        WProtoScalarFormatters.Enum<T>(invalidSize, signed)
                    );
                }
            }

            Assert.That(WProtoScalarFormatters.Enum<T>(size, signed), Is.Not.Null);
        }

        private static void AssertWire<T>(T value, int size, bool signed, long numeric)
            where T : struct
        {
            IWProtoScalarFormatter<T> formatter = WProtoScalarFormatters.Enum<T>(size, signed);
            Span<byte> expected = stackalloc byte[10];
            Span<byte> actual = stackalloc byte[10];
            WProtoWriter reference = new WProtoWriter(expected);
            Assert.That(reference.TryWriteInt64(numeric), Is.True);
            WProtoWriter writer = new WProtoWriter(actual);
            Assert.That(formatter.WriteValue(ref writer, in value), Is.True);
            Assert.That(writer.Written.SequenceEqual(reference.Written), Is.True, typeof(T).Name);
            Assert.That(formatter.MeasureValue(in value), Is.EqualTo(writer.Written.Length));
            Assert.That(formatter.IsDefault(in value), Is.EqualTo(numeric == 0));
            WProtoReader reader = new WProtoReader(writer.Written);
            Assert.That(formatter.TryReadValue(ref reader, out T result), Is.True);
            Assert.That(result, Is.EqualTo(value));
        }

        [Test]
        public void ReferenceContainingStructCannotAcquireEnumFormatter()
        {
            Assert.Throws<ArgumentException>(() =>
                WProtoScalarFormatters.Enum<ReferenceSlot>(IntPtr.Size, false)
            );
        }

        [Test]
        public void NonEnumValueTypesCannotAcquireEnumFormatter()
        {
            Assert.Throws<ArgumentException>(() => WProtoScalarFormatters.Enum<byte>(1, false));
            Assert.Throws<ArgumentException>(() => WProtoScalarFormatters.Enum<short>(2, true));
            Assert.Throws<ArgumentException>(() => WProtoScalarFormatters.Enum<int>(4, true));
            Assert.Throws<ArgumentException>(() => WProtoScalarFormatters.Enum<long>(8, true));
            Assert.Throws<ArgumentException>(() => WProtoScalarFormatters.Enum<float>(4, true));
            Assert.Throws<ArgumentException>(() => WProtoScalarFormatters.Enum<double>(8, true));
            Assert.Throws<ArgumentException>(() => WProtoScalarFormatters.Enum<bool>(1, false));
            Assert.Throws<ArgumentException>(() => WProtoScalarFormatters.Enum<char>(2, false));
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(3)]
        [TestCase(16)]
        [TestCase(int.MaxValue)]
        public void InvalidSizeIsRejected(int size)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                WProtoScalarFormatters.Enum<SignedInt>(size, true)
            );
        }

        [Test]
        public void EveryUnderlyingTypeRejectsIncorrectSizeAndSignedness()
        {
            AssertShape<SignedByte>(1, true);
            AssertShape<UnsignedByte>(1, false);
            AssertShape<SignedShort>(2, true);
            AssertShape<UnsignedShort>(2, false);
            AssertShape<SignedInt>(4, true);
            AssertShape<UnsignedInt>(4, false);
            AssertShape<SignedLong>(8, true);
            AssertShape<UnsignedLong>(8, false);
        }

        [Test]
        public void EveryUnderlyingTypePreservesBoundariesAndUndefinedWireValues()
        {
            AssertWire((SignedByte)sbyte.MinValue, 1, true, -128);
            AssertWire((SignedByte)sbyte.MaxValue, 1, true, 127);
            AssertWire((UnsignedByte)byte.MaxValue, 1, false, 255);
            AssertWire((SignedShort)short.MinValue, 2, true, -32768);
            AssertWire((SignedShort)short.MaxValue, 2, true, 32767);
            AssertWire((UnsignedShort)ushort.MaxValue, 2, false, 65535);
            AssertWire((SignedInt)int.MinValue, 4, true, int.MinValue);
            AssertWire((SignedInt)int.MaxValue, 4, true, int.MaxValue);
            AssertWire((UnsignedInt)uint.MaxValue, 4, false, -1);
            AssertWire((SignedLong)long.MinValue, 8, true, long.MinValue);
            AssertWire((SignedLong)long.MaxValue, 8, true, long.MaxValue);
            AssertWire((UnsignedLong)ulong.MaxValue, 8, false, -1);
            AssertWire(SignedByte.Alias, 1, true, 1);
            AssertWire(SignedByte.First | SignedByte.Second, 1, true, 3);
            AssertWire(default(SignedByte), 1, true, 0);
            AssertWire(default(UnsignedLong), 8, false, 0);
        }

        [Test]
        public void EveryBytePatternPreservesSignedAndUnsignedValues()
        {
            for (int bits = 0; bits <= byte.MaxValue; ++bits)
            {
                AssertWire((SignedByte)unchecked((sbyte)bits), 1, true, unchecked((sbyte)bits));
                AssertWire((UnsignedByte)bits, 1, false, bits);
            }
        }

        [TestCase(17)]
        [TestCase(637)]
        public void RandomizedUnderlyingBitsPreserveWireAndValues(int seed)
        {
            Random random = new Random(seed);
            byte[] bytes = new byte[8];
            for (int index = 0; index < 128; ++index)
            {
                random.NextBytes(bytes);
                ulong bits = BitConverter.ToUInt64(bytes, 0);
                AssertWire((SignedByte)unchecked((sbyte)bits), 1, true, unchecked((sbyte)bits));
                AssertWire((UnsignedByte)unchecked((byte)bits), 1, false, unchecked((byte)bits));
                AssertWire((SignedShort)unchecked((short)bits), 2, true, unchecked((short)bits));
                AssertWire(
                    (UnsignedShort)unchecked((ushort)bits),
                    2,
                    false,
                    unchecked((ushort)bits)
                );
                AssertWire((SignedInt)unchecked((int)bits), 4, true, unchecked((int)bits));
                AssertWire((UnsignedInt)unchecked((uint)bits), 4, false, unchecked((int)bits));
                AssertWire((SignedLong)unchecked((long)bits), 8, true, unchecked((long)bits));
                AssertWire((UnsignedLong)bits, 8, false, unchecked((long)bits));
            }
        }

        [Test]
        public void TruncatedVarintsReturnFailureWithoutProducingAnEnum()
        {
            IWProtoScalarFormatter<SignedLong> formatter = WProtoScalarFormatters.Enum<SignedLong>(
                8,
                true
            );
            WProtoReader reader = new WProtoReader(new byte[] { 0x80 });
            Assert.That(formatter.TryReadValue(ref reader, out SignedLong value), Is.False);
            Assert.That(value, Is.EqualTo(default(SignedLong)));
        }

        private struct ReferenceSlot
        {
            public object Value { get; set; }
        }

        [Flags]
        public enum SignedByte : sbyte
        {
            [Obsolete("Use a specific value.")]
            None = 0,
            First = 1,
            Alias = First,
            Second = 2,
        }

        public enum UnsignedByte : byte
        {
            [Obsolete("Use a specific value.")]
            None = 0,
            First = 1,
        }

        public enum SignedShort : short
        {
            [Obsolete("Use a specific value.")]
            None = 0,
            First = 1,
        }

        public enum UnsignedShort : ushort
        {
            [Obsolete("Use a specific value.")]
            None = 0,
            First = 1,
        }

        public enum SignedInt : int
        {
            [Obsolete("Use a specific value.")]
            None = 0,
            First = 1,
        }

        public enum UnsignedInt : uint
        {
            [Obsolete("Use a specific value.")]
            None = 0,
            First = 1,
        }

        public enum SignedLong : long
        {
            [Obsolete("Use a specific value.")]
            None = 0,
            First = 1,
        }

        public enum UnsignedLong : ulong
        {
            [Obsolete("Use a specific value.")]
            None = 0,
            First = 1,
        }
    }
}
