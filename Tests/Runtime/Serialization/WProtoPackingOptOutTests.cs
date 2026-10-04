// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Pins repeated write packing policy and read compatibility.</summary>
    [TestFixture]
    public sealed class WProtoPackingOptOutTests
    {
        private static void AssertEncoded<T>(T value, string hex)
        {
            byte[] bytes = Encode(value);
            CollectionAssert.AreEqual(Parse(hex), bytes);
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
            WProtoUnpackedRepeatedContract value = new WProtoUnpackedRepeatedContract();
            switch (shape)
            {
                case 0:
                    value.Ints = new[] { 3, 1, 4, 0, 2 };
                    break;
                case 1:
                    value.IntList = new List<int> { 0, 2 };
                    break;
                case 2:
                    value.Modes = new[] { WProtoRepeatedMode.None, WProtoRepeatedMode.Careful };
                    break;
                case 3:
                    value.ModeList = new List<WProtoRepeatedMode>
                    {
                        WProtoRepeatedMode.None,
                        WProtoRepeatedMode.Careful,
                    };
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
            WProtoUnpackedRepeatedContract value = new WProtoUnpackedRepeatedContract();
            WProtoUnpackedRepeatedBox<int> generic = new WProtoUnpackedRepeatedBox<int>();
            if (empty)
            {
                value.Ints = Array.Empty<int>();
                value.IntList = new List<int>();
                value.Modes = Array.Empty<WProtoRepeatedMode>();
                value.ModeList = new List<WProtoRepeatedMode>();
                generic.Array = Array.Empty<int>();
                generic.List = new List<int>();
            }
            AssertEncoded(value, string.Empty);
            AssertEncoded(generic, string.Empty);
            Assert.IsTrue(Decode<WProtoUnpackedRepeatedContract>(Array.Empty<byte>()).Ints == null);
            Assert.IsTrue(
                Decode<WProtoUnpackedRepeatedBox<int>>(Array.Empty<byte>()).Array == null
            );
        }

        [Test]
        public void AnEmptyDefaultPackedRunRetainsExistingOmission()
        {
            WProtoUnpackedRepeatedBox<int> value = new WProtoUnpackedRepeatedBox<int>
            {
                DefaultPacked = Array.Empty<int>(),
            };
            CollectionAssert.AreEqual(Array.Empty<byte>(), Encode(value));
        }

        [Test]
        public void GenericIntegerArrayAndListUseUnpackedWhileDefaultStillPacks()
        {
            WProtoUnpackedRepeatedBox<int> value = new WProtoUnpackedRepeatedBox<int>
            {
                Array = new[] { 0, -1, int.MaxValue },
                List = new List<int> { 0, 2 },
                DefaultPacked = new[] { 0, 2 },
            };
            byte[] bytes = Encode(value);
            AssertEncoded(value, "080008FFFFFFFFFFFFFFFFFF0108FFFFFFFF07100010021A020002");
            WProtoUnpackedRepeatedBox<int> restored = Decode<WProtoUnpackedRepeatedBox<int>>(bytes);
            CollectionAssert.AreEqual(value.Array, restored.Array);
            CollectionAssert.AreEqual(value.List, restored.List);
            CollectionAssert.AreEqual(value.DefaultPacked, restored.DefaultPacked);
        }

        [Test]
        public void GenericEnumArrayAndListUseUnpacked()
        {
            WProtoUnpackedRepeatedBox<WProtoRepeatedMode> value =
                new WProtoUnpackedRepeatedBox<WProtoRepeatedMode>
                {
                    Array = new[] { WProtoRepeatedMode.None, WProtoRepeatedMode.Careful },
                    List = new List<WProtoRepeatedMode>
                    {
                        WProtoRepeatedMode.None,
                        WProtoRepeatedMode.Fast,
                    },
                };
            AssertEncoded(value, "080008AC0210001001");
            WProtoUnpackedRepeatedBox<WProtoRepeatedMode> restored = Decode<
                WProtoUnpackedRepeatedBox<WProtoRepeatedMode>
            >(Encode(value));
            CollectionAssert.AreEqual(value.Array, restored.Array);
            CollectionAssert.AreEqual(value.List, restored.List);
        }

        [Test]
        public void GenericStringArrayAndListRemainUnpacked()
        {
            WProtoUnpackedRepeatedBox<string> value = new WProtoUnpackedRepeatedBox<string>
            {
                Array = new[] { string.Empty, "a" },
                List = new List<string> { "b" },
            };
            AssertEncoded(value, "0A000A0161120162");
            WProtoUnpackedRepeatedBox<string> restored = Decode<WProtoUnpackedRepeatedBox<string>>(
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
                Decode<WProtoUnpackedRepeatedContract>(payload).Ints
            );
            CollectionAssert.AreEqual(
                new[] { 0, 1 },
                Decode<WProtoUnpackedRepeatedBox<int>>(payload).Array
            );
        }

        [Test]
        public void UnpackedEnumWritePolicyReadsMixedRuns()
        {
            WProtoUnpackedRepeatedContract value = Decode<WProtoUnpackedRepeatedContract>(
                Parse("18001A02AC02")
            );
            CollectionAssert.AreEqual(
                new[] { WProtoRepeatedMode.None, WProtoRepeatedMode.Careful },
                value.Modes
            );
            WProtoUnpackedRepeatedBox<WProtoRepeatedMode> generic = Decode<
                WProtoUnpackedRepeatedBox<WProtoRepeatedMode>
            >(Parse("08000A02AC02"));
            CollectionAssert.AreEqual(
                new[] { WProtoRepeatedMode.None, WProtoRepeatedMode.Careful },
                generic.Array
            );
        }

        [TestCase(false, TestName = "ShortBuffer.Concrete")]
        [TestCase(true, TestName = "ShortBuffer.Generic")]
        public void UnpackedWriteReportsAShortBuffer(bool generic)
        {
            if (generic)
            {
                AssertShortBuffer(
                    new WProtoUnpackedRepeatedBox<int> { Array = new[] { 0, 1, int.MaxValue } }
                );
            }
            else
            {
                AssertShortBuffer(
                    new WProtoUnpackedRepeatedContract { Ints = new[] { 0, 1, int.MaxValue } }
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
                WProtoFormatterProvider
                    .Get<WProtoUnpackedRepeatedContract>()
                    .TryRead(ref concrete, out _)
            );
            WProtoReader generic = new WProtoReader(bytes);
            Assert.IsFalse(
                WProtoFormatterProvider
                    .Get<WProtoUnpackedRepeatedBox<int>>()
                    .TryRead(ref generic, out _)
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
            WProtoUnpackedRepeatedContract concrete = new WProtoUnpackedRepeatedContract
            {
                Ints = values,
            };
            WProtoUnpackedRepeatedBox<int> generic = new WProtoUnpackedRepeatedBox<int>
            {
                Array = values,
            };
            CollectionAssert.AreEqual(
                values,
                Decode<WProtoUnpackedRepeatedContract>(Encode(concrete)).Ints
            );
            CollectionAssert.AreEqual(
                values,
                Decode<WProtoUnpackedRepeatedBox<int>>(Encode(generic)).Array
            );
        }
    }
}
