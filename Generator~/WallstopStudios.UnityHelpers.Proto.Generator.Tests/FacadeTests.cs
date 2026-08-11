// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.IO;
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

            WProtoWriteResult result = WProtoFacade.Serialize(
                new ScalarContract { Int32 = 7 },
                ref buffer
            );

            Assert.IsTrue(result.Served);
            Assert.Greater(result.Length, 0);
            Assert.IsFalse(result.Resized, "a buffer with room to spare must not be replaced");
            Assert.AreSame(original, buffer);
            Assert.AreEqual(256, buffer.Length, "the buffer must not be trimmed to the payload");
        }

        [Test]
        public void TheWrittenCountIsThePayloadLengthNotTheBufferLength()
        {
            // The property a caller has to respect: writing buffer.Length bytes would append
            // whatever the previous, larger message left behind.
            byte[] buffer = new byte[256];
            int written = WProtoFacade
                .Serialize(new ScalarContract { Int32 = 7 }, ref buffer)
                .Length;

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

            WProtoWriteResult result = WProtoFacade.Serialize(
                new ScalarContract { Text = "a string comfortably longer than one byte" },
                ref buffer
            );

            Assert.IsTrue(result.Served);
            Assert.Greater(result.Length, 1);
            Assert.IsTrue(result.Resized, "the caller must be told its array was replaced");
            Assert.AreNotSame(original, buffer);
            Assert.GreaterOrEqual(buffer.Length, result.Length);
        }

        [Test]
        public void AnUnservedTypeIsNotExpressibleAsALengthAndLeavesTheBufferAlone()
        {
            // A null count rather than a sentinel, because 0 is a legitimate payload length: an
            // empty contract and a null root both encode to nothing. A magic -1 would read as a
            // length everywhere it was passed on.
            byte[] buffer = new byte[8];
            byte[] original = buffer;

            WProtoWriteResult result = WProtoFacade.Serialize(
                new Uri("https://example.com"),
                ref buffer
            );

            Assert.IsFalse(result.Served);
            Assert.IsNull(result.BytesWritten);
            Assert.IsFalse(result.Resized);
            Assert.AreSame(original, buffer);
        }

        [Test]
        public void ANullRootWritesNothingAndAllocatesNothing()
        {
            byte[] buffer = null;

            WProtoWriteResult result = WProtoFacade.Serialize<ScalarContract>(null, ref buffer);

            Assert.IsTrue(result.Served, "a null root IS served; it simply encodes to nothing");
            Assert.AreEqual(0, result.BytesWritten);
            Assert.IsFalse(result.Resized);
            Assert.IsNull(buffer, "a null root has no payload, so it must not force an allocation");
        }

        [Test]
        public void ReusingOneBufferAcrossDifferentSizesNeverLeaksTheOlderPayload()
        {
            // The failure this API makes possible if the count is ignored: a long message followed
            // by a short one leaves stale bytes past the short one's end.
            byte[] buffer = null;

            int big = WProtoFacade
                .Serialize(
                    new ScalarContract { Text = "a considerably longer string value" },
                    ref buffer
                )
                .Length;
            WProtoWriteResult second = WProtoFacade.Serialize(
                new ScalarContract { Int32 = 1 },
                ref buffer
            );
            int small = second.Length;

            Assert.Less(small, big);
            Assert.IsFalse(second.Resized, "the second, smaller message must reuse the buffer");
            Assert.IsTrue(
                WProtoFacade.TrySerialize(new ScalarContract { Int32 = 1 }, out byte[] exact)
            );
            Assert.AreEqual(exact.Length, small);
            for (int index = 0; index < small; index++)
            {
                Assert.AreEqual(exact[index], buffer[index], "byte " + index);
            }
        }

        [Test]
        public void AFailedWriteIsReportedRatherThanLookingUnserved()
        {
            // The serialize-side mirror of the refused-read fallthrough. "Not served" sends the
            // value on to protobuf-net; a registered formatter that FAILED must not do that, or the
            // bytes come from the reflection path this serializer exists to avoid.
            //
            // Driven through a formatter that measures more than it writes, which is the exact
            // internal inconsistency the throw is meant to surface.
            WProtoFormatterProvider.Register<FacadeBrokenContract>(new BrokenFormatter());
            try
            {
                byte[] buffer = null;
                Assert.Throws<InvalidOperationException>(() =>
                    WProtoFacade.Serialize(new FacadeBrokenContract(), ref buffer)
                );
            }
            finally
            {
                WProtoFormatterProvider.Register<FacadeBrokenContract>(null);
            }
        }

        [Test]
        public void AValueHeldAsItsBaseIsServedAndMatchesTheOracle()
        {
            // The case the facade used to decline. A generator is almost never held as its concrete
            // type -- `AbstractRandom` is the declared type this package documents -- so refusing
            // anything but an exact type match meant every realistic call took the protobuf-net path,
            // which is the one that cannot run under IL2CPP.
            AssertServedAndIdentical<IncludeBase>(
                new IncludeAlpha
                {
                    Id = 1,
                    Label = "a",
                    AlphaOnly = 7,
                    AlphaText = "x",
                }
            );

            // Two levels down, so the answer is not "the direct subtype happens to work".
            AssertServedAndIdentical<IncludeBase>(
                new IncludeGamma
                {
                    Id = 1,
                    Label = "a",
                    BetaOnly = 1.5,
                    GammaOnly = true,
                }
            );

            // And through the middle of the chain, where the declared type is itself a subtype.
            AssertServedAndIdentical<IncludeBeta>(new IncludeGamma { BetaOnly = 2.5 });
        }

        [Test]
        public void AValueHeldAsItsBaseComesBackAsTheSubtype()
        {
            IncludeBase original = new IncludeGamma
            {
                Id = 4,
                Label = "z",
                BetaOnly = 1.5,
                GammaOnly = true,
            };

            Assert.IsTrue(WProtoFacade.TrySerialize(original, out byte[] bytes));
            Assert.IsTrue(WProtoFacade.TryDeserialize(bytes, out IncludeBase restored));

            IncludeGamma gamma = restored as IncludeGamma;
            Assert.IsNotNull(gamma, "the subtype must survive the round trip");
            Assert.AreEqual(4, gamma.Id);
            Assert.AreEqual(1.5, gamma.BetaOnly);
            Assert.IsTrue(gamma.GammaOnly);
        }

        [Test]
        public void AnUndeclaredSubtypeFallsBackInsteadOfThrowing()
        {
            // The generated dispatch chain refuses a subtype no include names, by design. The facade
            // has to read that refusal as "not mine" and let protobuf-net answer -- which is what a
            // consumer who registered the subtype with protobuf-net's runtime model expects -- rather
            // than propagating an exception out of a call that used to work.
            IncludeBase undeclared = new UndeclaredAlpha { AlphaOnly = 7 };

            Assert.IsFalse(WProtoFacade.TrySerialize(undeclared, out byte[] bytes));
            Assert.IsNull(bytes);

            byte[] buffer = new byte[8];
            byte[] original = buffer;
            WProtoWriteResult result = WProtoFacade.Serialize(undeclared, ref buffer);

            Assert.IsFalse(result.Served);
            Assert.AreSame(original, buffer, "an unserved value must leave the buffer alone");
        }

        [Test]
        public void TheBufferOverloadServesASubtypeThroughItsBaseToo()
        {
            // Both write entry points share CanServe; this is what stops the allocation-free one
            // from being the odd one out.
            byte[] buffer = null;
            WProtoWriteResult result = WProtoFacade.Serialize<IncludeBase>(
                new IncludeAlpha { Id = 1, AlphaOnly = 7 },
                ref buffer
            );

            Assert.IsTrue(result.Served);
            Assert.Greater(result.Length, 0);
        }

        [Test]
        public void ReadingIntoANamedTypeThisFormatterDoesNotProduceIsNotServed()
        {
            // The read mirror of CanServe, and the entry point it guards is the one a caller reaches
            // for precisely when the declared type is not the type on the wire. Naming a type this
            // contract's chain never produces means the payload is not this contract's, so the
            // request belongs to protobuf-net -- decoding it here would hand back the wrong type
            // from bytes something else wrote.
            Assert.IsTrue(
                WProtoFacade.TrySerialize<IncludeBase>(
                    new IncludeAlpha { Id = 1, AlphaOnly = 7 },
                    out byte[] bytes
                )
            );

            Assert.IsTrue(
                WProtoFacade.TryDeserializeAs(bytes, typeof(IncludeBase), out IncludeBase _)
            );
            Assert.IsTrue(
                WProtoFacade.TryDeserializeAs(bytes, typeof(IncludeAlpha), out IncludeBase _),
                "a subtype the chain declares is this formatter's to produce"
            );

            Assert.IsFalse(
                WProtoFacade.TryDeserializeAs(bytes, typeof(UndeclaredAlpha), out IncludeBase _),
                "a subtype no include names is not produced by this chain"
            );
            Assert.IsFalse(
                WProtoFacade.TryDeserializeAs(bytes, typeof(ScalarContract), out IncludeBase _),
                "an unrelated contract is not produced by this chain"
            );
        }

        [Test]
        public void AFormatterAnswersOnlyForTypesItsDeclaredTypeCanHold()
        {
            // A subtype's entry point delegates to the root of its chain, which covers the root's
            // WHOLE subtree -- including this type's siblings. Without a narrowing test it would
            // claim a sibling it can never be handed, and the interface's answer would only be
            // correct because of where the facade happens to ask it from.
            IWProtoPolymorphicFormatter alpha = IncludeAlpha.WProtoRootFormatter.Instance;

            Assert.IsTrue(alpha.CanWrite(typeof(IncludeAlpha)));
            Assert.IsFalse(
                alpha.CanWrite(typeof(IncludeGamma)),
                "a sibling is not an IncludeAlpha"
            );
            Assert.IsFalse(alpha.CanWrite(typeof(UndeclaredAlpha)));

            // The root does hold all of them, which is what makes the case above a narrowing rather
            // than a missing branch.
            IWProtoPolymorphicFormatter root = IncludeBase.WProtoFormatter.Instance;

            Assert.IsTrue(root.CanWrite(typeof(IncludeGamma)));
            Assert.IsFalse(root.CanWrite(typeof(UndeclaredAlpha)));
        }

        private static void AssertServedAndIdentical<T>(T value)
        {
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out byte[] mine), typeof(T).Name);

            using MemoryStream stream = new();
            ProtoBuf.Serializer.Serialize(stream, value);

            CollectionAssert.AreEqual(stream.ToArray(), mine, typeof(T).Name);
        }

        private sealed class FacadeBrokenContract { }

        private sealed class BrokenFormatter : IWProtoFormatter<FacadeBrokenContract>
        {
            public int Measure(in FacadeBrokenContract value) => 4;

            public bool Write(ref WProtoWriter writer, in FacadeBrokenContract value) => false;

            public bool TryRead(ref WProtoReader reader, out FacadeBrokenContract value)
            {
                value = null;
                return false;
            }
        }
    }
}
