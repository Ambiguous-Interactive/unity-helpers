// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers.Tests
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Diagnostics;
    using NUnit.Framework;

    /// <summary>
    /// Pins WUH010: a read through a dictionary's key indexer, which throws on a key that is not
    /// there where <c>TryGetValue</c> reports it.
    /// </summary>
    /// <remarks>
    /// The negatives carry the weight. An indexer is one syntax over many unrelated types -- a
    /// list, an array, a string, a span, a write -- so a suite that only proved the positive would
    /// not distinguish this rule from "report every square bracket" (#652).
    /// <para>
    /// Every fixture here is analyzed with the diagnostic explicitly turned ON, because it ships
    /// off: see <see cref="TheDiagnosticIsOffByDefaultSuppressibleAndNeverAboveAWarning"/>, which
    /// is the one test that asks what an unconfigured consumer hears.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class DictionaryIndexerAnalyzerTests
    {
        private const string DiagnosticId = "WUH010";

        /// <summary>
        /// The package's own Unity-serializable dictionary, declared here so the fixtures are
        /// hermetic -- the real one binds UnityEngine and protobuf-net.
        /// </summary>
        /// <remarks>
        /// What matters to the analyzer is the pair of interfaces, not the name, so
        /// <see cref="TheStubbedSerializableDictionaryMatchesTheOneThePackageShips"/> reads the real
        /// source and fails if either one stops being implemented. Note the getter goes through
        /// <c>TryGetValue</c>: an indexer read inside the stub would be a WUH010 site of its own and
        /// would show up in every count below.
        /// </remarks>
        private const string PackageSerializableDictionary =
            @"namespace WallstopStudios.UnityHelpers.Core.DataStructure.Adapters
              {
                  using System.Collections;
                  using System.Collections.Generic;

                  public class SerializableDictionary<TKey, TValue>
                      : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
                  {
                      private readonly Dictionary<TKey, TValue> inner = new Dictionary<TKey, TValue>();

                      public TValue this[TKey key]
                      {
                          get => inner.TryGetValue(key, out TValue found) ? found : default;
                          set => inner[key] = value;
                      }

                      public ICollection<TKey> Keys => inner.Keys;
                      public ICollection<TValue> Values => inner.Values;
                      IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => inner.Keys;
                      IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => inner.Values;
                      public int Count => inner.Count;
                      public bool IsReadOnly => false;
                      public void Add(TKey key, TValue value) => inner.Add(key, value);
                      public void Add(KeyValuePair<TKey, TValue> item) => inner.Add(item.Key, item.Value);
                      public void Clear() => inner.Clear();
                      public bool Contains(KeyValuePair<TKey, TValue> item) => inner.ContainsKey(item.Key);
                      public bool ContainsKey(TKey key) => inner.ContainsKey(key);
                      public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) { }
                      public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => inner.GetEnumerator();
                      public bool Remove(TKey key) => inner.Remove(key);
                      public bool Remove(KeyValuePair<TKey, TValue> item) => inner.Remove(item.Key);
                      public bool TryGetValue(TKey key, out TValue value) => inner.TryGetValue(key, out value);
                      IEnumerator IEnumerable.GetEnumerator() => inner.GetEnumerator();
                  }
              }";

        /// <summary>
        /// A <c>GroupCollection</c> shaped like the one on the netstandard2.1 surface: a string
        /// indexer and no dictionary interface anywhere.
        /// </summary>
        /// <remarks>
        /// The analyzer matches the metadata name and the enclosing namespace, so this has to
        /// declare the real ones -- source wins over metadata for the simple name, which is why it
        /// is appended to ONE fixture rather than to the shared template every other test uses.
        /// </remarks>
        private const string InterfacelessGroupCollection =
            @"namespace System.Text.RegularExpressions
              {
                  public sealed class GroupCollection
                  {
                      public object this[string name] => name;
                  }
              }";

        /// <summary>
        /// Every dictionary the package or its consumers actually hold, plus the one whose indexer
        /// does something worse than throw.
        /// </summary>
        [TestCase(
            "a Dictionary",
            @"public static int Get(Dictionary<string, int> map, string key) => map[key];"
        )]
        [TestCase(
            "an IDictionary",
            @"public static int Get(IDictionary<string, int> map, string key) => map[key];"
        )]
        [TestCase(
            "an IReadOnlyDictionary",
            @"public static int Get(IReadOnlyDictionary<string, int> map, string key) => map[key];"
        )]
        [TestCase(
            "a ConcurrentDictionary",
            @"public static int Get(ConcurrentDictionary<string, int> map, string key) => map[key];"
        )]
        [TestCase(
            "a SortedDictionary",
            @"public static int Get(SortedDictionary<string, int> map, string key) => map[key];"
        )]
        [TestCase(
            "the package's own SerializableDictionary",
            @"public static int Get(SerializableDictionary<string, int> map, string key) => map[key];"
        )]
        [TestCase(
            "a dictionary read nested in a larger expression",
            @"public static int Get(Dictionary<string, int> map, string key) => map[key] + 1;"
        )]
        [TestCase(
            "a dictionary read through an interface-typed field",
            @"private static readonly IDictionary<string, int> Map = new Dictionary<string, int>();
              public static int Get(string key) => Map[key];"
        )]
        public void ADictionaryKeyIndexerReadIsReported(string shape, string body)
        {
            ImmutableArray<Diagnostic> reported = Analyze(body);
            Assert.AreEqual(1, reported.Length, shape + " must be reported exactly once");
            Assert.AreEqual(DiagnosticId, reported[0].Id);
        }

        /// <summary>
        /// <c>GroupCollection</c> implements <c>IReadOnlyDictionary&lt;string, Group&gt;</c>, so the
        /// type test reaches it -- which is the site #652 was opened about.
        /// </summary>
        /// <remarks>
        /// It is also the worst version of the hazard rather than an accident of the type test. The
        /// indexer does not throw on a group name the pattern never declared: it returns a
        /// <c>Group</c> whose <c>Success</c> is <c>false</c>, which is indistinguishable from a
        /// group that exists and did not match, forever.
        /// </remarks>
        [Test]
        public void AMatchGroupsLookupByNameIsReported()
        {
            Diagnostic reported = Single(
                @"public static string Get(Match match) => match.Groups[""name""].Value;"
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
            StringAssert.Contains("GroupCollection", reported.GetMessage());
        }

        /// <summary>
        /// <c>GroupCollection</c> is covered by name as well as by the interface test, and the name
        /// gate is the half that matters where the package ships.
        /// </summary>
        /// <remarks>
        /// <b>Deleting the name gate does not fail the test above.</b> That one runs on net9.0,
        /// where <c>GroupCollection</c> implements <c>IReadOnlyDictionary&lt;string, Group&gt;</c>.
        /// It does NOT implement it on netstandard2.1 -- MEASURED: under that target framework
        /// <c>IReadOnlyDictionary&lt;string, Group&gt; groups = match.Groups;</c> is CS0266 -- which
        /// is what Unity and all four check projects compile against, so the interface test alone
        /// reported the site #652 was opened about in this suite and nowhere a consumer builds.
        /// This fixture declares a <c>GroupCollection</c> carrying NO dictionary interface, so it
        /// passes only through the name gate.
        /// </remarks>
        [Test]
        public void AGroupCollectionWithoutTheInterfaceIsStillReported()
        {
            ImmutableArray<Diagnostic> reported = Analyze(
                @"public static object Get(GroupCollection groups) => groups[""name""];",
                ReportDiagnostic.Warn,
                InterfacelessGroupCollection
            );

            Assert.AreEqual(1, reported.Length, "the name gate has to carry this one alone");
            Assert.AreEqual(DiagnosticId, reported[0].Id);
        }

        /// <summary>
        /// A compound assignment reads before it writes, so the read half throws exactly as a bare
        /// read does. Reporting it is a decision, not a leak in the write exemption.
        /// </summary>
        /// <remarks>
        /// The exemption below is for <c>map[key] = value</c>, which is add-or-update and has no
        /// better <c>Try</c> form. <c>map[key] += 1</c> is not that: it is a read of a key that may
        /// not be there, spelled shorter. The fix is the same one the message names --
        /// <c>TryGetValue</c>, then write the sum back.
        /// </remarks>
        [TestCase(
            "a compound assignment",
            @"public static void Bump(Dictionary<string, int> map, string key) { map[key] += 1; }"
        )]
        [TestCase(
            "an increment",
            @"public static void Bump(Dictionary<string, int> map, string key) { map[key]++; }"
        )]
        [TestCase(
            "a null-coalescing assignment",
            @"public static void Fill(Dictionary<string, string> map, string key) { map[key] ??= ""x""; }"
        )]
        public void AnAssignmentThatAlsoReadsIsReported(string shape, string body)
        {
            ImmutableArray<Diagnostic> reported = Analyze(body);
            Assert.AreEqual(
                1,
                reported.Length,
                shape + " reads before it writes, so it is reported"
            );
            Assert.AreEqual(DiagnosticId, reported[0].Id);
        }

        /// <summary>
        /// A <c>ContainsKey</c>-guarded read is reported, and that is deliberate.
        /// </summary>
        /// <remarks>
        /// Proving the guard covers the read needs dataflow, and the shape it would exempt is a
        /// double lookup that <c>TryGetValue</c> collapses into one. Both reasons point the same
        /// way, so no attempt is made to detect the pair. This test exists so that a future reader
        /// finds the choice pinned rather than discovering it from a build log.
        /// </remarks>
        [Test]
        public void AContainsKeyGuardedReadIsStillReportedAsADoubleLookup()
        {
            Diagnostic reported = Single(
                @"public static int Get(Dictionary<string, int> map, string key)
                  {
                      if (map.ContainsKey(key))
                      {
                          return map[key];
                      }

                      return 0;
                  }"
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
        }

        /// <summary>
        /// Everything an indexer can be that is not a dictionary read, plus the dictionary write
        /// that has no <c>Try</c> form.
        /// </summary>
        [TestCase(
            "a dictionary write, which is add-or-update",
            @"public static void Set(Dictionary<string, int> map, string key) { map[key] = 1; }"
        )]
        [TestCase(
            "a dictionary write through an interface",
            @"public static void Set(IDictionary<string, int> map, string key) { map[key] = 1; }"
        )]
        [TestCase(
            "a dictionary write in an object initializer",
            @"public static Dictionary<string, int> Make() =>
                  new Dictionary<string, int> { [""a""] = 1 };"
        )]
        [TestCase(
            "a dictionary write through a deconstruction",
            @"public static void Set(Dictionary<string, int> map, string key)
              {
                  int first;
                  (first, map[key]) = (1, 2);
                  System.Console.WriteLine(first);
              }"
        )]
        [TestCase(
            "a List indexer",
            @"public static int Get(List<int> values, int index) => values[index];"
        )]
        [TestCase(
            "an array indexer",
            @"public static int Get(int[] values, int index) => values[index];"
        )]
        [TestCase(
            "a string indexer",
            @"public static char Get(string value, int index) => value[index];"
        )]
        [TestCase(
            "a Span indexer",
            @"public static int Get(Span<int> values, int index) => values[index];"
        )]
        [TestCase(
            "a positional GroupCollection indexer, which is IList and not the key one",
            @"public static string Get(Match match) => match.Groups[0].Value;"
        )]
        [TestCase(
            "ConcurrentDictionary.GetOrAdd, which is a method and answers the miss itself",
            @"public static int Get(ConcurrentDictionary<string, int> map, string key) =>
                  map.GetOrAdd(key, static k => k.Length);"
        )]
        [TestCase(
            "TryGetValue itself",
            @"public static int Get(Dictionary<string, int> map, string key) =>
                  map.TryGetValue(key, out int found) ? found : 0;"
        )]
        [TestCase(
            "a consumer type with a string indexer that is no dictionary",
            @"private sealed class Registry
              {
                  public int this[string key] => key.Length;
              }
              private static readonly Registry Store = new Registry();
              public static int Get(string key) => Store[key];"
        )]
        [TestCase(
            "a positional indexer on a type that is keyed by something else",
            @"public static string Get(SortedList<string, string> map, int index) =>
                  map.Values[index];"
        )]
        public void AnIndexerThatIsNotADictionaryKeyReadIsNotReported(string shape, string body)
        {
            Assert.IsEmpty(Analyze(body), shape + " must not be reported");
        }

        /// <summary>
        /// The message has to carry the fix and the opt-in, because the rule is off until someone
        /// reads it and turns it on.
        /// </summary>
        [Test]
        public void TheMessageNamesTryGetValueAndTheRulesetLineThatEnablesIt()
        {
            string message = Single(
                    @"public static int Get(Dictionary<string, int> map, string key) => map[key];"
                )
                .GetMessage();

            StringAssert.Contains("TryGetValue", message);
            StringAssert.Contains("<Rule Id=\"WUH010\" Action=\"Warning\" />", message);
            StringAssert.Contains("Assets/Default.ruleset", message);
            StringAssert.Contains("Dictionary<string, int>", message);
        }

        /// <summary>
        /// The consumer contract for THIS member: a warning at most, suppressible, and -- unlike
        /// every other <c>WUH###</c> -- <b>off</b> until a consumer asks for it.
        /// </summary>
        /// <remarks>
        /// <b>This test asserts the OPPOSITE default from the other WUH fixtures on purpose. It is
        /// not a copy-paste slip and must not be "fixed" to match them.</b> A dictionary read whose
        /// key is known present is correct and ubiquitous, so an on-by-default WUH010 would bury the
        /// nine rules that report shapes which are wrong wherever they appear, on the consumer's
        /// first build after a package upgrade. Rule 17 of <c>.llm/context.md</c> is deviated from
        /// deliberately and only on the default; the warning ceiling is unchanged.
        /// </remarks>
        [Test]
        public void TheDiagnosticIsOffByDefaultSuppressibleAndNeverAboveAWarning()
        {
            DiagnosticDescriptor descriptor =
                new DictionaryIndexerAnalyzer().SupportedDiagnostics.Single(candidate =>
                    candidate.Id == DiagnosticId
                );

            Assert.IsFalse(
                descriptor.IsEnabledByDefault,
                "WUH010 is opt-in: on by default it would bury the other nine"
            );
            Assert.AreEqual(
                DiagnosticSeverity.Warning,
                descriptor.DefaultSeverity,
                "a warning is the ceiling; an error would fail a build over a correct read"
            );

            const string offending =
                @"public static int Get(Dictionary<string, int> map, string key) => map[key];";

            Assert.IsEmpty(
                Analyze(offending, ReportDiagnostic.Default),
                "a consumer who configures nothing must hear nothing from this one"
            );
            Assert.IsNotEmpty(
                Analyze(offending, ReportDiagnostic.Warn),
                "and one who writes the ruleset line must be told"
            );
            Assert.IsEmpty(
                Analyze(offending, ReportDiagnostic.Suppress),
                "and one who turns it back off must be able to"
            );
        }

        /// <summary>
        /// The stub above stands in for real shipped code, so it has to keep standing for it.
        /// </summary>
        /// <remarks>
        /// The analyzer matches the two interfaces rather than the type name, so what would silently
        /// drop the package's own dictionary out of the rule is either interface leaving that
        /// declaration -- while every test here kept passing against the stub.
        /// </remarks>
        [Test]
        public void TheStubbedSerializableDictionaryMatchesTheOneThePackageShips()
        {
            string repoRoot = FindRepositoryRoot();
            string shipped = Path.Combine(
                repoRoot,
                "Runtime",
                "Core",
                "DataStructure",
                "Adapters",
                "SerializableDictionary.cs"
            );
            Assert.IsTrue(File.Exists(shipped), $"expected the shipped dictionary at {shipped}");

            string source = File.ReadAllText(shipped);
            StringAssert.Contains(
                "IDictionary<TKey, TValue>",
                source,
                "the analyzer reaches this type through IDictionary"
            );
            StringAssert.Contains(
                "IReadOnlyDictionary<TKey, TValue>",
                source,
                "and through IReadOnlyDictionary"
            );
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
            return Analyze(body, ReportDiagnostic.Warn);
        }

        /// <summary>
        /// Compiles <paramref name="body"/> and runs the analyzer over it.
        /// </summary>
        /// <param name="body">Members of a static class in namespace <c>Consumer</c>.</param>
        /// <param name="reportedAs">
        /// What the compilation says about the diagnostic -- <see cref="ReportDiagnostic.Default"/>
        /// for a consumer who configures nothing, or the ruleset / <c>.editorconfig</c> entry they
        /// would write, expressed as the option Roslyn resolves both of them to.
        /// <see cref="ReportDiagnostic.Warn"/> is the default here because WUH010 ships off.
        /// </param>
        /// <returns>Everything the analyzer reported.</returns>
        private static ImmutableArray<Diagnostic> Analyze(string body, ReportDiagnostic reportedAs)
        {
            return Analyze(body, reportedAs, string.Empty);
        }

        private static ImmutableArray<Diagnostic> Analyze(
            string body,
            ReportDiagnostic reportedAs,
            string extraTypes
        )
        {
            string source =
                "using System;\n"
                + "using System.Collections.Generic;\n"
                + "using System.Collections.Concurrent;\n"
                + "using System.Text.RegularExpressions;\n"
                + "using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;\n"
                + "namespace Consumer { public static class Subject { "
                + body
                + " } }\n"
                + PackageSerializableDictionary
                + extraTypes;

            HashSet<string> locations = new HashSet<string>(StringComparer.Ordinal);
            List<MetadataReference> references = new List<MetadataReference>();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (
                    !assembly.IsDynamic
                    && !string.IsNullOrEmpty(assembly.Location)
                    && locations.Add(assembly.Location)
                )
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }

            // Regex and ConcurrentDictionary live in assemblies the test host need not have loaded,
            // and a fixture that cannot resolve them fails the compile assertion below rather than
            // reporting anything.
            foreach (
                Type anchor in new[]
                {
                    typeof(object),
                    typeof(Regex),
                    typeof(ConcurrentDictionary<string, int>),
                    typeof(SortedDictionary<string, int>),
                }
            )
            {
                if (locations.Add(anchor.Assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(anchor.Assembly.Location));
                }
            }

            CSharpCompilation compilation = CSharpCompilation.Create(
                "ConsumerAssembly",
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        source,
                        new CSharpParseOptions(LanguageVersion.CSharp9)
                    ),
                },
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
                    ImmutableArray.Create<DiagnosticAnalyzer>(new DictionaryIndexerAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }
    }
}
