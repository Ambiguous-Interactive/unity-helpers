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
    /// Pins the opt-in boundary for explicit string comparison policy.
    /// </summary>
    [TestFixture]
    public sealed class StringEqualityAnalyzerTests
    {
        private const string DiagnosticId = "WUH018";

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
                    ImmutableArray.Create<DiagnosticAnalyzer>(new StringEqualityAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }

        [TestCase("left == right", "==")]
        [TestCase("left != right", "!=")]
        [TestCase("left == \"value\"", "==")]
        [TestCase("\"value\" != right", "!=")]
        public void StringEqualityOperatorsAreReported(string expression, string operatorText)
        {
            ImmutableArray<Diagnostic> reported = Analyze(
                "public static bool Same(string left, string right) => " + expression + ";"
            );

            Assert.AreEqual(1, reported.Length);
            Assert.AreEqual(DiagnosticId, reported[0].Id);
            Assert.AreEqual(DiagnosticSeverity.Warning, reported[0].Severity);
            StringAssert.Contains("'" + operatorText + "'", reported[0].GetMessage());
        }

        [TestCase("left == null")]
        [TestCase("null != right")]
        [TestCase("left is null")]
        [TestCase("left is not null")]
        public void StringNullChecksAreNotReported(string expression)
        {
            Assert.IsEmpty(
                Analyze(
                    "public static bool Missing(string left, string right) => " + expression + ";"
                )
            );
        }

        [TestCase("string.Equals(left, right, System.StringComparison.Ordinal)")]
        [TestCase("left.Equals(right, System.StringComparison.OrdinalIgnoreCase)")]
        [TestCase("object.ReferenceEquals(left, right)")]
        public void ExplicitStringComparisonsAreNotReported(string expression)
        {
            Assert.IsEmpty(
                Analyze("public static bool Same(string left, string right) => " + expression + ";")
            );
        }

        [Test]
        public void NonStringEqualityOperatorsAreNotReported()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static bool Same(int left, int right) => left == right; "
                        + "public static bool SameObjects(object left, object right) => left != right;"
                )
            );
        }

        [Test]
        public void UserDefinedEqualityOperatorsAreNotReported()
        {
            Assert.IsEmpty(
                Analyze(
                    "public sealed class Value { "
                        + "public static bool operator ==(Value left, Value right) => true; "
                        + "public static bool operator !=(Value left, Value right) => false; "
                        + "public override bool Equals(object other) => true; "
                        + "public override int GetHashCode() => 0; } "
                        + "public static bool Same(Value left, Value right) => left == right;"
                )
            );
        }

        [Test]
        public void TheRuleIsOffUntilAConsumerAsksForIt()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static bool Same(string left, string right) => left == right;",
                    ReportDiagnostic.Default
                )
            );
        }
    }
}
