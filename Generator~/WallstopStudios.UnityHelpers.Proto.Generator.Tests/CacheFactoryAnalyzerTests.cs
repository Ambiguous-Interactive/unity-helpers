// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
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
    /// Pins the one cache-fill rule a source linter cannot enforce: a method group handed to a
    /// concurrent cache's factory allocates a delegate on every call, cache hit included.
    /// </summary>
    /// <remarks>
    /// The negative cases carry the weight here. The whole reason this is an analyzer rather than a
    /// regex is that <c>GetOrAdd(key, Factory)</c> and <c>GetOrAdd(key, cachedFactory)</c> are the
    /// same token in argument position, so a test suite that only proved the positive would not
    /// distinguish this from the casing heuristic it exists to replace (#538).
    /// </remarks>
    [TestFixture]
    public sealed class CacheFactoryAnalyzerTests
    {
        private const string DiagnosticId = "WPROTO038";

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
        /// Every shape session 217 measured at zero or near-zero bytes per call, and the shapes the
        /// three fixed sites were rewritten into.
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
            "an unrelated method taking a delegate",
            @"private static bool Match(string value) => value.Length > 0;
              public static string Get(List<string> values) => values.Find(Match);"
        )]
        public void AMethodGroupOutsideACacheFillIsNotReported(string shape, string body)
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
        /// Every other diagnostic in this assembly is an error, because the alternative is an
        /// exception from inside a shipped player. This one reports an allocation in code that is
        /// otherwise correct, so a warning is the ceiling and turning it off has to work.
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
        /// What the compilation says about WPROTO038 -- <see cref="ReportDiagnostic.Default"/> for a
        /// consumer who configures nothing, or anything else for the ruleset / <c>.editorconfig</c>
        /// entry they would write, expressed as the option Roslyn resolves both of them to.
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
                + "namespace Consumer { public static class Subject { "
                + body
                + " } }";

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
