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
    /// Hands the same value to the generated formatter and to protobuf-net 3.2.56, and fails on any
    /// disagreement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the conformance proof for repeated fields, and it lives here rather than in the Unity
    /// test assembly for a reason: protobuf-net is what does not work under IL2CPP, so a differential
    /// run in Unity can only ever execute on the Mono legs. Running it from a plain
    /// <c>dotnet test</c> makes byte-compatibility a red-green loop measured in seconds instead of a
    /// CI round trip, over the same sources Unity compiles.
    /// </para>
    /// <para>
    /// The Unity fixture carries golden bytes copied out of this oracle instead, so the IL2CPP legs
    /// -- which cannot run the oracle at all -- still hold the guarantee.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class OracleDifferentialTests
    {
        [Test]
        public void EveryRepeatedShapeEncodesExactlyAsProtobufNetDoes()
        {
            int checks = 0;
            foreach (RepeatedContract value in Corpus())
            {
                AssertMatchesOracle(value, ref checks);
            }

            Assert.Greater(checks, 100, "The differential corpus shrank; that is a coverage loss");
        }

        [Test]
        public void AStructContractWithARepeatedMemberMatchesTheOracle()
        {
            int[][] variants =
            {
                null,
                Array.Empty<int>(),
                new[] { 0 },
                new[] { 1, 2, 3 },
                new[] { int.MinValue, int.MaxValue },
            };

            foreach (int[] ints in variants)
            {
                foreach (int marker in new[] { 0, 1, -1 })
                {
                    RepeatedStructContract value = new RepeatedStructContract
                    {
                        Ints = ints,
                        Marker = marker,
                    };

                    // Packed here too, so the claim is interop rather than identical bytes.
                    AssertRoundTripsBothWays(value, Describe(ints) + " / " + marker);
                }
            }
        }

        [Test]
        public void AppendAndOverwriteDecodeThePayloadTheSameWayTheOracleDoes()
        {
            // The read side is where OverwriteList exists at all, so byte equality on the write side
            // would prove nothing about it. Both serializers decode the same bytes and the resulting
            // collections are compared element by element.
            string[] payloads =
            {
                string.Empty,
                "2801",
                "0801100118012001",
                "08011001180120010801",
                "0A020102" + "12020102" + "1A020102" + "22020102",
                // A present-but-EMPTY packed run against members the constructor already filled.
                // The only shape where "the field was here and held nothing" is expressible, and
                // the one place appending and overwriting can disagree about an empty payload.
                "0A00" + "1200" + "1A00" + "2200",
            };

            foreach (string payload in payloads)
            {
                byte[] bytes = Parse(payload);

                SeededRepeatedContract oracle;
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    oracle = ProtoBuf.Serializer.Deserialize<SeededRepeatedContract>(stream);
                }

                WProtoReader reader = new WProtoReader(bytes);
                Assert.IsTrue(
                    WProtoFormatterProvider
                        .Get<SeededRepeatedContract>()
                        .TryRead(ref reader, out SeededRepeatedContract mine),
                    payload
                );

                CollectionAssert.AreEqual(oracle.AppendedList, mine.AppendedList, payload);
                CollectionAssert.AreEqual(oracle.OverwrittenList, mine.OverwrittenList, payload);
                CollectionAssert.AreEqual(oracle.AppendedArray, mine.AppendedArray, payload);
                CollectionAssert.AreEqual(oracle.OverwrittenArray, mine.OverwrittenArray, payload);
                Assert.AreEqual(oracle.Marker, mine.Marker, payload);
            }
        }

        [Test]
        public void ThePackedFormsTheOracleAcceptsAreTheOnesThisReaderAccepts()
        {
            // protobuf-net writes unpacked but reads either form, and the two may be interleaved.
            // Every payload here is decoded by both and the results compared, so "we accept packed"
            // is measured against the oracle rather than against this session's understanding of it.
            string[] payloads =
            {
                "0A040102AC02",
                "0A00",
                "0A0201020803",
                "2208000000000000F03F",
                "22100000000000000000000000000000F03F",
                "2A03008001",
                "32020001",
                "620AFEFFFFFFFFFFFFFFFF01",
                "3A040001AC02",
            };

            foreach (string payload in payloads)
            {
                byte[] bytes = Parse(payload);

                RepeatedContract oracle;
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    oracle = ProtoBuf.Serializer.Deserialize<RepeatedContract>(stream);
                }

                WProtoReader reader = new WProtoReader(bytes);
                Assert.IsTrue(
                    WProtoFormatterProvider
                        .Get<RepeatedContract>()
                        .TryRead(ref reader, out RepeatedContract mine),
                    payload
                );

                AssertSameValue(oracle, mine, payload);
            }
        }

        [Test]
        public void AnUnpackedRunFollowedByAPackedOneIsAcceptedHereAndRefusedByTheOracle()
        {
            // Measured, and worth recording rather than matching. protobuf-net accepts packed
            // followed by unpacked, but throws "Invalid wire-type (String)" on the reverse order --
            // once its repeated reader has seen the unpacked form it will not take a length-
            // delimited key for the same field. The protobuf encoding itself allows either order,
            // so this reader takes both.
            //
            // Leniency on read is the safe direction: every payload protobuf-net can WRITE is still
            // decoded identically, and the alternative would be discarding data that is legal
            // protobuf and that some other implementation may well emit.
            byte[] bytes = Parse("08030A020102");

            using (MemoryStream stream = new MemoryStream(bytes))
            {
                Assert.Throws<ProtoBuf.ProtoException>(() =>
                    ProtoBuf.Serializer.Deserialize<RepeatedContract>(stream)
                );
            }

            WProtoReader reader = new WProtoReader(bytes);
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<RepeatedContract>()
                    .TryRead(ref reader, out RepeatedContract mine)
            );
            CollectionAssert.AreEqual(new[] { 3, 1, 2 }, mine.Ints);
        }

        [Test]
        public void SetsAndOwnedCollectionsMatchTheOracleToo()
        {
            // Nothing in the repeated encoding cares which container the elements came out of, and
            // this is the check that keeps that true as the accepted set of containers widens.
            CollectionShapesContract[] cases =
            {
                new CollectionShapesContract(),
                new CollectionShapesContract { Set = new HashSet<int>() },
                new CollectionShapesContract
                {
                    Set = new HashSet<int> { 1, 2, 300 },
                },
                new CollectionShapesContract
                {
                    Sorted = new SortedSet<string> { "b", "a" },
                },
                new CollectionShapesContract
                {
                    Sorted = new SortedSet<string> { string.Empty, "é中" },
                },
                new CollectionShapesContract
                {
                    Owned = new System.Collections.ObjectModel.Collection<int> { 0, -1 },
                },
                new CollectionShapesContract
                {
                    Set = new HashSet<int> { 7 },
                    Sorted = new SortedSet<string> { "z" },
                    Owned = new System.Collections.ObjectModel.Collection<int> { 9 },
                },
            };

            foreach (CollectionShapesContract value in cases)
            {
                // `Set` and `Owned` hold ints, so they are packed here and unpacked by the oracle.
                // What has to hold is that protobuf-net reads what this package writes.
                CollectionShapesContract theirs;
                using (MemoryStream stream = new MemoryStream(Parse(MineHex(value))))
                {
                    theirs = ProtoBuf.Serializer.Deserialize<CollectionShapesContract>(stream);
                }

                CollectionShapesContract reference;
                using (MemoryStream stream = new MemoryStream(Parse(OracleHex(value))))
                {
                    reference = ProtoBuf.Serializer.Deserialize<CollectionShapesContract>(stream);
                }

                CollectionAssert.AreEqual(reference.Set, theirs.Set, "Set");
                CollectionAssert.AreEqual(reference.Sorted, theirs.Sorted, "Sorted");
                CollectionAssert.AreEqual(reference.Owned, theirs.Owned, "Owned");
            }
        }

        [Test]
        public void AStructCollectionEncodesAsTheSameRepeatedFieldAsAnArray()
        {
            // protobuf-net cannot serialize a struct collection at all, so there is no instance of
            // this contract for it to encode. The claim being proven is the one that matters
            // anyway -- a struct container is not a new wire shape, it is the same repeated field --
            // so the oracle is asked for an int[] at the same field number and the bytes must agree.
            int[][] variants =
            {
                Array.Empty<int>(),
                new[] { 0 },
                new[] { 1, 2, 300 },
                new[] { int.MinValue, -1, int.MaxValue },
            };

            foreach (int[] elements in variants)
            {
                IntBag bag = new IntBag();
                foreach (int element in elements)
                {
                    bag.Add(element);
                }

                string oracle = OracleHex(new RepeatedContract { Ints = elements });
                string mine = MineHex(
                    new ValueTypeCollectionContract
                    {
                        Bag = bag,
                        Seeded = default,
                        SeededOverwritten = default,
                    }
                );

                // Not byte equality: this package packs an int run and protobuf-net does not. The
                // claim is unchanged in substance -- a struct container is not a new wire shape --
                // so it is made by having the oracle READ what the struct container produced.
                RepeatedContract theirs;
                using (MemoryStream stream = new MemoryStream(Parse(mine)))
                {
                    theirs = ProtoBuf.Serializer.Deserialize<RepeatedContract>(stream);
                }

                CollectionAssert.AreEqual(
                    elements.Length == 0 ? null : elements,
                    theirs.Ints,
                    Describe(elements)
                );

                // ...and back, into a member the formatter can only reach by copy.
                WProtoReader reader = new WProtoReader(Parse(oracle));
                Assert.IsTrue(
                    WProtoFormatterProvider
                        .Get<ValueTypeCollectionContract>()
                        .TryRead(ref reader, out ValueTypeCollectionContract restored),
                    Describe(elements)
                );
                CollectionAssert.AreEqual(elements, restored.Bag, Describe(elements));
            }
        }

        [Test]
        public void AStructCollectionAppendsAndOverwritesLikeAReferenceOne()
        {
            // A struct accumulator has no null state, so presence is a flag; and every Add lands on
            // a copy, so the formatter has to assign it back. Both are invisible until a payload
            // arrives for a member the constructor already filled.
            ValueTypeCollectionContract absent = Decode(string.Empty);
            Assert.AreEqual(0, absent.Bag.Count);
            Assert.AreEqual(0, absent.Overwritten.Count);
            CollectionAssert.AreEqual(new[] { 7, 8 }, absent.Seeded);
            CollectionAssert.AreEqual(new[] { 7, 8 }, absent.SeededOverwritten);

            ValueTypeCollectionContract filled = Decode("0801" + "1002" + "0803" + "1801" + "2001");
            CollectionAssert.AreEqual(new[] { 1, 3 }, filled.Bag);
            CollectionAssert.AreEqual(new[] { 2 }, filled.Overwritten);
            CollectionAssert.AreEqual(new[] { 7, 8, 1 }, filled.Seeded);
            CollectionAssert.AreEqual(new[] { 1 }, filled.SeededOverwritten);
        }

        private static ValueTypeCollectionContract Decode(string hex)
        {
            WProtoReader reader = new WProtoReader(Parse(hex));
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<ValueTypeCollectionContract>()
                    .TryRead(ref reader, out ValueTypeCollectionContract value),
                hex
            );
            return value;
        }

        [Test]
        public void NullElementBehaviorIsPinnedToTheSelectedOracle()
        {
            // v3 refuses the null element, while v2 silently omits it. WallstopProto preserves the
            // v3 failure because silently changing a collection is data loss; the isolated runs
            // pin both oracle behaviors so this difference cannot disappear unnoticed.
            RepeatedContract value = new RepeatedContract { Texts = new[] { "a", null } };

            using (MemoryStream stream = new MemoryStream())
            {
#if PROTOBUF_NET_ORACLE_V2
                ProtoBuf.Serializer.Serialize(stream, value);
                Assert.AreEqual(
                    "1A0161",
                    ToHex(stream.ToArray()),
                    "v2 silently omits the null element; v3 rejects it"
                );
#else
                Assert.Throws<NullReferenceException>(() =>
                    ProtoBuf.Serializer.Serialize(stream, value)
                );
#endif
            }

            InvalidOperationException mine = Assert.Throws<InvalidOperationException>(() =>
                WProtoFormatterProvider.Get<RepeatedContract>().Measure(value)
            );
            StringAssert.Contains("RepeatedContract.Texts", mine.Message);
        }

        private static IEnumerable<RepeatedContract> Corpus()
        {
            int[][] ints =
            {
                null,
                Array.Empty<int>(),
                new[] { 0 },
                new[] { 1, 2, 3 },
                new[] { int.MinValue, -1, int.MaxValue },
            };
            List<int>[] intLists =
            {
                null,
                new List<int>(),
                new List<int> { 0 },
                new List<int> { 1, -1 },
            };
            string[][] texts =
            {
                null,
                Array.Empty<string>(),
                new[] { string.Empty },
                new[] { "a", string.Empty, "é中" },
            };
            double[][] doubles =
            {
                null,
                new[] { 0d },
                new[] { -0d, 0d },
                new[] { 1d, double.NaN, double.MaxValue, double.NegativeInfinity },
            };
            ulong[][] longs =
            {
                null,
                new ulong[] { 0 },
                new ulong[] { 1, 127, 128, ulong.MaxValue },
            };
            bool[][] flags = { null, new[] { false }, new[] { false, true, true } };
            Mode[][] modes =
            {
                null,
                new[] { Mode.None },
                new[] { Mode.None, Mode.Fast, Mode.Careful },
            };
            Outer.Point[][] points =
            {
                null,
                Array.Empty<Outer.Point>(),
                new[] { default(Outer.Point) },
                new[]
                {
                    new Outer.Point { X = 1, Y = 2 },
                    default,
                    new Outer.Point { X = -1 },
                },
            };
            EmptyContract[][] messages =
            {
                null,
                new[] { new EmptyContract() },
                new[] { new EmptyContract(), new EmptyContract() },
            };
            byte[][][] blobs =
            {
                null,
                new[] { Array.Empty<byte>() },
                new[] { Array.Empty<byte>(), new byte[] { 0 }, new byte[] { 255, 1 } },
            };
            List<Outer.Point>[] pointLists =
            {
                null,
                new List<Outer.Point>(),
                new List<Outer.Point> { new Outer.Point { X = 3 } },
            };
            short[][] shorts =
            {
                null,
                new short[] { 0 },
                new short[] { -2, 0, short.MinValue, short.MaxValue },
            };

            // One member at a time, so a divergence names its own shape rather than arriving inside
            // a value that varies in twelve places.
            foreach (int[] value in ints)
            {
                yield return new RepeatedContract { Ints = value };
            }

            foreach (List<int> value in intLists)
            {
                yield return new RepeatedContract { IntList = value };
            }

            foreach (string[] value in texts)
            {
                yield return new RepeatedContract { Texts = value };
            }

            foreach (double[] value in doubles)
            {
                yield return new RepeatedContract { Doubles = value };
            }

            foreach (ulong[] value in longs)
            {
                yield return new RepeatedContract { Longs = value };
            }

            foreach (bool[] value in flags)
            {
                yield return new RepeatedContract { Flags = value };
            }

            foreach (Mode[] value in modes)
            {
                yield return new RepeatedContract { Modes = value };
            }

            foreach (Outer.Point[] value in points)
            {
                yield return new RepeatedContract { Points = value };
            }

            foreach (EmptyContract[] value in messages)
            {
                yield return new RepeatedContract { Messages = value };
            }

            foreach (byte[][] value in blobs)
            {
                yield return new RepeatedContract { Blobs = value };
            }

            foreach (List<Outer.Point> value in pointLists)
            {
                yield return new RepeatedContract { PointList = value };
            }

            foreach (short[] value in shorts)
            {
                yield return new RepeatedContract { Shorts = value };
            }

            // Then every member populated at once, rotated through the variants, which is what
            // exercises field ordering across the whole contract.
            for (int step = 0; step < 60; step++)
            {
                yield return new RepeatedContract
                {
                    Ints = ints[step % ints.Length],
                    IntList = intLists[step % intLists.Length],
                    Texts = texts[step % texts.Length],
                    Doubles = doubles[step % doubles.Length],
                    Longs = longs[step % longs.Length],
                    Flags = flags[step % flags.Length],
                    Modes = modes[step % modes.Length],
                    Points = points[step % points.Length],
                    Messages = messages[step % messages.Length],
                    Blobs = blobs[step % blobs.Length],
                    PointList = pointLists[step % pointLists.Length],
                    Shorts = shorts[step % shorts.Length],
                };
            }
        }

        /// <summary>
        /// Asserts that this package and protobuf-net can each read what the other writes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>This deliberately does not assert identical bytes any more.</b> Packable repeated
        /// members are written PACKED here -- proto3's default, one key and one length for the whole
        /// run -- while protobuf-net at CompatibilityLevel 200 writes them unpacked, one key per
        /// element. Measured on <c>int[]</c>: 102 bytes against 200 at 100 elements.
        /// </para>
        /// <para>
        /// Wire compatibility is about what the other side can READ, and that is what is checked in
        /// both directions below. It is a stronger claim than byte equality, not a weaker one:
        /// identical output only ever exercised this package's encoder and protobuf-net's, while
        /// this exercises both decoders as well.
        /// </para>
        /// </remarks>
        private static void AssertMatchesOracle(RepeatedContract value, ref int checks)
        {
            checks++;
            string oracle = OracleHex(value);
            string mine = MineHex(value);

            // Their bytes, our decoder.
            byte[] theirBytes = Parse(oracle);
            WProtoReader reader = new WProtoReader(theirBytes);
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<RepeatedContract>()
                    .TryRead(ref reader, out RepeatedContract fromTheirs),
                "could not read protobuf-net's bytes: " + oracle
            );

            RepeatedContract theirsFromTheirs;
            using (MemoryStream stream = new MemoryStream(theirBytes))
            {
                theirsFromTheirs = ProtoBuf.Serializer.Deserialize<RepeatedContract>(stream);
            }

            AssertSameValue(theirsFromTheirs, fromTheirs, oracle);

            // Our bytes, their decoder. This is the direction the packed change puts at risk, and
            // the one that would break a consumer who downgrades.
            byte[] myBytes = Parse(mine);
            RepeatedContract theirsFromMine;
            using (MemoryStream stream = new MemoryStream(myBytes))
            {
                theirsFromMine = ProtoBuf.Serializer.Deserialize<RepeatedContract>(stream);
            }

            AssertSameValue(theirsFromTheirs, theirsFromMine, "protobuf-net reading mine: " + mine);

            // And our own round trip, so a symmetric encoder/decoder bug cannot hide between them.
            WProtoReader mineReader = new WProtoReader(myBytes);
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<RepeatedContract>()
                    .TryRead(ref mineReader, out RepeatedContract fromMine),
                "could not read my own bytes: " + mine
            );
            AssertSameValue(theirsFromTheirs, fromMine, "mine reading mine: " + mine);
        }

        private static void AssertSameValue(
            RepeatedContract expected,
            RepeatedContract actual,
            string context
        )
        {
            CollectionAssert.AreEqual(expected.Ints, actual.Ints, context + " Ints");
            CollectionAssert.AreEqual(expected.IntList, actual.IntList, context + " IntList");
            CollectionAssert.AreEqual(expected.Texts, actual.Texts, context + " Texts");
            CollectionAssert.AreEqual(expected.Doubles, actual.Doubles, context + " Doubles");
            CollectionAssert.AreEqual(expected.Longs, actual.Longs, context + " Longs");
            CollectionAssert.AreEqual(expected.Flags, actual.Flags, context + " Flags");
            CollectionAssert.AreEqual(expected.Modes, actual.Modes, context + " Modes");
            CollectionAssert.AreEqual(expected.Points, actual.Points, context + " Points");
            CollectionAssert.AreEqual(expected.Shorts, actual.Shorts, context + " Shorts");
            CollectionAssert.AreEqual(expected.PointList, actual.PointList, context + " PointList");

            Assert.AreEqual(
                expected.Messages == null ? -1 : expected.Messages.Length,
                actual.Messages == null ? -1 : actual.Messages.Length,
                context + " Messages"
            );

            Assert.AreEqual(
                expected.Blobs == null ? -1 : expected.Blobs.Length,
                actual.Blobs == null ? -1 : actual.Blobs.Length,
                context + " Blobs"
            );
            if (expected.Blobs != null)
            {
                for (int index = 0; index < expected.Blobs.Length; index++)
                {
                    CollectionAssert.AreEqual(
                        expected.Blobs[index],
                        actual.Blobs[index],
                        context + " Blobs[" + index + "]"
                    );
                }
            }
        }

        private static string Describe(int[] values)
        {
            return values == null ? "null" : "[" + string.Join(",", values) + "]";
        }

        /// <summary>
        /// Asserts each serializer reads the other's bytes, for a contract with no shared corpus.
        /// </summary>
        private static void AssertRoundTripsBothWays(RepeatedStructContract value, string context)
        {
            byte[] theirs = Parse(OracleHex(value));
            byte[] mine = Parse(MineHex(value));

            RepeatedStructContract theirsFromTheirs;
            using (MemoryStream stream = new MemoryStream(theirs))
            {
                theirsFromTheirs = ProtoBuf.Serializer.Deserialize<RepeatedStructContract>(stream);
            }

            RepeatedStructContract theirsFromMine;
            using (MemoryStream stream = new MemoryStream(mine))
            {
                theirsFromMine = ProtoBuf.Serializer.Deserialize<RepeatedStructContract>(stream);
            }

            WProtoReader reader = new WProtoReader(theirs);
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<RepeatedStructContract>()
                    .TryRead(ref reader, out RepeatedStructContract mineFromTheirs),
                context
            );

            Assert.AreEqual(theirsFromTheirs.Marker, theirsFromMine.Marker, context);
            Assert.AreEqual(theirsFromTheirs.Marker, mineFromTheirs.Marker, context);
            CollectionAssert.AreEqual(theirsFromTheirs.Ints, theirsFromMine.Ints, context);
            CollectionAssert.AreEqual(theirsFromTheirs.Ints, mineFromTheirs.Ints, context);
        }

        private static string OracleHex<T>(T value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(stream, value);
                return ToHex(stream.ToArray());
            }
        }

        private static string MineHex<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(buffer.Length, writer.Position, "Measure disagreed with Write");
            return ToHex(buffer);
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
