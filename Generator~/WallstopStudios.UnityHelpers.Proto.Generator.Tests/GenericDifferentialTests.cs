// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
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
        [OneTimeSetUp]
        public void RegisterBclFormatters()
        {
            WProtoBcl.RegisterAll();
        }

        [Test]
        public void TheFieldKeyChangesWithTheClosure()
        {
            // Three closures of ONE contract, three different wire types on the same field number.
            Assert.AreEqual("0801", Encode(new Box<int> { Value = 1 }));
            Assert.AreEqual("09000000000000F03F", Encode(new Box<double> { Value = 1 }));
            Assert.AreEqual("0A0161", Encode(new Box<string> { Value = "a" }));
            Assert.AreEqual("08E901", Encode(new Box<char> { Value = 'é' }));
            Assert.AreEqual(
                "0A020801",
                Encode(new Box<Outer.Point> { Value = new Outer.Point { X = 1 } })
            );
        }

        [Test]
        public void EveryClosureRoundTripsThroughProtobufNet()
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

            // A code unit closure: the default omits, and a repeated Many writes each element under
            // its own key because the oracle never packs chars.
            AssertMatches(new Box<char>());
            AssertMatches(new Box<char> { Value = 'é' });
            AssertMatches(new Box<char> { Value = '\0', Trailer = 4 });
            AssertMatches(new Box<char> { Many = new[] { 'A', '\0', '\uFFFF' } });

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

            AssertMatches(
                new Box<DateTime>
                {
                    Value = new DateTime(2026, 8, 26, 12, 34, 56, DateTimeKind.Utc),
                    Many = new[] { DateTime.MinValue, DateTime.MaxValue },
                }
            );
            AssertMatches(
                new Box<TimeSpan>
                {
                    Value = TimeSpan.FromHours(-3.25),
                    Many = new[] { TimeSpan.Zero, TimeSpan.MinValue },
                }
            );
            AssertMatches(
                new Box<Guid>
                {
                    Value = new Guid("12345678-1234-1234-1234-123456789abc"),
                    Many = new[] { Guid.Empty, new Guid("ffffffff-eeee-dddd-cccc-bbbb99998888") },
                }
            );
            AssertMatches(
                new Box<decimal>
                {
                    Value = -1234567.891m,
                    Many = new[] { 0m, decimal.Negate(decimal.Zero), decimal.MaxValue },
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
        public void EveryNewCollectionShapeRoundTripsAtEveryClosure()
        {
            // Two runtime decisions intersect here: whether the element packs is a property of the
            // closure, and how the collection is filled is a property of the declared type. A packed
            // branch that assumed List.Add, or a fill method that assumed a packable element, would
            // pass one of these closures and fail the other.
            CollectionBox<int> ints = RoundTrip(
                new CollectionBox<int>
                {
                    Queued = new Queue<int>(new[] { 1, 2 }),
                    Stacked = new Stack<int>(new[] { 3, 4 }),
                    Listed = new List<int> { 5 },
                    Enumerated = new List<int> { 6, 7 },
                    Trailer = 8,
                }
            );
            CollectionAssert.AreEqual(new[] { 1, 2 }, ints.Queued);
            CollectionAssert.AreEqual(new[] { 4, 3 }, ints.Stacked);
            CollectionAssert.AreEqual(new[] { 5 }, ints.Listed);
            CollectionAssert.AreEqual(new[] { 6, 7 }, ints.Enumerated);
            Assert.AreEqual(8, ints.Trailer);

            CollectionBox<string> texts = RoundTrip(
                new CollectionBox<string>
                {
                    Queued = new Queue<string>(new[] { "a", string.Empty }),
                    Stacked = new Stack<string>(new[] { "b", "c" }),
                    Listed = new List<string> { "d" },
                    Enumerated = new List<string> { "e" },
                }
            );
            CollectionAssert.AreEqual(new[] { "a", string.Empty }, texts.Queued);
            CollectionAssert.AreEqual(new[] { "c", "b" }, texts.Stacked);
            CollectionAssert.AreEqual(new[] { "d" }, texts.Listed);
            CollectionAssert.AreEqual(new[] { "e" }, texts.Enumerated);
        }

        [Test]
        public void AnEmptyGenericCollectionWritesNothingAtEveryClosure()
        {
            // The count-free IEnumerable path opens its packed run from the first element, so an
            // empty one has to leave no key behind -- at the packable closure, where the run exists,
            // and at the length-delimited closure, where it does not.
            Assert.AreEqual(0, Measure(new CollectionBox<int>()));
            Assert.AreEqual(0, Measure(new CollectionBox<string>()));
            Assert.AreEqual(
                0,
                Measure(
                    new CollectionBox<int>
                    {
                        Queued = new Queue<int>(),
                        Stacked = new Stack<int>(),
                        Listed = new List<int>(),
                        Enumerated = new List<int>(),
                    }
                )
            );
            Assert.AreEqual(
                0,
                Measure(
                    new CollectionBox<string>
                    {
                        Queued = new Queue<string>(),
                        Stacked = new Stack<string>(),
                        Listed = new List<string>(),
                        Enumerated = new List<string>(),
                    }
                )
            );
        }

        private static int Measure<T>(T value)
        {
            return WProtoFormatterProvider.Get<T>().Measure(value);
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
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<Box<DateTime>>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<Box<TimeSpan>>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<Box<Guid>>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<Box<decimal>>());
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
            Assert.AreEqual("0A040801100F", Encode(new Box<DateTime>()));
            Assert.AreEqual(string.Empty, Encode(new Box<TimeSpan>()));
            Assert.AreEqual(string.Empty, Encode(new Box<Guid>()));
            Assert.AreEqual(string.Empty, Encode(new Box<decimal>()));
        }

        [Test]
        public void AGenericBclMemberTakesTheLastOccurrence()
        {
            byte[] payload = ParseHex("0A0208020A021003");
            Box<DateTime> oracle = Deserialize<Box<DateTime>>(payload);

            WProtoReader reader = new WProtoReader(payload);
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<Box<DateTime>>()
                    .TryRead(ref reader, out Box<DateTime> restored)
            );
            Assert.AreEqual(new DateTime(WProtoBcl.EpochTicks), oracle.Value);
            Assert.AreEqual(oracle.Value, restored.Value);
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

        [Test]
        public void ARequiredGenericMemberObeysTheOmissionRuleOfItsClosure()
        {
            // What "required" means depends on the closure, so it cannot be decided when the generic
            // contract is emitted: a required int at 0 IS written, while a required null string is
            // still absent. Both expectations are the oracle's, asserted below rather than asserted
            // from memory.
            Assert.AreEqual(
                OracleHex(new RequiredBox<int> { Value = 0 }),
                Encode(new RequiredBox<int> { Value = 0 })
            );
            Assert.AreEqual(
                OracleHex(new RequiredBox<string> { Value = null }),
                Encode(new RequiredBox<string> { Value = null })
            );
            Assert.AreEqual(
                OracleHex(new RequiredBox<string> { Value = string.Empty }),
                Encode(new RequiredBox<string> { Value = string.Empty })
            );

            // Spelled out, so a change to the oracle's behaviour is visible as a change here and not
            // absorbed silently by comparing two things that moved together.
            Assert.AreEqual("0800", Encode(new RequiredBox<int> { Value = 0 }));
            Assert.AreEqual(string.Empty, Encode(new RequiredBox<string> { Value = null }));
        }

        [Test]
        public void ARegisteredBclOverrideServesRootsGeneratedMembersAndGenericClosures()
        {
            IWProtoFormatter<DateTime> original = WProtoFormatterProvider.Get<DateTime>();
            CountingDateTimeFormatter custom = new CountingDateTimeFormatter(original);
            DateTime expected = new DateTime(2026, 8, 26, 12, 34, 56, DateTimeKind.Utc);

            try
            {
                WProtoFormatterProvider.Register(custom);

                custom.Reset();
                Assert.IsTrue(WProtoFacade.TrySerialize(expected, out byte[] rootBytes));
                Assert.IsTrue(WProtoFacade.TryDeserialize(rootBytes, out DateTime root));
                Assert.AreEqual(expected, root);
                AssertUsed(custom, "root");

                custom.Reset();
                byte[] memberBytes = ParseHex(Encode(new BclScalarContract { When = expected }));
                WProtoReader memberReader = new WProtoReader(memberBytes);
                Assert.IsTrue(
                    WProtoFormatterProvider
                        .Get<BclScalarContract>()
                        .TryRead(ref memberReader, out BclScalarContract member)
                );
                Assert.AreEqual(expected, member.When);
                AssertUsed(custom, "generated member");

                custom.Reset();
                byte[] genericBytes = ParseHex(Encode(new Box<DateTime> { Value = expected }));
                WProtoReader genericReader = new WProtoReader(genericBytes);
                Assert.IsTrue(
                    WProtoFormatterProvider
                        .Get<Box<DateTime>>()
                        .TryRead(ref genericReader, out Box<DateTime> generic)
                );
                Assert.AreEqual(expected, generic.Value);
                AssertUsed(custom, "generic closure");

                WProtoFormatterProvider.Register<DateTime>(null);
                Assert.IsFalse(WProtoFacade.TrySerialize(expected, out byte[] _));

                custom.Enabled = false;
                WProtoFormatterProvider.Register(custom);
                Assert.IsFalse(WProtoFacade.TrySerialize(expected, out byte[] _));
            }
            finally
            {
                WProtoFormatterProvider.Register(original);
            }
        }

        private static T Deserialize<T>(byte[] bytes)
        {
            using (MemoryStream stream = new MemoryStream(bytes))
            {
                return ProtoBuf.Serializer.Deserialize<T>(stream);
            }
        }

        /// <summary>
        /// Asserts protobuf-net decodes this closure's bytes to the values it would have produced.
        /// </summary>
        /// <remarks>
        /// Not byte equality: <c>Many</c> is a repeated member, and a closure whose element packs is
        /// written PACKED here where protobuf-net writes one key per element. Which closures pack is
        /// decided at runtime by <c>WProtoGeneric&lt;T&gt;.Packable</c> -- <c>Box&lt;int&gt;</c> does,
        /// <c>Box&lt;string&gt;</c> cannot -- so this covers both branches of that decision.
        /// </remarks>
        private static void AssertMatches<T>(Box<T> value)
        {
            string label = typeof(T).Name + " " + Describe(value);
            byte[] mine = ParseHex(Encode(value));

            Box<T> theirs = Deserialize<Box<T>>(mine);
            Box<T> reference = Deserialize<Box<T>>(ParseHex(OracleHex(value)));

            Assert.AreEqual(OracleHex(reference), OracleHex(theirs), label);
        }

        private static byte[] ParseHex(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            return bytes;
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

        private static void AssertUsed(CountingDateTimeFormatter formatter, string path)
        {
            Assert.Greater(formatter.MeasureCount, 0, path + " measure");
            Assert.Greater(formatter.WriteCount, 0, path + " write");
            Assert.Greater(formatter.ReadCount, 0, path + " read");
        }

        private sealed class CountingDateTimeFormatter
            : IWProtoFormatter<DateTime>,
                IWProtoConditionalFormatter
        {
            private readonly IWProtoFormatter<DateTime> _inner;

            internal int MeasureCount { get; private set; }
            internal int WriteCount { get; private set; }
            internal int ReadCount { get; private set; }
            internal bool Enabled { get; set; } = true;

            internal CountingDateTimeFormatter(IWProtoFormatter<DateTime> inner)
            {
                _inner = inner;
            }

            public int Measure(in DateTime value)
            {
                MeasureCount++;
                return _inner.Measure(value);
            }

            public bool Write(ref WProtoWriter writer, in DateTime value)
            {
                WriteCount++;
                return _inner.Write(ref writer, value);
            }

            public bool TryRead(ref WProtoReader reader, out DateTime value)
            {
                bool read = _inner.TryRead(ref reader, out DateTime decoded);
                ReadCount++;
                value = decoded;
                return read;
            }

            public bool CanServe()
            {
                return Enabled;
            }

            internal void Reset()
            {
                MeasureCount = 0;
                WriteCount = 0;
                ReadCount = 0;
            }
        }
    }
}
