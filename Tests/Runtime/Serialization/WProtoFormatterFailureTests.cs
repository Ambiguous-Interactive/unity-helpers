// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Globalization;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [TestFixture]
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class WProtoFormatterFailureTests
    {
        internal static IWProtoFormatter<string> CreateRefusingFormatter()
        {
            return new RefusingFormatter();
        }

        internal static void AssertCannotContinue(ref WProtoReader reader)
        {
            Assert.IsTrue(reader.Malformed);
            int position = reader.Position;
            Assert.IsFalse(reader.TryReadRemaining(out ReadOnlySpan<byte> remaining));
            Assert.AreEqual(0, remaining.Length);
            Assert.IsFalse(reader.TryReadTag(out _, out _));
            Assert.IsFalse(reader.TryReadVarint64(out _));
            Assert.AreEqual(position, reader.Position);
        }

        private static byte[] ParseHex(string hex)
        {
            Assert.AreEqual(0, hex.Length % 2, "Each test byte requires two hexadecimal digits.");
            byte[] bytes = new byte[hex.Length / 2];
            for (int index = 0; index < bytes.Length; ++index)
            {
                bytes[index] = byte.Parse(
                    hex.Substring(index * 2, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture
                );
            }
            return bytes;
        }

        private static void AssertRefused<T>(string hex, IWProtoFormatter<T> formatter)
        {
            WProtoReader reader = new WProtoReader(ParseHex(hex));
            WProtoReader copy = reader;
            Assert.IsFalse(formatter.TryRead(ref reader, out T value));
            Assert.AreEqual(default(T), value);
            AssertCannotContinue(ref reader);
            Assert.IsFalse(copy.Malformed);
        }

        [TestCase("183A")]
        [TestCase("18FE03")]
        public void DecimalSemanticFailureLatchesReader(string hex)
        {
            AssertRefused(hex, WProtoDecimalFormatter.Instance);
        }

        [TestCase("18030800", false)]
        [TestCase("18030800", true)]
        [TestCase("1006", false)]
        [TestCase("1006", true)]
        [TestCase("0804100F", false)]
        [TestCase("0804100F", true)]
        [TestCase("08FEFFFFFFFFFFFFFFFF01", false)]
        [TestCase("08FEFFFFFFFFFFFFFFFF01", true)]
        public void TemporalSemanticFailureLatchesReader(string hex, bool duration)
        {
            if (duration)
            {
                AssertRefused(hex, WProtoTimeSpanFormatter.Instance);
            }
            else
            {
                AssertRefused(hex, WProtoDateTimeFormatter.Instance);
            }
        }

        [Test]
        public void DateTimeRangeFailureLatchesReader()
        {
            AssertRefused("08FEFFFFFFFFFFFFFFFF011005", WProtoDateTimeFormatter.Instance);
        }

        [TestCase("FFFE")]
        [TestCase("68747470733A2F2F4558414D504C452E636F6D3AEFBFBD2F")]
        public void UriDecodingFailureLatchesReader(string hex)
        {
            AssertRefused(hex, WProtoUriFormatter.Instance);
        }

        [TestCase("")]
        [TestCase("/")]
        [TestCase("relative/path")]
        [TestCase("https://example.com:443/a")]
        public void UriEmptyAndRelativePolicyIsPreserved(string text)
        {
            bool accepted = text.Length != 0;
            WProtoReader reader = new WProtoReader(Encoding.UTF8.GetBytes(text));
            Assert.AreEqual(
                accepted,
                WProtoUriFormatter.Instance.TryRead(ref reader, out Uri value)
            );
            if (accepted)
            {
                Assert.AreEqual(text, value.OriginalString);
                Assert.IsFalse(reader.Malformed);
            }
            else
            {
                Assert.IsTrue(value == null);
                AssertCannotContinue(ref reader);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ValidTemporalKindsRemainAccepted(byte kind)
        {
            WProtoReader dateReader = new WProtoReader(new byte[] { 0x18, kind });
            Assert.IsTrue(
                WProtoDateTimeFormatter.Instance.TryRead(ref dateReader, out DateTime date)
            );
            Assert.AreEqual((DateTimeKind)kind, date.Kind);
            Assert.IsFalse(dateReader.Malformed);
            WProtoReader spanReader = new WProtoReader(new byte[] { 0x18, kind });
            Assert.IsTrue(
                WProtoTimeSpanFormatter.Instance.TryRead(ref spanReader, out TimeSpan span)
            );
            Assert.AreEqual(TimeSpan.Zero, span);
            Assert.IsFalse(spanReader.Malformed);
        }

        [Test]
        public void MaximumDecimalScaleRemainsAccepted()
        {
            WProtoReader decimalReader = new WProtoReader(new byte[] { 0x08, 1, 0x18, 56 });
            Assert.IsTrue(
                WProtoDecimalFormatter.Instance.TryRead(ref decimalReader, out decimal amount)
            );
            Assert.AreEqual(0.0000000000000000000000000001m, amount);
            Assert.IsFalse(decimalReader.Malformed);
        }

        [Test]
        public void GenericScalarForwardingLatchesFailureAndClearsPartialOutput()
        {
            if (!WProtoScalarFormatterProvider.TryGet(out IWProtoScalarFormatter<int> previous))
            {
                previous = null;
            }
            try
            {
                WProtoScalarFormatterProvider.Register<int>(new RefusingFormatter());
                WProtoReader reader = new WProtoReader(new byte[] { 7, 9 });
                Assert.IsFalse(WProtoGeneric<int>.TryReadValue(ref reader, out int value));
                Assert.AreEqual(0, value);
                Assert.AreEqual(1, reader.Position);
                AssertCannotContinue(ref reader);
            }
            finally
            {
                WProtoScalarFormatterProvider.Register(previous);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeclaredRootReadFailureLatchesWithOrWithoutAFormatter(bool registered)
        {
            if (!WProtoFormatterProvider.TryGet(out IWProtoFormatter<string> previous))
            {
                previous = null;
            }
            try
            {
                WProtoFormatterProvider.Register<string>(
                    registered ? CreateRefusingFormatter() : null
                );
                WProtoDeclaredRootFormatter<object, string> formatter =
                    new WProtoDeclaredRootFormatter<object, string>();
                WProtoReader reader = new WProtoReader(new byte[] { 7, 9 });
                Assert.IsFalse(formatter.TryRead(ref reader, out object value));
                Assert.IsTrue(value == null);
                Assert.AreEqual(registered ? 1 : 0, reader.Position);
                AssertCannotContinue(ref reader);
            }
            finally
            {
                WProtoFormatterProvider.Register(previous);
            }
        }

        [Test]
        public void UnservedFacadeReadStillDeclinesBeforeDecoding()
        {
            Assert.IsFalse(
                WProtoFacade.TryDeserialize(
                    new byte[] { 0xFF },
                    out WProtoFormatterFailureTests value
                )
            );
            Assert.IsTrue(value == null);
        }

        private readonly struct RefusingFormatter
            : IWProtoScalarFormatter<int>,
                IWProtoFormatter<string>
        {
            public int WireType => WProtoWireType.Varint;

            public bool IsDefault(in int value)
            {
                return value == 0;
            }

            public int MeasureValue(in int value)
            {
                return 0;
            }

            public bool WriteValue(ref WProtoWriter writer, in int value)
            {
                return false;
            }

            public bool TryReadValue(ref WProtoReader reader, out int value)
            {
                reader.TryReadInt32(out _);
                value = 42;
                return false;
            }

            public int Measure(in string value)
            {
                return 0;
            }

            public bool Write(ref WProtoWriter writer, in string value)
            {
                return false;
            }

            public bool TryRead(ref WProtoReader reader, out string value)
            {
                reader.TryReadInt32(out _);
                value = "partial";
                return false;
            }
        }
    }
}
