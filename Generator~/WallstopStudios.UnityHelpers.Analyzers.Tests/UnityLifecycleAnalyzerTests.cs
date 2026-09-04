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

    /// <summary>
    /// Exercises lifecycle signatures with semantic rather than textual type matching.
    /// </summary>
    [TestFixture]
    public sealed class UnityLifecycleAnalyzerTests
    {
        private const string UnityTypes =
            @"
namespace UnityEngine
{
    public class Object { }
    public class MonoBehaviour : Object { }
    public class ScriptableObject : Object { }
}
namespace WallstopStudios.UnityHelpers.Tests.Core
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method)]
    public sealed class SuppressAnalyzerAttribute : System.Attribute { }
}";

        [TestCase("static void Awake() {}")]
        [TestCase("int Awake() => 1;")]
        [TestCase("void Update(int count) {}")]
        [TestCase("void FixedUpdate(int count) {}")]
        [TestCase("void LateUpdate(int count) {}")]
        [TestCase("void OnApplicationQuit(int count) {}")]
        [TestCase("void OnEnable<T>() {}")]
        [TestCase("System.Collections.IEnumerator OnDisable() { yield break; }")]
        [TestCase("System.Collections.Generic.IEnumerator<int> Start() { yield break; }")]
        [TestCase("System.Threading.Tasks.Task Start() => null;")]
        public void InvalidMonoBehaviourSignatureIsReported(string method)
        {
            Diagnostic[] diagnostics = Analyze(
                "class Subject : UnityEngine.MonoBehaviour { " + method + " }"
            );
            Assert.That(diagnostics, Has.Length.EqualTo(1));
            Assert.That(diagnostics[0].Id, Is.EqualTo("WUH015"));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        }

        [TestCase("void Awake() {}")]
        [TestCase("void Start() {}")]
        [TestCase("System.Collections.IEnumerator Start() { yield break; }")]
        [TestCase("void OnTriggerEnter(object collider) {}")]
        public void ValidOrOutOfScopeSignatureIsNotReported(string method)
        {
            Assert.That(
                Analyze("class Subject : UnityEngine.MonoBehaviour { " + method + " }"),
                Is.Empty
            );
        }

        [TestCase("Awake", 1)]
        [TestCase("OnEnable", 1)]
        [TestCase("OnDisable", 1)]
        [TestCase("OnDestroy", 1)]
        [TestCase("OnValidate", 1)]
        [TestCase("Reset", 1)]
        [TestCase("Update", 0)]
        [TestCase("Start", 0)]
        public void ScriptableObjectsOnlyUseTheirOwnCallbacks(string name, int expected)
        {
            Assert.That(
                Analyze(
                    "class Subject : UnityEngine.ScriptableObject { int " + name + "() => 1; }"
                ),
                Has.Length.EqualTo(expected)
            );
        }

        [Test]
        public void AliasedBaseAndPartialDeclarationsResolveSemantically()
        {
            Assert.That(
                Analyze(
                    @"using Base = UnityEngine.MonoBehaviour;
partial class Subject : Base { }
partial class Subject { int Awake() => 1; }"
                ),
                Has.Length.EqualTo(1)
            );
        }

        [Test]
        public void AliasedCoroutineReturnTypeIsAccepted()
        {
            Assert.That(
                Analyze(
                    @"using Coroutine = System.Collections.IEnumerator;
class Subject : UnityEngine.MonoBehaviour { Coroutine Start() { yield break; } }"
                ),
                Is.Empty
            );
        }

        [Test]
        public void GenericAncestorIsResolved()
        {
            Assert.That(
                Analyze(
                    @"class Base<T> : UnityEngine.MonoBehaviour { }
class Subject : Base<int> { int Awake() => 1; }"
                ),
                Has.Length.EqualTo(1)
            );
        }

        [TestCase("class MonoBehaviour {} class Subject : MonoBehaviour { int Awake() => 1; }")]
        [TestCase("class Subject { static int Awake() => 1; }")]
        [TestCase(
            "class Subject : UnityEngine.MonoBehaviour { void Run() { int Awake() => 1; Awake(); } }"
        )]
        [TestCase(
            "class Subject : UnityEngine.MonoBehaviour { class Nested { int Awake() => 1; } }"
        )]
        [TestCase(
            "class Subject : UnityEngine.MonoBehaviour { string text = @\"static int Awake() => 1;\"; }"
        )]
        [TestCase(
            "class Subject : UnityEngine.MonoBehaviour {\n#if NEVER_ENABLED\nint Awake() => 1;\n#endif\n}"
        )]
        [TestCase(
            "interface ICallback { int Awake(); } class Subject : UnityEngine.MonoBehaviour, ICallback { int ICallback.Awake() => 1; }"
        )]
        public void SimilarTextOutsideUnityCallbacksIsNotReported(string source)
        {
            Assert.That(Analyze(source), Is.Empty);
        }

        [TestCase(
            "[WallstopStudios.UnityHelpers.Tests.Core.SuppressAnalyzer] class Subject : UnityEngine.MonoBehaviour { int Awake() => 1; }"
        )]
        [TestCase(
            "class Subject : UnityEngine.MonoBehaviour { [WallstopStudios.UnityHelpers.Tests.Core.SuppressAnalyzer] int Awake() => 1; }"
        )]
        [TestCase(
            "#pragma warning disable WUH015\nclass Subject : UnityEngine.MonoBehaviour { int Awake() => 1; }"
        )]
        public void ExistingAndCompilerSuppressionsAreRespected(string source)
        {
            Assert.That(Analyze(source), Is.Empty);
        }

        [Test]
        public void UnrelatedSuppressionAttributeDoesNotHideCallbackErrors()
        {
            Assert.That(
                Analyze(
                    @"class SuppressAnalyzerAttribute : System.Attribute { }
[SuppressAnalyzer] class Subject : UnityEngine.MonoBehaviour { int Awake() => 1; }"
                ),
                Has.Length.EqualTo(1)
            );
        }

        private static Diagnostic[] Analyze(string source)
        {
            List<MetadataReference> references = new List<MetadataReference>();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }

            CSharpCompilation compilation = CSharpCompilation.Create(
                "LifecycleFixture",
                new[]
                {
                    CSharpSyntaxTree.ParseText(UnityTypes),
                    CSharpSyntaxTree.ParseText(source),
                },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            Assert.That(
                compilation
                    .GetDiagnostics()
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                Is.Empty
            );
            return compilation
                .WithAnalyzers(
                    ImmutableArray.Create<DiagnosticAnalyzer>(new UnityLifecycleAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult()
                .ToArray();
        }
    }
}
