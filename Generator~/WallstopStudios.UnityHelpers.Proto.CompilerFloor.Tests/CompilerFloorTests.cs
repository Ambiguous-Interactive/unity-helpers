// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.CompilerFloor.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection.Metadata;
    using System.Reflection.PortableExecutable;
    using System.Runtime.InteropServices;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Proto.Generator;

    [TestFixture]
    public sealed class CompilerFloorTests
    {
        private const string AttributeName =
            "System.Runtime.CompilerServices.ModuleInitializerAttribute";
        private const string PolyfillHint = "WProtoModuleInitializerAttribute.g.cs";
        private const string AttributeUse =
            "[global::System.Runtime.CompilerServices.ModuleInitializer]";
        private const string InternalAttribute =
            "namespace System.Runtime.CompilerServices { [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)] internal sealed class ModuleInitializerAttribute : global::System.Attribute { } }";
        private const string PublicAttribute =
            "namespace System.Runtime.CompilerServices { [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)] public sealed class ModuleInitializerAttribute : global::System.Attribute { } }";
        private const string ConsumerSource =
            "namespace CompilerFloorConsumer { using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto; [WProtoContract] public partial class Save { [WProtoMember(1)] public int Score; } }";

        private static readonly string EvidenceDirectory = Path.Combine(
            Environment.GetEnvironmentVariable("WPROTO_COMPILER_EVIDENCE_ROOT")
                ?? Path.Combine(AppContext.BaseDirectory, "compiler-evidence"),
            Guid.NewGuid().ToString("N")
        );
        private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(
            LanguageVersion.CSharp9
        );
        private static readonly CSharpCompilationOptions CompilationOptions =
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                deterministic: true
            );
        private static readonly MetadataReference[] References = CreateReferences();

        private static IEnumerable<TestCaseData> AttributeCases()
        {
            yield return new TestCaseData(string.Empty, string.Empty, true).SetName(
                "Polyfill.Missing"
            );
            yield return new TestCaseData(string.Empty, InternalAttribute, false).SetName(
                "Polyfill.SourceInternal"
            );
            yield return new TestCaseData(string.Empty, PublicAttribute, false).SetName(
                "Polyfill.SourcePublic"
            );
            yield return new TestCaseData(PublicAttribute, string.Empty, false).SetName(
                "Polyfill.DependencyPublic"
            );
            yield return new TestCaseData(InternalAttribute, string.Empty, true).SetName(
                "Polyfill.DependencyInternal"
            );
            yield return new TestCaseData(
                "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"CompilerFloorConsumer\")] "
                    + InternalAttribute,
                string.Empty,
                false
            ).SetName("Polyfill.DependencyFriendInternal");
            yield return new TestCaseData(InternalAttribute, InternalAttribute, false).SetName(
                "Polyfill.DependencyInternalWithLocalDefinition"
            );
        }

        private static MetadataReference[] CreateReferences()
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "NetStandardReferences");
            string[] paths = Directory.GetFiles(directory, "*.dll");
            Array.Sort(paths, StringComparer.Ordinal);
            Assert.IsTrue(
                80 < paths.Length,
                "The real .NET Standard reference set must be present."
            );
            List<MetadataReference> references = new List<MetadataReference>(paths.Length + 1);
            foreach (string path in paths)
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            references.Add(
                MetadataReference.CreateFromFile(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "CompilerReferences",
                        "WallstopStudios.UnityHelpers.TypeCheck.dll"
                    )
                )
            );
            return references.ToArray();
        }

        private static byte[] Emit(CSharpCompilation compilation)
        {
            using MemoryStream stream = new MemoryStream();
            Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
            SaveArtifact(
                compilation.AssemblyName + ".diagnostics.txt",
                Encoding.UTF8.GetBytes(string.Join("\n", result.Diagnostics))
            );
            Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
            Assert.IsTrue(0 < stream.Length);
            byte[] binary = stream.ToArray();
            SaveArtifact(compilation.AssemblyName + ".dll", binary);
            return binary;
        }

        private static object FileIdentity(string path)
        {
            using SHA256 hash = SHA256.Create();
            byte[] contents = File.ReadAllBytes(path);
            string directory = Path.Combine(EvidenceDirectory, "inputs");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, Path.GetFileName(path)), contents);
            return new
            {
                path,
                bytes = contents.Length,
                sha256 = Convert.ToHexString(hash.ComputeHash(contents)).ToLowerInvariant(),
            };
        }

        private static void SaveArtifact(string name, byte[] contents)
        {
            string directory = Path.Combine(EvidenceDirectory, TestContext.CurrentContext.Test.ID);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name);
            File.WriteAllBytes(path, contents);
            TestContext.AddTestAttachment(path);
        }

        private static bool HasModuleConstructor(byte[] binary)
        {
            using MemoryStream stream = new MemoryStream(binary);
            using PEReader portableExecutable = new PEReader(stream);
            MetadataReader metadata = portableExecutable.GetMetadataReader();
            foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
            {
                TypeDefinition type = metadata.GetTypeDefinition(handle);
                if (
                    !string.Equals(
                        metadata.GetString(type.Name),
                        "<Module>",
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }
                foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
                {
                    if (
                        string.Equals(
                            metadata.GetString(metadata.GetMethodDefinition(methodHandle).Name),
                            ".cctor",
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static (CSharpCompilation Compilation, GeneratorDriverRunResult Run) Generate(
            string dependencyAttribute,
            string sourceAttribute,
            string consumerSource = ConsumerSource
        )
        {
            SaveArtifact(
                "consumer.cs.txt",
                Encoding.UTF8.GetBytes(consumerSource + "\n" + sourceAttribute)
            );
            SaveArtifact("dependency.cs.txt", Encoding.UTF8.GetBytes(dependencyAttribute));
            List<MetadataReference> references = new List<MetadataReference>(References);
            if (!string.IsNullOrEmpty(dependencyAttribute))
            {
                CSharpCompilation dependency = CSharpCompilation.Create(
                    "PolyfillDependency",
                    new[] { CSharpSyntaxTree.ParseText(dependencyAttribute, ParseOptions) },
                    References,
                    CompilationOptions
                );
                references.Add(MetadataReference.CreateFromImage(Emit(dependency)));
            }
            CSharpCompilation compilation = CSharpCompilation.Create(
                "CompilerFloorConsumer",
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        consumerSource + "\n" + sourceAttribute,
                        ParseOptions
                    ),
                },
                references,
                CompilationOptions
            );
            if (string.IsNullOrEmpty(sourceAttribute) && string.IsNullOrEmpty(dependencyAttribute))
            {
                Assert.IsTrue(
                    compilation.GetTypeByMetadataName(AttributeName) == null,
                    "The target references must not supply the newer framework attribute."
                );
            }
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new WProtoGenerator() },
                parseOptions: ParseOptions
            );
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation output,
                out System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics
            );
            foreach (Diagnostic diagnostic in diagnostics)
            {
                Assert.AreNotEqual(
                    DiagnosticSeverity.Error,
                    diagnostic.Severity,
                    diagnostic.ToString()
                );
            }
            foreach (GeneratorRunResult result in driver.GetRunResult().Results)
            {
                foreach (GeneratedSourceResult generated in result.GeneratedSources)
                {
                    SaveArtifact(
                        generated.HintName + ".txt",
                        Encoding.UTF8.GetBytes(generated.SourceText.ToString())
                    );
                }
            }
            return ((CSharpCompilation)output, driver.GetRunResult());
        }

        [OneTimeSetUp]
        public void CaptureCompilerInputs()
        {
            Directory.CreateDirectory(EvidenceDirectory);
            List<object> references = new List<object>(References.Length);
            foreach (MetadataReference reference in References)
            {
                Assert.IsTrue(reference is PortableExecutableReference);
                references.Add(FileIdentity(((PortableExecutableReference)reference).FilePath));
            }
            object inputs = new
            {
                backend = RuntimeInformation.FrameworkDescription,
                languageVersion = ParseOptions.LanguageVersion.ToString(),
                targetReferences = "NETStandard.Library.Ref/2.1.0",
                compiler = FileIdentity(typeof(CSharpCompilation).Assembly.Location),
                common = FileIdentity(typeof(Compilation).Assembly.Location),
                generator = FileIdentity(typeof(WProtoGenerator).Assembly.Location),
                fixture = FileIdentity(typeof(CompilerFloorTests).Assembly.Location),
                references,
            };
            string path = Path.Combine(EvidenceDirectory, "compiler-inputs.json");
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(inputs, new JsonSerializerOptions { WriteIndented = true })
            );
            TestContext.AddTestAttachment(path);
        }

        [Test]
        public void TargetReferencesUseTheNetStandard21CoreLibrary()
        {
            CSharpCompilation compilation = CSharpCompilation.Create(
                "ReferenceIdentity",
                references: References,
                options: CompilationOptions
            );
            AssemblyIdentity identity = compilation
                .GetSpecialType(SpecialType.System_Object)
                .ContainingAssembly.Identity;
            Assert.AreEqual("netstandard", identity.Name);
            Assert.AreEqual(new Version(2, 1, 0, 0), identity.Version);
            Assert.IsTrue(compilation.GetTypeByMetadataName(AttributeName) == null);
        }

        [Test]
        public void CompilerApiIsThePinnedRoslyn38Floor()
        {
            Assert.AreEqual(
                new Version(3, 8, 0, 0),
                typeof(CSharpCompilation).Assembly.GetName().Version
            );
            Assert.AreEqual(
                new Version(3, 8, 0, 0),
                typeof(Compilation).Assembly.GetName().Version
            );
            Assert.AreEqual(LanguageVersion.CSharp9, ParseOptions.LanguageVersion);
            TestContext.Out.WriteLine(typeof(CSharpCompilation).Assembly.FullName);
            TestContext.Out.WriteLine(typeof(Compilation).Assembly.FullName);
        }

        [TestCaseSource(nameof(AttributeCases))]
        public void GeneratedConsumerEmitsWithOnlyAnAccessiblePolyfill(
            string dependencyAttribute,
            string sourceAttribute,
            bool expectedPolyfill
        )
        {
            (CSharpCompilation compilation, GeneratorDriverRunResult run) = Generate(
                dependencyAttribute,
                sourceAttribute
            );
            byte[] binary = Emit(compilation);
            int polyfills = 0;
            foreach (GeneratorRunResult result in run.Results)
            {
                foreach (GeneratedSourceResult source in result.GeneratedSources)
                {
                    if (string.Equals(source.HintName, PolyfillHint, StringComparison.Ordinal))
                    {
                        ++polyfills;
                    }
                }
            }
            Assert.AreEqual(expectedPolyfill ? 1 : 0, polyfills);
            Assert.IsTrue(
                HasModuleConstructor(binary),
                "Roslyn must emit an actual module initializer, not merely accept the attribute spelling."
            );
        }

        [Test]
        public void RemovingTheGeneratedPolyfillRefusesEmission()
        {
            (CSharpCompilation compilation, GeneratorDriverRunResult run) = Generate(
                string.Empty,
                string.Empty
            );
            foreach (SyntaxTree tree in run.GeneratedTrees)
            {
                if (tree.FilePath.EndsWith(PolyfillHint, StringComparison.Ordinal))
                {
                    using MemoryStream stream = new MemoryStream();
                    Microsoft.CodeAnalysis.Emit.EmitResult result = compilation
                        .RemoveSyntaxTrees(tree)
                        .Emit(stream);
                    Assert.IsFalse(result.Success);
                    StringAssert.Contains("CS0234", string.Join("\n", result.Diagnostics));
                    return;
                }
            }
            Assert.Fail("The generated polyfill was not found.");
        }

        [Test]
        public void RemovingTheInitializerUseRemovesTheModuleConstructor()
        {
            (CSharpCompilation compilation, GeneratorDriverRunResult run) = Generate(
                string.Empty,
                string.Empty
            );
            foreach (SyntaxTree tree in run.GeneratedTrees)
            {
                string original = tree.ToString();
                string changed = original.Replace(AttributeUse, string.Empty);
                if (!string.Equals(original, changed, StringComparison.Ordinal))
                {
                    CSharpCompilation control = compilation.ReplaceSyntaxTree(
                        tree,
                        CSharpSyntaxTree.ParseText(changed, ParseOptions)
                    );
                    Assert.IsFalse(HasModuleConstructor(Emit(control)));
                    return;
                }
            }
            Assert.Fail("The generated module initializer use was not found.");
        }

        [Test]
        public void AConsumerWithoutARegistrarNeedsNoPolyfill()
        {
            (CSharpCompilation compilation, GeneratorDriverRunResult run) = Generate(
                string.Empty,
                string.Empty,
                "namespace CompilerFloorConsumer { public sealed class Plain { } }"
            );
            Assert.IsEmpty(run.GeneratedTrees);
            Assert.IsFalse(HasModuleConstructor(Emit(compilation)));
        }
    }
}
