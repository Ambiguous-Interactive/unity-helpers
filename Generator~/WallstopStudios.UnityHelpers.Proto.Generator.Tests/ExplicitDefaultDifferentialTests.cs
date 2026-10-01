// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Tests.Runtime.Serialization;

    /// <summary>Checks explicit scalar default omission and construction against both oracles.</summary>
    [TestFixture]
    public sealed class ExplicitDefaultDifferentialTests
    {
        private static byte[] Oracle(WProtoExplicitDefaultContract value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(stream, value);
                return stream.ToArray();
            }
        }

        private static WProtoExplicitDefaultContract ReadOracle(byte[] bytes)
        {
            using (MemoryStream stream = new MemoryStream(bytes))
            {
                return ProtoBuf.Serializer.Deserialize<WProtoExplicitDefaultContract>(stream);
            }
        }

        private static void AssertValue(
            WProtoExplicitDefaultContract expected,
            WProtoExplicitDefaultContract actual
        )
        {
            Assert.AreEqual(expected.SignedByte, actual.SignedByte);
            Assert.AreEqual(expected.Byte, actual.Byte);
            Assert.AreEqual(expected.Int16, actual.Int16);
            Assert.AreEqual(expected.UInt16, actual.UInt16);
            Assert.AreEqual(expected.Int32, actual.Int32);
            Assert.AreEqual(expected.UInt32, actual.UInt32);
            Assert.AreEqual(expected.Int64, actual.Int64);
            Assert.AreEqual(expected.UInt64, actual.UInt64);
            Assert.AreEqual(expected.Flag, actual.Flag);
            Assert.AreEqual(expected.Character, actual.Character);
            Assert.AreEqual(expected.Single, actual.Single);
            Assert.AreEqual(expected.Double, actual.Double);
            Assert.AreEqual(expected.Text, actual.Text);
            Assert.AreEqual(expected.Enum, actual.Enum);
            Assert.AreEqual(expected.Nullable, actual.Nullable);
            Assert.AreEqual(expected.NaN, actual.NaN);
            Assert.AreEqual(expected.Infinity, actual.Infinity);
            Assert.AreEqual(expected.Decimal, actual.Decimal);
            Assert.AreEqual(expected.Timestamp, actual.Timestamp);
            Assert.AreEqual(expected.Duration, actual.Duration);
            Assert.AreEqual(expected.Guid, actual.Guid);
            Assert.AreEqual(expected.Plain, actual.Plain);
            Assert.AreEqual(expected.Required, actual.Required);
            Assert.AreEqual(expected.RequiredNaN, actual.RequiredNaN);
            Assert.AreEqual(expected.DirectZonedTimestamp.Ticks, actual.DirectZonedTimestamp.Ticks);
            Assert.AreEqual(expected.DirectZonedTimestamp.Kind, actual.DirectZonedTimestamp.Kind);
            Assert.AreEqual(expected.TypedZonedTimestamp.Ticks, actual.TypedZonedTimestamp.Ticks);
            Assert.AreEqual(expected.TypedZonedTimestamp.Kind, actual.TypedZonedTimestamp.Kind);
            Assert.AreEqual(expected.ZonedTimestampText, actual.ZonedTimestampText);
            Assert.AreEqual(expected.NullNullable, actual.NullNullable);
            Assert.AreEqual(expected.NullText, actual.NullText);
        }

        [OneTimeSetUp]
        public void RegisterBuiltInFormatters()
        {
            WProtoBcl.RegisterAll();
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ScalarDefaultsMatchBytesAndCrossReads(int mode)
        {
            WProtoExplicitDefaultContract value = new WProtoExplicitDefaultContract();
            if (mode == 1)
            {
                value.SignedByte = 0;
                value.Byte = 0;
                value.Int16 = 0;
                value.UInt16 = 0;
                value.Int32 = 0;
                value.UInt32 = 0;
                value.Int64 = 0;
                value.UInt64 = 0;
                value.Flag = false;
                value.Character = '\0';
                value.Single = 0;
                value.Double = 0;
                value.Text = "";
                value.Enum = DayOfWeek.Sunday;
                value.Nullable = 0;
                value.NaN = 0;
                value.Infinity = 0;
                value.Decimal = 0;
                value.Timestamp = default;
                value.DirectZonedTimestamp = default;
                value.TypedZonedTimestamp = default;
                value.ZonedTimestampText = "";
                value.Duration = default;
                value.Guid = default;
                value.Plain = 5;
                value.Required = 0;
                value.NullNullable = 0;
                value.NullText = "";
            }
            else if (mode == 2)
            {
                value.SignedByte = sbyte.MinValue;
                value.Byte = byte.MaxValue;
                value.Int16 = short.MinValue;
                value.UInt16 = ushort.MaxValue;
                value.Int32 = int.MinValue;
                value.UInt32 = uint.MaxValue;
                value.Int64 = long.MinValue;
                value.UInt64 = ulong.MaxValue;
                value.Character = '\uffff';
                value.Single = float.NegativeInfinity;
                value.RequiredNaN = float.NaN;
                value.Double = double.NaN;
                value.Text = null;
                value.Nullable = null;
                value.NaN = float.PositiveInfinity;
                value.Infinity = double.NegativeInfinity;
                value.Decimal = decimal.MaxValue;
                value.Timestamp = DateTime.MaxValue;
                value.DirectZonedTimestamp = DateTime.MaxValue;
                value.TypedZonedTimestamp = DateTime.MinValue;
                value.ZonedTimestampText = "different";
                value.Duration = TimeSpan.MinValue;
                value.Guid = Guid.Empty;
                value.Plain = -1;
                value.NullNullable = -1;
                value.NullText = "text";
            }
            byte[] oracle = Oracle(value);
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out byte[] generated));
            CollectionAssert.AreEqual(oracle, generated);
            Assert.IsTrue(
                WProtoFacade.TryDeserialize(oracle, out WProtoExplicitDefaultContract migrated)
            );
            WProtoExplicitDefaultContract originalRead = ReadOracle(oracle);
            AssertValue(originalRead, migrated);
            AssertValue(originalRead, ReadOracle(generated));
        }

        [Test]
        public void AbsentFieldsRetainConstructorStateRatherThanDefaultAttributeValues()
        {
            byte[] empty = Array.Empty<byte>();
            WProtoExplicitDefaultContract oracle = ReadOracle(empty);
            Assert.AreEqual(0, oracle.Plain);
            Assert.AreEqual(-123, oracle.Int32);
            IWProtoFormatter<WProtoExplicitDefaultContract> formatter =
                WProtoFormatterProvider.Get<WProtoExplicitDefaultContract>();
            WProtoReader reader = new WProtoReader(empty);
            Assert.IsTrue(
                formatter.TryRead(ref reader, out WProtoExplicitDefaultContract generated)
            );
            AssertValue(oracle, generated);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExplicitNaNDefaultsExposeOracleBackendEncoding(bool autoCompile)
        {
            ProtoBuf.Meta.RuntimeTypeModel model = ProtoBuf.Meta.RuntimeTypeModel.Create();
            model.AutoCompile = autoCompile;
#pragma warning disable WPROTO048
            ProtoBuf.Meta.MetaType type = model.Add(typeof(ScalarContract), false);
            type.Add(6, nameof(ScalarContract.Single));
            type[6].DefaultValue = float.NaN;
            type.Add(7, nameof(ScalarContract.Double));
            type[7].DefaultValue = double.NaN;
#pragma warning restore WPROTO048
            using (MemoryStream stream = new MemoryStream())
            {
                model.Serialize(
                    stream,
                    new ScalarContract { Single = float.NaN, Double = double.NaN }
                );
                TestContext.Progress.WriteLine(
                    "Oracle AutoCompile="
                        + autoCompile
                        + " NaN bytes="
                        + BitConverter.ToString(stream.ToArray())
                );
                Assert.AreEqual(autoCompile ? 14 : 0, stream.Length);
            }
        }
    }
}
