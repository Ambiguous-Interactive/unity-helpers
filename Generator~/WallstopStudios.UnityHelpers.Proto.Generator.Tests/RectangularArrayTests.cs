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
    /// Pins the wrapper-message encoding that gives a rectangular array a shape on the wire (#434).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Like the nested-collection vectors, these bytes have no oracle behind them, and
    /// <see cref="TheOracleRefusesEveryRectangularShape"/> is why. The message is
    /// <c>message Rect { repeated int32 dims = 1; repeated T values = 2; }</c>, which is a concrete
    /// proto3 definition rather than a private convention.
    /// </para>
    /// <para>
    /// <c>0A 0A 0A 02 02 02 12 04 01 02 03 04</c> reads as: field 1, length-delimited, ten bytes,
    /// holding field 1 as the packed dimension run <c>02 02</c> and field 2 as the packed element run
    /// <c>01 02 03 04</c>.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class RectangularArrayTests
    {
        [Test]
        public void TheOracleRefusesEveryRectangularShape()
        {
            // The premise the whole encoding rests on, kept as a measurement rather than as a claim
            // in a comment. The two majors refuse for DIFFERENT reasons -- 2.4.9 has a dedicated
            // multi-dimensional message and 3.2.56 falls into its generic repeated refusal -- so
            // asserting either message alone would pass on one oracle leg and fail on the other.
            OracleRectangularContract value = new OracleRectangularContract
            {
                Grid = new[,]
                {
                    { 1, 2 },
                    { 3, 4 },
                },
            };

            using MemoryStream stream = new MemoryStream();
            NotSupportedException refusal = Assert.Throws<NotSupportedException>(() =>
                ProtoBuf.Serializer.Serialize(stream, value)
            );

#if PROTOBUF_NET_ORACLE_V2
            const string expected = "Multi-dimensional arrays are not supported";
#else
            const string expected = "Repeated data of type System.Int32[,] is not supported";
#endif

            Assert.AreEqual(
                expected,
                refusal.Message,
                "protobuf-net refused for an unexpected reason"
            );
        }

        [Test]
        public void EveryRectangularShapeMatchesItsWrapperEncoding()
        {
            Assert.AreEqual(
                "0A0A0A020202120401020304",
                Encode(
                    Bare(c =>
                        c.Grid = new[,]
                        {
                            { 1, 2 },
                            { 3, 4 },
                        }
                    )
                ),
                "int[,]"
            );

            // Rank three: three dimensions in the header and the elements in row-major order.
            Assert.AreEqual(
                "120B0A03010202120401020304",
                Encode(
                    Bare(c =>
                        c.Volume = new[,,]
                        {
                            {
                                { 1, 2 },
                                { 3, 4 },
                            },
                        }
                    )
                ),
                "int[,,]"
            );

            // A length-delimited element cannot be packed, so the run is one key per element --
            // exactly what a top-level repeated string does, one level down.
            Assert.AreEqual(
                "1A0A0A020201120161120162",
                Encode(
                    Bare(c =>
                        c.Labels = new[,]
                        {
                            { "a" },
                            { "b" },
                        }
                    )
                ),
                "string[,]"
            );

            // A zero-length dimension is the case that forces the header to be written even when the
            // element run is empty: this array has a real shape and no elements.
            Assert.AreEqual("0A040A020005", Encode(Bare(c => c.Grid = new int[0, 5])), "int[0,5]");
        }

        [Test]
        public void EveryRectangularShapeRoundTrips()
        {
            RectangularArrayContract original = new RectangularArrayContract
            {
                Grid = new[,]
                {
                    { 1, 2, 3 },
                    { 4, 5, 6 },
                },
                Volume = new[,,]
                {
                    {
                        { 1, 2 },
                        { 3, 4 },
                    },
                    {
                        { 5, 6 },
                        { 7, 8 },
                    },
                },
                Labels = new[,]
                {
                    { "a", "b" },
                    { "c", "d" },
                },
                Points = new[,]
                {
                    {
                        new Outer.Point { X = 1, Y = 2 },
                    },
                },
                Layers = new[]
                {
                    new[,]
                    {
                        { 1, 2 },
                    },
                    new[,]
                    {
                        { 3 },
                        { 4 },
                    },
                },
                Frames = new List<int[,]>
                {
                    new[,]
                    {
                        { 9 },
                    },
                },
                Named = new Dictionary<string, int[,]>
                {
                    {
                        "k",
                        new[,]
                        {
                            { 7, 8 },
                        }
                    },
                },
                Rows = new int[,][]
                {
                    { new[] { 1, 2 }, new[] { 3 } },
                },
                Blobs = new byte[,]
                {
                    { 1, 2 },
                    { 3, 4 },
                },
            };

            RectangularArrayContract restored = RoundTrip(original);

            AssertSameShape(original.Grid, restored.Grid, "Grid");
            AssertSameShape(original.Volume, restored.Volume, "Volume");
            AssertSameShape(original.Labels, restored.Labels, "Labels");
            Assert.AreEqual(1, restored.Points.GetLength(0));
            Assert.AreEqual(1, restored.Points[0, 0].X);
            Assert.AreEqual(2, restored.Points[0, 0].Y);
            Assert.AreEqual(2, restored.Layers.Length);
            AssertSameShape(original.Layers[1], restored.Layers[1], "Layers[1]");
            AssertSameShape(original.Frames[0], restored.Frames[0], "Frames[0]");
            AssertSameShape(original.Named["k"], restored.Named["k"], "Named[k]");
            Assert.AreEqual(1, restored.Rows.GetLength(0));
            Assert.AreEqual(2, restored.Rows.GetLength(1));
            CollectionAssert.AreEqual(new[] { 1, 2 }, restored.Rows[0, 0]);
            CollectionAssert.AreEqual(new[] { 3 }, restored.Rows[0, 1]);
            AssertSameShape(original.Blobs, restored.Blobs, "Blobs");
        }

        [Test]
        public void AShapeSurvivesEvenWhenTheElementsCannotDistinguishIt()
        {
            // The entire reason a header exists. Both of these deliver 1..6 in the same order, and
            // without the dimensions there is nothing in the payload that tells them apart.
            RectangularArrayContract wide = RoundTrip(
                Bare(c =>
                    c.Grid = new[,]
                    {
                        { 1, 2, 3 },
                        { 4, 5, 6 },
                    }
                )
            );
            RectangularArrayContract tall = RoundTrip(
                Bare(c =>
                    c.Grid = new[,]
                    {
                        { 1, 2 },
                        { 3, 4 },
                        { 5, 6 },
                    }
                )
            );

            Assert.AreEqual(2, wide.Grid.GetLength(0));
            Assert.AreEqual(3, wide.Grid.GetLength(1));
            Assert.AreEqual(3, tall.Grid.GetLength(0));
            Assert.AreEqual(2, tall.Grid.GetLength(1));
            Assert.AreEqual(6, wide.Grid[1, 2]);
            Assert.AreEqual(6, tall.Grid[2, 1]);
        }

        [Test]
        public void AZeroLengthDimensionKeepsItsShape()
        {
            // `new int[0, 5]` has no elements and a real shape, which is the one place the
            // omit-an-empty-run rule had to be overridden rather than inherited.
            RectangularArrayContract restored = RoundTrip(Bare(c => c.Grid = new int[0, 5]));

            Assert.AreEqual(0, restored.Grid.GetLength(0));
            Assert.AreEqual(5, restored.Grid.GetLength(1));

            RectangularArrayContract empty = RoundTrip(Bare(c => c.Grid = new int[0, 0]));

            Assert.AreEqual(0, empty.Grid.GetLength(0));
            Assert.AreEqual(0, empty.Grid.GetLength(1));
        }

        [TestCase(1, "0202", "", "a header claiming four elements with an empty run")]
        [TestCase(1, "85EA0285EA02", "0102", "a header claiming 46341x46341 with two elements")]
        [TestCase(
            2,
            "808080028080800180808001",
            null,
            "a rank-three header whose product wraps to 0"
        )]
        [TestCase(
            1,
            "FFFFFFFF0700",
            null,
            "an axis of int.MaxValue beside a zero axis, which multiplies to an empty run"
        )]
        [TestCase(
            1,
            "008080C002",
            null,
            "a zero axis first, so the product is zero before the large axis is even read"
        )]
        [TestCase(1, "02020202", "01020304", "a rank-four header on a rank-two member")]
        [TestCase(1, "02", "01020304", "a rank-one header on a rank-two member")]
        [TestCase(1, "0202", "010203040506", "a run longer than the header allows")]
        [TestCase(1, "FFFFFFFF0F02", "01020304", "a negative dimension")]
        [TestCase(1, null, "01020304", "elements with no header at all")]
        public void AHeaderThatDisagreesWithItsElementsIsRefused(
            int tag,
            string dimensions,
            string values,
            string why
        )
        {
            // A dimension header is a CAPACITY CLAIM rather than a length prefix: nothing about the
            // bytes that carry it bounds what it asks for, and `[46341, 46341]` costs six bytes and
            // would ask for 8 GB. Requiring the product to equal the delivered count is what turns
            // the claim back into a number the sender already paid for -- so every one of these is
            // refused rather than allocated, clamped, or silently zero-filled.
            //
            // Every length prefix is COMPUTED. Four of these payloads were hand-written hex whose
            // prefixes disagreed with their contents, so the reader refused them for being
            // malformed and the property under test was never reached -- they passed, and proved
            // nothing. AWellFormedHeaderIsAcceptedByTheSameBuilder is the control that keeps this
            // honest.
            IWProtoFormatter<RectangularArrayContract> formatter =
                WProtoFormatterProvider.Get<RectangularArrayContract>();
            WProtoReader reader = new WProtoReader(Wrapper(tag, dimensions, values));

            Assert.IsFalse(
                formatter.TryRead(ref reader, out RectangularArrayContract _),
                "a payload was accepted that should not have been: " + why
            );
        }

        [Test]
        public void AWellFormedHeaderIsAcceptedByTheSameBuilder()
        {
            // The control for the table above. Without it, a builder that produced malformed bytes
            // would make every hostile case pass for the wrong reason -- which is exactly what the
            // hand-written version of that table did.
            IWProtoFormatter<RectangularArrayContract> formatter =
                WProtoFormatterProvider.Get<RectangularArrayContract>();
            WProtoReader reader = new WProtoReader(Wrapper(1, "0202", "01020304"));

            Assert.IsTrue(formatter.TryRead(ref reader, out RectangularArrayContract restored));
            Assert.AreEqual(2, restored.Grid.GetLength(0));
            Assert.AreEqual(2, restored.Grid.GetLength(1));
            Assert.AreEqual(4, restored.Grid[1, 1]);
        }

        [Test]
        public void AnEmptyShapeWithAnOrdinaryAxisIsStillAccepted()
        {
            // The control for the two zero-axis rows above, and the reason they are refused for the
            // axis rather than for being empty. `new int[5, 0]` is a real shape with real dimensions
            // and no elements, and it has to keep round-tripping -- the refusal is of an axis
            // nothing pays for, not of an empty array.
            IWProtoFormatter<RectangularArrayContract> formatter =
                WProtoFormatterProvider.Get<RectangularArrayContract>();
            WProtoReader reader = new WProtoReader(Wrapper(1, "0500", null));

            Assert.IsTrue(formatter.TryRead(ref reader, out RectangularArrayContract restored));
            Assert.AreEqual(5, restored.Grid.GetLength(0));
            Assert.AreEqual(0, restored.Grid.GetLength(1));
        }

        [Test]
        public void AnUnpackedHeaderIsAcceptedLikeAnyOtherRepeatedInt32()
        {
            // `repeated int32 dims = 1` is an ordinary proto3 field, so a toolkit generating from
            // this schema may write it one key per dimension. Leniency on read cannot lose data, and
            // it is the same rule the element run already follows in both directions.
            IWProtoFormatter<RectangularArrayContract> formatter =
                WProtoFormatterProvider.Get<RectangularArrayContract>();

            // Field 1: a wrapper holding dims as two separate varint fields, then the packed run.
            WProtoReader reader = new WProtoReader(Bytes("0A0A08020802120401020304"));

            Assert.IsTrue(formatter.TryRead(ref reader, out RectangularArrayContract restored));
            Assert.AreEqual(2, restored.Grid.GetLength(0));
            Assert.AreEqual(2, restored.Grid.GetLength(1));
            Assert.AreEqual(4, restored.Grid[1, 1]);
        }

        [Test]
        public void AnUnpackedElementRunIsAcceptedTooAndTheHeaderMayFollowIt()
        {
            // Field order is not a wire guarantee, so the header may arrive after the run it
            // describes -- which is the case the flat accumulator exists for.
            IWProtoFormatter<RectangularArrayContract> formatter =
                WProtoFormatterProvider.Get<RectangularArrayContract>();
            WProtoReader reader = new WProtoReader(Bytes("0A0C10011002100310040A020202"));

            Assert.IsTrue(formatter.TryRead(ref reader, out RectangularArrayContract restored));
            Assert.AreEqual(2, restored.Grid.GetLength(0));
            Assert.AreEqual(3, restored.Grid[1, 0]);
        }

        [Test]
        public void ANonZeroLowerBoundIsRefusedRatherThanRebased()
        {
            // The one array a writer can hold that the encoding cannot express. Only
            // Array.CreateInstance produces it, nothing on the wire carries a lower bound, and
            // reading rebuilds with `new T[a, b]` -- so writing it would hand every element back
            // under a different index.
            int[,] rebased = (int[,])
                Array.CreateInstance(typeof(int), new[] { 2, 2 }, new[] { 1, 1 });
            rebased[1, 1] = 7;

            InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() =>
                Encode(Bare(c => c.Grid = rebased))
            );

            Assert.IsTrue(
                refusal.Message.Contains("index 1 rather than 0"),
                "unexpected refusal: " + refusal.Message
            );
        }

        [Test]
        public void ANullRectangularMemberIsOmittedAndAbsentLeavesTheConstructorValueAlone()
        {
            Assert.AreEqual(string.Empty, Encode(new RectangularArrayContract()));
            Assert.IsTrue(RoundTrip(new RectangularArrayContract()).Grid == null);
        }

        [Test]
        public void ANullRectangularElementIsRefusedRatherThanInvented()
        {
            // The same rule a null element of any repeated member gets. The message names the TYPE
            // rather than a member, because one wrapper serves every member holding that type.
            InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() =>
                Encode(Bare(c => c.Frames = new List<int[,]> { null }))
            );

            Assert.IsTrue(
                refusal.Message.Contains("null 'int[,]' element"),
                "unexpected refusal: " + refusal.Message
            );
        }

        [Test]
        public void MeasurePredictsWriteExactlyForEveryRectangularShape()
        {
            // A wrapper's Measure and Write are two separate emitted methods over the same value,
            // and a length prefix produced by one and consumed by the other is what corrupts
            // silently. Encode asserts the two agree for each of these.
            RectangularArrayContract[] cases =
            {
                Bare(c => c.Grid = new int[0, 0]),
                Bare(c => c.Grid = new int[0, 5]),
                Bare(c =>
                    c.Grid = new[,]
                    {
                        { int.MinValue, int.MaxValue },
                    }
                ),
                Bare(c => c.Volume = new int[2, 0, 3]),
                Bare(c =>
                    c.Labels = new[,]
                    {
                        { string.Empty, "é" },
                    }
                ),
                Bare(c => c.Points = new Outer.Point[1, 1]),
                Bare(c => c.Layers = new[] { new int[0, 0] }),
                Bare(c => c.Named = new Dictionary<string, int[,]> { { "k", new int[1, 1] } }),
                Bare(c =>
                    c.Rows = new int[1, 1][]
                    {
                        { Array.Empty<int>() },
                    }
                ),
                Bare(c => c.Blobs = new byte[2, 2]),
                new RectangularArrayContract(),
            };

            foreach (RectangularArrayContract value in cases)
            {
                Encode(value);
            }
        }

        [Test]
        public void AWrapperSpendsANestingLevelAndGivesItBack()
        {
            RectangularArrayContract value = Bare(c =>
                c.Layers = new[]
                {
                    new[,]
                    {
                        { 1 },
                    },
                }
            );

            IWProtoFormatter<RectangularArrayContract> formatter =
                WProtoFormatterProvider.Get<RectangularArrayContract>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);

            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(0, writer.Depth, "a wrapper left the nesting depth unbalanced");
        }

        [Test]
        public void AWrapperCarryingAnUnknownFieldIsSteppedOverRatherThanRefused()
        {
            // Forward compatibility one level down: a wrapper is a real message, so a payload from a
            // later build that adds a third field to it has to be skipped exactly.
            IWProtoFormatter<RectangularArrayContract> formatter =
                WProtoFormatterProvider.Get<RectangularArrayContract>();
            WProtoReader reader = new WProtoReader(Bytes("0A0C0A0202021204010203041807"));

            Assert.IsTrue(formatter.TryRead(ref reader, out RectangularArrayContract restored));
            Assert.AreEqual(4, restored.Grid[1, 1]);
        }

        [Test]
        public void AnImmutableContractRoundTripsItsRectangularArray()
        {
            ImmutableRectangularContract restored = RoundTrip(
                new ImmutableRectangularContract(
                    new[,]
                    {
                        { 1, 2 },
                        { 3, 4 },
                    }
                )
            );

            Assert.AreEqual(2, restored.Grid.GetLength(1));
            Assert.AreEqual(3, restored.Grid[1, 0]);
        }

        [Test]
        public void AGenericContractRoundTripsARectangularArrayOfItsTypeParameter()
        {
            // Whether the run packs is decided at the closure, and the header is independent of it.
            RectangularBox<int> packed = RoundTrip(
                new RectangularBox<int>
                {
                    Grid = new[,]
                    {
                        { 1, 2 },
                        { 3, 4 },
                    },
                }
            );
            RectangularBox<string> loose = RoundTrip(
                new RectangularBox<string>
                {
                    Grid = new[,]
                    {
                        { "a", "b" },
                    },
                }
            );

            Assert.AreEqual(4, packed.Grid[1, 1]);
            Assert.AreEqual(1, loose.Grid.GetLength(0));
            Assert.AreEqual("b", loose.Grid[0, 1]);
        }

        private static void AssertSameShape<T>(T[,] expected, T[,] actual, string what)
        {
            Assert.AreEqual(expected.GetLength(0), actual.GetLength(0), what + " rows");
            Assert.AreEqual(expected.GetLength(1), actual.GetLength(1), what + " columns");
            for (int row = 0; row < expected.GetLength(0); row++)
            {
                for (int column = 0; column < expected.GetLength(1); column++)
                {
                    Assert.AreEqual(
                        expected[row, column],
                        actual[row, column],
                        what + "[" + row + "," + column + "]"
                    );
                }
            }
        }

        private static void AssertSameShape<T>(T[,,] expected, T[,,] actual, string what)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                Assert.AreEqual(
                    expected.GetLength(axis),
                    actual.GetLength(axis),
                    what + " axis " + axis
                );
            }

            for (int x = 0; x < expected.GetLength(0); x++)
            {
                for (int y = 0; y < expected.GetLength(1); y++)
                {
                    for (int z = 0; z < expected.GetLength(2); z++)
                    {
                        Assert.AreEqual(expected[x, y, z], actual[x, y, z], what);
                    }
                }
            }
        }

        /// <summary>
        /// Builds a wrapper message for the contract member at <paramref name="tag"/>, from the
        /// dimension header's bytes and the element run's bytes.
        /// </summary>
        /// <remarks>
        /// Every length prefix is computed rather than typed. A prefix that disagrees with its
        /// content makes the reader refuse the payload for being malformed, which is a different
        /// answer from the one these tests are about -- and a test that gets the right verdict for
        /// the wrong reason proves nothing.
        /// </remarks>
        private static byte[] Wrapper(int tag, string dimensionsHex, string valuesHex)
        {
            List<byte> payload = new List<byte>();
            Append(payload, 1, dimensionsHex);
            Append(payload, 2, valuesHex);

            Assert.Less(payload.Count, 128, "the builder writes single-byte length prefixes");

            List<byte> message = new List<byte> { (byte)((tag << 3) | 2), (byte)payload.Count };
            message.AddRange(payload);
            return message.ToArray();
        }

        private static void Append(List<byte> payload, int field, string hex)
        {
            if (hex == null)
            {
                return;
            }

            byte[] content = Bytes(hex);
            Assert.Less(content.Length, 128, "the builder writes single-byte length prefixes");
            payload.Add((byte)((field << 3) | 2));
            payload.Add((byte)content.Length);
            payload.AddRange(content);
        }

        private static byte[] Bytes(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            return bytes;
        }

        private static RectangularArrayContract Bare(Action<RectangularArrayContract> set)
        {
            RectangularArrayContract value = new RectangularArrayContract();
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
