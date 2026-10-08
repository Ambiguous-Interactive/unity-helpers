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
#if !PROTOBUF_NET_ORACLE_V2
        [Test]
        public void MapOracleIsPinnedToVersionThreeTwoFiftySix()
        {
            System.Diagnostics.FileVersionInfo version =
                System.Diagnostics.FileVersionInfo.GetVersionInfo(
                    typeof(ProtoBuf.Serializer).Assembly.Location
                );
            Assert.AreEqual(3, version.FileMajorPart);
            Assert.AreEqual(2, version.FileMinorPart);
            Assert.AreEqual(56, version.FileBuildPart);
        }
#endif

        [TestCase(0, 0d, "0A00")]
        [TestCase(0, 1d, "0A0911000000000000F03F")]
        [TestCase(1, 0d, "0A020801")]
        [TestCase(1, 1d, "0A0B080111000000000000F03F")]
        public void IntegerDoubleMapMatchesPinnedV3Bytes(int key, double value, string expected)
        {
            ZeroKeyMapContract contract = new ZeroKeyMapContract
            {
                Signed32 = new Dictionary<int, double> { { key, value } },
            };
            Assert.AreEqual(expected, OracleHex(contract), "Pinned integer-key oracle bytes");
            Assert.AreEqual(expected, Encode(contract));
            using MemoryStream stream = new MemoryStream(Parse(expected));
            ZeroKeyMapContract oracleRead = ProtoBuf.Serializer.Deserialize<ZeroKeyMapContract>(
                stream
            );
            Assert.AreEqual(value, oracleRead.Signed32.ValueFor(key));
        }

        [TestCase((ZeroMapKeyKind)0, 0d, "32020800")]
        [TestCase((ZeroMapKeyKind)0, 1d, "320B080011000000000000F03F")]
        [TestCase(ZeroMapKeyKind.Other, 0d, "32020807")]
        [TestCase(ZeroMapKeyKind.Other, 1d, "320B080711000000000000F03F")]
        public void EnumDoubleMapMatchesPinnedV3Bytes(
            ZeroMapKeyKind key,
            double value,
            string expected
        )
        {
            ZeroKeyMapContract contract = new ZeroKeyMapContract
            {
                Enumeration = new Dictionary<ZeroMapKeyKind, double> { { key, value } },
            };
#if PROTOBUF_NET_ORACLE_V2
            string v2Expected =
                "320B080"
                + (key == (ZeroMapKeyKind)0 ? "0" : "7")
                + "11"
                + (value == 0d ? "0000000000000000" : "000000000000F03F");
            Assert.AreEqual(
                v2Expected,
                OracleHex(contract),
                "v2 retains enum keys and default fixed-width values"
            );
#else
            Assert.AreEqual(expected, OracleHex(contract), "Pinned protobuf-net 3.2.56 bytes");
#endif
            Assert.AreEqual(expected, Encode(contract));
            using MemoryStream stream = new MemoryStream(Parse(expected));
            ZeroKeyMapContract oracleRead = ProtoBuf.Serializer.Deserialize<ZeroKeyMapContract>(
                stream
            );
            Assert.AreEqual(value, oracleRead.Enumeration.ValueFor(key));
        }

        [TestCase("320911000000000000F03F", 1d)]
        [TestCase("320B080011000000000000F03F", 1d)]
        [TestCase("3200", 0d)]
        [TestCase("32020800", 0d)]
        public void KeylessAndExplicitZeroEnumEntriesCrossRead(string hex, double expected)
        {
            WProtoReader reader = new WProtoReader(Parse(hex));
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<ZeroKeyMapContract>()
                    .TryRead(ref reader, out ZeroKeyMapContract read)
            );
            using MemoryStream stream = new MemoryStream(Parse(hex));
            ZeroKeyMapContract oracle = ProtoBuf.Serializer.Deserialize<ZeroKeyMapContract>(stream);
            CollectionAssert.AreEquivalent(oracle.Enumeration, read.Enumeration);
            Assert.AreEqual(expected, read.Enumeration.ValueFor((ZeroMapKeyKind)0));
        }

        [TestCase("0A0911000000000000F03F")]
        [TestCase("0A0B080011000000000000F03F")]
        [TestCase("0A00")]
        [TestCase("0A020800")]
        public void KeylessAndExplicitZeroMapEntriesCrossRead(string hex)
        {
            WProtoReader reader = new WProtoReader(Parse(hex));
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<ZeroKeyMapContract>()
                    .TryRead(ref reader, out ZeroKeyMapContract read)
            );
            using MemoryStream stream = new MemoryStream(Parse(hex));
            ZeroKeyMapContract oracle = ProtoBuf.Serializer.Deserialize<ZeroKeyMapContract>(stream);
            CollectionAssert.AreEquivalent(oracle.Signed32, read.Signed32);
            Assert.AreEqual(hex.Contains("11") ? 1d : 0d, read.Signed32.ValueFor(0));
        }

