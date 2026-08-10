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
    }
}
