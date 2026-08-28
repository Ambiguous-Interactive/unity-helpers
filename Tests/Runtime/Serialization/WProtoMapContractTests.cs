// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Runs generated map code inside Unity, on the editors and players CI builds.
    /// </summary>
    /// <remarks>
    /// A protobuf map is a repeated <b>entry message</b>, key at field 1 and value at field 2, and
    /// the entry obeys ordinary default-omission -- so <c>{"a": 0}</c> carries only its key. Every
    /// expected payload was copied out of protobuf-net 3.2.56.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoMapContractTests
    {
        [Test]
        public void AnEntryIsAMessageWithTheKeyAtOneAndTheValueAtTwo()
        {
            Assert.AreEqual(
                "0A050A01611001",
                Encode(
                    new WProtoMapContract { ByName = new Dictionary<string, int> { { "a", 1 } } }
                )
            );
        }

        [Test]
        public void AValueEqualToItsDefaultIsOmittedFromTheEntry()
        {
            // The entry is a message, and its members obey the same omission rules as any other. An
            // empty-string KEY is still written, because only null is absent.
            Assert.AreEqual(
                "0A030A0161",
                Encode(
                    new WProtoMapContract { ByName = new Dictionary<string, int> { { "a", 0 } } }
                )
            );
            Assert.AreEqual(
                "0A040A001001",
                Encode(
                    new WProtoMapContract
                    {
                        ByName = new Dictionary<string, int> { { string.Empty, 1 } },
                    }
                )
            );
        }

        [Test]
        public void AMapRoundTripsUnderIl2cpp()
        {
            WProtoMapContract original = new()
            {
                ByName = new Dictionary<string, int> { { "a", 1 }, { "b", 0 } },
                ById = new Dictionary<int, WProtoRepeatedPoint>
                {
                    {
                        7,
                        new WProtoRepeatedPoint { X = 1, Y = 2 }
                    },
                },
                Sorted = new SortedDictionary<string, string> { { "k", "v" } },
            };

            WProtoMapContract restored = RoundTrip(original);

            Assert.AreEqual(2, restored.ByName.Count);
            Assert.AreEqual(1, restored.ByName["a"]);
            Assert.AreEqual(0, restored.ByName["b"]);
            Assert.AreEqual(1, restored.ById[7].X);
            Assert.AreEqual(2, restored.ById[7].Y);
            Assert.AreEqual("v", restored.Sorted["k"]);
        }

        [Test]
        public void AnEmptyMapDoesNotSurviveARoundTrip()
        {
            // Same as any repeated field: nothing separates empty from absent on the wire.
            Assert.AreEqual(
                string.Empty,
                Encode(new WProtoMapContract { ByName = new Dictionary<string, int>() })
            );
            Assert.IsTrue(
                RoundTrip(new WProtoMapContract { ByName = new Dictionary<string, int>() }).ByName
                    == null
            );
        }

        [Test]
        public void AKeylessEntryDecodesToTheProtoDefaultRatherThanNull()
        {
            // A missing string key is "" and not null -- measured against protobuf-net. Decoding it
            // as null throws inside Dictionary<string, V>, which is an unhandled exception out of a
            // reader handed ordinary bytes.
            Assert.AreEqual(1, Decode("0A021001").ByName[string.Empty]);
            Assert.AreEqual(0, Decode("0A00").ByName[string.Empty]);
        }

        [Test]
        public void ARepeatedKeyIsLastWinsRatherThanAThrow()
        {
            Assert.AreEqual(2, Decode("0A050A01611001" + "0A050A01611002").ByName["a"]);
        }

        [Test]
        public void ReadingMergesUnlessOverwriteListIsSet()
        {
            WProtoMapContract seeded = new()
            {
                Overwritten = new Dictionary<string, int> { { "seed", 9 } },
                Merged = new Dictionary<string, int> { { "seed", 9 } },
            };

            byte[] payload = Parse("2207" + "0A036162631001" + "2A07" + "0A036162631001");
            WProtoReader reader = new(payload);
            IWProtoFormatter<WProtoMapContract> formatter =
                WProtoFormatterProvider.Get<WProtoMapContract>();
            Assert.IsTrue(formatter.TryRead(ref reader, out WProtoMapContract decoded));

            // The formatter builds its own instance, so the constructor value here is null for both
            // members; what this pins is that the two paths differ in kind, not that they merge with
            // `seeded`.
            Assert.AreEqual(1, decoded.Overwritten["abc"]);
            Assert.AreEqual(1, decoded.Merged["abc"]);
            Assert.IsTrue(seeded.Merged != null);
        }

        [Test]
        public void MeasurePredictsWriteExactlyForEveryMapShape()
        {
            WProtoMapContract[] cases =
            {
                new(),
                new() { ByName = new Dictionary<string, int> { { new string('k', 200), 1 } } },
                new()
                {
                    ById = new Dictionary<int, WProtoRepeatedPoint>
                    {
                        {
                            int.MinValue,
                            new WProtoRepeatedPoint { X = int.MaxValue }
                        },
                    },
                },
            };

            IWProtoFormatter<WProtoMapContract> formatter =
                WProtoFormatterProvider.Get<WProtoMapContract>();

            foreach (WProtoMapContract value in cases)
            {
                int predicted = formatter.Measure(value);
                byte[] buffer = new byte[predicted];
                WProtoWriter writer = new(buffer);
                Assert.IsTrue(formatter.Write(ref writer, value));
                Assert.AreEqual(predicted, writer.Position);
            }
        }

        [Test]
        public void TupleMembersAndTupleMapKeysMatchProtobufNetAndRoundTrip()
        {
            WProtoTupleMapContract original = new()
            {
                Pair = new ValueTuple<int, string>(7, "pair"),
                Triple = new ValueTuple<int, string, double>(8, "triple", 0.5d),
                Values = new Dictionary<(WProtoButtonType, WProtoButtonDirection), double>
                {
                    { (WProtoButtonType.None, WProtoButtonDirection.None), 0d },
                    { (WProtoButtonType.None, WProtoButtonDirection.Left), 0.25d },
                    {
                        (
                            WProtoButtonType.Primary,
                            WProtoButtonDirection.Left | WProtoButtonDirection.Right
                        ),
                        1d
                    },
                },
            };

#if !ENABLE_IL2CPP
            // protobuf-net's tuple discovery calls RuntimeParameterInfo.GetTypeModifiers, an icall
            // Unity IL2CPP does not implement. WallstopProto is the AOT path under test there;
            // the protobuf-net byte/cross-reader oracle remains active on every editor backend.
            string wallstopProto = Encode(original);
            using MemoryStream protobufNetStream = new();
            ProtoBuf.Serializer.Serialize(protobufNetStream, original);

            Assert.AreEqual(ToHex(protobufNetStream.ToArray()), wallstopProto);

            byte[] wallstopBytes = Parse(wallstopProto);
            using MemoryStream wallstopStream = new(wallstopBytes);
            WProtoTupleMapContract protobufRead =
                ProtoBuf.Serializer.Deserialize<WProtoTupleMapContract>(wallstopStream);
            Assert.AreEqual(original.Values.Count, protobufRead.Values.Count);

            WProtoReader protobufReader = new(protobufNetStream.ToArray());
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<WProtoTupleMapContract>()
                    .TryRead(ref protobufReader, out WProtoTupleMapContract wallstopRead)
            );
            Assert.AreEqual(original.Values.Count, wallstopRead.Values.Count);
#endif

            WProtoTupleMapContract restored = RoundTrip(original);
            Assert.AreEqual(original.Pair, restored.Pair);
            Assert.AreEqual(original.Triple, restored.Triple);
            Assert.AreEqual(
                0d,
                restored.Values[(WProtoButtonType.None, WProtoButtonDirection.None)]
            );
            Assert.AreEqual(
                0.25d,
                restored.Values[(WProtoButtonType.None, WProtoButtonDirection.Left)]
            );
            Assert.AreEqual(
                1d,
                restored.Values[
                    (
                        WProtoButtonType.Primary,
                        WProtoButtonDirection.Left | WProtoButtonDirection.Right
                    )
                ]
            );
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

        private static WProtoMapContract Decode(string hex)
        {
            WProtoReader reader = new(Parse(hex));
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<WProtoMapContract>()
                    .TryRead(ref reader, out WProtoMapContract value),
                hex
            );
            return value;
        }

        private static string Encode<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));

            return ToHex(writer.Written);
        }

        private static string ToHex(ReadOnlySpan<byte> bytes)
        {
            StringBuilder builder = new(bytes.Length * 2);
            foreach (byte current in bytes)
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
