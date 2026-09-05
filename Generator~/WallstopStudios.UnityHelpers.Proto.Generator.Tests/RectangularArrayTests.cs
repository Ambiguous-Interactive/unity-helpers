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
            /*
             * The oracle majors reject rectangular arrays for different reasons, so avoid asserting one
             * exception message.
             */
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

            // A zero axis still has shape information, requiring a header despite having no elements.
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
        [TestCase(
            2,
            "808080020200",
            null,
            "a rank-three header whose zero axis hides an unbacked one, the shape #434 had"
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
            /*
             * Computed prefixes ensure hostile dimensions reach capacity validation instead of failing
             * earlier as malformed bytes.
             */
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
            // A valid control prevents a broken payload builder from making every rejection test pass.
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
            /*
             * A bounded zero axis is valid; these controls distinguish empty shapes from unbacked dimension
             * claims.
             */
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
            // Schema-generated clients may encode dimensions unpacked, so the reader must accept both forms.
            IWProtoFormatter<RectangularArrayContract> formatter =
                WProtoFormatterProvider.Get<RectangularArrayContract>();

            WProtoReader reader = new WProtoReader(Bytes("0A0A08020802120401020304"));

            Assert.IsTrue(formatter.TryRead(ref reader, out RectangularArrayContract restored));
            Assert.AreEqual(2, restored.Grid.GetLength(0));
            Assert.AreEqual(2, restored.Grid.GetLength(1));
            Assert.AreEqual(4, restored.Grid[1, 1]);
        }

        [Test]
        public void AnUnpackedElementRunIsAcceptedTooAndTheHeaderMayFollowIt()
        {
            // Field ordering is unrestricted, so dimensions can arrive after elements.
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
            /*
             * The wire form carries no lower bound; accepting nonzero-based arrays would change element
             * indices.
             */
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
