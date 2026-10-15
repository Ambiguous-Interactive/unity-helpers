// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [TestFixture]
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class WProtoRuntimeRootTests
    {
        private static void AssertErasedCollection<T>(
            T value,
            byte[] expected,
            bool forceRuntimeType
        )
            where T : IEnumerable
        {
            Assert.IsFalse(WProtoFacade.TrySerialize<object>(value, out byte[] declined));
            Assert.IsTrue(declined == null);
            Assert.IsTrue(
                WProtoFormatterProvider.TryGetRuntimeCodec(value, out IWProtoRuntimeCodec writer)
            );
            Assert.IsTrue(writer.TrySerialize(value, out byte[] generated));
            CollectionAssert.AreEqual(expected, generated);
            CollectionAssert.AreEqual(
                expected,
                Serializer.ProtoSerialize<object>(value, forceRuntimeType)
            );
            CollectionAssert.AreEqual(
                expected,
                Serializer.ProtoSerialize<IEnumerable>(value, forceRuntimeType)
            );
            byte[] buffer = new byte[expected.Length + 16];
            byte[] original = buffer;
            int written = Serializer.ProtoSerialize<object>(value, ref buffer, forceRuntimeType);
            Assert.AreSame(original, buffer);
            Assert.AreEqual(expected.Length, written);
            Assert.IsTrue(buffer.AsSpan(0, written).SequenceEqual(expected));
            written = Serializer.ProtoSerialize<IEnumerable>(value, ref buffer, forceRuntimeType);
            Assert.AreSame(original, buffer);
            Assert.AreEqual(expected.Length, written);
            Assert.IsTrue(buffer.AsSpan(0, written).SequenceEqual(expected));
            CollectionAssert.AreEqual(value, WProtoFacade.Deserialize<T>(expected));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ErasedArrayRootsKeepTheirConcreteEncoding(bool empty, bool forceRuntimeType)
        {
            int[] value = empty ? Array.Empty<int>() : new[] { 0, -1, int.MinValue, int.MaxValue };
            AssertErasedCollection(value, WProtoFacade.Serialize(value), forceRuntimeType);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ErasedListRootsKeepTheirConcreteEncoding(bool empty, bool forceRuntimeType)
        {
            List<int> value = empty
                ? new List<int>()
                : new List<int> { 0, -1, int.MinValue, int.MaxValue };
            AssertErasedCollection(value, WProtoFacade.Serialize(value), forceRuntimeType);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ErasedDictionaryRootsKeepTheirConcreteEncoding(
            bool empty,
            bool forceRuntimeType
        )
        {
            Dictionary<int, string> value = empty
                ? new Dictionary<int, string>()
                : new Dictionary<int, string> { { -1, "station/café" } };
            AssertErasedCollection(value, WProtoFacade.Serialize(value), forceRuntimeType);
        }

        [Test]
        public void NestedArrayPackingDoesNotChangeTheArrayRootEncoding()
        {
            List<int[]> value = new List<int[]> { new[] { 1, 2 } };
            byte[] generated = WProtoFacade.Serialize(value);
            CollectionAssert.AreEqual(new byte[] { 10, 4, 10, 2, 1, 2 }, generated);
            List<int[]> restored = WProtoFacade.Deserialize<List<int[]>>(generated);
            Assert.AreEqual(1, restored.Count);
            CollectionAssert.AreEqual(value[0], restored[0]);
            CollectionAssert.AreEqual(new byte[] { 8, 1, 8, 2 }, WProtoFacade.Serialize(value[0]));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ErasedEmptyStringMapRootsKeepTheGeneratedWritePolicy(bool forceRuntimeType)
        {
            Dictionary<string, string> value = new Dictionary<string, string>
            {
                { "b", string.Empty },
            };
            byte[] expected = WProtoFacade.Serialize(value);
            CollectionAssert.AreEqual(new byte[] { 10, 5, 10, 1, 98, 18, 0 }, expected);
            AssertErasedCollection(value, expected, forceRuntimeType);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void UnrelatedInterfaceRootsKeepTheirConcreteBytes(
            bool otherRoot,
            bool forceRuntimeType
        )
        {
            IWProtoRuntimeSave value = otherRoot
                ? new WProtoRuntimeOtherSave { Currency = -1234567890123 }
                : new WProtoRuntimeSave { Health = -1, Station = "station/café" };
            byte[] expected = otherRoot
                ? WProtoFacade.Serialize((WProtoRuntimeOtherSave)value)
                : WProtoFacade.Serialize((WProtoRuntimeSave)value);
            Assert.IsFalse(WProtoFacade.TrySerialize(value, out byte[] declined));
            Assert.IsTrue(declined == null);
            CollectionAssert.AreEqual(expected, Serializer.ProtoSerialize(value, forceRuntimeType));
            CollectionAssert.AreEqual(
                expected,
                Serializer.ProtoSerialize<object>(value, forceRuntimeType)
            );
            byte[] buffer = new byte[expected.Length + 16];
            byte[] originalBuffer = buffer;
            int written = Serializer.ProtoSerialize(value, ref buffer, forceRuntimeType);
            Assert.AreSame(originalBuffer, buffer);
            Assert.AreEqual(expected.Length, written);
            Assert.IsTrue(buffer.AsSpan(0, written).SequenceEqual(expected));
            written = Serializer.ProtoSerialize<object>(value, ref buffer, forceRuntimeType);
            Assert.AreEqual(expected.Length, written);
            Assert.IsTrue(buffer.AsSpan(0, written).SequenceEqual(expected));
            if (otherRoot)
            {
                Assert.AreEqual(
                    ((WProtoRuntimeOtherSave)value).Currency,
                    Serializer.ProtoDeserialize<WProtoRuntimeOtherSave>(expected).Currency
                );
            }
            else
            {
                WProtoRuntimeSave restored = Serializer.ProtoDeserialize<WProtoRuntimeSave>(
                    expected
                );
                Assert.AreEqual(((WProtoRuntimeSave)value).Health, restored.Health);
                Assert.AreEqual(((WProtoRuntimeSave)value).Station, restored.Station);
            }
        }

        [Test]
        public void ClearingAFormatterAlsoRemovesItsErasedWriter()
        {
            Assert.IsTrue(
                WProtoFormatterProvider.TryGet(out IWProtoFormatter<WProtoRuntimeSave> formatter)
            );
            WProtoRuntimeSave value = new WProtoRuntimeSave();
            Assert.IsTrue(
                WProtoFormatterProvider.TryGetRuntimeCodec(value, out IWProtoRuntimeCodec writer)
            );
            try
            {
                WProtoFormatterProvider.Register<WProtoRuntimeSave>(null);
                Assert.IsFalse(WProtoFormatterProvider.TryGetRuntimeCodec(value, out writer));
                Assert.IsTrue(writer == null);
            }
            finally
            {
                WProtoFormatterProvider.Register(formatter);
            }
            Assert.IsTrue(WProtoFormatterProvider.TryGetRuntimeCodec(value, out writer));
        }

        [Test]
        public void RootMarshalRegistrationRetainsErasedWritesAfterMemberFormatterRemoval()
        {
            Assert.IsTrue(
                WProtoFormatterProvider.TryGet(out IWProtoFormatter<WProtoRuntimeSave> formatter)
            );
            IWProtoFormatter<WProtoRuntimeSave> previousMarshal = WProtoRootMarshalProvider.TryGet(
                out IWProtoFormatter<WProtoRuntimeSave> currentMarshal
            )
                ? currentMarshal
                : null;
            WProtoRuntimeSave value = new WProtoRuntimeSave { Health = 7 };
            byte[] expected = WProtoFacade.Serialize(value);
            try
            {
                WProtoRootMarshalProvider.Register(formatter);
                WProtoFormatterProvider.Register<WProtoRuntimeSave>(null);
                Assert.IsTrue(
                    WProtoFormatterProvider.TryGetRuntimeCodec(
                        value,
                        out IWProtoRuntimeCodec writer
                    )
                );
                CollectionAssert.AreEqual(expected, Serializer.ProtoSerialize<object>(value));
                byte[] buffer = null;
                Assert.AreEqual(
                    expected.Length,
                    Serializer.ProtoSerialize<object>(value, ref buffer)
                );
                CollectionAssert.AreEqual(expected, buffer);
                WProtoRootMarshalProvider.Register<WProtoRuntimeSave>(null);
                Assert.IsFalse(WProtoFormatterProvider.TryGetRuntimeCodec(value, out writer));
            }
            finally
            {
                WProtoFormatterProvider.Register(formatter);
                WProtoRootMarshalProvider.Register(previousMarshal);
            }
            Assert.IsTrue(
                WProtoFormatterProvider.TryGetRuntimeCodec(value, out IWProtoRuntimeCodec restored)
            );
            Assert.IsTrue(restored.TrySerialize(value, out byte[] restoredBytes));
            CollectionAssert.AreEqual(expected, restoredBytes);
        }

        [Test]
        public void UnknownAndNullRuntimeValuesHaveNoWriter()
        {
            Assert.IsFalse(
                WProtoFormatterProvider.TryGetRuntimeCodec(
                    new object(),
                    out IWProtoRuntimeCodec unknown
                )
            );
            Assert.IsTrue(unknown == null);
            Assert.IsFalse(
                WProtoFormatterProvider.TryGetRuntimeCodec(null, out IWProtoRuntimeCodec missing)
            );
            Assert.IsTrue(missing == null);
        }

        [Test]
        public void EmptyConcretePayloadStillServesAnErasedRoot()
        {
            IWProtoRuntimeSave value = new WProtoRuntimeSave();
            Assert.AreEqual(0, Serializer.ProtoSerialize(value).Length);
            byte[] buffer = new byte[16];
            byte[] original = buffer;
            Assert.AreEqual(0, Serializer.ProtoSerialize(value, ref buffer));
            Assert.AreSame(original, buffer);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void CodecFailureKeepsTheSerializationExceptionContract(
            bool erased,
            bool callerBuffer
        )
        {
            WProtoBufferWriterContract value = new WProtoBufferWriterContract
            {
                FailSerialization = true,
            };
            if (callerBuffer)
            {
                byte[] buffer = null;
                Assert.Throws<SerializationCorruptDataException>(() =>
                {
                    if (erased)
                    {
                        Serializer.ProtoSerialize<object>(value, ref buffer);
                    }
                    else
                    {
                        Serializer.ProtoSerialize(value, ref buffer);
                    }
                });
            }
            else
            {
                Assert.Throws<SerializationCorruptDataException>(() =>
                {
                    if (erased)
                    {
                        Serializer.ProtoSerialize<object>(value);
                    }
                    else
                    {
                        Serializer.ProtoSerialize(value);
                    }
                });
            }
#if WALLSTOP_PROTO_ONLY
            Assert.Throws<SerializationTypeException>(() =>
                Serializer.ProtoSerialize(new object())
            );
#endif
        }
    }
}
