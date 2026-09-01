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
    /// Pins which counting loops can become <c>foreach</c> and, more importantly, which cannot.
    /// </summary>
    /// <remarks>
    /// The negative cases are the rule. <c>foreach</c> over <c>IReadOnlyList&lt;T&gt;</c> boxes an
    /// enumerator, so a counting loop there is the correct shape and reporting it would push people
    /// toward the allocation this family exists to prevent.
    /// </remarks>
    [TestFixture]
    public sealed class CountingLoopAnalyzerTests
    {
        private const string DiagnosticId = "WUH013";

        [TestCase("string[] rows", "rows.Length")]
        [TestCase("System.Collections.Generic.List<string> rows", "rows.Count")]
        public void ACountingWalkOfAnAllocationFreeSequenceIsReported(
            string declaration,
            string bound
        )
        {
            ImmutableArray<Diagnostic> reported = Analyze(
                "public static void Walk("
                    + declaration
                    + ") { for (int index = 0; index < "
                    + bound
                    + "; ++index) { System.Console.WriteLine(rows[index]); } }"
            );

            Assert.AreEqual(1, reported.Length);
            Assert.AreEqual(DiagnosticId, reported[0].Id);
            Assert.AreEqual(DiagnosticSeverity.Warning, reported[0].Severity);
        }

        [TestCase("System.Collections.Generic.IReadOnlyList<string> rows")]
        [TestCase("System.Collections.Generic.IList<string> rows")]
        public void ACountingWalkOfAnInterfaceIsTheCorrectShape(string declaration)
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk("
                        + declaration
                        + ") { for (int index = 0; index < rows.Count; ++index) { System.Console.WriteLine(rows[index]); } }"
                ),
                "foreach over an interface boxes its enumerator, so the counting loop is right."
            );
        }

        [Test]
        public void ALoopThatUsesTheIndexItselfIsNotReported()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(string[] rows) { for (int index = 0; index < rows.Length; ++index) { System.Console.WriteLine(index + \": \" + rows[index]); } }"
                )
            );
        }

        [Test]
        public void ALoopThatIndexesASecondSequenceIsNotReported()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(string[] rows, string[] others) { for (int index = 0; index < rows.Length; ++index) { System.Console.WriteLine(rows[index] + others[index]); } }"
                )
            );
        }

        [TestCase("for (int index = 1; index < rows.Length; ++index)")]
        [TestCase("for (int index = rows.Length - 1; 0 <= index; --index)")]
        [TestCase("for (int index = 0; index < rows.Length; index += 2)")]
        public void AWalkThatIsNotTheOrdinaryForwardOneIsNotReported(string header)
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(string[] rows) { "
                        + header
                        + " { System.Console.WriteLine(rows[index]); } }"
                )
            );
        }

        [Test]
        public void AForeachIsNeverReported()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(string[] rows) { foreach (string row in rows) { System.Console.WriteLine(row); } }"
                )
            );
        }

        [Test]
        public void TheRuleIsOffUntilAConsumerAsksForIt()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(string[] rows) { for (int index = 0; index < rows.Length; ++index) { System.Console.WriteLine(rows[index]); } }",
                    ReportDiagnostic.Default
                ),
                "Measured at 127 sites, so on by default it would bury the correctness rules."
            );
        }

        private static ImmutableArray<Diagnostic> Analyze(string body)
        {
            return Analyze(body, ReportDiagnostic.Warn);
        }

        private static ImmutableArray<Diagnostic> Analyze(string body, ReportDiagnostic reportedAs)
        {
            string source = "namespace Consumer { public static class Subject { " + body + " } }";

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

            ImmutableArray<Diagnostic> compileErrors = compilation
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();
            Assert.IsEmpty(
                compileErrors.Select(diagnostic => diagnostic.ToString()).ToArray(),
                "The fixture must compile"
            );

            return compilation
                .WithAnalyzers(
                    ImmutableArray.Create<DiagnosticAnalyzer>(new CountingLoopAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }
    }
}
