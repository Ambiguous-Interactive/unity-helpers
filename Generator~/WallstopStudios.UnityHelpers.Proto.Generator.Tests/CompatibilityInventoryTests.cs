// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [TestFixture]
    public sealed class CompatibilityInventoryTests
    {
        private const string ControlSource =
            @"
            namespace InventoryControl
            {
                using System.ComponentModel;
                using ProtoBuf;
                using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
                [ProtoContract, WProtoContract]
                public partial class Message
                {
                    private const int WireTag = 1;
                    [ProtoMember(WireTag), WProtoMember(WireTag), DefaultValue(7)]
                    public int Value = 7;
                    [ProtoMember(2), WProtoMember(2)]
                    public int[] Values;
                }
            }

            ";

        private static void AddSources(
            List<SyntaxTree> trees,
            string directory,
            CSharpParseOptions options
        )
        {
            foreach (
                string path in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            )
            {
                trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(path), options, path));
            }
        }

        private static string[] CaptureControl(string source)
        {
            string[] result = CompatibilityInventory.Capture(
                CompatibilityInventory.Compile(new[] { CSharpSyntaxTree.ParseText(source) })
            );
            Assert.Less(0, result.Length);
            return result;
        }

        private static MetadataReference[] ReadUnityReferences(string directory)
        {
            Assert.IsTrue(
                Directory.Exists(directory),
                "Missing copied Unity references; restore and build this host test project."
            );
            string[] files = Directory.GetFiles(directory, "*.dll");
            Assert.IsTrue(
                File.Exists(Path.Combine(directory, "UnityEngine.CoreModule.dll")),
                "Unity core reference is missing from the inventory inputs."
            );
            Assert.Less(0, files.Length);
            return files.Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrEmptyUnityReferencesCannotPass(bool exists)
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                if (exists)
                {
                    Directory.CreateDirectory(directory);
                }
                Assert.Throws<AssertionException>(() => ReadUnityReferences(directory));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory);
                }
            }
        }

        [Test]
        public void PackageAndConsumerMetadataMatchesFrozenInventory()
        {
            string root = ContractMirrorTests.RepositoryRoot();
            List<SyntaxTree> trees = new List<SyntaxTree>();
            CSharpParseOptions options = new CSharpParseOptions(
                preprocessorSymbols: new[] { "WALLSTOP_PROTO", "UNITY_5_3_OR_NEWER" }
            );
            AddSources(trees, Path.Combine(root, "Runtime"), options);
            int packageSources = trees.Count;
            AddSources(
                trees,
                Path.Combine(root, "Tests", "Runtime", "Serialization", "ConsumerMigration"),
                options
            );
            Assert.Less(packageSources, trees.Count, "Consumer inventory cannot be empty.");
            Assert.Less(0, packageSources, "Package inventory cannot be empty.");
            CSharpCompilation compilation = CompatibilityInventory.Compile(trees);
            string unityReferences = Path.Combine(
                AppContext.BaseDirectory,
                "CompatibilityReferences"
            );
            compilation = compilation.AddReferences(ReadUnityReferences(unityReferences));
            string[] actual = CompatibilityInventory.Capture(compilation);
            Assert.IsTrue(
                actual.Any(entry =>
                    entry.StartsWith(
                        "contract global::WallstopStudios.UnityHelpers.Tests.Serialization.ConsumerMigration.",
                        StringComparison.Ordinal
                    )
                ),
                "Consumer contracts fell out of the semantic inventory."
            );
            Assert.IsTrue(
                actual.Any(entry =>
                    entry.StartsWith(
                        "contract global::WallstopStudios.UnityHelpers.Core.Random.AbstractRandom ",
                        StringComparison.Ordinal
                    )
                ),
                "Package contracts fell out of the semantic inventory."
            );
            string expectedPath = Path.Combine(
                root,
                "Generator~",
                "WallstopStudios.UnityHelpers.Proto.Generator.Tests",
                "CompatibilityInventory.v1.json"
            );
            string actualPath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "CompatibilityInventory.actual.json"
            );
            File.WriteAllText(
                actualPath,
                JsonSerializer.Serialize(actual, new JsonSerializerOptions { WriteIndented = true })
                    + "\n"
            );
            Assert.IsTrue(
                File.Exists(expectedPath),
                "Missing frozen inventory. Candidate written to " + actualPath
            );
            string[] expected = JsonSerializer.Deserialize<string[]>(
                File.ReadAllText(expectedPath)
            );
            CollectionAssert.AreEqual(
                expected,
                actual,
                "Wire metadata changed. Review the contract version; do not automatically rebaseline. Candidate: "
                    + actualPath
            );
        }

        [TestCase(
            "WireTag = 1",
            "WireTag = 9",
            TestName = "CoordinatedLegacyAndGeneratedTagChangeFailsFrozenInventory"
        )]
        [TestCase(
            "DefaultValue(7)",
            "DefaultValue(8)",
            TestName = "DefaultChangeFailsFrozenInventory"
        )]
        [TestCase(
            "WProtoMember(2)",
            "WProtoMember(2, IsPacked = false)",
            TestName = "PackingChangeFailsFrozenInventory"
        )]
        [TestCase(
            "public int Value = 7;",
            "public long Value = 7;",
            TestName = "MemberTypeChangeFailsFrozenInventory"
        )]
        [TestCase(
            "[ProtoMember(2), WProtoMember(2)]",
            "",
            TestName = "MemberDeletionFailsFrozenInventory"
        )]
        [TestCase("Value = 7", "Value = 8", TestName = "ReadInitializerChangeFailsFrozenInventory")]
        public void WireMutationChangesInventory(string original, string replacement)
        {
            string[] baseline = CaptureControl(ControlSource);
            string changed = ControlSource.Replace(original, replacement, StringComparison.Ordinal);
            Assert.AreNotEqual(ControlSource, changed, "The mutation must reach its subject.");
            string[] mutated = CaptureControl(changed);
            Assert.IsFalse(baseline.SequenceEqual(mutated, StringComparer.Ordinal));
        }

        [Test]
        public void ConstantExpressionsAndExplicitDefaultsPreserveEffectiveMetadata()
        {
            string[] baseline = CaptureControl(ControlSource);
            string equivalent = ControlSource
                .Replace("WireTag = 1", "WireTag = 2 - 1", StringComparison.Ordinal)
                .Replace(
                    "WProtoMember(2)",
                    "WProtoMember(2, IsPacked = true, IsRequired = false, OverwriteList = false, DataFormat = WProtoDataFormat.Default)",
                    StringComparison.Ordinal
                );
            CollectionAssert.AreEqual(baseline, CaptureControl(equivalent));
        }

        [Test]
        public void UnresolvedMetadataCannotProduceACleanInventory()
        {
            string broken = ControlSource.Replace(
                "WireTag = 1",
                "WireTag = MissingTag",
                StringComparison.Ordinal
            );
            Assert.Throws<AssertionException>(() => CaptureControl(broken));
        }

        [TestCase(
            ": System.IDisposable",
            "",
            TestName = "FormatterBaseInterfaceChangeFailsFrozenInventory"
        )]
        [TestCase(
            "where T : class",
            "where T : struct",
            TestName = "FormatterConstraintChangeFailsFrozenInventory"
        )]
        [TestCase(
            "public interface",
            "internal interface",
            TestName = "FormatterAccessibilityChangeFailsFrozenInventory"
        )]
        public void FormatterExtensionDeclarationChangesInventory(
            string original,
            string replacement
        )
        {
            const string source =
                "namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto { public interface IWProtoInventoryControl<T> : System.IDisposable where T : class { } }";
            string[] baseline = CompatibilityInventory.Capture(
                CompatibilityInventory.Compile(new[] { CSharpSyntaxTree.ParseText(source) })
            );
            Assert.Less(0, baseline.Length);
            string[] mutated = CompatibilityInventory.Capture(
                CompatibilityInventory.Compile(
                    new[]
                    {
                        CSharpSyntaxTree.ParseText(
                            source.Replace(original, replacement, StringComparison.Ordinal)
                        ),
                    }
                )
            );
            Assert.IsFalse(baseline.SequenceEqual(mutated, StringComparer.Ordinal));
        }

        [Test]
        public void RecordedImplicitPoliciesMatchRuntimeAttributeDefaults()
        {
            WProtoMemberAttribute member = new WProtoMemberAttribute(1);
            Assert.IsTrue(member.IsPacked);
            Assert.IsFalse(member.IsRequired);
            Assert.IsFalse(member.OverwriteList);
            Assert.AreEqual(WProtoDataFormat.Default, member.DataFormat);
            WProtoContractAttribute contract = new WProtoContractAttribute();
            Assert.IsFalse(contract.IgnoreListHandling);
            Assert.IsFalse(contract.SkipConstructor);
        }
    }
}
