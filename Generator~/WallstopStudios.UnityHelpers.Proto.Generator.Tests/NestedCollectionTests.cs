// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins the wrapper-message encoding that lets one collection hold another (#399).
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the only bytes in this package with no oracle behind them, and
    /// <see cref="TheOracleRefusesEveryNestedShape"/> is why: protobuf-net cannot produce a payload
    /// of this shape at all, so there is nothing to be byte-compatible with and the encoding was
    /// chosen instead of measured. It is the one every other protobuf implementation would emit for
    /// the equivalent schema -- <c>message Wrapper { repeated T values = 1; }</c> -- so the bytes
    /// map back onto a concrete proto3 definition rather than onto a private convention.
    /// </para>
    /// <para>
    /// The hex below was produced by this implementation and read against that schema by hand, then
    /// pinned. <c>0A 04 0A 02 01 02</c> is field 1, length-delimited, four bytes, holding field 1,
    /// length-delimited, two bytes, holding the packed run <c>01 02</c>.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class NestedCollectionTests
    {
        [Test]
        public void TheOracleRefusesEveryNestedShape()
        {
            // The premise the whole encoding rests on, kept as a measurement rather than a claim in
            // a comment. If a protobuf-net version ever starts writing these, this test goes red and
            // the question of matching its bytes reopens -- which is exactly when it should.
            OracleNestedContract value = new OracleNestedContract { Rows = new[] { new[] { 1 } } };

            using MemoryStream stream = new MemoryStream();
            NotSupportedException refusal = Assert.Throws<NotSupportedException>(() =>
                ProtoBuf.Serializer.Serialize(stream, value)
            );

            Assert.IsTrue(
                refusal.Message.Contains("Nested or jagged"),
                "protobuf-net refused for an unexpected reason: " + refusal.Message
            );
        }

        [Test]
        public void EveryNestedShapeMatchesItsWrapperEncoding()
        {
            Assert.AreEqual(
                "0A040A0201020A030A0103",
                Encode(Bare(c => c.Rows = new[] { new[] { 1, 2 }, new[] { 3 } })),
                "int[][]"
            );
            Assert.AreEqual(
                "12040A020102",
                Encode(Bare(c => c.Batches = new List<int[]> { new[] { 1, 2 } })),
                "List<int[]>"
            );
            Assert.AreEqual(
                "1A040A020102",
                Encode(
                    Bare(c =>
                        c.Grid = new List<List<int>>
                        {
                            new List<int> { 1, 2 },
                        }
                    )
                ),
                "List<List<int>>"
            );

            // The inner run here is UNPACKED -- one key per element inside the wrapper -- because a
            // string is length-delimited and a packed run of length-delimited values could not be
            // parsed. Same rule as a top-level repeated string, applied one level down.
            Assert.AreEqual(
                "22060A01610A0162",
                Encode(Bare(c => c.Names = new[] { new[] { "a", "b" } })),
                "string[][]"
            );
            Assert.AreEqual(
                "2A0B0A030A01010A040A020203",
                Encode(Bare(c => c.Cube = new[] { new[] { new[] { 1 }, new[] { 2, 3 } } })),
                "int[][][]"
            );
            Assert.AreEqual(
                "32030A0101",
                Encode(Bare(c => c.Sets = new[] { new HashSet<int> { 1 } })),
                "HashSet<int>[]"
            );
            Assert.AreEqual(
                "3A060A0408011002",
                Encode(
                    Bare(c =>
                        c.Shapes = new[]
                        {
                            new[]
                            {
                                new Outer.Point { X = 1, Y = 2 },
                            },
                        }
                    )
                ),
                "Point[][]"
            );
            Assert.AreEqual(
                "42070A050A01611001",
                Encode(
                    Bare(c =>
                        c.Tables = new List<Dictionary<string, int>>
                        {
                            new Dictionary<string, int> { { "a", 1 } },
                        }
                    )
                ),
                "List<Dictionary<string, int>>"
            );
            Assert.AreEqual(
                "4A090A016112040A020102",
                Encode(
                    Bare(c =>
                        c.Lookup = new Dictionary<string, List<int>>
                        {
                            {
                                "a",
                                new List<int> { 1, 2 }
                            },
                        }
                    )
                ),
                "Dictionary<string, List<int>>"
            );
        }

        [Test]
        public void AByteArrayOfArraysIsStillAnOrdinaryRepeatedMember()
        {
            // The one jagged-looking shape that must NOT gain a wrapper. `byte[]` is a scalar rather
            // than a repeated field, so `byte[][]` was always an ordinary repeated member -- and its
            // bytes are data that already exists. Asserted against the contract that predates this
            // capability rather than against a literal, so the two cannot drift apart.
            byte[][] blobs = { new byte[] { 1, 2 }, new byte[] { 3 } };

            Assert.AreEqual(
                Encode(new RepeatedContract { Blobs = blobs }),
                Encode(Bare(c => c.Blobs = blobs))
            );
            Assert.AreEqual("52020102520103", Encode(Bare(c => c.Blobs = blobs)));
        }

        [Test]
        public void EveryNestedShapeRoundTrips()
        {
            NestedCollectionContract original = new NestedCollectionContract
            {
                Rows = new[] { new[] { 1, 2 }, new[] { 3 } },
                Batches = new List<int[]> { new[] { 4 }, new[] { 5, 6 } },
                Grid = new List<List<int>>
                {
                    new List<int> { 7, 8 },
                },
                Names = new[] { new[] { "a", "b" }, new[] { "c" } },
                Cube = new[] { new[] { new[] { 1 }, new[] { 2, 3 } }, new[] { new[] { 4 } } },
                Sets = new[]
                {
                    new HashSet<int> { 9, 10 },
                },
                Shapes = new[]
                {
                    new[]
                    {
                        new Outer.Point { X = 1, Y = 2 },
                    },
                },
                Tables = new List<Dictionary<string, int>>
                {
                    new Dictionary<string, int> { { "a", 1 }, { "b", 2 } },
                },
                Lookup = new Dictionary<string, List<int>>
                {
                    {
                        "k",
                        new List<int> { 11, 12 }
                    },
                },
                Blobs = new[] { new byte[] { 13 } },
            };

            NestedCollectionContract restored = RoundTrip(original);

            Assert.AreEqual(2, restored.Rows.Length);
            CollectionAssert.AreEqual(new[] { 1, 2 }, restored.Rows[0]);
            CollectionAssert.AreEqual(new[] { 3 }, restored.Rows[1]);
            Assert.AreEqual(2, restored.Batches.Count);
            CollectionAssert.AreEqual(new[] { 4 }, restored.Batches[0]);
            CollectionAssert.AreEqual(new[] { 5, 6 }, restored.Batches[1]);
            CollectionAssert.AreEqual(new[] { 7, 8 }, restored.Grid[0]);
            CollectionAssert.AreEqual(new[] { "a", "b" }, restored.Names[0]);
            CollectionAssert.AreEqual(new[] { "c" }, restored.Names[1]);
            CollectionAssert.AreEqual(new[] { 2, 3 }, restored.Cube[0][1]);
            CollectionAssert.AreEqual(new[] { 4 }, restored.Cube[1][0]);
            CollectionAssert.AreEquivalent(new[] { 9, 10 }, restored.Sets[0]);
            Assert.AreEqual(1, restored.Shapes[0][0].X);
            Assert.AreEqual(2, restored.Shapes[0][0].Y);
            Assert.AreEqual(2, restored.Tables[0].Count);
            Assert.AreEqual(2, restored.Tables[0]["b"]);
            CollectionAssert.AreEqual(new[] { 11, 12 }, restored.Lookup["k"]);
            CollectionAssert.AreEqual(new byte[] { 13 }, restored.Blobs[0]);
        }

        [Test]
        public void AnEmptyInnerCollectionSurvivesARoundTripWhileAnEmptyOuterOneDoesNot()
        {
            // The asymmetry worth stating, because it looks like an inconsistency and is not. A
            // top-level repeated field that is ABSENT and one that is EMPTY are the same bytes, so
            // an empty outer collection cannot come back. An inner one can: its wrapper message is
            // present on the wire and says so, which is strictly more information.
            Assert.AreEqual("0A00", Encode(Bare(c => c.Rows = new[] { Array.Empty<int>() })));
            Assert.AreEqual(string.Empty, Encode(Bare(c => c.Rows = Array.Empty<int[]>())));

            NestedCollectionContract restored = RoundTrip(
                Bare(c => c.Rows = new[] { new[] { 1 }, Array.Empty<int>(), new[] { 2 } })
            );

            Assert.AreEqual(3, restored.Rows.Length);
            CollectionAssert.AreEqual(new[] { 1 }, restored.Rows[0]);
            Assert.IsTrue(restored.Rows[1] != null, "an empty inner collection came back null");
            Assert.AreEqual(0, restored.Rows[1].Length);
            CollectionAssert.AreEqual(new[] { 2 }, restored.Rows[2]);

            Assert.IsTrue(RoundTrip(Bare(c => c.Rows = Array.Empty<int[]>())).Rows == null);
        }

        [Test]
        public void AnEmptyInnerCollectionOfEveryFormComesBackEmpty()
        {
            NestedCollectionContract restored = RoundTrip(
                new NestedCollectionContract
                {
                    Batches = new List<int[]> { Array.Empty<int>() },
                    Grid = new List<List<int>> { new List<int>() },
                    Names = new[] { Array.Empty<string>() },
                    Sets = new[] { new HashSet<int>() },
                    Tables = new List<Dictionary<string, int>> { new Dictionary<string, int>() },
                    Lookup = new Dictionary<string, List<int>> { { "k", new List<int>() } },
                }
            );

            Assert.AreEqual(0, restored.Batches[0].Length);
            Assert.AreEqual(0, restored.Grid[0].Count);
            Assert.AreEqual(0, restored.Names[0].Length);
            Assert.AreEqual(0, restored.Sets[0].Count);
            Assert.AreEqual(0, restored.Tables[0].Count);
            Assert.AreEqual(0, restored.Lookup["k"].Count);
        }

        [Test]
        public void ANullInnerCollectionIsRefusedRatherThanInvented()
        {
            // The same rule a null element of any repeated member gets, one level down: there is no
            // encoding for an absent value inside a run. The message names the TYPE rather than a
            // member, because one wrapper serves every member of the contract holding that type.
            InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() =>
                Encode(Bare(c => c.Rows = new[] { new[] { 1 }, null }))
            );

            Assert.IsTrue(
                refusal.Message.Contains("null 'int[]' element"),
                "unexpected refusal: " + refusal.Message
            );
        }

        [Test]
        public void MeasurePredictsWriteExactlyForEveryNestedShape()
        {
            // Encode asserts it for each of these; the point of running them as a table is that a
            // wrapper's Measure and Write are two separate emitted methods over the same member, and
            // a length prefix produced by one and consumed by the other is what corrupts silently.
            NestedCollectionContract[] cases =
            {
                Bare(c => c.Rows = new[] { Array.Empty<int>() }),
                Bare(c => c.Rows = new[] { new[] { int.MinValue, int.MaxValue } }),
                Bare(c => c.Names = new[] { new[] { string.Empty, "é" } }),
                Bare(c => c.Cube = new[] { new[] { Array.Empty<int>() } }),
                Bare(c =>
                    c.Sets = new[]
                    {
                        new HashSet<int>(),
                        new HashSet<int> { 0 },
                    }
                ),
                Bare(c => c.Shapes = new[] { Array.Empty<Outer.Point>() }),
                Bare(c =>
                    c.Tables = new List<Dictionary<string, int>> { new Dictionary<string, int>() }
                ),
                Bare(c => c.Lookup = new Dictionary<string, List<int>> { { "k", null } }),
                new NestedCollectionContract(),
            };

            foreach (NestedCollectionContract value in cases)
            {
                Encode(value);
            }
        }

        [Test]
        public void AWrapperSpendsANestingLevelAndGivesItBack()
        {
            // Load-bearing for the generator's depth bound, which is the reader's own: every wrapper
            // level is a real sub-message, so a chain deeper than the reader accepts would be
            // writable and unreadable. That is only true if a wrapper actually charges a level.
            NestedCollectionContract value = Bare(c => c.Cube = new[] { new[] { new[] { 1 } } });

            IWProtoFormatter<NestedCollectionContract> formatter =
                WProtoFormatterProvider.Get<NestedCollectionContract>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(0, writer.Depth, "a wrapper left the nesting depth unbalanced");
        }

        [Test]
        public void AnImmutableContractRoundTripsItsNestedCollections()
        {
            // A readonly member switches the whole contract to construct-at-end, which holds every
            // decoded member in a local named `value<tag>` -- the same spelling a wrapper's own
            // member uses. The two live in different classes, and this is what says so.
            ImmutableNestedContract restored = RoundTrip(
                new ImmutableNestedContract(
                    new[] { new[] { 1, 2 } },
                    new Dictionary<string, List<int>>
                    {
                        {
                            "k",
                            new List<int> { 3 }
                        },
                    }
                )
            );

            CollectionAssert.AreEqual(new[] { 1, 2 }, restored.Rows[0]);
            CollectionAssert.AreEqual(new[] { 3 }, restored.Lookup["k"]);
        }

        [Test]
        public void APolymorphicContractRoundTripsNestedCollectionsOnBothHalves()
        {
            // Every member of a contract declaring [WProtoInclude] is DEFERRED: collected aside and
            // committed once an include tag has settled which instance they land on. A wrapper's own
            // member is never deferred, so this is where a deferred outer and an eager inner meet.
            NestedIncludeSubtype original = new NestedIncludeSubtype
            {
                Rows = new[] { new[] { 1 } },
                Extra = new List<List<string>>
                {
                    new List<string> { "a", "b" },
                },
            };

            NestedIncludeBase restored = RoundTrip<NestedIncludeBase>(original);
            NestedIncludeSubtype subtype = restored as NestedIncludeSubtype;

            Assert.IsTrue(subtype != null, "the include did not produce the subtype");
            CollectionAssert.AreEqual(new[] { 1 }, subtype.Rows[0]);
            CollectionAssert.AreEqual(new[] { "a", "b" }, subtype.Extra[0]);
        }

        [Test]
        public void AWrapperCarryingAnUnknownFieldIsSteppedOverRatherThanRefused()
        {
            // Forward compatibility one level down. A wrapper is a real message, so a payload from a
            // later build that adds a second field to it has to be skipped exactly -- the same
            // guarantee a contract gives, for the same reason.
            //
            // Field 1: a wrapper holding the run {1} at field 1, and an unknown varint at field 2.
            byte[] payload = { 0x0A, 0x05, 0x0A, 0x01, 0x01, 0x10, 0x07 };

            IWProtoFormatter<NestedCollectionContract> formatter =
                WProtoFormatterProvider.Get<NestedCollectionContract>();
            WProtoReader reader = new WProtoReader(payload);

            Assert.IsTrue(formatter.TryRead(ref reader, out NestedCollectionContract restored));
            CollectionAssert.AreEqual(new[] { 1 }, restored.Rows[0]);
        }

        [Test]
        public void ATruncatedWrapperIsReportedRatherThanReturnedShort()
        {
            // The wrapper claims five payload bytes and three follow. Reporting it matters more here
            // than for a flat member: a short inner collection is a plausible-looking save file.
            byte[] payload = { 0x0A, 0x05, 0x0A, 0x01, 0x01 };

            IWProtoFormatter<NestedCollectionContract> formatter =
                WProtoFormatterProvider.Get<NestedCollectionContract>();
            WProtoReader reader = new WProtoReader(payload);

            Assert.IsFalse(formatter.TryRead(ref reader, out NestedCollectionContract _));
        }

        [Test]
        public void AnAbsentNestedMemberLeavesTheConstructorValueAlone()
        {
            // Absent and empty are the same bytes at the top level, so an absent member must not
            // replace what the constructor left. Unchanged by this capability, and asserted because
            // the wrapper's read deliberately does the opposite one level down.
            NestedCollectionContract restored = RoundTrip(new NestedCollectionContract());

            Assert.IsTrue(restored.Rows == null);
            Assert.IsTrue(restored.Grid == null);
            Assert.IsTrue(restored.Lookup == null);
        }

        private static NestedCollectionContract Bare(Action<NestedCollectionContract> set)
        {
            NestedCollectionContract value = new NestedCollectionContract();
            set(value);
            return value;
        }

        private static string Encode<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(buffer.Length, writer.Position, "Measure disagreed with Write");
            return BitConverter.ToString(buffer).Replace("-", string.Empty);
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
