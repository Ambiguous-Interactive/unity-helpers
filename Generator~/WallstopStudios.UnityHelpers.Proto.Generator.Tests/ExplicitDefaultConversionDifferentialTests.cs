// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Verifies the attribute and member conversions against each original serializer.</summary>
    [TestFixture]
    public sealed class ExplicitDefaultConversionDifferentialTests
    {
        private static void AssertValue(
            ExplicitDefaultConversionContract expected,
            ExplicitDefaultConversionContract actual
        )
        {
            Assert.IsTrue(actual != null);
            Assert.AreEqual(expected.Rounded, actual.Rounded);
            Assert.AreEqual(expected.Hexadecimal, actual.Hexadecimal);
            Assert.AreEqual(expected.Numeric, actual.Numeric);
            Assert.AreEqual(expected.Boolean, actual.Boolean);
            Assert.AreEqual(expected.Character, actual.Character);
            Assert.AreEqual(expected.Flags, actual.Flags);
            Assert.AreEqual(expected.Text, actual.Text);
            Assert.AreEqual(expected.Fractional, actual.Fractional);
            Assert.AreEqual(expected.EnumText, actual.EnumText);
            Assert.AreEqual(expected.TypedEnumText, actual.TypedEnumText);
        }

        private static byte[] OracleBytes(ExplicitDefaultConversionContract value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(stream, value);
                return stream.ToArray();
            }
        }

        private static ExplicitDefaultConversionContract OracleRead(byte[] bytes)
        {
            using (MemoryStream stream = new MemoryStream(bytes))
            {
                return ProtoBuf.Serializer.Deserialize<ExplicitDefaultConversionContract>(stream);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void AttributeConversionPrecedesMemberConversionForBytesAndCrossReads(int mode)
        {
            ExplicitDefaultConversionContract value = new ExplicitDefaultConversionContract();
            if (mode == 1)
            {
                value.Rounded = 16777217;
            }
            else if (mode == 2)
            {
                value.Rounded = 0;
                value.Hexadecimal = 0;
                value.Numeric = DayOfWeek.Sunday;
                value.Boolean = DayOfWeek.Sunday;
                value.Character = DayOfWeek.Sunday;
                value.Flags = 0;
                value.Text = "";
                value.Fractional = 0;
                value.EnumText = "";
                value.TypedEnumText = "";
            }
            byte[] oracle = OracleBytes(value);
            if (mode == 0)
            {
                CollectionAssert.AreEqual(Array.Empty<byte>(), oracle);
            }
            else if (mode == 1)
            {
                CollectionAssert.AreEqual(new byte[] { 8, 129, 128, 128, 8 }, oracle);
            }
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out byte[] generated));
            CollectionAssert.AreEqual(oracle, generated);
            Assert.IsTrue(
                WProtoFacade.TryDeserialize(oracle, out ExplicitDefaultConversionContract migrated)
            );
            AssertValue(value, migrated);
            AssertValue(value, OracleRead(generated));
        }
    }
}
