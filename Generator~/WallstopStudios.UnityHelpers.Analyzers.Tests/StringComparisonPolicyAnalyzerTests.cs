// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers.Tests
{
    using System.Collections.Immutable;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Diagnostics;
    using NUnit.Framework;

    [TestFixture]
    public sealed class StringComparisonPolicyAnalyzerTests
    {
        private const string DiagnosticId = "WUH030";
        private static readonly MetadataReference CoreLibrary = MetadataReference.CreateFromFile(
            typeof(object).Assembly.Location
        );

        private static ImmutableArray<Diagnostic> Analyze(
            string expression,
            ReportDiagnostic severity = ReportDiagnostic.Warn
        )
        {
            string source =
                "using System; using System.Globalization; public static class Subject { public static object Run(string left, string right) { return "
                + expression
                + "; } }";
            CSharpSyntaxTree tree = (CSharpSyntaxTree)
                CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp9));
            CSharpCompilation compilation = CSharpCompilation.Create(
                "Consumer",
                new[] { tree },
                new[] { CoreLibrary },
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary
                ).WithSpecificDiagnosticOptions(
                    ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(DiagnosticId, severity)
                )
            );
            ImmutableArray<Diagnostic> errors = compilation
                .GetDiagnostics()
                .Where(item => item.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();
            Assert.IsEmpty(
                errors.Select(item => item.ToString()).ToArray(),
                "The consumer must compile."
            );
            return compilation
                .WithAnalyzers(
                    ImmutableArray.Create<DiagnosticAnalyzer>(new StringComparisonPolicyAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }

        [TestCase("string.Equals(left, right)", "Equals")]
        [TestCase("left.Equals(right)", "Equals")]
        [TestCase("string.Compare(left, right)", "Compare")]
        [TestCase("string.Compare(left, right, true)", "Compare")]
        [TestCase("string.Compare(left, 0, right, 0, 1)", "Compare")]
        [TestCase("left.StartsWith(right)", "StartsWith")]
        [TestCase("left.EndsWith(right)", "EndsWith")]
        [TestCase("left.IndexOf(right)", "IndexOf")]
        [TestCase("left.IndexOf(right, 0)", "IndexOf")]
        [TestCase("left.IndexOf(right, 0, 1)", "IndexOf")]
        [TestCase("left.LastIndexOf(right)", "LastIndexOf")]
        [TestCase("left.LastIndexOf(right, 0)", "LastIndexOf")]
        [TestCase("left.LastIndexOf(right, 0, 1)", "LastIndexOf")]
        [TestCase("left.ToLower()", "ToLower")]
        [TestCase("left.ToUpper()", "ToUpper")]
        [TestCase("left?.StartsWith(right)", "StartsWith")]
        [TestCase("left.IndexOf(value: right)", "IndexOf")]
        public void ImplicitPoliciesAreReported(string expression, string method)
        {
            ImmutableArray<Diagnostic> diagnostics = Analyze(expression);

            Assert.AreEqual(1, diagnostics.Length);
            Assert.AreEqual(DiagnosticId, diagnostics[0].Id);
            Assert.AreEqual(DiagnosticSeverity.Warning, diagnostics[0].Severity);
            StringAssert.Contains(method, diagnostics[0].GetMessage());
        }

        [TestCase("string.Equals(left, right, StringComparison.Ordinal)")]
        [TestCase("left.Equals(right, StringComparison.OrdinalIgnoreCase)")]
        [TestCase("string.Compare(left, right, StringComparison.CurrentCulture)")]
        [TestCase("string.Compare(left, right, false, CultureInfo.InvariantCulture)")]
        [TestCase("left.StartsWith(right, StringComparison.InvariantCulture)")]
        [TestCase("left.EndsWith(right, true, CultureInfo.CurrentCulture)")]
        [TestCase("left.IndexOf(right, StringComparison.Ordinal)")]
        [TestCase("left.LastIndexOf(right, StringComparison.OrdinalIgnoreCase)")]
        [TestCase("left.ToLower(CultureInfo.CurrentCulture)")]
        [TestCase("left.ToUpper(CultureInfo.InvariantCulture)")]
        [TestCase("left.ToLowerInvariant()")]
        [TestCase("left.ToUpperInvariant()")]
        [TestCase("left.IndexOf('x')")]
        [TestCase("left.IndexOf('x', 0, 1)")]
        [TestCase("left.LastIndexOf('x')")]
        [TestCase("left.StartsWith('x')")]
        [TestCase("left.EndsWith('x')")]
        [TestCase("left.Equals((object)right)")]
        [TestCase("object.Equals(left, right)")]
        [TestCase("string.CompareOrdinal(left, right)")]
        [TestCase("left.IndexOf(right, comparisonType: default(StringComparison))")]
        [TestCase("new System.Collections.Generic.List<string>().IndexOf(left)")]
        [TestCase("string.IsNullOrWhiteSpace(left)")]
        public void ExplicitPoliciesAndUnrelatedMethodsAreUnchanged(string expression)
        {
            Assert.IsEmpty(Analyze(expression));
        }

        [TestCase(ReportDiagnostic.Default)]
        [TestCase(ReportDiagnostic.Suppress)]
        public void PolicyIsOptInAndSuppressible(ReportDiagnostic severity)
        {
            Assert.IsEmpty(Analyze("left.StartsWith(right)", severity));
            DiagnosticDescriptor rule =
                new StringComparisonPolicyAnalyzer().SupportedDiagnostics.Single();
            Assert.IsFalse(rule.IsEnabledByDefault);
            Assert.AreEqual(DiagnosticSeverity.Warning, rule.DefaultSeverity);
        }
    }
}
