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
    public sealed class PublicStructEqualityAnalyzerTests
    {
        private const string DiagnosticId = "WUH023";

        private static ImmutableArray<Diagnostic> Analyze(
            string subject,
            ReportDiagnostic severity = ReportDiagnostic.Warn,
            string sourcePath = "Consumer.cs"
        )
        {
            string source = "using System; " + subject;
            List<MetadataReference> references = new();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            CSharpCompilation compilation = CSharpCompilation.Create(
                "EqualityConsumer",
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        source,
                        new CSharpParseOptions(LanguageVersion.CSharp9),
                        path: sourcePath
                    ),
                },
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary
                ).WithSpecificDiagnosticOptions(
                    ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(DiagnosticId, severity)
                )
            );
            Assert.IsEmpty(
                compilation
                    .GetDiagnostics()
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.ToString())
                    .ToArray(),
                "Fixture must compile"
            );
            return compilation
                .WithAnalyzers(
                    ImmutableArray.Create<DiagnosticAnalyzer>(new PublicStructEqualityAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }

        [TestCase("public struct Value { public int Number; }")]
        [TestCase("public readonly struct Value { }")]
        [TestCase("public struct Value<T> { public T Item; }")]
        [TestCase("public class Owner { public struct Value { } }")]
        [TestCase(
            "public struct Value : IEquatable<int> { public bool Equals(int other) { return false; } }"
        )]
        [TestCase("public struct Value { public bool Equals(Value other) { return true; } }")]
        [TestCase(
            "public partial struct Value { } public partial struct Value { public int Number; }"
        )]
        public void PublicStructWithoutSelfInterfaceReportsOnce(string source)
        {
            ImmutableArray<Diagnostic> diagnostics = Analyze(source);
            Assert.That(diagnostics.Length, Is.EqualTo(1));
            Assert.That(diagnostics[0].Id, Is.EqualTo(DiagnosticId));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(diagnostics[0].Location.IsInSource, Is.True);
        }

        [TestCase(
            "public struct Value : IEquatable<Value> { public bool Equals(Value other) { return true; } }"
        )]
        [TestCase(
            "public struct Value<T> : IEquatable<Value<T>> { public bool Equals(Value<T> other) { return true; } }"
        )]
        [TestCase(
            "public struct Value : IEquatable<Value> { bool IEquatable<Value>.Equals(Value other) { return true; } }"
        )]
        [TestCase(
            "public interface IValue : IEquatable<Value> { } public struct Value : IValue { public bool Equals(Value other) { return true; } }"
        )]
        [TestCase("internal struct Value { }")]
        [TestCase("internal class Owner { public struct Value { } }")]
        [TestCase("public class Owner { private struct Value { } }")]
        [TestCase("public ref struct Value { }")]
        [TestCase("public readonly ref struct Value { }")]
        [TestCase("public enum Value { None = 0 }")]
        [TestCase("public class Value { }")]
        [TestCase(
            "public partial struct Value : IEquatable<Value> { } public partial struct Value { public bool Equals(Value other) { return true; } }"
        )]
        public void CorrectAndInapplicableTypesRemainSilent(string source)
        {
            Assert.IsEmpty(Analyze(source));
        }

        [TestCase(ReportDiagnostic.Default)]
        [TestCase(ReportDiagnostic.Suppress)]
        public void ConsumerDefaultAndSuppressionRemainSilent(ReportDiagnostic severity)
        {
            Assert.IsEmpty(Analyze("public struct Value { }", severity));
        }

        [TestCase("Consumer.g.cs")]
        [TestCase("Consumer.generated.cs")]
        public void GeneratedSourcesRemainSilent(string path)
        {
            Assert.IsEmpty(Analyze("public struct Value { }", ReportDiagnostic.Warn, path));
        }

        [Test]
        public void PragmaSuppressionRemainsAvailable()
        {
            Assert.IsEmpty(Analyze("\n#pragma warning disable WUH023\npublic struct Value { }"));
        }

        [TestCase(
            "namespace Consumer { public interface IEquatable<T> { } public struct Value : IEquatable<Value> { } }"
        )]
        [TestCase(
            "namespace System { public interface IEquatable<T> { } } namespace Consumer { public struct Value : System.IEquatable<Value> { } }"
        )]
        public void SameNamedInterfacesDoNotSatisfyFrameworkContract(string source)
        {
            Assert.That(Analyze(source).Length, Is.EqualTo(1));
        }
    }
}
