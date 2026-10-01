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
    /// Pins named collection matching, available API boundaries, and the warning policy.
    /// </summary>
    [TestFixture]
    public sealed class HardCollectionReadAnalyzerTests
    {
        private const string DiagnosticId = "WUH020";

        private static ImmutableArray<Diagnostic> Analyze(
            string body,
            string declarations = "",
            ReportDiagnostic reportedAs = ReportDiagnostic.Default
        )
        {
            string source =
                "using System.Collections.Generic; using System.Collections.Concurrent; "
                + "namespace Consumer { public static class Subject { "
                + body
                + " } } "
                + declarations;
            List<MetadataReference> references = new List<MetadataReference>();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            CSharpCompilation compilation = CSharpCompilation.Create(
                "ConsumerAssembly",
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
                    ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(
                        DiagnosticId,
                        reportedAs
                    )
                )
            );
            Assert.IsEmpty(
                compilation
                    .GetDiagnostics()
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.ToString())
                    .ToArray(),
                "The fixture must compile"
            );
            return compilation
                .WithAnalyzers(
                    ImmutableArray.Create<DiagnosticAnalyzer>(new HardCollectionReadAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }

        [TestCase("Stack<int>", "Pop", "TryPop")]
        [TestCase("Stack<int>", "Peek", "TryPeek")]
        [TestCase("Queue<int>", "Dequeue", "TryDequeue")]
        [TestCase("Queue<int>", "Peek", "TryPeek")]
        public void KnownHardReadsNameTheAvailableAlternative(
            string type,
            string method,
            string alternative
        )
        {
            ImmutableArray<Diagnostic> diagnostics = Analyze(
                "public static int Read(" + type + " values) { return values." + method + "(); }"
            );
            Assert.That(diagnostics.Length, Is.EqualTo(1));
            Assert.That(diagnostics[0].Id, Is.EqualTo(DiagnosticId));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            StringAssert.Contains(alternative, diagnostics[0].GetMessage());
        }

        [TestCase("if (values.Count > 0) { return values.Pop(); } return 0;")]
        [TestCase("while (values.Count > 0) { values.Pop(); } return 0;")]
        [TestCase("values.Push(1); return values.Pop();")]
        public void ASeparateGuardOrKnownPushDoesNotExemptTheRead(string body)
        {
            Assert.That(
                Analyze("public static int Read(Stack<int> values) { " + body + " }").Length,
                Is.EqualTo(1)
            );
        }

        [TestCase("Stack<int>", "TryPop")]
        [TestCase("Stack<int>", "TryPeek")]
        [TestCase("Queue<int>", "TryDequeue")]
        [TestCase("Queue<int>", "TryPeek")]
        [TestCase("ConcurrentStack<int>", "TryPop")]
        [TestCase("ConcurrentStack<int>", "TryPeek")]
        [TestCase("ConcurrentQueue<int>", "TryDequeue")]
        [TestCase("ConcurrentQueue<int>", "TryPeek")]
        public void TryReadsAreNotReported(string type, string method)
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Read("
                        + type
                        + " values) { return values."
                        + method
                        + "(out int value) ? value : 0; }"
                )
            );
        }

        [TestCase("Stack", "Pop", "TryPop")]
        [TestCase("Stack", "Peek", "TryPeek")]
        [TestCase("Queue", "Dequeue", "TryDequeue")]
        [TestCase("Queue", "Peek", "TryPeek")]
        public void ATargetFrameworkWithoutTheAlternativeIsNotReported(
            string type,
            string method,
            string alternative
        )
        {
            string declarations =
                "namespace System.Collections.Generic { public class "
                + type
                + "<T> { public T "
                + method
                + "() { return default; } } }";
            Assert.IsEmpty(
                Analyze(
                    "public static int Read("
                        + type
                        + "<int> values) { return values."
                        + method
                        + "(); }",
                    declarations
                ),
                alternative
            );
        }

        [TestCase("public int TryPop(out int value) { value = 0; return 0; }")]
        [TestCase("public bool TryPop(ref int value) { return false; }")]
        [TestCase("public bool TryPop(out string value) { value = null; return false; }")]
        [TestCase("public bool TryPop(out int value, int extra) { value = 0; return false; }")]
        [TestCase("public static bool TryPop(out int value) { value = 0; return false; }")]
        [TestCase("private bool TryPop(out int value) { value = 0; return false; }")]
        [TestCase("public bool TryPop<TValue>(out int value) { value = 0; return false; }")]
        public void AnIncompatibleOrInaccessibleAlternativeIsNotSuggested(string alternative)
        {
            string declarations =
                "namespace System.Collections.Generic { public class Stack<T> { public int Pop() { return 0; } "
                + alternative
                + " } }";
            Assert.IsEmpty(
                Analyze(
                    "public static int Read(Stack<int> values) { return values.Pop(); }",
                    declarations
                )
            );
        }

        [TestCase("Pop", "TryPop")]
        [TestCase("Peek", "TryPeek")]
        [TestCase("Dequeue", "TryDequeue")]
        public void MatchingMethodNamesOnCustomApisAreNotReported(string method, string alternative)
        {
            string declarations =
                "namespace Consumer { public class Stack<T> { public T "
                + method
                + "() { return default; } public bool "
                + alternative
                + "(out T value) { value = default; return false; } } }";
            Assert.IsEmpty(
                Analyze(
                    "public static int Read(Stack<int> values) { return values." + method + "(); }",
                    declarations
                )
            );
        }

        [Test]
        public void AHiddenCustomPopOnADerivedStackIsNotReported()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Read(Custom values) { return values.Pop(); }",
                    "public class Custom : Stack<int> { public new int Pop() { return 0; } }"
                )
            );
        }

        [Test]
        public void AnInheritedRealStackReadIsReported()
        {
            Assert.That(
                Analyze(
                    "public static int Read(Custom values) { return values.Pop(); }",
                    "public class Custom : Stack<int> { }"
                ).Length,
                Is.EqualTo(1)
            );
        }

        [TestCase("Stack<int>", "Pop")]
        [TestCase("Queue<int>", "Dequeue")]
        public void AConstrainedTypeParameterReadIsReported(string constraint, string method)
        {
            Assert.That(
                Analyze(
                    "public static int Read<T>(T values) where T : "
                        + constraint
                        + " { return values."
                        + method
                        + "(); }"
                ).Length,
                Is.EqualTo(1)
            );
        }

        [Test]
        public void NonGenericCollectionsWithoutTryMethodsAreNotReported()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static object Read(System.Collections.Stack values) { return values.Pop(); }"
                )
            );
        }

        [Test]
        public void TheDiagnosticIsEnabledSuppressibleAndCappedAtWarning()
        {
            DiagnosticDescriptor descriptor =
                new HardCollectionReadAnalyzer().SupportedDiagnostics.Single();
            Assert.IsTrue(descriptor.IsEnabledByDefault);
            Assert.That(descriptor.DefaultSeverity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.IsEmpty(
                Analyze(
                    "public static int Read(Stack<int> values) { return values.Pop(); }",
                    reportedAs: ReportDiagnostic.Suppress
                )
            );
        }
    }
}
