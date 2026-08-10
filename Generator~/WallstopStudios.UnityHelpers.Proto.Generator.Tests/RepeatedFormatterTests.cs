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
    /// Pins the repeated-field encoding against protobuf-net 3.2.56.
    /// </summary>
    /// <remarks>
    /// Every expected payload below was produced by serializing an identical contract with the
    /// vendored oracle and copying the bytes out, because four of the rules are the opposite of the
    /// scalar ones this suite already pins: a repeated field is unpacked, every element is written
    /// even when it equals its type's default, null and empty are the same bytes, and a null element
    /// has no encoding at all. Guessing any of them produces a payload that looks plausible and
    /// decodes wrong in a consumer's shipped game.
    /// </remarks>
    [TestFixture]
    public sealed class RepeatedFormatterTests
    {
        private static readonly object[] OracleBytes =
        {
            new object[]
            {
                "Ints {1,0,-1}",
                new RepeatedContract { Ints = new[] { 1, 0, -1 } },
                "0A0C0100FFFFFFFFFFFFFFFFFF01",
            },
            new object[]
            {
                "IntList {2}",
                new RepeatedContract { IntList = new List<int> { 2 } },
                "120102",
            },
            new object[]
            {
                "Texts {a,empty}",
                new RepeatedContract { Texts = new[] { "a", string.Empty } },
                "1A01611A00",
            },
            new object[]
            {
                "Doubles {0,1}",
                new RepeatedContract { Doubles = new[] { 0d, 1d } },
                "22100000000000000000000000000000F03F",
            },
            new object[]
            {
                "Longs {0,1}",
                new RepeatedContract { Longs = new ulong[] { 0, 1 } },
                "2A020001",
            },
            new object[]
            {
                "Flags {false,true}",
                new RepeatedContract { Flags = new[] { false, true } },
                "32020001",
            },
            new object[]
            {
                "Modes {None,Careful}",
                new RepeatedContract { Modes = new[] { Mode.None, Mode.Careful } },
                "3A0300AC02",
            },
            new object[]
            {
                "Points {default,(1,2)}",
                new RepeatedContract
                {
                    Points = new[]
                    {
                        default(Outer.Point),
                        new Outer.Point { X = 1, Y = 2 },
                    },
                },
                "4200420408011002",
            },
            new object[]
            {
                "Messages {empty}",
                new RepeatedContract { Messages = new[] { new EmptyContract() } },
                "4A00",
            },
            new object[]
            {
                "Blobs {empty,{1}}",
                new RepeatedContract { Blobs = new[] { Array.Empty<byte>(), new byte[] { 1 } } },
                "5200520101",
            },
            new object[]
            {
                "PointList {(3,0)}",
                new RepeatedContract
                {
                    PointList = new List<Outer.Point> { new Outer.Point { X = 3 } },
                },
                "5A020803",
            },
            new object[]
            {
                "Shorts {-2,0}",
                new RepeatedContract { Shorts = new short[] { -2, 0 } },
                "620BFEFFFFFFFFFFFFFFFF0100",
            },
            new object[]
            {
                "ascending field order across four members",
                new RepeatedContract
                {
                    Ints = new[] { 1, 2 },
                    IntList = new List<int> { 3 },
                    Texts = new[] { "a" },
                    Shorts = new short[] { -2 },
                },
                "0A0201021201031A0161620AFEFFFFFFFFFFFFFFFF01",
            },
        };

        /// <summary>
        /// Pins the exact bytes each element shape produces, and that protobuf-net reads them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// These are no longer protobuf-net's OWN bytes for the packable shapes. A packable repeated
        /// member is written PACKED here -- proto3's default, one key and one length for the run --
        /// where protobuf-net at CompatibilityLevel 200 writes one key per element. `Texts` is
        /// unchanged, because strings are length-delimited and cannot be packed at all; that
        /// contrast is the point of leaving it in the table.
        /// </para>
        /// <para>
        /// The expectations are literal so a change in encoding is visible as a diff here, and each
        /// one is checked against protobuf-net's decoder in the same test, so a literal that is
        /// simply wrong cannot pass.
        /// </para>
        /// </remarks>
        [TestCaseSource(nameof(OracleBytes))]
        public void EveryElementShapeEncodesAsExpectedAndProtobufNetReadsItBack(
            string label,
            RepeatedContract value,
            string expected
        )
        {
            string mine = Encode(value);
            Assert.AreEqual(expected, mine, label);

            // The half that makes the divergence safe rather than merely intentional.
            RepeatedContract theirs;
            using (MemoryStream stream = new MemoryStream(Parse(mine)))
            {
                theirs = ProtoBuf.Serializer.Deserialize<RepeatedContract>(stream);
            }

            RepeatedContract reference;
            using (MemoryStream stream = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(stream, value);
                stream.Position = 0;
                reference = ProtoBuf.Serializer.Deserialize<RepeatedContract>(stream);
            }

            CollectionAssert.AreEqual(reference.Ints, theirs.Ints, label + " Ints");
            CollectionAssert.AreEqual(reference.IntList, theirs.IntList, label + " IntList");
            CollectionAssert.AreEqual(reference.Texts, theirs.Texts, label + " Texts");
            CollectionAssert.AreEqual(reference.Doubles, theirs.Doubles, label + " Doubles");
            CollectionAssert.AreEqual(reference.Longs, theirs.Longs, label + " Longs");
            CollectionAssert.AreEqual(reference.Flags, theirs.Flags, label + " Flags");
            CollectionAssert.AreEqual(reference.Modes, theirs.Modes, label + " Modes");
            CollectionAssert.AreEqual(reference.Shorts, theirs.Shorts, label + " Shorts");
        }

        [Test]
        public void AnElementEqualToItsDefaultIsStillWritten()
        {
            // The scalar rule reversed. A member holding 0 is omitted; an element holding 0 is not,
            // because dropping it would shorten the collection rather than restore a default.
            // Packed: one key, a length of 1, then the bare zero. The element is still WRITTEN,
            // which is the rule this test exists for -- dropping it would shorten the collection.
            Assert.AreEqual("0A0100", Encode(new RepeatedContract { Ints = new[] { 0 } }));
            Assert.AreEqual("320100", Encode(new RepeatedContract { Flags = new[] { false } }));
            Assert.AreEqual(
                "4200",
                Encode(new RepeatedContract { Points = new[] { default(Outer.Point) } })
            );
        }

        [Test]
        public void NullAndEmptyCollectionsAreTheSameBytes()
        {
            Assert.AreEqual(string.Empty, Encode(new RepeatedContract()));
            Assert.AreEqual(
                string.Empty,
                Encode(
                    new RepeatedContract
                    {
                        Ints = Array.Empty<int>(),
                        IntList = new List<int>(),
                        Texts = Array.Empty<string>(),
                        Doubles = Array.Empty<double>(),
                        Longs = Array.Empty<ulong>(),
                        Flags = Array.Empty<bool>(),
                        Modes = Array.Empty<Mode>(),
                        Points = Array.Empty<Outer.Point>(),
                        Messages = Array.Empty<EmptyContract>(),
                        Blobs = Array.Empty<byte[]>(),
                        PointList = new List<Outer.Point>(),
                        Shorts = Array.Empty<short>(),
                    }
                )
            );
        }

        [Test]
        public void AnEmptyCollectionDoesNotSurviveARoundTrip()
        {
            // A silent data change, reproduced deliberately: there is no encoding that separates an
            // empty repeated field from an absent one, so an empty collection with no constructor
            // value behind it comes back null. protobuf-net does the same (measured).
            RepeatedContract restored = RoundTrip(
                new RepeatedContract { Ints = Array.Empty<int>(), IntList = new List<int>() }
            );

            Assert.IsNull(restored.Ints);
            Assert.IsNull(restored.IntList);
        }

        [Test]
        public void EveryRepeatedShapeRoundTrips()
        {
            RepeatedContract original = new RepeatedContract
            {
                Ints = new[] { 1, 0, -1, int.MinValue },
                IntList = new List<int> { 2, 3 },
                Texts = new[] { "a", string.Empty, "é中" },
                Doubles = new[] { 0d, 1d, double.NaN },
                Longs = new ulong[] { 0, ulong.MaxValue },
                Flags = new[] { false, true },
                Modes = new[] { Mode.None, Mode.Careful },
                Points = new[]
                {
                    default(Outer.Point),
                    new Outer.Point { X = 1, Y = 2 },
                },
                Messages = new[] { new EmptyContract(), new EmptyContract() },
                Blobs = new[] { Array.Empty<byte>(), new byte[] { 1, 255 } },
                PointList = new List<Outer.Point> { new Outer.Point { X = 3 } },
                Shorts = new short[] { -2, 0, short.MaxValue },
            };

            RepeatedContract restored = RoundTrip(original);

            CollectionAssert.AreEqual(original.Ints, restored.Ints);
            CollectionAssert.AreEqual(original.IntList, restored.IntList);
            CollectionAssert.AreEqual(original.Texts, restored.Texts);
            CollectionAssert.AreEqual(original.Doubles, restored.Doubles);
            CollectionAssert.AreEqual(original.Longs, restored.Longs);
            CollectionAssert.AreEqual(original.Flags, restored.Flags);
            CollectionAssert.AreEqual(original.Modes, restored.Modes);
            CollectionAssert.AreEqual(original.Points, restored.Points);
            Assert.AreEqual(original.Messages.Length, restored.Messages.Length);
            CollectionAssert.AreEqual(original.Blobs[0], restored.Blobs[0]);
            CollectionAssert.AreEqual(original.Blobs[1], restored.Blobs[1]);
            CollectionAssert.AreEqual(original.PointList, restored.PointList);
            CollectionAssert.AreEqual(original.Shorts, restored.Shorts);
        }

        [Test]
        public void AStructContractCarriesARepeatedMember()
        {
            Assert.AreEqual(
                "0A0201021003",
                Encode(new RepeatedStructContract { Ints = new[] { 1, 2 }, Marker = 3 })
            );

            RepeatedStructContract restored = RoundTrip(
                new RepeatedStructContract { Ints = new[] { 1, 2 }, Marker = 3 }
            );
            CollectionAssert.AreEqual(new[] { 1, 2 }, restored.Ints);
            Assert.AreEqual(3, restored.Marker);
        }

        [Test]
        public void ConstructorSeededCollectionsAreWrittenLikeAnyOther()
        {
            Assert.AreEqual(
                "0A020708120207081A02070822020708",
                Encode(new SeededRepeatedContract())
            );
        }

        [Test]
        public void ReadingAppendsToTheConstructorsCollectionUnlessOverwriteListIsSet()
        {
            // Measured, both halves: a member initialized to {7,8} that receives {1} holds {7,8,1}
            // by default and {1} under OverwriteList. Getting this backwards duplicates a game's
            // default inventory on every load, or silently discards it.
            SeededRepeatedContract present = Decode<SeededRepeatedContract>("0801100118012001");

            CollectionAssert.AreEqual(new[] { 7, 8, 1 }, present.AppendedList);
            CollectionAssert.AreEqual(new[] { 1 }, present.OverwrittenList);
            CollectionAssert.AreEqual(new[] { 7, 8, 1 }, present.AppendedArray);
            CollectionAssert.AreEqual(new[] { 1 }, present.OverwrittenArray);
        }

        [Test]
        public void AnAbsentRepeatedFieldLeavesTheConstructorsCollectionAlone()
        {
            // Including under OverwriteList: "absent" and "empty" are the same bytes, so there is
            // nothing for the overwrite to be triggered by.
            SeededRepeatedContract absent = Decode<SeededRepeatedContract>("2801");

            CollectionAssert.AreEqual(new[] { 7, 8 }, absent.AppendedList);
            CollectionAssert.AreEqual(new[] { 7, 8 }, absent.OverwrittenList);
            CollectionAssert.AreEqual(new[] { 7, 8 }, absent.AppendedArray);
            CollectionAssert.AreEqual(new[] { 7, 8 }, absent.OverwrittenArray);
            Assert.AreEqual(1, absent.Marker);
        }

        [Test]
        public void APackedPayloadDecodesIntoAMemberThisPackageWritesUnpacked()
        {
            // protobuf-net accepts both forms for the same member and interleaves them freely
            // (measured). A reader that knew only the form it writes would treat the field as
            // unrecognized, skip it, and hand back a short collection with no error anywhere.
            CollectionAssert.AreEqual(
                new[] { 1, 2, 300 },
                Decode<RepeatedContract>("0A040102AC02").Ints
            );
            CollectionAssert.AreEqual(
                new[] { 1, 2, 3 },
                Decode<RepeatedContract>("0A0201020803").Ints
            );
            CollectionAssert.AreEqual(
                new[] { 1d },
                Decode<RepeatedContract>("2208000000000000F03F").Doubles
            );
            CollectionAssert.AreEqual(
                new short[] { -2 },
                Decode<RepeatedContract>("620AFEFFFFFFFFFFFFFFFF01").Shorts
            );
        }

        [Test]
        public void APresentButEmptyPackedRunProducesAnEmptyCollectionRatherThanNothing()
        {
            // The one shape that CAN distinguish empty from absent, because the length prefix is on
            // the wire. protobuf-net returns an empty collection here, not null (measured).
            int[] decoded = Decode<RepeatedContract>("0A00").Ints;

            Assert.IsNotNull(decoded);
            Assert.AreEqual(0, decoded.Length);
        }

        [Test]
        public void APackedRunDoesNotSpendANestingLevel()
        {
            // A packed run holds primitives with no field keys, so it cannot recurse and must not be
            // charged for nesting. Charging it would refuse an array at the bottom of an otherwise
            // legal message that protobuf-net accepts.
            byte[] payload = { 0x0A, 0x02, 0x01, 0x02 };
            WProtoReader outer = new WProtoReader(payload);
            Assert.IsTrue(outer.TryReadTag(out int fieldNumber, out int wireType));
            Assert.AreEqual(1, fieldNumber);
            Assert.AreEqual(WProtoWireType.LengthDelimited, wireType);
            Assert.IsTrue(outer.TryReadPackedRun(out WProtoReader packed));
            Assert.AreEqual(outer.Depth, packed.Depth);
        }

        [Test]
        public void ATruncatedRepeatedElementIsReportedRatherThanTruncatingTheCollection()
        {
            IWProtoFormatter<RepeatedContract> formatter =
                WProtoFormatterProvider.Get<RepeatedContract>();

            // A field key with no value behind it, and a packed run whose last varint runs off the
            // end of its own length.
            foreach (string payload in new[] { "080108", "0A02018F" })
            {
                WProtoReader reader = new WProtoReader(Parse(payload));
                Assert.IsFalse(formatter.TryRead(ref reader, out RepeatedContract value), payload);
                Assert.IsNull(value, payload);
            }
        }

        [Test]
        public void ANullElementIsRefusedByNameRatherThanEncodedAsSomethingElse()
        {
            // Writing it would either invent an empty value -- a null string element would encode
            // identically to "" -- or drop the element and shorten the collection. protobuf-net
            // raises on the same input.
            AssertRefusesNullElement(new RepeatedContract { Texts = new[] { "a", null } }, "Texts");
            AssertRefusesNullElement(
                new RepeatedContract { Messages = new EmptyContract[] { null } },
                "Messages"
            );
            AssertRefusesNullElement(
                new RepeatedContract { Blobs = new byte[][] { null } },
                "Blobs"
            );
        }

        [Test]
        public void MeasurePredictsWriteExactlyForEveryRepeatedShape()
        {
            // The buffer a parent allocates comes from Measure, so a repeated member whose
            // measurement disagrees with its output corrupts every message that contains it.
            foreach (object[] testCase in OracleBytes)
            {
                RepeatedContract value = (RepeatedContract)testCase[1];
                IWProtoFormatter<RepeatedContract> formatter =
                    WProtoFormatterProvider.Get<RepeatedContract>();
                int predicted = formatter.Measure(value);
                byte[] buffer = new byte[predicted];
                WProtoWriter writer = new WProtoWriter(buffer);
                Assert.IsTrue(formatter.Write(ref writer, value), (string)testCase[0]);
                Assert.AreEqual(predicted, writer.Position, (string)testCase[0]);
            }
        }

        [Test]
        public void AWriteThatRunsOutOfBufferMidCollectionReportsFailure()
        {
            RepeatedContract value = new RepeatedContract { Ints = new[] { 1, 2, 3 } };
            IWProtoFormatter<RepeatedContract> formatter =
                WProtoFormatterProvider.Get<RepeatedContract>();

            byte[] buffer = new byte[formatter.Measure(value) - 1];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsFalse(formatter.Write(ref writer, value));
        }

        private static void AssertRefusesNullElement(RepeatedContract value, string member)
        {
            IWProtoFormatter<RepeatedContract> formatter =
                WProtoFormatterProvider.Get<RepeatedContract>();

            InvalidOperationException measured = Assert.Throws<InvalidOperationException>(() =>
                formatter.Measure(value)
            );
            StringAssert.Contains("RepeatedContract." + member, measured.Message);

            // Write guards independently of Measure: the interface is public, and a hand-written
            // caller is free to call one without the other. Written as a try/catch because a
            // `ref` local cannot be captured by the lambda Assert.Throws wants.
            bool wroteWithoutRefusing = false;
            try
            {
                WProtoWriter writer = new WProtoWriter(new byte[64]);
                formatter.Write(ref writer, value);
                wroteWithoutRefusing = true;
            }
            catch (InvalidOperationException)
            {
                // Expected.
            }

            Assert.IsFalse(
                wroteWithoutRefusing,
                "Write must refuse a null element on its own, not only through Measure"
            );
        }

        private static byte[] Parse(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            return bytes;
        }

        private static T Decode<T>(string hex)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            WProtoReader reader = new WProtoReader(Parse(hex));
            Assert.IsTrue(formatter.TryRead(ref reader, out T value), hex);
            return value;
        }

        private static string Encode<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));

            StringBuilder builder = new StringBuilder(writer.Position * 2);
            foreach (byte current in writer.Written)
            {
                builder.Append(current.ToString("X2"));
            }

            return builder.ToString();
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

        [Test]
        public void PackableRepeatedMembersAreWrittenPackedAndAreSmallerForIt()
        {
            // The deliberate divergence from protobuf-net's OUTPUT, pinned so it cannot regress
            // silently in either direction. Unpacked pays a field key per element; packed pays one
            // key and one length for the whole run.
            int[] many = new int[100];
            for (int index = 0; index < many.Length; index++)
            {
                many[index] = index;
            }

            byte[] mine = Parse(Encode(new RepeatedContract { Ints = many }));

            byte[] theirs;
            using (MemoryStream stream = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(stream, new RepeatedContract { Ints = many });
                theirs = stream.ToArray();
            }

            Assert.Less(
                mine.Length,
                theirs.Length,
                "packed must be smaller, or there is no reason to diverge"
            );

            // Roughly half, and asserted as a bound rather than an exact ratio so a varint-width
            // change in the corpus does not make this brittle.
            Assert.Less(mine.Length, theirs.Length * 0.6, "expected close to a halving");

            // One field key for the whole member: 0x0A is tag 1, length-delimited.
            Assert.AreEqual(0x0A, mine[0]);

            // And protobuf-net still reads it, which is what makes the divergence safe.
            RepeatedContract decoded;
            using (MemoryStream stream = new MemoryStream(mine))
            {
                decoded = ProtoBuf.Serializer.Deserialize<RepeatedContract>(stream);
            }

            CollectionAssert.AreEqual(many, decoded.Ints);
        }

        [Test]
        public void AStringRunIsNeverPackedBecauseItCannotBe()
        {
            // The boundary of the change. A length-delimited element carries its own length, so a
            // packed run of them could not be parsed at all -- protobuf-net writes one key per
            // string and so must this package.
            string mine = Encode(new RepeatedContract { Texts = new[] { "a", "b" } });

            Assert.AreEqual("1A01611A0162", mine);
        }

        [Test]
        public void AGeneratedPackedRunStillWritesAtTheNestingBound()
        {
            // The mirror of APackedRunDoesNotSpendANestingLevel on the read side, and the version
            // that actually discriminates. Asserting the depth returns to zero proves nothing here:
            // open and close are symmetric, so it balances whether or not the run was charged. What
            // separates the two is writing one at the BOUND -- if the generator charges a level, the
            // open is refused and a deep-but-legal message becomes decodable and not encodable.
            RepeatedContract value = new RepeatedContract { Ints = new[] { 1, 2, 3 } };
            IWProtoFormatter<RepeatedContract> formatter =
                WProtoFormatterProvider.Get<RepeatedContract>();

            byte[] buffer = new byte[4096];
            WProtoWriter writer = new WProtoWriter(buffer);

            // Sit exactly AT the bound. One level below is not enough to discriminate: a charged
            // open at depth 63 still passes the `>= 64` test and only fails at 64.
            WProtoLengthToken[] open = new WProtoLengthToken[WProtoReader.MaxNestingDepth];
            for (int level = 0; level < open.Length; level++)
            {
                Assert.IsTrue(
                    writer.TryBeginLengthDelimited(1, true, out open[level]),
                    "level " + level
                );
            }

            Assert.AreEqual(WProtoReader.MaxNestingDepth, writer.Depth);
            Assert.IsTrue(
                formatter.Write(ref writer, value),
                "a packed run must not need a nesting level it cannot have"
            );

            for (int level = open.Length - 1; level >= 0; level--)
            {
                Assert.IsTrue(writer.TryCloseLengthDelimited(open[level]), "close " + level);
            }

            Assert.AreEqual(0, writer.Depth);
        }

        [Test]
        public void OpeningAPackedRunDoesNotConsumeTheNestingBudget()
        {
            // Asserted on the writer directly, because the formatter above never gets near the
            // bound: what matters is that opening the run does not move Depth at all, where opening
            // a sub-message does.
            byte[] buffer = new byte[64];
            WProtoWriter writer = new WProtoWriter(buffer);

            Assert.IsTrue(writer.TryBeginLengthDelimited(1, false, out WProtoLengthToken packed));
            Assert.AreEqual(0, writer.Depth, "a packed run must not charge the nesting bound");
            Assert.IsTrue(writer.TryWriteInt32(7));
            Assert.IsTrue(writer.TryCloseLengthDelimited(packed));
            Assert.AreEqual(0, writer.Depth);

            Assert.IsTrue(writer.TryBeginLengthDelimited(2, true, out WProtoLengthToken message));
            Assert.AreEqual(1, writer.Depth, "a sub-message must still charge it");
            Assert.IsTrue(writer.TryCloseLengthDelimited(message));
            Assert.AreEqual(0, writer.Depth);
        }
    }
}
