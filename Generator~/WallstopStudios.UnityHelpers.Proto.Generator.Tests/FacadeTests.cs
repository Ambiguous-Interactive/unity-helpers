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
            Assert.IsTrue(WProtoFacade.TrySerialize<ScalarContract>(null, out byte[] bytes));
            Assert.IsNotNull(bytes);
            Assert.AreEqual(0, bytes.Length);
        }

        [Test]
        public void SerializingNullRunsNoSerializationHook()
        {
            Assert.IsTrue(WProtoFacade.TrySerialize<HookedContract>(null, out byte[] bytes));
            Assert.AreEqual(0, bytes.Length);
        }

        [Test]
        public void ACorruptPayloadIsReportedRatherThanHandedBackToProtobufNet()
        {
            byte[] corrupt = { 0x0A, 0x14, 0x01, 0x02 };

            // A failed registered formatter must not fall through to a second decoder.
            Assert.Throws<InvalidOperationException>(() =>
                WProtoFacade.TryDeserialize(corrupt, out ScalarContract _)
            );
        }

        [Test]
        public void AnUnregisteredTypeIsUnhandledRatherThanAnError()
        {
            Assert.IsFalse(WProtoFacade.TryDeserialize(new byte[] { 0x08, 0x01 }, out Type _));
            Assert.IsFalse(WProtoFacade.TrySerialize(typeof(string), out byte[] _));
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
            // Zero is a valid payload length, so absence needs a distinct representation.
            byte[] buffer = new byte[8];
            byte[] original = buffer;

            WProtoWriteResult result = WProtoFacade.Serialize(typeof(string), ref buffer);

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
            // A formatter that writes less than it measures must fail rather than silently fall back.
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
            AssertServedAndIdentical<IncludeBase>(
                new IncludeAlpha
                {
                    Id = 1,
                    Label = "a",
                    AlphaOnly = 7,
                    AlphaText = "x",
                }
            );

            AssertServedAndIdentical<IncludeBase>(
                new IncludeGamma
                {
                    Id = 1,
                    Label = "a",
                    BetaOnly = 1.5,
                    GammaOnly = true,
                }
            );

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
            // Unknown runtime subtypes remain available to explicitly configured protobuf-net fallback.
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
            // A declared type outside this contract chain belongs to the configured fallback decoder.
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
            // Delegating to the root includes sibling subtypes, so a subtype formatter must narrow its claim.
            IWProtoPolymorphicFormatter alpha = IncludeAlpha.WProtoRootFormatter.Instance;

            Assert.IsTrue(alpha.CanWrite(typeof(IncludeAlpha)));
            Assert.IsFalse(
                alpha.CanWrite(typeof(IncludeGamma)),
                "a sibling is not an IncludeAlpha"
            );
            Assert.IsFalse(alpha.CanWrite(typeof(UndeclaredAlpha)));

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

            public bool Write(ref WProtoWriter writer, in FacadeBrokenContract value) => true;

            public bool TryRead(ref WProtoReader reader, out FacadeBrokenContract value)
            {
                value = null;
                return false;
            }
        }
    }
}
