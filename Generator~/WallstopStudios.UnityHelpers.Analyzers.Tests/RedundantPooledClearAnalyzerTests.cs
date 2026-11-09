// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Diagnostics;
    using NUnit.Framework;

    [TestFixture]
    public sealed class RedundantPooledClearAnalyzerTests
    {
        internal const string PoolDeclarations =
            @"
namespace WallstopStudios.UnityHelpers.Utils {
    public readonly struct PooledResource<T> : System.IDisposable {
        public T Value { get; }
        public PooledResource(T value) { Value = value; }
        public void Dispose() { }
    }
    public sealed class WallstopGenericPool<T> where T : new() {
        public PooledResource<T> Get(out T value) { value = new T(); return new PooledResource<T>(value); }
        public PooledResource<T> Get() { return new PooledResource<T>(new T()); }
    }
    public static class Buffers {
        public static readonly WallstopGenericPool<System.Text.StringBuilder> StringBuilder = new();
        public static PooledResource<System.Text.StringBuilder> GetStringBuilder(int capacity, out System.Text.StringBuilder value) { return StringBuilder.Get(out value); }
    }
    public static class Buffers<T> {
        public static readonly WallstopGenericPool<System.Collections.Generic.List<T>> List = new();
        public static readonly WallstopGenericPool<System.Collections.Generic.HashSet<T>> HashSet = new();
        public static readonly WallstopGenericPool<System.Collections.Generic.Queue<T>> Queue = new();
        public static readonly WallstopGenericPool<System.Collections.Generic.Stack<T>> Stack = new();
        public static PooledResource<System.Collections.Generic.List<T>> GetList(int capacity, out System.Collections.Generic.List<T> value) { return List.Get(out value); }
    }
    public static class SetBuffers<T> {
        public static readonly WallstopGenericPool<System.Collections.Generic.SortedSet<T>> SortedSet = new();
        public static WallstopGenericPool<System.Collections.Generic.HashSet<T>> GetHashSetPool(System.Collections.Generic.IEqualityComparer<T> comparer) { return new(); }
        public static WallstopGenericPool<System.Collections.Generic.SortedSet<T>> GetSortedSetPool(System.Collections.Generic.IComparer<T> comparer) { return new(); }
    }
    public static class DictionaryBuffer<K, V> {
        public static readonly WallstopGenericPool<System.Collections.Generic.Dictionary<K, V>> Dictionary = new();
        public static readonly WallstopGenericPool<System.Collections.Generic.SortedDictionary<K, V>> SortedDictionary = new();
        public static WallstopGenericPool<System.Collections.Generic.Dictionary<K, V>> GetDictionaryPool(System.Collections.Generic.IEqualityComparer<K> comparer) { return new(); }
        public static WallstopGenericPool<System.Collections.Generic.SortedDictionary<K, V>> GetSortedDictionaryPool(System.Collections.Generic.IComparer<K> comparer) { return new(); }
    }
    public static class LinkedListBuffer<T> {
        public static readonly WallstopGenericPool<System.Collections.Generic.LinkedList<T>> LinkedList = new();
    }
}
namespace Other {
    public static class Buffers<T> {
        public static readonly WallstopStudios.UnityHelpers.Utils.WallstopGenericPool<System.Collections.Generic.List<T>> List = new();
    }
}";

        private const string DiagnosticId = "WUH028";

        private static ImmutableArray<Diagnostic> Analyze(string body, string path = "Consumer.cs")
        {
            string source =
                "using System.Collections.Generic; using System.Text; using WallstopStudios.UnityHelpers.Utils; using KnownLists = WallstopStudios.UnityHelpers.Utils.Buffers<int>; public class Consumer { public void Run() { "
                + body
                + " } } "
                + PoolDeclarations;
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
                "PooledConsumer",
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        source,
                        new CSharpParseOptions(LanguageVersion.CSharp9),
                        path: path
                    ),
                },
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
            return compilation
                .WithAnalyzers(
                    ImmutableArray.Create<DiagnosticAnalyzer>(new RedundantPooledClearAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }

        [TestCase("using var lease = Buffers<int>.List.Get(out var items); items.Clear();")]
        [TestCase("using var lease = Buffers<int>.HashSet.Get(out var items); items.Clear();")]
        [TestCase("using var lease = Buffers<int>.Queue.Get(out var items); items.Clear();")]
        [TestCase("using var lease = Buffers<int>.Stack.Get(out var items); items.Clear();")]
        [TestCase("using var lease = SetBuffers<int>.SortedSet.Get(out var items); items.Clear();")]
        [TestCase("using var lease = Buffers.StringBuilder.Get(out var items); items.Clear();")]
        [TestCase(
            "using var lease = LinkedListBuffer<int>.LinkedList.Get(out var items); items.Clear();"
        )]
        [TestCase(
            "using var lease = DictionaryBuffer<int, int>.Dictionary.Get(out var items); items.Clear();"
        )]
        [TestCase(
            "using var lease = DictionaryBuffer<int, int>.SortedDictionary.Get(out var items); items.Clear();"
        )]
        [TestCase(
            "using var lease = SetBuffers<int>.GetHashSetPool(EqualityComparer<int>.Default).Get(out var items); items.Clear();"
        )]
        [TestCase(
            "using var lease = SetBuffers<int>.GetSortedSetPool(Comparer<int>.Default).Get(out var items); items.Clear();"
        )]
        [TestCase(
            "using var lease = DictionaryBuffer<int, int>.GetDictionaryPool(EqualityComparer<int>.Default).Get(out var items); items.Clear();"
        )]
        [TestCase(
            "using var lease = DictionaryBuffer<int, int>.GetSortedDictionaryPool(Comparer<int>.Default).Get(out var items); items.Clear();"
        )]
        [TestCase("using var lease = Buffers<int>.GetList(32, out var items); items.Clear();")]
        [TestCase("using var lease = Buffers.GetStringBuilder(32, out var items); items.Clear();")]
        [TestCase("using (Buffers<int>.List.Get(out var items)) { items.Clear(); }")]
        [TestCase("using (var lease = Buffers<int>.List.Get(out var items)) items.Clear();")]
        [TestCase(
            "List<int> items; using var lease = Buffers<int>.List.Get(out items); items.Clear();"
        )]
        [TestCase("using var lease = Buffers<int>.List.Get(); lease.Value.Clear();")]
        [TestCase("using var lease = KnownLists.List.Get(out var items); items.Clear();")]
        [TestCase(
            "using var lease = Buffers<(int start, int length)>.List.Get(out var items); items.Clear();"
        )]
        public void FreshKnownBufferClearsReport(string body)
        {
            ImmutableArray<Diagnostic> diagnostics = Analyze(body);
            Assert.That(diagnostics.Length, Is.EqualTo(1));
            Assert.That(diagnostics[0].Id, Is.EqualTo(DiagnosticId));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        }

        [TestCase(
            "using var lease = Buffers<int>.List.Get(out var items); items.Add(1); items.Clear();"
        )]
        [TestCase(
            "using var lease = Buffers<int>.List.Get(out var items); items = new List<int> { 1 }; items.Clear();"
        )]
        [TestCase(
            "using var lease = Buffers<int>.List.Get(out var items); if (items.Count == 0) items.Clear();"
        )]
        [TestCase("var items = new List<int>(); items.Clear();")]
        [TestCase(
            "var pool = new WallstopGenericPool<List<int>>(); using var lease = pool.Get(out var items); items.Clear();"
        )]
        [TestCase(
            "List<int> other = new(); using var lease = Buffers<int>.List.Get(out var items); other.Clear();"
        )]
        [TestCase(
            "using var lease = Buffers<int>.List.Get(out var items); items.Add(1); lease.Value.Clear();"
        )]
        [TestCase(
            "\n#pragma warning disable WUH028\nusing var lease = Buffers<int>.List.Get(out var items); items.Clear();"
        )]
        [TestCase("using var lease = Other.Buffers<int>.List.Get(out var items); items.Clear();")]
        public void MidUseClearsCustomPoolsAndSuppressionsRemainAllowed(string body)
        {
            Assert.That(Analyze(body), Is.Empty);
        }

        [Test]
        public void GeneratedSourcesRemainExcluded()
        {
            Assert.That(
                Analyze(
                    "using var lease = Buffers<int>.List.Get(out var items); items.Clear();",
                    "Consumer.g.cs"
                ),
                Is.Empty
            );
        }
    }
}
