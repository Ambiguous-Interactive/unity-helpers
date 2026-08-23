// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Diagnostics;
    using NUnit.Framework;

    /// <summary>
    /// Pins the one cache-fill rule a source linter cannot enforce: a method group handed to a
    /// lookup's value factory allocates a delegate on every call, hits included.
    /// </summary>
    /// <remarks>
    /// The negative cases carry the weight here. The whole reason this is an analyzer rather than a
    /// regex is that <c>GetOrAdd(key, Factory)</c> and <c>GetOrAdd(key, cachedFactory)</c> are the
    /// same token in argument position, so a suite that only proved the positive would not
    /// distinguish this from the casing heuristic it exists to replace (#538).
    /// </remarks>
    [TestFixture]
    public sealed class CacheFactoryAnalyzerTests
    {
        private const string DiagnosticId = "WUH001";

        /// <summary>
        /// The package's own dictionary extensions, declared here so the fixtures are hermetic.
        /// </summary>
        /// <remarks>
        /// A stub can drift from what it stands for, so
        /// <see cref="TheStubbedExtensionsMatchTheOnesThePackageShips"/> reads the real source and
        /// fails if these names or that namespace stop existing.
        /// </remarks>
        private const string PackageDictionaryExtensions =
            @"namespace WallstopStudios.UnityHelpers.Core.Extension
              {
                  using System;
                  using System.Collections.Generic;

                  public static class DictionaryExtensions
                  {
                      public static V GetOrAdd<K, V>(this IDictionary<K, V> d, K key, Func<V> f) => f();
                      public static V GetOrAdd<K, V>(this IDictionary<K, V> d, K key, Func<K, V> f) => f(key);
                      public static V GetOrElse<K, V>(this IReadOnlyDictionary<K, V> d, K key, Func<V> f) => f();
                      public static V AddOrUpdate<K, V>(this IDictionary<K, V> d, K key, Func<K, V> c, Func<K, V, V> u) => c(key);
                      public static V TryAdd<K, V>(this IDictionary<K, V> d, K key, Func<K, V> c) => c(key);
                      public static Dictionary<K, V> Merge<K, V>(this IReadOnlyDictionary<K, V> l, IReadOnlyDictionary<K, V> r, Func<Dictionary<K, V>> c = null) => c();
                      public static Dictionary<K, V> ToDictionary<K, V>(this IReadOnlyDictionary<K, V> d) => null;
                  }
              }";

        [Test]
        public void AMethodGroupFactoryIsReported()
        {
            Diagnostic reported = Single(
                @"private static readonly ConcurrentDictionary<string, int> Cache =
                      new ConcurrentDictionary<string, int>();
                  private static int Create(string key) => key.Length;
                  public static int Get(string key) => Cache.GetOrAdd(key, Create);"
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
            string message = reported.GetMessage();
            StringAssert.Contains("Create", message);
            StringAssert.Contains("GetOrAdd", message);
        }

        /// <summary>
        /// A plain <c>Dictionary</c> has no factory-taking member in the BCL, so it reaches the same
        /// defect only through this package's extensions -- which is exactly why they are covered.
        /// </summary>
        [TestCase(
            "a Dictionary through GetOrAdd",
            @"private static readonly Dictionary<string, int> Cache = new Dictionary<string, int>();
              private static int Create(string key) => key.Length;
              public static int Get(string key) => Cache.GetOrAdd(key, Create);"
        )]
        [TestCase(
            "a Dictionary through GetOrElse, which does not even add",
            @"private static readonly Dictionary<string, int> Cache = new Dictionary<string, int>();
              private static int Create() => 7;
              public static int Get(string key) => Cache.GetOrElse(key, Create);"
        )]
        [TestCase(
            "an IDictionary through AddOrUpdate",
            @"private static int Create(string key) => key.Length;
              private static int Update(string key, int existing) => existing + 1;
              public static int Get(IDictionary<string, int> cache, string key) =>
                  cache.AddOrUpdate(key, Create, Update);"
        )]
        [TestCase(
            "TryAdd, whose creator runs only when the key is absent",
            @"private static readonly Dictionary<string, int> Cache = new Dictionary<string, int>();
              private static int Create(string key) => key.Length;
              public static int Get(string key) => Cache.TryAdd(key, Create);"
        )]
        [TestCase(
            "Merge, whose optional creator is a delegate parameter like any other",
            @"private static Dictionary<string, int> Create() => new Dictionary<string, int>();
              public static Dictionary<string, int> Get(
                  IReadOnlyDictionary<string, int> a,
                  IReadOnlyDictionary<string, int> b) => a.Merge(b, Create);"
        )]
        [TestCase(
            "the extension called in unreduced form",
            @"private static readonly Dictionary<string, int> Cache = new Dictionary<string, int>();
              private static int Create(string key) => key.Length;
              public static int Get(string key) =>
                  WallstopStudios.UnityHelpers.Core.Extension.DictionaryExtensions.GetOrAdd(Cache, key, Create);"
        )]
        public void AMethodGroupThroughThePackagesOwnExtensionsIsReported(string shape, string body)
        {
            ImmutableArray<Diagnostic> reported = Analyze(body);
            Assert.IsNotEmpty(reported, shape + " must be reported");
            Assert.IsTrue(
                reported.All(diagnostic => diagnostic.Id == DiagnosticId),
                shape + " must report only WUH001"
            );
        }

        /// <summary>
        /// Every shape session 217 measured at zero or near-zero bytes per call, and the shapes the
        /// fixed sites were rewritten into.
        /// </summary>
        [TestCase(
            "a cached delegate field",
            @"private static readonly Func<string, int> Factory = Create;
              private static int Create(string key) => key.Length;
              public static int Get(string key) => Cache.GetOrAdd(key, Factory);"
        )]
        [TestCase(
            "a static lambda",
            @"public static int Get(string key) => Cache.GetOrAdd(key, static k => k.Length);"
        )]
        [TestCase(
            "the state-taking overload with a static lambda",
            @"public static int Get(string key, string state) =>
                  Cache.GetOrAdd(key, static (k, s) => k.Length + s.Length, state);"
        )]
        [TestCase(
            "a local delegate variable",
            @"private static int Create(string key) => key.Length;
              public static int Get(string key)
              {
                  Func<string, int> factory = Create;
                  return Cache.GetOrAdd(key, factory);
              }"
        )]
        [TestCase(
            "an added value rather than a factory",
            @"public static int Get(string key) => Cache.GetOrAdd(key, 7);"
        )]
        public void AFactoryThatDoesNotAllocatePerCallIsNotReported(string shape, string body)
        {
            Assert.IsEmpty(
                Analyze(
                    @"private static readonly ConcurrentDictionary<string, int> Cache =
                          new ConcurrentDictionary<string, int>();
                      " + body
                ),
                shape + " must not be reported"
            );
        }

        [Test]
        public void EveryMethodGroupInOneAddOrUpdateIsReported()
        {
            ImmutableArray<Diagnostic> reported = Analyze(
                @"private static readonly ConcurrentDictionary<string, int> Cache =
                      new ConcurrentDictionary<string, int>();
                  private static int Create(string key) => key.Length;
                  private static int Update(string key, int existing) => existing + 1;
                  public static int Get(string key) => Cache.AddOrUpdate(key, Create, Update);"
            );

            CollectionAssert.AreEquivalent(
                new[] { "Create", "Update" },
                reported.Select(diagnostic => diagnostic.GetMessage().Split('\'')[1]).ToArray()
            );
        }

        [Test]
        public void AConditionalWeakTableCallbackIsReported()
        {
            Diagnostic reported = Single(
                @"private static readonly ConditionalWeakTable<string, object> Table =
                      new ConditionalWeakTable<string, object>();
                  private static object Create(string key) => new object();
                  public static object Get(string key) => Table.GetValue(key, Create);"
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
            StringAssert.Contains("Create", reported.GetMessage());
        }

        /// <summary>
        /// A method group is only worth reporting where the delegate is rebuilt per lookup. A type
        /// of the consumer's own that happens to spell a member <c>GetOrAdd</c> is not that, and
        /// neither is any other method taking a delegate.
        /// </summary>
        [TestCase(
            "a consumer type that merely spells the member GetOrAdd",
            @"private sealed class Registry
              {
                  public int GetOrAdd(string key, Func<string, int> factory) => factory(key);
              }
              private static readonly Registry Store = new Registry();
              private static int Create(string key) => key.Length;
              public static int Get(string key) => Store.GetOrAdd(key, Create);"
        )]
        [TestCase(
            "a consumer extension class that merely spells the member GetOrAdd",
            @"public static int Get(Dictionary<string, int> cache, string key) =>
                  Other.Ext.GetOrAdd(cache, key, Create);
              private static int Create(string key) => key.Length;"
        )]
        [TestCase(
            "a package extension member that takes no delegate at all",
            @"public static Dictionary<string, int> Get(IReadOnlyDictionary<string, int> d) =>
                  d.ToDictionary();"
        )]
        [TestCase(
            "an unrelated method taking a delegate",
            @"private static bool Match(string value) => value.Length > 0;
              public static string Get(List<string> values) => values.Find(Match);"
        )]
        public void AMethodGroupOutsideAFactoryTakingLookupIsNotReported(string shape, string body)
        {
            Assert.IsEmpty(Analyze(body), shape + " must not be reported");
        }

        /// <summary>
        /// C# 11 caches a method-group conversion in a compiler-generated field, so from that
        /// version on the diagnostic is simply false.
        /// </summary>
        /// <remarks>
        /// Unity pins C# 9 on every version this package supports, which is what makes the shape
        /// worth reporting at all. This proves the guard rather than assuming the analyzer will
        /// never meet a newer compiler -- and it is the assertion that fails if the version constant
        /// is ever compared the wrong way round.
        /// </remarks>
        [Test]
        public void AMethodGroupIsNotReportedOnACompilerThatCachesIt()
        {
            const string source =
                @"private static readonly ConcurrentDictionary<string, int> Cache =
                      new ConcurrentDictionary<string, int>();
                  private static int Create(string key) => key.Length;
                  public static int Get(string key) => Cache.GetOrAdd(key, Create);";

            Assert.IsNotEmpty(Analyze(source, LanguageVersion.CSharp9));
            Assert.IsEmpty(Analyze(source, LanguageVersion.CSharp11));
        }

        /// <summary>
        /// The consumer contract: on by default, so a project that has taken on this package gets
        /// the safety without discovering it, but never able to fail their build.
        /// </summary>
        /// <remarks>
        /// A WallstopProto diagnostic is an error, because the alternative is an exception from
        /// inside a shipped player. A WUH diagnostic reports an allocation in code that is otherwise
        /// correct, so a warning is the ceiling and turning it off has to work.
        /// </remarks>
        [Test]
        public void TheDiagnosticIsOnByDefaultSuppressibleAndNeverAboveAWarning()
        {
            DiagnosticDescriptor descriptor =
                new CacheFactoryAnalyzer().SupportedDiagnostics.Single(candidate =>
                    candidate.Id == DiagnosticId
                );

            Assert.IsTrue(
                descriptor.IsEnabledByDefault,
                "a consumer using this package should get the safety without asking for it"
            );
            Assert.AreEqual(
                DiagnosticSeverity.Warning,
                descriptor.DefaultSeverity,
                "a warning is the ceiling; an error would fail a build over an allocation"
            );

            const string offending =
                @"private static readonly ConcurrentDictionary<string, int> Cache =
                      new ConcurrentDictionary<string, int>();
                  private static int Create(string key) => key.Length;
                  public static int Get(string key) => Cache.GetOrAdd(key, Create);";

            Assert.IsNotEmpty(
                Analyze(offending, LanguageVersion.CSharp9, ReportDiagnostic.Default),
                "a consumer who configures nothing must still be told"
            );
            Assert.IsEmpty(
                Analyze(offending, LanguageVersion.CSharp9, ReportDiagnostic.Suppress),
                "and one who does not want it must be able to turn it off"
            );
        }

        /// <summary>
        /// The stub above stands in for real shipped code, so it has to keep standing for it.
        /// </summary>
        /// <remarks>
        /// The analyzer matches on a namespace and a type name that live in
        /// <c>Runtime/Core/Extension/DictionaryExtensions.cs</c>. Renaming either would make the
        /// analyzer silently stop covering the package's own dictionaries while every test here kept
        /// passing against the stub, which is the one way this suite could lie.
        /// </remarks>
        [Test]
        public void TheStubbedExtensionsMatchTheOnesThePackageShips()
        {
            string repoRoot = FindRepositoryRoot();
            string shipped = Path.Combine(
                repoRoot,
                "Runtime",
                "Core",
                "Extension",
                "DictionaryExtensions.cs"
            );
            Assert.IsTrue(File.Exists(shipped), $"expected the shipped extensions at {shipped}");

            string source = File.ReadAllText(shipped);
            StringAssert.Contains(
                "namespace WallstopStudios.UnityHelpers.Core.Extension",
                source,
                "the analyzer matches this namespace by name"
            );
            StringAssert.Contains(
                "class DictionaryExtensions",
                source,
                "the analyzer matches this type by name"
            );
            foreach (string method in new[] { "GetOrAdd", "GetOrElse", "AddOrUpdate", "TryAdd" })
            {
                StringAssert.Contains(
                    $" {method}<K, V>(",
                    source,
                    $"the analyzer covers {method}, so it has to still be there"
                );
            }
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "Runtime")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not find the repository root above the test directory"
            );
        }

        private static Diagnostic Single(string body)
        {
            ImmutableArray<Diagnostic> reported = Analyze(body);
            Assert.AreEqual(1, reported.Length, "Expected exactly one diagnostic");
            return reported[0];
        }

        private static ImmutableArray<Diagnostic> Analyze(string body)
        {
            return Analyze(body, LanguageVersion.CSharp9);
        }

        private static ImmutableArray<Diagnostic> Analyze(string body, LanguageVersion language)
        {
            return Analyze(body, language, ReportDiagnostic.Default);
        }

        /// <summary>
        /// Compiles <paramref name="body"/> and runs the analyzer over it.
        /// </summary>
        /// <param name="body">Members of a static class in namespace <c>Consumer</c>.</param>
        /// <param name="language">Language version the fixture is parsed at.</param>
        /// <param name="reportedAs">
        /// What the compilation says about the diagnostic -- <see cref="ReportDiagnostic.Default"/>
        /// for a consumer who configures nothing, or anything else for the ruleset /
        /// <c>.editorconfig</c> entry they would write, expressed as the option Roslyn resolves both
        /// of them to.
        /// </param>
        /// <returns>Everything the analyzer reported.</returns>
        private static ImmutableArray<Diagnostic> Analyze(
            string body,
            LanguageVersion language,
            ReportDiagnostic reportedAs
        )
        {
            string source =
                "using System;\n"
                + "using System.Collections.Generic;\n"
                + "using System.Collections.Concurrent;\n"
                + "using System.Runtime.CompilerServices;\n"
                + "using WallstopStudios.UnityHelpers.Core.Extension;\n"
                + "namespace Other { public static class Ext { public static int GetOrAdd<K, V>(this Dictionary<K, V> d, K k, Func<K, int> f) => f(k); } }\n"
                + "namespace Consumer { public static class Subject { "
                + body
                + " } }\n"
                + PackageDictionaryExtensions;

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
                new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(language)) },
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

            // A fixture that does not compile would report nothing and read as a pass, which is the
            // one way this suite could go quietly green while the analyzer did nothing at all.
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
                    ImmutableArray.Create<DiagnosticAnalyzer>(new CacheFactoryAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }
    }
}
