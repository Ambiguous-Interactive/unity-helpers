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
    /// Pins map-shaped members against protobuf-net 2.4.9 and 3.2.56.
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
            /*
             * Map entries follow ordinary member omission rules; an empty string key is present rather than
             * null.
             */
            Assert.AreEqual(
                "0A030A0161",
                Encode(Bare(c => c.ByName = new Dictionary<string, int> { { "a", 0 } }))
            );
            Assert.AreEqual(
                "0A040A001001",
                Encode(Bare(c => c.ByName = new Dictionary<string, int> { { string.Empty, 1 } }))
            );

            MapContract structDefault = Bare(c =>
                c.ById = new Dictionary<int, Outer.Point> { { 7, default } }
            );
#if PROTOBUF_NET_ORACLE_V2
            /*
             * Use captured v2 bytes: its oracle cannot reliably prepare this struct map after another path
             * freezes the model.
             */
            const string v2Hex = "12020807";
            Assert.AreEqual(default(Outer.Point), Decode(v2Hex).ById[7]);
#else
            Assert.AreEqual(OracleHex(structDefault), Encode(structDefault));
#endif
        }

        [Test]
        public void EveryMapShapeMatchesTheOracleByteForByte()
        {
#if PROTOBUF_NET_ORACLE_V2
            V2CompatibleMapContract[] values =
            {
                V2Bare(),
                V2Bare(c => c.Values = new Dictionary<string, int>()),
                V2Bare(c => c.Values = new Dictionary<string, int> { { "a", 1 } }),
                V2Bare(c => c.Values = new Dictionary<string, int> { { "a", 0 } }),
                V2Bare(c =>
                {
                    c.Values = new Dictionary<string, int> { { "k", 5 } };
                    c.Sorted = new SortedDictionary<string, string> { { "a", "x" }, { "b", "y" } };
                }),
                new V2CompatibleMapContract
                {
                    Overwritten = new Dictionary<string, int> { { "replacement", 1 } },
                    Merged = new Dictionary<string, int> { { "addition", 2 } },
                },
            };

            foreach (V2CompatibleMapContract value in values)
            {
                Assert.AreEqual(OracleHex(value), Encode(value));
            }
#else
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
#endif
        }

#if PROTOBUF_NET_ORACLE_V2
        [Test]
        public void V2AndV3MapDefaultRulesDivergeButStringMapsCrossRead()
        {
            V2CompatibleMapContract original = V2Bare(c =>
            {
                c.Values = new Dictionary<string, int> { { string.Empty, 1 } };
            });

            string v2Hex = OracleHex(original);
            string currentHex = Encode(original);

            Assert.AreEqual("0A021001", v2Hex, "v2 omits the default-valued string key");
            Assert.AreEqual("0A040A001001", currentHex, "v3 writes an explicit empty string key");

            V2CompatibleMapContract migrated = DecodeV2CompatibleMap(v2Hex);
            Assert.AreEqual(1, migrated.Values[string.Empty]);

            using (MemoryStream stream = new MemoryStream(Parse(currentHex)))
            {
                V2CompatibleMapContract readByV2 =
                    ProtoBuf.Serializer.Deserialize<V2CompatibleMapContract>(stream);
                Assert.AreEqual(1, readByV2.Values[string.Empty]);
            }

            V2CompatibleMapContract emptyStringValue = V2Bare(c =>
            {
                c.Sorted = new SortedDictionary<string, string> { { "b", string.Empty } };
            });
            string v2ValueHex = OracleHex(emptyStringValue);
            string currentValueHex = Encode(emptyStringValue);
            Assert.AreEqual("1A030A0162", v2ValueHex, "v2 omits the default string value");
            Assert.AreEqual(
                "1A050A01621200",
                currentValueHex,
                "v3 writes an explicit empty string value"
            );
            Assert.AreEqual(string.Empty, DecodeV2CompatibleMap(v2ValueHex).Sorted["b"]);

            using (MemoryStream stream = new MemoryStream(Parse(currentValueHex)))
            {
                V2CompatibleMapContract readByV2 =
                    ProtoBuf.Serializer.Deserialize<V2CompatibleMapContract>(stream);
                Assert.AreEqual(string.Empty, readByV2.Sorted["b"]);
            }
        }
