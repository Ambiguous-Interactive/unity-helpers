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
    /// Hands the base-class-library value types to the built-in formatters and to protobuf-net,
    /// and fails on any disagreement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both majors produce identical bytes for these types -- measured across the whole sweep this
    /// corpus grew from -- so one differential serves the v2 and v3 processes alike. The edge cases
    /// are where an encoding ladder shows its seams: a value one tick past a whole minute scales
    /// differently from the minute itself, and every boundary here sits on one.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class BclDifferentialTests
    {
        [OneTimeSetUp]
        public void RegisterBclFormatters()
        {
            WProtoBcl.RegisterAll();
        }

        [Test]
        public void EveryBclShapeEncodesExactlyAsProtobufNetDoes()
        {
            int checks = 0;
            foreach (BclScalarContract value in Corpus())
            {
                AssertMatchesOracle(value, ref checks);
            }

            Assert.GreaterOrEqual(
                checks,
                100,
                "The differential corpus shrank; that is a coverage loss"
            );
        }

        [Test]
        public void TheGoldenBytesFromTheOracleSweepHold()
        {
            // Transcribed from the probe that established parity between both majors. Member tags
            // first: When rides field 1, Duration 2, Identifier 3, Amount 4 -- so each vector opens
            // with its key and length prefix.
            Assert.AreEqual(
                "0A040801100F",
                MineHex(new BclScalarContract { When = DateTime.MinValue })
            );
            Assert.AreEqual(
                "0A0908AA8288E187681004",
                MineHex(
                    new BclScalarContract
                    {
                        When = new DateTime(2026, 8, 26, 12, 34, 56, 789, DateTimeKind.Utc),
                    }
                )
            );
            Assert.AreEqual(
                "0A040801100F",
                MineHex(new BclScalarContract { Duration = TimeSpan.Zero })
            );
            Assert.AreEqual(
                "0A040801100F120508C8011004",
                MineHex(new BclScalarContract { Duration = TimeSpan.FromMilliseconds(100) })
            );
            Assert.AreEqual(
                "0A040801100F12050886031002",
                MineHex(new BclScalarContract { Duration = TimeSpan.FromHours(3.25) })
            );
            Assert.AreEqual(
                "0A040801100F",
                MineHex(new BclScalarContract { Identifier = Guid.Empty })
            );
            Assert.AreEqual(
                "0A040801100F220408051802",
                MineHex(new BclScalarContract { Amount = 0.5m })
            );
            Assert.AreEqual(
                "0A040801100F",
                MineHex(new BclScalarContract { Amount = decimal.Negate(decimal.Zero) })
            );
        }

        [Test]
        public void ARepeatedBclMemberTakesTheLastOccurrence()
        {
            byte[] payload = Parse("0A0208020A021003");
            BclScalarContract oracle;
            using (MemoryStream stream = new MemoryStream(payload))
            {
                oracle = ProtoBuf.Serializer.Deserialize<BclScalarContract>(stream);
            }

            WProtoReader reader = new WProtoReader(payload);
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<BclScalarContract>()
                    .TryRead(ref reader, out BclScalarContract restored)
            );
            Assert.AreEqual(new DateTime(WProtoBcl.EpochTicks), oracle.When);
            Assert.AreEqual(oracle.When, restored.When);
        }

        [Test]
        public void DateTimeKindFieldsMatchProtobufNetSemantics()
        {
            Assert.IsTrue(TryRead("1801", WProtoDateTimeFormatter.Instance, out DateTime utcEpoch));
            Assert.AreEqual(new DateTime(WProtoBcl.EpochTicks, DateTimeKind.Utc), utcEpoch);

            Assert.IsTrue(
                TryRead("0801100F1801", WProtoDateTimeFormatter.Instance, out DateTime minimum)
            );
            Assert.AreEqual(DateTime.MinValue, minimum);
            Assert.AreEqual(DateTimeKind.Unspecified, minimum.Kind);

            Assert.IsTrue(TryRead("1802", WProtoTimeSpanFormatter.Instance, out TimeSpan zero));
            Assert.AreEqual(TimeSpan.Zero, zero);

            Assert.IsFalse(TryRead("1803", WProtoDateTimeFormatter.Instance, out DateTime _));
            Assert.IsFalse(TryRead("1803", WProtoTimeSpanFormatter.Instance, out TimeSpan _));
        }

        [Test]
        public void BclRootFormattersMatchProtobufNet()
        {
            AssertRootMatches(
                new DateTime(2026, 1, 1),
                WProtoDateTimeFormatter.Instance,
                "0A0408CCBF02"
            );
            AssertRootMatches(
                TimeSpan.FromHours(3),
                WProtoTimeSpanFormatter.Instance,
                "0A0408061001"
            );
            AssertRootMatches(
                new Guid("12345678-1234-1234-1234-123456789abc"),
                WProtoGuidFormatter.Instance,
                "0A12097856341234123412111234123456789ABC"
            );
            AssertRootMatches(0.5m, WProtoDecimalFormatter.Instance, "0A0408051802");

            // The char root keeps writing even where a member would omit: the zero travels as
            // "08 00", the shape both majors emit for a bare-root code unit. These go through the
            // facade because the root formatters are marshal-registered rather than served by
            // Get<T>() -- which is exactly what AssertRootMatches verifies for the others.
            FacadeRootRoundTrips('A', "0841");
            FacadeRootRoundTrips('\0', "0800");
#if !PROTOBUF_NET_ORACLE_V2
            FacadeRootRoundTrips(
                new Uri("https://EXAMPLE.com/PaTh?q=1"),
                "0A1C68747470733A2F2F4558414D504C452E636F6D2F506154683F713D31"
            );
            FacadeRootRoundTrips(
                new Uri("/relative/path", UriKind.RelativeOrAbsolute),
                "0A0E2F72656C61746976652F70617468"
            );
#endif

            Assert.IsTrue(
                WProtoFacade.TryDeserialize(Parse("0A0208020A021003"), out DateTime lastRootWins)
            );
            Assert.AreEqual(new DateTime(WProtoBcl.EpochTicks), lastRootWins);

            Assert.IsTrue(
                WProtoFacade.TryDeserialize(Parse("0A04080210030A00"), out DateTime emptyLastWins)
            );
            Assert.AreEqual(new DateTime(WProtoBcl.EpochTicks), emptyLastWins);
        }

        /// <summary>
        /// Round-trips one root value through the facade against a transcribed byte vector.
        /// </summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="value">The value.</param>
        /// <param name="rootHex">The exact bytes the oracle writes for this root.</param>
        /// <remarks>
        /// Both directions assert the transcribed vector, so neither side can drift: the writer has
        /// to produce those bytes, and the reader has to accept them back into an equal value.
        /// </remarks>
        private static void FacadeRootRoundTrips<T>(T value, string rootHex)
        {
            byte[] encoded;
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out encoded), typeof(T).Name);
            Assert.AreEqual(rootHex, ToHex(encoded), typeof(T).Name);

            byte[] payload = Parse(rootHex);
            Assert.IsTrue(
                WProtoFacade.TryDeserialize(payload, out T restored),
                typeof(T).Name + " read"
            );
            Assert.AreEqual(value, restored, typeof(T).Name + " value");
        }

        [Test]
        public void BclMapKeysMatchProtobufNet()
        {
            DateTime date = new DateTime(2026, 1, 1);
            TimeSpan duration = TimeSpan.FromHours(3);
            Guid identifier = new Guid("12345678-1234-1234-1234-123456789abc");
            const decimal amount = 0.5m;
            BclKeyContract value = new BclKeyContract
            {
                ByDate = new Dictionary<DateTime, int> { { date, 7 } },
                ByDuration = new Dictionary<TimeSpan, int> { { duration, 7 } },
                ByIdentifier = new Dictionary<Guid, int> { { identifier, 7 } },
                ByAmount = new Dictionary<decimal, int> { { amount, 7 } },
                ByCode = new Dictionary<char, int> { { 'A', 7 }, { '\u00E9', 0 } },
            };

            string oracle = OracleHex(value);
            Assert.AreEqual(oracle, MineHex(value));

            WProtoReader reader = new WProtoReader(Parse(oracle));
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<BclKeyContract>()
                    .TryRead(ref reader, out BclKeyContract restored)
            );
            Assert.AreEqual(7, restored.ByDate[date]);
            Assert.AreEqual(7, restored.ByDuration[duration]);
            Assert.AreEqual(7, restored.ByIdentifier[identifier]);
            Assert.AreEqual(7, restored.ByAmount[amount]);
            Assert.AreEqual(7, restored.ByCode['A']);
            Assert.AreEqual(0, restored.ByCode['\u00E9']);
        }

        [Test]
        public void MalformedBclPayloadsAreRefusedAndMismatchedFieldsAreSkippedCleanly()
        {
            Assert.IsFalse(
                TryRead(
                    "088080808080808080808000",
                    WProtoDateTimeFormatter.Instance,
                    out DateTime _
                ),
                "overlong DateTime varint"
            );
            Assert.IsFalse(
                TryRead("1006", WProtoTimeSpanFormatter.Instance, out TimeSpan _),
                "unknown TimeSpan scale"
            );
            Assert.IsFalse(
                TryRead("0804100F", WProtoTimeSpanFormatter.Instance, out TimeSpan _),
                "invalid MinMax sentinel"
            );
            Assert.IsFalse(
                TryRead(
                    "08FEFFFFFFFFFFFFFFFF011005",
                    WProtoDateTimeFormatter.Instance,
                    out DateTime _
                ),
                "DateTime tick overflow"
            );
            Assert.IsFalse(
                TryRead("183A", WProtoDecimalFormatter.Instance, out decimal _),
                "decimal scale above 28"
            );
            Assert.IsTrue(
                TryRead("0D00000000", WProtoDateTimeFormatter.Instance, out DateTime mismatched),
                "a valid field with the wrong wire type is an unknown field"
            );
            Assert.AreEqual(new DateTime(WProtoBcl.EpochTicks), mismatched);
            Assert.IsFalse(
                TryRead("0F", WProtoDateTimeFormatter.Instance, out DateTime _),
                "invalid DateTime wire type"
            );
            Assert.IsFalse(
                TryRead("090102", WProtoGuidFormatter.Instance, out Guid _),
                "truncated Guid fixed64"
            );
            Assert.IsFalse(
                TryRead("FFFE", WProtoUriFormatter.Instance, out Uri _),
                "invalid UTF-8 inside a Uri region"
            );
            Assert.IsFalse(
                TryRead("", WProtoUriFormatter.Instance, out Uri _),
                "an empty Uri region refuses rather than manufacturing a value"
            );
            Assert.IsFalse(
                TryRead(
                    "68747470733A2F2F4558414D504C452E636F6D3AEFBFBD2F",
                    WProtoUriFormatter.Instance,
                    out Uri _
                ),
                "text that no Uri constructor accepts is refused, not defaulted"
            );
            Assert.Throws<InvalidOperationException>(
                () => WProtoFacade.TryDeserialize(Parse("0A050801"), out DateTime _),
                "the facade reports an owned malformed root with its documented exception"
            );
        }

        private static IEnumerable<BclScalarContract> Corpus()
        {
            DateTime[] dates =
            {
                DateTime.MinValue,
                DateTime.MaxValue,
                new DateTime(WProtoBcl.EpochTicks),
                new DateTime(WProtoBcl.EpochTicks - 1),
                new DateTime(WProtoBcl.EpochTicks + 1),
                new DateTime(2026, 8, 26, 12, 34, 56, DateTimeKind.Unspecified),
                new DateTime(2026, 8, 26, 12, 34, 56, 789, DateTimeKind.Utc),
                new DateTime(9999, 12, 31, 23, 59, 59, DateTimeKind.Utc),
                new DateTime(1601, 5, 9, 0, 0, 0, DateTimeKind.Utc),
            };

            TimeSpan[] durations =
            {
                TimeSpan.Zero,
                TimeSpan.FromTicks(1),
                TimeSpan.FromTicks(-1),
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromSeconds(-1.5),
                TimeSpan.FromHours(3.25),
                TimeSpan.FromHours(-3.25),
                TimeSpan.FromDays(12345),
                TimeSpan.FromDays(-2),
                TimeSpan.FromMinutes(90),
                TimeSpan.FromMinutes(-91),
                TimeSpan.FromSeconds(2),
                TimeSpan.MaxValue,
                TimeSpan.MinValue,
                TimeSpan.FromTicks(long.MaxValue / 2 + 1),
            };

            Guid[] identifiers =
            {
                Guid.Empty,
                new Guid("12345678-1234-1234-1234-123456789abc"),
                new Guid("ffffffff-eeee-dddd-cccc-bbbb99998888"),
                new Guid("00000000-0000-0000-0000-000000000001"),
            };

            decimal[] amounts =
            {
                0m,
                decimal.Negate(decimal.Zero),
                0.5m,
                -0.25m,
                1234567.891m,
                -1234567.891m,
                0.000001m,
                0.0000000000000000000000000001m,
                79228162514264337593543950335m,
                -79228162514264337593543950335m,
                1234567890123456789012345678m,
                -7.9228162514264337593543950335m,
            };

            char[] codes =
            {
                '\0',
                'A',
                '\u00E9',
                '\u07FF',
                '\u0800',
                '\uD800',
                '\uDFFF',
                '\uFFFF',
            };

            foreach (char code in codes)
            {
                yield return new BclScalarContract { Code = code };
            }

#if !PROTOBUF_NET_ORACLE_V2
            Uri[] sources = SourceCorpus();
            foreach (Uri source in sources)
            {
                yield return new BclScalarContract { Source = source };
            }
#endif

            foreach (DateTime when in dates)
            {
                yield return new BclScalarContract { When = when };
            }

            foreach (TimeSpan duration in durations)
            {
                yield return new BclScalarContract { Duration = duration };
            }

            foreach (Guid identifier in identifiers)
            {
                yield return new BclScalarContract { Identifier = identifier };
            }

            foreach (decimal amount in amounts)
            {
                yield return new BclScalarContract { Amount = amount };
            }

            Random random = new Random(20260826);
            for (int index = 0; index < 60; index++)
            {
                long ticks = (long)random.Next(-(1 << 24), 1 << 24) * random.Next(1 << 10);
                yield return new BclScalarContract
                {
                    When = new DateTime(ticks + WProtoBcl.EpochTicks),
                    Duration = TimeSpan.FromTicks(ticks),
                    Identifier = NewRandomGuid(random),
                    Amount = random.Next(-1000000000, 1000000000) * DivisorOf(random),
                    Code = (char)random.Next(0, 1 << 16),
#if !PROTOBUF_NET_ORACLE_V2
                    Source = SourceAt(random, index),
#endif
                    NullableWhen =
                        index % 3 == 0 ? null : new DateTime?(dates[index % dates.Length]),
                    Timeline = BuildTimeline(random, index),
                    CodePoints = BuildCodePoints(random, index),
                    DurationsByName = BuildDurations(random, index),
                };
            }
        }

        /// <summary>
        /// The Uri values the differential sweeps, chosen so each wire-relevant quirk appears once.
        /// </summary>
        /// <remarks>
        /// Letter case and escapes prove which string property reached the bytes; the relative form
        /// proves <see cref="UriKind.RelativeOrAbsolute"/> reading; the port-omitted form survives
        /// round trips under <see cref="Uri.Equals"/> only through its original spelling.
        /// </remarks>
        private static Uri[] SourceCorpus()
        {
            return new[]
            {
                new Uri("https://EXAMPLE.com/PaTh?q=1"),
                new Uri("http://example.com/%41%2Fb"),
                new Uri("http://example.com/\u00E9\u4E2D"),
                new Uri("/relative/path", UriKind.RelativeOrAbsolute),
                new Uri("//hostless/path", UriKind.RelativeOrAbsolute),
                new Uri("mailto:user@example.com"),
            };
        }

        private static Uri SourceAt(Random random, int index)
        {
            if (index % 4 == 3)
            {
                return null;
            }

            return SourceCorpus()[index % SourceCorpus().Length];
        }

        private static List<char> BuildCodePoints(Random random, int index)
        {
            if (index % 6 == 5)
            {
                return null;
            }

            List<char> codePoints = new List<char>();
            for (int entry = 0; entry <= index % 4; entry++)
            {
                codePoints.Add((char)random.Next(0, 1 << 16));
            }

            return codePoints;
        }

        private static decimal DivisorOf(Random random)
        {
            return new decimal(1, 0, 0, false, (byte)random.Next(0, 29));
        }

        private static Guid NewRandomGuid(Random random)
        {
            byte[] bytes = new byte[16];
            random.NextBytes(bytes);
            return new Guid(bytes);
        }

        private static List<DateTime> BuildTimeline(Random random, int index)
        {
            if (index % 4 == 0)
            {
                return null;
            }

            List<DateTime> timeline = new List<DateTime>();
            for (int entry = 0; entry <= index % 3; entry++)
            {
                timeline.Add(new DateTime(WProtoBcl.EpochTicks + random.Next(1 << 30) * 10000L));
            }

            return timeline;
        }

        private static Dictionary<string, TimeSpan> BuildDurations(Random random, int index)
        {
            if (index % 5 == 0)
            {
                return null;
            }

            Dictionary<string, TimeSpan> durations = new Dictionary<string, TimeSpan>();
            for (int entry = 0; entry <= index % 3; entry++)
            {
                durations["k" + entry] = TimeSpan.FromTicks(random.Next(1 << 28) * 10000L);
            }

            return durations;
        }

        private static void AssertMatchesOracle(BclScalarContract value, ref int checks)
        {
            checks++;
            string oracle = OracleHex(value);
            string mine = MineHex(value);

            byte[] theirBytes = Parse(oracle);
            byte[] myBytes = Parse(mine);
            Assert.AreEqual(oracle, mine, "the write side diverged from protobuf-net");

            BclScalarContract theirsFromTheirs;
            using (MemoryStream stream = new MemoryStream(theirBytes))
            {
                theirsFromTheirs = ProtoBuf.Serializer.Deserialize<BclScalarContract>(stream);
            }

            // Their bytes, our decoder.
            WProtoReader theirReader = new WProtoReader(theirBytes);
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<BclScalarContract>()
                    .TryRead(ref theirReader, out BclScalarContract oursFromTheirs),
                "could not read protobuf-net's bytes: " + oracle
            );
            AssertSameValue(theirsFromTheirs, oursFromTheirs, oracle);

            // Our bytes, their decoder.
            BclScalarContract theirsFromMine;
            using (MemoryStream stream = new MemoryStream(myBytes))
            {
                theirsFromMine = ProtoBuf.Serializer.Deserialize<BclScalarContract>(stream);
            }
            AssertSameValue(theirsFromTheirs, theirsFromMine, mine);

            // Our own round trip.
            WProtoReader mineReader = new WProtoReader(myBytes);
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<BclScalarContract>()
                    .TryRead(ref mineReader, out BclScalarContract oursFromOurs),
                "could not read my own bytes: " + mine
            );
            AssertSameValue(theirsFromTheirs, oursFromOurs, mine);
        }

        private static void AssertSameValue(
            BclScalarContract expected,
            BclScalarContract actual,
            string context
        )
        {
            // A default(DateTime) reads back as MinValue under both serializers -- the wire form
            // carries no kind, so absence of the field and the sentinel share an encoding.
            Assert.AreEqual(expected.When, actual.When, context + " When");
            Assert.AreEqual(expected.Duration, actual.Duration, context + " Duration");
            Assert.AreEqual(expected.Identifier, actual.Identifier, context + " Identifier");
            Assert.AreEqual(expected.Amount, actual.Amount, context + " Amount");
            Assert.AreEqual(expected.Code, actual.Code, context + " Code");

            CollectionAssert.AreEqual(
                expected.CodePoints,
                actual.CodePoints,
                context + " CodePoints"
            );

#if !PROTOBUF_NET_ORACLE_V2
            if (expected.Source == null || actual.Source == null)
            {
                Assert.IsTrue(expected.Source == null, context + " Source null only");
                Assert.IsTrue(actual.Source == null, context + " Source null only");
            }
            else
            {
                // OriginalString rather than Uri.Equals: the equality the wire cares about is the
                // spelling that produced these bytes, and Uri.Equals treats some distinct
                // spellings as one value.
                Assert.AreEqual(
                    expected.Source.OriginalString,
                    actual.Source.OriginalString,
                    context + " Source"
                );
                Assert.AreEqual(
                    expected.Source.IsAbsoluteUri,
                    actual.Source.IsAbsoluteUri,
                    context + " Source kind"
                );
            }
#endif
            Assert.AreEqual(
                expected.NullableWhen.HasValue,
                actual.NullableWhen.HasValue,
                context + " NullableWhen presence"
            );
            if (expected.NullableWhen.HasValue)
            {
                Assert.AreEqual(
                    expected.NullableWhen.Value,
                    actual.NullableWhen.Value,
                    context + " NullableWhen"
                );
            }

            if (expected.Timeline == null || actual.Timeline == null)
            {
                Assert.IsTrue(expected.Timeline == null, context + " Timeline null only");
                Assert.IsTrue(actual.Timeline == null, context + " Timeline null only");
            }
            else
            {
                CollectionAssert.AreEqual(
                    expected.Timeline,
                    actual.Timeline,
                    context + " Timeline"
                );
            }

            if (expected.DurationsByName == null || actual.DurationsByName == null)
            {
                Assert.IsTrue(
                    expected.DurationsByName == null,
                    context + " DurationsByName null only"
                );
                Assert.IsTrue(
                    actual.DurationsByName == null,
                    context + " DurationsByName null only"
                );
            }
            else
            {
                Assert.AreEqual(
                    expected.DurationsByName.Count,
                    actual.DurationsByName.Count,
                    context + " DurationsByName count"
                );
                foreach (KeyValuePair<string, TimeSpan> pair in expected.DurationsByName)
                {
                    Assert.IsTrue(
                        actual.DurationsByName.TryGetValue(pair.Key, out TimeSpan duration),
                        context + " DurationsByName missing key " + pair.Key
                    );
                    Assert.AreEqual(
                        pair.Value,
                        duration,
                        context + " DurationsByName[" + pair.Key + "]"
                    );
                }
            }
        }

        private static string OracleHex<T>(T value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(stream, value);
                return ToHex(stream.ToArray());
            }
        }

        private static string MineHex<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(buffer.Length, writer.Position, "Measure disagreed with Write");
            return ToHex(buffer);
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

        private static byte[] Parse(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            return bytes;
        }

        private static bool TryRead<T>(string hex, IWProtoFormatter<T> formatter, out T value)
        {
            WProtoReader reader = new WProtoReader(Parse(hex));
            return formatter.TryRead(ref reader, out value);
        }

        private static void AssertRootMatches<T>(
            T value,
            IWProtoFormatter<T> formatter,
            string rootHex
        )
        {
            Assert.IsTrue(
                ReferenceEquals(formatter, WProtoFormatterProvider.Get<T>()),
                typeof(T).Name
            );
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out byte[] encoded), typeof(T).Name);
            Assert.AreEqual(rootHex, ToHex(encoded), typeof(T).Name);
            Assert.IsTrue(
                WProtoFacade.TryDeserialize(Parse(rootHex), out T restored),
                typeof(T).Name
            );
            Assert.AreEqual(rootHex, OracleHex(restored), typeof(T).Name);
        }
    }
}
