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
    /// Pins WUH005: any use of <c>UnityEngine.Random</c>, whose one process-global generator a test
    /// cannot set or read without moving every other caller.
    /// </summary>
    /// <remarks>
    /// The negatives are the reason this is semantic rather than a grep. An alias and a
    /// <c>using static</c> both erase the <c>Random.</c> token a source linter would look for, and
    /// <c>System.Random</c> presents as exactly that token while being a different mistake with a
    /// different fix -- so a suite proving only the positives would not tell this analyzer apart
    /// from the regex it exists to replace (#622).
    /// </remarks>
    [TestFixture]
    public sealed class UnityRandomAnalyzerTests
    {
        private const string DiagnosticId = "WUH005";

        /// <summary>
        /// A hermetic stand-in for the engine generator, so the fixtures need no Unity assemblies.
        /// </summary>
        /// <remarks>
        /// Only the members the diagnostic is asserted over are declared, plus the two return types
        /// they need. The nested <c>State</c> struct is here because it is the one part of
        /// <c>UnityEngine.Random</c> that can be used without naming a member at all.
        /// </remarks>
        private const string UnityRandomStub =
            @"namespace UnityEngine
              {
                  public struct Vector2
                  {
                      public float x;
                      public float y;
                  }

                  public struct Color
                  {
                      public float r;
                      public float g;
                      public float b;
                      public float a;
                  }

                  public static class Random
                  {
                      public static float value => 0f;

                      public static Vector2 insideUnitCircle => default;

                      public static State state { get; set; }

                      public static int Range(int min, int max) => min;

                      public static float Range(float min, float max) => min;

                      public static void InitState(int seed) { }

                      public static Color ColorHSV() => default;

                      public struct State
                      {
                          public int position;
                      }
                  }
              }";

        [TestCase(
            "Range(int, int)",
            "",
            "public static int Roll() => UnityEngine.Random.Range(0, 6);",
            "Range"
        )]
        [TestCase(
            "Range(float, float)",
            "",
            "public static float Spread() => UnityEngine.Random.Range(0f, 1f);",
            "Range"
        )]
        [TestCase(
            "the value property",
            "",
            "public static float Draw() => UnityEngine.Random.value;",
            "value"
        )]
        [TestCase(
            "the insideUnitCircle property",
            "",
            "public static UnityEngine.Vector2 Scatter() => UnityEngine.Random.insideUnitCircle;",
            "insideUnitCircle"
        )]
        [TestCase(
            "InitState, which moves every other caller",
            "",
            "public static void Seed(int seed) { UnityEngine.Random.InitState(seed); }",
            "InitState"
        )]
        [TestCase(
            "ColorHSV",
            "",
            "public static UnityEngine.Color Tint() => UnityEngine.Random.ColorHSV();",
            "ColorHSV"
        )]
        [TestCase(
            "a read of state",
            "",
            "public static object Capture() => UnityEngine.Random.state;",
            "state"
        )]
        [TestCase(
            "a write to state",
            "",
            "public static void Restore() { UnityEngine.Random.state = default; }",
            "state"
        )]
        [TestCase(
            "an alias, which erases the token a grep would match",
            "    using R = UnityEngine.Random;",
            "public static int Roll() => R.Range(0, 6);",
            "Range"
        )]
        [TestCase(
            "a using static, which erases the qualifier entirely",
            "    using static UnityEngine.Random;",
            "public static int Roll() => Range(0, 6);",
            "Range"
        )]
        public void AUseOfUnityEngineRandomIsReported(
            string shape,
            string directives,
            string body,
            string member
        )
        {
            Diagnostic reported = Single(directives, body, shape);

            Assert.AreEqual(DiagnosticId, reported.Id, shape);
            StringAssert.Contains("UnityEngine.Random." + member, reported.GetMessage(), shape);

            // Member locations allow separate suppressions for multiple draws in one statement.
            string reportedText = reported
                .Location.SourceTree.GetText()
                .ToString(reported.Location.SourceSpan);
            StringAssert.EndsWith(member, reportedText, shape);
            StringAssert.DoesNotContain("(", reportedText, shape + " must not span the call");
        }

        /// <summary>
        /// Naming the nested <c>State</c> type reports under the same id, and the message has to be
        /// true of a declaration -- which reads nothing.
        /// </summary>
        /// <remarks>
        /// A second id would have escaped every <c>#pragma warning disable WUH005</c> a consumer had
        /// already written around a deliberate engine save/restore, so the wording carries the
        /// difference instead: the site is "tied to" the engine generator rather than reading it,
        /// and the snapshot fix (<c>RandomState</c>) sits beside the draw fix.
        /// </remarks>
        [TestCase(
            "the nested State struct named as a return type",
            "public static UnityEngine.Random.State Empty() => default;"
        )]
        [TestCase(
            "the nested State struct declared as a field",
            "public static UnityEngine.Random.State Snapshot;"
        )]
        [TestCase(
            "the nested State struct constructed",
            "public static object Fresh() => new UnityEngine.Random.State();"
        )]
        public void NamingTheNestedStateTypeIsReportedWithoutClaimingItReadsAnything(
            string shape,
            string body
        )
        {
            Diagnostic reported = Single(string.Empty, body, shape);

            Assert.AreEqual(DiagnosticId, reported.Id, shape);
            string message = reported.GetMessage();
            StringAssert.Contains("UnityEngine.Random.State", message, shape);
            StringAssert.DoesNotContain(
                "reads process-global",
                message,
                shape + " draws nothing, so the message must not say it reads"
            );
            StringAssert.Contains(
                "RandomState",
                message,
                shape + " needs the portable snapshot type named, not only a generator"
            );
        }

        /// <summary>
        /// The three claims the message makes about the API, each of which was false before.
        /// </summary>
        /// <remarks>
        /// It named <c>IRandom.NextFloat(min, max)</c> as "the non-throwing draw" and then said it
        /// throws; it asserted a state "a test can neither set nor read" while firing on
        /// <c>InitState</c> and <c>state</c>, which are that set and that read; and it said nothing
        /// about <c>UnityEngine.Random.Range(x, x)</c> being legal, which is what makes the port
        /// dangerous.
        /// </remarks>
        [Test]
        public void TheMessageNamesTheNonThrowingSiblingAndTheSetAndReadItFiresOn()
        {
            string message = Single(
                    string.Empty,
                    "public static float Draw() => UnityEngine.Random.value;",
                    "the value property"
                )
                .GetMessage();

            StringAssert.Contains("NextFloatInRange", message, "the non-throwing sibling");
            StringAssert.Contains(
                "'UnityEngine.Random.Range(x, x)' returns x",
                message,
                "a straight port of a zero-width spread is what throws"
            );
            StringAssert.DoesNotContain(
                "neither set nor read",
                message,
                "'InitState' and 'state' are exactly that set and that read"
            );
            StringAssert.Contains(
                "InitState",
                message,
                "the message has to own the two members that do set and read the state"
            );
        }

        /// <summary>
        /// The message spells two package members, so both have to keep naming something real.
        /// </summary>
        /// <remarks>
        /// The wording it replaced pointed at <c>NextFloat</c> as the non-throwing draw, which is
        /// the throwing one. A message is not covered by any compiler, so the only thing that keeps
        /// it honest is reading the shipped source.
        /// </remarks>
        [Test]
        public void TheFixTheMessageOffersMatchesWhatThePackageShips()
        {
            string repositoryRoot = FindRepositoryRoot();
            string extensions = Path.Combine(
                repositoryRoot,
                "Runtime",
                "Core",
                "Extension",
                "RandomExtensions.cs"
            );
            Assert.IsTrue(File.Exists(extensions), $"expected the ranged draws at {extensions}");
            StringAssert.Contains(
                "public static float NextFloatInRange(this IRandom random, float low, float high)",
                File.ReadAllText(extensions),
                "the message names this as the non-throwing sibling"
            );

            string state = Path.Combine(
                repositoryRoot,
                "Runtime",
                "Core",
                "Random",
                "RandomState.cs"
            );
            Assert.IsTrue(File.Exists(state), $"expected the portable snapshot at {state}");
            StringAssert.Contains(
                "struct RandomState",
                File.ReadAllText(state),
                "the message names this as the replacement for UnityEngine.Random.State"
            );
        }

        /// <summary>
        /// <c>System.Random</c> is a different mistake with a different fix, and a consumer's own
        /// type is no mistake at all.
        /// </summary>
        /// <remarks>
        /// Reporting either would make the message -- which names <c>PRNG.Instance</c> and the
        /// engine's global state -- wrong about the code it is pointing at, which is the whole
        /// argument the issue makes for keeping the two apart.
        /// </remarks>
        [TestCase(
            "a System.Random instance",
            @"private static readonly System.Random Rng = new System.Random();
              public static int Roll() => Rng.Next(6);"
        )]
        [TestCase(
            "System.Random used statically",
            "public static int Roll() => System.Random.Shared.Next(6);"
        )]
        [TestCase(
            "a consumer's own type named Random",
            @"public static int Roll() => Random.Range(0, 6);

              private static class Random
              {
                  public static int Range(int min, int max) => min;
              }"
        )]
        public void AGeneratorThatIsNotTheEngineIsNotReported(string shape, string body)
        {
            Assert.IsEmpty(Analyze(body), shape + " must not be reported");
        }

        /// <summary>
        /// The package's own adapter over the engine generator exists to call
        /// <c>UnityEngine.Random</c>, so it is exempt -- and it alone.
        /// </summary>
        /// <remarks>
        /// The neighbor carries byte-identical code in the same namespace. Without it, an exemption
        /// accidentally written against the namespace would pass this test while silently excusing
        /// all twenty seedable generators that live beside the adapter.
        /// </remarks>
        [Test]
        public void ThePackagesOwnEngineWrapperIsExemptAndNothingBesideIt()
        {
            const string source =
                @"namespace WallstopStudios.UnityHelpers.Core.Random
                  {
                      public sealed class UnityRandom
                      {
                          public float Next() => UnityEngine.Random.value;
                      }

                      public sealed class Neighbor
                      {
                          public float Next() => UnityEngine.Random.value;
                      }
                  }
                 " + UnityRandomStub;

            ImmutableArray<Diagnostic> reported = Run(
                Compile(source, ReportDiagnostic.Default),
                "the exemption fixture"
            );

            Assert.AreEqual(1, reported.Length, "only the neighbor draws a diagnostic");
            int neighborStart = source.IndexOf("class Neighbor", StringComparison.Ordinal);
            Assert.IsTrue(
                neighborStart <= reported[0].Location.SourceSpan.Start,
                "the reported site must be inside Neighbor, not inside the exempt wrapper"
            );
        }

        /// <summary>
        /// A compilation with no <c>UnityEngine.Random</c> gets no registered actions at all.
        /// </summary>
        /// <remarks>
        /// That is most consumers' Editor-side tooling and every non-Unity project that references
        /// the package, so the guard is what keeps the analyzer from walking every operation in a
        /// tree it can never report on.
        /// </remarks>
        [Test]
        public void TheAnalyzerRegistersNothingWithoutUnityEngineRandom()
        {
            CSharpCompilation compilation = Compile(
                @"namespace Consumer
                  {
                      public static class Subject
                      {
                          public static int Roll(System.Random rng) => rng.Next(6);
                      }
                  }",
                ReportDiagnostic.Default
            );

            Assert.IsNull(
                compilation.GetTypeByMetadataName("UnityEngine.Random"),
                "the guard under test is what happens when this resolves to nothing"
            );
            Assert.IsEmpty(Run(compilation, "a compilation without UnityEngine.Random"));
        }

        /// <summary>
        /// The consumer contract for the whole WUH family: on by default, and never able to fail a
        /// build.
        /// </summary>
        [Test]
        public void TheDiagnosticIsOnByDefaultSuppressibleAndNeverAboveAWarning()
        {
            DiagnosticDescriptor descriptor = new UnityRandomAnalyzer().SupportedDiagnostics.Single(
                candidate => candidate.Id == DiagnosticId
            );

            Assert.IsTrue(
                descriptor.IsEnabledByDefault,
                "a consumer using this package should get the safety without asking for it"
            );
            Assert.AreEqual(
                DiagnosticSeverity.Warning,
                descriptor.DefaultSeverity,
                "a warning is the ceiling; an error would fail a build over a working draw"
            );

            const string offending = "public static float Draw() => UnityEngine.Random.value;";

            Assert.IsNotEmpty(
                Analyze(string.Empty, offending, ReportDiagnostic.Default),
                "a consumer who configures nothing must still be told"
            );
            Assert.IsEmpty(
                Analyze(string.Empty, offending, ReportDiagnostic.Suppress),
                "and an editor-only assembly with nothing to replay must be able to turn it off"
            );
        }

        /// <summary>
        /// One suppression has to cover a deliberate save/restore whole -- the member draws AND the
        /// <c>State</c> declaration that holds the snapshot.
        /// </summary>
        /// <remarks>
        /// <c>Tests/Runtime/Performance/RandomPerformanceTests.cs</c> is that shape in this
        /// repository: it benchmarks the engine generator and puts it back afterwards, under a
        /// single <c>#pragma warning disable WUH005</c>. Reporting the declaration under a second id
        /// would leave half the block warning again after a package upgrade.
        /// </remarks>
        [Test]
        public void OneSuppressionCoversBothTheDrawAndTheSnapshotDeclaration()
        {
            const string saveAndRestore =
                @"public static void Benchmark()
                  {
                      UnityEngine.Random.State original = UnityEngine.Random.state;
                      UnityEngine.Random.InitState(7);
                      UnityEngine.Random.state = original;
                  }";

            Assert.AreEqual(
                4,
                Analyze(string.Empty, saveAndRestore, ReportDiagnostic.Default).Length,
                "the declaration, the read, the seed and the restore are all WUH005"
            );
            Assert.IsEmpty(
                Analyze(string.Empty, saveAndRestore, ReportDiagnostic.Suppress),
                "one id turns the whole deliberate save/restore off"
            );
        }

        /// <summary>
        /// The exemption is a namespace and a type name spelled in the analyzer, so it has to keep
        /// naming something real.
        /// </summary>
        /// <remarks>
        /// If the adapter were renamed or moved, every test above would keep passing while the one
        /// type that legitimately wraps the engine generator started reporting itself on every line.
        /// </remarks>
        [Test]
        public void TheExemptedWrapperMatchesTheTypeThePackageShips()
        {
            string shipped = Path.Combine(
                FindRepositoryRoot(),
                "Runtime",
                "Core",
                "Random",
                "UnityRandom.cs"
            );
            Assert.IsTrue(File.Exists(shipped), $"expected the shipped adapter at {shipped}");

            string source = File.ReadAllText(shipped);
            StringAssert.Contains(
                "namespace WallstopStudios.UnityHelpers.Core.Random",
                source,
                "the analyzer's exemption names this namespace"
            );
            StringAssert.Contains(
                "class UnityRandom",
                source,
                "the analyzer's exemption names this type"
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

        private static Diagnostic Single(string directives, string body, string shape)
        {
            ImmutableArray<Diagnostic> reported = Analyze(
                directives,
                body,
                ReportDiagnostic.Default
            );
            Assert.AreEqual(1, reported.Length, shape + " must report exactly one diagnostic");
            return reported[0];
        }

        private static ImmutableArray<Diagnostic> Analyze(string body)
        {
            return Analyze(string.Empty, body, ReportDiagnostic.Default);
        }

        /// <summary>
        /// Compiles <paramref name="body"/> as members of a static class and runs the analyzer.
        /// </summary>
        /// <param name="directives">
        /// Using directives placed inside <c>namespace Consumer</c>, which is where an alias or a
        /// <c>using static</c> has to sit for the fixture to exercise it.
        /// </param>
        /// <param name="body">Members of a static class in namespace <c>Consumer</c>.</param>
        /// <param name="reportedAs">
        /// What the compilation says about the diagnostic -- <see cref="ReportDiagnostic.Default"/>
        /// for a consumer who configures nothing, or anything else for the ruleset /
        /// <c>.editorconfig</c> entry they would write, expressed as the option Roslyn resolves both
        /// of them to.
        /// </param>
        /// <returns>Everything the analyzer reported.</returns>
        private static ImmutableArray<Diagnostic> Analyze(
            string directives,
            string body,
            ReportDiagnostic reportedAs
        )
        {
            string source =
                "namespace Consumer\n{\n"
                + directives
                + "\n    public static class Subject\n    {\n"
                + body
                + "\n    }\n}\n"
                + UnityRandomStub;

            return Run(Compile(source, reportedAs), "the fixture");
        }

        private static CSharpCompilation Compile(string source, ReportDiagnostic reportedAs)
        {
            List<MetadataReference> references = new List<MetadataReference>();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }

            return CSharpCompilation.Create(
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
        }

        private static ImmutableArray<Diagnostic> Run(
            CSharpCompilation compilation,
            string described
        )
        {
            ImmutableArray<Diagnostic> compileErrors = compilation
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();
            Assert.IsEmpty(
                compileErrors.Select(diagnostic => diagnostic.ToString()).ToArray(),
                described + " must compile"
            );

            return compilation
                .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new UnityRandomAnalyzer()))
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }
    }
}
