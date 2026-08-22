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
    /// Proves <c>DataFormat = ZigZag</c> is the same bytes under the generated formatter and under
    /// protobuf-net 3.2.56, and that those bytes are shorter than the default encoding's.
    /// </summary>
    /// <remarks>
    /// The size half matters as much as the parity half. ZigZag is not a correctness feature: a
    /// member encoded either way round trips through its own reader, so an implementation that
    /// silently kept writing <c>int32</c> would pass every round-trip assertion in this file. What
    /// it cannot pass is the byte count.
    /// </remarks>
    [TestFixture]
    public sealed class ZigZagDifferentialTests
    {
        [Test]
        public void EveryZigZagShapeEncodesExactlyAsProtobufNetDoes()
        {
            int checks = 0;
            foreach (ZigZagContract value in Corpus())
            {
                checks++;
                string oracle = OracleHex(value);
                Assert.AreEqual(oracle, Encode(value), "the two encoders disagree");

                // Their bytes through our reader, and ours through theirs. A symmetric bug in one
                // encoder/decoder pair cannot survive both directions.
                using MemoryStream stream = new MemoryStream(Parse(Encode(value)));
                ZigZagContract theirs = ProtoBuf.Serializer.Deserialize<ZigZagContract>(stream);
                AssertSameValue(value, theirs, "protobuf-net reading mine: " + oracle);

                WProtoReader reader = new WProtoReader(Parse(oracle));
                Assert.IsTrue(
                    WProtoFormatterProvider
                        .Get<ZigZagContract>()
                        .TryRead(ref reader, out ZigZagContract mine),
                    "could not read protobuf-net's bytes: " + oracle
                );
                AssertSameValue(value, mine, "mine reading theirs: " + oracle);
            }

            Assert.Greater(checks, 40, "The ZigZag corpus shrank; that is a coverage loss");
        }

        /// <summary>
        /// The reason the feature exists, stated as an inequality rather than as a round trip.
        /// </summary>
        /// <remarks>
        /// <c>-1</c> is the extreme case and the one that motivated it: protobuf's <c>int32</c>
        /// sign-extends a negative value to 64 bits, so it costs the full ten bytes, where ZigZag
        /// maps it onto <c>1</c> and costs one.
        /// </remarks>
        [Test]
        public void ANegativeZigZagMemberCostsOneByteWhereTheDefaultCostsTen()
        {
            ZigZagContract zigZag = new ZigZagContract { Int32 = -1 };
            ZigZagContract plain = new ZigZagContract { Plain = -1 };

            Assert.AreEqual("0801", Encode(zigZag));
            Assert.AreEqual("30FFFFFFFFFFFFFFFFFF01", Encode(plain));
        }

        /// <summary>
        /// ZigZag maps zero onto zero, so a default member is still the field that is not written.
        /// </summary>
        [Test]
        public void AZigZagMemberAtZeroIsAbsentJustAsTheDefaultEncodingWouldBe()
        {
            Assert.AreEqual(string.Empty, Encode(new ZigZagContract()));
            Assert.AreEqual(string.Empty, OracleHex(new ZigZagContract()));

            // ...and a Nullable member set to zero is present, which is the case "absent means
            // zero" would encode identically and wrongly.
            ZigZagContract explicitZero = new ZigZagContract { MaybeInt32 = 0 };
            Assert.AreEqual("2800", Encode(explicitZero));
            Assert.AreEqual(OracleHex(explicitZero), Encode(explicitZero));
        }

        /// <summary>
        /// The grid the change was made for, measured rather than predicted.
        /// </summary>
        /// <remarks>
        /// Two 40x25 grids of 1,000 cells each -- one anchored at the origin, one centered on it.
        /// Under <c>int32</c> the centered one cost 2.5x the anchored one for the same magnitudes,
        /// because half its coordinates were negative and a negative <c>int32</c> sign-extends to
        /// ten bytes. Under <c>sint32</c> the two are identical, which is the property being bought:
        /// a cell's cost follows its distance from the origin rather than which side of it it sits.
        /// </remarks>
        [Test]
        public void ACentredGridCostsWhatAnAnchoredOneDoes()
        {
            int anchored = GridBytes(0, 0);
            int centered = GridBytes(-20, -12);

            Assert.AreEqual(anchored, centered, "sign must not decide a cell's cost");
            Assert.AreEqual(3870, anchored, "the measured component cost of 1,000 cells");
        }

        /// <summary>
        /// ZigZag is not free: it spends the low bit on the sign, so a large positive value grows.
        /// </summary>
        /// <remarks>
        /// Recorded as a test rather than as a caveat in prose because it is the honest half of the
        /// tradeoff, and because it bounds it: the loss is one byte per component and only inside
        /// the bands a varint boundary falls in, against nine bytes saved on every negative one.
        /// </remarks>
        [Test]
        public void ALargePositiveComponentCostsOneByteMoreThanItDidAsAnInt32()
        {
            Assert.AreEqual(4, Encode(new GridCellShape { X = 8192 }).Length / 2);
            Assert.AreEqual(3, Encode(new GridCellShape { LegacyX = 8192 }).Length / 2);

            // ...and the value one below the band, where the two agree.
            Assert.AreEqual(3, Encode(new GridCellShape { X = 8191 }).Length / 2);
            Assert.AreEqual(3, Encode(new GridCellShape { LegacyX = 8191 }).Length / 2);
        }

        /// <summary>
        /// The golden bytes the Unity fixture pins for <c>FastVector2Int</c> and
        /// <c>FastVector3Int</c>, produced here against the real protobuf-net.
        /// </summary>
        [Test]
        public void TheShippedCellShapeEncodesAsProtobufNetDoes()
        {
            (GridCellShape cell, string expected)[] cases =
            {
                (new GridCellShape(), string.Empty),
                (new GridCellShape { X = 1, Y = 2 }, "280230 04".Replace(" ", string.Empty)),
                (
                    new GridCellShape
                    {
                        X = 1,
                        Y = 2,
                        Z = 3,
                    },
                    "2802300438 06".Replace(" ", string.Empty)
                ),
                (new GridCellShape { X = -1, Y = -2 }, "28013003"),
                (
                    new GridCellShape
                    {
                        X = -1,
                        Y = -2,
                        Z = -3,
                    },
                    "280130033805"
                ),
                (
                    new GridCellShape { X = int.MaxValue, Y = int.MinValue },
                    "28FEFFFFFF0F30FFFFFFFF0F"
                ),
                (
                    new GridCellShape { X = int.MaxValue, Z = int.MinValue },
                    "28FEFFFFFF0F38FFFFFFFF0F"
                ),
                (new GridCellShape { LegacyX = 1, LegacyY = 2 }, "08011002"),
            };

            foreach ((GridCellShape cell, string expected) in cases)
            {
                Assert.AreEqual(expected, OracleHex(cell), "protobuf-net");
                Assert.AreEqual(expected, Encode(cell), "WallstopProto");
            }
        }

        /// <summary>
        /// A payload carrying only the <c>int32</c> fields still decodes to the same coordinates.
        /// </summary>
        /// <remarks>
        /// This is the reason the components moved to new field numbers instead of changing the
        /// format of the ones they had. A varint written as <c>int32</c> and read as <c>sint32</c>
        /// is a wrong number rather than a failure, so a renumbering would have halved every
        /// coordinate in every grid already saved, silently.
        /// </remarks>
        [Test]
        public void APayloadWrittenBeforeTheZigZagFieldsExistedStillReadsBack()
        {
            // 08 07 10 08 20 09 -- x=7, y=8, z=9 as int32 on the fields they used to occupy, plus
            // the cached hash on field 3 that a build older still would have written.
            WProtoReader reader = new WProtoReader(Parse("0807100818FFFFFFFF072009"));
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<GridCellShape>()
                    .TryRead(ref reader, out GridCellShape cell)
            );

            Assert.AreEqual(7, cell.LegacyX);
            Assert.AreEqual(8, cell.LegacyY);
            Assert.AreEqual(9, cell.LegacyZ);
            Assert.AreEqual(0, cell.X, "a legacy payload carries no zigzag field");
            Assert.AreEqual(0, cell.Y);
            Assert.AreEqual(0, cell.Z);
        }

        private static int GridBytes(int originX, int originY)
        {
            int total = 0;
            for (int x = originX; x < originX + 40; x++)
            {
                for (int y = originY; y < originY + 25; y++)
                {
                    total += Encode(new GridCellShape { X = x, Y = y }).Length / 2;
                }
            }

            return total;
        }

        private static IEnumerable<ZigZagContract> Corpus()
        {
            int[] int32Values = { 0, 1, -1, 63, 64, -64, -65, int.MaxValue, int.MinValue };

            foreach (int value in int32Values)
            {
                yield return new ZigZagContract { Int32 = value };
                yield return new ZigZagContract { MaybeInt32 = value };
                yield return new ZigZagContract { Plain = value };
            }

            foreach (long value in new[] { 0L, 1L, -1L, long.MaxValue, long.MinValue })
            {
                yield return new ZigZagContract { Int64 = value };
            }

            foreach (short value in new short[] { 0, 1, -1, short.MaxValue, short.MinValue })
            {
                yield return new ZigZagContract { Int16 = value };
            }

            foreach (sbyte value in new sbyte[] { 0, 1, -1, sbyte.MaxValue, sbyte.MinValue })
            {
                yield return new ZigZagContract { Int8 = value };
            }

            // Every member at once, so no member's encoding depends on its neighbors being default.
            yield return new ZigZagContract
            {
                Int32 = -12345,
                Int64 = -1234567890123L,
                Int16 = -321,
                Int8 = -21,
                MaybeInt32 = -7,
                Plain = -7,
            };
        }

        private static void AssertSameValue(
            ZigZagContract expected,
            ZigZagContract actual,
            string context
        )
        {
            Assert.AreEqual(expected.Int32, actual.Int32, context);
            Assert.AreEqual(expected.Int64, actual.Int64, context);
            Assert.AreEqual(expected.Int16, actual.Int16, context);
            Assert.AreEqual(expected.Int8, actual.Int8, context);
            Assert.AreEqual(expected.MaybeInt32, actual.MaybeInt32, context);
            Assert.AreEqual(expected.Plain, actual.Plain, context);
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

        private static string OracleHex<T>(T value)
        {
            using MemoryStream stream = new();
            ProtoBuf.Serializer.Serialize(stream, value);
            return ToHex(stream.ToArray());
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new(bytes.Length * 2);
            foreach (byte current in bytes)
            {
                builder.Append(current.ToString("X2"));
            }

            return builder.ToString();
        }

        private static string Encode<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(buffer.Length, writer.Position, "Measure disagreed with Write");
            return ToHex(buffer);
        }
    }
}
