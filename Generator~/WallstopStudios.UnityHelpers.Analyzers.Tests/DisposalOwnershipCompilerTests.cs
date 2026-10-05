// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Diagnostics;
    using NUnit.Framework;

    [TestFixture]
    public sealed class DisposalOwnershipCompilerTests
    {
        private const string BoxingDiagnosticId = "WUH022";
        private const string ConsumerSourcePath = "Consumer.cs";

        private static IEnumerable<TestCaseData> OwnershipCases()
        {
            string[] bodies =
            {
                "SemaphoreLease copy = lease; lease.Dispose(); copy.Dispose();",
                "DisposeCopy(lease); lease.Dispose();",
                "Action release = () => lease.Dispose(); release(); lease.Dispose();",
                "Saved = lease; lease.Dispose(); Saved.Dispose();",
                "lease.Dispose(); lease.Dispose();",
                "using (lease) { lease.Dispose(); }",
            };
            string[] names =
            {
                "Assignment",
                "ByValue",
                "Capture",
                "HeapRetention",
                "RepeatedDisposal",
                "UsingAndManualDisposal",
            };
            for (int index = 0; index < bodies.Length; ++index)
            {
                yield return new TestCaseData(bodies[index], false).SetName(
                    "DisposalOwnership.Compiler." + names[index] + ".Threaded"
                );
                yield return new TestCaseData(bodies[index], true).SetName(
                    "DisposalOwnership.Compiler." + names[index] + ".SingleThreaded"
                );
            }
        }

        private static CSharpCompilation CompileConsumer(
            string body,
            bool singleThreaded,
            bool stackOnly = false
        )
        {
            DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (
                directory != null
                && !File.Exists(
                    Path.Combine(directory.FullName, "Runtime", "Utils", "DisposalLease.cs")
                )
            )
            {
                directory = directory.Parent;
            }
            Assert.That(directory != null, Is.True, "Actual shipped lease sources were not found.");
            CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
            if (singleThreaded)
            {
                parseOptions = parseOptions.WithPreprocessorSymbols("SINGLE_THREADED");
            }
            string disposalPath = Path.Combine(
                directory.FullName,
                "Runtime",
                "Utils",
                "DisposalLease.cs"
            );
            string semaphorePath = Path.Combine(
                directory.FullName,
                "Runtime",
                "Core",
                "Threading",
                "SemaphoreLease.cs"
            );
            string leaseType = stackOnly ? "StackLease" : "SemaphoreLease";
            string acquire = stackOnly
                ? "new StackLease(semaphore.Acquire())"
                : "semaphore.Acquire()";
            string storage = stackOnly ? string.Empty : "public static SemaphoreLease Saved;";
            const string stackLease =
                "public readonly ref struct StackLease { private readonly SemaphoreLease inner; public StackLease(SemaphoreLease lease) { inner = lease; } public void Dispose() { inner.Dispose(); } }";
            SyntaxTree[] trees =
            {
                CSharpSyntaxTree.ParseText(
                    File.ReadAllText(disposalPath),
                    parseOptions,
                    disposalPath
                ),
                CSharpSyntaxTree.ParseText(
                    File.ReadAllText(semaphorePath),
                    parseOptions,
                    semaphorePath
                ),
                CSharpSyntaxTree.ParseText(
                    "using System; using System.Threading; using WallstopStudios.UnityHelpers.Core.Threading; "
                        + (stackOnly ? stackLease : string.Empty)
                        + " public static class Consumer { "
                        + storage
                        + " public static void Run(SemaphoreSlim semaphore) { "
                        + leaseType
                        + " lease = "
                        + acquire
                        + "; "
                        + body
                        + " } private static void DisposeCopy("
                        + leaseType
                        + " lease) { lease.Dispose(); } }",
                    parseOptions,
                    ConsumerSourcePath
                ),
            };
            string platformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            Assert.That(string.IsNullOrEmpty(platformAssemblies), Is.False);
            List<MetadataReference> references = new List<MetadataReference>();
            foreach (string path in platformAssemblies.Split(Path.PathSeparator))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            CSharpCompilation compilation = CSharpCompilation.Create(
                nameof(DisposalOwnershipCompilerTests),
                trees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            foreach (Diagnostic diagnostic in compilation.GetDiagnostics())
            {
                Assert.That(
                    diagnostic.Severity,
                    Is.Not.EqualTo(DiagnosticSeverity.Error),
                    diagnostic.ToString()
                );
            }
            return compilation;
        }

        private static ImmutableArray<Diagnostic> Analyze(CSharpCompilation compilation)
        {
            return compilation
                .WithAnalyzers(
                    ImmutableArray.Create<DiagnosticAnalyzer>(
                        new DisposableStructAssignmentAnalyzer(),
                        new DisposableStructBoxingAnalyzer()
                    )
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }

        [TestCaseSource(nameof(OwnershipCases))]
        public void ActualPublicLeaseOwnershipCopiesCompileWithoutOwnershipDiagnostics(
            string body,
            bool singleThreaded
        )
        {
            Assert.That(Analyze(CompileConsumer(body, singleThreaded)), Is.Empty);
        }

        [TestCase(false, "StackLease copy = lease; lease.Dispose(); copy.Dispose();")]
        [TestCase(true, "StackLease copy = lease; lease.Dispose(); copy.Dispose();")]
        [TestCase(false, "DisposeCopy(lease); lease.Dispose();")]
        [TestCase(true, "DisposeCopy(lease); lease.Dispose();")]
        [TestCase(false, "lease.Dispose(); lease.Dispose();")]
        [TestCase(true, "lease.Dispose(); lease.Dispose();")]
        [TestCase(false, "using (lease) { lease.Dispose(); }")]
        [TestCase(true, "using (lease) { lease.Dispose(); }")]
        public void StackOnlyWrapperStillAllowsOwnershipCopiesAndRepeatedDisposal(
            bool singleThreaded,
            string body
        )
        {
            Assert.That(Analyze(CompileConsumer(body, singleThreaded, stackOnly: true)), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ActualPublicLeaseBoxingWarningCanBeSuppressed(bool singleThreaded)
        {
            const string boxingBody = "IDisposable copy = lease; lease.Dispose(); copy.Dispose();";
            ImmutableArray<Diagnostic> diagnostics = Analyze(
                CompileConsumer(boxingBody, singleThreaded)
            );
            Assert.That(diagnostics.Length, Is.EqualTo(1));
            Assert.That(diagnostics[0].Id, Is.EqualTo(BoxingDiagnosticId));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(
                diagnostics[0].Location.SourceTree.FilePath,
                Is.EqualTo(ConsumerSourcePath)
            );
            Assert.That(
                Analyze(
                    CompileConsumer(
                        "\n#pragma warning disable WUH022\n" + boxingBody,
                        singleThreaded
                    )
                ),
                Is.Empty
            );
        }
    }
}
