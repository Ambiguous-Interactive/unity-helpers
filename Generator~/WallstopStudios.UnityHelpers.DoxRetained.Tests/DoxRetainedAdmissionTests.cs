// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.DoxRetained.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json.Nodes;
    using Data.Persistence.Models;
    using global::Tests.Configuration.Persistence;
    using NUnit.Framework;
    using ProtoBuf;
    using ProtoBuf.Meta;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Tests.Serialization;

    [TestFixture]
    public sealed class DoxRetainedAdmissionTests
    {
        private static RuntimeTypeModel CreateModel(
            bool includeFalseDefault = true,
            bool includeMap = true
        )
        {
            RuntimeTypeModel model = RuntimeTypeModel.Create();
            MetaType progress = model.Add(typeof(LevelProgressModel), false);
            progress.CompatibilityLevel = CompatibilityLevel.Level200;
            progress
                .Add(1, nameof(LevelProgressModel.Id))
                .Add(2, nameof(LevelProgressModel.Solved))
                .Add(3, nameof(LevelProgressModel.ProgressJson));
            if (includeFalseDefault)
            {
                progress[2].DefaultValue = false;
            }
            MetaType settings = model.Add(typeof(SettingsModel), false);
            settings.CompatibilityLevel = CompatibilityLevel.Level200;
            settings.Add(1, nameof(SettingsModel.settings));
            settings[1].IsMap = includeMap;
            model.CompileInPlace();
            return model;
        }

        private static RuntimeTypeModel CreateProxy()
        {
            RuntimeTypeModel model = RuntimeTypeModel.Create();
            model.Add(typeof(ProgressOracle), true);
            model.Add(typeof(SettingsOracle), true);
            model.CompileInPlace();
            return model;
        }

        private static byte[] Write<T>(RuntimeTypeModel model, T value)
            where T : class
        {
            ProtoBenchmarkBufferWriter writer = new ProtoBenchmarkBufferWriter(512);
            model.Serialize<T>(writer, value, userState: null);
            return writer.WrittenSpan.ToArray();
        }

        private static LevelProgressModel Progress(int index)
        {
            JsonNode value = JsonNode.Parse(
                DoxRetainedCorpus.Read("Data/level-progress-oracle.json.txt")
            )[index];
            return new LevelProgressModel
            {
                Id = value[nameof(LevelProgressModel.Id)].GetValue<string>(),
                Solved = value[nameof(LevelProgressModel.Solved)].GetValue<int>() != 0,
                ProgressJson = value[nameof(LevelProgressModel.ProgressJson)].GetValue<string>(),
            };
        }

        private static SettingsModel Settings()
        {
            SettingsModel value = new SettingsModel();
            JsonObject entries = JsonNode
                .Parse(DoxRetainedCorpus.Read("Data/settings.json.txt"))[
                    nameof(SettingsModel.settings)
                ]
                .AsObject();
            foreach (KeyValuePair<string, JsonNode> entry in entries)
            {
                value.settings.Add(entry.Key, entry.Value.GetValue<string>());
            }
            return value;
        }

        private static void CheckProgress(LevelProgressModel expected, LevelProgressModel actual)
        {
            Assert.IsTrue(actual != null);
            Assert.AreEqual(expected.Id, actual.Id);
            Assert.AreEqual(expected.Solved, actual.Solved);
            Assert.AreEqual(expected.ProgressJson, actual.ProgressJson);
        }

        private static void CheckMap(
            Dictionary<string, string> expected,
            Dictionary<string, string> actual
        )
        {
            Assert.IsTrue(actual != null);
            Assert.AreEqual(expected.Count, actual.Count);
            foreach (KeyValuePair<string, string> entry in expected)
            {
                Assert.IsTrue(actual.TryGetValue(entry.Key, out string value), entry.Key);
                Assert.AreEqual(entry.Value, value, entry.Key);
            }
        }

        private static void Verify<T>(
            RuntimeTypeModel model,
            byte[] input,
            T expected,
            Action<T, T> check
        )
            where T : class
        {
            Assert.IsTrue(
                WProtoFormatterProvider.TryGet(out IWProtoFormatter<T> formatter)
                    && formatter != null
            );
            check(
                expected,
                model.Deserialize<T>(new ReadOnlyMemory<byte>(input), value: null, userState: null)
            );
            check(expected, WProtoFacade.Deserialize<T>(input));
            byte[] reference = Write(model, expected);
            byte[] generated = WProtoFacade.Serialize(expected);
            CollectionAssert.AreEqual(input, reference);
            CollectionAssert.AreEqual(input, generated);
            ProtoBenchmarkBufferWriter writer = new ProtoBenchmarkBufferWriter(512);
            Assert.AreEqual(input.Length, WProtoFacade.Serialize(expected, writer));
            CollectionAssert.AreEqual(input, writer.WrittenSpan.ToArray());
            check(
                expected,
                model.Deserialize<T>(
                    new ReadOnlyMemory<byte>(generated),
                    value: null,
                    userState: null
                )
            );
            check(expected, WProtoFacade.Deserialize<T>(reference));
        }

        [Test]
        public void CapturedInputsAndLoadedOracleMatchPinnedIdentities()
        {
            JsonObject inventory = DoxRetainedCorpus.ReadInventory();
            DoxRetainedCorpus.Validate(inventory, DoxRetainedCorpus.Read);
#if PROTOBUF_NET_ORACLE_CANDIDATE
            new WallstopStudios.UnityHelpers.Proto.Generator.Tests.OracleIdentityTests().LoadedOracleHasTheExpectedPhysicalIdentity();
#else
            DoxRetainedCorpus.ValidateFiles(
                inventory["oracleAssemblies"].AsArray(),
                new[] { "protobuf-net.dll", "protobuf-net.Core.dll" },
                file =>
                    File.ReadAllBytes(
                        string.Equals(file, "protobuf-net.dll", StringComparison.Ordinal)
                            ? typeof(ProtoBuf.Serializer).Assembly.Location
                            : typeof(ProtoReader).Assembly.Location
                    )
            );
#endif
            Assert.AreEqual(
                new Version(3, 0, 0, 0),
                typeof(ProtoBuf.Serializer).Assembly.GetName().Version
            );
            Assert.AreEqual(
                new Version(3, 0, 0, 0),
                typeof(ProtoReader).Assembly.GetName().Version
            );
        }

        [TestCaseSource(typeof(DoxRetainedCorpus), nameof(DoxRetainedCorpus.Ids))]
        public void ExactConsumerTypesReadAndWriteEveryRetainedInput(string id)
        {
            DoxRetainedCorpus.Validate(DoxRetainedCorpus.ReadInventory(), DoxRetainedCorpus.Read);
            RuntimeTypeModel model = CreateModel();
            RuntimeTypeModel proxy = CreateProxy();
            byte[] input = DoxRetainedCorpus.Read(DoxRetainedCorpus.FileFor(id));
            if (string.Equals(id, "dox-settings-retained", StringComparison.Ordinal))
            {
                SettingsModel expected = Settings();
                Verify(
                    model,
                    input,
                    expected,
                    (first, second) => CheckMap(first.settings, second.settings)
                );
                SettingsOracle independent = proxy.Deserialize<SettingsOracle>(
                    new ReadOnlyMemory<byte>(input),
                    value: null,
                    userState: null
                );
                CheckMap(expected.settings, independent.Settings);
                CollectionAssert.AreEqual(
                    input,
                    Write(
                        proxy,
                        new SettingsOracle
                        {
                            Settings = new Dictionary<string, string>(expected.settings),
                        }
                    )
                );
            }
            else
            {
                int index = int.Parse(
                    id.Substring("dox-progress-row-".Length),
                    System.Globalization.CultureInfo.InvariantCulture
                );
                LevelProgressModel expected = Progress(index);
                Verify(model, input, expected, CheckProgress);
                ProgressOracle independent = proxy.Deserialize<ProgressOracle>(
                    new ReadOnlyMemory<byte>(input),
                    value: null,
                    userState: null
                );
                Assert.AreEqual(expected.Id, independent.Id);
                Assert.AreEqual(expected.Solved, independent.Solved);
                Assert.AreEqual(expected.ProgressJson, independent.ProgressJson);
                CollectionAssert.AreEqual(
                    input,
                    Write(
                        proxy,
                        new ProgressOracle
                        {
                            Id = expected.Id,
                            Solved = expected.Solved,
                            ProgressJson = expected.ProgressJson,
                        }
                    )
                );
            }
        }

        [Test]
        public void ComparatorMetadataMatchesTheIndependentAttributedOracle()
        {
            RuntimeTypeModel model = CreateModel();
            RuntimeTypeModel proxy = CreateProxy();
            foreach (int tag in new[] { 1, 2, 3 })
            {
                Assert.AreEqual(
                    proxy[typeof(ProgressOracle)][tag].DefaultValue,
                    model[typeof(LevelProgressModel)][tag].DefaultValue
                );
                Assert.AreEqual(
                    CompatibilityLevel.Level200,
                    model[typeof(LevelProgressModel)][tag].CompatibilityLevel
                );
            }
            ValueMember actual = model[typeof(SettingsModel)][1];
            ValueMember expected = proxy[typeof(SettingsOracle)][1];
            Assert.IsTrue(actual.IsMap && expected.IsMap);
            Assert.IsFalse(actual.OverwriteList || expected.OverwriteList);
            Assert.AreEqual(expected.MapKeyFormat, actual.MapKeyFormat);
            Assert.AreEqual(expected.MapValueFormat, actual.MapValueFormat);
            Assert.AreEqual(expected.CompatibilityLevel, actual.CompatibilityLevel);
            Assert.AreEqual(expected.IsRequired, actual.IsRequired);
            Assert.IsTrue(
                model[typeof(SettingsModel)].UseConstructor
                    && proxy[typeof(SettingsOracle)].UseConstructor
            );
            CollectionAssert.IsEmpty(Write(model, new LevelProgressModel()));
        }

        [Test]
        public void ImmutableSettingsConstructAnEmptyMapWithoutClaimingReusableReads()
        {
            Assert.IsTrue(
                WProtoFormatterProvider.TryGet(out IWProtoFormatter<SettingsModel> formatter)
                    && formatter != null
            );
            Assert.IsFalse(formatter is IWProtoMergeFormatter<SettingsModel>);
            SettingsModel generated = WProtoFacade.Deserialize<SettingsModel>(Array.Empty<byte>());
            SettingsModel reference = CreateModel()
                .Deserialize<SettingsModel>(
                    ReadOnlyMemory<byte>.Empty,
                    value: null,
                    userState: null
                );
            Assert.IsTrue(generated != null && generated.settings != null);
            Assert.IsTrue(reference != null && reference.settings != null);
            Assert.AreEqual(0, generated.settings.Count);
            Assert.AreEqual(0, reference.settings.Count);
            Dictionary<string, string> original = new Dictionary<string, string>
            {
                { "retained", "value" },
            };
            SettingsModel copy = new SettingsModel(original);
            Assert.IsFalse(ReferenceEquals(original, copy.settings));
            CheckMap(original, copy.settings);
        }

        [Test]
        public void SameTypeOracleMergePreservesItsInitializedReadonlyMap()
        {
            byte[] input = DoxRetainedCorpus.Read("Data/settings-wire-oracle.bin");
            SettingsModel seed = new SettingsModel(
                new Dictionary<string, string>
                {
                    { "untouched", "keep" },
                    { nameof(SettingsModel.SfxEnabled), "False" },
                }
            );
            Dictionary<string, string> original = seed.settings;
            SettingsOracle proxySeed = new SettingsOracle
            {
                Settings = new Dictionary<string, string>(original),
            };
            Dictionary<string, string> proxyMap = proxySeed.Settings;
            SettingsModel result = CreateModel()
                .Deserialize(new ReadOnlyMemory<byte>(input), value: seed, userState: null);
            SettingsOracle proxy = CreateProxy()
                .Deserialize(new ReadOnlyMemory<byte>(input), value: proxySeed, userState: null);
            Assert.AreSame(seed, result);
            Assert.AreSame(original, result.settings);
            Assert.AreSame(proxySeed, proxy);
            Assert.AreSame(proxyMap, proxy.Settings);
            Assert.AreEqual(3, result.settings.Count);
            Assert.IsTrue(result.settings.TryGetValue("untouched", out string untouched));
            Assert.AreEqual("keep", untouched);
            Assert.IsTrue(
                result.settings.TryGetValue(nameof(SettingsModel.SfxEnabled), out string sfxEnabled)
            );
            Assert.AreEqual("True", sfxEnabled);
            CheckMap(result.settings, proxy.Settings);
        }

        [Test]
        public void OmittingTheFalseDefaultChangesTheWireBytes()
        {
            CollectionAssert.AreEqual(
                new byte[] { 0x10, 0x00 },
                Write(CreateModel(includeFalseDefault: false), new LevelProgressModel())
            );
            CollectionAssert.IsEmpty(Write(CreateModel(), new LevelProgressModel()));
        }

        [Test]
        public void OmittingMapMetadataRejectsAnExistingDuplicateKey()
        {
            SettingsModel seed = new SettingsModel(
                new Dictionary<string, string> { { nameof(SettingsModel.SfxEnabled), "False" } }
            );
            byte[] input = DoxRetainedCorpus.Read("Data/settings-wire-oracle.bin");
            ArgumentException exception = Assert.Catch<ArgumentException>(() =>
                CreateModel(includeMap: false)
                    .Deserialize(new ReadOnlyMemory<byte>(input), value: seed, userState: null)
            );
            StringAssert.Contains(nameof(SettingsModel.SfxEnabled), exception.Message);
        }

        [TestCase("alias")]
        [TestCase("duplicate")]
        [TestCase("missing-populated")]
        [TestCase("extra")]
        [TestCase("row-mapping")]
        [TestCase("length")]
        public void InventoryRejectsChangedMembershipAndAssociations(string mutation)
        {
            JsonObject inventory = DoxRetainedCorpus.ReadInventory();
            JsonArray inputs = inventory["inputs"].AsArray();
            switch (mutation)
            {
                case "alias":
                    inputs[0]["id"] = "dox-progress-row-01";
                    break;
                case "duplicate":
                    inputs[1]["id"] = inputs[0]["id"].GetValue<string>();
                    break;
                case "missing-populated":
                    inputs.RemoveAt(0);
                    break;
                case "extra":
                    inputs.Add(inputs[0].DeepClone());
                    inputs[inputs.Count - 1]["id"] = "dox-progress-row-10";
                    break;
                case "row-mapping":
                    inputs[0]["file"] = inputs[1]["file"].GetValue<string>();
                    break;
                case "length":
                    inputs[0]["bytes"] = inputs[0]["bytes"].GetValue<int>() + 1;
                    break;
                default:
                    Assert.Fail("Unknown mutation.");
                    break;
            }
            Assert.Throws<AssertionException>(() =>
                DoxRetainedCorpus.Validate(inventory, DoxRetainedCorpus.Read)
            );
        }

        [TestCase("Data/progress-wire-0.bin")]
        [TestCase("Data/settings-wire-oracle.bin")]
        [TestCase("Data/level-progress-oracle.json.txt")]
        [TestCase("Data/settings.json.txt")]
        [TestCase("Captured/LevelProgressModel.cs.txt")]
        [TestCase("Captured/SettingsModel.cs.txt")]
        [TestCase("Captured/ProgressOracle.cs.txt")]
        [TestCase("Captured/SettingsOracle.cs.txt")]
        public void InventoryRejectsChangedCapturedBytes(string changedFile)
        {
            Assert.Throws<AssertionException>(() =>
                DoxRetainedCorpus.Validate(
                    DoxRetainedCorpus.ReadInventory(),
                    file =>
                    {
                        byte[] bytes = DoxRetainedCorpus.Read(file);
                        if (string.Equals(file, changedFile, StringComparison.Ordinal))
                        {
                            bytes[0] ^= 1;
                        }
                        return bytes;
                    }
                )
            );
        }

        [TestCase("id")]
        [TestCase("solved")]
        [TestCase("json")]
        public void IndependentProgressAssertionsRejectSemanticLoss(string mutation)
        {
            LevelProgressModel expected = Progress(0);
            LevelProgressModel changed = Progress(0);
            switch (mutation)
            {
                case "id":
                    changed.Id = "changed";
                    break;
                case "solved":
                    changed.Solved = !changed.Solved;
                    break;
                case "json":
                    changed.ProgressJson += " ";
                    break;
                default:
                    Assert.Fail("Unknown mutation.");
                    break;
            }
            Assert.Throws<AssertionException>(() => CheckProgress(expected, changed));
        }

        [TestCase("count")]
        [TestCase("key")]
        [TestCase("value")]
        public void IndependentMapAssertionsRejectSemanticLoss(string mutation)
        {
            SettingsModel expected = Settings();
            SettingsModel changed = Settings();
            switch (mutation)
            {
                case "count":
                    changed.settings.Remove(nameof(SettingsModel.SfxEnabled));
                    break;
                case "key":
                    changed.settings.Remove(nameof(SettingsModel.SfxEnabled));
                    changed.settings.Add("changed", "True");
                    break;
                case "value":
                    changed.settings[nameof(SettingsModel.SfxEnabled)] = "False";
                    break;
                default:
                    Assert.Fail("Unknown mutation.");
                    break;
            }
            Assert.Throws<AssertionException>(() => CheckMap(expected.settings, changed.settings));
        }
    }
}
