// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using NUnit.Framework;
    using ProtoBuf.Meta;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins surrogates against protobuf-net 3.2.56.
    /// </summary>
    /// <remarks>
    /// A surrogate is how a type nobody owns gets a wire shape — Unity's <c>Vector3</c>,
    /// <c>Color</c> and <c>Bounds</c> cannot carry an attribute of ours. Measured: a surrogated
    /// member is <b>byte-identical</b> to a member of the surrogate type, so this is a substitution
    /// rather than a new encoding, and the surrogate's field numbers alone define the bytes.
    /// </remarks>
    [TestFixture]
    public sealed class SurrogateDifferentialTests
    {
        [OneTimeSetUp]
        public void RegisterOracleSurrogate()
        {
            // protobuf-net learns the pair from its runtime model; this package learns it from an
            // assembly attribute at build time. Both have to describe the same mapping for the
            // comparison below to mean anything.
            RuntimeTypeModel
                .Default.Add(typeof(ForeignVector3), false)
                .SetSurrogate(typeof(ForeignVector3Surrogate));
        }

        [Test]
        public void ASurrogatedMemberIsExactlyTheSurrogatesShape()
        {
            Assert.AreEqual(
                "0A0F0D0000803F15000000401D00004040",
                Encode(
                    new SurrogateHolder
                    {
                        Position = new ForeignVector3
                        {
                            x = 1,
                            y = 2,
                            z = 3,
                        },
                    }
                )
            );
        }

        [Test]
        public void ADefaultSurrogatedStructIsStillWritten()
        {
            // The struct sub-message rule, reached through a surrogate: `0A 00` rather than nothing.
            // Omitting it would read back as a default that the sender may not have meant.
            Assert.AreEqual("0A00", Encode(new SurrogateHolder()));
            Assert.AreEqual("0A001805", Encode(new SurrogateHolder { Trailer = 5 }));
        }

        [Test]
        public void EverySurrogatedShapeMatchesTheOracleByteForByte()
        {
            SurrogateHolder[] values =
            {
                new SurrogateHolder(),
                new SurrogateHolder
                {
                    Position = new ForeignVector3
                    {
                        x = 1,
                        y = 2,
                        z = 3,
                    },
                },
                new SurrogateHolder { Position = new ForeignVector3 { x = -0f } },
                new SurrogateHolder { Trailer = 5 },
                new SurrogateHolder { Path = Array.Empty<ForeignVector3>() },
                new SurrogateHolder { Path = new[] { new ForeignVector3 { x = 1 } } },
                new SurrogateHolder
                {
                    Path = new[]
                    {
                        default,
                        new ForeignVector3 { z = 2 },
                    },
                },
                new SurrogateHolder
                {
                    Position = new ForeignVector3 { y = float.MaxValue },
                    Path = new[] { new ForeignVector3 { x = float.NaN } },
                    Trailer = -1,
                },
                new SurrogateHolder
                {
                    Named = new Dictionary<string, ForeignVector3>
                    {
                        {
                            "a",
                            new ForeignVector3 { x = 1 }
                        },
                    },
                },
                new SurrogateHolder
                {
                    Named = new Dictionary<string, ForeignVector3> { { "b", default } },
                },
            };

            foreach (SurrogateHolder value in values)
            {
                Assert.AreEqual(OracleHex(value), Encode(value), Describe(value));
            }
        }

        [Test]
        public void ASurrogatedValueRoundTripsBackToTheRealType()
        {
            SurrogateHolder original = new SurrogateHolder
            {
                Position = new ForeignVector3
                {
                    x = 1,
                    y = 2,
                    z = 3,
                },
                Path = new[]
                {
                    new ForeignVector3 { x = 4 },
                    default,
                },
                Trailer = 7,
                Named = new Dictionary<string, ForeignVector3>
                {
                    {
                        "k",
                        new ForeignVector3 { z = 9 }
                    },
                },
            };

            SurrogateHolder restored = RoundTrip(original);

            Assert.AreEqual(1, restored.Position.x);
            Assert.AreEqual(2, restored.Position.y);
            Assert.AreEqual(3, restored.Position.z);
            Assert.AreEqual(2, restored.Path.Length);
            Assert.AreEqual(4, restored.Path[0].x);
            Assert.AreEqual(0, restored.Path[1].x);
            Assert.AreEqual(7, restored.Trailer);
            Assert.AreEqual(9, restored.Named["k"].z);
        }

        [Test]
        public void TheOracleDecodesWhatThisPackageWrote()
        {
            // Byte equality is not agreement about meaning, so the payload goes the other way too.
            SurrogateHolder original = new SurrogateHolder
            {
                Position = new ForeignVector3 { x = 1, z = 3 },
                Path = new[] { new ForeignVector3 { y = 2 } },
                Trailer = 4,
            };

            using (MemoryStream stream = new MemoryStream(Parse(Encode(original))))
            {
                SurrogateHolder theirs = ProtoBuf.Serializer.Deserialize<SurrogateHolder>(stream);
                Assert.AreEqual(1, theirs.Position.x);
                Assert.AreEqual(3, theirs.Position.z);
                Assert.AreEqual(2, theirs.Path[0].y);
                Assert.AreEqual(4, theirs.Trailer);
            }
        }

        private static string Describe(SurrogateHolder value)
        {
            return "Position=("
                + value.Position.x
                + ","
                + value.Position.y
                + ","
                + value.Position.z
                + ") Path="
                + (value.Path == null ? "null" : value.Path.Length.ToString())
                + " Trailer="
                + value.Trailer
                + " Named="
                + (value.Named == null ? "null" : value.Named.Count.ToString());
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
            using (MemoryStream stream = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(stream, value);
                return ToHex(stream.ToArray());
            }
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
    }
}
