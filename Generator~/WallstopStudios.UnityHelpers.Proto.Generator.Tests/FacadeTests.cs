// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using NUnit.Framework;
    using UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// The seam <c>Serializer</c> calls under <c>WALLSTOP_PROTO</c>. Every case here is about what
    /// the facade does when it CANNOT serve a request, because that is the half that decides whether
    /// a caller gets protobuf-net's answer or an exception.
    /// </summary>
    [TestFixture]
    public sealed class FacadeTests
    {
        [Test]
        public void SerializingNullReturnsTheEmptyPayloadRatherThanThrowing()
        {
            // protobuf-net encodes a null root as zero bytes. The facade previously answered "yes I
            // can serve this" for null and then dereferenced it inside the generated Measure, so a
            // ported contract turned a legal call into a NullReferenceException.
            Assert.IsTrue(WProtoFacade.TrySerialize<ScalarContract>(null, out byte[] bytes));
            Assert.IsNotNull(bytes);
            Assert.AreEqual(0, bytes.Length);
        }

        [Test]
        public void SerializingNullRunsNoSerializationHook()
        {
            // A hook on a null instance cannot run at all; the point of the assertion is that the
            // facade does not reach it. Measure is where the hook fires, so this is the same defect
            // observed from the other side.
            Assert.IsTrue(WProtoFacade.TrySerialize<HookedContract>(null, out byte[] bytes));
            Assert.AreEqual(0, bytes.Length);
        }

        [Test]
        public void ACorruptPayloadIsReportedRatherThanHandedBackToProtobufNet()
        {
            // A truncated length prefix: field 1, length-delimited, claims 20 bytes and supplies 2.
            byte[] corrupt = { 0x0A, 0x14, 0x01, 0x02 };

            // "The formatter exists and failed" and "no formatter exists" are different answers.
            // Returning false for both let Serializer.ProtoDeserialize continue into protobuf-net
            // with a payload WallstopProto had already rejected, so a corrupt buffer got a second,
            // differently-implemented decode instead of surfacing.
            Assert.Throws<InvalidOperationException>(() =>
                WProtoFacade.TryDeserialize(corrupt, out ScalarContract _)
            );
        }

        [Test]
        public void AnUnregisteredTypeIsUnhandledRatherThanAnError()
        {
            // The other half of the same distinction: no formatter means "not ours", which must stay
            // a quiet false so the caller falls back. This is what keeps the port incremental.
            Assert.IsFalse(WProtoFacade.TryDeserialize(new byte[] { 0x08, 0x01 }, out Uri _));
            Assert.IsFalse(WProtoFacade.TrySerialize(new Uri("https://example.com"), out byte[] _));
        }

        [Test]
        public void SerializingIntoABigEnoughBufferReusesItWithoutAllocating()
        {
            byte[] buffer = new byte[256];
            byte[] original = buffer;

            int written = WProtoFacade.Serialize(new ScalarContract { Int32 = 7 }, ref buffer);

            Assert.Greater(written, 0);
            Assert.AreSame(original, buffer, "a buffer with room to spare must not be replaced");
            Assert.AreEqual(256, buffer.Length, "the buffer must not be trimmed to the payload");
        }

        [Test]
        public void TheWrittenCountIsThePayloadLengthNotTheBufferLength()
        {
            // The property a caller has to respect: writing buffer.Length bytes would append
            // whatever the previous, larger message left behind.
            byte[] buffer = new byte[256];
            int written = WProtoFacade.Serialize(new ScalarContract { Int32 = 7 }, ref buffer);

            Assert.IsTrue(
                WProtoFacade.TrySerialize(new ScalarContract { Int32 = 7 }, out byte[] exact)
            );
            Assert.AreEqual(exact.Length, written);
            for (int index = 0; index < written; index++)
            {
                Assert.AreEqual(exact[index], buffer[index], "byte " + index);
            }
        }

        [Test]
        public void ATooSmallBufferIsGrownRatherThanOverrun()
        {
            byte[] buffer = new byte[1];
            byte[] original = buffer;

            int written = WProtoFacade.Serialize(
                new ScalarContract { Text = "a string comfortably longer than one byte" },
                ref buffer
            );

            Assert.Greater(written, 1);
            Assert.AreNotSame(original, buffer);
            Assert.GreaterOrEqual(buffer.Length, written);
        }

        [Test]
        public void AnUnservedTypeReturnsMinusOneAndLeavesTheBufferAlone()
        {
            // -1 rather than 0, because 0 is a legitimate payload length: an empty contract and a
            // null root both encode to nothing. A caller that treated "not served" as "empty" would
            // silently persist zero bytes for a type this serializer never handled.
            byte[] buffer = new byte[8];
            byte[] original = buffer;

            Assert.AreEqual(-1, WProtoFacade.Serialize(new Uri("https://example.com"), ref buffer));
            Assert.AreSame(original, buffer);
        }

        [Test]
        public void ANullRootWritesNothingAndAllocatesNothing()
        {
            byte[] buffer = null;

            Assert.AreEqual(0, WProtoFacade.Serialize<ScalarContract>(null, ref buffer));
            Assert.IsNull(buffer, "a null root has no payload, so it must not force an allocation");
        }

        [Test]
        public void ReusingOneBufferAcrossDifferentSizesNeverLeaksTheOlderPayload()
        {
            // The failure this API makes possible if the count is ignored: a long message followed
            // by a short one leaves stale bytes past the short one's end.
            byte[] buffer = null;

            int big = WProtoFacade.Serialize(
                new ScalarContract { Text = "a considerably longer string value" },
                ref buffer
            );
            int small = WProtoFacade.Serialize(new ScalarContract { Int32 = 1 }, ref buffer);

            Assert.Less(small, big);
            Assert.IsTrue(
                WProtoFacade.TrySerialize(new ScalarContract { Int32 = 1 }, out byte[] exact)
            );
            Assert.AreEqual(exact.Length, small);
            for (int index = 0; index < small; index++)
            {
                Assert.AreEqual(exact[index], buffer[index], "byte " + index);
            }
        }
    }
}
