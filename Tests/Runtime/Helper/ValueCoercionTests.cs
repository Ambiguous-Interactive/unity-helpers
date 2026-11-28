// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    [Category("Fast")]
    public sealed class ValueCoercionTests
    {
        [Test]
        public void SameTypePassesThrough()
        {
            Assert.IsTrue(ValueCoercion.TryCoerce(typeof(int), 42, out object integer));
            Assert.AreEqual(42, integer);
            Assert.IsTrue(ValueCoercion.TryCoerce(typeof(string), "text", out object text));
            Assert.AreEqual("text", text);
            Assert.IsTrue(ValueCoercion.TryCoerce(typeof(Sample), Sample.Second, out object value));
            Assert.AreEqual(Sample.Second, value);
        }

        [Test]
        public void StringsParseWithTheInvariantCulture()
        {
            Assert.IsTrue(ValueCoercion.TryCoerce(typeof(int), "42", out object integer));
            Assert.AreEqual(42, integer);
            Assert.IsTrue(ValueCoercion.TryCoerce(typeof(float), "1.5", out object single));
            Assert.AreEqual(1.5f, single);
            Assert.IsTrue(ValueCoercion.TryCoerce(typeof(double), "1.5", out object number));
            Assert.AreEqual(1.5d, number);
        }

        [Test]
        public void EnumsAcceptNamesAndNumbers()
        {
            Assert.IsTrue(ValueCoercion.TryCoerce(typeof(Sample), "Second", out object named));
            Assert.AreEqual(Sample.Second, named);
            Assert.IsTrue(ValueCoercion.TryCoerce(typeof(Sample), "2", out object numeric));
            Assert.AreEqual(Sample.Second, numeric);
            Assert.IsTrue(ValueCoercion.TryCoerce(typeof(Sample), 2, out object converted));
            Assert.AreEqual(Sample.Second, converted);
        }

        [Test]
        public void BoolParsesCommonStrings()
        {
            Assert.IsTrue(ValueCoercion.TryCoerce(typeof(bool), "true", out object yes));
            Assert.AreEqual(true, yes);
            Assert.IsTrue(ValueCoercion.TryCoerce(typeof(bool), "False", out object no));
            Assert.AreEqual(false, no);
        }

        [Test]
        public void NullableTargetsAcceptNullAndValues()
        {
            Assert.IsTrue(ValueCoercion.TryCoerce(typeof(int?), null, out object empty));
            Assert.IsTrue(empty == null);
            Assert.IsTrue(ValueCoercion.TryCoerce(typeof(int?), "5", out object five));
            Assert.AreEqual(5, five);
        }

        [Test]
        public void NullIsRejectedForNonNullableTargetsAndAcceptedForReferences()
        {
            Assert.IsFalse(ValueCoercion.TryCoerce(typeof(int), null, out object value));
            Assert.IsTrue(value == null);
            Assert.IsTrue(ValueCoercion.TryCoerce(typeof(string), null, out object text));
            Assert.IsTrue(text == null);
            Assert.IsTrue(ValueCoercion.TryCoerce(typeof(object), null, out object anything));
            Assert.IsTrue(anything == null);
        }

        [Test]
        public void InvalidConversionsReturnFalse()
        {
            Assert.IsFalse(ValueCoercion.TryCoerce(typeof(int), "abc", out object number));
            Assert.IsTrue(number == null);
            Assert.IsFalse(
                ValueCoercion.TryCoerce(typeof(Sample), "Missing", out object enumValue)
            );
            Assert.IsTrue(enumValue == null);
            Assert.IsFalse(
                ValueCoercion.TryCoerce(typeof(int), new object(), out object objectValue)
            );
            Assert.IsTrue(objectValue == null);
            Assert.IsFalse(ValueCoercion.TryCoerce(typeof(ByteSample), 256, out object tooLarge));
            Assert.IsTrue(tooLarge == null);
        }

        [Test]
        public void StringTargetsUseTheInvariantCulture()
        {
            Assert.IsTrue(ValueCoercion.TryCoerce(typeof(string), 42, out object integer));
            Assert.AreEqual("42", integer);
            Assert.IsTrue(ValueCoercion.TryCoerce(typeof(string), 1.5d, out object number));
            Assert.AreEqual("1.5", number);
        }

        [Test]
        public void UnsignedEnumValuesAboveLongMaximumAreCoerced()
        {
            Assert.IsTrue(
                ValueCoercion.TryCoerce(typeof(ULongSample), ulong.MaxValue, out object value)
            );
            Assert.AreEqual(ULongSample.Max, value);
            Assert.IsTrue(
                ValueCoercion.TryCoerce(
                    typeof(ULongSample),
                    "18446744073709551615",
                    out object parsed
                )
            );
            Assert.AreEqual(ULongSample.Max, parsed);
        }

        [Test]
        public void FractionalEnumInputsAreRejected()
        {
            Assert.IsFalse(ValueCoercion.TryCoerce(typeof(Sample), 1.5d, out object fractional));
            Assert.IsNull(fractional);
            Assert.IsTrue(ValueCoercion.TryCoerce(typeof(Sample), 2d, out object integral));
            Assert.AreEqual(Sample.Second, integral);
        }

        [Test]
        public void ThrowingConvertibleReturnsFalse()
        {
            ThrowingConvertible input = new();
            Assert.IsFalse(ValueCoercion.TryCoerce(typeof(Sample), input, out object enumValue));
            Assert.IsNull(enumValue);
            Assert.IsFalse(ValueCoercion.TryCoerce(typeof(string), input, out object text));
            Assert.IsNull(text);
        }

        [Test]
        public void NullTargetReturnsFalse()
        {
            Assert.IsFalse(ValueCoercion.TryCoerce(null, 1, out object value));
            Assert.IsTrue(value == null);
        }

        private enum Sample
        {
            Unknown = 0,
            First = 1,
            Second = 2,
        }

        private enum ByteSample : byte
        {
            Zero = 0,
            Max = 255,
        }

        private enum ULongSample : ulong
        {
            Zero = 0,
            Max = ulong.MaxValue,
        }

        private sealed class ThrowingConvertible : IConvertible
        {
            public TypeCode GetTypeCode()
            {
                return TypeCode.Object;
            }

            public bool ToBoolean(IFormatProvider provider)
            {
                throw new NotSupportedException();
            }

            public byte ToByte(IFormatProvider provider)
            {
                throw new NotSupportedException();
            }

            public char ToChar(IFormatProvider provider)
            {
                throw new NotSupportedException();
            }

            public DateTime ToDateTime(IFormatProvider provider)
            {
                throw new NotSupportedException();
            }

            public decimal ToDecimal(IFormatProvider provider)
            {
                throw new NotSupportedException();
            }

            public double ToDouble(IFormatProvider provider)
            {
                throw new NotSupportedException();
            }

            public short ToInt16(IFormatProvider provider)
            {
                throw new NotSupportedException();
            }

            public int ToInt32(IFormatProvider provider)
            {
                throw new NotSupportedException();
            }

            public long ToInt64(IFormatProvider provider)
            {
                throw new NotSupportedException();
            }

            public sbyte ToSByte(IFormatProvider provider)
            {
                throw new NotSupportedException();
            }

            public float ToSingle(IFormatProvider provider)
            {
                throw new NotSupportedException();
            }

            public string ToString(IFormatProvider provider)
            {
                throw new InvalidCastException();
            }

            public object ToType(Type conversionType, IFormatProvider provider)
            {
                throw new NotSupportedException();
            }

            public ushort ToUInt16(IFormatProvider provider)
            {
                throw new NotSupportedException();
            }

            public uint ToUInt32(IFormatProvider provider)
            {
                throw new NotSupportedException();
            }

            public ulong ToUInt64(IFormatProvider provider)
            {
                throw new NotSupportedException();
            }
        }
    }
}
