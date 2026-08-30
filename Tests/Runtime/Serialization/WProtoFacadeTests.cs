// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Core.Serialization;
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
    /// Most tests exercise the seam directly. The default-dispatch test calls every public
    /// <see cref="Serializer"/> shape with a marker formatter, proving the runtime assembly's
    /// <c>WALLSTOP_PROTO</c> version define actually enters this path.
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
            //
            // The oracle has to be woken first, and here that is load-bearing rather than a
            // formality: without its surrogate registered, protobuf-net does not refuse
            // FastVector2Int the way it refuses Vector2 -- it silently encodes the type's own
            // contract instead, on the int32 fields the surrogate retired. This assertion then
            // fails for a reason that has nothing to do with the code under test, and used to pass
            // only because some earlier fixture happened to touch Serializer first.
            ProtobufUnityModel.EnsureInitialized();
            AssertServedAndIdentical(new FastVector2Int(1, -2));
            AssertServedAndIdentical(new FastVector3Int(3, 4, -5));
            // A zero-initialized instance is the case where the two encoders can most easily drift:
            // it stores a cached hash of 0 that no constructor produces, and anything mirroring the
            // wire through GetHashCode() rather than the stored field writes bytes the formatter
            // does not. Both ranks are covered because both carry that cache.
            AssertServedAndIdentical(default(FastVector2Int));
            AssertServedAndIdentical(default(FastVector3Int));
            AssertServedAndIdentical(new FastVector2Int(0, 0));
            AssertServedAndIdentical(new FastVector3Int(0, 0, 0));
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
        public void SerializerUsesWallstopProtoByDefaultForEveryPublicShape()
        {
            WProtoFormatterProvider.TryGet(out IWProtoFormatter<DefaultDispatchMarker> original);
            try
            {
                WProtoFormatterProvider.Register<DefaultDispatchMarker>(
                    new DefaultDispatchMarkerFormatter()
                );
                DefaultDispatchMarker value = new DefaultDispatchMarker { Value = 7 };

                CollectionAssert.AreEqual(
                    new byte[] { 0x08, 0x07 },
                    Serializer.ProtoSerialize(value)
                );
                CollectionAssert.AreEqual(
                    new byte[] { 0x08, 0x07 },
                    Serializer.ProtoSerialize(value, forceRuntimeType: true)
                );

                byte[] buffer = Array.Empty<byte>();
                Assert.AreEqual(2, Serializer.ProtoSerialize(value, ref buffer));
                CollectionAssert.AreEqual(new byte[] { 0x08, 0x07 }, buffer);

                byte[] payload = { 0x08, 0x09 };
                Assert.AreEqual(
                    9,
                    Serializer.ProtoDeserialize<DefaultDispatchMarker>(payload).Value
                );
                Assert.AreEqual(
                    9,
                    Serializer
                        .ProtoDeserialize<DefaultDispatchMarker>(
                            payload,
                            typeof(DefaultDispatchMarker)
                        )
                        .Value
                );
                Assert.IsTrue(
                    Serializer.TryProtoDeserialize(payload, out DefaultDispatchMarker restored)
                );
                Assert.AreEqual(9, restored.Value);
                Assert.IsTrue(
                    Serializer.TryProtoDeserialize(
                        payload,
                        typeof(DefaultDispatchMarker),
                        out DefaultDispatchMarker restoredAs
                    )
                );
                Assert.AreEqual(9, restoredAs.Value);
            }
            finally
            {
                WProtoFormatterProvider.Register(original);
            }
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

        private sealed class DefaultDispatchMarker
        {
            internal int Value;
        }

        private sealed class DefaultDispatchMarkerFormatter
            : IWProtoFormatter<DefaultDispatchMarker>
        {
            public int Measure(in DefaultDispatchMarker value)
            {
                return WProtoSizes.TagSize(1) + WProtoSizes.Int32Size(value.Value);
            }

            public bool Write(ref WProtoWriter writer, in DefaultDispatchMarker value)
            {
                return writer.TryWriteTag(1, WProtoWireType.Varint)
                    && writer.TryWriteInt32(value.Value);
            }

            public bool TryRead(ref WProtoReader reader, out DefaultDispatchMarker value)
            {
                value = new DefaultDispatchMarker();
                while (reader.TryReadTag(out int fieldNumber, out int wireType))
                {
                    if (fieldNumber == 1 && reader.TryReadInt32(out int raw))
                    {
                        value.Value = raw;
                    }
                    else if (!reader.TrySkipField(fieldNumber, wireType))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// A generator <see cref="AbstractRandom"/> does not declare, which is the shape a consumer
        /// writing their own PRNG produces.
        /// </summary>
        /// <summary>
        /// A test double for <see cref="AbstractRandom"/>, and never serialized.
        /// </summary>
        /// <remarks>
        /// <c>AbstractRandom</c>'s formatter ends in a guard refusing any runtime type it does not
        /// declare, so an undeclared subclass throws on the first save. This one never reaches
        /// the serializer, and <c>[WProtoNotSerialized]</c> is where that decision is recorded
        /// rather than inferred from the absence of an attribute
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/613">#613</see>).
        /// </remarks>
        [WProtoNotSerialized]
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
