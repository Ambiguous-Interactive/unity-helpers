// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using NUnit.Framework;

    [TestFixture]
    public sealed class OdinMigrationRewriteGeneratorTests
    {
        private const string Header = "using System.Collections.Generic; public enum Key { A=0 } ";

        private static string[] Rewrite(string[] sources, out string metadata)
        {
            GeneratorDriverRunResult result = OdinMigrationBindingGeneratorTests.Generate(
                sources,
                true,
                out Compilation _
            );
            Assert.AreEqual(1, result.GeneratedTrees.Length);
            metadata = result.GeneratedTrees[0].GetText().ToString();
            List<(string Path, int Start, int Length, string Replacement)> edits =
                new List<(string, int, int, string)>();
            HashSet<string> distinct = new HashSet<string>(StringComparer.Ordinal);
            foreach (SyntaxNode node in result.GeneratedTrees[0].GetRoot().DescendantNodes())
            {
                if (
                    !(node is ObjectCreationExpressionSyntax creation)
                    || !creation
                        .Type.ToString()
                        .EndsWith(".OdinMigrationCompiledSourceEdit", StringComparison.Ordinal)
                )
                {
                    continue;
                }
                string path = (
                    (LiteralExpressionSyntax)creation.ArgumentList.Arguments[0].Expression
                )
                    .Token
                    .ValueText;
                int start = int.Parse(
                    creation.ArgumentList.Arguments[1].Expression.ToString(),
                    CultureInfo.InvariantCulture
                );
                int length = int.Parse(
                    creation.ArgumentList.Arguments[2].Expression.ToString(),
                    CultureInfo.InvariantCulture
                );
                string replacement = (
                    (LiteralExpressionSyntax)creation.ArgumentList.Arguments[3].Expression
                )
                    .Token
                    .ValueText;
                if (distinct.Add(path + ":" + start + ":" + length + ":" + replacement))
                {
                    edits.Add((path, start, length, replacement));
                }
            }
            edits.Sort((left, right) => right.Start.CompareTo(left.Start));
            string[] rewritten = (string[])sources.Clone();
            for (int index = 0; index < rewritten.Length; ++index)
            {
                string path = "Assets/Source" + index + ".cs";
                StringBuilder text = new StringBuilder(rewritten[index]);
                foreach ((string Path, int Start, int Length, string Replacement) edit in edits)
                {
                    if (string.Equals(path, edit.Path, StringComparison.Ordinal))
                    {
                        text.Remove(edit.Start, edit.Length);
                        text.Insert(edit.Start, edit.Replacement);
                    }
                }
                rewritten[index] = text.ToString();
            }
            return rewritten;
        }

        private static string CompilingRewrite(string source)
        {
            string[] rewritten = Rewrite(new[] { source }, out string _);
            OdinMigrationBindingGeneratorTests.Generate(rewritten, true, out Compilation _);
            return rewritten[0];
        }

        [TestCase("ScriptableObject")]
        [TestCase("MonoBehaviour")]
        public void OdinBaseAttributeAndInitializerBecomeUnitySerializedFields(string unityBase)
        {
            string source =
                "using Map=System.Collections.Generic.Dictionary<Key,System.Collections.Generic.List<UnityEngine.Texture>>; using Base=Sirenix.OdinInspector.Serialized"
                + unityBase
                + "; "
                + Header
                + "public sealed class Asset : Base { [Sirenix.Serialization.OdinSerialize] private readonly Map Values = new Map(); }";
            string rewritten = CompilingRewrite(source);
            StringAssert.Contains("global::UnityEngine." + unityBase, rewritten);
            StringAssert.Contains("global::UnityEngine.SerializeField", rewritten);
            StringAssert.Contains(
                "new global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.SerializableDictionary<",
                rewritten
            );
            StringAssert.DoesNotContain("readonly", rewritten);
            StringAssert.DoesNotContain("[Sirenix.Serialization.OdinSerialize]", rewritten);
        }

        [TestCase("[UnityEngine.SerializeField, Sirenix.Serialization.OdinSerialize]")]
        [TestCase("[Sirenix.Serialization.OdinSerialize, UnityEngine.SerializeField]")]
        [TestCase("[Sirenix.Serialization.OdinSerialize] [UnityEngine.SerializeField]")]
        public void ExistingSerializeFieldDoesNotGetADuplicateAttribute(string attributes)
        {
            string rewritten = CompilingRewrite(
                Header
                    + "public sealed class Asset : UnityEngine.ScriptableObject { "
                    + attributes
                    + " private Dictionary<Key,List<UnityEngine.Texture>> Values = new(); }"
            );
            StringAssert.DoesNotContain("OdinSerialize", rewritten);
            StringAssert.Contains("UnityEngine.SerializeField", rewritten);
        }

        [Test]
        public void CopyConstructorAndPrivateNestedEnumRemainAccessible()
        {
            string rewritten = CompilingRewrite(
                "using System.Collections.Generic; public sealed class Asset : UnityEngine.ScriptableObject { private enum Key { A=0 } private Dictionary<Key,List<UnityEngine.Texture>> Values = new Dictionary<Key,List<UnityEngine.Texture>>(new Dictionary<Key,List<UnityEngine.Texture>>()); }"
            );
            StringAssert.Contains("SerializableDictionary<global::Asset.Key", rewritten);
            StringAssert.Contains("new Dictionary<Key,List<UnityEngine.Texture>>()", rewritten);
        }

        [Test]
        public void SharedDeclarationAndSeparateInitializersUseOneTypeReplacement()
        {
            string rewritten = CompilingRewrite(
                Header
                    + "public sealed class Asset : UnityEngine.ScriptableObject { private Dictionary<Key,List<UnityEngine.Texture>> First = new(), Second = new Dictionary<Key,List<UnityEngine.Texture>>(); }"
            );
            StringAssert.Contains("First = new(), Second = new global::", rewritten);
            StringAssert.Contains("SerializeField", rewritten);
        }

        [Test]
        public void ClosedGenericInheritancePreservesTheBaseTypeParameter()
        {
            string rewritten = CompilingRewrite(
                Header
                    + "public class Base<T> : Sirenix.OdinInspector.SerializedScriptableObject where T:struct { protected Dictionary<T,List<UnityEngine.Texture>> Values = new Dictionary<T,List<UnityEngine.Texture>>(); } public sealed class Asset : Base<Key> {}"
            );
            StringAssert.Contains("SerializableDictionary<T,", rewritten);
            StringAssert.DoesNotContain("SerializableDictionary<global::Key,", rewritten);
            StringAssert.Contains("Base<Key>", rewritten);
        }

        [Test]
        public void ImplicitlyPrivateReadonlyFieldGetsOneNonoverlappingReplacement()
        {
            string rewritten = CompilingRewrite(
                Header
                    + "public sealed class Asset : UnityEngine.ScriptableObject { readonly Dictionary<Key,List<UnityEngine.Texture>> Values = new(); }"
            );
            StringAssert.DoesNotContain("readonly", rewritten);
            StringAssert.Contains("[global::UnityEngine.SerializeField]", rewritten);
        }

        [Test]
        public void ImplicitlyPrivateFieldCombinesItsAttributeAndTypeReplacement()
        {
            string rewritten = CompilingRewrite(
                Header
                    + "public sealed class Asset : UnityEngine.ScriptableObject { Dictionary<Key,List<UnityEngine.Texture>> Values = new(); }"
            );
            StringAssert.Contains(
                "[global::UnityEngine.SerializeField] global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.SerializableDictionary<",
                rewritten
            );
        }

        [TestCase(
            "public Dictionary<Key,List<UnityEngine.Texture>> Values = new(10);",
            "dictionary-copy constructor"
        )]
        [TestCase(
            "public Dictionary<Key,List<UnityEngine.Texture>> Values = new(10, null);",
            "dictionary-copy constructor"
        )]
        [TestCase(
            "public Dictionary<Key,List<UnityEngine.Texture>> Values = null;",
            "construct the original"
        )]
        [TestCase(
            "[System.NonSerialized] public Dictionary<Key,List<UnityEngine.Texture>> Values;",
            "NonSerialized"
        )]
        [TestCase(
            "[UnityEngine.SerializeReference] public Dictionary<Key,List<UnityEngine.Texture>> Values;",
            "SerializeReference"
        )]
        public void UnsupportedOriginalStateGetsAnExplicitRewriteFailure(
            string field,
            string expected
        )
        {
            Rewrite(
                new[]
                {
                    Header
                        + "public sealed class Asset : UnityEngine.ScriptableObject { "
                        + field
                        + " }",
                },
                out string metadata
            );
            StringAssert.Contains(expected, metadata);
        }
    }
}
