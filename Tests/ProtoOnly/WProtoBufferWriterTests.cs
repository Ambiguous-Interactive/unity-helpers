// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Buffers;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [TestFixture]
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class WProtoBufferWriterTests
    {
        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(8, false)]
        [TestCase(1024, false)]
        [TestCase(10000, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(8, true)]
        [TestCase(1024, true)]
        [TestCase(10000, true)]
        public void WriterAppendsExactPayloadWithoutReplacingItsPrefix(int length, bool tryWrite)
        {
            WProtoBufferWriterContract value = new WProtoBufferWriterContract
            {
                Number = length,
                Data = new byte[length],
            };
            for (int index = 0; index < length; ++index)
            {
                value.Data[index] = unchecked((byte)index);
            }
            byte[] expected = WProtoFacade.Serialize(value);
            ArrayBufferWriter<byte> writer = new ArrayBufferWriter<byte>(1);
            writer.GetSpan(1)[0] = 255;
            writer.Advance(1);
            for (int round = 0; round < 2; ++round)
            {
                int written;
                if (tryWrite)
                {
                    Assert.IsTrue(WProtoFacade.TrySerialize(value, writer, out written));
                }
                else
                {
                    written = WProtoFacade.Serialize(value, writer);
                }
                Assert.AreEqual(expected.Length, written);
            }
            Assert.AreEqual(1 + expected.Length * 2, writer.WrittenCount);
            Assert.AreEqual(255, writer.WrittenSpan[0]);
            Assert.IsTrue(writer.WrittenSpan.Slice(1, expected.Length).SequenceEqual(expected));
            Assert.IsTrue(writer.WrittenSpan.Slice(1 + expected.Length).SequenceEqual(expected));
            WProtoBufferWriterContract restored =
                WProtoFacade.Deserialize<WProtoBufferWriterContract>(expected);
            Assert.AreEqual(value.Number, restored.Number);
            CollectionAssert.AreEqual(value.Data, restored.Data);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NullRootDoesNotAdvanceTheWriter(bool tryWrite)
        {
            ArrayBufferWriter<byte> writer = new ArrayBufferWriter<byte>();
            if (tryWrite)
            {
                Assert.IsTrue(
                    WProtoFacade.TrySerialize<WProtoBufferWriterContract>(
                        null,
                        writer,
                        out int written
                    )
                );
                Assert.AreEqual(0, written);
            }
            else
            {
                Assert.AreEqual(
                    0,
                    WProtoFacade.Serialize<WProtoBufferWriterContract>(null, writer)
                );
            }
            Assert.AreEqual(0, writer.WrittenCount);
        }

        [TestCase(WProtoFaultBufferWriter.SpanFailure)]
        [TestCase(WProtoFaultBufferWriter.ShortSpan)]
        [TestCase(WProtoFaultBufferWriter.AdvanceFailure)]
        public void EmptyPayloadDoesNotRequestOrAdvanceTheDestination(int mode)
        {
            WProtoFaultBufferWriter writer = new WProtoFaultBufferWriter(mode);
            WProtoBufferWriterContract value = new WProtoBufferWriterContract();
            Assert.AreEqual(0, WProtoFacade.Serialize(value).Length);
            Assert.AreEqual(0, WProtoFacade.Serialize(value, writer));
            Assert.IsTrue(WProtoFacade.TrySerialize(value, writer, out int written));
            Assert.AreEqual(0, written);
            Assert.AreEqual(0, writer.Advanced);
        }

        [Test]
        public void UnsupportedModelHasTypedFailureAndNoDestinationWrites()
        {
            ArrayBufferWriter<byte> writer = new ArrayBufferWriter<byte>();
            SerializationTypeException failure = Assert.Throws<SerializationTypeException>(() =>
                WProtoFacade.Serialize<Type>(typeof(int), writer)
            );
            Assert.AreEqual(typeof(Type), failure.DeclaredType);
            Assert.AreEqual(SerializationOperation.Serialize, failure.Operation);
            Assert.IsFalse(WProtoFacade.TrySerialize<Type>(typeof(int), writer, out int written));
            Assert.AreEqual(0, written);
            Assert.AreEqual(0, writer.WrittenCount);
        }

        [Test]
        public void NullDestinationHasTypedFailureAndTryReturnsFalse()
        {
            WProtoBufferWriterContract value = new WProtoBufferWriterContract { Number = 7 };
            Assert.Throws<SerializationConfigurationException>(() =>
                WProtoFacade.Serialize(value, (IBufferWriter<byte>)null)
            );
            Assert.IsFalse(
                WProtoFacade.TrySerialize(value, (IBufferWriter<byte>)null, out int written)
            );
            Assert.AreEqual(0, written);
        }

        [TestCase(WProtoFaultBufferWriter.SpanFailure)]
        [TestCase(WProtoFaultBufferWriter.ShortSpan)]
        [TestCase(WProtoFaultBufferWriter.AdvanceFailure)]
        public void DestinationFailuresAreTypedAndTryReturnsFalse(int mode)
        {
            WProtoFaultBufferWriter writer = new WProtoFaultBufferWriter(mode);
            WProtoBufferWriterContract value = new WProtoBufferWriterContract { Number = 7 };
            SerializationCorruptDataException failure =
                Assert.Throws<SerializationCorruptDataException>(() =>
                    WProtoFacade.Serialize(value, writer)
                );
            Assert.IsTrue(failure.InnerException != null);
            Assert.AreEqual(SerializationStage.Encode, failure.Stage);
            Assert.IsFalse(WProtoFacade.TrySerialize(value, writer, out int written));
            Assert.AreEqual(0, written);
            Assert.AreEqual(0, writer.Advanced);
        }

        [Test]
        public void CallbackFailureHasTypedCauseWithoutAdvancingTheDestination()
        {
            WProtoBufferWriterContract value = new WProtoBufferWriterContract
            {
                FailSerialization = true,
            };
            ArrayBufferWriter<byte> writer = new ArrayBufferWriter<byte>();
            SerializationCorruptDataException failure =
                Assert.Throws<SerializationCorruptDataException>(() =>
                    WProtoFacade.Serialize(value, writer)
                );
            Assert.IsInstanceOf<InvalidOperationException>(failure.InnerException);
            Assert.AreEqual(SerializationStage.Encode, failure.Stage);
            Assert.IsFalse(WProtoFacade.TrySerialize(value, writer, out int written));
            Assert.AreEqual(0, written);
            Assert.AreEqual(0, writer.WrittenCount);
        }
    }
}
