// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*
    WUH010 is suppressed for this file: its subject is the WallstopProto map contract, whose round-tripped maps are read through the indexer the contract promises.
    Rewriting those reads through TryGetValue would delete what they assert. Everywhere the
    indexer is incidental, tests read through DictionaryAssertions.ValueFor instead (#653).
*/
#pragma warning disable WUH010

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
        private static byte[] Parse(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int index = 0; index < bytes.Length; ++index)
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
            Assert.AreEqual(buffer.Length, writer.Position, "Measure disagreed with Write");

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

        private static IEnumerable<TestCaseData> MessageMapEntryCases()
        {
            yield return new TestCaseData("120A08011202080712021009", 1, 7, 9, false).SetName(
                "MessageEntry.SplitFields.Direct"
            );
            yield return new TestCaseData("120A08011202080712021009", 1, 7, 9, true).SetName(
                "MessageEntry.SplitFields.Facade"
            );
            yield return new TestCaseData("120A08011202100912020807", 1, 7, 9, false).SetName(
                "MessageEntry.ReversedFields.Direct"
            );
            yield return new TestCaseData("120A08011202100912020807", 1, 7, 9, true).SetName(
                "MessageEntry.ReversedFields.Facade"
            );
            yield return new TestCaseData("12080801120208071200", 1, 7, 0, false).SetName(
                "MessageEntry.EmptyLast.Direct"
            );
            yield return new TestCaseData("12080801120208071200", 1, 7, 0, true).SetName(
                "MessageEntry.EmptyLast.Facade"
            );
            yield return new TestCaseData("12080801120012020807", 1, 7, 0, false).SetName(
                "MessageEntry.EmptyFirst.Direct"
            );
            yield return new TestCaseData("12080801120012020807", 1, 7, 0, true).SetName(
                "MessageEntry.EmptyFirst.Facade"
            );
            yield return new TestCaseData("120C080112020807180412021009", 1, 7, 9, false).SetName(
                "MessageEntry.UnknownInterleaved.Direct"
            );
            yield return new TestCaseData("120C080112020807180412021009", 1, 7, 9, true).SetName(
                "MessageEntry.UnknownInterleaved.Facade"
            );
            yield return new TestCaseData("120C080112040807100912020805", 1, 5, 9, false).SetName(
                "MessageEntry.LaterScalarWins.Direct"
            );
            yield return new TestCaseData("120C080112040807100912020805", 1, 5, 9, true).SetName(
                "MessageEntry.LaterScalarWins.Facade"
            );
            yield return new TestCaseData(
                "12060801120208071206080112021009",
                1,
                0,
                9,
                false
            ).SetName("MessageEntry.LaterEntryReplaces.Direct");
            yield return new TestCaseData(
                "12060801120208071206080112021009",
                1,
                0,
                9,
                true
            ).SetName("MessageEntry.LaterEntryReplaces.Facade");
            yield return new TestCaseData("12020801", 1, 0, 0, false).SetName(
                "MessageEntry.AbsentValue.Direct"
            );
            yield return new TestCaseData("12020801", 1, 0, 0, true).SetName(
                "MessageEntry.AbsentValue.Facade"
            );
            yield return new TestCaseData("1200", 0, 0, 0, false).SetName(
                "MessageEntry.AbsentKeyAndValue.Direct"
            );
            yield return new TestCaseData("1200", 0, 0, 0, true).SetName(
                "MessageEntry.AbsentKeyAndValue.Facade"
            );
        }

        [TestCase("12080801120108120107")]
        [TestCase("12090801120208071201")]
        [TestCase("1209080112020807120108")]
        [TestCase("120A08011202080712020000")]
        [TestCase("120B08011202080712041080")]
        public void MalformedMessageMapValuesAreRefused(string hex)
        {
            byte[] payload = Parse(hex);
            WProtoReader reader = new WProtoReader(payload);
            Assert.IsFalse(
                WProtoFormatterProvider
                    .Get<WProtoMapContract>()
                    .TryRead(ref reader, out WProtoMapContract direct)
            );
            Assert.IsTrue(direct == null);
            Assert.Throws<InvalidOperationException>(() =>
                WProtoFacade.TryDeserialize(payload, out WProtoMapContract _)
            );
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, true)]
        public void MessageMapMergingChargesBothEntryAndValueDepth(int maximumDepth, bool expected)
        {
            byte[] payload = Parse("120A08011202080712021009");
            WProtoReader reader = new WProtoReader(
                payload,
                new WProtoReadLimits(maximumNestingDepth: maximumDepth)
            );
            Assert.AreEqual(
                expected,
                WProtoFormatterProvider
                    .Get<WProtoMapContract>()
                    .TryRead(ref reader, out WProtoMapContract read)
            );
            if (expected)
            {
                Assert.IsTrue(read != null);
                Assert.AreEqual(7, read.ById[1].X);
                Assert.AreEqual(9, read.ById[1].Y);
                Assert.IsFalse(reader.Malformed);
                Assert.IsTrue(reader.End);
            }
            else
            {
                Assert.IsTrue(read == null);
            }
        }

        [TestCaseSource(nameof(MessageMapEntryCases))]
        public void MessageMapEntryFieldsMergeWithoutMergingSeparateEntries(
            string hex,
            int key,
            int expectedX,
            int expectedY,
            bool facade
        )
        {
            byte[] payload = Parse(hex);
            WProtoMapContract read;
            if (facade)
            {
                Assert.IsTrue(WProtoFacade.TryDeserialize(payload, out read));
            }
            else
            {
                WProtoReader reader = new WProtoReader(payload);
                Assert.IsTrue(
                    WProtoFormatterProvider.Get<WProtoMapContract>().TryRead(ref reader, out read)
                );
                Assert.IsFalse(reader.Malformed);
                Assert.IsTrue(reader.End);
            }
            Assert.AreEqual(1, read.ById.Count);
            Assert.AreEqual(expectedX, read.ById[key].X);
            Assert.AreEqual(expectedY, read.ById[key].Y);
        }

        [TestCase(WProtoButtonType.None, 0d, "0A020800")]
        [TestCase(WProtoButtonType.None, 1d, "0A0B080011000000000000F03F")]
        [TestCase(WProtoButtonType.Primary, 0d, "0A020801")]
        [TestCase(WProtoButtonType.Primary, 1d, "0A0B080111000000000000F03F")]
        public void EnumMapKeysMatchV3GoldenBytes(
            WProtoButtonType key,
            double value,
            string expected
        )
        {
            WProtoZeroKeyMapContract contract = new WProtoZeroKeyMapContract
            {
                ByEnum = new Dictionary<WProtoButtonType, double> { { key, value } },
            };
#if !ENABLE_IL2CPP
            // protobuf-net tuple-discovery reflection calls an icall IL2CPP lacks.
            using MemoryStream stream = new MemoryStream();
            ProtoBuf.Serializer.Serialize(stream, contract);
            Assert.AreEqual(expected, ToHex(stream.ToArray()));
#endif
            Assert.AreEqual(expected, Encode(contract));
        }

        [TestCase(0, 0d, "1200")]
        [TestCase(0, 1d, "120911000000000000F03F")]
        [TestCase(1, 0d, "12020801")]
        [TestCase(1, 1d, "120B080111000000000000F03F")]
        public void IntegerMapKeysRetainDefaultOmission(int key, double value, string expected)
        {
            WProtoZeroKeyMapContract contract = new WProtoZeroKeyMapContract
            {
                ByInteger = new Dictionary<int, double> { { key, value } },
            };
#if !ENABLE_IL2CPP
            using MemoryStream stream = new MemoryStream();
            ProtoBuf.Serializer.Serialize(stream, contract);
            Assert.AreEqual(expected, ToHex(stream.ToArray()));
#endif
            Assert.AreEqual(expected, Encode(contract));
        }

        [TestCase(0f, "1A0911000000000000F03F")]
        [TestCase(1.5f, "1A0E0D0000C03F11000000000000F03F")]
        public void SingleMapKeysMatchV3GoldenBytes(float key, string expected)
        {
            WProtoZeroKeyMapContract contract = new WProtoZeroKeyMapContract
            {
                BySingle = new Dictionary<float, double> { { key, 1d } },
            };
#if !ENABLE_IL2CPP
            using MemoryStream stream = new MemoryStream();
            ProtoBuf.Serializer.Serialize(stream, contract);
            Assert.AreEqual(expected, ToHex(stream.ToArray()));
#endif
            Assert.AreEqual(expected, Encode(contract));
        }

        [TestCase(0d, "220911000000000000F03F")]
        [TestCase(1.5d, "221209000000000000F83F11000000000000F03F")]
        public void DoubleMapKeysMatchV3GoldenBytes(double key, string expected)
        {
            WProtoZeroKeyMapContract contract = new WProtoZeroKeyMapContract
            {
                ByDouble = new Dictionary<double, double> { { key, 1d } },
            };
#if !ENABLE_IL2CPP
            using MemoryStream stream = new MemoryStream();
            ProtoBuf.Serializer.Serialize(stream, contract);
            Assert.AreEqual(expected, ToHex(stream.ToArray()));
#endif
            Assert.AreEqual(expected, Encode(contract));
        }

        [TestCase("0A00", 0d)]
        [TestCase("0A020800", 0d)]
        [TestCase("0A0911000000000000F03F", 1d)]
        [TestCase("0A0B080011000000000000F03F", 1d)]
        public void EnumMapReadsHistoricalKeylessAndExplicitZeroEntries(string hex, double expected)
        {
            WProtoReader reader = new WProtoReader(Parse(hex));
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<WProtoZeroKeyMapContract>()
                    .TryRead(ref reader, out WProtoZeroKeyMapContract read)
            );
            Assert.AreEqual(1, read.ByEnum.Count);
#if !ENABLE_IL2CPP
            using MemoryStream stream = new MemoryStream(Parse(hex));
            WProtoZeroKeyMapContract oracle =
                ProtoBuf.Serializer.Deserialize<WProtoZeroKeyMapContract>(stream);
            CollectionAssert.AreEquivalent(oracle.ByEnum, read.ByEnum);
#endif
            Assert.AreEqual(expected, read.ByEnum[WProtoButtonType.None]);
        }

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
            // An empty string key is present even when other default entry fields are omitted.
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
            /*
                Protobuf-net restores missing string keys as empty strings; null would throw during dictionary
                insertion.
            */
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

            // Constructor values are null here, isolating replacement versus merge behavior.
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
            /*
                IL2CPP lacks the tuple-discovery icall used by protobuf-net; keep its oracle on editor backends
                and exercise WallstopProto in players.
            */
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
    }
}
