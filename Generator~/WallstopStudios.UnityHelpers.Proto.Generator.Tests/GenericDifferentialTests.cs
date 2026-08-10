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
