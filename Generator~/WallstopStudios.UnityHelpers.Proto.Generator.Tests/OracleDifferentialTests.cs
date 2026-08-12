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

                // Every OTHER member is cleared, not just the collections: the payload is handed to
                // protobuf-net as a RepeatedContract, whose field 6 is a bool[]. A constructor-seeded
                // map at that number decodes as a packed run of booleans under v3 and throws
                // "Unexpected boolean value" under v2 -- a fixture that quietly reinterprets a field,
                // rather than anything about the encoding under test.
                string mine = MineHex(
                    new ValueTypeCollectionContract
                    {
                        Bag = bag,
                        Seeded = default,
                        SeededOverwritten = default,
                        SeededPairs = default,
                        SeededOverwrittenPairs = default,
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

        [Test]
        public void AStructDictionaryEncodesAsTheSameMapAsAReferenceOne()
        {
            // The map half of the same value-type assumption (#388). The emitter accepted a struct
            // dictionary and then wrote `member != null` and `read.Member ?? new Member()` for it,
            // which are both CS0019 -- so this shape did not produce wrong bytes, it produced a
            // compiler error inside generated source. Now that it compiles, the bytes are the claim:
            // a struct container is not a new wire shape.
            Dictionary<int, int>[] variants =
            {
                new Dictionary<int, int>(),
                new Dictionary<int, int> { { 0, 0 } },
                new Dictionary<int, int> { { 1, 300 } },
                new Dictionary<int, int> { { int.MaxValue, int.MinValue } },
            };

            foreach (Dictionary<int, int> entries in variants)
            {
                IntPairs pairs = new IntPairs();
                foreach (KeyValuePair<int, int> entry in entries)
                {
                    pairs.Add(entry.Key, entry.Value);
                }

                string oracle = OracleHex(new IntKeyedMapContract { Pairs = entries });
                string mine = MineHex(
                    new ValueTypeCollectionContract
                    {
                        Pairs = pairs,
                        Seeded = default,
                        SeededOverwritten = default,
                        SeededPairs = default,
                        SeededOverwrittenPairs = default,
                    }
                );

                Assert.AreEqual(oracle, mine, Describe(entries));
            }
        }

        [Test]
        public void AStructDictionaryMergesAndOverwritesLikeAReferenceOne()
        {
            // Same invisible half as the struct collection: every indexer assignment lands on a copy,
            // so the formatter has to assign it back, and that only shows on a member the constructor
            // already filled.
            ValueTypeCollectionContract absent = Decode(string.Empty);
            Assert.AreEqual(0, absent.Pairs.Count);
            CollectionAssert.AreEqual(new[] { 7 }, absent.SeededPairs.Keys);
            CollectionAssert.AreEqual(new[] { 7 }, absent.SeededOverwrittenPairs.Keys);

            // Field 5 {1: 1}, field 6 {1: 1}, field 7 {1: 1}.
            ValueTypeCollectionContract filled = Decode(
                "2A04" + "0801" + "1001" + "3204" + "0801" + "1001" + "3A04" + "0801" + "1001"
            );
            Assert.AreEqual(1, filled.Pairs[1]);
            Assert.AreEqual(1, filled.Pairs.Count);
            Assert.AreEqual(70, filled.SeededPairs[7]);
            Assert.AreEqual(1, filled.SeededPairs[1]);
            Assert.AreEqual(2, filled.SeededPairs.Count);
            Assert.AreEqual(1, filled.SeededOverwrittenPairs.Count);
            Assert.AreEqual(1, filled.SeededOverwrittenPairs[1]);
        }

        [Test]
        public void EveryStdlibCollectionShapeAgreesWithTheOracleBothWays()
        {
            // #395's whole point: these are shapes protobuf-net writes, so the bytes are a contract
            // and not a free choice. Byte equality is asserted only where it is the contract -- the
            // packable members are written packed here and unpacked by protobuf-net, per the
            // encoding policy -- so agreement is proven by having each side READ what the other
            // wrote, which exercises two decoders instead of two encoders.
            StdlibCollectionContract value = new StdlibCollectionContract
            {
                Linked = new LinkedList<int>(new[] { 1, 2, 300 }),
                Listed = new List<string> { "a", string.Empty },
                Collected = new List<int> { 0, -1 },
                Enumerated = new List<int> { 9 },
                ReadOnlyListed = new List<int> { 10, 11 },
                ReadOnlyCollected = new List<int> { 12 },
                Mapped = new Dictionary<string, int> { { "k", 13 } },
            };

            StdlibCollectionContract theirsFromMine;
            using (MemoryStream stream = new MemoryStream(Parse(MineHex(value))))
            {
                theirsFromMine = ProtoBuf.Serializer.Deserialize<StdlibCollectionContract>(stream);
            }

            AssertSameShape(value, theirsFromMine, "protobuf-net reading WallstopProto");

            WProtoReader reader = new WProtoReader(Parse(OracleHex(value)));
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<StdlibCollectionContract>()
                    .TryRead(ref reader, out StdlibCollectionContract mineFromTheirs)
            );

            AssertSameShape(value, mineFromTheirs, "WallstopProto reading protobuf-net");
        }

        private static void AssertSameShape(
            StdlibCollectionContract expected,
            StdlibCollectionContract actual,
            string context
        )
        {
            CollectionAssert.AreEqual(expected.Linked, actual.Linked, context + " Linked");
            CollectionAssert.AreEqual(expected.Listed, actual.Listed, context + " Listed");
            CollectionAssert.AreEqual(expected.Collected, actual.Collected, context + " Collected");
            CollectionAssert.AreEqual(
                expected.Enumerated,
                actual.Enumerated,
                context + " Enumerated"
            );
            CollectionAssert.AreEqual(
                expected.ReadOnlyListed,
                actual.ReadOnlyListed,
                context + " ReadOnlyListed"
            );
            CollectionAssert.AreEqual(
                expected.ReadOnlyCollected,
                actual.ReadOnlyCollected,
                context + " ReadOnlyCollected"
            );
            CollectionAssert.AreEquivalent(expected.Mapped, actual.Mapped, context + " Mapped");
        }

        [Test]
        public void TheStackAndSetShapesRoundTripThroughWallstopProto()
        {
            // Runs in both oracle processes because it never touches protobuf-net: 2.4.9 has no
            // serializer for Queue or Stack at all, and reading its own ISet or IReadOnlyDictionary
            // bytes throws. The claim here is WallstopProto's own, and the stack ordering is the
            // half that would have been guessed wrong -- pushing the decoded run back in wire order
            // inverts a stack, and nothing about the bytes says so.
            V3CollectionContract value = new V3CollectionContract
            {
                Queued = new Queue<int>(new[] { 4, 5 }),
                Stacked = new Stack<int>(new[] { 6, 7, 8 }),
                SetOf = new HashSet<int> { 12 },
                ReadOnlyMapped = new Dictionary<string, int> { { "r", 14 } },
                StackedPoints = new Stack<Outer.Point>(
                    new[]
                    {
                        new Outer.Point { X = 1 },
                        new Outer.Point { Y = 2 },
                    }
                ),
            };

            WProtoReader reader = new WProtoReader(Parse(MineHex(value)));
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<V3CollectionContract>()
                    .TryRead(ref reader, out V3CollectionContract decoded)
            );

            CollectionAssert.AreEqual(value.Queued, decoded.Queued, "Queued");
            CollectionAssert.AreEqual(value.Stacked, decoded.Stacked, "Stacked");
            CollectionAssert.AreEquivalent(value.SetOf, decoded.SetOf, "SetOf");
            CollectionAssert.AreEquivalent(
                value.ReadOnlyMapped,
                decoded.ReadOnlyMapped,
                "ReadOnlyMapped"
            );
            CollectionAssert.AreEqual(value.StackedPoints, decoded.StackedPoints, "StackedPoints");

            Assert.AreEqual(typeof(HashSet<int>), decoded.SetOf.GetType());
            Assert.AreEqual(typeof(Dictionary<string, int>), decoded.ReadOnlyMapped.GetType());
        }

