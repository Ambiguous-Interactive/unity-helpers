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
    public sealed class WProtoWriteCompletionTests
    {
        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 2)]
        [TestCase(false, 3)]
        [TestCase(false, 4)]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(true, 2)]
        [TestCase(true, 3)]
        [TestCase(true, 4)]
        public void UnclosedFieldsCannotCompleteAnyFacadeWrite(bool nested, int surface)
        {
            IWProtoFormatter<string> previous = WProtoFormatterProvider.TryGet(
                out IWProtoFormatter<string> registered
            )
                ? registered
                : null;
            try
            {
                WProtoFormatterProvider.Register<string>(new IncompleteFormatter(nested, null));
                byte[] buffer = new byte[3];
                ArrayBufferWriter<byte> destination = new ArrayBufferWriter<byte>();
                switch (surface)
                {
                    case 0:
                        Assert.Throws<InvalidOperationException>(() =>
                            WProtoFacade.Serialize("value", ref buffer)
                        );
                        break;
                    case 1:
                        Assert.Throws<InvalidOperationException>(() =>
                            WProtoFacade.TrySerialize("value", out byte[] _)
                        );
                        break;
                    case 2:
                        Assert.Throws<SerializationCorruptDataException>(() =>
                            WProtoFacade.Serialize("value")
                        );
                        break;
                    case 3:
                        Assert.Throws<SerializationCorruptDataException>(() =>
                            WProtoFacade.Serialize("value", destination)
                        );
                        break;
                    default:
                        Assert.IsFalse(
                            WProtoFacade.TrySerialize("value", destination, out int written)
                        );
                        Assert.AreEqual(0, written);
                        break;
                }
                Assert.AreEqual(0, destination.WrittenCount);
            }
            finally
            {
                WProtoFormatterProvider.Register(previous);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnclosedInnerFieldsLatchMessageFailureAndRestoreOuterDepth(bool nested)
        {
            byte[] buffer = new byte[16];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(writer.TryBeginLengthDelimited(4, out WProtoLengthToken outer));
            Assert.IsFalse(
                writer.TryWriteMessage(1, new IncompleteFormatter(nested, null), "value")
            );
            Assert.IsTrue(writer.Faulted);
            Assert.AreEqual(1, writer.Depth);
            byte[] snapshot = (byte[])buffer.Clone();
            Assert.IsFalse(writer.TryCloseLengthDelimited(outer));
            Assert.AreEqual(0, writer.Depth);
            Assert.IsFalse(writer.TryWriteVarint32(1));
            CollectionAssert.AreEqual(snapshot, buffer);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReplacingWriterCannotClearAFaultOrPublishAnotherDestination(bool sameBuffer)
        {
            IWProtoFormatter<string> previous = WProtoFormatterProvider.TryGet(
                out IWProtoFormatter<string> registered
            )
                ? registered
                : null;
            try
            {
                byte[] buffer = new byte[2];
                byte[] replacement = sameBuffer ? buffer : new byte[2];
                WProtoFormatterProvider.Register<string>(
                    new IncompleteFormatter(false, replacement)
                );
                Assert.Throws<InvalidOperationException>(() =>
                    WProtoFacade.Serialize("value", ref buffer)
                );
                ArrayBufferWriter<byte> destination = new ArrayBufferWriter<byte>();
                Assert.IsFalse(WProtoFacade.TrySerialize("value", destination, out int written));
                Assert.AreEqual(0, written);
                Assert.AreEqual(0, destination.WrittenCount);
            }
            finally
            {
                WProtoFormatterProvider.Register(previous);
            }
        }

        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(2, true)]
        [TestCase(3, false)]
        [TestCase(3, true)]
        [TestCase(4, false)]
        [TestCase(4, true)]
        public void InvalidCloseRefusesWithoutChangingBytesOrDepth(int mode, bool nested)
        {
            byte[] buffer = new byte[32];
            buffer.AsSpan().Fill(0xA5);
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(writer.TryBeginLengthDelimited(1, nested, out WProtoLengthToken first));
            Assert.IsTrue(writer.TryWriteVarint32(1));
            WProtoLengthToken invalid = default;
            if (mode == 1 || mode == 4)
            {
                WProtoWriter other = new WProtoWriter(mode == 4 ? buffer : new byte[32]);
                Assert.IsTrue(other.TryBeginLengthDelimited(1, nested, out invalid));
            }
            else if (mode == 2)
            {
                Assert.IsTrue(writer.TryCloseLengthDelimited(first));
                invalid = first;
            }
            else if (mode == 3)
            {
                Assert.IsTrue(writer.TryBeginLengthDelimited(2, nested, out _));
                invalid = first;
            }
            int position = writer.Position;
            int depth = writer.Depth;
            byte[] snapshot = (byte[])buffer.Clone();
            Assert.IsFalse(writer.TryCloseLengthDelimited(invalid));
            Assert.IsTrue(writer.Faulted);
            Assert.AreEqual(position, writer.Position);
            Assert.AreEqual(depth, writer.Depth);
            CollectionAssert.AreEqual(snapshot, buffer);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BalancedFieldsRetainTheirWireBytes(bool nested)
        {
            byte[] buffer = new byte[3];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(writer.TryBeginLengthDelimited(1, nested, out WProtoLengthToken token));
            Assert.IsTrue(writer.TryWriteVarint32(42));
            Assert.IsTrue(writer.TryCloseLengthDelimited(token));
            Assert.IsFalse(writer.Faulted);
            Assert.AreEqual(0, writer.Depth);
            CollectionAssert.AreEqual(new byte[] { 10, 1, 42 }, writer.Written.ToArray());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BalancedCustomFormatterRetainsFacadeBytes(bool nested)
        {
            IWProtoFormatter<string> previous = WProtoFormatterProvider.TryGet(
                out IWProtoFormatter<string> registered
            )
                ? registered
                : null;
            try
            {
                WProtoFormatterProvider.Register<string>(
                    new IncompleteFormatter(nested, null, true)
                );
                byte[] expected = new byte[] { 10, 1, 1 };
                CollectionAssert.AreEqual(expected, WProtoFacade.Serialize("value"));
                ArrayBufferWriter<byte> destination = new ArrayBufferWriter<byte>();
                Assert.AreEqual(3, WProtoFacade.Serialize("value", destination));
                CollectionAssert.AreEqual(expected, destination.WrittenSpan.ToArray());
            }
            finally
            {
                WProtoFormatterProvider.Register(previous);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReplacingMessageWriterRestoresTheOriginalFailedFrame(bool preopened)
        {
            byte[] buffer = new byte[32];
            WProtoWriter writer = new WProtoWriter(buffer);
            WProtoLengthToken outer = default;
            if (preopened)
            {
                Assert.IsTrue(writer.TryBeginLengthDelimited(3, out outer));
            }
            int depth = writer.Depth;
            Assert.IsFalse(
                writer.TryWriteMessage(1, new IncompleteFormatter(false, new byte[2]), "value")
            );
            Assert.IsTrue(writer.Faulted);
            Assert.AreEqual(depth, writer.Depth);
            Assert.IsFalse(writer.TryWriteVarint32(1));
            byte[] snapshot = (byte[])buffer.Clone();
            if (preopened)
            {
                Assert.IsFalse(writer.TryCloseLengthDelimited(outer));
                Assert.AreEqual(0, writer.Depth);
            }
            CollectionAssert.AreEqual(snapshot, buffer);
        }

        [TestCase(0, 0, 3)]
        [TestCase(0, 1, 3)]
        [TestCase(0, 3, 3)]
        [TestCase(1, 0, 3)]
        [TestCase(1, 1, 3)]
        [TestCase(1, 2, 3)]
        [TestCase(0, 0, 128)]
        [TestCase(1, 0, 128)]
        [TestCase(0, 0, 0)]
        public void OverlappingBytePayloadPreservesItsOriginalContents(
            int start,
            int sourceOffset,
            int count
        )
        {
            byte[] buffer = new byte[start + sourceOffset + count + 16];
            for (int index = 0; index < buffer.Length; ++index)
            {
                buffer[index] = (byte)(index + 42);
            }
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(writer.TryWriteRaw(new byte[start]));
            byte[] payload = buffer.AsSpan(sourceOffset, count).ToArray();
            byte[] prior = writer.Written.ToArray();
            Assert.IsTrue(writer.TryWriteBytes(buffer.AsSpan(sourceOffset, count)));
            int prefix = count < 128 ? 1 : 2;
            Assert.AreEqual(start + prefix + count, writer.Position);
            CollectionAssert.AreEqual(prior, writer.Written.Slice(0, start).ToArray());
            CollectionAssert.AreEqual(payload, writer.Written.Slice(start + prefix).ToArray());
            byte[] expectedPrefix =
                count < 128 ? new byte[] { (byte)count } : new byte[] { 128, 1 };
            CollectionAssert.AreEqual(
                expectedPrefix,
                writer.Written.Slice(start, prefix).ToArray()
            );
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(128)]
        public void RefusedOverlappingByteWritePreservesTheEntireDestination(int count)
        {
            int prefix = count < 128 ? 1 : 2;
            byte[] buffer = new byte[count + prefix - 1];
            buffer.AsSpan().Fill(42);
            byte[] snapshot = (byte[])buffer.Clone();
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsFalse(writer.TryWriteBytes(buffer.AsSpan(0, count)));
            Assert.IsTrue(writer.Faulted);
            Assert.AreEqual(0, writer.Position);
            CollectionAssert.AreEqual(snapshot, buffer);
        }

        private readonly struct IncompleteFormatter : IWProtoFormatter<string>
        {
            private readonly bool _nested;
            private readonly byte[] _replacement;
            private readonly bool _complete;

            internal IncompleteFormatter(bool nested, byte[] replacement, bool complete = false)
            {
                _nested = nested;
                _replacement = replacement;
                _complete = complete;
            }

            public int Measure(in string value)
            {
                return _replacement == null ? 3 : 2;
            }

            public bool Write(ref WProtoWriter writer, in string value)
            {
                if (_replacement != null)
                {
                    writer.TryWriteTag(0, WProtoWireType.Varint);
                    writer = new WProtoWriter(_replacement);
                    return writer.TryWriteTag(1, WProtoWireType.Varint)
                        && writer.TryWriteVarint32(42);
                }
                return writer.TryBeginLengthDelimited(1, _nested, out WProtoLengthToken token)
                    && writer.TryWriteVarint32(1)
                    && (!_complete || writer.TryCloseLengthDelimited(token));
            }

            public bool TryRead(ref WProtoReader reader, out string value)
            {
                value = null;
                return false;
            }
        }
    }
}
