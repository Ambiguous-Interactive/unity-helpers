// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.IO;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins generic contracts against protobuf-net 3.2.56.
    /// </summary>
    /// <remarks>
    /// The property that makes this hard: <b>the field key changes with the closure</b>.
    /// <c>Box&lt;int&gt;.Value</c> is <c>08 01</c>, <c>Box&lt;double&gt;</c> is <c>09 …</c>,
    /// <c>Box&lt;string&gt;</c> is <c>0A …</c>. A generic contract therefore cannot be emitted with a
    /// wire-type constant, and these tests are what prove the deferral produces the same bytes as a
    /// hand-written closure would.
    /// </remarks>
    [TestFixture]
    public sealed class GenericDifferentialTests
    {
        [Test]
        public void TheFieldKeyChangesWithTheClosure()
        {
            // Three closures of ONE contract, three different wire types on the same field number.
            Assert.AreEqual("0801", Encode(new Box<int> { Value = 1 }));
            Assert.AreEqual("09000000000000F03F", Encode(new Box<double> { Value = 1 }));
            Assert.AreEqual("0A0161", Encode(new Box<string> { Value = "a" }));
            Assert.AreEqual(
                "0A020801",
                Encode(new Box<Outer.Point> { Value = new Outer.Point { X = 1 } })
            );
        }

        [Test]
        public void EveryClosureMatchesTheOracleByteForByte()
        {
            AssertMatches(new Box<int>());
            AssertMatches(new Box<int> { Value = 1 });
            AssertMatches(new Box<int> { Value = int.MinValue, Trailer = 2 });
            AssertMatches(new Box<int> { Many = new[] { 1, 0, -1 } });
            AssertMatches(new Box<int> { Many = Array.Empty<int>() });

            AssertMatches(new Box<double>());
            AssertMatches(new Box<double> { Value = -0.5 });
            AssertMatches(new Box<double> { Many = new[] { 0d, 1d } });

            AssertMatches(new Box<string>());
            AssertMatches(new Box<string> { Value = string.Empty });
            AssertMatches(new Box<string> { Value = "é中", Trailer = -1 });
            AssertMatches(new Box<string> { Many = new[] { "a", string.Empty } });

            AssertMatches(new Box<Outer.Point>());
            AssertMatches(
                new Box<Outer.Point>
                {
                    Value = new Outer.Point { X = 1, Y = 2 },
                }
            );
            AssertMatches(
                new Box<Outer.Point>
                {
                    Many = new[]
                    {
                        default,
                        new Outer.Point { X = 3 },
                    },
                }
            );
        }

        [Test]
        public void EveryClosureRoundTrips()
        {
            Box<int> ints = RoundTrip(
                new Box<int>
                {
                    Value = 7,
                    Many = new[] { 1, 2 },
                    Trailer = 3,
                }
            );
            Assert.AreEqual(7, ints.Value);
            CollectionAssert.AreEqual(new[] { 1, 2 }, ints.Many);
            Assert.AreEqual(3, ints.Trailer);

            Box<string> texts = RoundTrip(
                new Box<string> { Value = "a", Many = new[] { "b", string.Empty } }
            );
            Assert.AreEqual("a", texts.Value);
            CollectionAssert.AreEqual(new[] { "b", string.Empty }, texts.Many);

            Box<Outer.Point> points = RoundTrip(
                new Box<Outer.Point> { Value = new Outer.Point { X = 4 } }
            );
            Assert.AreEqual(4, points.Value.X);

            Box<double> doubles = RoundTrip(new Box<double> { Value = 1.5 });
            Assert.AreEqual(1.5, doubles.Value);
        }

        [Test]
        public void EveryClosureNamedInSourceIsRegisteredWithoutAnythingBeingCalled()
        {
            // A registrar cannot register an open generic, so the generator registers the closed
            // constructions it can see in source. This is the property that makes a consumer's
            // `Deque<TheirStruct>` work, and it is why the closures are named in BoxClosures.
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<Box<int>>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<Box<double>>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<Box<string>>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<Box<Outer.Point>>());
        }

        [Test]
        public void AGenericMemberObeysTheOmissionRuleOfItsClosure()
        {
            // Omission is per closed type, not per emitted constant: 0, 0.0 and null are each their
            // own type's default, and an EMPTY string is not.
            Assert.AreEqual(string.Empty, Encode(new Box<int> { Value = 0 }));
            Assert.AreEqual(string.Empty, Encode(new Box<double> { Value = 0 }));
            Assert.AreEqual(string.Empty, Encode(new Box<string> { Value = null }));
            Assert.AreEqual("0A00", Encode(new Box<string> { Value = string.Empty }));
        }

        [Test]
        public void AGenericRepeatedMemberReadsAPackedRun()
        {
            // Tag 2, length-delimited, three varints: the packed spelling of Many = {1, 2, 3}.
            // Neither serializer WRITES this for these closures, and that is exactly why it has to be
            // read: packed is the proto3 default and what every other implementation emits, so a
            // payload from outside this package arrives in this shape.
            byte[] packed = { 0x12, 0x03, 0x01, 0x02, 0x03 };

            // The oracle accepts it, which is what makes dropping it a compatibility defect rather
            // than a policy choice.
            Box<int> oracle = Deserialize<Box<int>>(packed);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, oracle.Many);

            // The non-generic path emits a second case for this and calls the alternative "the worst
            // of the available failures" -- a silently short collection. The generic path gated its
            // only case on the element's native wire type, so the run fell through to TrySkipField.
            IWProtoFormatter<Box<int>> formatter = WProtoFormatterProvider.Get<Box<int>>();
            WProtoReader reader = new WProtoReader(packed);
            Assert.IsTrue(formatter.TryRead(ref reader, out Box<int> restored));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, restored.Many);
        }

        [Test]
        public void AGenericRepeatedMemberReadsPackedAndUnpackedInterleaved()
        {
            // A packed run followed by a loose element, the same mixture the non-generic path is
            // pinned on. The seed runs once across BOTH cases, so a per-case accumulator would drop
            // whichever half came first.
            //
            // The bound is the oracle's, not a guess: protobuf-net REFUSES a second packed run after
            // a loose element (measured -- "Invalid wire-type (String)" at the third group), so this
            // is the widest interleaving that is actually legal, and asserting more would have been
            // asserting a shape no reader accepts.
            byte[] mixed = { 0x12, 0x02, 0x01, 0x02, 0x10, 0x03 };

            Box<int> oracle = Deserialize<Box<int>>(mixed);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, oracle.Many);

            IWProtoFormatter<Box<int>> formatter = WProtoFormatterProvider.Get<Box<int>>();
            WProtoReader reader = new WProtoReader(mixed);
            Assert.IsTrue(formatter.TryRead(ref reader, out Box<int> restored));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, restored.Many);
        }

        private static T Deserialize<T>(byte[] bytes)
        {
            using (MemoryStream stream = new MemoryStream(bytes))
            {
                return ProtoBuf.Serializer.Deserialize<T>(stream);
            }
        }

        private static void AssertMatches<T>(Box<T> value)
        {
            string label = typeof(T).Name + " " + Describe(value);
            Assert.AreEqual(OracleHex(value), Encode(value), label);
        }

        private static string Describe<T>(Box<T> value)
        {
            return "Value="
                + (value.Value == null ? "null" : value.Value.ToString())
                + " Many="
                + (value.Many == null ? "null" : value.Many.Length.ToString())
                + " Trailer="
                + value.Trailer;
        }

        private static string OracleHex<T>(T value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(stream, value);
                return ToHex(stream.ToArray());
            }
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            foreach (byte current in bytes)
            {
                builder.Append(current.ToString("X2"));
            }

            return builder.ToString();
        }

        private static string Encode<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(buffer.Length, writer.Position, "Measure disagreed with Write");
            return ToHex(buffer);
        }

        private static T RoundTrip<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));

            WProtoReader reader = new WProtoReader(buffer);
            Assert.IsTrue(formatter.TryRead(ref reader, out T restored));
            return restored;
        }
    }
}
