// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.OdinMigration.Generator;

    [TestFixture]
    public sealed class OdinMigrationBindingGeneratorTests
    {
        private const string UnityTypes =
            "namespace UnityEngine { public class Object {} public class ScriptableObject : Object {} public class MonoBehaviour : Object {} public class Texture : Object {} public sealed class SerializeField : System.Attribute {} public sealed class SerializeReference : System.Attribute {} } namespace UnityEditor { public sealed class InitializeOnLoadMethodAttribute : System.Attribute {} } namespace Sirenix.Serialization { public sealed class OdinSerializeAttribute : System.Attribute {} } namespace Sirenix.OdinInspector { public class SerializedScriptableObject : UnityEngine.ScriptableObject {} public class SerializedMonoBehaviour : UnityEngine.MonoBehaviour {} } namespace WallstopStudios.UnityHelpers.Core.DataStructure.Adapters { public class SerializableDictionary<TKey,TValue> : System.Collections.Generic.Dictionary<TKey,TValue> { public SerializableDictionary() {} public SerializableDictionary(System.Collections.Generic.IDictionary<TKey,TValue> original) : base(original) {} } }";

        internal static GeneratorDriverRunResult Generate(
            string[] sources,
            bool editor,
            out Compilation updated
        )
        {
            CSharpParseOptions options = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols: editor ? new[] { "UNITY_EDITOR" } : Array.Empty<string>()
            );
            List<SyntaxTree> trees = new List<SyntaxTree>();
            trees.Add(CSharpSyntaxTree.ParseText(UnityTypes, options, "UnityTypes.cs"));
            string root = ContractMirrorTests.RepositoryRoot();
            foreach (
                string name in new[]
                {
                    "OdinMigrationCompiledSource",
                    "OdinMigrationBindingAttribute",
                    "OdinMigrationCompiledField",
                    "OdinMigrationCompiledBinding",
                    "OdinMigrationCompiledBindings",
                    "OdinMigrationCompiledSourceEdit",
                    "OdinMigrationDictionaryCapture",
                    "OdinMigrationDictionaryRestore",
                    "OdinMigrationLiveDictionary",
                }
            )
            {
                string path = Path.Combine(
                    root,
                    "Runtime/Core/Serialization/OdinMigration",
                    name + ".cs"
                );
                trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(path), options, path));
            }
            string comparerPath = Path.Combine(
                root,
                "Runtime/Core/Helper/ReferenceIdentityComparer.cs"
            );
            trees.Add(
                CSharpSyntaxTree.ParseText(File.ReadAllText(comparerPath), options, comparerPath)
            );
            for (int index = 0; index < sources.Length; ++index)
            {
                trees.Add(
                    CSharpSyntaxTree.ParseText(
                        sources[index],
                        options,
                        "Assets/Source" + index + ".cs"
                    )
                );
            }
            List<MetadataReference> references = new List<MetadataReference>();
            string platform = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            foreach (string path in platform.Split(Path.PathSeparator))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            CSharpCompilation compilation = CSharpCompilation.Create(
                nameof(OdinMigrationBindingGeneratorTests),
                trees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new OdinMigrationBindingGenerator() },
                parseOptions: options
            );
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out updated,
                out System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics
            );
            Assert.IsEmpty(diagnostics);
            foreach (Diagnostic diagnostic in updated.GetDiagnostics())
            {
                Assert.AreNotEqual(
                    DiagnosticSeverity.Error,
                    diagnostic.Severity,
                    diagnostic.ToString()
                );
            }
            return driver.GetRunResult();
        }

        private static string Generated(string[] sources)
        {
            GeneratorDriverRunResult result = Generate(sources, true, out Compilation _);
            Assert.AreEqual(1, result.GeneratedTrees.Length);
            return result.GeneratedTrees[0].GetText().ToString();
        }

        [TestCase("private", false)]
        [TestCase("protected", false)]
        [TestCase("public", true)]
        [TestCase("private", true)]
        public void ReferencedOwnersAdvertiseBindingsWithoutExposingPrivateFields(
            string access,
            bool readonlyField
        )
        {
            Generate(
                new[]
                {
                    "namespace Consumer { public partial class Parent : UnityEngine.ScriptableObject { "
                        + access
                        + (readonlyField ? " readonly " : " ")
                        + "System.Collections.Generic.Dictionary<System.DayOfWeek,System.Collections.Generic.List<UnityEngine.Texture>> Values = new(); } }",
                },
                true,
                out Compilation parent
            );
            using MemoryStream image = new MemoryStream();
            Microsoft.CodeAnalysis.Emit.EmitResult emitted = parent.Emit(image);
            Assert.IsTrue(emitted.Success, string.Join("\n", emitted.Diagnostics));
            CSharpParseOptions options = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols: new[] { "UNITY_EDITOR" }
            );
            List<MetadataReference> references = new List<MetadataReference>(parent.References);
            references.Add(MetadataReference.CreateFromImage(image.ToArray()));
            CSharpCompilation child = CSharpCompilation.Create(
                "ReferencedConsumer",
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        "namespace Consumer { public sealed class Child : Parent {} }",
                        options,
                        "Child.cs"
                    ),
                },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new OdinMigrationBindingGenerator() },
                parseOptions: options
            );
            driver = driver.RunGeneratorsAndUpdateCompilation(
                child,
                out Compilation updated,
                out System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics
            );
            Assert.IsEmpty(diagnostics);
            foreach (Diagnostic diagnostic in updated.GetDiagnostics())
            {
                Assert.AreNotEqual(
                    DiagnosticSeverity.Error,
                    diagnostic.Severity,
                    diagnostic.ToString()
                );
            }
            GeneratorDriverRunResult result = driver.GetRunResult();
            Assert.AreEqual(1, result.GeneratedTrees.Length);
            string generated = result.GeneratedTrees[0].GetText().ToString();
            StringAssert.Contains("typeof(global::Consumer.Child)", generated);
            StringAssert.Contains("}, typeof(global::Consumer.Parent)", generated);
            StringAssert.DoesNotContain("original.@Values", generated);
        }

        [Test]
        public void ExistingBindingConstructorKeepsItsBinarySignatureAndOptionalArguments()
        {
            Generate(Array.Empty<string>(), true, out Compilation compilation);
            INamedTypeSymbol binding = compilation.GetTypeByMetadataName(
                "WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration.OdinMigrationCompiledBinding"
            );
            bool found = false;
            foreach (IMethodSymbol constructor in binding.InstanceConstructors)
            {
                if (constructor.Parameters.Length != 5)
                {
                    continue;
                }
                found = true;
                Assert.IsTrue(constructor.Parameters[3].IsOptional);
                Assert.IsTrue(constructor.Parameters[4].IsOptional);
            }
            Assert.IsTrue(
                found,
                "Existing compiled editor integrations call this five-argument constructor."
            );
        }

        [TestCase("private", false)]
        [TestCase("protected", false)]
        [TestCase("private protected", true)]
        [TestCase("private", true)]
        public void PartialOwnersCaptureAndRestoreInaccessibleDictionaryFields(
            string access,
            bool readonlyField
        )
        {
            string source =
                "namespace Consumer { public partial class Asset : UnityEngine.ScriptableObject { "
                + access
                + " enum Key { A=0 } "
                + access
                + (readonlyField ? " readonly " : " ")
                + "System.Collections.Generic.Dictionary<Key,System.Collections.Generic.List<UnityEngine.Texture>> Values = new(); } }";
            string generated = Generated(new[] { source });
            StringAssert.Contains("partial class @Asset", generated);
            StringAssert.Contains("TryCapture(original.@Values", generated);
            StringAssert.Contains(readonlyField ? "TryRestoreContents<" : "TryRebuild<", generated);
            StringAssert.Contains(
                "global::Consumer.Asset.CreateOdinMigrationCaptureValuesAt",
                generated
            );
            StringAssert.Contains(
                "global::Consumer.Asset.CreateOdinMigrationRestoreValuesAt",
                generated
            );
        }

        [TestCase(true)]
        [TestCase(false)]
        public void NestedPrivateAccessRequiresEveryContainingClassToBePartial(
            bool partialContainer
        )
        {
            string source =
                "namespace @event { public "
                + (partialContainer ? "partial " : string.Empty)
                + "class Container { public partial class Asset : UnityEngine.ScriptableObject { private enum Key { A=0 } private System.Collections.Generic.Dictionary<Key,System.Collections.Generic.List<UnityEngine.Texture>> Values; } } }";
            string generated = Generated(new[] { source });
            Assert.That(
                generated.Contains("partial class @Container"),
                Is.EqualTo(partialContainer)
            );
            Assert.That(
                generated.Contains("TryCapture(original.@Values"),
                Is.EqualTo(partialContainer)
            );
        }

        [Test]
        public void SharedPartialBaseEmitsOneAccessorPerFieldAcrossDerivedOwners()
        {
            string[] sources =
            {
                "public partial class Base : UnityEngine.ScriptableObject { private enum Key { A=0 } private System.Collections.Generic.Dictionary<Key,System.Collections.Generic.List<UnityEngine.Texture>> Values; }",
                "public partial class Base { public bool Flag; } public sealed class First : Base {} public sealed class Second : Base {}",
            };
            string generated = Generated(sources);
            Assert.That(
                generated
                    .Split(
                        new[]
                        {
                            "internal static global::WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration.OdinMigrationDictionaryCapture",
                        },
                        StringSplitOptions.None
                    )
                    .Length,
                Is.EqualTo(2)
            );
            StringAssert.Contains("typeof(global::First)", generated);
            StringAssert.Contains("typeof(global::Second)", generated);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AccessorFactoriesAvoidExistingConsumerMemberNames(bool inherited)
        {
            string source =
                (
                    inherited
                        ? "public partial class Parent : UnityEngine.ScriptableObject {} "
                        : string.Empty
                )
                + "public partial class Asset : "
                + (inherited ? "Parent" : "UnityEngine.ScriptableObject")
                + " { private enum Key { A=0 } private System.Collections.Generic.Dictionary<Key,System.Collections.Generic.List<UnityEngine.Texture>> Values; }";
            Generate(new[] { source }, true, out Compilation initial);
            string method = null;
            foreach (ISymbol member in initial.GetTypeByMetadataName("Asset").GetMembers())
            {
                if (
                    member is IMethodSymbol
                    && member.Name.StartsWith(
                        "CreateOdinMigrationCapture",
                        StringComparison.Ordinal
                    )
                )
                {
                    method = member.Name;
                    break;
                }
            }
            Assert.That(method, Is.Not.Null);
            string extra =
                "public partial class "
                + (inherited ? "Parent" : "Asset")
                + " { "
                + (inherited ? "protected" : "private")
                + " static object "
                + method
                + "() { return null; } }";
            string generated = Generated(new[] { source, extra });
            StringAssert.Contains(method + "1()", generated);
        }

        [Test]
        public void GeneratedFactoryNamesExcludeFieldUnderscores()
        {
            const string source =
                "public partial class Asset : UnityEngine.ScriptableObject { private enum Key { A=0 } private System.Collections.Generic.Dictionary<Key,System.Collections.Generic.List<UnityEngine.Texture>> _values; }";
            Generate(new[] { source }, true, out Compilation updated);
            int factories = 0;
            foreach (ISymbol member in updated.GetTypeByMetadataName("Asset").GetMembers())
            {
                if (
                    member is IMethodSymbol
                    && member.Name.StartsWith("CreateOdinMigration", StringComparison.Ordinal)
                )
                {
                    StringAssert.DoesNotContain("_", member.Name);
                    ++factories;
                }
            }
            Assert.That(factories, Is.EqualTo(2));
        }

        [Test]
        public void MigrationGeneratorHasAnIndependentCompilerAssembly()
        {
            Assert.AreNotSame(
                typeof(WProtoGenerator).Assembly,
                typeof(OdinMigrationBindingGenerator).Assembly
            );
            Assert.AreEqual(
                "WallstopStudios.UnityHelpers.OdinMigration.Generator",
                typeof(OdinMigrationBindingGenerator).Assembly.GetName().Name
            );
            foreach (
                System.Reflection.AssemblyName reference in typeof(OdinMigrationBindingGenerator).Assembly.GetReferencedAssemblies()
            )
            {
                Assert.AreNotEqual(typeof(WProtoGenerator).Assembly.GetName().Name, reference.Name);
            }
        }

        [TestCase("public", true)]
        [TestCase("internal", true)]
        [TestCase("protected internal", true)]
        [TestCase("private", false)]
        [TestCase("protected", false)]
        public void LiveDictionaryReadersUseOnlyAccessibleOriginalMembers(
            string accessibility,
            bool captured
        )
        {
            string source =
                "public enum Key { A=0 } public class Asset : UnityEngine.ScriptableObject { "
                + accessibility
                + " System.Collections.Generic.Dictionary<Key,System.Collections.Generic.List<UnityEngine.Texture>> Values; }";
            string generated = Generated(new[] { source });

            if (captured)
            {
                StringAssert.Contains("TryCapture(original.@Values, key => (int)key", generated);
                StringAssert.Contains("value is global::Asset original", generated);
                StringAssert.Contains("original.@Values = rebuilt", generated);
            }
            else
            {
                StringAssert.DoesNotContain("original.@Values", generated);
            }
            StringAssert.DoesNotContain("GetField", generated);
            StringAssert.DoesNotContain("GetValue", generated);
        }

        [Test]
        public void ReadonlyFieldsRestoreContentsWithoutAssigningTheField()
        {
            string generated = Generated(
                new[]
                {
                    "public enum Key { A=0 } public class Asset : UnityEngine.ScriptableObject { public readonly System.Collections.Generic.Dictionary<Key,System.Collections.Generic.List<UnityEngine.Texture>> Values; }",
                }
            );

            StringAssert.Contains("TryCapture(original.@Values", generated);
            StringAssert.Contains(
                "TryRestoreContents<global::Key, global::UnityEngine.Texture>(original.@Values",
                generated
            );
            StringAssert.DoesNotContain("original.@Values = rebuilt", generated);
        }

        [Test]
        public void LiveDictionaryReadersBindInheritedAndHiddenFieldsToTheirDeclaringTypes()
        {
            string generated = Generated(
                new[]
                {
                    "public enum Key { A=0 } public class Base : UnityEngine.ScriptableObject { public System.Collections.Generic.Dictionary<Key,System.Collections.Generic.List<UnityEngine.Texture>> Values; } public class Asset : Base { public new System.Collections.Generic.Dictionary<Key,System.Collections.Generic.List<UnityEngine.Texture>> Values; }",
                }
            );

            StringAssert.Contains("value is global::Base original", generated);
            StringAssert.Contains("value is global::Asset original", generated);
        }

        [Test]
        public void AliasesAndNestedEnumsHaveCompilerResolvedIdentity()
        {
            const string Source =
                "using Map = System.Collections.Generic.Dictionary<Asset.Key, System.Collections.Generic.List<UnityEngine.Texture>>; public sealed class Asset : UnityEngine.ScriptableObject { internal enum Key { A = 0 } private Map Values; }";
            string generated = Generated(new[] { Source });
            StringAssert.Contains("\"Asset+Key\"", generated);
            StringAssert.Contains("\"UnityEngine.Texture\"", generated);
            StringAssert.Contains("\"Values\"", generated);
            StringAssert.Contains("typeof(global::Asset)", generated);
            StringAssert.DoesNotContain("typeof(global::Asset.Key)", generated);
            using SHA256 hash = SHA256.Create();
            string expected = Convert.ToBase64String(
                hash.ComputeHash(Encoding.UTF8.GetBytes(Source))
            );
            StringAssert.Contains(expected, generated);
        }

        [Test]
        public void PrivateNestedEnumBindingsDoNotNeedRuntimeTypeAccess()
        {
            string generated = Generated(
                new[]
                {
                    "public sealed class Asset : UnityEngine.ScriptableObject { private enum Key { A=0 } private System.Collections.Generic.Dictionary<Key,System.Collections.Generic.List<UnityEngine.Texture>> Values; }",
                }
            );
            StringAssert.Contains("\"Asset+Key\"", generated);
            StringAssert.Contains("\"Values\"", generated);
            StringAssert.DoesNotContain("typeof(global::Asset.Key)", generated);
        }

        [Test]
        public void PartialAndInheritedDeclarationsCaptureEveryOriginalSource()
        {
            string generated = Generated(
                new[]
                {
                    "using System.Collections.Generic; public enum Key { A=0 } public class Base : UnityEngine.ScriptableObject { public Dictionary<Key,List<UnityEngine.Texture>> Inherited; } public partial class Asset : Base {}",
                    "using System.Collections.Generic; public partial class Asset { public Dictionary<Key,List<UnityEngine.Texture>> Own; }",
                }
            );
            StringAssert.Contains("\"Assets/Source0.cs\"", generated);
            StringAssert.Contains("\"Assets/Source1.cs\"", generated);
            StringAssert.Contains("\"Inherited\"", generated);
            StringAssert.Contains("\"Own\"", generated);
            StringAssert.Contains(
                "new global::System.Type[] {typeof(global::Asset),typeof(global::Base),",
                generated
            );
        }

        [Test]
        public void ClosedGenericBaseFieldsUseTheirConcreteEnum()
        {
            string generated = Generated(
                new[]
                {
                    "using System.Collections.Generic; public enum Key { A=0 } public class Base<T> : UnityEngine.ScriptableObject where T:struct { public Dictionary<T,List<UnityEngine.Texture>> Values; } public sealed class Asset : Base<Key> {}",
                }
            );
            StringAssert.Contains("\"Key\"", generated);
            StringAssert.Contains("typeof(global::Asset)", generated);
            StringAssert.DoesNotContain("typeof(global::Base<T>)", generated);
            StringAssert.Contains("typeof(global::Base<global::Key>)", generated);
            StringAssert.Contains(
                "new global::System.Type[] {typeof(global::Asset),typeof(global::Base<global::Key>),",
                generated
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InheritedOnlyBindingsAlwaysIncludeTheOriginalUnityType(bool generic)
        {
            string source = generic
                ? "using System.Collections.Generic; public enum Key { A=0 } public class Base<T> : UnityEngine.ScriptableObject where T:struct { public Dictionary<T,List<UnityEngine.Texture>> Values; } public class Intermediate : Base<Key> {} public sealed class Asset : Intermediate {}"
                : "using System.Collections.Generic; public enum Key { A=0 } public class Base : UnityEngine.ScriptableObject { public Dictionary<Key,List<UnityEngine.Texture>> Values; } public class Intermediate : Base {} public sealed class Asset : Intermediate {}";
            string generated = Generated(new[] { source });
            string owner = generic ? "global::Base<global::Key>" : "global::Base";
            StringAssert.Contains(
                "new global::System.Type[] {typeof(global::Asset),typeof(" + owner + "),",
                generated
            );
            StringAssert.Contains(
                "new global::System.Type[] {typeof(" + owner + "),typeof(global::Intermediate),",
                generated
            );
        }

        [Test]
        public void SeparateEnumElementAndBasePartsAreCaptured()
        {
            string generated = Generated(
                new[]
                {
                    "using System.Collections.Generic; public partial class Base : UnityEngine.ScriptableObject { public Dictionary<Key,List<Element>> Values; } public sealed class Asset : Base {}",
                    "public enum Key { A=0 }",
                    "public sealed class Element : UnityEngine.Texture {}",
                    "public partial class Base { public int Other; }",
                }
            );
            for (int index = 0; index < 4; ++index)
            {
                StringAssert.Contains("\"Assets/Source" + index + ".cs\"", generated);
            }
        }

        [Test]
        public void ConstructedKeyAndElementNamesContainAllRuntimeArguments()
        {
            string generated = Generated(
                new[]
                {
                    "using System.Collections.Generic; public class Keys<T> { public enum Key { A=0 } } public class Element<T> : UnityEngine.Texture {} public sealed class Asset : UnityEngine.ScriptableObject { public Dictionary<Keys<List<int[]>>.Key,List<Element<Dictionary<string,int[,]>>>> Values; }",
                }
            );
            StringAssert.Contains("\"Keys`1+Key\" + \"[[\"", generated);
            StringAssert.Contains(
                "typeof(global::System.Collections.Generic.List<int[]>).AssemblyQualifiedName",
                generated
            );
            StringAssert.Contains("\"Element`1\" + \"[[\"", generated);
            StringAssert.Contains(
                "typeof(global::System.Collections.Generic.Dictionary<string, int[,]>).AssemblyQualifiedName",
                generated
            );
        }

        [Test]
        public void PrivateGenericArgumentsNeverNeedAnInaccessibleTypeLiteral()
        {
            string generated = Generated(
                new[]
                {
                    "using System.Collections.Generic; public sealed class Asset : UnityEngine.ScriptableObject { private class Hidden {} private class Holder<T> { public enum Key { A=0 } } private Dictionary<Holder<Hidden>.Key,List<UnityEngine.Texture>> Values; }",
                }
            );
            StringAssert.Contains("\"Asset+Holder`1+Key\" + \"[[\"", generated);
            StringAssert.Contains("\"Asset+Hidden\"", generated);
            StringAssert.DoesNotContain("typeof(global::Asset.Hidden)", generated);
            StringAssert.DoesNotContain("typeof(global::Asset.Holder<", generated);
        }

        [TestCase(
            "public enum Key : long { A=0 } public class Asset : UnityEngine.ScriptableObject { public System.Collections.Generic.Dictionary<Key,System.Collections.Generic.List<UnityEngine.Texture>> Values; }"
        )]
        [TestCase(
            "public enum Key { A=0 } public class Asset { public System.Collections.Generic.Dictionary<Key,System.Collections.Generic.List<UnityEngine.Texture>> Values; }"
        )]
        [TestCase(
            "public enum Key { A=0 } public class Asset : UnityEngine.ScriptableObject { public static System.Collections.Generic.Dictionary<Key,System.Collections.Generic.List<UnityEngine.Texture>> Values; }"
        )]
        public void UnsupportedDeclarationsDoNotReceiveAFieldBinding(string source)
        {
            GeneratorDriverRunResult result = Generate(new[] { source }, true, out Compilation _);
            Assert.AreEqual(0, result.GeneratedTrees.Length);
        }

        [Test]
        public void PlayerCompilationDoesNotReceiveEditorBindings()
        {
            GeneratorDriverRunResult result = Generate(
                new[]
                {
                    "public enum Key { A=0 } public class Asset : UnityEngine.ScriptableObject { public System.Collections.Generic.Dictionary<Key,System.Collections.Generic.List<UnityEngine.Texture>> Values; }",
                },
                false,
                out Compilation _
            );
            Assert.AreEqual(0, result.GeneratedTrees.Length);
        }
    }
}
