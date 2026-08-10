// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Runs the generated repeated-field code inside Unity, on the editors and players CI builds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle comparison is not here. protobuf-net is what fails under IL2CPP -- the reason this
    /// serializer exists -- so a differential run in Unity could only ever execute on the Mono legs,
    /// and it already runs on every push from the generator's own <c>dotnet test</c> suite. What
    /// only Unity can prove is that the emitted loops, the packed reader and the accumulator are
    /// AOT-compiled and behave, which is what the standalone legs do with these fixtures.
    /// </para>
    /// <para>
    /// Every expected payload below was copied out of protobuf-net 3.2.56 serializing a contract
    /// with the same field numbers, so the guarantee survives here even though the oracle cannot.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoRepeatedContractTests
    {
        [Test]
        public void AVarintElementRunIsUnpackedWithOneKeyPerElement()
        {
            Assert.AreEqual(
                "0801080008FFFFFFFFFFFFFFFFFF01",
                Encode(new WProtoRepeatedContract { Ints = new[] { 1, 0, -1 } })
            );
        }

        [Test]
        public void AnElementEqualToItsDefaultIsStillWritten()
        {
            // The scalar rule reversed. A member holding 0 is omitted; an element holding 0 is not,
            // because dropping it would shorten the collection rather than restore a default.
            Assert.AreEqual("0800", Encode(new WProtoRepeatedContract { Ints = new[] { 0 } }));
            Assert.AreEqual("3000", Encode(new WProtoRepeatedContract { Flags = new[] { false } }));
            Assert.AreEqual(
                "4200",
                Encode(
                    new WProtoRepeatedContract { Points = new[] { default(WProtoRepeatedPoint) } }
                )
            );
        }

        [Test]
        public void EveryElementShapeMatchesTheBytesTheOracleProduced()
        {
            Assert.AreEqual(
                "1002",
                Encode(new WProtoRepeatedContract { IntList = new List<int> { 2 } })
            );
            Assert.AreEqual(
                "1A01611A00",
                Encode(new WProtoRepeatedContract { Texts = new[] { "a", string.Empty } })
            );
            Assert.AreEqual(
                "21000000000000000021000000000000F03F",
                Encode(new WProtoRepeatedContract { Doubles = new[] { 0d, 1d } })
            );
            Assert.AreEqual(
                "28002801",
                Encode(new WProtoRepeatedContract { Longs = new ulong[] { 0, 1 } })
            );
            Assert.AreEqual(
                "30003001",
                Encode(new WProtoRepeatedContract { Flags = new[] { false, true } })
            );
            Assert.AreEqual(
                "380038AC02",
                Encode(
                    new WProtoRepeatedContract
                    {
                        Modes = new[] { WProtoRepeatedMode.None, WProtoRepeatedMode.Careful },
                    }
                )
            );
            Assert.AreEqual(
                "4200420408011002",
                Encode(
                    new WProtoRepeatedContract
                    {
                        Points = new[]
                        {
                            default,
                            new WProtoRepeatedPoint { X = 1, Y = 2 },
                        },
                    }
                )
            );
            Assert.AreEqual(
                "4A00",
                Encode(
                    new WProtoRepeatedContract { Messages = new[] { new WProtoRepeatedEmpty() } }
                )
            );
            Assert.AreEqual(
                "5200520101",
                Encode(
                    new WProtoRepeatedContract
                    {
                        Blobs = new[] { Array.Empty<byte>(), new byte[] { 1 } },
                    }
                )
            );
            Assert.AreEqual(
                "5A020803",
                Encode(
                    new WProtoRepeatedContract
                    {
                        PointList = new List<WProtoRepeatedPoint>
                        {
                            new WProtoRepeatedPoint { X = 3 },
                        },
                    }
                )
            );
            Assert.AreEqual(
                "60FEFFFFFFFFFFFFFFFF016000",
                Encode(new WProtoRepeatedContract { Shorts = new short[] { -2, 0 } })
            );
        }

        [Test]
        public void MembersAreWrittenInAscendingFieldNumberOrder()
        {
            Assert.AreEqual(
                "0801080210031A016160FEFFFFFFFFFFFFFFFF01",
                Encode(
                    new WProtoRepeatedContract
                    {
                        Ints = new[] { 1, 2 },
                        IntList = new List<int> { 3 },
                        Texts = new[] { "a" },
                        Shorts = new short[] { -2 },
                    }
                )
            );
        }

        [Test]
        public void NullAndEmptyCollectionsAreTheSameBytesAndBothReadBackAsNull()
        {
            // A silent data change, reproduced deliberately: nothing on the wire separates an empty
            // repeated field from an absent one, so an empty collection with no constructor value
            // behind it comes back null.
            Assert.AreEqual(string.Empty, Encode(new WProtoRepeatedContract()));
            Assert.AreEqual(
                string.Empty,
                Encode(
                    new WProtoRepeatedContract
                    {
                        Ints = Array.Empty<int>(),
                        IntList = new List<int>(),
                        Texts = Array.Empty<string>(),
                        Blobs = Array.Empty<byte[]>(),
                    }
                )
            );

            WProtoRepeatedContract restored = RoundTrip(
                new WProtoRepeatedContract { Ints = Array.Empty<int>(), IntList = new List<int>() }
            );
            Assert.IsTrue(restored.Ints == null);
            Assert.IsTrue(restored.IntList == null);
        }

        [Test]
        public void EveryRepeatedShapeRoundTrips()
        {
            WProtoRepeatedContract original = new()
            {
                Ints = new[] { 1, 0, -1, int.MinValue },
                IntList = new List<int> { 2, 3 },
                Texts = new[] { "a", string.Empty, "é中" },
                Doubles = new[] { 0d, 1d, double.NaN },
                Longs = new ulong[] { 0, ulong.MaxValue },
                Flags = new[] { false, true },
                Modes = new[] { WProtoRepeatedMode.None, WProtoRepeatedMode.Careful },
                Points = new[]
                {
                    default,
                    new WProtoRepeatedPoint { X = 1, Y = 2 },
                },
                Messages = new[] { new WProtoRepeatedEmpty(), new WProtoRepeatedEmpty() },
                Blobs = new[] { Array.Empty<byte>(), new byte[] { 1, 255 } },
                PointList = new List<WProtoRepeatedPoint> { new() { X = 3 } },
                Shorts = new short[] { -2, 0, short.MaxValue },
            };

            WProtoRepeatedContract restored = RoundTrip(original);

            CollectionAssert.AreEqual(original.Ints, restored.Ints);
            CollectionAssert.AreEqual(original.IntList, restored.IntList);
            CollectionAssert.AreEqual(original.Texts, restored.Texts);
            CollectionAssert.AreEqual(original.Doubles, restored.Doubles);
            CollectionAssert.AreEqual(original.Longs, restored.Longs);
            CollectionAssert.AreEqual(original.Flags, restored.Flags);
            CollectionAssert.AreEqual(original.Modes, restored.Modes);
            CollectionAssert.AreEqual(original.Points, restored.Points);
            CollectionAssert.AreEqual(original.PointList, restored.PointList);
            CollectionAssert.AreEqual(original.Shorts, restored.Shorts);
            Assert.AreEqual(2, restored.Messages.Length);
            CollectionAssert.AreEqual(original.Blobs[0], restored.Blobs[0]);
            CollectionAssert.AreEqual(original.Blobs[1], restored.Blobs[1]);
        }

        [Test]
        public void AStructContractCarriesARepeatedMember()
        {
            Assert.AreEqual(
                "080108021003",
                Encode(new WProtoRepeatedStructContract { Ints = new[] { 1, 2 }, Marker = 3 })
            );

            WProtoRepeatedStructContract restored = RoundTrip(
                new WProtoRepeatedStructContract { Ints = new[] { 1, 2 }, Marker = 3 }
            );
            CollectionAssert.AreEqual(new[] { 1, 2 }, restored.Ints);
            Assert.AreEqual(3, restored.Marker);
        }

        [Test]
        public void ReadingAppendsToTheConstructorsCollectionUnlessOverwriteListIsSet()
        {
            // Measured against protobuf-net, both halves: a member initialized to {7,8} that
            // receives {1} holds {7,8,1} by default and {1} under OverwriteList. Getting it backwards
            // duplicates a game's default inventory on every load, or silently discards it.
            WProtoSeededRepeatedContract present = Decode<WProtoSeededRepeatedContract>(
                "0801100118012001"
            );

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
            WProtoSeededRepeatedContract absent = Decode<WProtoSeededRepeatedContract>("2801");

            CollectionAssert.AreEqual(new[] { 7, 8 }, absent.AppendedList);
            CollectionAssert.AreEqual(new[] { 7, 8 }, absent.OverwrittenList);
            CollectionAssert.AreEqual(new[] { 7, 8 }, absent.AppendedArray);
            CollectionAssert.AreEqual(new[] { 7, 8 }, absent.OverwrittenArray);
            Assert.AreEqual(1, absent.Marker);
        }

        [Test]
        public void APackedPayloadDecodesIntoAMemberThisPackageWritesUnpacked()
        {
            // protobuf-net accepts either form for the same member and interleaves them (measured).
            // A reader that knew only the form it writes would treat the field as unrecognized, skip
            // it, and hand back a short collection with no error anywhere.
            CollectionAssert.AreEqual(
                new[] { 1, 2, 300 },
                Decode<WProtoRepeatedContract>("0A040102AC02").Ints
            );
            CollectionAssert.AreEqual(
                new[] { 1, 2, 3 },
                Decode<WProtoRepeatedContract>("0A0201020803").Ints
            );
            CollectionAssert.AreEqual(
                new[] { 1d },
                Decode<WProtoRepeatedContract>("2208000000000000F03F").Doubles
            );
            CollectionAssert.AreEqual(
                new short[] { -2 },
                Decode<WProtoRepeatedContract>("620AFEFFFFFFFFFFFFFFFF01").Shorts
            );
        }

        [Test]
        public void APresentButEmptyPackedRunProducesAnEmptyCollectionRatherThanNothing()
        {
            // The one shape that CAN distinguish empty from absent, because the length prefix is on
            // the wire. protobuf-net returns an empty collection here, not null (measured).
            int[] decoded = Decode<WProtoRepeatedContract>("0A00").Ints;

            Assert.IsTrue(decoded != null);
            Assert.AreEqual(0, decoded.Length);
        }

        [Test]
        public void APackedRunDoesNotSpendANestingLevel()
        {
            // A packed run holds primitives with no field keys, so it cannot recurse and must not be
            // charged for nesting. Charging it would refuse an array at the bottom of an otherwise
            // legal message that protobuf-net accepts.
            WProtoReader outer = new(new byte[] { 0x0A, 0x02, 0x01, 0x02 });
            Assert.IsTrue(outer.TryReadTag(out int fieldNumber, out int wireType));
            Assert.AreEqual(1, fieldNumber);
            Assert.AreEqual(WProtoWireType.LengthDelimited, wireType);
            Assert.IsTrue(outer.TryReadPackedRun(out WProtoReader packed));
            Assert.AreEqual(outer.Depth, packed.Depth);
        }

        [Test]
        public void ATruncatedRepeatedElementIsReportedRatherThanTruncatingTheCollection()
        {
            IWProtoFormatter<WProtoRepeatedContract> formatter =
                WProtoFormatterProvider.Get<WProtoRepeatedContract>();

            foreach (string payload in new[] { "080108", "0A02018F" })
            {
                WProtoReader reader = new(Parse(payload));
                Assert.IsFalse(
                    formatter.TryRead(ref reader, out WProtoRepeatedContract value),
                    payload
                );
                Assert.IsTrue(value == null, payload);
            }
        }

        [Test]
        public void ANullElementIsRefusedByNameRatherThanEncodedAsSomethingElse()
        {
            // Writing it would either invent an empty value -- a null string element encodes exactly
            // like "" -- or drop the element and shorten the collection. protobuf-net raises on the
            // same input; this names the member instead.
            IWProtoFormatter<WProtoRepeatedContract> formatter =
                WProtoFormatterProvider.Get<WProtoRepeatedContract>();

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                formatter.Measure(new WProtoRepeatedContract { Texts = new[] { "a", null } })
            );

            StringAssert.Contains("WProtoRepeatedContract", refused.Message);
            StringAssert.Contains("Texts", refused.Message);
        }

        [Test]
        public void EveryCollectionShapeEncodesAsTheSameRepeatedField()
        {
            // The container is not part of the encoding. These bytes are protobuf-net's own output
            // for a HashSet, a SortedSet, a Collection and an int[] at the same field numbers -- the
            // struct collection at field 4 is compared against the array because protobuf-net cannot
            // serialize one at all, which is the reason supporting it is worth doing.
            WProtoIntBag bag = new();
            bag.Add(5);
            bag.Add(6);

            Assert.AreEqual(
                "0801080208AC02120161120162180018FFFFFFFFFFFFFFFFFF0120052006",
                Encode(
                    new WProtoCollectionShapesContract
                    {
                        Set = new HashSet<int> { 1, 2, 300 },
                        Sorted = new SortedSet<string> { "b", "a" },
                        Owned = new System.Collections.ObjectModel.Collection<int> { 0, -1 },
                        Bag = bag,
                    }
                )
            );

            Assert.AreEqual(string.Empty, Encode(new WProtoCollectionShapesContract()));
        }

        [Test]
        public void AStructCollectionRoundTripsThroughACopy()
        {
            // Every Add during a read lands on a local copy of the struct, so the formatter has to
            // assign it back. Nothing about that is visible until the member is a value type.
            WProtoIntBag bag = new();
            bag.Add(5);
            bag.Add(6);

            WProtoCollectionShapesContract restored = RoundTrip(
                new WProtoCollectionShapesContract
                {
                    Set = new HashSet<int> { 1, 2 },
                    Sorted = new SortedSet<string> { "a" },
                    Owned = new System.Collections.ObjectModel.Collection<int> { 3 },
                    Bag = bag,
                }
            );

            CollectionAssert.AreEqual(new[] { 1, 2 }, restored.Set);
            CollectionAssert.AreEqual(new[] { "a" }, restored.Sorted);
            CollectionAssert.AreEqual(new[] { 3 }, restored.Owned);
            CollectionAssert.AreEqual(new[] { 5, 6 }, restored.Bag);
            Assert.AreEqual(2, restored.Bag.Count);
        }

        [Test]
        public void MeasurePredictsWriteExactlyForEveryRepeatedShape()
        {
            // The buffer a parent allocates comes from Measure, so a repeated member whose
            // measurement disagrees with its output corrupts every message that contains it.
            WProtoRepeatedContract[] cases =
            {
                new(),
                new() { Ints = new[] { 0, 1, -1, int.MinValue, int.MaxValue } },
                new() { Texts = new[] { string.Empty, "é中" } },
                new() { Blobs = new[] { Array.Empty<byte>(), new byte[300] } },
                new()
                {
                    Points = new[]
                    {
                        default,
                        new WProtoRepeatedPoint { X = int.MinValue },
                    },
                },
                new() { Shorts = new short[] { short.MinValue, short.MaxValue } },
            };

            IWProtoFormatter<WProtoRepeatedContract> formatter =
                WProtoFormatterProvider.Get<WProtoRepeatedContract>();

            foreach (WProtoRepeatedContract value in cases)
            {
                int predicted = formatter.Measure(value);
                byte[] buffer = new byte[predicted];
                WProtoWriter writer = new(buffer);
                Assert.IsTrue(formatter.Write(ref writer, value));
                Assert.AreEqual(predicted, writer.Position);
            }
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
            WProtoReader reader = new(Parse(hex));
            Assert.IsTrue(WProtoFormatterProvider.Get<T>().TryRead(ref reader, out T value), hex);
            return value;
        }

        private static string Encode<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));

            StringBuilder builder = new(writer.Position * 2);
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
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));

            WProtoReader reader = new(buffer);
            Assert.IsTrue(formatter.TryRead(ref reader, out T restored));
            return restored;
        }
    }
}