#if !PROTOBUF_NET_ORACLE_V2
        [Test]
        public void DefaultScalarMapKeysMatchV3AcrossShapes()
        {
            ZeroKeyMapContract[] contracts =
            {
                new ZeroKeyMapContract { Signed32 = new Dictionary<int, double> { { 0, 1 } } },
                new ZeroKeyMapContract { Signed64 = new Dictionary<long, double> { { 0, 1 } } },
                new ZeroKeyMapContract { Unsigned32 = new Dictionary<uint, double> { { 0, 1 } } },
                new ZeroKeyMapContract { Unsigned64 = new Dictionary<ulong, double> { { 0, 1 } } },
                new ZeroKeyMapContract { Boolean = new Dictionary<bool, double> { { false, 1 } } },
                new ZeroKeyMapContract
                {
                    Enumeration = new Dictionary<ZeroMapKeyKind, double>
                    {
                        { (ZeroMapKeyKind)0, 1 },
                    },
                },
                new ZeroKeyMapContract { Single = new Dictionary<float, double> { { 0, 1 } } },
                new ZeroKeyMapContract { Double = new Dictionary<double, double> { { 0, 1 } } },
                new ZeroKeyMapContract
                {
                    Text = new Dictionary<string, double> { { string.Empty, 1 } },
                },
            };
            Assert.Multiple(() =>
            {
                foreach (ZeroKeyMapContract contract in contracts)
                {
                    string expected = OracleHex(contract);
                    TestContext.WriteLine(expected);
                    Assert.AreEqual(expected, Encode(contract));
                }
            });
        }
#endif

#if !PROTOBUF_NET_ORACLE_V2
        [Test]
        public void SeparateMalformedMapMessageFragmentsCannotRepairEachOther()
        {
            byte[] payload = Parse("12080801120108120107");
            using MemoryStream stream = new MemoryStream(payload);
            Assert.Catch(() => ProtoBuf.Serializer.Deserialize<MapContract>(stream));
            Assert.Throws<InvalidOperationException>(() =>
                WProtoFacade.TryDeserialize(payload, out MapContract _)
            );
        }

        [Test]
        public void SeparateMalformedScalarMessageFragmentsCannotRepairEachOther()
        {
            byte[] payload = Parse("120108120107");
            ProtoBuf.Meta.RuntimeTypeModel model = ProtoBuf.Meta.RuntimeTypeModel.Create();
            model.Add(typeof(HookedContract), false).Add(1, nameof(HookedContract.Value));
            model.Add(typeof(NestingContract), false).Add(2, nameof(NestingContract.Child));
            using MemoryStream stream = new MemoryStream(payload);
            Assert.Catch(() => model.Deserialize(stream, null, typeof(NestingContract)));
            Assert.Throws<InvalidOperationException>(() =>
                WProtoFacade.TryDeserialize(payload, out NestingContract _)
            );
        }

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
                    .Get<MapContract>()
                    .TryRead(ref reader, out MapContract direct)
            );
            Assert.IsNull(direct);
            Assert.Throws<InvalidOperationException>(() =>
                WProtoFacade.TryDeserialize(payload, out MapContract _)
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
                WProtoFormatterProvider.Get<MapContract>().TryRead(ref reader, out MapContract read)
            );
            if (expected)
            {
                Assert.IsTrue(read != null);
                Assert.AreEqual(7, read.ById.ValueFor(1).X);
                Assert.AreEqual(9, read.ById.ValueFor(1).Y);
                Assert.IsFalse(reader.Malformed);
                Assert.IsTrue(reader.End);
            }
            else
            {
                Assert.IsNull(read);
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
            using MemoryStream stream = new MemoryStream(payload);
            MapContract oracle = ProtoBuf.Serializer.Deserialize<MapContract>(stream);
            Assert.AreEqual(1, oracle.ById.Count);
            Assert.AreEqual(expectedX, oracle.ById.ValueFor(key).X, "Oracle X");
            Assert.AreEqual(expectedY, oracle.ById.ValueFor(key).Y, "Oracle Y");
            MapContract read;
            if (facade)
            {
                Assert.IsTrue(WProtoFacade.TryDeserialize(payload, out read));
            }
            else
            {
                WProtoReader reader = new WProtoReader(payload);
                Assert.IsTrue(
                    WProtoFormatterProvider.Get<MapContract>().TryRead(ref reader, out read)
                );
                Assert.IsFalse(reader.Malformed);
                Assert.IsTrue(reader.End);
            }
            Assert.AreEqual(1, read.ById.Count);
            Assert.AreEqual(expectedX, read.ById.ValueFor(key).X);
            Assert.AreEqual(expectedY, read.ById.ValueFor(key).Y);
        }