#if !PROTOBUF_NET_ORACLE_V2
        [Test]
        public void TheStackAndSetShapesAgreeWithTheV3Oracle()
        {
            // Gated because 2.4.9 cannot serve any of these: Queue and Stack have no serializer, and
            // ISet and IReadOnlyDictionary write and then throw on read. That divergence is the
            // measurement, not a gap in coverage -- WallstopProto serves all five on both.
            V3CollectionContract value = new V3CollectionContract
            {
                Queued = new Queue<int>(new[] { 4, 5 }),
                Stacked = new Stack<int>(new[] { 6, 7, 8 }),
                SetOf = new HashSet<int> { 12 },
                ReadOnlyMapped = new Dictionary<string, int> { { "r", 14 } },
                StackedPoints = new Stack<Outer.Point>(
                    new[]
                    {
                        new Outer.Point { X = 1 },
                        new Outer.Point { Y = 2 },
                    }
                ),
            };

            V3CollectionContract theirsFromMine;
            using (MemoryStream stream = new MemoryStream(Parse(MineHex(value))))
            {
                theirsFromMine = ProtoBuf.Serializer.Deserialize<V3CollectionContract>(stream);
            }

            WProtoReader reader = new WProtoReader(Parse(OracleHex(value)));
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<V3CollectionContract>()
                    .TryRead(ref reader, out V3CollectionContract mineFromTheirs)
            );

            foreach (V3CollectionContract actual in new[] { theirsFromMine, mineFromTheirs })
            {
                CollectionAssert.AreEqual(value.Queued, actual.Queued, "Queued");
                CollectionAssert.AreEqual(value.Stacked, actual.Stacked, "Stacked");
                CollectionAssert.AreEquivalent(value.SetOf, actual.SetOf, "SetOf");
                CollectionAssert.AreEquivalent(
                    value.ReadOnlyMapped,
                    actual.ReadOnlyMapped,
                    "ReadOnlyMapped"
                );
                CollectionAssert.AreEqual(
                    value.StackedPoints,
                    actual.StackedPoints,
                    "StackedPoints"
                );
            }
        }
