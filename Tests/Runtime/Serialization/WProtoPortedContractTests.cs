// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Math;
    using WallstopStudios.UnityHelpers.Core.OneOf;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Tags;

    /// <summary>
    /// Proves the generator's output for the package's own newly annotated contracts, on every
    /// backend including standalone IL2CPP.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What only this fixture can prove.</b> Wire compatibility is settled elsewhere and faster:
    /// <c>ContractMirrorTests</c> checks that each contract's two attribute sets agree, and
    /// <c>PackageContractShapeTests</c> drives each shape through protobuf-net 3.2.56 in about a
    /// second. Neither runs inside Unity, and neither can -- protobuf-net is the thing that does not
    /// work under IL2CPP, which is the reason WallstopProto exists. What is left for this fixture is
    /// the part that only a player build can answer: that the emitted formatters AOT-compile and
    /// resolve for the real types.
    /// </para>
    /// <para>
    /// <b>The golden bytes are also the drift detector.</b> They were captured from protobuf-net
    /// against the stand-in shapes, so a real contract whose members have quietly diverged from its
    /// stand-in -- a renumbered tag, a member added on one side only -- fails here rather than
    /// passing both of the faster gates and shipping.
    /// </para>
    /// <para>
    /// Nothing registers formatters in a setup, deliberately: everything resolves through
    /// <see cref="WProtoFormatterProvider"/>, so the fixture is also the assertion that the
    /// generated registrar ran. Registering by hand would hide a stripped registrar on the one
    /// backend where stripping happens.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoPortedContractTests
    {
        [Test]
        public void AContractWithNoMembersEncodesToNothing()
        {
            AssertBytesAndValue(default(None), string.Empty);
        }

        [Test]
        public void RangeEncodesAsProtobufNetDoes()
        {
            AssertBytesAndValue(
                new Range<int>(-5, 300, startInclusive: true, endInclusive: false),
                "08FBFFFFFFFFFFFFFFFF0110AC021801"
            );
            AssertBytesAndValue(
                new Range<float>(0.5f, 2.5f, startInclusive: true, endInclusive: true),
                "0D0000003F150000204018012001"
            );
        }

        [Test]
        public void SerializableNullableEncodesAsProtobufNetDoes()
        {
            AssertBytesAndValue(default(SerializableNullable<int>), string.Empty);
            AssertBytesAndValue(new SerializableNullable<int>(7), "08011007");

            // Present-and-zero is the case a bare value cannot express, and the reason the flag is a
            // member of its own rather than inferred from the value.
            AssertBytesAndValue(new SerializableNullable<int>(0), "0801");
        }

        [Test]
        public void AttributeModificationEncodesAsProtobufNetDoes()
        {
            AssertBytesAndValue(default(AttributeModification), string.Empty);
            AssertBytesAndValue(
                new AttributeModification("Health", ModificationAction.Multiplication, -5f),
                "0A064865616C746810011D0000A0C0"
            );
        }

        // Vector2 and Vector3 are not this package's types to annotate, so both of these travel
        // through the surrogates registered for them; the encoding is the surrogate's, exactly.
        [Test]
        public void LineSegmentsEncodeThroughTheirSurrogates()
        {
            AssertBytesAndValue(default(Line2D), "0A001200");
            AssertBytesAndValue(
                new Line2D(new Vector2(1.5f, -2f), new Vector2(0f, 0.25f)),
                "0A0A0D0000C03F15000000C01205150000803E"
            );
            AssertBytesAndValue(default(Line3D), "0A001200");
            AssertBytesAndValue(
                new Line3D(new Vector3(1.5f, -2f, 0.25f), Vector3.zero),
                "0A0F0D0000C03F15000000C01D0000803E1200"
            );
        }

        [Test]
        public void PeriodicEffectDefinitionEncodesAsProtobufNetDoes()
        {
            PeriodicEffectDefinition definition = new PeriodicEffectDefinition
            {
                name = "burn",
                initialDelay = 0.5f,
                interval = 2f,
                maxTicks = 3,
            };
            definition.modifications.Add(
                new AttributeModification("Health", ModificationAction.Multiplication, -5f)
            );

            AssertBytes(
                definition,
                "0A046275726E150000003F1D0000004020032A0F0A064865616C746810011D0000A0C0"
            );
        }

        /// <summary>
        /// A repeated scalar member is written packed, which protobuf-net reads and does not write.
        /// </summary>
        /// <remarks>
        /// The interop claim -- each serializer reads what the other writes -- is measured against
        /// the oracle in the generator suite. What is pinned here is the exact packed output, so a
        /// change to it is a visible diff rather than a silent size change.
        /// </remarks>
        [Test]
        public void ARepeatedScalarMemberIsWrittenPacked()
        {
            SerializableList<int> list = new SerializableList<int> { 1, 2, 300 };

            AssertBytes(list, "0A040102AC02");
        }

        [Test]
        public void EveryPortedCollectionRoundTripsThroughItsGeneratedFormatter()
        {
            SerializableList<int> list = new SerializableList<int> { 1, 2, 300 };
            SerializableList<int> restoredList = RoundTrip(list);
            Assert.AreEqual(list.Count, restoredList.Count);
            for (int index = 0; index < list.Count; index++)
            {
                Assert.AreEqual(list[index], restoredList[index]);
            }

            DisjointSet sets = new DisjointSet(4);
            Assert.IsTrue(sets.TryUnion(0, 1));
            DisjointSet restoredSets = RoundTrip(sets);
            Assert.AreEqual(sets.Count, restoredSets.Count);
            Assert.AreEqual(sets.SetCount, restoredSets.SetCount);
            Assert.IsTrue(restoredSets.TryIsConnected(0, 1, out bool connected));
            Assert.IsTrue(connected);

            // Deliberately set a bit past the initial capacity, so the round trip has to carry a
            // grown backing array rather than the one the constructor sized.
            BitSet bits = new BitSet(64);
            Assert.IsTrue(bits.TrySet(2));
            Assert.IsTrue(bits.TrySet(70));
            BitSet restoredBits = RoundTrip(bits);
            Assert.AreEqual(bits.Count, restoredBits.Count);
            Assert.IsTrue(restoredBits.TryGet(2, out bool second) && second);
            Assert.IsTrue(restoredBits.TryGet(70, out bool seventieth) && seventieth);
            Assert.IsTrue(restoredBits.TryGet(3, out bool third) && !third);
        }

        [Test]
        public void SerializableTypeRoundTripsThroughItsGeneratedFormatter()
        {
            SerializableType wrapped = new SerializableType(typeof(BitSet));

            Assert.AreEqual(wrapped, RoundTrip(wrapped));
            Assert.AreEqual(typeof(BitSet), RoundTrip(wrapped).Value);
            Assert.AreEqual(default(SerializableType), RoundTrip(default(SerializableType)));
        }

        [Test]
        public void AValueCacheRoundTripsThroughItsGeneratedFormatter()
        {
            SerializableDictionary.Cache<int> cache = new SerializableDictionary.Cache<int>
            {
                Data = 7,
            };

            Assert.AreEqual(7, RoundTrip(cache).Data);
        }

        /// <summary>
        /// A generator round-trips to the same stream position, under its own declared type.
        /// </summary>
        /// <remarks>
        /// The seventeen generators are reached through <c>AbstractRandom</c>'s includes, and the
        /// declared type here is the concrete one, which is the case that encodes the include and the
        /// base's members together. Comparing the next drawn values rather than the fields is what
        /// makes this an assertion about the generator's state rather than about its serialization.
        /// </remarks>
        [Test]
        public void AGeneratorResumesItsStreamAfterARoundTrip()
        {
            PcgRandom generator = new PcgRandom();
            for (int index = 0; index < 5; index++)
            {
                generator.NextUint();
            }

            PcgRandom restored = RoundTrip(generator);

            for (int index = 0; index < 16; index++)
            {
                Assert.AreEqual(
                    generator.NextUint(),
                    restored.NextUint(),
                    "the restored generator diverged at draw " + index
                );
            }
        }

        /// <summary>
        /// A generator declared <c>SkipConstructor</c> resumes from the payload, not from a
        /// constructor.
        /// </summary>
        /// <remarks>
        /// <c>DotNetRandom</c>'s parameterless constructor seeds a live generator from a fresh
        /// <c>Guid</c>, and its after-deserialization hook returns early when one already exists --
        /// so a formatter that ran the constructor would hand back a generator on a random stream
        /// with no error to show for it.
        /// </remarks>
        [Test]
        public void AGeneratorThatSkipsItsConstructorResumesFromThePayload()
        {
            DotNetRandom generator = new DotNetRandom();
            for (int index = 0; index < 5; index++)
            {
                generator.NextUint();
            }

            DotNetRandom restored = RoundTrip(generator);

            for (int index = 0; index < 16; index++)
            {
                Assert.AreEqual(
                    generator.NextUint(),
                    restored.NextUint(),
                    "the restored generator diverged at draw " + index
                );
            }
        }

        /// <summary>
        /// Asserts the encoding and that the payload reads back.
        /// </summary>
        /// <typeparam name="T">The contract type.</typeparam>
        /// <param name="value">The value to encode.</param>
        /// <param name="expectedHex">The bytes protobuf-net produces for this shape.</param>
        /// <remarks>
        /// Deliberately does not compare the decoded value to the original: most of these contracts
        /// are reference types with no equality member, so the comparison would be by reference and
        /// would fail on a perfectly correct read. Contracts that define equality use
        /// <see cref="AssertBytesAndValue{T}"/>; the rest have their contents compared field by field
        /// in the round-trip tests above.
        /// </remarks>
        private static void AssertBytes<T>(T value, string expectedHex)
        {
            byte[] encoded = Encode(value);

            Assert.AreEqual(expectedHex, ToHex(encoded), typeof(T).Name);

            WProtoReader reader = new WProtoReader(encoded);
            Assert.IsTrue(
                WProtoFormatterProvider.Get<T>().TryRead(ref reader, out T _),
                typeof(T).Name + " refused its own bytes"
            );
        }

        private static void AssertBytesAndValue<T>(T value, string expectedHex)
        {
            AssertBytes(value, expectedHex);
            Assert.AreEqual(value, RoundTrip(value), typeof(T).Name);
        }

        private static T RoundTrip<T>(T value)
        {
            WProtoReader reader = new WProtoReader(Encode(value));
            Assert.IsTrue(
                WProtoFormatterProvider.Get<T>().TryRead(ref reader, out T restored),
                typeof(T).Name + " refused its own bytes"
            );
            return restored;
        }

        private static byte[] Encode<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);

            Assert.IsTrue(formatter.Write(ref writer, value), typeof(T).Name + " failed to write");

            // Measure has to predict Write exactly: it sizes this buffer and, for a nested value,
            // supplies the length prefix, so a short measure is a truncated payload not an error.
            Assert.AreEqual(
                buffer.Length,
                writer.Position,
                typeof(T).Name + " measured a different length than it wrote"
            );
            return buffer;
        }

        private static string ToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty);
        }
    }
}
