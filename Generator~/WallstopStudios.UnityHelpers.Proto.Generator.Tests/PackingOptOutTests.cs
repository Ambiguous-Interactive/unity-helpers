// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Pins repeated write packing policy and read compatibility.</summary>
    [TestFixture]
    public sealed class PackingOptOutTests
    {
        private static void AssertEncoded<T>(T value, string hex)
        {
            AssertOracle(value);
            byte[] bytes = Encode(value);
            CollectionAssert.AreEqual(Parse(hex), bytes);
        }

        private static void AssertOracle<T>(T value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(stream, value);
                CollectionAssert.AreEqual(
                    stream.ToArray(),
                    Encode(value),
                    "Live protobuf-net oracle bytes"
                );
                stream.Position = 0;
                Assert.IsTrue(ProtoBuf.Serializer.Deserialize<T>(stream) != null);
            }
        }

        private static void AssertShortBuffer<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            WProtoWriter writer = new WProtoWriter(new byte[formatter.Measure(value) - 1]);
            Assert.IsFalse(formatter.Write(ref writer, value));
        }

        private static byte[] Encode<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            int measured = formatter.Measure(value);
            byte[] bytes = new byte[measured];
            WProtoWriter writer = new WProtoWriter(bytes);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(measured, writer.Position);
            return bytes;
        }

        private static T Decode<T>(byte[] bytes)
        {
            WProtoReader reader = new WProtoReader(bytes);
            Assert.IsTrue(WProtoFormatterProvider.Get<T>().TryRead(ref reader, out T value));
            Assert.IsTrue(reader.End);
            Assert.IsFalse(reader.Malformed);
            return value;
        }

        private static byte[] Parse(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int index = 0; index < bytes.Length; ++index)
            {
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }
            return bytes;
        }

        [TestCase(0, "08030801080408000802", TestName = "Unpacked.IntArray")]
        [TestCase(1, "10001002", TestName = "Unpacked.IntList")]
        [TestCase(2, "180018AC02", TestName = "Unpacked.EnumArray")]
        [TestCase(3, "200020AC02", TestName = "Unpacked.EnumList")]
        [TestCase(4, "29000000000000000029000000000000F03F", TestName = "Unpacked.DoubleArray")]
        [TestCase(5, "30003001", TestName = "Unpacked.BoolArray")]
        [TestCase(6, "3A003A0161", TestName = "Unpacked.StringArray")]
        [TestCase(7, "42020001", TestName = "Packed.ExplicitTrue")]
        [TestCase(8, "48004861", TestName = "Unpacked.CharArray")]
        public void ExplicitWritePolicyProducesExpectedBytes(int shape, string hex)
        {
            UnpackedRepeatedContract value = new UnpackedRepeatedContract();
            switch (shape)
            {
                case 0:
                    value.Ints = new[] { 3, 1, 4, 0, 2 };
                    break;
                case 1:
                    value.IntList = new List<int> { 0, 2 };
                    break;
                case 2:
                    value.Modes = new[] { Mode.None, Mode.Careful };
                    break;
                case 3:
                    value.ModeList = new List<Mode> { Mode.None, Mode.Careful };
                    break;
                case 4:
                    value.Doubles = new[] { 0d, 1d };
                    break;
                case 5:
                    value.Flags = new[] { false, true };
                    break;
                case 6:
                    value.Texts = new[] { string.Empty, "a" };
                    break;
                case 7:
                    value.PackedInts = new[] { 0, 1 };
                    break;
                case 8:
                    value.Chars = new[] { '\0', 'a' };
                    break;
            }
            AssertEncoded(value, hex);
        }

        [TestCase(false, TestName = "Unpacked.NullCollections")]
        [TestCase(true, TestName = "Unpacked.EmptyCollections")]
        public void NullAndEmptyCollectionsWriteNothing(bool empty)
        {
            UnpackedRepeatedContract value = new UnpackedRepeatedContract();
            UnpackedRepeatedBox<int> generic = new UnpackedRepeatedBox<int>();
            if (empty)
            {
                value.Ints = Array.Empty<int>();
                value.IntList = new List<int>();
                value.Modes = Array.Empty<Mode>();
                value.ModeList = new List<Mode>();
                generic.Array = Array.Empty<int>();
                generic.List = new List<int>();
            }
            AssertEncoded(value, string.Empty);
            AssertEncoded(generic, string.Empty);
            Assert.IsTrue(Decode<UnpackedRepeatedContract>(Array.Empty<byte>()).Ints == null);
            Assert.IsTrue(Decode<UnpackedRepeatedBox<int>>(Array.Empty<byte>()).Array == null);
        }

        [Test]
        public void AnEmptyDefaultPackedRunRetainsExistingOmission()
        {
            UnpackedRepeatedBox<int> value = new UnpackedRepeatedBox<int>
            {
                DefaultPacked = Array.Empty<int>(),
            };
            CollectionAssert.AreEqual(Array.Empty<byte>(), Encode(value));
            using (MemoryStream stream = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(stream, value);
                CollectionAssert.AreEqual(
                    Parse("1A00"),
                    stream.ToArray(),
                    "The explicit-packed oracle preserves a present empty run."
                );
            }
        }

        [Test]
        public void GenericIntegerArrayAndListUseUnpackedWhileDefaultStillPacks()
        {
            UnpackedRepeatedBox<int> value = new UnpackedRepeatedBox<int>
            {
                Array = new[] { 0, -1, int.MaxValue },
                List = new List<int> { 0, 2 },
                DefaultPacked = new[] { 0, 2 },
            };
            byte[] bytes = Encode(value);
            AssertEncoded(value, "080008FFFFFFFFFFFFFFFFFF0108FFFFFFFF07100010021A020002");
            UnpackedRepeatedBox<int> restored = Decode<UnpackedRepeatedBox<int>>(bytes);
            CollectionAssert.AreEqual(value.Array, restored.Array);
            CollectionAssert.AreEqual(value.List, restored.List);
            CollectionAssert.AreEqual(value.DefaultPacked, restored.DefaultPacked);
        }

        [Test]
        public void GenericEnumArrayAndListUseUnpacked()
        {
            UnpackedRepeatedBox<Mode> value = new UnpackedRepeatedBox<Mode>
            {
                Array = new[] { Mode.None, Mode.Careful },
                List = new List<Mode> { Mode.None, Mode.Fast },
            };
            AssertEncoded(value, "080008AC0210001001");
            UnpackedRepeatedBox<Mode> restored = Decode<UnpackedRepeatedBox<Mode>>(Encode(value));
            CollectionAssert.AreEqual(value.Array, restored.Array);
            CollectionAssert.AreEqual(value.List, restored.List);
        }

        [Test]
        public void GenericStringArrayAndListRemainUnpacked()
        {
            UnpackedRepeatedValues<string> value = new UnpackedRepeatedValues<string>
            {
                Array = new[] { string.Empty, "a" },
                List = new List<string> { "b" },
            };
            AssertEncoded(value, "0A000A0161120162");
            UnpackedRepeatedValues<string> restored = Decode<UnpackedRepeatedValues<string>>(
                Encode(value)
            );
            CollectionAssert.AreEqual(value.Array, restored.Array);
            CollectionAssert.AreEqual(value.List, restored.List);
        }

        [TestCase("08000801", TestName = "Read.Unpacked")]
        [TestCase("0A020001", TestName = "Read.Packed")]
        [TestCase("08000A0101", TestName = "Read.Mixed")]
        public void UnpackedWritePolicyReadsEitherWireForm(string hex)
        {
            byte[] payload = Parse(hex);
            CollectionAssert.AreEqual(
                new[] { 0, 1 },
                Decode<UnpackedRepeatedContract>(payload).Ints
            );
            CollectionAssert.AreEqual(
                new[] { 0, 1 },
                Decode<UnpackedRepeatedBox<int>>(payload).Array
            );
        }

        [Test]
        public void UnpackedEnumWritePolicyReadsMixedRuns()
        {
            UnpackedRepeatedContract value = Decode<UnpackedRepeatedContract>(
                Parse("18001A02AC02")
            );
            CollectionAssert.AreEqual(new[] { Mode.None, Mode.Careful }, value.Modes);
            UnpackedRepeatedBox<Mode> generic = Decode<UnpackedRepeatedBox<Mode>>(
                Parse("08000A02AC02")
            );
            CollectionAssert.AreEqual(new[] { Mode.None, Mode.Careful }, generic.Array);
        }

        [TestCase(false, TestName = "ShortBuffer.Concrete")]
        [TestCase(true, TestName = "ShortBuffer.Generic")]
        public void UnpackedWriteReportsAShortBuffer(bool generic)
        {
            if (generic)
            {
                AssertShortBuffer(
                    new UnpackedRepeatedBox<int> { Array = new[] { 0, 1, int.MaxValue } }
                );
            }
            else
            {
                AssertShortBuffer(
                    new UnpackedRepeatedContract { Ints = new[] { 0, 1, int.MaxValue } }
                );
            }
        }

        [TestCase("08", TestName = "Malformed.Unpacked")]
        [TestCase("0A0201", TestName = "Malformed.Packed")]
        public void UnpackedWritePolicyDoesNotRelaxReadValidation(string hex)
        {
            byte[] bytes = Parse(hex);
            WProtoReader concrete = new WProtoReader(bytes);
            Assert.IsFalse(
                WProtoFormatterProvider.Get<UnpackedRepeatedContract>().TryRead(ref concrete, out _)
            );
            WProtoReader generic = new WProtoReader(bytes);
            Assert.IsFalse(
                WProtoFormatterProvider.Get<UnpackedRepeatedBox<int>>().TryRead(ref generic, out _)
            );
        }

        [Test]
        public void ALargeUnpackedRunMeasuresExactlyAndRoundTrips()
        {
            int[] values = new int[10000];
            for (int index = 0; index < values.Length; ++index)
            {
                values[index] = index % 3 == 0 ? 0 : index;
            }
            UnpackedRepeatedContract concrete = new UnpackedRepeatedContract { Ints = values };
            UnpackedRepeatedBox<int> generic = new UnpackedRepeatedBox<int> { Array = values };
            CollectionAssert.AreEqual(
                values,
                Decode<UnpackedRepeatedContract>(Encode(concrete)).Ints
            );
            CollectionAssert.AreEqual(
                values,
                Decode<UnpackedRepeatedBox<int>>(Encode(generic)).Array
            );
            AssertOracle(concrete);
            AssertOracle(generic);
        }
    }
}
