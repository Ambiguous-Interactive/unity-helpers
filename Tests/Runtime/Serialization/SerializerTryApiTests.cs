// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    /// <summary>
    /// Tests the <c>TryXxx</c> deserialize family: must never throw for
    /// <see cref="SerializationInputException"/> or <see cref="SerializationCorruptDataException"/>,
    /// must set <c>out</c> to <see langword="default"/> on failure, and must propagate
    /// <see cref="SerializationTypeException"/> / <see cref="SerializationConfigurationException"/>
    /// (programmer errors).
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializerTryApiTests
    {
        [ProtoContract]
        private sealed class Sample
        {
            [ProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            public string Name { get; set; }
        }

        // ---------------------------------------------------------------------------
        // Happy path: Try* returns true for a valid roundtrip.
        // ---------------------------------------------------------------------------

        [Test]
        public void TryProtoDeserializeValidPayloadReturnsTrue()
        {
            byte[] data = Serializer.ProtoSerialize(new Sample { Id = 7, Name = "ok" });
            bool ok = Serializer.TryProtoDeserialize(data, out Sample value);
            Assert.IsTrue(ok);
            Assert.IsTrue(value != null);
            Assert.AreEqual(7, value.Id);
            Assert.AreEqual("ok", value.Name);
        }

        [Test]
        public void TryJsonDeserializeStringValidPayloadReturnsTrue()
        {
            string json = "{\"Id\":42,\"Name\":\"x\"}";
            bool ok = Serializer.TryJsonDeserialize(json, out Sample value);
            Assert.IsTrue(ok);
            Assert.AreEqual(42, value.Id);
        }

        [Test]
        public void TryJsonDeserializeBytesValidPayloadReturnsTrue()
        {
            byte[] data = Serializer.JsonSerialize(new Sample { Id = 9, Name = "y" });
            bool ok = Serializer.TryJsonDeserialize(data, out Sample value);
            Assert.IsTrue(ok);
            Assert.AreEqual(9, value.Id);
        }

        [Test]
        public void TryDeserializeDispatcherValidPayloadReturnsTrue()
        {
            byte[] data = Serializer.ProtoSerialize(new Sample { Id = 3 });
            bool ok = Serializer.TryDeserialize(data, SerializationType.Protobuf, out Sample value);
            Assert.IsTrue(ok);
            Assert.AreEqual(3, value.Id);
        }

        // ---------------------------------------------------------------------------
        // Sad path: null/empty/corrupt input returns false, out=default, never throws.
        // ---------------------------------------------------------------------------

        [Test]
        public void TryProtoDeserializeNullBytesReturnsFalse()
        {
            bool ok = Serializer.TryProtoDeserialize(null, out Sample value);
            Assert.IsFalse(ok);
            Assert.IsTrue(value == null);
        }

        [Test]
        public void TryProtoDeserializeEmptyBytesReturnsFalse()
        {
            bool ok = Serializer.TryProtoDeserialize(Array.Empty<byte>(), out Sample value);
            Assert.IsFalse(ok);
            Assert.IsTrue(value == null);
        }

        [Test]
        public void TryProtoDeserializeGarbageBytesReturnsFalse()
        {
            byte[] garbage = { 0xFF, 0xFF, 0xFF, 0xFF };
            bool ok = Serializer.TryProtoDeserialize(garbage, out Sample value);
            Assert.IsFalse(ok);
            Assert.IsTrue(value == null);
        }

        [Test]
        public void TryJsonDeserializeNullStringReturnsFalse()
        {
            bool ok = Serializer.TryJsonDeserialize((string)null, out Sample value);
            Assert.IsFalse(ok);
            Assert.IsTrue(value == null);
        }

        [Test]
        public void TryJsonDeserializeNullBytesReturnsFalse()
        {
            bool ok = Serializer.TryJsonDeserialize((byte[])null, out Sample value);
            Assert.IsFalse(ok);
        }

        [Test]
        public void TryJsonDeserializeGarbageReturnsFalse()
        {
            bool ok = Serializer.TryJsonDeserialize("not json", out Sample value);
            Assert.IsFalse(ok);
            Assert.IsTrue(value == null);
        }

        [Test]
        public void TryJsonDeserializeFastNullBytesReturnsFalse()
        {
            bool ok = Serializer.TryJsonDeserializeFast(null, out Sample value);
            Assert.IsFalse(ok);
        }

        [Test]
        public void TryJsonDeserializeFastGarbageReturnsFalse()
        {
            bool ok = Serializer.TryJsonDeserializeFast(new byte[] { 0xFF }, out Sample value);
            Assert.IsFalse(ok);
        }

        [Test]
        public void TryBinaryDeserializeNullBytesReturnsFalse()
        {
            bool ok = Serializer.TryBinaryDeserialize(null, out Sample value);
            Assert.IsFalse(ok);
        }

        [Test]
        public void TryBinaryDeserializeGarbageReturnsFalse()
        {
            bool ok = Serializer.TryBinaryDeserialize(new byte[] { 0xFF, 0xFE }, out Sample value);
            Assert.IsFalse(ok);
        }

        [Test]
        public void TryDeserializeNullBytesReturnsFalse()
        {
            bool ok = Serializer.TryDeserialize(null, SerializationType.Protobuf, out Sample value);
            Assert.IsFalse(ok);
        }

        // ---------------------------------------------------------------------------
        // Programmer errors still throw — Try* does NOT swallow Type/Configuration failures.
        // ---------------------------------------------------------------------------

        private interface IUnregistered { }

        [ProtoContract]
        private sealed class Unregistered : IUnregistered
        {
            [ProtoMember(1)]
            public int X { get; set; }
        }

        [Test]
        public void TryProtoDeserializeUnresolvedInterfaceStillThrowsTypeException()
        {
            byte[] data = Serializer.ProtoSerialize<IUnregistered>(new Unregistered { X = 1 });
            Assert.Throws<SerializationTypeException>(() =>
                Serializer.TryProtoDeserialize(data, out IUnregistered _)
            );
        }

        [Test]
        public void TryDeserializeUnknownSerializationTypeStillThrowsConfiguration()
        {
            Assert.Throws<SerializationConfigurationException>(() =>
                Serializer.TryDeserialize(new byte[] { 0 }, (SerializationType)9999, out Sample _)
            );
        }

        [Test]
        public void TryProtoDeserializeWithTypeNullTypeStillThrowsConfiguration()
        {
            byte[] data = Serializer.ProtoSerialize(new Sample { Id = 1 });
            Assert.Throws<SerializationConfigurationException>(() =>
                Serializer.TryProtoDeserialize(data, null, out object _)
            );
        }
    }
}
