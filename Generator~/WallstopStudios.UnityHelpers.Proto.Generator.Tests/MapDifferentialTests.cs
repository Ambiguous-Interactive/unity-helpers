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
    /// Pins map-shaped members against protobuf-net 3.2.56.
    /// </summary>
    /// <remarks>
    /// A protobuf map is a repeated <b>entry message</b> — key at field 1, value at field 2 — not a
    /// repeated value, which is why a dictionary could not ride the collection path. The rule that
    /// is easy to get wrong is the second one: the entry obeys ordinary default-omission, so
    /// <c>{"a": 0}</c> carries only its key.
    /// </remarks>
    [TestFixture]
    public sealed class MapDifferentialTests
    {
        [Test]
        public void AnEntryIsAMessageWithTheKeyAtOneAndTheValueAtTwo()
        {
            Assert.AreEqual(
                "0A050A01611001",
                Encode(Bare(c => c.ByName = new Dictionary<string, int> { { "a", 1 } }))
            );
        }

        [Test]
        public void AValueEqualToItsDefaultIsOmittedFromTheEntry()
        {
            // The counter-intuitive one, and the reason the entry cannot be emitted as two
            // unconditional fields: the entry is a message, and its members obey the same omission
            // rules as any other. An empty-string KEY is still written, because only null is absent.
            Assert.AreEqual(
                "0A030A0161",
                Encode(Bare(c => c.ByName = new Dictionary<string, int> { { "a", 0 } }))
            );
            Assert.AreEqual(
                "0A040A001001",
                Encode(Bare(c => c.ByName = new Dictionary<string, int> { { string.Empty, 1 } }))
            );
            // The value here is a STRUCT sub-message, and the nested-contract rule says one is
            // written even at default (`12 00`) while a null reference is omitted. Rather than
            // restate that from memory, the oracle is asked.
            MapContract structDefault = Bare(c =>
                c.ById = new Dictionary<int, Outer.Point> { { 7, default } }
            );
            Assert.AreEqual(OracleHex(structDefault), Encode(structDefault));
        }

        [Test]
        public void EveryMapShapeMatchesTheOracleByteForByte()
        {
            MapContract[] values =
            {
                Bare(),
                Bare(c => c.ByName = new Dictionary<string, int>()),
                Bare(c => c.ByName = new Dictionary<string, int> { { "a", 1 } }),
                Bare(c => c.ByName = new Dictionary<string, int> { { "a", 0 } }),
                Bare(c => c.ByName = new Dictionary<string, int> { { string.Empty, -1 } }),
                Bare(c =>
                    c.ById = new Dictionary<int, Outer.Point>
                    {
                        {
                            7,
                            new Outer.Point { X = 1, Y = 2 }
                        },
                    }
                ),
                Bare(c => c.ById = new Dictionary<int, Outer.Point> { { 0, default } }),
                Bare(c =>
                    c.Sorted = new SortedDictionary<string, string>
                    {
                        { "a", "x" },
                        { "b", string.Empty },
                    }
                ),
                Bare(c =>
                {
                    c.ByName = new Dictionary<string, int> { { "k", 5 } };
                    c.Sorted = new SortedDictionary<string, string> { { "z", "w" } };
                }),
            };

            foreach (MapContract value in values)
            {
                Assert.AreEqual(OracleHex(value), Encode(value), Describe(value));
            }
        }

        [Test]
        public void AMapRoundTrips()
        {
            MapContract original = Bare(c =>
            {
                c.ByName = new Dictionary<string, int> { { "a", 1 }, { "b", 0 } };
                c.ById = new Dictionary<int, Outer.Point>
                {
                    {
                        7,
                        new Outer.Point { X = 1 }
                    },
                };
                c.Sorted = new SortedDictionary<string, string> { { "k", "v" } };
            });

            MapContract restored = RoundTrip(original);

            CollectionAssert.AreEquivalent(original.ByName, restored.ByName);
            Assert.AreEqual(1, restored.ById.Count);
            Assert.AreEqual(1, restored.ById[7].X);
            CollectionAssert.AreEquivalent(original.Sorted, restored.Sorted);
        }

        [Test]
        public void AnEmptyMapDoesNotSurviveARoundTrip()
        {
            // Same as any repeated field: nothing on the wire separates empty from absent, so an
            // empty dictionary comes back as whatever the constructor left behind.
            Assert.AreEqual(
                string.Empty,
                Encode(Bare(c => c.ByName = new Dictionary<string, int>()))
            );
            Assert.IsNull(RoundTrip(Bare(c => c.ByName = new Dictionary<string, int>())).ByName);
        }

        [Test]
        public void ReadingMergesIntoTheConstructorsMapUnlessOverwriteListIsSet()
        {
            // Field 5 merges, field 4 replaces; both constructors seed {"seed": 9}.
            MapContract decoded = Decode("2207" + "0A036162631001" + "2A07" + "0A036162631001");

            CollectionAssert.AreEquivalent(
                new Dictionary<string, int> { { "abc", 1 } },
                decoded.Overwritten
            );
            CollectionAssert.AreEquivalent(
                new Dictionary<string, int> { { "seed", 9 }, { "abc", 1 } },
                decoded.Merged
            );
        }

        [Test]
        public void AbsentMapsLeaveTheConstructorsValuesAlone()
        {
            MapContract decoded = Decode("0A050A01611001");

            CollectionAssert.AreEquivalent(
                new Dictionary<string, int> { { "seed", 9 } },
                decoded.Overwritten
            );
            CollectionAssert.AreEquivalent(
                new Dictionary<string, int> { { "seed", 9 } },
                decoded.Merged
            );
        }

        [Test]
        public void ARepeatedKeyIsLastWinsRatherThanAThrow()
        {
            // `Add` would throw on the second occurrence, and a reader that throws on a hostile
            // payload is a worse failure than one that decides. protobuf-net also takes the last.
            string twice = "0A050A01611001" + "0A050A01611002";

            using (MemoryStream stream = new MemoryStream(Parse(twice)))
            {
                MapContract oracle = ProtoBuf.Serializer.Deserialize<MapContract>(stream);
                Assert.AreEqual(2, oracle.ByName["a"]);
            }

            Assert.AreEqual(2, Decode(twice).ByName["a"]);
        }

        [Test]
        public void AnEntryMissingItsKeyOrValueDecodesToTheProtoDefault()
        {
            // A payload can carry either half alone. The missing half is the key type's PROTO
            // default, which for a string is "" and not null -- measured, protobuf-net reads
            // `0A 02 10 01` as {"": 1}. Decoding it as null instead throws inside
            // Dictionary<string, V>, which is an unhandled exception out of a reader handed
            // ordinary bytes.
            Assert.AreEqual(0, Decode("0A030A0161").ByName["a"]);
            Assert.AreEqual(1, Decode("0A02" + "1001").ByName[string.Empty]);
            Assert.AreEqual(0, Decode("0A00").ByName[string.Empty]);

            foreach (string hex in new[] { "0A021001", "0A00" })
            {
                using (MemoryStream stream = new MemoryStream(Parse(hex)))
                {
                    MapContract oracle = ProtoBuf.Serializer.Deserialize<MapContract>(stream);
                    CollectionAssert.AreEquivalent(oracle.ByName, Decode(hex).ByName, hex);
                }
            }
        }

        [Test]
        public void MeasurePredictsWriteExactlyForEveryMapShape()
        {
            MapContract[] values =
            {
                Bare(c => c.ByName = new Dictionary<string, int> { { new string('k', 200), 1 } }),
                Bare(c =>
                    c.ById = new Dictionary<int, Outer.Point>
                    {
                        {
                            int.MinValue,
                            new Outer.Point { X = int.MaxValue }
                        },
                    }
                ),
            };

            IWProtoFormatter<MapContract> formatter = WProtoFormatterProvider.Get<MapContract>();
            foreach (MapContract value in values)
            {
                int predicted = formatter.Measure(value);
                byte[] buffer = new byte[predicted];
                WProtoWriter writer = new WProtoWriter(buffer);
                Assert.IsTrue(formatter.Write(ref writer, value));
                Assert.AreEqual(predicted, writer.Position);
            }
        }

        private static MapContract Bare(Action<MapContract> configure = null)
        {
            // The seeded members are cleared so a case can isolate one map at a time; their own
            // behaviour has its own tests.
            MapContract value = new MapContract { Overwritten = null, Merged = null };
            configure?.Invoke(value);
            return value;
        }

        private static string Describe(MapContract value)
        {
            return "ByName="
                + (value.ByName == null ? "null" : value.ByName.Count.ToString())
                + " ById="
                + (value.ById == null ? "null" : value.ById.Count.ToString())
                + " Sorted="
                + (value.Sorted == null ? "null" : value.Sorted.Count.ToString());
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

        private static MapContract Decode(string hex)
        {
            WProtoReader reader = new WProtoReader(Parse(hex));
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<MapContract>()
                    .TryRead(ref reader, out MapContract value),
                hex
            );
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

        [Test]
        public void AHookedMapValueRunsItsSerializationHookOnce()
        {
            // The rule the whole Measure/Write split exists to enforce: a before-serialization hook
            // runs exactly once per serialize. A hook that projects live state into serialized
            // members is idempotent by luck; one that rents a pooled buffer -- the shape CyclicBuffer
            // in this package actually uses -- leaks the first lease when it runs twice.
            //
            // Map entries were sized in BOTH Measure and Write, and sizing a contract value calls its
            // Measure, so a hooked value ran its hook once per pass instead of once per serialize.
            HookedContract value = new HookedContract { Value = 7 };
            HookedMapContract contract = new HookedMapContract
            {
                ById = new Dictionary<int, HookedContract> { { 1, value } },
            };

            IWProtoFormatter<HookedMapContract> formatter =
                WProtoFormatterProvider.Get<HookedMapContract>();
            byte[] buffer = new byte[formatter.Measure(contract)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, contract));

            Assert.AreEqual(
                1,
                value.Trace.FindAll(entry => entry == "OnBeforeSerialization").Count,
                "before-serialization ran " + string.Join(", ", value.Trace)
            );
        }

        [Test]
        public void KeysTheSpecForbidsMatchTheOracleRatherThanBeingRefused()
        {
            // protobuf restricts map keys to the integral types, bool and string. protobuf-net does
            // not, and THIS package's contract is byte-compatibility with protobuf-net -- so a float,
            // double or enum key is encoded rather than rejected, and these are the bytes it has to
            // produce. Measured, then asserted literally so a change is visible here.
            Assert.AreEqual(
                "0A070D0000C03F1002",
                Encode(
                    new ExoticKeyContract { ByFloat = new Dictionary<float, int> { { 1.5f, 2 } } }
                )
            );
            Assert.AreEqual(
                "120B09000000000000F83F1002",
                Encode(
                    new ExoticKeyContract { ByDouble = new Dictionary<double, int> { { 1.5, 2 } } }
                )
            );
            Assert.AreEqual(
                "1A0408071002",
                Encode(
                    new ExoticKeyContract
                    {
                        ByEnum = new Dictionary<MapKeyKind, int> { { MapKeyKind.Other, 2 } },
                    }
                )
            );
            Assert.AreEqual(
                "220408011002",
                Encode(new ExoticKeyContract { ByBool = new Dictionary<bool, int> { { true, 2 } } })
            );
        }
    }
}
