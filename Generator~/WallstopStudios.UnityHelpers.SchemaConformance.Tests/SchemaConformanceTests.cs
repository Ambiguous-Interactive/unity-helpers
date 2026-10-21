// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.SchemaConformance.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Tests.Serialization;

    [TestFixture]
    public sealed class SchemaConformanceTests
    {
        private string _directory;

        private static string ExportSchema<T>(Type[] roots = null, int expectedDiagnostics = 0)
        {
            Assert.IsTrue(
                WProtoSchemaText.TryWriteSchema(
                    roots ?? new[] { typeof(T) },
                    "interop",
                    null,
                    out string schema,
                    out IReadOnlyList<string> diagnostics
                )
            );
            Assert.AreEqual(expectedDiagnostics, diagnostics.Count, string.Join("\n", diagnostics));
            return schema;
        }

        private static void AssertScalarValues(InteropScalars actual)
        {
            Assert.IsTrue(actual != null);
            Assert.AreEqual(int.MinValue, actual.Signed);
            Assert.AreEqual(long.MinValue, actual.WideSigned);
            Assert.AreEqual(uint.MaxValue, actual.Unsigned);
            Assert.AreEqual(ulong.MaxValue, actual.WideUnsigned);
            Assert.AreEqual(-17, actual.ZigZag);
            Assert.AreEqual(1.5f, actual.Single);
            Assert.AreEqual(-2.5d, actual.Double);
            Assert.IsTrue(actual.Enabled);
            Assert.AreEqual('界', actual.Character);
            Assert.AreEqual("A界🙂\0", actual.Text);
            CollectionAssert.AreEqual(new byte[] { 0, 255, 34 }, actual.Bytes);
            Assert.AreEqual(0, actual.Optional);
            Assert.IsTrue(actual.Optional.HasValue);
        }

        private static void AssertDerivedValues(SchemaDerived actual)
        {
            Assert.IsTrue(actual != null);
            Assert.AreEqual(1, actual.BaseId);
            Assert.AreEqual("base", actual.BaseName);
            Assert.AreEqual(7, actual.Extra);
        }

        private static void AssertChainValues(SchemaChainBase actual)
        {
            Assert.IsInstanceOf<SchemaChainSubDerived>(actual);
            SchemaChainSubDerived leaf = (SchemaChainSubDerived)actual;
            Assert.AreEqual(1, leaf.RootId);
            Assert.AreEqual(2, leaf.Middle);
            Assert.AreEqual(3, leaf.Leaf);
        }

        [SetUp]
        public async Task SetUp()
        {
            WProtoBcl.RegisterAll();
            _directory = Path.Combine(
                Environment.GetEnvironmentVariable("WPROTO_SCHEMA_EVIDENCE_ROOT")
                    ?? Path.Combine(AppContext.BaseDirectory, "schema-evidence"),
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(_directory);
            byte[] version = await ProtocOracle.RequireSuccess(
                _directory,
                "version",
                Array.Empty<byte>(),
                "--version"
            );
            using SHA256 hash = SHA256.Create();
            string digest = Convert
                .ToHexString(
                    hash.ComputeHash(
                        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "protoc"))
                    )
                )
                .ToLowerInvariant();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(version).Trim());
            TestContext.Out.WriteLine("protoc SHA256: " + digest);
            Assert.AreEqual("libprotoc 27.2", Encoding.UTF8.GetString(version).Trim());
            Assert.AreEqual(
                "0e8f5a802a0768354f0a66eec112a968fbc0374521296e7355bfa352474c29ff",
                digest
            );
        }

        [Test]
        public async Task ScalarsDecodeThroughTheExportedSchemaInBothLanguages()
        {
            InteropScalars value = new InteropScalars
            {
                Signed = int.MinValue,
                WideSigned = long.MinValue,
                Unsigned = uint.MaxValue,
                WideUnsigned = ulong.MaxValue,
                ZigZag = -17,
                Single = 1.5f,
                Double = -2.5d,
                Enabled = true,
                Character = '界',
                Text = "A界🙂\0",
                Bytes = new byte[] { 0, 255, 34 },
                Optional = 0,
            };
            await Verify(
                value,
                nameof(InteropScalars),
                "Signed: -2147483648 WideSigned: -9223372036854775808 "
                    + "Unsigned: 4294967295 WideUnsigned: 18446744073709551615 ZigZag: -17 "
                    + "Single: 1069547520 Double: 13836183955189006336 Enabled: true Character: 30028 "
                    + "Text: \"A界🙂\\000\" Bytes: \"\\000\\377\\042\" Optional: 0",
                AssertScalarValues
            );
        }

        [Test]
        public async Task DefaultScalarsKeepAbsentNullablePresence()
        {
            await Verify(
                new InteropScalars(),
                nameof(InteropScalars),
                string.Empty,
                actual =>
                {
                    Assert.IsTrue(actual != null);
                    Assert.AreEqual(0, actual.Signed);
                    Assert.AreEqual(0, actual.WideSigned);
                    Assert.AreEqual(0, actual.Unsigned);
                    Assert.AreEqual(0, actual.WideUnsigned);
                    Assert.AreEqual(0, actual.ZigZag);
                    Assert.AreEqual(0, actual.Single);
                    Assert.AreEqual(0, actual.Double);
                    Assert.IsFalse(actual.Enabled);
                    Assert.AreEqual('\0', actual.Character);
                    Assert.IsTrue(actual.Text == null);
                    Assert.IsTrue(actual.Bytes == null);
                    Assert.IsFalse(actual.Optional.HasValue);
                }
            );
        }

        [Test]
        public async Task CollectionsDecodeThroughNativeMapsAndEntryWrappers()
        {
            Guid key = new Guid("00000001-0000-0000-0200-000000000000");
            await Verify(
                new InteropCollections
                {
                    Packed = new List<int> { int.MinValue, 0, int.MaxValue },
                    Unpacked = new[] { -17, 128 },
                    Rows = new[] { new[] { 1, -2 }, Array.Empty<int>() },
                    Grid = new[,]
                    {
                        { 0, 1 },
                        { -2, 3 },
                    },
                    Counts = new Dictionary<string, int> { { "", 0 }, { "界", -17 } },
                    ByGuid = new Dictionary<Guid, int> { { key, 23 } },
                    Blobs = new[] { Array.Empty<byte>(), new byte[] { 0, 255 } },
                },
                nameof(InteropCollections),
                "Packed: [-2147483648, 0, 2147483647] Unpacked: [-17, 128] "
                    + "Rows { values: [1, -2] } Rows {} Grid { dims: [2, 2] values: [0, 1, -2, 3] } "
                    + "Counts { key: \"\" value: 0 } Counts { key: \"界\" value: -17 } "
                    + "ByGuid { key { low: 1 high: 2 } value: 23 } Blobs: [\"\", \"\\000\\377\"]",
                actual =>
                {
                    Assert.IsTrue(actual != null);
                    CollectionAssert.AreEqual(
                        new[] { int.MinValue, 0, int.MaxValue },
                        actual.Packed
                    );
                    CollectionAssert.AreEqual(new[] { -17, 128 }, actual.Unpacked);
                    Assert.AreEqual(2, actual.Rows.Length);
                    CollectionAssert.AreEqual(new[] { 1, -2 }, actual.Rows[0]);
                    Assert.IsEmpty(actual.Rows[1]);
                    Assert.AreEqual(2, actual.Grid.GetLength(0));
                    Assert.AreEqual(2, actual.Grid.GetLength(1));
                    CollectionAssert.AreEqual(new[] { 0, 1, -2, 3 }, actual.Grid);
                    Assert.AreEqual(2, actual.Counts.Count);
                    Assert.IsTrue(actual.Counts.TryGetValue(string.Empty, out int zero));
                    Assert.AreEqual(0, zero);
                    Assert.IsTrue(actual.Counts.TryGetValue("界", out int count));
                    Assert.AreEqual(-17, count);
                    Assert.AreEqual(1, actual.ByGuid.Count);
                    Assert.IsTrue(actual.ByGuid.TryGetValue(key, out int number));
                    Assert.AreEqual(23, number);
                    Assert.AreEqual(2, actual.Blobs.Length);
                    Assert.IsEmpty(actual.Blobs[0]);
                    CollectionAssert.AreEqual(new byte[] { 0, 255 }, actual.Blobs[1]);
                }
            );
        }

        [Test]
        public async Task BclValuesDecodeTheirIndependentMessageLayouts()
        {
            await Verify(
                new InteropBclValues
                {
                    Id = new Guid("00000001-0000-0000-0200-000000000000"),
                    Moment = new DateTime(1970, 1, 3, 0, 0, 0, DateTimeKind.Unspecified),
                    Duration = TimeSpan.FromMilliseconds(-123),
                    Amount = -123.45m,
                },
                nameof(InteropBclValues),
                "Id { low: 1 high: 2 } Moment { value: 2 } "
                    + "Duration { value: -123 scale: 4 } Amount { low: 12345 sign_scale: 5 }",
                actual =>
                {
                    Assert.IsTrue(actual != null);
                    Assert.AreEqual(new Guid("00000001-0000-0000-0200-000000000000"), actual.Id);
                    Assert.AreEqual(new DateTime(1970, 1, 3), actual.Moment);
                    Assert.AreEqual(DateTimeKind.Unspecified, actual.Moment.Kind);
                    Assert.AreEqual(TimeSpan.FromMilliseconds(-123), actual.Duration);
                    Assert.AreEqual(-123.45m, actual.Amount);
                }
            );
        }

        [Test]
        public async Task ChangedFieldNumberBreaksBothReaderDirections()
        {
            string schema = ExportSchema<InteropScalars>()
                .Replace(
                    $"int32 {nameof(InteropScalars.Signed)} = 1;",
                    $"int32 {nameof(InteropScalars.Signed)} = 40;",
                    StringComparison.Ordinal
                );
            File.WriteAllText(Path.Combine(_directory, "schema.proto"), schema);
            byte[] oracle = await ProtocOracle.RequireSuccess(
                _directory,
                "wrong-tag-encode",
                Encoding.UTF8.GetBytes("Signed: 17"),
                "--encode=interop." + nameof(InteropScalars),
                "schema.proto"
            );
            Assert.AreEqual(0, WProtoFacade.Deserialize<InteropScalars>(oracle).Signed);
            byte[] decoded = await ProtocOracle.RequireSuccess(
                _directory,
                "wrong-tag-decode",
                WProtoFacade.Serialize(new InteropScalars { Signed = 17 }),
                "--decode=interop." + nameof(InteropScalars),
                "schema.proto"
            );
            StringAssert.Contains("1: 17", Encoding.UTF8.GetString(decoded));
            StringAssert.DoesNotContain(
                nameof(InteropScalars.Signed) + ":",
                Encoding.UTF8.GetString(decoded)
            );
        }

        [Test]
        public async Task SingularUriDecodesAsRawTextInBothLanguages()
        {
            await Verify(
                new InteropUriValue { Source = new Uri("../界", UriKind.Relative) },
                nameof(InteropUriValue),
                "Source: \"../界\"",
                actual =>
                {
                    Assert.IsTrue(actual != null);
                    Assert.IsTrue(actual.Source != null);
                    Assert.AreEqual("../界", actual.Source.OriginalString);
                }
            );
        }

        [Test]
        public async Task UriValuesDecodeAsRawTextInBothLanguages()
        {
            await Verify(
                new InteropUriValues
                {
                    Source = new Uri("https://example.com/A%20B?q=%2f", UriKind.Absolute),
                    Mirrors = new List<Uri> { new Uri("../界", UriKind.Relative) },
                    ByName = new Dictionary<string, Uri>
                    {
                        { "primary", new Uri("URN:example:Thing", UriKind.Absolute) },
                    },
                },
                nameof(InteropUriValues),
                "Source: \"https://example.com/A%20B?q=%2f\" "
                    + "Mirrors: \"../界\" ByName { key: \"primary\" value: \"URN:example:Thing\" }",
                actual =>
                {
                    Assert.IsTrue(actual != null);
                    Assert.AreEqual(
                        "https://example.com/A%20B?q=%2f",
                        actual.Source.OriginalString
                    );
                    Assert.AreEqual(1, actual.Mirrors.Count);
                    Assert.AreEqual("../界", actual.Mirrors[0].OriginalString);
                    Assert.AreEqual(1, actual.ByName.Count);
                    Assert.IsTrue(actual.ByName.TryGetValue("primary", out Uri source));
                    Assert.AreEqual("URN:example:Thing", source.OriginalString);
                }
            );
        }

        [Test]
        public async Task NarrowEnumMapKeysKeepTheirNamedDeclarations()
        {
            await Verify(
                new SchemaEnumMap
                {
                    ByColor = new Dictionary<SchemaColor, int>
                    {
                        { (SchemaColor)0, 0 },
                        { SchemaColor.Blue, 17 },
                    },
                },
                nameof(SchemaEnumMap),
                "ByColor { key: None value: 0 } ByColor { key: Blue value: 17 }",
                actual =>
                {
                    Assert.IsTrue(actual != null);
                    Assert.AreEqual(2, actual.ByColor.Count);
                    Assert.IsTrue(actual.ByColor.TryGetValue((SchemaColor)0, out int zero));
                    Assert.AreEqual(0, zero);
                    Assert.IsTrue(actual.ByColor.TryGetValue(SchemaColor.Blue, out int blue));
                    Assert.AreEqual(17, blue);
                }
            );
        }

        [Test]
        public async Task Unsigned64EnumExportCompilesAndCrossReadsItsMaximumValue()
        {
            await Verify(
                new SchemaUnsignedEnumHost { Value = SchemaUnsignedColor.Huge },
                nameof(SchemaUnsignedEnumHost),
                "Value: 18446744073709551615",
                actual =>
                {
                    Assert.IsTrue(actual != null);
                    Assert.AreEqual(ulong.MaxValue, (ulong)actual.Value);
                }
            );
        }

        [Test]
        public async Task WideEnumsKeepTheirFullNumericRangeInEveryContainer()
        {
            await Verify(
                new WProtoSchemaWideEnumContract
                {
                    Unsigned = WProtoSchemaUnsigned32.Maximum,
                    Signed = WProtoSchemaSigned64.Minimum,
                    WideUnsigned = WProtoSchemaUnsigned64.Maximum,
                    Optional = (WProtoSchemaUnsigned32)0,
                    Packed = new[]
                    {
                        WProtoSchemaSigned64.Minimum,
                        (WProtoSchemaSigned64)0,
                        WProtoSchemaSigned64.Maximum,
                    },
                    Unpacked = new[]
                    {
                        WProtoSchemaUnsigned32.Maximum,
                        (WProtoSchemaUnsigned32)0,
                        (WProtoSchemaUnsigned32)42,
                    },
                    ByNumber = new Dictionary<WProtoSchemaUnsigned64, WProtoSchemaSigned64>
                    {
                        { WProtoSchemaUnsigned64.Maximum, WProtoSchemaSigned64.Maximum },
                    },
                    Rows = new[]
                    {
                        new[] { WProtoSchemaUnsigned64.Maximum },
                        Array.Empty<WProtoSchemaUnsigned64>(),
                    },
                    Grid = new[,]
                    {
                        { (WProtoSchemaUnsigned32)0, WProtoSchemaUnsigned32.Maximum },
                    },
                    ByName = new Dictionary<string, WProtoSchemaUnsigned64>
                    {
                        { "wide", WProtoSchemaUnsigned64.Maximum },
                    },
                },
                nameof(WProtoSchemaWideEnumContract),
                "Unsigned: 4294967295 Signed: -9223372036854775808 WideUnsigned: 18446744073709551615 Optional: 0 "
                    + "Packed: [-9223372036854775808, 0, 9223372036854775807] Unpacked: [4294967295, 0, 42] "
                    + "ByNumber { key: 18446744073709551615 value: 9223372036854775807 } "
                    + "Rows { values: 18446744073709551615 } Rows {} Grid { dims: [1, 2] values: [0, 4294967295] } "
                    + "ByName { key: \"wide\" value: 18446744073709551615 }",
                actual =>
                {
                    Assert.IsTrue(actual != null);
                    Assert.AreEqual(uint.MaxValue, (uint)actual.Unsigned);
                    Assert.AreEqual(long.MinValue, (long)actual.Signed);
                    Assert.AreEqual(ulong.MaxValue, (ulong)actual.WideUnsigned);
                    Assert.IsTrue(actual.Optional.HasValue);
                    Assert.AreEqual(0u, (uint)actual.Optional.Value);
                    CollectionAssert.AreEqual(
                        new[]
                        {
                            WProtoSchemaSigned64.Minimum,
                            (WProtoSchemaSigned64)0,
                            WProtoSchemaSigned64.Maximum,
                        },
                        actual.Packed
                    );
                    CollectionAssert.AreEqual(
                        new[]
                        {
                            WProtoSchemaUnsigned32.Maximum,
                            (WProtoSchemaUnsigned32)0,
                            (WProtoSchemaUnsigned32)42,
                        },
                        actual.Unpacked
                    );
                    Assert.AreEqual(1, actual.ByNumber.Count);
                    Assert.IsTrue(
                        actual.ByNumber.TryGetValue(
                            WProtoSchemaUnsigned64.Maximum,
                            out WProtoSchemaSigned64 number
                        )
                    );
                    Assert.AreEqual(long.MaxValue, (long)number);
                    Assert.AreEqual(2, actual.Rows.Length);
                    CollectionAssert.AreEqual(
                        new[] { WProtoSchemaUnsigned64.Maximum },
                        actual.Rows[0]
                    );
                    Assert.IsEmpty(actual.Rows[1]);
                    Assert.AreEqual(1, actual.Grid.GetLength(0));
                    Assert.AreEqual(2, actual.Grid.GetLength(1));
                    CollectionAssert.AreEqual(
                        new[] { (WProtoSchemaUnsigned32)0, WProtoSchemaUnsigned32.Maximum },
                        actual.Grid
                    );
                    Assert.AreEqual(1, actual.ByName.Count);
                    Assert.IsTrue(
                        actual.ByName.TryGetValue("wide", out WProtoSchemaUnsigned64 named)
                    );
                    Assert.AreEqual(ulong.MaxValue, (ulong)named);
                }
            );
        }

        [Test]
        public async Task WideEnumsKeepUndeclaredValuesAndAbsentNullablePresence()
        {
            await Verify(
                new WProtoSchemaWideEnumContract
                {
                    Unsigned = (WProtoSchemaUnsigned32)2147483648u,
                    Signed = (WProtoSchemaSigned64)(-2147483649L),
                    WideUnsigned = (WProtoSchemaUnsigned64)9223372036854775808UL,
                },
                nameof(WProtoSchemaWideEnumContract),
                "Unsigned: 2147483648 Signed: -2147483649 WideUnsigned: 9223372036854775808",
                actual =>
                {
                    Assert.IsTrue(actual != null);
                    Assert.AreEqual(2147483648u, (uint)actual.Unsigned);
                    Assert.AreEqual(-2147483649L, (long)actual.Signed);
                    Assert.AreEqual(9223372036854775808UL, (ulong)actual.WideUnsigned);
                    Assert.IsFalse(actual.Optional.HasValue);
                }
            );
        }

        [TestCase(
            nameof(WProtoSchemaWideEnumContract.Unsigned),
            "uint32",
            "int32",
            "Unsigned: 4294967295"
        )]
        [TestCase(
            nameof(WProtoSchemaWideEnumContract.Signed),
            "int64",
            "int32",
            "Signed: -9223372036854775808"
        )]
        [TestCase(
            nameof(WProtoSchemaWideEnumContract.WideUnsigned),
            "uint64",
            "uint32",
            "WideUnsigned: 18446744073709551615"
        )]
        public async Task NarrowedEnumSchemaRejectsAnOtherwiseValidValue(
            string member,
            string correctType,
            string wrongType,
            string input
        )
        {
            string original = ExportSchema<WProtoSchemaWideEnumContract>();
            string schema = original.Replace(
                correctType + " " + member + " =",
                wrongType + " " + member + " =",
                StringComparison.Ordinal
            );
            Assert.AreNotEqual(original, schema);
            File.WriteAllText(Path.Combine(_directory, "schema.proto"), schema);
            await ProtocOracle.RequireSuccess(
                _directory,
                "wrong-width-compile",
                Array.Empty<byte>(),
                "--descriptor_set_out=schema.pb",
                "schema.proto"
            );
            (int exitCode, byte[] output, string error) = await ProtocOracle.Run(
                _directory,
                "wrong-width-encode",
                Encoding.UTF8.GetBytes(input),
                "--encode=interop." + nameof(WProtoSchemaWideEnumContract),
                "schema.proto"
            );
            Assert.AreNotEqual(0, exitCode);
            Assert.IsEmpty(output);
            StringAssert.Contains("out of range", error);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task ConcreteSubtypeRootsDecodeTheirEnvelopeInBothDirections(int rootOrder)
        {
            Type[] roots =
                rootOrder == 0 ? new[] { typeof(SchemaDerived) }
                : rootOrder == 1 ? new[] { typeof(SchemaIncludeBase), typeof(SchemaDerived) }
                : new[] { typeof(SchemaDerived), typeof(SchemaIncludeBase) };
            await Verify(
                new SchemaDerived
                {
                    BaseId = 1,
                    BaseName = "base",
                    Extra = 7,
                },
                nameof(SchemaDerived),
                "BaseId: 1 BaseName: \"base\" schemaDerived { Extra: 7 }",
                AssertDerivedValues,
                roots
            );
        }

        [Test]
        public async Task BaseRootsDecodeTheirSubtypePayloadInBothDirections()
        {
            await Verify<SchemaIncludeBase>(
                new SchemaDerived
                {
                    BaseId = 1,
                    BaseName = "base",
                    Extra = 7,
                },
                nameof(SchemaIncludeBase),
                "BaseId: 1 BaseName: \"base\" schemaDerived { Extra: 7 }",
                actual =>
                {
                    Assert.IsInstanceOf<SchemaDerived>(actual);
                    AssertDerivedValues((SchemaDerived)actual);
                }
            );
        }

        [TestCase(typeof(SchemaChainBase))]
        [TestCase(typeof(SchemaChainDerived))]
        [TestCase(typeof(SchemaChainSubDerived))]
        public async Task ChainRootsDecodeOnlyTheFieldsOwnedByEachPayload(Type root)
        {
            SchemaChainSubDerived value = new SchemaChainSubDerived
            {
                RootId = 1,
                Middle = 2,
                Leaf = 3,
            };
            const string text =
                "RootId: 1 schemaChainDerived { Middle: 2 schemaChainSubDerived { Leaf: 3 } }";
            CollectionAssert.AreEqual(
                Convert.FromHexString("3A074203A0010350020801"),
                WProtoFacade.Serialize(value)
            );
            if (root == typeof(SchemaChainBase))
            {
                await Verify<SchemaChainBase>(value, root.Name, text, AssertChainValues);
            }
            else if (root == typeof(SchemaChainDerived))
            {
                await Verify<SchemaChainDerived>(value, root.Name, text, AssertChainValues);
            }
            else
            {
                await Verify(value, root.Name, text, AssertChainValues);
            }
        }

        [Test]
        public async Task MemberAndCollectionReferencesDecodeConcreteEnvelopes()
        {
            SchemaDerived concrete = new SchemaDerived
            {
                BaseId = 1,
                BaseName = "base",
                Extra = 7,
            };
            SchemaChainSubDerived leaf = new SchemaChainSubDerived
            {
                RootId = 1,
                Middle = 2,
                Leaf = 3,
            };
            await Verify(
                new WProtoSchemaSubtypeHolder
                {
                    Base = concrete,
                    Concrete = concrete,
                    Middle = leaf,
                    Leaf = leaf,
                    Repeated = new List<SchemaDerived> { concrete },
                    ByName = new Dictionary<string, SchemaDerived> { { "a", concrete } },
                },
                nameof(WProtoSchemaSubtypeHolder),
                "Base { BaseId: 1 BaseName: \"base\" schemaDerived { Extra: 7 } } "
                    + "Concrete { BaseId: 1 BaseName: \"base\" schemaDerived { Extra: 7 } } "
                    + "Middle { RootId: 1 schemaChainDerived { Middle: 2 schemaChainSubDerived { Leaf: 3 } } } "
                    + "Leaf { RootId: 1 schemaChainDerived { Middle: 2 schemaChainSubDerived { Leaf: 3 } } } "
                    + "Repeated { BaseId: 1 BaseName: \"base\" schemaDerived { Extra: 7 } } "
                    + "ByName { key: \"a\" value { BaseId: 1 BaseName: \"base\" schemaDerived { Extra: 7 } } }",
                actual =>
                {
                    Assert.IsTrue(actual != null);
                    Assert.IsInstanceOf<SchemaDerived>(actual.Base);
                    AssertDerivedValues((SchemaDerived)actual.Base);
                    AssertDerivedValues(actual.Concrete);
                    AssertChainValues(actual.Middle);
                    AssertChainValues(actual.Leaf);
                    Assert.IsTrue(actual.Repeated != null);
                    Assert.AreEqual(1, actual.Repeated.Count);
                    AssertDerivedValues(actual.Repeated[0]);
                    Assert.IsTrue(actual.ByName != null);
                    Assert.AreEqual(1, actual.ByName.Count);
                    Assert.IsTrue(actual.ByName.TryGetValue("a", out SchemaDerived mapped));
                    AssertDerivedValues(mapped);
                }
            );
        }

        [Test]
        public async Task NamedSubtypeRootsDecodeDespiteAnIncludeFieldNameCollision()
        {
            WProtoNamedSchemaDerived value = new WProtoNamedSchemaDerived
            {
                RootId = 1,
                PayloadNumber = 5,
                Extra = 7,
            };
            CollectionAssert.AreEqual(
                Convert.FromHexString("3A02100708011805"),
                WProtoFacade.Serialize(value)
            );
            await Verify(
                value,
                "NamedNode",
                "RootId: 1 namedNode: 5 namedNode3 { Extra: 7 }",
                actual =>
                {
                    Assert.IsTrue(actual != null);
                    Assert.AreEqual(1, actual.RootId);
                    Assert.AreEqual(5, actual.PayloadNumber);
                    Assert.AreEqual(7, actual.Extra);
                },
                expectedDiagnostics: 1
            );
        }

        [Test]
        public async Task AFlatConcreteSchemaFailsTheIndependentSemanticWitness()
        {
            File.WriteAllText(
                Path.Combine(_directory, "schema.proto"),
                "syntax = \"proto3\"; package interop; message Flat { int32 BaseId = 1; string BaseName = 2; int32 Extra = 10; }"
            );
            byte[] flat = await ProtocOracle.RequireSuccess(
                _directory,
                "encode-flat",
                Encoding.UTF8.GetBytes("BaseId: 1 BaseName: \"base\" Extra: 7"),
                "--encode=interop.Flat",
                "schema.proto"
            );
            Assert.Throws<SerializationCorruptDataException>(() =>
                WProtoFacade.Deserialize<SchemaDerived>(flat)
            );
            byte[] nested = WProtoFacade.Serialize(
                new SchemaDerived
                {
                    BaseId = 1,
                    BaseName = "base",
                    Extra = 7,
                }
            );
            CollectionAssert.AreEqual(Convert.FromHexString("3A0250070801120462617365"), nested);
            byte[] decoded = await ProtocOracle.RequireSuccess(
                _directory,
                "decode-flat",
                nested,
                "--decode=interop.Flat",
                "schema.proto"
            );
            StringAssert.DoesNotContain("Extra:", Encoding.UTF8.GetString(decoded));
            StringAssert.Contains("7 {", Encoding.UTF8.GetString(decoded));
        }

        [Test]
        public async Task ProtocRejectsAnInvalidSchema()
        {
            File.WriteAllText(
                Path.Combine(_directory, "schema.proto"),
                "syntax = \"not-a-syntax\";"
            );
            (int exitCode, byte[] output, string error) = await ProtocOracle.Run(
                _directory,
                "invalid-schema",
                Array.Empty<byte>(),
                "--descriptor_set_out=schema.pb",
                "schema.proto"
            );
            Assert.AreNotEqual(0, exitCode);
            Assert.IsEmpty(output);
            StringAssert.Contains("schema.proto", error);
            Assert.IsFalse(File.Exists(Path.Combine(_directory, "schema.pb")));
        }

        private async Task Verify<T>(
            T expected,
            string messageName,
            string independentText,
            Action<T> assertValues,
            Type[] roots = null,
            int expectedDiagnostics = 0
        )
        {
            File.WriteAllText(
                Path.Combine(_directory, "schema.proto"),
                ExportSchema<T>(roots, expectedDiagnostics)
            );
            await ProtocOracle.RequireSuccess(
                _directory,
                "compile",
                Array.Empty<byte>(),
                "--descriptor_set_out=schema.pb",
                "schema.proto"
            );
            Assert.IsTrue(0 < new FileInfo(Path.Combine(_directory, "schema.pb")).Length);
            byte[] oracle = await ProtocOracle.RequireSuccess(
                _directory,
                "encode",
                Encoding.UTF8.GetBytes(independentText),
                "--encode=interop." + messageName,
                "schema.proto"
            );
            assertValues(WProtoFacade.Deserialize<T>(oracle));
            byte[] encoded = WProtoFacade.Serialize(expected);
            File.WriteAllBytes(Path.Combine(_directory, "wproto.bin"), encoded);
            byte[] normalized = await ProtocOracle.RequireSuccess(
                _directory,
                "decode-reference",
                oracle,
                "--decode=interop." + messageName,
                "schema.proto"
            );
            byte[] actual = await ProtocOracle.RequireSuccess(
                _directory,
                "decode-wproto",
                encoded,
                "--decode=interop." + messageName,
                "schema.proto"
            );
            Assert.AreEqual(Encoding.UTF8.GetString(normalized), Encoding.UTF8.GetString(actual));
            TestContext.AddTestAttachment(Path.Combine(_directory, "schema.proto"));
            TestContext.AddTestAttachment(Path.Combine(_directory, "schema.pb"));
            TestContext.AddTestAttachment(Path.Combine(_directory, "wproto.bin"));
        }
    }
}
