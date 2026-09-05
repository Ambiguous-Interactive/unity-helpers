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
    /// Generates random <b>valid</b> values and asserts both serializers agree about all of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other half of #437. <c>FuzzTests</c> feeds hostile <b>bytes</b> to the reader and asks
    /// that a decode terminate and report failure; this feeds random <b>values</b> to the writer and
    /// asks that protobuf-net and this package produce and accept the same wire. The fixed corpora
    /// in the differential fixtures cover the shapes somebody thought of, which is exactly the set a
    /// conformance bug is least likely to be found in.
    /// </para>
    /// <para>
    /// Equality is <b>re-encoding</b>, not a per-contract field comparison. A comparer per contract
    /// would be long, would need editing for every new member, and would quietly stop covering the
    /// member somebody forgot to add to it. Round-tripping a decode back through the same encoder
    /// asks the question the wire format actually poses -- did this reader recover everything the
    /// writer put there -- and it asks it of every member without naming any.
    /// </para>
    /// <para>
    /// Every case is a fixed seed, and a failure prints the seed, the iteration and both hex
    /// payloads, so a finding is a <c>[TestCase]</c> rather than a story.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class DifferentialFuzzTests
    {
        private const int Iterations = 400;

        [Test]
        public void EveryRandomRepeatedValueEncodesAsProtobufNetDoes()
        {
            RunCorpus(1, seeded => Repeated(seeded), byteIdentical: false);
        }

        [Test]
        public void EveryRandomMapValueEncodesAsProtobufNetDoes()
        {
            /*
             * protobuf-net 2 omits empty string map keys; version 3 and this writer emit them. Compare
             * decoded values across both formats.
             */
#if PROTOBUF_NET_ORACLE_V2
            RunCorpus(2, seeded => Map(seeded), byteIdentical: false);
#else
            RunCorpus(2, seeded => Map(seeded), byteIdentical: true);
#endif
        }

        [Test]
        public void EveryRandomPolymorphicValueEncodesAsProtobufNetDoes()
        {
            RunCorpus(3, seeded => Polymorphic(seeded), byteIdentical: true);
        }

        /// <summary>
        /// Drives one corpus and asserts every property that holds for it.
        /// </summary>
        /// <typeparam name="T">The contract type.</typeparam>
        /// <param name="seed">The fixed seed, printed with any failure.</param>
        /// <param name="make">Produces one random value.</param>
        /// <param name="byteIdentical">
        /// Whether the two encoders must produce the same bytes. False where this package encodes
        /// deliberately differently: a repeated packable scalar is written as one PACKED run here
        /// and as one field per element by protobuf-net, which roughly halves the payload and is
        /// safe because wire compatibility is about what the other side can read. Interop is
        /// asserted either way, and it is the property that matters.
        /// </param>
        private static void RunCorpus<T>(int seed, Func<Random, T> make, bool byteIdentical)
        {
            Random random = new Random(seed);
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                T value = make(random);
                string context = $"seed {seed}, iteration {iteration}";

                string mine = ToHex(Mine(value));
                string theirs = ToHex(Oracle(value));

                if (byteIdentical)
                {
                    Assert.AreEqual(theirs, mine, context + ": the two encoders disagree");
                }

                Assert.AreEqual(
                    theirs,
                    ToHex(Oracle(OracleRead<T>(Parse(mine)))),
                    context + ": protobuf-net did not recover what we wrote from " + mine
                );

                Assert.AreEqual(
                    mine,
                    ToHex(Mine(MineRead<T>(Parse(theirs), context))),
                    context + ": we did not recover what protobuf-net wrote from " + theirs
                );

                Assert.AreEqual(
                    mine,
                    ToHex(Mine(MineRead<T>(Parse(mine), context))),
                    context + ": our own round trip lost something from " + mine
                );
                Assert.AreEqual(
                    theirs,
                    ToHex(Oracle(OracleRead<T>(Parse(theirs)))),
                    context + ": protobuf-net's own round trip lost something from " + theirs
                );
            }
        }

        private static RepeatedContract Repeated(Random random)
        {
            return new RepeatedContract
            {
                Ints = Array(random, () => random.Next(int.MinValue, int.MaxValue)),
                IntList = List(random, () => random.Next(-8, 8)),
                Texts = Array(random, () => Element(random)),
                Doubles = Array(random, () => random.NextDouble() * random.Next(-1000, 1000)),
                Longs = Array(random, () => unchecked((ulong)random.NextInt64())),
                Flags = Array(random, () => random.Next(2) == 0),
                Modes = Array(random, () => (Mode)random.Next(0, 3)),
                Points = Array(
                    random,
                    () => new Outer.Point { X = random.Next(-9, 9), Y = random.Next(-9, 9) }
                ),
                Shorts = Array(random, () => (short)random.Next(short.MinValue, short.MaxValue)),
                PointList = List(
                    random,
                    () => new Outer.Point { X = random.Next(-3, 3), Y = random.Next(-3, 3) }
                ),
                Blobs = Array(random, () => ByteElement(random)),
            };
        }

        private static V2CompatibleMapContract Map(Random random)
        {
            V2CompatibleMapContract value = new V2CompatibleMapContract();
            if (random.Next(4) == 0)
            {
                return value;
            }

            value.Values = new Dictionary<string, int>();
            int entries = random.Next(0, 6);
            for (int index = 0; index < entries; index++)
            {
                value.Values[Text(random) ?? "k" + index] = random.Next(-64, 64);
            }

            return value;
        }

        private static IncludeHolder Polymorphic(Random random)
        {
            return new IncludeHolder { Value = Included(random), Trailer = random.Next(-64, 64) };
        }

        private static IncludeBase Included(Random random)
        {
            int level = random.Next(5);
            IncludeBase value;
            switch (level)
            {
                case 0:
                    return null;
                case 1:
                    value = new IncludeBase();
                    break;
                case 2:
                    value = new IncludeAlpha
                    {
                        AlphaOnly = random.Next(-64, 64),
                        AlphaText = Text(random),
                    };
                    break;
                case 3:
                    value = new IncludeBeta { BetaOnly = random.NextDouble() };
                    break;
                default:
                    value = new IncludeGamma
                    {
                        BetaOnly = random.NextDouble(),
                        GammaOnly = random.Next(2) == 0,
                    };
                    break;
            }

            value.Id = random.Next(-64, 64);
            value.Label = Text(random);
            return value;
        }

        private static T[] Array<T>(Random random, Func<T> element)
        {
            int shape = random.Next(6);
            if (shape == 0)
            {
                return null;
            }

            T[] values = new T[shape == 1 ? 0 : random.Next(1, 6)];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = element();
            }

            return values;
        }

        private static List<T> List<T>(Random random, Func<T> element)
        {
            T[] values = Array(random, element);
            return values == null ? null : new List<T>(values);
        }

        /// <summary>
        /// A string that is never <c>null</c>, for use INSIDE a repeated field.
        /// </summary>
        /// <param name="random">The seeded source.</param>
        /// <returns>An empty or populated string.</returns>
        /// <remarks>
        /// A repeated field is a run of same-numbered fields, and there is no encoding for an absent
        /// value inside one -- the generator refuses a null element rather than inventing an empty
        /// one or silently shortening the collection. That refusal is the contract, so a corpus that
        /// produced null elements would be testing invalid values rather than disagreement.
        /// </remarks>
        private static string Element(Random random)
        {
            return Text(random) ?? string.Empty;
        }

        private static byte[] ByteElement(Random random)
        {
            return Bytes(random) ?? System.Array.Empty<byte>();
        }

        private static string Text(Random random)
        {
            int shape = random.Next(6);
            if (shape == 0)
            {
                return null;
            }

            if (shape == 1)
            {
                return string.Empty;
            }

            // Non-ASCII text distinguishes UTF-8 byte lengths from character counts.
            const string Alphabet = "abzAZ09 _é世😀";
            StringBuilder builder = new StringBuilder();
            int length = random.Next(1, 8);
            for (int index = 0; index < length; index++)
            {
                builder.Append(Alphabet[random.Next(Alphabet.Length)]);
            }

            return builder.ToString();
        }

        private static byte[] Bytes(Random random)
        {
            int shape = random.Next(6);
            if (shape == 0)
            {
                return null;
            }

            byte[] payload = new byte[shape == 1 ? 0 : random.Next(1, 24)];
            random.NextBytes(payload);
            return payload;
        }

        private static byte[] Mine<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            formatter.Write(ref writer, value);
            return buffer;
        }

        private static T MineRead<T>(byte[] payload, string context)
        {
            WProtoReader reader = new WProtoReader(payload);
            Assert.IsTrue(
                WProtoFormatterProvider.Get<T>().TryRead(ref reader, out T value),
                context + ": could not read " + ToHex(payload)
            );
            return value;
        }

        private static byte[] Oracle<T>(T value)
        {
            using MemoryStream stream = new MemoryStream();
            ProtoBuf.Serializer.Serialize(stream, value);
            return stream.ToArray();
        }

        private static T OracleRead<T>(byte[] payload)
        {
            using MemoryStream stream = new MemoryStream(payload);
            return ProtoBuf.Serializer.Deserialize<T>(stream);
        }

        private static byte[] Parse(string hex)
        {
            byte[] payload = new byte[hex.Length / 2];
            for (int index = 0; index < payload.Length; index++)
            {
                payload[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            return payload;
        }

        private static string ToHex(byte[] payload)
        {
            StringBuilder builder = new StringBuilder(payload.Length * 2);
            foreach (byte value in payload)
            {
                builder.Append(value.ToString("X2"));
            }

            return builder.ToString();
        }
    }
}
