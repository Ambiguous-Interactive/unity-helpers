// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Pins the wrapper-message encoding that lets one collection hold another (#399), inside Unity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every hex constant here is the output the generator suite measured for the mirrored
    /// <c>NestedCollectionContract</c> under <c>Generator~/</c>, at the same field numbers. These
    /// shapes have no oracle at all -- protobuf-net refuses every one of them on both 2.4.9 and
    /// 3.2.56 -- so the golden vectors are the only thing standing between a generated formatter and
    /// a silent encoding change, and the IL2CPP standalone legs are the only place this code is
    /// AOT-compiled.
    /// </para>
    /// <para>
    /// <c>0A 04 0A 02 01 02</c> reads as: field 1, length-delimited, four bytes, holding field 1,
    /// length-delimited, two bytes, holding the packed run <c>01 02</c>.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoNestedCollectionTests
    {
        [Test]
        public void EveryNestedShapeMatchesItsGoldenBytes()
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

            // Unpacked inside the wrapper, because a string is length-delimited and a packed run of
            // length-delimited values could not be parsed. The same rule a top-level repeated string
            // gets, one level down.
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
                                new WProtoNestedPoint { X = 1, Y = 2 },
                            },
                        }
                    )
                ),
                "WProtoNestedPoint[][]"
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

            // Unchanged by this capability, and asserted here so it stays that way: a byte[] is one
            // length-delimited value, so byte[][] never was a nested collection.
            Assert.AreEqual(
                "52020102520103",
                Encode(Bare(c => c.Blobs = new[] { new byte[] { 1, 2 }, new byte[] { 3 } })),
                "byte[][]"
            );
        }

        [Test]
        public void EveryNestedShapeRoundTrips()
        {
            WProtoNestedCollectionContract original = new WProtoNestedCollectionContract
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
                        new WProtoNestedPoint { X = 1, Y = 2 },
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

            WProtoNestedCollectionContract restored = RoundTrip(original);

            Assert.AreEqual(2, restored.Rows.Length);
            CollectionAssert.AreEqual(new[] { 1, 2 }, restored.Rows[0]);
            CollectionAssert.AreEqual(new[] { 3 }, restored.Rows[1]);
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
            Assert.AreEqual(2, restored.Tables[0].ValueFor("b"));
            CollectionAssert.AreEqual(new[] { 11, 12 }, restored.Lookup.ValueFor("k"));
            CollectionAssert.AreEqual(new byte[] { 13 }, restored.Blobs[0]);
        }

        [Test]
        public void AnEmptyInnerCollectionSurvivesARoundTripWhileAnEmptyOuterOneDoesNot()
        {
            // The asymmetry worth stating, because it looks like an inconsistency and is not. A
            // top-level repeated field that is absent and one that is empty are the same bytes, so
            // an empty outer collection cannot come back. An inner one can: its wrapper message is
            // present on the wire and says so.
            Assert.AreEqual("0A00", Encode(Bare(c => c.Rows = new[] { Array.Empty<int>() })));
            Assert.AreEqual(string.Empty, Encode(Bare(c => c.Rows = Array.Empty<int[]>())));

            WProtoNestedCollectionContract restored = RoundTrip(
                Bare(c => c.Rows = new[] { new[] { 1 }, Array.Empty<int>(), new[] { 2 } })
            );

            Assert.AreEqual(3, restored.Rows.Length);
            Assert.IsTrue(restored.Rows[1] != null, "an empty inner collection came back null");
            Assert.AreEqual(0, restored.Rows[1].Length);
            CollectionAssert.AreEqual(new[] { 2 }, restored.Rows[2]);

            Assert.IsTrue(RoundTrip(Bare(c => c.Rows = Array.Empty<int[]>())).Rows == null);
        }

        [Test]
        public void AnEmptyInnerCollectionOfEveryFormComesBackEmpty()
        {
            WProtoNestedCollectionContract restored = RoundTrip(
                new WProtoNestedCollectionContract
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
            Assert.AreEqual(0, restored.Lookup.ValueFor("k").Count);
        }

        [Test]
        public void ANullInnerCollectionIsRefusedRatherThanInvented()
        {
            // The rule a null element of any repeated member gets, one level down. The message names
            // the type rather than a member, because one wrapper serves every member holding it.
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
            // A different rule from the one above rather than an exception to it. A repeated element
            // has nowhere to put an absence; a map entry has a field it can leave out, so a null
            // value is omitted and reads back null, exactly as a null message value does.
            Assert.AreEqual(
                "4A030A016B",
                Encode(Bare(c => c.Lookup = new Dictionary<string, List<int>> { { "k", null } }))
            );

            WProtoNestedCollectionContract restored = RoundTrip(
                Bare(c => c.Lookup = new Dictionary<string, List<int>> { { "k", null } })
            );

            Assert.AreEqual(1, restored.Lookup.Count);
            Assert.IsTrue(
                restored.Lookup.ValueFor("k") == null,
                "a null map value came back non-null"
            );
        }

        [Test]
        public void AWrapperCarryingAnUnknownFieldIsSteppedOverRatherThanRefused()
        {
            // Forward compatibility one level down: a wrapper is a real message, so a payload from a
            // later build that adds a field to it has to be skipped exactly.
            WProtoNestedCollectionContract restored = Decode("0A050A01011007");

            CollectionAssert.AreEqual(new[] { 1 }, restored.Rows[0]);
        }

        [Test]
        public void ATruncatedWrapperIsReportedRatherThanReturnedShort()
        {
            // The wrapper claims five payload bytes and three follow. A short inner collection is a
            // plausible-looking save file, which is why it has to be reported rather than returned.
            WProtoReader reader = new WProtoReader(Parse("0A050A0101"));

            Assert.IsFalse(
                WProtoFormatterProvider
                    .Get<WProtoNestedCollectionContract>()
                    .TryRead(ref reader, out WProtoNestedCollectionContract _)
            );
        }

        [Test]
        public void MeasurePredictsWriteExactlyForEveryNestedShape()
        {
            // A wrapper's Measure and Write are two separate emitted methods over one member, and
            // the length prefix one produces is consumed by the other -- a disagreement corrupts
            // every message containing it, silently. Encode asserts it for each case.
            WProtoNestedCollectionContract[] cases =
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
                Bare(c => c.Shapes = new[] { Array.Empty<WProtoNestedPoint>() }),
                Bare(c => c.Lookup = new Dictionary<string, List<int>> { { "k", null } }),
                new WProtoNestedCollectionContract(),
            };

            foreach (WProtoNestedCollectionContract value in cases)
            {
                Encode(value);
            }
        }

        [Test]
        public void AWrapperSpendsANestingLevelAndGivesItBack()
        {
            // Load-bearing for the generator's depth bound, which is the reader's own: a wrapper
            // level is a real sub-message, so a chain deeper than the reader accepts would be
            // writable and unreadable. That is only true if a wrapper actually charges a level.
            WProtoNestedCollectionContract value = Bare(c =>
                c.Cube = new[] { new[] { new[] { 1 } } }
            );

            IWProtoFormatter<WProtoNestedCollectionContract> formatter =
                WProtoFormatterProvider.Get<WProtoNestedCollectionContract>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(0, writer.Depth, "a wrapper left the nesting depth unbalanced");
        }

        [Test]
        public void AnAbsentNestedMemberLeavesTheConstructorValueAlone()
        {
            WProtoNestedCollectionContract restored = RoundTrip(
                new WProtoNestedCollectionContract()
            );

            Assert.IsTrue(restored.Rows == null);
            Assert.IsTrue(restored.Grid == null);
            Assert.IsTrue(restored.Lookup == null);
        }

        private static WProtoNestedCollectionContract Bare(
            Action<WProtoNestedCollectionContract> set
        )
        {
            WProtoNestedCollectionContract value = new WProtoNestedCollectionContract();
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

        private static WProtoNestedCollectionContract Decode(string hex)
        {
            WProtoReader reader = new WProtoReader(Parse(hex));
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<WProtoNestedCollectionContract>()
                    .TryRead(ref reader, out WProtoNestedCollectionContract value),
                hex
            );
            return value;
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

        private static byte[] Parse(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            return bytes;
        }
    }
}