#endif

        [TestCase("0A080801120208071200", 7, false, TestName = "ReferenceEntry.EmptyLast.Direct")]
        [TestCase("0A080801120208071200", 7, true, TestName = "ReferenceEntry.EmptyLast.Facade")]
        [TestCase("0A080801120012020807", 7, false, TestName = "ReferenceEntry.EmptyFirst.Direct")]
        [TestCase("0A080801120012020807", 7, true, TestName = "ReferenceEntry.EmptyFirst.Facade")]
        [TestCase(
            "0A0A08011202080712020809",
            9,
            false,
            TestName = "ReferenceEntry.LaterScalar.Direct"
        )]
        [TestCase(
            "0A0A08011202080712020809",
            9,
            true,
            TestName = "ReferenceEntry.LaterScalar.Facade"
        )]
        public void ReferenceMessageEntryMergeMatchesBothOracleMajors(
            string hex,
            int expected,
            bool facade
        )
        {
            ProtoBuf.Meta.RuntimeTypeModel model = ProtoBuf.Meta.RuntimeTypeModel.Create();
            model.Add(typeof(HookedContract), false).Add(1, nameof(HookedContract.Value));
            model.Add(typeof(HookedMapContract), false).Add(1, nameof(HookedMapContract.ById));
            byte[] payload = Parse(hex);
            using MemoryStream stream = new MemoryStream(payload);
            HookedMapContract oracle = (HookedMapContract)
                model.Deserialize(stream, null, typeof(HookedMapContract));
            Assert.AreEqual(1, oracle.ById.Count);
            Assert.AreEqual(
                expected,
                oracle.ById.ValueFor(1).Value,
                "Independent explicit oracle model"
            );
            HookedMapContract read;
            if (facade)
            {
                Assert.IsTrue(WProtoFacade.TryDeserialize(payload, out read));
            }
            else
            {
                WProtoReader reader = new WProtoReader(payload);
                Assert.IsTrue(
                    WProtoFormatterProvider.Get<HookedMapContract>().TryRead(ref reader, out read)
                );
                Assert.IsFalse(reader.Malformed);
                Assert.IsTrue(reader.End);
            }
            Assert.AreEqual(1, read.ById.Count);
            Assert.AreEqual(expected, read.ById.ValueFor(1).Value);
        }

        /// <summary>Preserves WallstopProto's last-entry-wins map contract.</summary>
        /// <remarks>
        /// The explicit legacy oracle model reads this dictionary as repeated pairs and uses Add,
        /// rejecting duplicate keys. It is used only for within-entry message merging above;
        /// the annotated version-three map oracle independently covers last-entry replacement.
        /// </remarks>
        [TestCase(false)]
        [TestCase(true)]
        public void SeparateReferenceMapEntriesReplaceTheEarlierValue(bool facade)
        {
            byte[] payload = Parse("0A060801120208070A0408011200");
            HookedMapContract read;
            if (facade)
            {
                Assert.IsTrue(WProtoFacade.TryDeserialize(payload, out read));
            }
            else
            {
                WProtoReader reader = new WProtoReader(payload);
                Assert.IsTrue(
                    WProtoFormatterProvider.Get<HookedMapContract>().TryRead(ref reader, out read)
                );
                Assert.IsFalse(reader.Malformed);
                Assert.IsTrue(reader.End);
            }
            Assert.AreEqual(1, read.ById.Count);
            Assert.AreEqual(0, read.ById.ValueFor(1).Value);
        }

        [TestCase("0A070A016110071009", 9, false, TestName = "ScalarEntry.LastValue.Direct")]
        [TestCase("0A070A016110071009", 9, true, TestName = "ScalarEntry.LastValue.Facade")]
        [TestCase("0A030A0161", 0, false, TestName = "ScalarEntry.AbsentValue.Direct")]
        [TestCase("0A030A0161", 0, true, TestName = "ScalarEntry.AbsentValue.Facade")]
        public void ScalarMapEntryFieldsRetainLastValueAndAbsentDefaults(
            string hex,
            int expected,
            bool facade
        )
        {
            byte[] payload = Parse(hex);
            using MemoryStream stream = new MemoryStream(payload);
            V2CompatibleMapContract oracle =
                ProtoBuf.Serializer.Deserialize<V2CompatibleMapContract>(stream);
            Assert.AreEqual(expected, oracle.Values.ValueFor("a"));
            V2CompatibleMapContract read;
            if (facade)
            {
                Assert.IsTrue(WProtoFacade.TryDeserialize(payload, out read));
            }
            else
            {
                WProtoReader reader = new WProtoReader(payload);
                Assert.IsTrue(
                    WProtoFormatterProvider
                        .Get<V2CompatibleMapContract>()
                        .TryRead(ref reader, out read)
                );
                Assert.IsFalse(reader.Malformed);
                Assert.IsTrue(reader.End);
            }
            Assert.AreEqual(1, read.Values.Count);
            Assert.AreEqual(expected, read.Values.ValueFor("a"));
        }

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
            Assert.AreEqual(default(Outer.Point), Decode(v2Hex).ById.ValueFor(7));
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
            Assert.AreEqual(1, migrated.Values.ValueFor(string.Empty));

            using (MemoryStream stream = new MemoryStream(Parse(currentHex)))
            {
                V2CompatibleMapContract readByV2 =
                    ProtoBuf.Serializer.Deserialize<V2CompatibleMapContract>(stream);
                Assert.AreEqual(1, readByV2.Values.ValueFor(string.Empty));
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
            Assert.AreEqual(string.Empty, DecodeV2CompatibleMap(v2ValueHex).Sorted.ValueFor("b"));

            using (MemoryStream stream = new MemoryStream(Parse(currentValueHex)))
            {
                V2CompatibleMapContract readByV2 =
                    ProtoBuf.Serializer.Deserialize<V2CompatibleMapContract>(stream);
                Assert.AreEqual(string.Empty, readByV2.Sorted.ValueFor("b"));
            }
        }