#endif

        [Test]
        public void AnInterfaceMemberIsLeftHoldingTheImplementationTheOraclePicks()
        {
            // Which concrete type a round trip leaves behind is a decision, not an implementation
            // detail: a consumer's code runs against whatever the member holds afterwards. These are
            // the types protobuf-net produces, measured on both majors, so a migrating contract
            // keeps working.
            WProtoReader reader = new WProtoReader(
                Parse(
                    OracleHex(
                        new StdlibCollectionContract
                        {
                            Listed = new List<string> { "a" },
                            Collected = new List<int> { 1 },
                            Enumerated = new List<int> { 2 },
                            ReadOnlyListed = new List<int> { 3 },
                            ReadOnlyCollected = new List<int> { 4 },
                            Mapped = new Dictionary<string, int> { { "k", 5 } },
                        }
                    )
                )
            );

            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<StdlibCollectionContract>()
                    .TryRead(ref reader, out StdlibCollectionContract decoded)
            );

            Assert.AreEqual(typeof(List<string>), decoded.Listed.GetType());
            Assert.AreEqual(typeof(List<int>), decoded.Collected.GetType());
            Assert.AreEqual(typeof(List<int>), decoded.Enumerated.GetType());
            Assert.AreEqual(typeof(List<int>), decoded.ReadOnlyListed.GetType());
            Assert.AreEqual(typeof(List<int>), decoded.ReadOnlyCollected.GetType());
            Assert.AreEqual(typeof(Dictionary<string, int>), decoded.Mapped.GetType());
        }

#if !PROTOBUF_NET_ORACLE_V2
        [Test]
        public void ACollectionThatCanOnlyBeConstructedIsWrittenLikeTheOracleAndReadUnlikeIt()
        {
            // protobuf-net writes both of these and then refuses to read either back, so this is the
            // one place the two claims differ. The write is byte-identical -- the members are a
            // string map and a packable run, and the run is packed here by policy, so the map half
            // is compared literally and the whole payload is handed to the oracle to decode.
            ConstructedCollectionContract value = new ConstructedCollectionContract
            {
                Frozen = new System.Collections.ObjectModel.ReadOnlyCollection<int>(
                    new List<int> { 1, 2 }
                ),
                FrozenMap = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(
                    new Dictionary<string, int> { { "k", 3 } }
                ),
            };

            ConstructedCollectionContract mapOnly = new ConstructedCollectionContract
            {
                FrozenMap = value.FrozenMap,
            };
            Assert.AreEqual(OracleHex(mapOnly), MineHex(mapOnly), "the map half is byte-identical");

            WProtoReader reader = new WProtoReader(Parse(OracleHex(value)));
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<ConstructedCollectionContract>()
                    .TryRead(ref reader, out ConstructedCollectionContract decoded)
            );

            CollectionAssert.AreEqual(new[] { 1, 2 }, decoded.Frozen);
            Assert.AreEqual(3, decoded.FrozenMap["k"]);

            // And the oracle cannot do the same with its own bytes, which is why the two contracts
            // are separate fixtures rather than one.
            using (MemoryStream stream = new MemoryStream(Parse(OracleHex(value))))
            {
                Assert.Throws(
                    Is.InstanceOf<Exception>(),
                    () => ProtoBuf.Serializer.Deserialize<ConstructedCollectionContract>(stream)
                );
            }
        }
