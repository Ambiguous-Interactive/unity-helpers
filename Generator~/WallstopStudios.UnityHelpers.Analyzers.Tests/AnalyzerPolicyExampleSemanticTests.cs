// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Diagnostics;
    using NUnit.Framework;
#if UNITY_EDITOR
    using WallstopStudios.UnityHelpers.Editor.Tools;
#endif

#if UNITY_EDITOR
    [TestFixture]
    public sealed class AnalyzerPolicyExampleSemanticTests
    {
        private const string Imports =
            @"using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using NUnit.Framework;
using UnityEngine;
using WallstopStudios.UnityHelpers.Tags;
using WallstopStudios.UnityHelpers.Utils;
using WallstopStudios.UnityHelpers.Core.Random;";

        private const string Surroundings =
            @"
public class Item { public void Refresh() { } }
public class Value { }
[Serializable] public class SerializableDictionary<K, V> { public V[] values; }
[Serializable] public class SerializableList<T> { public List<T> values; }
namespace UnityEngine {
    public class Object {
        public static bool operator ==(Object a, Object b) { return ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !ReferenceEquals(a, b); }
        public override bool Equals(object other) { return base.Equals(other); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    public class GameObject : Object {
        public void SetActive(bool active) { }
        public WallstopStudios.UnityHelpers.Tags.EffectHandle? ApplyEffect(Item effect) { return null; }
    }
    public class Component : Object {
        public GameObject gameObject;
        public T GetComponent<T>() where T : Component { return null; }
        public bool TryGetComponent<T>(out T value) where T : Component { value = null; return false; }
    }
    public class MonoBehaviour : Component {
        protected virtual void OnDestroy() { }
        public Coroutine StartCoroutine(IEnumerator routine) { return null; }
    }
    public class SpriteRenderer : Component { }
    public class Coroutine { }
    public sealed class SerializeField : Attribute { }
    public static class Random { public static float Range(float min, float max) { return min; } }
}
namespace WallstopStudios.UnityHelpers.Tags { public struct EffectHandle { } }
namespace WallstopStudios.UnityHelpers.Utils {
    public sealed class SerializedStringComparer : IEqualityComparer<string> {
        public enum StringCompareMode { Ordinal, OrdinalIgnoreCase }
        public StringCompareMode compareMode;
        public bool Equals(string a, string b) { return string.Equals(a, b, StringComparison.Ordinal); }
        public int GetHashCode(string value) { return value.GetHashCode(); }
        public IEqualityComparer<string> Freeze() { return StringComparer.Ordinal; }
    }
}
namespace WallstopStudios.UnityHelpers.Core.Random {
    public interface IRandom { float NextFloat(float min, float max); }
    public sealed class PcgRandom : IRandom {
        public PcgRandom(long seed) { }
        public float NextFloat(float min, float max) { return min; }
    }
}";

        private static IEnumerable<TestCaseData> Cases()
        {
            for (int number = 1; number <= 19; number++)
            {
                string id = $"WUH{number:000}";
                yield return new TestCaseData(id).SetName(id + "ExamplesMatchTheActualDiagnostic");
            }
        }

        private static DiagnosticAnalyzer AnalyzerFor(string id)
        {
            switch (id)
            {
                case "WUH001":
                    return new CacheFactoryAnalyzer();
                case "WUH002":
                    return new NestedCollectionAnalyzer();
                case "WUH003":
                case "WUH004":
                    return new UnityObjectNullAnalyzer();
                case "WUH005":
                    return new UnityRandomAnalyzer();
                case "WUH006":
                case "WUH007":
                    return new DiscardedHandleAnalyzer();
                case "WUH008":
                    return new UntestedTryOutAnalyzer();
                case "WUH009":
                    return new TeardownBaseCallAnalyzer();
                case "WUH010":
                    return new DictionaryIndexerAnalyzer();
                case "WUH011":
                    return new SerializedStringComparerMutationAnalyzer();
                case "WUH012":
                    return new SerializedRowDereferenceAnalyzer();
                case "WUH013":
                    return new CountingLoopAnalyzer();
                case "WUH014":
                    return new DisposableStructAssignmentAnalyzer();
                case "WUH015":
                    return new UnityLifecycleAnalyzer();
                case "WUH016":
                    return new UnityMessageInheritanceAnalyzer();
                case "WUH017":
                    return new GetComponentNullComparisonAnalyzer();
                case "WUH018":
                    return new StringEqualityAnalyzer();
                case "WUH019":
                    return new LoopBoundAnalyzer();
                default:
                    Assert.Fail("Unmapped diagnostic " + id);
                    return null;
            }
        }

        private static string SourceFor(string id, string code)
        {
            string fields = string.Empty;
            string members = string.Empty;
            string body = code;
            switch (id)
            {
                case "WUH001":
                    fields =
                        "private ConcurrentDictionary<string, Value> cache = new(); private string key; private static Value CreateValue(string key) { return new Value(); }";
                    if (code.StartsWith("private static", StringComparison.Ordinal))
                    {
                        int split = code.IndexOf(';');
                        members = code.Substring(0, split + 1);
                        body = code.Substring(split + 1);
                    }
                    body = "private Value Run() { " + body + " }";
                    break;
                case "WUH002":
                case "WUH009":
                case "WUH015":
                    members = code;
                    body = string.Empty;
                    break;
                case "WUH003":
                case "WUH004":
                    fields = "private Component component;";
                    break;
                case "WUH005":
                    if (code.StartsWith("private readonly", StringComparison.Ordinal))
                    {
                        int split = code.IndexOf(';');
                        members = code.Substring(0, split + 1);
                        body = code.Substring(split + 1);
                    }
                    break;
                case "WUH006":
                    fields =
                        "private GameObject player; private Item immobilize; private EffectHandle? immobilizeHandle;";
                    break;
                case "WUH007":
                    fields =
                        "private Coroutine refreshRoutine; private IEnumerator RefreshRoutine() { yield break; }";
                    break;
                case "WUH008":
                    fields = "private Dictionary<string, Item> map = new(); private string key;";
                    break;
                case "WUH010":
                    fields =
                        "private Dictionary<string, Item> items = new(); private string key; private void Use(Item item) { }";
                    break;
                case "WUH012":
                    int fieldEnd = code.IndexOf("\n\n", StringComparison.Ordinal);
                    members = code.Substring(0, fieldEnd);
                    body = code.Substring(fieldEnd + 2);
                    break;
                case "WUH013":
                    fields =
                        "private Item[] items = Array.Empty<Item>(); private void Use(Item item) { }";
                    break;
                case "WUH014":
                    return Imports + "\n" + code + "\n" + Surroundings;
                case "WUH016":
                    return Imports
                        + "\n"
                        + code.Replace(
                            "class Parent : MonoBehaviour\n{",
                            "class Parent : MonoBehaviour\n{\n    protected void Initialize() { }"
                        )
                        + "\n"
                        + Surroundings;
                case "WUH017":
                    fields = "private void Render() { }";
                    break;
                case "WUH018":
                    fields = "private string mode; private void Read() { }";
                    break;
                case "WUH019":
                    fields = "private int[] rows = new int[4]; private int[] output = new int[4];";
                    break;
            }
            if (!string.Equals(id, "WUH001", StringComparison.Ordinal))
            {
                body = "private void Run() { " + body + " }";
            }
            return Imports
                + "\npublic class Subject : MonoBehaviour { "
                + fields
                + " "
                + members
                + " private void ReleaseResources() { } private void Initialize() { } "
                + body
                + " }\n"
                + Surroundings;
        }

        private static ImmutableArray<Diagnostic> Analyze(string id, string code)
        {
            string source = SourceFor(id, code);
            List<MetadataReference> references = new();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            CSharpCompilation compilation = CSharpCompilation.Create(
                "PolicyExample",
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        source,
                        new CSharpParseOptions(LanguageVersion.CSharp9)
                    ),
                },
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary
                ).WithSpecificDiagnosticOptions(
                    new Dictionary<string, ReportDiagnostic> { [id] = ReportDiagnostic.Warn }
                )
            );
            Assert.IsEmpty(
                compilation
                    .GetDiagnostics()
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.ToString())
                    .ToArray(),
                source
            );
            return compilation
                .WithAnalyzers(ImmutableArray.Create(AnalyzerFor(id)))
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryExampleReportsItsPolicyAndItsCorrectionDoesNot(string id)
        {
            Assert.IsTrue(AnalyzerPolicyExamples.TryGet(id, out string bad, out string good));
            Assert.That(
                Analyze(id, bad)
                    .Count(diagnostic =>
                        string.Equals(diagnostic.Id, id, StringComparison.Ordinal)
                    ),
                Is.EqualTo(1),
                bad
            );
            Assert.IsEmpty(
                Analyze(id, good)
                    .Where(diagnostic => string.Equals(diagnostic.Id, id, StringComparison.Ordinal))
                    .Select(diagnostic => diagnostic.ToString())
                    .ToArray(),
                good
            );
        }
    }
#endif
}
