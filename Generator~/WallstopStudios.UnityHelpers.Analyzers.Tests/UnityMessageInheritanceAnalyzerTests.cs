// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Diagnostics;
    using NUnit.Framework;

    /// <summary>
    /// Verifies semantic callback inheritance across source and assembly boundaries.
    /// </summary>
    [TestFixture]
    public sealed class UnityMessageInheritanceAnalyzerTests
    {
        private const string UnityTypes =
            @"
namespace UnityEngine { public class MonoBehaviour {} public class ScriptableObject {} }
namespace WallstopStudios.UnityHelpers.Tests.Core {
[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method)]
public sealed class SuppressAnalyzerAttribute : System.Attribute {} }
";

        [TestCase(
            "class Base : UnityEngine.MonoBehaviour { private void Awake() {} } class Subject : Base { private void Awake() {} }",
            1
        )]
        [TestCase(
            "class Base : UnityEngine.MonoBehaviour { protected virtual void Awake() {} } class Subject : Base { protected override void Awake() { base.Awake(); } }",
            0
        )]
        [TestCase(
            "class Base : UnityEngine.MonoBehaviour { protected virtual void Awake() {} } class Subject : Base { protected new void Awake() {} }",
            1
        )]
        [TestCase(
            "class Base : UnityEngine.MonoBehaviour { protected void Awake() {} } class Middle : Base {} class Subject : Middle { private void Awake() {} }",
            1
        )]
        [TestCase(
            "class Base<T> : UnityEngine.MonoBehaviour { protected void Awake() {} } class Subject : Base<int> { private void Awake() {} }",
            1
        )]
        [TestCase(
            "class Base : UnityEngine.ScriptableObject { private void OnEnable() {} } class Subject : Base { private void OnEnable() {} }",
            1
        )]
        [TestCase(
            "class Base : UnityEngine.ScriptableObject { private void Update() {} } class Subject : Base { private void Update() {} }",
            0
        )]
        [TestCase(
            "class Base { private void Awake() {} } class Subject : Base { private void Awake() {} }",
            0
        )]
        [TestCase(
            "class Base : UnityEngine.MonoBehaviour { private void Helper() {} } class Subject : Base { private void Helper() {} }",
            0
        )]
        [TestCase(
            "class Base : UnityEngine.MonoBehaviour { private void Awake(int value) {} } class Subject : Base { private void Awake() {} }",
            0
        )]
        [TestCase(
            "class Base : UnityEngine.MonoBehaviour { private static void Awake() {} } class Subject : Base { private void Awake() {} }",
            0
        )]
        [TestCase(
            "class Base : UnityEngine.MonoBehaviour { protected void Start() {} } class Subject : Base { private System.Collections.IEnumerator Start() { yield break; } }",
            1
        )]
        [TestCase(
            "class Base : UnityEngine.MonoBehaviour { private void Awake() {} } [WallstopStudios.UnityHelpers.Tests.Core.SuppressAnalyzer] class Subject : Base { private void Awake() {} }",
            0
        )]
        [TestCase(
            "class Base : UnityEngine.MonoBehaviour { private void Awake() {} } class Subject : Base { [WallstopStudios.UnityHelpers.Tests.Core.SuppressAnalyzer] private void Awake() {} }",
            0
        )]
        [TestCase(
            "#pragma warning disable WUH016\nclass Base : UnityEngine.MonoBehaviour { private void Awake() {} } class Subject : Base { private void Awake() {} }",
            0
        )]
        [TestCase(
            "class SuppressAnalyzerAttribute : System.Attribute {} class Base : UnityEngine.MonoBehaviour { private void Awake() {} } [SuppressAnalyzer] class Subject : Base { private void Awake() {} }",
            1
        )]
        [TestCase(
            "class Base : UnityEngine.MonoBehaviour { protected void Awake() {} } partial class Subject : Base {} partial class Subject { private void Awake() {} }",
            1
        )]
        [TestCase(
            "class Base : UnityEngine.MonoBehaviour { private int Awake() => 1; } class Subject : Base { private int Awake() => 1; }",
            0
        )]
        public void OnlyResolvedUnityCallbackHidingIsReported(string source, int expected)
        {
            Diagnostic[] diagnostics = Analyze(CreateCompilation("Fixture", UnityTypes, source));
            Assert.That(diagnostics, Has.Length.EqualTo(expected));
            foreach (Diagnostic diagnostic in diagnostics)
            {
                Assert.That(diagnostic.Id, Is.EqualTo("WUH016"));
                Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
                Assert.That(
                    diagnostic.GetMessage(),
                    Does.Contain("Subject.Awake")
                        .Or.Contain("Subject.Start")
                        .Or.Contain("Subject.OnEnable")
                );
            }
        }

        [Test]
        public void MetadataAncestorIsResolvedWithoutSourceScanning()
        {
            CSharpCompilation dependency = CreateCompilation(
                "Dependency",
                UnityTypes,
                "public class Base<T> : UnityEngine.MonoBehaviour { protected void Awake() {} }"
            );
            using MemoryStream image = new();
            Assert.That(dependency.Emit(image).Success, Is.True);
            CSharpCompilation consumer = CreateCompilation(
                    "Consumer",
                    "using Alias = Base<int>; class Subject : Alias { private void Awake() {} }"
                )
                .AddReferences(MetadataReference.CreateFromImage(image.ToArray()));
            Assert.That(Analyze(consumer), Has.Length.EqualTo(1));
        }

        [TestCase(
            "public virtual void Run(int value) {}",
            "public override void Run(string value) {}",
            "CS0115"
        )]
        [TestCase(
            "public virtual int Run() => 1;",
            "public override string Run() => null;",
            "CS0508"
        )]
        [TestCase("public virtual void Run() {}", "protected override void Run() {}", "CS0507")]
        [TestCase("public virtual void Run() {}", "public void Run() {}", "CS0114")]
        [TestCase("public void Run() {}", "public void Run() {}", "CS0108")]
        public void CompilerOwnsGeneralInheritanceDiagnostics(
            string baseMethod,
            string derivedMethod,
            string expected
        )
        {
            CSharpCompilation compilation = CreateCompilation(
                "Fixture",
                "class Base { " + baseMethod + " } class Subject : Base { " + derivedMethod + " }"
            );
            Assert.That(
                compilation.GetDiagnostics().Select(diagnostic => diagnostic.Id),
                Does.Contain(expected)
            );
        }

        private static CSharpCompilation CreateCompilation(string name, params string[] sources)
        {
            List<MetadataReference> references = new();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            return CSharpCompilation.Create(
                name,
                sources.Select(source => CSharpSyntaxTree.ParseText(source)),
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
        }

        private static Diagnostic[] Analyze(CSharpCompilation compilation)
        {
            Assert.That(
                compilation
                    .GetDiagnostics()
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                Is.Empty
            );
            return compilation
                .WithAnalyzers(
                    ImmutableArray.Create<DiagnosticAnalyzer>(new UnityMessageInheritanceAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult()
                .ToArray();
        }
    }
}
