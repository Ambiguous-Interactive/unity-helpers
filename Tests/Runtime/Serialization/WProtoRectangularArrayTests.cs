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
    /// Pins the wrapper-message encoding that gives a rectangular array a shape on the wire (#434),
    /// inside Unity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every hex constant here is the output the generator suite measured for the mirrored
    /// <c>RectangularArrayContract</c> under <c>Generator~/</c>, at the same field numbers. The shape
    /// has no oracle -- protobuf-net refuses it at write on both 2.4.9 and 3.2.56 -- so the golden
    /// vectors are the only thing standing between a generated formatter and a silent encoding
    /// change, and the IL2CPP standalone legs are the only place this code is AOT-compiled.
    /// </para>
    /// <para>
    /// <c>0A 0A 0A 02 02 02 12 04 01 02 03 04</c> reads as: field 1, length-delimited, ten bytes,
    /// holding field 1 as the packed dimension run <c>02 02</c> and field 2 as the packed element
    /// run <c>01 02 03 04</c>.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoRectangularArrayTests
    {
        [Test]
        public void EveryRectangularShapeMatchesItsGoldenBytes()
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

            // Unpacked inside the wrapper, because a string is length-delimited and a packed run of
            // length-delimited values could not be parsed.
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

            // The header is written even though the run is empty: this array has a real shape and no
            // elements, which is the one place the omit-an-empty-run rule is overridden.
            Assert.AreEqual("0A040A020005", Encode(Bare(c => c.Grid = new int[0, 5])), "int[0,5]");
        }

        [Test]
        public void AShapeSurvivesEvenWhenTheElementsCannotDistinguishIt()
        {
            // The entire reason a header exists. Both of these deliver 1..6 in the same order.
            WProtoRectangularArrayContract wide = RoundTrip(
                Bare(c =>
                    c.Grid = new[,]
                    {
                        { 1, 2, 3 },
                        { 4, 5, 6 },
                    }
                )
            );
            WProtoRectangularArrayContract tall = RoundTrip(
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
            Assert.AreEqual(6, wide.Grid[1, 2]);
            Assert.AreEqual(3, tall.Grid.GetLength(0));
            Assert.AreEqual(2, tall.Grid.GetLength(1));
            Assert.AreEqual(6, tall.Grid[2, 1]);
        }

        [Test]
        public void EveryRectangularShapeRoundTrips()
        {
            WProtoRectangularArrayContract restored = RoundTrip(
                new WProtoRectangularArrayContract
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
                    },
                    Labels = new[,]
                    {
                        { "a", "b" },
                        { "c", "d" },
                    },
                    Points = new[,]
                    {
                        {
                            new WProtoNestedPoint { X = 1, Y = 2 },
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
                }
            );

            Assert.AreEqual(6, restored.Grid[1, 2]);
            Assert.AreEqual(4, restored.Volume[0, 1, 1]);
            Assert.AreEqual("d", restored.Labels[1, 1]);
            Assert.AreEqual(2, restored.Points[0, 0].Y);
            Assert.AreEqual(2, restored.Layers.Length);
            Assert.AreEqual(4, restored.Layers[1][1, 0]);
            Assert.AreEqual(9, restored.Frames[0][0, 0]);
            Assert.AreEqual(8, restored.Named["k"][0, 1]);
            CollectionAssert.AreEqual(new[] { 3 }, restored.Rows[0, 1]);
            Assert.AreEqual(4, restored.Blobs[1, 1]);
        }

        [Test]
        public void AZeroLengthDimensionKeepsItsShape()
        {
            WProtoRectangularArrayContract restored = RoundTrip(Bare(c => c.Grid = new int[0, 5]));

            Assert.AreEqual(0, restored.Grid.GetLength(0));
            Assert.AreEqual(5, restored.Grid.GetLength(1));
        }

        [TestCase(1, "0202", "", "a header claiming four elements with an empty run")]
        [TestCase(1, "85EA0285EA02", "0102", "a header claiming 46341x46341 with two elements")]
        [TestCase(
            2,
            "808080028080800180808001",
            null,
            "a rank-three header whose product wraps to 0"
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
            WProtoReader reader = new WProtoReader(Wrapper(tag, dimensions, values));

            Assert.IsFalse(
                WProtoFormatterProvider
                    .Get<WProtoRectangularArrayContract>()
                    .TryRead(ref reader, out WProtoRectangularArrayContract _),
                "a payload was accepted that should not have been: " + why
            );
        }

        [Test]
        public void AWellFormedHeaderIsAcceptedByTheSameBuilder()
        {
            // The control for the table above. Without it, a builder that produced malformed bytes
            // would make every hostile case pass for the wrong reason -- which is exactly what the
            // hand-written version of that table did.
            WProtoReader reader = new WProtoReader(Wrapper(1, "0202", "01020304"));

            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<WProtoRectangularArrayContract>()
                    .TryRead(ref reader, out WProtoRectangularArrayContract restored)
            );
            Assert.AreEqual(2, restored.Grid.GetLength(0));
            Assert.AreEqual(2, restored.Grid.GetLength(1));
            Assert.AreEqual(4, restored.Grid[1, 1]);
        }

        [Test]
        public void AnUnpackedHeaderIsAcceptedLikeAnyOtherRepeatedInt32()
        {
            // `repeated int32 dims = 1` is an ordinary proto3 field, so a toolkit generating from
            // this schema may write it one key per dimension, and leniency on read cannot lose data.
            WProtoRectangularArrayContract restored = Decode("0A0A08020802120401020304");

            Assert.AreEqual(2, restored.Grid.GetLength(0));
            Assert.AreEqual(4, restored.Grid[1, 1]);
        }

        [Test]
        public void AHeaderThatFollowsItsElementsIsStillHonoured()
        {
            // Field order is not a wire guarantee, so the header may arrive after the run it
            // describes -- which is the case the flat accumulator exists for.
            WProtoRectangularArrayContract restored = Decode("0A0C10011002100310040A020202");

            Assert.AreEqual(2, restored.Grid.GetLength(1));
            Assert.AreEqual(3, restored.Grid[1, 0]);
        }

        [Test]
        public void ANonZeroLowerBoundIsRefusedRatherThanRebased()
        {
            // The one array a writer can hold that the encoding cannot express. Nothing on the wire
            // carries a lower bound and reading rebuilds with `new T[a, b]`, so writing it would hand
            // every element back under a different index.
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
        public void AnAbsentRectangularMemberLeavesTheConstructorValueAlone()
        {
            Assert.AreEqual(string.Empty, Encode(new WProtoRectangularArrayContract()));
            Assert.IsTrue(RoundTrip(new WProtoRectangularArrayContract()).Grid == null);
        }

        [Test]
        public void AWrapperCarryingAnUnknownFieldIsSteppedOverRatherThanRefused()
        {
            // Forward compatibility one level down: a wrapper is a real message, so a payload from a
            // later build that adds a third field to it has to be skipped exactly.
            WProtoRectangularArrayContract restored = Decode("0A0C0A0202021204010203041807");

            Assert.AreEqual(4, restored.Grid[1, 1]);
        }

        private static WProtoRectangularArrayContract Bare(
            Action<WProtoRectangularArrayContract> set
        )
        {
            WProtoRectangularArrayContract value = new WProtoRectangularArrayContract();
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

        private static WProtoRectangularArrayContract Decode(string hex)
        {
            WProtoReader reader = new WProtoReader(Parse(hex));
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<WProtoRectangularArrayContract>()
                    .TryRead(ref reader, out WProtoRectangularArrayContract value),
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

            byte[] content = Parse(hex);
            Assert.Less(content.Length, 128, "the builder writes single-byte length prefixes");
            payload.Add((byte)((field << 3) | 2));
            payload.Add((byte)content.Length);
            payload.AddRange(content);
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