#endif

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
            // Repeated fields cannot distinguish empty from absent, so constructor-provided entries survive.
            Assert.AreEqual(
                string.Empty,
                Encode(Bare(c => c.ByName = new Dictionary<string, int>()))
            );
            Assert.IsNull(RoundTrip(Bare(c => c.ByName = new Dictionary<string, int>())).ByName);
        }

        [Test]
        public void ReadingMergesIntoTheConstructorsMapUnlessOverwriteListIsSet()
        {
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
            string twice = "0A050A01611001" + "0A050A01611002";

            using (MemoryStream stream = new MemoryStream(Parse(twice)))
            {
                V2CompatibleMapContract oracle =
                    ProtoBuf.Serializer.Deserialize<V2CompatibleMapContract>(stream);
                Assert.AreEqual(2, oracle.Values["a"]);
            }

            Assert.AreEqual(2, Decode(twice).ByName["a"]);
        }

        [Test]
        public void AnEntryMissingItsKeyOrValueDecodesToTheProtoDefault()
        {
            // Missing string keys use the protobuf empty-string default, not the C# null default.
            Assert.AreEqual(0, Decode("0A030A0161").ByName["a"]);
            Assert.AreEqual(1, Decode("0A02" + "1001").ByName[string.Empty]);
            Assert.AreEqual(0, Decode("0A00").ByName[string.Empty]);

            foreach (string hex in new[] { "0A021001", "0A00" })
            {
                using (MemoryStream stream = new MemoryStream(Parse(hex)))
                {
                    V2CompatibleMapContract oracle =
                        ProtoBuf.Serializer.Deserialize<V2CompatibleMapContract>(stream);
                    CollectionAssert.AreEquivalent(oracle.Values, Decode(hex).ByName, hex);
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
            MapContract value = new MapContract { Overwritten = null, Merged = null };
            configure?.Invoke(value);
            return value;
        }

#if PROTOBUF_NET_ORACLE_V2
        private static V2CompatibleMapContract V2Bare(
            Action<V2CompatibleMapContract> configure = null
        )
        {
            V2CompatibleMapContract value = new V2CompatibleMapContract
            {
                Overwritten = null,
                Merged = null,
            };
            configure?.Invoke(value);
            return value;
        }
#endif

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

        private static V2CompatibleMapContract DecodeV2CompatibleMap(string hex)
        {
            WProtoReader reader = new WProtoReader(Parse(hex));
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<V2CompatibleMapContract>()
                    .TryRead(ref reader, out V2CompatibleMapContract value),
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
            /*
             * Measuring map values during both Measure and Write repeats hooks and can leak their pooled
             * state.
             */
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

            Assert.AreEqual(0, writer.Depth, "a map entry left the nesting depth unbalanced");
        }

        [Test]
        public void KeysTheSpecForbidsMatchTheOracleRatherThanBeingRefused()
        {
            /*
             * protobuf-net accepts float, double, and enum keys beyond the protobuf map specification;
             * compatibility requires their encoding.
             */
            Assert.AreEqual(
                "0A070D0000C03F1002",
                Encode(
                    new ExoticKeyContract { ByFloat = new Dictionary<float, int> { { 1.5f, 2 } } }
                )
            );
            Assert.AreEqual(
                "0A070D000000001002",
                Encode(new ExoticKeyContract { ByFloat = new Dictionary<float, int> { { 0f, 2 } } })
            );
            Assert.AreEqual(
                "120B09000000000000F83F1002",
                Encode(
                    new ExoticKeyContract { ByDouble = new Dictionary<double, int> { { 1.5, 2 } } }
                )
            );
            Assert.AreEqual(
                "120B0900000000000000001002",
                Encode(
                    new ExoticKeyContract { ByDouble = new Dictionary<double, int> { { 0d, 2 } } }
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