#endif

        private static MapContract Bare(Action<MapContract> configure = null)
        {
            MapContract value = new MapContract { Overwritten = null, Merged = null };
            configure?.Invoke(value);
            return value;
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
            Assert.AreEqual(1, restored.ById.ValueFor(7).X);
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
            Assert.IsTrue(
                RoundTrip(Bare(c => c.ByName = new Dictionary<string, int>())).ByName == null
            );
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
                Assert.AreEqual(2, oracle.Values.ValueFor("a"));
            }

            Assert.AreEqual(2, Decode(twice).ByName.ValueFor("a"));
        }

        [Test]
        public void AnEntryMissingItsKeyOrValueDecodesToTheProtoDefault()
        {
            // Missing string keys use the protobuf empty-string default, not the C# null default.
            Assert.AreEqual(0, Decode("0A030A0161").ByName.ValueFor("a"));
            Assert.AreEqual(1, Decode("0A02" + "1001").ByName.ValueFor(string.Empty));
            Assert.AreEqual(0, Decode("0A00").ByName.ValueFor(string.Empty));

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
            for (int index = 0; index < bytes.Length; ++index)
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
        public void RepeatedMessageMapValuesRunDeserializationHooksOnce()
        {
            HookedContract.AfterDeserializationRuns = 0;
            Assert.IsTrue(
                WProtoFacade.TryDeserialize(
                    Parse("0A0A08011202080712020809"),
                    out HookedMapContract read
                )
            );
            HookedContract value = read.ById.ValueFor(1);
            Assert.AreEqual(9, value.Value);
            Assert.AreEqual(1, HookedContract.AfterDeserializationRuns);
            Assert.AreEqual(2, value.Trace.Count);
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
                value
                    .Trace.FindAll(entry =>
                        string.Equals(
                            entry,
                            "OnBeforeSerialization",
                            System.StringComparison.Ordinal
                        )
                    )
                    .Count,
                "before-serialization ran " + string.Join(", ", value.Trace)
            );

            Assert.AreEqual(0, writer.Depth, "a map entry left the nesting depth unbalanced");
        }

#if !PROTOBUF_NET_ORACLE_V2
        [Test]
        public void ExoticNumericKeysMatchActualV3OracleForZeroAndNonzero()
        {
            ExoticKeyContract[] contracts =
            {
                new ExoticKeyContract { ByFloat = new Dictionary<float, int> { { 0f, 2 } } },
                new ExoticKeyContract { ByFloat = new Dictionary<float, int> { { 1.5f, 2 } } },
                new ExoticKeyContract { ByDouble = new Dictionary<double, int> { { 0d, 2 } } },
                new ExoticKeyContract { ByDouble = new Dictionary<double, int> { { 1.5d, 2 } } },
                new ExoticKeyContract
                {
                    ByEnum = new Dictionary<MapKeyKind, int> { { MapKeyKind.None, 2 } },
                },
                new ExoticKeyContract
                {
                    ByEnum = new Dictionary<MapKeyKind, int> { { MapKeyKind.Other, 2 } },
                },
            };
            foreach (ExoticKeyContract contract in contracts)
            {
                Assert.AreEqual(OracleHex(contract), Encode(contract));
            }
        }
#endif

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
                "0A021002",
                Encode(new ExoticKeyContract { ByFloat = new Dictionary<float, int> { { 0f, 2 } } })
            );
            Assert.AreEqual(
                "120B09000000000000F83F1002",
                Encode(
                    new ExoticKeyContract { ByDouble = new Dictionary<double, int> { { 1.5, 2 } } }
                )
            );
            Assert.AreEqual(
                "12021002",
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
