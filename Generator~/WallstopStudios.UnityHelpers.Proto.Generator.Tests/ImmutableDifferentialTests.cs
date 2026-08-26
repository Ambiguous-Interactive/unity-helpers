// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.IO;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins contracts whose members cannot be assigned after construction.
    /// </summary>
    /// <remarks>
    /// Thirty of this package's serialized fields are <c>readonly</c>. protobuf-net assigns them by
    /// reflection, which is precisely what does not survive IL2CPP — so the generator emits a
    /// private constructor into the contract's own <c>partial</c> declaration instead. C# allows a
    /// readonly field to be assigned there and nowhere else, so the type keeps the immutability its
    /// author chose and gains no public surface.
    /// </remarks>
    [TestFixture]
    public sealed class ImmutableDifferentialTests
    {
        [Test]
        public void AnImmutableStructRoundTripsThroughItsGeneratedConstructor()
        {
            ImmutablePoint original = Build(1, -2, "a", new[] { 3, 4 });
            ImmutablePoint restored = RoundTrip(original);

            Assert.AreEqual(1, restored.X);
            Assert.AreEqual(-2, restored.Y);
            Assert.AreEqual("a", restored.Label);
            CollectionAssert.AreEqual(new[] { 3, 4 }, restored.Marks);
        }

        [Test]
        public void AnImmutableClassRoundTripsToo()
        {
            ImmutableRecord restored = RoundTrip(BuildRecord(7, "n", new[] { 1, 2 }));

            Assert.AreEqual(7, restored.Id);
            Assert.AreEqual("n", restored.Name);
            CollectionAssert.AreEqual(new[] { 1, 2 }, restored.Tags);
        }

        [Test]
        public void EveryImmutableShapeRoundTripsThroughProtobufNet()
        {
            ImmutablePoint[] values =
            {
                default,
                Build(1, 0, null, null),
                Build(0, -1, string.Empty, Array.Empty<int>()),
                Build(int.MinValue, int.MaxValue, "é中", new[] { 0, -1 }),
            };

            foreach (ImmutablePoint value in values)
            {
                AssertProtobufNetReadsMine(value, Describe(value));
            }

            ImmutableRecord[] records =
            {
                BuildRecord(0, null, null),
                BuildRecord(7, "n", null),
                BuildRecord(0, string.Empty, Array.Empty<int>()),
                BuildRecord(-1, "x", new[] { 1, 0, -1 }),
            };

            foreach (ImmutableRecord record in records)
            {
                AssertProtobufNetReadsMine(record, record.Id.ToString());
            }
        }

        [Test]
        public void TheGeneratedConstructorDoesNotCollideWithTheAuthorsOwn()
        {
            // ImmutablePoint declares its own (int, int) constructor. The generated one is
            // disambiguated by a leading WProtoConstruct, so both exist -- and the author's still
            // behaves exactly as written.
            ImmutablePoint theirs = new ImmutablePoint(5, 6);

            Assert.AreEqual(5, theirs.X);
            Assert.AreEqual(6, theirs.Y);
            Assert.IsNull(theirs.Label);
            Assert.IsNull(theirs.Marks);
        }

        [Test]
        public void MeasurePredictsWriteExactlyForAnImmutableContract()
        {
            ImmutablePoint value = Build(int.MinValue, 1, new string('x', 200), new[] { 1, 2, 3 });
            IWProtoFormatter<ImmutablePoint> formatter =
                WProtoFormatterProvider.Get<ImmutablePoint>();

            int predicted = formatter.Measure(value);
            byte[] buffer = new byte[predicted];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(predicted, writer.Position);
        }

        // The oracle can set readonly fields by reflection, which is how a fixture value with all
        // four members populated is produced without adding a constructor this package would then
        // be testing instead of the generated one.
        private static ImmutablePoint Build(int x, int y, string label, int[] marks)
        {
            using MemoryStream stream = new();
            ProtoBuf.Serializer.Serialize(
                stream,
                new Seed
                {
                    X = x,
                    Y = y,
                    Label = label,
                    Marks = marks,
                }
            );
            stream.Position = 0;
            return ProtoBuf.Serializer.Deserialize<ImmutablePoint>(stream);
        }

        private static ImmutableRecord BuildRecord(int id, string name, int[] tags)
        {
            using MemoryStream stream = new();
            ProtoBuf.Serializer.Serialize(
                stream,
                new RecordSeed
                {
                    Id = id,
                    Name = name,
                    Tags = tags,
                }
            );
            stream.Position = 0;
            return ProtoBuf.Serializer.Deserialize<ImmutableRecord>(stream);
        }

        private static string Describe(ImmutablePoint value)
        {
            return "("
                + value.X
                + ","
                + value.Y
                + ",'"
                + (value.Label ?? "null")
                + "',"
                + (value.Marks == null ? "null" : value.Marks.Length.ToString())
                + ")";
        }

        /// <summary>
        /// Asserts protobuf-net decodes this package's bytes to the same values it would produce.
        /// </summary>
        /// <remarks>
        /// Byte equality no longer holds: <c>Marks</c> is an <c>int[]</c>, which this package packs
        /// and protobuf-net does not. Interop is the property that actually matters, and it is the
        /// stronger one -- it exercises protobuf-net's decoder rather than only its encoder.
        /// </remarks>
        private static void AssertProtobufNetReadsMine<T>(T value, string context)
        {
            byte[] mine = Parse(Encode(value));

            T theirs;
            using (MemoryStream stream = new MemoryStream(mine))
            {
                theirs = ProtoBuf.Serializer.Deserialize<T>(stream);
            }

            T reference;
            using (MemoryStream stream = new MemoryStream(Parse(OracleHex(value))))
            {
                reference = ProtoBuf.Serializer.Deserialize<T>(stream);
            }

            Assert.AreEqual(OracleHex(reference), OracleHex(theirs), context);
        }

        private static byte[] Parse(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = System.Convert.ToByte(hex.Substring(index * 2, 2), 16);
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

        [ProtoBuf.ProtoContract]
        private sealed class Seed
        {
            [ProtoBuf.ProtoMember(1)]
            public int X;

            [ProtoBuf.ProtoMember(2)]
            public int Y;

            [ProtoBuf.ProtoMember(3)]
            public string Label;

            [ProtoBuf.ProtoMember(4)]
            public int[] Marks;
        }

        [ProtoBuf.ProtoContract]
        private sealed class RecordSeed
        {
            [ProtoBuf.ProtoMember(1)]
            public int Id;

            [ProtoBuf.ProtoMember(2)]
            public string Name;

            [ProtoBuf.ProtoMember(3)]
            public int[] Tags;
        }
    }
}
