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
                    "OdinMigrationCompiledField",
                    "OdinMigrationCompiledBinding",
                    "OdinMigrationCompiledBindings",
                    "OdinMigrationCompiledSourceEdit",
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
