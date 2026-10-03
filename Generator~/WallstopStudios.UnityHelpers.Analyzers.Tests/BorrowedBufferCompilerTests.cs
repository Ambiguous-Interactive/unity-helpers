// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using NUnit.Framework;

    [TestFixture]
    public sealed class BorrowedBufferCompilerTests
    {
        private const string ConsumerSourcePath = "Consumer.cs";

        private static IEnumerable<TestCaseData> EscapeCases()
        {
            yield return new TestCaseData(
                "public sealed class Consumer { public Span<int> Saved; }",
                new[] { "CS8345" }
            ).SetName("BorrowedBuffer.Compiler.HeapStorage");
            yield return new TestCaseData(
                "public static class Consumer { public static Action Save(Span<int> buffer) { return () => Console.WriteLine(buffer.Length); } }",
                new[] { "CS9108", "CS4013" }
            ).SetName("BorrowedBuffer.Compiler.Capture");
            yield return new TestCaseData(
                "public static class Consumer { public static async Task<int> Save(Span<int> buffer, int state) { await Task.Yield(); return buffer[0]; } }",
                new[] { "CS4012" }
            ).SetName("BorrowedBuffer.Compiler.AsyncRetention");
            yield return new TestCaseData(
                "public static class Consumer { public static BufferFunc<int, int, Span<int>> Saved; }",
                new[] { "CS0306", "CS9244" }
            ).SetName("BorrowedBuffer.Compiler.BorrowedResult");
            yield return new TestCaseData(
                "public static class Consumer { public static object Save(Span<int> buffer, int state) { return buffer; } }",
                new[] { "CS0029" }
            ).SetName("BorrowedBuffer.Compiler.Boxing");
            yield return new TestCaseData(
                "public static class Consumer { public static BufferFunc<int, Span<int>, int> Saved; }",
                new[] { "CS0306", "CS9244" }
            ).SetName("BorrowedBuffer.Compiler.BorrowedState");
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (
                    File.Exists(
                        Path.Combine(directory.FullName, "Runtime", "Utils", "BufferAction.cs")
                    )
                )
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            Assert.Fail("Actual shipped delegate sources were not found.");
            return string.Empty;
        }

        private static List<Diagnostic> CompileConsumer(string source)
        {
            string repositoryRoot = FindRepositoryRoot();
            CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
            string actionPath = Path.Combine(repositoryRoot, "Runtime", "Utils", "BufferAction.cs");
            string functionPath = Path.Combine(repositoryRoot, "Runtime", "Utils", "BufferFunc.cs");
            SyntaxTree[] syntaxTrees =
            {
                CSharpSyntaxTree.ParseText(File.ReadAllText(actionPath), parseOptions, actionPath),
                CSharpSyntaxTree.ParseText(
                    File.ReadAllText(functionPath),
                    parseOptions,
                    functionPath
                ),
                CSharpSyntaxTree.ParseText(
                    "using System; using System.Threading.Tasks; using WallstopStudios.UnityHelpers.Utils; "
                        + source,
                    parseOptions,
                    ConsumerSourcePath
                ),
            };
            List<MetadataReference> references = new List<MetadataReference>();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            CSharpCompilation compilation = CSharpCompilation.Create(
                nameof(BorrowedBufferCompilerTests),
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            List<Diagnostic> errors = new List<Diagnostic>();
            foreach (Diagnostic diagnostic in compilation.GetDiagnostics())
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    errors.Add(diagnostic);
                }
            }
            return errors;
        }

        [Test]
        public void ActualBorrowedBufferDelegatesAcceptStaticCallbacksAndOwnedCopies()
        {
            List<Diagnostic> errors = CompileConsumer(
                @"public static class Consumer {
                public static readonly BufferAction<int, int[]> TypedAction = Process;
                public static readonly BufferFunc<int, int, int[]> TypedCopy = Copy;
                public static readonly BufferFunc<int, int, int> TypedScalar = Sum;
                public static readonly BufferAction<int, int[]> Action = static (buffer, state) => state[0] = buffer.Length;
                public static readonly BufferFunc<int, int, int[]> Function = static (buffer, state) => { buffer[0] = state; return buffer.ToArray(); };
                private static void Process(Span<int> buffer, int[] state) { state[0] = buffer.Length; }
                private static int[] Copy(Span<int> buffer, int state) { buffer[0] = state; return buffer.ToArray(); }
                public static int[] Invoke(Span<int> buffer) { return TypedCopy(buffer, 3); }
                private static int Sum(Span<int> buffer, int state) { return buffer.Length + state; }
                public static int InvokeScalar(Span<int> buffer) { return TypedScalar(buffer, 4); }
            }"
            );
            Assert.That(errors, Is.Empty);
        }

        [TestCaseSource(nameof(EscapeCases))]
        public void ActualBorrowedBufferDelegatesRejectEscapingSpans(
            string source,
            string[] expectedDiagnosticIds
        )
        {
            List<Diagnostic> errors = CompileConsumer(source);
            Assert.That(errors.Count, Is.EqualTo(1), string.Join(Environment.NewLine, errors));
            foreach (Diagnostic error in errors)
            {
                Assert.That(error.Location.IsInSource, Is.True, error.ToString());
                Assert.That(error.Location.SourceTree != null, Is.True, error.ToString());
                Assert.That(
                    string.Equals(
                        error.Location.SourceTree.FilePath,
                        ConsumerSourcePath,
                        StringComparison.Ordinal
                    ),
                    Is.True,
                    error.ToString()
                );
                bool expectedFound = false;
                foreach (string expected in expectedDiagnosticIds)
                {
                    expectedFound |= string.Equals(error.Id, expected, StringComparison.Ordinal);
                }
                Assert.That(expectedFound, Is.True, error.ToString());
            }
        }
    }
}
