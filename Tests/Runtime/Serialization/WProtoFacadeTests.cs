// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins the seam <c>Serializer</c> uses to serve a type through WallstopProto.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The swap is opt-in <b>per type</b>: each call asks whether a formatter exists for the declared
    /// type -- and, when the value's runtime type is a subtype, whether that formatter's
    /// <c>[WProtoInclude]</c> chain writes it -- and falls back when it does not. That is what makes
    /// porting the remaining contracts incremental and individually verifiable rather than one change
    /// that moves everything at once.
    /// </para>
    /// <para>
    /// These run whether or not <c>WALLSTOP_PROTO</c> is defined, because the seam compiles
    /// unconditionally. What the define controls is only whether <c>Serializer</c> calls it.
    /// </para>
    /// <para>
    /// Only the oracle comparison is skipped under IL2CPP, and for the reason the package exists:
    /// protobuf-net's own static initializer throws there. Everything else in this fixture runs on
    /// every leg.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoFacadeTests
    {
        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void APortedTypeIsServedAndMatchesProtobufNetByteForByte()
        {
            // Skipped under IL2CPP because the ORACLE cannot run there -- protobuf-net's
            // `TypeHelper<T>` static constructor throws, which is the whole reason WallstopProto
            // exists. Byte-equality is proven off-Unity by the differential suite under Generator~;
            // what the IL2CPP legs verify is that the generated code itself runs, which the other
            // tests in this fixture do without touching the oracle.
            // FastVector2Int and friends already have formatters, so the facade serves them -- and
            // the bytes have to equal what protobuf-net would have written, or the swap changes
            // saved data.
            AssertServedAndIdentical(new FastVector2Int(1, -2));
            AssertServedAndIdentical(new FastVector3Int(3, 4, -5));
            AssertServedAndIdentical(default(FastVector2Int));
        }

        [Test]
        public void AnUnportedTypeIsNotServed()
        {
            // The fallback that makes incremental porting safe: no formatter, no interception.
            Assert.IsFalse(
                WProtoFacade.TrySerialize(new UnportedThing { Value = 1 }, out byte[] _)
            );
            Assert.IsFalse(
                WProtoFacade.TryDeserialize(new byte[] { 0x08, 0x01 }, out UnportedThing _)
            );
        }

        [Test]
        public void APortedValueRoundTripsThroughTheSeam()
        {
            Assert.IsTrue(WProtoFacade.TrySerialize(new FastVector3Int(7, 8, 9), out byte[] bytes));
            Assert.IsTrue(WProtoFacade.TryDeserialize(bytes, out FastVector3Int restored));
            Assert.AreEqual(new FastVector3Int(7, 8, 9), restored);
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void AGeneratorHeldAsAbstractRandomIsServedAndMatchesProtobufNet()
        {
            // The case that decides whether the seventeen generators take this path at all. A PRNG
            // is almost never held as its concrete type -- AbstractRandom is the declared type this
            // package documents -- so an exact-type-only seam served none of them in practice, and
            // every save went through protobuf-net, which is what cannot run under IL2CPP.
            AssertServedAndIdentical<AbstractRandom>(new PcgRandom(12345));
            AssertServedAndIdentical<AbstractRandom>(new SquirrelRandom(999));
        }

        [Test]
        public void AGeneratorHeldAsAbstractRandomComesBackAsItsConcreteType()
        {
            AbstractRandom original = new PcgRandom(4242);

            // Advanced first, so the saved state is not the one the seed alone would rebuild and a
            // reader that ignored the payload could not pass by luck.
            original.NextUint();

            Assert.IsTrue(WProtoFacade.TrySerialize(original, out byte[] bytes));
            Assert.IsTrue(WProtoFacade.TryDeserialize(bytes, out AbstractRandom restored));

            Assert.IsInstanceOf<PcgRandom>(restored, "the subtype must survive the round trip");

            // The state has to survive with the type, or the restored generator is a different
            // stream wearing the right name.
            Assert.AreEqual(original.NextUint(), restored.NextUint());
        }

        [Test]
        public void AnUndeclaredSubtypeIsNotServedThroughItsBasesFormatter()
        {
            // A subtype no [WProtoInclude] names has no encoding here: written under its nearest
            // declared ancestor's tag it would read back AS that ancestor, losing a level of type
            // identity in saved data. The seam declines and protobuf-net answers, which is what a
            // consumer who registered the subtype with protobuf-net's runtime model expects.
            AbstractRandom undeclared = new UndeclaredRandom();

            Assert.IsFalse(WProtoFacade.TrySerialize(undeclared, out byte[] bytes));
            Assert.IsTrue(bytes == null);
        }

        private static void AssertServedAndIdentical<T>(T value)
        {
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out byte[] mine), typeof(T).Name);

            using MemoryStream stream = new();
            ProtoBuf.Serializer.Serialize(stream, value);
            CollectionAssert.AreEqual(stream.ToArray(), mine, typeof(T).Name);
        }

        private sealed class UnportedThing
        {
            public int Value;
        }

        /// <summary>
        /// A generator <see cref="AbstractRandom"/> does not declare, which is the shape a consumer
        /// writing their own PRNG produces.
        /// </summary>
        private sealed class UndeclaredRandom : AbstractRandom
        {
            private uint _state = 1;

            public override RandomState InternalState => new RandomState(_state);

            public override uint NextUint()
            {
                _state = (_state * 1664525) + 1013904223;
                return _state;
            }

            public override IRandom Copy()
            {
                return new UndeclaredRandom { _state = _state };
            }
        }
    }
}
