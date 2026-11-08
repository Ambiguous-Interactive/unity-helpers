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
    public sealed class RedundantCallerNameAnalyzerTests
    {
        private const string DiagnosticId = "WUH027";

        private static ImmutableArray<Diagnostic> Analyze(
            string body,
            string members = "",
            ReportDiagnostic severity = ReportDiagnostic.Default,
            string path = "Consumer.cs",
            LanguageVersion languageVersion = LanguageVersion.CSharp9
        )
        {
            string source =
                "using System; using System.Runtime.CompilerServices; public class Consumer { "
                + "public void Run() { "
                + body
                + " } "
                + "private void Log(int value, [CallerMemberName] string caller = null) {} "
                + "private void Plain(string caller) {} "
                + members
                + " }";
            HashSet<string> locations = new(StringComparer.Ordinal);
            List<MetadataReference> references = new();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (
                    !assembly.IsDynamic
                    && !string.IsNullOrWhiteSpace(assembly.Location)
                    && locations.Add(assembly.Location)
                )
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            CSharpCompilation compilation = CSharpCompilation.Create(
                "CallerConsumer",
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        source,
                        new CSharpParseOptions(languageVersion),
                        path: path
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
                    ImmutableArray.Create<DiagnosticAnalyzer>(new RedundantCallerNameAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }

        [TestCase("Log(1, nameof(Run));")]
        [TestCase("Log(1, caller: nameof(Run));")]
        [TestCase("Log(caller: nameof(Run), value: 1);")]
        [TestCase("Log(1, \"Run\");")]
        [TestCase("const string name = nameof(Run); Log(1, name);")]
        public void RedundantExplicitCallerNamesReport(string body)
        {
            ImmutableArray<Diagnostic> diagnostics = Analyze(body);
            Assert.That(diagnostics.Length, Is.EqualTo(1));
            Assert.That(diagnostics[0].Id, Is.EqualTo(DiagnosticId));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(diagnostics[0].GetMessage(), Does.Contain("CallerMemberName"));
        }

        [TestCase("Log(1);")]
        [TestCase("Log(1, \"Other operation\");")]
        [TestCase("Log(1, null);")]
        [TestCase("Log(1, string.Empty);")]
        [TestCase("Plain(nameof(Run));")]
        [TestCase("string caller = Environment.UserName; Log(1, caller);")]
        [TestCase("void Local() { Log(1, nameof(Local)); } Local();")]
        public void ImplicitNamesAndIntentionalLabelsRemainAllowed(string body)
        {
            Assert.That(Analyze(body), Is.Empty);
        }

        [Test]
        public void OmittingAnArgumentMustPreserveTheSelectedOverload()
        {
            Assert.That(
                Analyze("Log(1, nameof(Run));", "private void Log(int value) {}"),
                Is.Empty
            );
        }

        [Test]
        public void RemovingAnArgumentMustLeaveRequiredArgumentsInTheirPositions()
        {
            Assert.That(
                Analyze(
                    "Before(nameof(Run), 1);",
                    "private void Before([CallerMemberName] string caller = null, int value = 0) {}"
                ),
                Is.Empty
            );
        }

        [TestCase("Shifted(\"x\", nameof(Run), \"y\");", 0)]
        [TestCase("Shifted(\"x\", nameof(Run), b: \"y\");", 1)]
        [TestCase("Shifted(a: \"x\", caller: nameof(Run), b: \"y\");", 1)]
        public void LaterArgumentsKeepTheirParameterBindings(string body, int expected)
        {
            Assert.That(
                Analyze(
                    body,
                    "private void Shifted(string a = null, [CallerMemberName] string caller = null, string b = null) {}"
                ).Length,
                Is.EqualTo(expected)
            );
        }

        [Test]
        public void ForwardedCallerNamesRemainAllowed()
        {
            Assert.That(
                Analyze(
                    "Wrapper();",
                    "private void Wrapper([CallerMemberName] string caller = null) { Log(1, caller); }"
                ),
                Is.Empty
            );
        }

        [Test]
        public void CallerArgumentExpressionsDependingOnTheNameRemainUnchanged()
        {
            Assert.That(
                Analyze(
                    "Expression(nameof(Run));",
                    "private void Expression([CallerMemberName] string caller = null, [CallerArgumentExpression(nameof(caller))] string expression = null) {}",
                    languageVersion: LanguageVersion.CSharp10
                ),
                Is.Empty
            );
        }

        [Test]
        public void DiagnosticCanBeSuppressed()
        {
            Assert.That(
                Analyze("Log(1, nameof(Run));", severity: ReportDiagnostic.Suppress),
                Is.Empty
            );
        }

        [Test]
        public void GeneratedSourcesAreExcluded()
        {
            Assert.That(Analyze("Log(1, nameof(Run));", path: "Consumer.g.cs"), Is.Empty);
        }
    }
}
