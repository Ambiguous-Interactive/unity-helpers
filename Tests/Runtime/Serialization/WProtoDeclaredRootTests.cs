// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins the one declared root this package ships: <c>IRandom</c>, served by
    /// <c>AbstractRandom</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A generator is almost never held as its concrete type. <c>IRandom</c> is the declared type
    /// this package's own documentation recommends, and an interface has no members, so without a
    /// declared root the facade declines and every realistic call takes the protobuf-net path --
    /// the one that does not run under IL2CPP, which is the reason WallstopProto exists.
    /// </para>
    /// <para>
    /// The facade is called directly rather than through <c>Serializer.ProtoSerialize</c>, which
    /// only routes here once <c>WALLSTOP_PROTO</c> is defined. What the oracle comparison asserts is
    /// that the bytes are unchanged: <c>ResolveProtobufRootType</c> already resolves
    /// <c>IRandom</c> to <c>AbstractRandom</c> by scanning the interface's assembly, so a declared
    /// root states an answer that shipped rather than inventing one.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoDeclaredRootTests
    {
        private static IEnumerable<IRandom> Generators()
        {
            yield return new PcgRandom(12345L);
            yield return new XorShiftRandom(6789);
            yield return new SquirrelRandom(42);
            yield return new DotNetRandom(new Guid("2f1f0b6c-6f2f-4c4a-9b1e-0d3a5c7e9f11"));
        }

        [TearDown]
        public void ReleaseRoots()
        {
            // A claim is process-global; leaking one leaves IRandom unserved for every test after.
            Serializer.ClearProtobufRootCacheForTesting(typeof(IRandom));
        }

        [Test]
        public void TheGeneratedRegistrarServesIRandomThroughAbstractRandom()
        {
            Assert.IsTrue(
                WProtoDeclaredRootProvider.TryGetRoot(typeof(IRandom), out Type root),
                "the [assembly: WProtoDeclaredRoot] pair is the whole registration"
            );
            Assert.AreEqual(typeof(AbstractRandom), root);
            Assert.IsTrue(
                WProtoDeclaredRootProvider.TryGetFormatter(out IWProtoFormatter<IRandom> _)
            );
        }

        /// <summary>
        /// A declared root is root-only, like a root marshal and for the same kind of reason.
        /// </summary>
        /// <remarks>
        /// <see cref="WProtoGeneric{T}"/> resolves a member whose type a closure decides through
        /// <see cref="WProtoFormatterProvider"/>, and asks it no <c>CanServe</c> or
        /// <c>CanWrite</c> question. An adapter registered there made <c>Deque&lt;IRandom&gt;</c>
        /// encodable -- writing a member as <c>AbstractRandom</c>'s message, which protobuf-net has
        /// no counterpart for, and writing NOTHING for an element outside the include chain while
        /// reporting success. Both shapes declined before this pair existed and must decline still.
        /// </remarks>
        [Test]
        public void ADeclaredRootIsNotReachableFromAMemberPosition()
        {
            Assert.IsFalse(WProtoFormatterProvider.IsRegistered<IRandom>());
            Assert.IsFalse(WProtoGeneric<IRandom>.CanEncode);
            Assert.IsFalse(WProtoRootMarshalProvider.IsRegistered<IRandom>());
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void AGeneratorHeldAsIRandomWritesWhatProtobufNetWrites()
        {
            foreach (IRandom generator in Generators())
            {
                Advance(generator);

                Assert.IsTrue(
                    WProtoFacade.TrySerialize(generator, out byte[] mine),
                    generator.GetType().Name
                );

                using MemoryStream stream = new();
                ProtoBuf.Serializer.Serialize(stream, (AbstractRandom)generator);

                CollectionAssert.AreEqual(stream.ToArray(), mine, generator.GetType().Name);
            }
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void ADeclaredRootReadsWhatProtobufNetWroteThroughIt()
        {
            /*
                The direction that matters for save files that already exist: every one of them came out of
                protobuf-net, through the root its own resolution picked.
            */
            foreach (IRandom generator in Generators())
            {
                Advance(generator);

                using MemoryStream stream = new();
                ProtoBuf.Serializer.Serialize(stream, (AbstractRandom)generator);

                Assert.IsTrue(
                    WProtoFacade.TryDeserialize(stream.ToArray(), out IRandom restored),
                    generator.GetType().Name
                );
                Assert.AreEqual(generator.GetType(), restored.GetType());
                Assert.AreEqual(
                    generator.InternalState,
                    restored.InternalState,
                    generator.GetType().Name
                );
            }
        }

        [Test]
        public void AGeneratorHeldAsIRandomRoundTripsToTheSameStream()
        {
            foreach (IRandom generator in Generators())
            {
                Advance(generator);

                Assert.IsTrue(WProtoFacade.TrySerialize(generator, out byte[] bytes));
                Assert.IsTrue(WProtoFacade.TryDeserialize(bytes, out IRandom restored));

                Assert.AreEqual(
                    generator.GetType(),
                    restored.GetType(),
                    "the concrete generator must survive the interface"
                );
                Assert.AreEqual(generator.InternalState, restored.InternalState);
                for (int index = 0; index < 32; ++index)
                {
                    Assert.AreEqual(
                        generator.NextUint(),
                        restored.NextUint(),
                        generator.GetType().Name + " value " + index
                    );
                }
            }
        }

        /// <summary>
        /// A consumer who names their own root for <c>IRandom</c> gets protobuf-net, as before.
        /// </summary>
        /// <remarks>
        /// This is the hazard that kept #403 open. <c>TryDeserialize</c> has no second chance -- a
        /// formatter that answers and then rejects the payload throws by design -- so an adapter
        /// that kept serving here would turn a working call into an exception, on a consumer's own
        /// data, decoded through a chain that never wrote it.
        /// </remarks>
        [Test]
        public void AConsumersOwnRootStopsWallstopProtoAnswering()
        {
            IRandom generator = new PcgRandom(99);
            Assert.IsTrue(WProtoFacade.TrySerialize(generator, out byte[] before));

            Serializer.RegisterProtobufRoot<IRandom, PcgRandom>();

            Assert.IsFalse(
                WProtoFacade.TrySerialize(generator, out byte[] _),
                "the write side must defer to the root the consumer named"
            );
            Assert.IsFalse(
                WProtoFacade.TryDeserialize(before, out IRandom _),
                "and the read side must fall back rather than throw"
            );

            Serializer.ClearProtobufRootCacheForTesting(typeof(IRandom));

            Assert.IsTrue(
                WProtoFacade.TrySerialize(generator, out byte[] _),
                "releasing the claim restores the declaration"
            );
        }

        private static void Advance(IRandom generator)
        {
            /*
                A freshly seeded generator and a used one differ in the reservoir members, which are the ones a
                naive formatter drops.
            */
            for (int index = 0; index < 17; ++index)
            {
                generator.NextUint();
            }
        }
    }
}
