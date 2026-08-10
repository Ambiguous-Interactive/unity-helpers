// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins the seam <c>Serializer</c> uses to serve a type through WallstopProto.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The swap is opt-in <b>per type</b>: each call asks whether a formatter exists for exactly the
    /// declared type and falls back when it does not. That is what makes porting the remaining
    /// contracts incremental and individually verifiable rather than one change that moves
    /// everything at once.
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
        public void ASubtypeIsNotServedThroughItsBasesFormatter()
        {
            // A formatter is per declared type. Serving a subtype through its base would drop
            // everything the subtype declares -- the same failure the generator refuses at build
            // time -- so the seam declines rather than guessing.
            BaseThing subtype = new DerivedThing { Value = 1, Extra = 2 };

            Assert.IsFalse(WProtoFacade.TrySerialize(subtype, out byte[] _));
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

        private class BaseThing
        {
            public int Value;
        }

        private sealed class DerivedThing : BaseThing
        {
            public int Extra;
        }
    }
}
