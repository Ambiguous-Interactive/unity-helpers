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
    public sealed class DisposableStructBoxingAnalyzerTests
    {
        private const string DiagnosticId = "WUH022";
        private const string Declarations =
            @"
            public readonly struct Lease : IDisposable { public void Dispose() { } }
            public sealed class Owner : IDisposable { public void Dispose() { } }
            public readonly struct Unrelated { }
            public static class Consumer {
                private static void Accept(IDisposable value) { }
                private static void AcceptGeneric<T>(T value) where T : IDisposable { }
                SUBJECT
            }";

        private static ImmutableArray<Diagnostic> Analyze(
            string subject,
            ReportDiagnostic severity = ReportDiagnostic.Default,
            string sourcePath = "Consumer.cs"
        )
        {
            string source = "using System; " + Declarations.Replace("SUBJECT", subject);
            List<MetadataReference> references = new();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            CSharpCompilation compilation = CSharpCompilation.Create(
                "BoxingConsumer",
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
                    ImmutableArray.Create<DiagnosticAnalyzer>(new DisposableStructBoxingAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }

        [TestCase("public static object Run(Lease lease) { return lease; }")]
        [TestCase("public static IDisposable Run(Lease lease) { return lease; }")]
        [TestCase("public static ValueType Run(Lease lease) { return lease; }")]
        [TestCase("public static void Run(Lease lease) { Accept(lease); }")]
        [TestCase(
            "public static void Run(Lease lease) { IDisposable boxed = (IDisposable)lease; boxed.Dispose(); }"
        )]
        [TestCase("public static void Run(Lease lease) { dynamic boxed = lease; }")]
        [TestCase("public static object Run(Lease? lease) { return lease; }")]
        [TestCase("public static IDisposable Run(Lease? lease) { return lease; }")]
        [TestCase(
            "public static object Run<T>(T lease) where T : struct, IDisposable { return lease; }"
        )]
        [TestCase(
            "public static IDisposable Run<T>(T lease) where T : struct, IDisposable { return lease; }"
        )]
        [TestCase("public static object[] Run(Lease lease) { return new object[] { lease }; }")]
        [TestCase("public static object Run<T>(T lease) where T : IDisposable { return lease; }")]
        public void BoxingConversionsReportOneWarning(string subject)
        {
            ImmutableArray<Diagnostic> diagnostics = Analyze(subject);
            Assert.That(diagnostics.Length, Is.EqualTo(1));
            Assert.That(diagnostics[0].Id, Is.EqualTo(DiagnosticId));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        }

        [TestCase(
            "public static void Run(Lease lease) { Lease copy = lease; copy.Dispose(); lease.Dispose(); }"
        )]
        [TestCase("public static void Run(Lease lease) { using (lease) { } }")]
        [TestCase("public static void Run(Lease lease) { AcceptGeneric(lease); }")]
        [TestCase("public static IDisposable Run(Owner owner) { return owner; }")]
        [TestCase("public static object Run(Unrelated value) { return value; }")]
        [TestCase("public static Lease Run(IDisposable value) { return (Lease)value; }")]
        [TestCase(
            "public static IDisposable Run<T>(T value) where T : class, IDisposable { return value; }"
        )]
        [TestCase("public static object Run(IDisposable value) { return value; }")]
        public void NonBoxingAndSafeCopiesRemainSilent(string subject)
        {
            Assert.IsEmpty(Analyze(subject));
        }

        [TestCase(
            "System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return new Enumerator(); }",
            0
        )]
        [TestCase(
            "public System.Collections.IEnumerator GetEnumerator() { return new Enumerator(); }",
            1
        )]
        public void RequiredExplicitEnumeratorBoundaryIsSilent(string member, int expected)
        {
            string subject = @"public sealed class Sequence : System.Collections.IEnumerable {
                MEMBER
                private struct Enumerator : System.Collections.IEnumerator {
                    public object Current { get { return null; } }
                    public bool MoveNext() { return false; }
                    public void Reset() { }
                    public void Dispose() { }
                }
            }".Replace("MEMBER", member).Replace(
                "System.Collections.IEnumerator {",
                "System.Collections.IEnumerator, IDisposable {"
            );
            Assert.That(Analyze(subject).Length, Is.EqualTo(expected));
        }

        [Test]
        public void RequiredGenericEnumerableAndDictionaryBoundariesAreSilent()
        {
            Assert.IsEmpty(
                Analyze(
                    @"public sealed class Sequence : System.Collections.Generic.IEnumerable<int> {
                System.Collections.Generic.IEnumerator<int> System.Collections.Generic.IEnumerable<int>.GetEnumerator() { return new System.Collections.Generic.List<int>().GetEnumerator(); }
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return new System.Collections.Generic.List<int>().GetEnumerator(); }
            }"
                )
            );
            Assert.IsEmpty(
                Analyze(
                    @"public sealed class Sequence : System.Collections.Hashtable, System.Collections.IDictionary {
                System.Collections.IDictionaryEnumerator System.Collections.IDictionary.GetEnumerator() { return new System.Collections.Generic.Dictionary<int, int>().GetEnumerator(); }
            }"
                )
            );
        }

        [TestCase("Consumer.g.cs")]
        [TestCase("Consumer.generated.cs")]
        public void GeneratedCodeFollowsFamilyExclusion(string sourcePath)
        {
            Assert.IsEmpty(
                Analyze(
                    "public static object Run(Lease lease) { return lease; }",
                    sourcePath: sourcePath
                )
            );
        }

        [Test]
        public void ConsumerCanSuppressAllocationPolicy()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static object Run(Lease lease) { return lease; }",
                    ReportDiagnostic.Suppress
                )
            );
            Assert.IsEmpty(
                Analyze(
                    "#pragma warning disable WUH022\n public static object Run(Lease lease) { return lease; }\n#pragma warning restore WUH022"
                )
            );
        }
    }
}