#endif

        [Test]
        public void TheNewCollectionShapesAppendAndOverwriteAsMeasured()
        {
            // Every expectation below is protobuf-net 3.2.56's answer to the same payload, measured
            // before the emitter was written. The stack is the interesting row twice over: the first
            // decoded element ends up on TOP of the constructor's stack, and OverwriteList replaces
            // it outright.
            //
            // The payload is produced by WallstopProto rather than by the oracle because two of
            // these members are shapes protobuf-net 2.4.9 has no serializer for, and a payload of
            // one element per field is not something an oracle is needed to construct.
            string payload = MineHex(
                new SeededStdlibContract
                {
                    Linked = new LinkedList<int>(new[] { 1 }),
                    Queued = new Queue<int>(new[] { 1 }),
                    Stacked = new Stack<int>(new[] { 1 }),
                    OverwrittenStack = new Stack<int>(new[] { 1 }),
                    Listed = new List<int> { 1 },
                    OverwrittenList = new List<int> { 1 },
                    SetOf = new HashSet<int> { 1 },
                    Mapped = new Dictionary<string, int> { { "k", 1 } },
                }
            );

            WProtoReader reader = new WProtoReader(Parse(payload));
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<SeededStdlibContract>()
                    .TryRead(ref reader, out SeededStdlibContract mine)
            );

            CollectionAssert.AreEqual(new[] { 7, 8, 1 }, mine.Linked, "Linked");
            CollectionAssert.AreEqual(new[] { 7, 8, 1 }, mine.Queued, "Queued");
            CollectionAssert.AreEqual(new[] { 1, 8, 7 }, mine.Stacked, "Stacked");
            CollectionAssert.AreEqual(new[] { 1 }, mine.OverwrittenStack, "OverwrittenStack");
            CollectionAssert.AreEqual(new[] { 7, 8, 1 }, mine.Listed, "Listed");
            CollectionAssert.AreEqual(new[] { 1 }, mine.OverwrittenList, "OverwrittenList");
            CollectionAssert.AreEquivalent(new[] { 7, 8, 1 }, mine.SetOf, "SetOf");
            Assert.AreEqual(9, mine.Mapped["seed"], "Mapped keeps the seed");
            Assert.AreEqual(1, mine.Mapped["k"], "Mapped takes the payload");
        }

        [Test]
        public void AnAbsentFieldLeavesEveryNewCollectionShapeAlone()
        {
            // "Absent" and "empty" are the same bytes, so the constructor's value has to survive an
            // empty payload -- including for a stack, whose commit runs from the epilogue and would
            // otherwise replace it with a fresh one.
            WProtoReader reader = new WProtoReader(System.Array.Empty<byte>());
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<SeededStdlibContract>()
                    .TryRead(ref reader, out SeededStdlibContract mine)
            );

            CollectionAssert.AreEqual(new[] { 7, 8 }, mine.Linked);
            CollectionAssert.AreEqual(new[] { 7, 8 }, mine.Queued);
            CollectionAssert.AreEqual(new[] { 8, 7 }, mine.Stacked);
            CollectionAssert.AreEqual(new[] { 8, 7 }, mine.OverwrittenStack);
            CollectionAssert.AreEqual(new[] { 7, 8 }, mine.Listed);
            CollectionAssert.AreEqual(new[] { 7, 8 }, mine.OverwrittenList);
            CollectionAssert.AreEquivalent(new[] { 7, 8 }, mine.SetOf);
            Assert.AreEqual(9, mine.Mapped["seed"]);
        }

        [Test]
        public void AnEmptyOrNullNewCollectionShapeWritesNothing()
        {
            // The rule every repeated member obeys, restated for the shapes that reach it through
            // new code -- in particular the count-free IEnumerable path, whose emptiness test is a
            // flag set by the first element rather than a Count.
            Assert.AreEqual(string.Empty, MineHex(new StdlibCollectionContract()));
            Assert.AreEqual(
                string.Empty,
                MineHex(
                    new StdlibCollectionContract
                    {
                        Linked = new LinkedList<int>(),
                        Listed = new List<string>(),
                        Collected = new List<int>(),
                        Enumerated = new List<int>(),
                        ReadOnlyListed = new List<int>(),
                        ReadOnlyCollected = new List<int>(),
                        Mapped = new Dictionary<string, int>(),
                    }
                )
            );

            Assert.AreEqual(string.Empty, MineHex(new V3CollectionContract()));
            Assert.AreEqual(
                string.Empty,
                MineHex(
                    new V3CollectionContract
                    {
                        Queued = new Queue<int>(),
                        Stacked = new Stack<int>(),
                        SetOf = new HashSet<int>(),
                        ReadOnlyMapped = new Dictionary<string, int>(),
                        StackedPoints = new Stack<Outer.Point>(),
                    }
                )
            );

            Assert.AreEqual(string.Empty, MineHex(new ConstructedCollectionContract()));
        }

        private static string Describe(Dictionary<int, int> entries)
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<int, int> entry in entries)
            {
                parts.Add(entry.Key + ":" + entry.Value);
            }

            return "{" + string.Join(", ", parts) + "}";
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
