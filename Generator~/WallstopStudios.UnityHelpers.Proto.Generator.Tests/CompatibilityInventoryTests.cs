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

        private const string ApiControlSource =
            @"
            namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
            {
                public ref struct InventoryReader { public void MarkMalformed() { } }
                public readonly struct InventoryResult { public readonly int Count; }
                public class InventoryFactory { }
                public enum InventoryMode : byte { None = 0, Read = 1 }
                public delegate int InventoryCallback(in int value);
                public interface InventoryOutput<out T> { }
                public class InventoryApi<T> : System.IDisposable where T : class
                {
                    public const int Maximum = 64;
                    public int Limit { get; private set; }
                    public ref readonly int First { get { throw null; } }
                    public InventoryApi(int maximum = 64) { }
                    public bool TryAdd(System.ReadOnlySpan<byte> bytes, ref InventoryReader parent) { return true; }
                    public void Read(out int value) { value = 0; }
                    public void Dispose() { }
                    public V Convert<V>(V value) where V : class { return value; }
                    public event System.Action Changed;
                    protected void OnChanged() { }
                    public class Nested { }
                }
            }
            ";

        private const string ApiMetadataControlSource =
            @"
            #nullable enable
            namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
            {
                [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = true, Inherited = false)]
                public sealed class InventoryMarkerAttribute : System.Attribute
                {
                    public InventoryMarkerAttribute(int value) { }
                    public InventoryMarkerAttribute(long value) { }
                    public InventoryMarkerAttribute(object value) { }
                }
                [InventoryMarker(10)]
                public class InventoryMetadata<[InventoryMarker(20)] T> where T : class
                {
                    [System.Obsolete(""old"", false)]
                    [return: InventoryMarker(30)]
                    public int Read([InventoryMarker(40)] int value) { return value; }
                    public void Optional([System.Runtime.InteropServices.Optional] int value) { }
                    [System.Diagnostics.Conditional(""API_TRACE"")]
                    public void Trace() { }
                    public string Text { get { return """"; } }
                    public System.Collections.Generic.List<string> Labels = new System.Collections.Generic.List<string>();
                    public virtual void Override() { }
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

        [TestCase(false)]
        [TestCase(true)]
        public void ReferencedEnumInventoryIsIndependentOfDeclarationOrder(bool enumFirst)
        {
            const string enumSource =
                "namespace InventoryOrder { public enum Mode : byte { None = 0, Read = 1 } }";
            const string contractSource =
                @"
                namespace InventoryOrder
                {
                    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
                    [WProtoContract]
                    public partial class Message
                    {
                        [WProtoMember(1)] public Mode Value;
                        [WProtoMember(2)] public Mode[] Values;
                        [WProtoMember(3)] public System.Collections.Generic.List<Mode> Items;
                    }
                }";
            SyntaxTree enumTree = CSharpSyntaxTree.ParseText(enumSource);
            SyntaxTree contractTree = CSharpSyntaxTree.ParseText(contractSource);
            CSharpCompilation compilation = CompatibilityInventory.Compile(
                enumFirst ? new[] { enumTree, contractTree } : new[] { contractTree, enumTree }
            );
            Assert.IsEmpty(
                compilation
                    .GetDiagnostics()
                    .Where(item => item.Severity == DiagnosticSeverity.Error)
            );
            string[] inventory = CompatibilityInventory.Capture(compilation);
            CollectionAssert.Contains(
                inventory,
                "enum global::InventoryOrder.Mode underlying=byte"
            );
            CollectionAssert.Contains(inventory, "enum-value global::InventoryOrder.Mode.None=0");
            CollectionAssert.Contains(inventory, "enum-value global::InventoryOrder.Mode.Read=1");
            CollectionAssert.AreEqual(
                CompatibilityInventory.Capture(
                    CompatibilityInventory.Compile(new[] { contractTree, enumTree })
                ),
                inventory
            );
        }

        private static string[] CaptureControl(string source)
        {
            string[] result = CompatibilityInventory.Capture(
                CompatibilityInventory.Compile(new[] { CSharpSyntaxTree.ParseText(source) })
            );
            Assert.Less(0, result.Length);
            return result;
        }

        private static string[] CaptureApiControl(string source)
        {
            CSharpCompilation compilation = CompatibilityInventory.Compile(
                new[] { CSharpSyntaxTree.ParseText(source) }
            );
            Assert.IsEmpty(
                compilation
                    .GetDiagnostics()
                    .Where(item => item.Severity == DiagnosticSeverity.Error),
                "API mutation controls must remain valid C# declarations."
            );
            string[] result = CompatibilityInventory.Capture(compilation);
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
        public void BoundOpenGenericAttributeTypesHaveAStableInventory()
        {
            const string source =
                "namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto { [System.ComponentModel.TypeConverter(typeof(System.Collections.Generic.List<>))] public class InventoryOpenGeneric { } }";
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
            CSharpCompilation compilation = CompatibilityInventory.Compile(new[] { tree });
            string[] entries = CompatibilityInventory.Capture(compilation);
            Assert.IsTrue(entries.Any(entry => entry.Contains("List<>", StringComparison.Ordinal)));
            string changed = source.Replace("List<>", "List<Missing>", StringComparison.Ordinal);
            Assert.Throws<AssertionException>(() =>
                CompatibilityInventory.Capture(
                    CompatibilityInventory.Compile(new[] { CSharpSyntaxTree.ParseText(changed) })
                )
            );
        }

        [Test]
        public void PackageSourceScanCannotBorrowMissingRuntimeDeclarations()
        {
            const string source =
                "namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto { public class InventoryOwner { public WProtoReader Reader; } }";
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
            CSharpCompilation compilation = CompatibilityInventory.Compile(
                new[] { tree },
                includeCompiledRuntime: false
            );
            Assert.IsTrue(
                compilation.GetTypeByMetadataName(typeof(WProtoContractAttribute).FullName) == null
            );
            Assert.Throws<AssertionException>(() => CompatibilityInventory.Capture(compilation));
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        public void MissingOrAmbiguousAttributeReferencesCannotPass(
            bool includeCompiledRuntime,
            bool includeUnityReferences
        )
        {
            const string source =
                "namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto { [UnityEngine.Scripting.Preserve] public class InventoryPreserved { } }";
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
            MetadataReference[] unityReferences = ReadUnityReferences(
                Path.Combine(AppContext.BaseDirectory, "CompatibilityReferences")
            );
            CSharpCompilation valid = CompatibilityInventory
                .Compile(new[] { tree }, includeCompiledRuntime: false)
                .AddReferences(unityReferences);
            Assert.IsEmpty(
                valid.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error)
            );
            Assert.Less(0, CompatibilityInventory.Capture(valid).Length);
            CSharpCompilation invalid = CompatibilityInventory.Compile(
                new[] { tree },
                includeCompiledRuntime
            );
            if (includeUnityReferences)
            {
                invalid = invalid.AddReferences(unityReferences);
            }
            Assert.IsTrue(
                invalid.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error)
            );
            Assert.Throws<AssertionException>(() => CompatibilityInventory.Capture(invalid));
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
            CSharpCompilation compilation = CompatibilityInventory.Compile(
                trees,
                includeCompiledRuntime: false
            );
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

        [TestCase(
            "public ref struct",
            "public struct",
            TestName = "RefStructChangeFailsApiInventory"
        )]
        [TestCase(
            "public readonly struct",
            "public struct",
            TestName = "ReadonlyStructChangeFailsApiInventory"
        )]
        [TestCase(
            "public class InventoryFactory { }",
            "public class InventoryFactory { private InventoryFactory() { } }",
            TestName = "ImplicitConstructorRemovalFailsApiInventory"
        )]
        [TestCase(
            "public class InventoryFactory",
            "public sealed class InventoryFactory",
            TestName = "SealedTypeChangeFailsApiInventory"
        )]
        [TestCase(
            "public class InventoryFactory",
            "public abstract class InventoryFactory",
            TestName = "AbstractTypeChangeFailsApiInventory"
        )]
        [TestCase(
            "public void MarkMalformed() { }",
            "",
            TestName = "ReaderFailureApiRemovalFailsInventory"
        )]
        [TestCase(
            "public bool TryAdd(System.ReadOnlySpan<byte> bytes, ref InventoryReader parent) { return true; }",
            "",
            TestName = "AccumulatorOverloadRemovalFailsApiInventory"
        )]
        [TestCase(
            "ref InventoryReader parent",
            "in InventoryReader parent",
            TestName = "RefParameterChangeFailsApiInventory"
        )]
        [TestCase(
            "out int value",
            "ref int value",
            TestName = "OutParameterChangeFailsApiInventory"
        )]
        [TestCase(
            "maximum = 64",
            "maximum = 65",
            TestName = "OptionalParameterDefaultChangeFailsApiInventory"
        )]
        [TestCase(
            "Maximum = 64",
            "Maximum = 65",
            TestName = "ConstantValueChangeFailsApiInventory"
        )]
        [TestCase(
            "public int Limit { get; private set; }",
            "public int Limit { get; set; }",
            TestName = "PropertySetterAccessibilityChangeFailsApiInventory"
        )]
        [TestCase(
            "public int Limit { get; private set; }",
            "public int Limit { get; }",
            TestName = "PropertySetterRemovalFailsApiInventory"
        )]
        [TestCase(
            "ref readonly int First",
            "ref int First",
            TestName = "PropertyReturnRefChangeFailsApiInventory"
        )]
        [TestCase(
            ": System.IDisposable",
            "",
            TestName = "PublicApiBaseInterfaceChangeFailsInventory"
        )]
        [TestCase(
            "where T : class",
            "where T : struct",
            TestName = "PublicApiConstraintChangeFailsInventory"
        )]
        [TestCase(
            "where V : class",
            "where V : struct",
            TestName = "PublicApiMethodConstraintChangeFailsInventory"
        )]
        [TestCase(
            "enum InventoryMode : byte",
            "enum InventoryMode : ushort",
            TestName = "PublicEnumUnderlyingTypeChangeFailsApiInventory"
        )]
        [TestCase("Read = 1", "Read = 2", TestName = "PublicEnumValueChangeFailsApiInventory")]
        [TestCase(
            "delegate int InventoryCallback",
            "delegate long InventoryCallback",
            TestName = "PublicDelegateReturnTypeChangeFailsApiInventory"
        )]
        [TestCase(
            "InventoryCallback(in int value)",
            "InventoryCallback(ref int value)",
            TestName = "PublicDelegateParameterChangeFailsApiInventory"
        )]
        [TestCase(
            "InventoryOutput<out T>",
            "InventoryOutput<T>",
            TestName = "PublicGenericVarianceChangeFailsApiInventory"
        )]
        [TestCase(
            "public event System.Action Changed;",
            "",
            TestName = "PublicEventRemovalFailsApiInventory"
        )]
        [TestCase(
            "protected void OnChanged",
            "private void OnChanged",
            TestName = "ProtectedExtensionRemovalFailsApiInventory"
        )]
        [TestCase(
            "public class Nested",
            "internal class Nested",
            TestName = "NestedTypeAccessibilityChangeFailsApiInventory"
        )]
        [TestCase(
            "public void MarkMalformed",
            "public readonly void MarkMalformed",
            TestName = "ReadonlyInstanceMethodChangeFailsApiInventory"
        )]
        public void PublicApiMutationChangesInventory(string original, string replacement)
        {
            string[] baseline = CaptureApiControl(ApiControlSource);
            string changed = ApiControlSource.Replace(
                original,
                replacement,
                StringComparison.Ordinal
            );
            Assert.AreNotEqual(ApiControlSource, changed, "The mutation must reach its subject.");
            string[] mutated = CaptureApiControl(changed);
            Assert.IsFalse(baseline.SequenceEqual(mutated, StringComparer.Ordinal));
        }

        [TestCase("System.ReadOnlySpan<byte>", "System.ReadOnlySpan<Missing>")]
        [TestCase("ref readonly int First", "ref readonly Missing First")]
        [TestCase("where T : class", "where T : Missing")]
        [TestCase(": System.IDisposable", ": Missing")]
        [TestCase("Maximum = 64", "Maximum = Missing.Value")]
        [TestCase("maximum = 64", "maximum = Missing.Value")]
        [TestCase("delegate int InventoryCallback", "delegate Missing InventoryCallback")]
        public void UnresolvedPublicApiCannotProduceACleanInventory(
            string original,
            string replacement
        )
        {
            CaptureApiControl(ApiControlSource);
            string changed = ApiControlSource.Replace(
                original,
                replacement,
                StringComparison.Ordinal
            );
            Assert.AreNotEqual(ApiControlSource, changed);
            Assert.Throws<AssertionException>(() =>
                CompatibilityInventory.Capture(
                    CompatibilityInventory.Compile(new[] { CSharpSyntaxTree.ParseText(changed) })
                )
            );
        }

        [Test]
        public void EquivalentApiConstantsAndExplicitDefaultConstructorPreserveInventory()
        {
            string equivalent = ApiControlSource
                .Replace("= 64", "= 1 << 6", StringComparison.Ordinal)
                .Replace(
                    "public class InventoryFactory { }",
                    "public class InventoryFactory { public InventoryFactory() { } }",
                    StringComparison.Ordinal
                );
            CollectionAssert.AreEqual(
                CaptureApiControl(ApiControlSource),
                CaptureApiControl(equivalent)
            );
        }

        [Test]
        public void PublicMembersInsideInaccessibleContainersAreNotExternalApi()
        {
            const string source =
                "namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto { internal class Hidden { public class Nested { public void Call() { } } } public class Visible { private class Hidden { public void Call() { } } } }";
            string[] entries = CaptureApiControl(source);
            Assert.IsFalse(
                entries.Any(entry => entry.Contains("Hidden", StringComparison.Ordinal))
            );
            Assert.IsTrue(
                entries.Any(entry => entry.Contains("Visible", StringComparison.Ordinal))
            );
        }

        [TestCase(
            "[InventoryMarker(10)]",
            "[InventoryMarker(10L)]",
            TestName = "AttributeConstructorOverloadChangeFailsApiInventory"
        )]
        [TestCase(
            "[InventoryMarker(10)]",
            "[InventoryMarker((object)10)]",
            TestName = "AttributeObjectConstructorChangeFailsApiInventory"
        )]
        [TestCase(
            "AttributeTargets.All",
            "AttributeTargets.Class | System.AttributeTargets.Method | System.AttributeTargets.ReturnValue | System.AttributeTargets.Parameter | System.AttributeTargets.GenericParameter",
            TestName = "AttributeTargetsChangeFailsApiInventory"
        )]
        [TestCase(
            "AllowMultiple = true",
            "AllowMultiple = false",
            TestName = "AttributeMultiplicityChangeFailsApiInventory"
        )]
        [TestCase(
            "Inherited = false",
            "Inherited = true",
            TestName = "AttributeInheritanceChangeFailsApiInventory"
        )]
        [TestCase(
            "[InventoryMarker(10)]",
            "[InventoryMarker(11)]",
            TestName = "PublicTypeAttributeChangeFailsApiInventory"
        )]
        [TestCase(
            "[InventoryMarker(20)]",
            "[InventoryMarker(21)]",
            TestName = "TypeParameterAttributeChangeFailsApiInventory"
        )]
        [TestCase(
            "[return: InventoryMarker(30)]",
            "[return: InventoryMarker(31)]",
            TestName = "ReturnAttributeChangeFailsApiInventory"
        )]
        [TestCase(
            "[InventoryMarker(40)]",
            "[InventoryMarker(41)]",
            TestName = "ParameterAttributeChangeFailsApiInventory"
        )]
        [TestCase(
            "Obsolete(\"old\", false)",
            "Obsolete(\"old\", true)",
            TestName = "ObsoleteErrorChangeFailsApiInventory"
        )]
        [TestCase(
            "Conditional(\"API_TRACE\")",
            "Conditional(\"OTHER_TRACE\")",
            TestName = "ConditionalCallChangeFailsApiInventory"
        )]
        [TestCase(
            "[System.Runtime.InteropServices.Optional]",
            "",
            TestName = "OptionalWithoutDefaultChangeFailsApiInventory"
        )]
        [TestCase(
            "where T : class",
            "where T : class?",
            TestName = "NullableGenericConstraintChangeFailsApiInventory"
        )]
        [TestCase(
            "where T : class",
            "where T : notnull",
            TestName = "NotNullGenericConstraintChangeFailsApiInventory"
        )]
        [TestCase(
            "where T : class",
            "where T : unmanaged",
            TestName = "UnmanagedGenericConstraintChangeFailsApiInventory"
        )]
        [TestCase(
            "where T : class",
            "where T : class, new()",
            TestName = "ConstructorGenericConstraintChangeFailsApiInventory"
        )]
        [TestCase(
            "public string Text",
            "public string? Text",
            TestName = "NullablePropertyChangeFailsApiInventory"
        )]
        [TestCase(
            "List<string>",
            "List<string?>",
            TestName = "NestedNullableFieldChangeFailsApiInventory"
        )]
        [TestCase(
            "public virtual void Override",
            "public void Override",
            TestName = "VirtualMemberChangeFailsApiInventory"
        )]
        [TestCase(
            "[InventoryMarker(10)]",
            "[InventoryMarker(10), InventoryMarker(10)]",
            TestName = "RepeatedPublicAttributeChangeFailsApiInventory"
        )]
        public void PublicApiMetadataMutationChangesInventory(string original, string replacement)
        {
            string[] baseline = CaptureApiControl(ApiMetadataControlSource);
            string changed = ApiMetadataControlSource.Replace(
                original,
                replacement,
                StringComparison.Ordinal
            );
            Assert.AreNotEqual(ApiMetadataControlSource, changed);
            Assert.IsFalse(
                baseline.SequenceEqual(CaptureApiControl(changed), StringComparer.Ordinal)
            );
        }

        [TestCase("[InventoryMarker(10)]", "[Missing(10)]")]
        [TestCase("[InventoryMarker(10)]", "[InventoryMarker(Missing.Value)]")]
        [TestCase("AllowMultiple = true", "AllowMultiple = Missing.Value")]
        [TestCase("AllowMultiple = true", "MissingProperty = true")]
        public void UnresolvedPublicApiAttributesCannotProduceACleanInventory(
            string original,
            string replacement
        )
        {
            CaptureApiControl(ApiMetadataControlSource);
            string changed = ApiMetadataControlSource.Replace(
                original,
                replacement,
                StringComparison.Ordinal
            );
            Assert.AreNotEqual(ApiMetadataControlSource, changed);
            Assert.Throws<AssertionException>(() =>
                CompatibilityInventory.Capture(
                    CompatibilityInventory.Compile(new[] { CSharpSyntaxTree.ParseText(changed) })
                )
            );
        }

        [Test]
        public void BoxedAttributeArgumentTypeChangeFailsApiInventory()
        {
            string source = ApiMetadataControlSource.Replace(
                "[InventoryMarker(10)]",
                "[InventoryMarker((object)10)]",
                StringComparison.Ordinal
            );
            string changed = source.Replace(
                "(object)10",
                "(object)(System.DayOfWeek)10",
                StringComparison.Ordinal
            );
            Assert.IsFalse(
                CaptureApiControl(source)
                    .SequenceEqual(CaptureApiControl(changed), StringComparer.Ordinal)
            );
        }

        [Test]
        public void InheritedAttributeUsageMatchesExplicitEffectivePolicy()
        {
            const string source =
                "namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto { [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = true, Inherited = false)] public class InventoryBaseAttribute : System.Attribute { } public sealed class InventoryDerivedAttribute : InventoryBaseAttribute { } }";
            string explicitPolicy = source.Replace(
                "public sealed class",
                "[System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = true, Inherited = false)] public sealed class",
                StringComparison.Ordinal
            );
            CollectionAssert.AreEqual(CaptureApiControl(source), CaptureApiControl(explicitPolicy));
        }

        [Test]
        public void AttributeUsageDefaultsAndArgumentOrderPreserveEffectiveApi()
        {
            string source = ApiMetadataControlSource.Replace(
                "AllowMultiple = true, Inherited = false",
                "AllowMultiple = false, Inherited = true",
                StringComparison.Ordinal
            );
            string omitted = source.Replace(
                ", AllowMultiple = false, Inherited = true",
                "",
                StringComparison.Ordinal
            );
            string reordered = source.Replace(
                "AllowMultiple = false, Inherited = true",
                "Inherited = true, AllowMultiple = false",
                StringComparison.Ordinal
            );
            CollectionAssert.AreEqual(CaptureApiControl(source), CaptureApiControl(omitted));
            CollectionAssert.AreEqual(CaptureApiControl(source), CaptureApiControl(reordered));
        }

        [Test]
        public void ImplicitAttributeUsageMatchesExplicitDefaultPolicy()
        {
            const string source =
                "namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto { public class InventoryDefaultAttribute : System.Attribute { } }";
            string explicitPolicy = source.Replace(
                "public class",
                "[System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = false, Inherited = true)] public class",
                StringComparison.Ordinal
            );
            CollectionAssert.AreEqual(CaptureApiControl(source), CaptureApiControl(explicitPolicy));
        }

        [Test]
        public void ImplementationBodiesAndInliningDoNotChangePublicSourceApi()
        {
            string changed = ApiMetadataControlSource
                .Replace("return value;", "return value + 1;", StringComparison.Ordinal)
                .Replace(
                    "public int Read",
                    "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)] public int Read",
                    StringComparison.Ordinal
                );
            CollectionAssert.AreEqual(
                CaptureApiControl(ApiMetadataControlSource),
                CaptureApiControl(changed)
            );
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
