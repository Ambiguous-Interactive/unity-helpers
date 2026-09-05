// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    /// <summary>
    /// Regression tests covering the exception contract on every public deserialize entry point.
    /// Locks in the screenshot bug fix: <c>new MemoryStream(null)</c> can never surface again, and
    /// every failure surfaces as a <see cref="SerializationFailureException"/> (or a documented subclass)
    /// — never a leaked framework exception (<c>ProtoException</c>, <c>JsonException</c>, etc.).
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializerExceptionContractTests
    {
        [Test]
        public void ProtoDeserializeNullBytesThrowsSerializationInputException()
        {
            SerializationInputException ex = Assert.Throws<SerializationInputException>(() =>
                Serializer.ProtoDeserialize<Sample>(null)
            );
            Assert.AreEqual(SerializationFormat.Protobuf, ex.Format);
            Assert.AreEqual(SerializationOperation.Deserialize, ex.Operation);
            Assert.AreEqual(SerializationStage.InputValidation, ex.Stage);
            Assert.AreEqual(typeof(Sample), ex.DeclaredType);
            Assert.IsTrue(ex.InnerException == null);
            StringAssert.Contains("Protobuf", ex.Message);
            StringAssert.Contains("null", ex.Message);
        }

        [Test]
        public void ProtoDeserializeWithTypeNullBytesThrowsSerializationInputException()
        {
            Assert.Throws<SerializationInputException>(() =>
                Serializer.ProtoDeserialize<object>(null, typeof(Sample))
            );
        }

        [Test]
        public void JsonDeserializeBytesNullBytesThrowsSerializationInputException()
        {
            SerializationInputException ex = Assert.Throws<SerializationInputException>(() =>
                Serializer.JsonDeserialize<Sample>((byte[])null)
            );
            Assert.AreEqual(SerializationFormat.Json, ex.Format);
            Assert.IsTrue(ex.InnerException == null);
        }

        [Test]
        public void JsonDeserializeStringNullStringThrowsSerializationInputException()
        {
            Assert.Throws<SerializationInputException>(() =>
                Serializer.JsonDeserialize<Sample>((string)null)
            );
        }

        [Test]
        public void JsonDeserializeFastNullBytesThrowsSerializationInputException()
        {
            SerializationInputException ex = Assert.Throws<SerializationInputException>(() =>
                Serializer.JsonDeserializeFast<Sample>(null)
            );
            Assert.AreEqual(SerializationFormat.JsonFast, ex.Format);
        }

        [Test]
        public void BinaryDeserializeNullBytesThrowsSerializationInputException()
        {
            SerializationInputException ex = Assert.Throws<SerializationInputException>(() =>
                Serializer.BinaryDeserialize<Sample>(null)
            );
            Assert.AreEqual(SerializationFormat.Binary, ex.Format);
        }

        [Test]
        public void DispatcherDeserializeNullBytesRoutesToInputException(
            [Values(SerializationType.Protobuf, SerializationType.Json)]
                SerializationType serializationType
        )
        {
            Assert.Throws<SerializationInputException>(() =>
                Serializer.Deserialize<Sample>(null, serializationType)
            );
        }

        [Test]
        public void ProtoDeserializeEmptyBytesReturnsTheAllDefaultsValue()
        {
            Sample value = Serializer.ProtoDeserialize<Sample>(Array.Empty<byte>());

            Assert.IsTrue(value != null);
            Assert.AreEqual(0, value.Id);
            Assert.IsTrue(value.Name == null);
        }

        [Test]
        public void JsonDeserializeBytesEmptyBytesThrowsSerializationInputException()
        {
            Assert.Throws<SerializationInputException>(() =>
                Serializer.JsonDeserialize<Sample>(Array.Empty<byte>())
            );
        }

        [Test]
        public void JsonDeserializeStringEmptyStringThrowsSerializationInputException()
        {
            Assert.Throws<SerializationInputException>(() =>
                Serializer.JsonDeserialize<Sample>(string.Empty)
            );
        }

        [Test]
        public void BinaryDeserializeEmptyBytesThrowsSerializationInputException()
        {
            Assert.Throws<SerializationInputException>(() =>
                Serializer.BinaryDeserialize<Sample>(Array.Empty<byte>())
            );
        }

        [Test]
        public void ProtoDeserializeGarbageBytesThrowsSerializationCorruptDataException()
        {
            byte[] garbage = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            SerializationCorruptDataException ex = Assert.Throws<SerializationCorruptDataException>(
                () =>
                    Serializer.ProtoDeserialize<Sample>(garbage)
            );
            Assert.AreEqual(SerializationFormat.Protobuf, ex.Format);
            Assert.AreEqual(SerializationStage.Decode, ex.Stage);
            Assert.AreEqual(typeof(Sample), ex.DeclaredType);
            Assert.IsTrue(
                ex.InnerException != null,
                "InnerException must preserve the underlying codec failure."
            );
            StringAssert.Contains("byte[" + garbage.Length, ex.Message);
        }

        [Test]
        public void JsonDeserializeBytesGarbageBytesThrowsSerializationCorruptDataException()
        {
            byte[] garbage = { 0xFF, 0xFE, 0xFD };
            SerializationCorruptDataException ex = Assert.Throws<SerializationCorruptDataException>(
                () =>
                    Serializer.JsonDeserialize<Sample>(garbage)
            );
            Assert.AreEqual(SerializationFormat.Json, ex.Format);
            Assert.IsTrue(ex.InnerException != null);
        }

        [Test]
        public void JsonDeserializeStringGarbageThrowsSerializationCorruptDataException()
        {
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Sample>("not json {{{")
            );
        }

        [Test]
        public void JsonDeserializeFastGarbageThrowsSerializationCorruptDataException()
        {
            byte[] garbage = { 0xFF };
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserializeFast<Sample>(garbage)
            );
        }

        [Test]
        public void BinaryDeserializeGarbageThrowsSerializationCorruptDataException()
        {
            byte[] garbage = { 0x00, 0x01, 0x02, 0x03 };
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.BinaryDeserialize<Sample>(garbage)
            );
        }

        [Test]
        public void ProtoDeserializeUnresolvedInterfaceThrowsSerializationTypeException()
        {
            byte[] data = Serializer.ProtoSerialize<IUnregistered>(new Unregistered { X = 1 });
            SerializationTypeException ex = Assert.Throws<SerializationTypeException>(() =>
                Serializer.ProtoDeserialize<IUnregistered>(data)
            );
            Assert.AreEqual(SerializationFormat.Protobuf, ex.Format);
            Assert.AreEqual(SerializationStage.TypeResolution, ex.Stage);
            Assert.AreEqual(typeof(IUnregistered), ex.DeclaredType);
        }

        [Test]
        public void DeserializeUnknownSerializationTypeThrowsConfiguration()
        {
            byte[] data = { 0x00 };
            Assert.Throws<SerializationConfigurationException>(() =>
                Serializer.Deserialize<Sample>(data, (SerializationType)9999)
            );
        }

        [Test]
        public void SerializeUnknownSerializationTypeThrowsConfiguration()
        {
            Assert.Throws<SerializationConfigurationException>(() =>
                Serializer.Serialize(new Sample(), (SerializationType)9999)
            );
        }

        [Test]
        public void SerializeWithBufferUnknownSerializationTypeThrowsConfiguration()
        {
            byte[] buffer = null;
            Assert.Throws<SerializationConfigurationException>(() =>
                Serializer.Serialize(new Sample(), (SerializationType)9999, ref buffer)
            );
        }

        [Test]
        public void ProtoDeserializeWithTypeNullTypeThrowsConfiguration()
        {
            byte[] data = Serializer.ProtoSerialize(new Sample { Id = 1 });
            Assert.Throws<SerializationConfigurationException>(() =>
                Serializer.ProtoDeserialize<object>(data, null)
            );
        }

        private static IEnumerable<TestCaseData> AllBadInputCases()
        {
            byte[][] badBytes =
            {
                null,
                Array.Empty<byte>(),
                new byte[] { 0xFF },
                new byte[] { 0xFF, 0xFE, 0xFD, 0xFC, 0xFB, 0xFA },
            };
            foreach (byte[] b in badBytes)
            {
                yield return new TestCaseData(b);
            }
        }

        [TestCaseSource(nameof(AllBadInputCases))]
        public void EveryDeserializerBadInputThrowsOnlySerializationFailure(byte[] bad)
        {
            AssertOnlySerializationFailure(() => Serializer.ProtoDeserialize<Sample>(bad));
            AssertOnlySerializationFailure(() => Serializer.JsonDeserialize<Sample>(bad));
            AssertOnlySerializationFailure(() => Serializer.JsonDeserializeFast<Sample>(bad));
            AssertOnlySerializationFailure(() => Serializer.BinaryDeserialize<Sample>(bad));
            AssertOnlySerializationFailure(() =>
                Serializer.Deserialize<Sample>(bad, SerializationType.Protobuf)
            );
            AssertOnlySerializationFailure(() =>
                Serializer.Deserialize<Sample>(bad, SerializationType.Json)
            );
        }

        private static void AssertOnlySerializationFailure(TestDelegate action)
        {
            try
            {
                action();
                // Successful decoding is allowed here; only leaked framework exceptions violate this contract.
            }
            catch (SerializationFailureException) { }
            catch (Exception other)
            {
                Assert.Fail(
                    "Serializer leaked a non-SerializationFailureException type: "
                        + other.GetType().FullName
                        + ": "
                        + other.Message
                );
            }
        }

        /*
            A null-payload pipeline must preserve the package failure type instead of leaking a MemoryStream
            argument exception.
        */

        [Test]
        public void ScreenshotRegressionNullPayloadInPipelineNeverLeaksMemoryStreamException()
        {
            byte[][] payloads = { null };
            foreach (byte[] payload in payloads)
            {
                try
                {
                    _ = Serializer.ProtoDeserialize<Sample>(payload);
                    Assert.Fail("Expected SerializationInputException to be thrown.");
                }
                catch (SerializationInputException) { }
                catch (ArgumentNullException ane)
                {
                    Assert.Fail(
                        "Regression: legacy MemoryStream(buffer:null) ArgumentNullException leaked: "
                            + ane.Message
                    );
                }
            }
        }

        [ProtoContract]
        private sealed class Sample
        {
            [ProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            public string Name { get; set; }
        }

        private interface IUnregistered { }

        [ProtoContract]
        private sealed class Unregistered : IUnregistered
        {
            [ProtoMember(1)]
            public int X { get; set; }
        }
    }
}
