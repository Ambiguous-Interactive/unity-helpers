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

    [TestFixture]
    public sealed class BooleanBitwiseAnalyzerTests
    {
        private const string DiagnosticId = "WUH026";

        private static ImmutableArray<Diagnostic> Analyze(
            string body,
            ReportDiagnostic severity = ReportDiagnostic.Default,
            string path = "Consumer.cs"
        )
        {
            string source =
                "using System; public partial class Consumer { public object Run(bool a, bool b, bool? x, bool? y, int i, int j, Consumer other) { "
                + body
                + " } }";
            System.Reflection.Assembly expressions =
                typeof(System.Linq.Expressions.Expression).Assembly;
            List<MetadataReference> references = new()
            {
                MetadataReference.CreateFromFile(expressions.Location),
            };
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (
                    !ReferenceEquals(assembly, expressions)
                    && !assembly.IsDynamic
                    && !string.IsNullOrWhiteSpace(assembly.Location)
                )
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            CSharpCompilation compilation = CSharpCompilation.Create(
                "BooleanConsumer",
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        source,
                        new CSharpParseOptions(LanguageVersion.CSharp9),
                        path: path
                    ),
                    CSharpSyntaxTree.ParseText(
                        "public partial class Consumer { public bool Ready; public static Consumer operator |(Consumer a, Consumer b) { return a; } public static Consumer operator &(Consumer a, Consumer b) { return a; } public static Consumer operator ^(Consumer a, Consumer b) { return a; } }",
                        new CSharpParseOptions(LanguageVersion.CSharp9),
                        path: "Other.cs"
                    ),
                },
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary
                ).WithSpecificDiagnosticOptions(
                    ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(DiagnosticId, severity)
                )
            );
            Assert.That(
                compilation
                    .GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()),
                Is.Empty,
                "Fixture must compile"
            );
            return compilation
                .WithAnalyzers(
                    ImmutableArray.Create<DiagnosticAnalyzer>(new BooleanBitwiseAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }

        [TestCase("return a | b;")]
        [TestCase("return a & b;")]
        [TestCase("return a ^ b;")]
        [TestCase("a |= b; return a;")]
        [TestCase("a &= b; return a;")]
        [TestCase("a ^= b; return a;")]
        [TestCase("return x | y;")]
        [TestCase("return x & y;")]
        [TestCase("return x ^ y;")]
        [TestCase("x |= y; return x;")]
        [TestCase("x &= y; return x;")]
        [TestCase("x ^= y; return x;")]
        [TestCase("return x | null;")]
        [TestCase("return x & null;")]
        [TestCase("return x ^ null;")]
        [TestCase("x |= null; return x;")]
        [TestCase("x &= null; return x;")]
        [TestCase("x ^= null; return x;")]
        [TestCase("x |= a; return x;")]
        [TestCase("x &= a; return x;")]
        [TestCase("x ^= a; return x;")]
        [TestCase("return x | a;")]
        [TestCase("return a & y;")]
        [TestCase("return (bool?)null ^ a;")]
        [TestCase("return other.Ready | a;")]
        [TestCase("other.Ready |= a; return other.Ready;")]
        [TestCase("return ((Func<bool>)(() => true))() | b;")]
        public void BooleanOperatorsReport(string body)
        {
            ImmutableArray<Diagnostic> diagnostics = Analyze(body);
            Assert.That(diagnostics.Length, Is.EqualTo(1));
            Assert.That(diagnostics[0].Id, Is.EqualTo(DiagnosticId));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(
                diagnostics[0].GetMessage(),
                Does.Contain("Preserve both operand evaluations")
            );
            Assert.That(diagnostics[0].GetMessage(), Does.Contain("Nullable booleans"));
            Assert.That(
                diagnostics[0]
                    .Location.SourceTree.GetText()
                    .ToString(diagnostics[0].Location.SourceSpan),
                Does.Contain(
                    body.Contains('|') ? "|"
                    : body.Contains('&') ? "&"
                    : "^"
                )
            );
        }

        [TestCase("return a || b;")]
        [TestCase("return a && b;")]
        [TestCase("return a != b;")]
        [TestCase("return a == b;")]
        [TestCase("return i | j;")]
        [TestCase("return i & j;")]
        [TestCase("return i ^ j;")]
        [TestCase("i |= j; return i;")]
        [TestCase("i &= j; return i;")]
        [TestCase("i ^= j; return i;")]
        [TestCase("return (a ? 1 : 0) | (b ? 2 : 0);")]
        [TestCase("return DayOfWeek.Monday | DayOfWeek.Tuesday;")]
        [TestCase("return (int?)i & j;")]
        [TestCase("return x != y;")]
        [TestCase("return x ?? false;")]
        [TestCase("return other | other;")]
        [TestCase("return other & other;")]
        [TestCase("return other ^ other;")]
        [TestCase("other |= other; return other;")]
        [TestCase("other &= other; return other;")]
        [TestCase("other ^= other; return other;")]
        public void OtherOperatorsDoNotReport(string body)
        {
            Assert.That(Analyze(body), Is.Empty);
        }

        [Test]
        public void ExplicitSuppressionIsRespected()
        {
            Assert.That(Analyze("return a | b;", ReportDiagnostic.Suppress), Is.Empty);
        }

        [Test]
        public void GeneratedCodeIsNotReported()
        {
            Assert.That(Analyze("return a | b;", path: "Consumer.g.cs"), Is.Empty);
        }
    }
}
