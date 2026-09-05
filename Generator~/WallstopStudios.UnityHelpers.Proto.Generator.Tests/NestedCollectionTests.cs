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
            /*
             * byte[] is a scalar, so byte[][] must retain its existing repeated-scalar encoding without
             * wrappers.
             */
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
            /*
             * Inner wrappers preserve empty collections; top-level repeated fields cannot distinguish empty
             * from absent.
             */
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
            InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() =>
                Encode(Bare(c => c.Rows = new[] { new[] { 1 }, null }))
            );

            Assert.IsTrue(
                refusal.Message.Contains("null 'int[]' element"),
                "unexpected refusal: " + refusal.Message
            );
        }

        [Test]
        public void ANullMapValueIsOmittedRatherThanRefused()
        {
            // Repeated elements cannot encode absence, while a map value can omit its field to preserve null.
            Assert.AreEqual(
                "4A030A016B",
                Encode(Bare(c => c.Lookup = new Dictionary<string, List<int>> { { "k", null } }))
            );

            NestedCollectionContract restored = RoundTrip(
                Bare(c => c.Lookup = new Dictionary<string, List<int>> { { "k", null } })
            );

            Assert.AreEqual(1, restored.Lookup.Count);
            Assert.IsTrue(restored.Lookup["k"] == null, "a null map value came back non-null");
        }

        [Test]
        public void MeasurePredictsWriteExactlyForEveryNestedShape()
        {
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
            // Every wrapper must charge a nesting level or the writer can produce unreadable depth.
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
            /*
             * Immutable outer locals and eager wrapper locals share names but must belong to separate
             * classes.
             */
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
            // Include members defer assignment until the subtype is known; wrapper members remain eager.
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
            byte[] payload = { 0x0A, 0x05, 0x0A, 0x01, 0x01 };

            IWProtoFormatter<NestedCollectionContract> formatter =
                WProtoFormatterProvider.Get<NestedCollectionContract>();
            WProtoReader reader = new WProtoReader(payload);

            Assert.IsFalse(formatter.TryRead(ref reader, out NestedCollectionContract _));
        }

        [Test]
        public void AnAbsentNestedMemberLeavesTheConstructorValueAlone()
        {
            /*
             * Absent outer members preserve constructor seeds, unlike explicitly present empty inner
             * wrappers.
             */
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
