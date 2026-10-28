// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using ProtoBuf.Meta;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Tests.Serialization;

    [TestFixture]
    public sealed class SmallIntegerDifferentialTests
    {
        private static void AssertOverflow<T>(long numeric)
        {
            byte[] bytes = WProtoSmallIntegerTests.Encode(numeric, 1);
            Assert.Throws<OverflowException>(() =>
            {
                using MemoryStream input = new MemoryStream(bytes);
                ProtoBuf.Serializer.Deserialize<T>(input);
            });
            WProtoScalarFormatters.RegisterAll();
            Assert.IsTrue(
                WProtoScalarFormatterProvider.TryGet(out IWProtoScalarFormatter<T> formatter)
            );
            WProtoReader reader = new WProtoReader(WProtoSmallIntegerTests.Encode(numeric));
            Assert.IsFalse(formatter.TryReadValue(ref reader, out T result));
            Assert.AreEqual(default(T), result);
            Assert.IsTrue(reader.Malformed);
        }

        private static void AssertBoundary<T>(long numeric, T expected)
        {
            using MemoryStream input = new MemoryStream(WProtoSmallIntegerTests.Encode(numeric, 1));
            Assert.AreEqual(expected, ProtoBuf.Serializer.Deserialize<T>(input));
        }

        private static RuntimeTypeModel CreateModel(bool autoCompile)
        {
            RuntimeTypeModel model = RuntimeTypeModel.Create();
            model.AutoCompile = autoCompile;
#pragma warning disable WPROTO048
            MetaType type = model.Add(typeof(WProtoSmallIntegerContract), false);
            type.Add(1, nameof(WProtoSmallIntegerContract.Byte));
            type.Add(2, nameof(WProtoSmallIntegerContract.SignedByte));
            type.Add(3, nameof(WProtoSmallIntegerContract.Short));
            type.Add(4, nameof(WProtoSmallIntegerContract.UnsignedShort));
            type.Add(5, nameof(WProtoSmallIntegerContract.Character));
            type.Add(6, nameof(WProtoSmallIntegerContract.NullableShort));
            type.Add(7, nameof(WProtoSmallIntegerContract.Shorts));
            type.Add(8, nameof(WProtoSmallIntegerContract.Bytes));
            type.Add(9, nameof(WProtoSmallIntegerContract.Values));
            type.Add(10, nameof(WProtoSmallIntegerContract.ZigZagShort));
            type[10].DataFormat = ProtoBuf.DataFormat.ZigZag;
            type.Add(11, nameof(WProtoSmallIntegerContract.ZigZagSignedByte));
            type[11].DataFormat = ProtoBuf.DataFormat.ZigZag;
            type.Add(12, nameof(WProtoSmallIntegerContract.NullableZigZagShort));
            type[12].DataFormat = ProtoBuf.DataFormat.ZigZag;
            type.Add(13, nameof(WProtoSmallIntegerContract.SignedBytes));
            type.Add(14, nameof(WProtoSmallIntegerContract.UnsignedShorts));
            type.Add(15, nameof(WProtoSmallIntegerContract.Characters));
            type.Add(16, nameof(WProtoSmallIntegerContract.CharactersByNumber));
            type.Add(17, nameof(WProtoSmallIntegerContract.ShortsBySignedByte));
            type[7].IsPacked = true;
            type[8].IsPacked = true;
            type[13].IsPacked = true;
            type[14].IsPacked = true;
#pragma warning restore WPROTO048
            return model;
        }

        [Test]
        public void ScalarOverflowMatchesBothOracleVersions()
        {
            AssertOverflow<byte>(256);
            AssertOverflow<byte>(-1);
            AssertOverflow<sbyte>(128);
            AssertOverflow<sbyte>(-129);
            AssertOverflow<short>(32768);
            AssertOverflow<short>(-32769);
            AssertOverflow<ushort>(65536);
            AssertOverflow<ushort>(-1);
            AssertOverflow<char>(65536);
            AssertOverflow<char>(-1);
        }

        [Test]
        public void ScalarBoundariesMatchBothOracleVersions()
        {
            AssertBoundary(0, (byte)0);
            AssertBoundary(255, byte.MaxValue);
            AssertBoundary(-128, sbyte.MinValue);
            AssertBoundary(127, sbyte.MaxValue);
            AssertBoundary(-32768, short.MinValue);
            AssertBoundary(32767, short.MaxValue);
            AssertBoundary(65535, ushort.MaxValue);
            AssertBoundary(65535, char.MaxValue);
            AssertBoundary(uint.MaxValue, (short)-1);
            AssertBoundary(uint.MaxValue, (sbyte)-1);
        }

        [TestCase(1, 256, false)]
        [TestCase(2, 128, false)]
        [TestCase(3, 32768, false)]
        [TestCase(4, 65536, false)]
        [TestCase(5, 65536, false)]
        [TestCase(6, 32768, false)]
        [TestCase(7, 32768, false)]
        [TestCase(8, 256, false)]
        [TestCase(10, 32768, true)]
        [TestCase(10, -32769, true)]
        [TestCase(11, 128, true)]
        [TestCase(11, -129, true)]
        [TestCase(12, 32768, true)]
        [TestCase(13, 128, false)]
        [TestCase(14, 65536, false)]
        [TestCase(15, 65536, false)]
        public void GeneratedOverflowMatchesOracle(int tag, long numeric, bool zigZag)
        {
            byte[] bytes = WProtoSmallIntegerTests.Encode(numeric, tag, zigZag);
            foreach (bool autoCompile in new[] { false, true })
            {
                RuntimeTypeModel model = CreateModel(autoCompile);
                Assert.Throws<OverflowException>(() =>
                {
                    using MemoryStream input = new MemoryStream(bytes);
                    model.Deserialize(input, null, typeof(WProtoSmallIntegerContract));
                });
            }
            Assert.Throws<InvalidOperationException>(() =>
                WProtoFacade.TryDeserialize(bytes, out WProtoSmallIntegerContract _)
            );
        }

        [TestCase(9, 1, 32768)]
        [TestCase(9, 2, 256)]
        [TestCase(16, 1, 65536)]
        [TestCase(16, 2, 65536)]
        [TestCase(17, 1, 128)]
        [TestCase(17, 2, 32768)]
        public void MapOverflowMatchesOracle(int tag, int entryTag, long numeric)
        {
            byte[] bytes = WProtoSmallIntegerTests.Wrap(
                tag,
                WProtoSmallIntegerTests.Encode(numeric, entryTag)
            );
            foreach (bool autoCompile in new[] { false, true })
            {
                RuntimeTypeModel model = CreateModel(autoCompile);
                Assert.Throws<OverflowException>(() =>
                {
                    using MemoryStream input = new MemoryStream(bytes);
                    model.Deserialize(input, null, typeof(WProtoSmallIntegerContract));
                });
            }
            Assert.Throws<InvalidOperationException>(() =>
                WProtoFacade.TryDeserialize(bytes, out WProtoSmallIntegerContract _)
            );
        }

        [TestCase(7, 32768)]
        [TestCase(8, 256)]
        [TestCase(13, 128)]
        [TestCase(14, 65536)]
        public void PackedOverflowMatchesOracle(int tag, long numeric)
        {
            byte[] bytes = WProtoSmallIntegerTests.Wrap(
                tag,
                WProtoSmallIntegerTests.Encode(numeric)
            );
            foreach (bool autoCompile in new[] { false, true })
            {
                RuntimeTypeModel model = CreateModel(autoCompile);
                Assert.Throws<OverflowException>(() =>
                {
                    using MemoryStream input = new MemoryStream(bytes);
                    model.Deserialize(input, null, typeof(WProtoSmallIntegerContract));
                });
            }
            Assert.Throws<InvalidOperationException>(() =>
                WProtoFacade.TryDeserialize(bytes, out WProtoSmallIntegerContract _)
            );
        }

        [Test]
        public void CharacterRootRefusesAnOverflowBeforeALaterValidOccurrence()
        {
            WProtoBcl.RegisterAll();
            byte[] bytes = { 0x08, 0x80, 0x80, 0x04, 0x08, 0x41 };
            Assert.Throws<OverflowException>(() =>
            {
                using MemoryStream input = new MemoryStream(bytes);
                ProtoBuf.Serializer.Deserialize<char>(input);
            });
            Assert.Throws<InvalidOperationException>(() =>
                WProtoFacade.TryDeserialize(bytes, out char _)
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GeneratedBoundaryBytesAndReadDirectionsMatchOracle(bool autoCompile)
        {
            RuntimeTypeModel model = CreateModel(autoCompile);
            WProtoSmallIntegerContract value = WProtoSmallIntegerTests.CreateBoundaryValue();
            using MemoryStream output = new MemoryStream();
            model.Serialize(output, value);
            byte[] oracle = output.ToArray();
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out byte[] generated));
            CollectionAssert.AreEqual(oracle, generated);
            using MemoryStream input = new MemoryStream(generated);
            WProtoSmallIntegerContract oracleRead = (WProtoSmallIntegerContract)
                model.Deserialize(input, null, typeof(WProtoSmallIntegerContract));
            Assert.IsTrue(WProtoFacade.TrySerialize(oracleRead, out byte[] rewritten));
            CollectionAssert.AreEqual(oracle, rewritten);
            Assert.IsTrue(
                WProtoFacade.TryDeserialize(oracle, out WProtoSmallIntegerContract generatedRead)
            );
            using MemoryStream final = new MemoryStream();
            model.Serialize(final, generatedRead);
            CollectionAssert.AreEqual(oracle, final.ToArray());
        }
    }
}
