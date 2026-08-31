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
    /// Pins the teardown half of the base-call rule: setup chains base-FIRST, teardown chains
    /// base-LAST.
    /// </summary>
    /// <remarks>
    /// The <c>Awake</c> and <c>OnEnable</c> negatives carry as much weight as any positive here.
    /// They are what proves the asymmetry is actually encoded rather than assumed: an analyzer that
    /// reported "base call is not last" on every override would pass every positive in this file
    /// and be wrong about the two hooks where base-first is the correct order (#630).
    /// </remarks>
    [TestFixture]
    public sealed class TeardownBaseCallAnalyzerTests
    {
        private const string DiagnosticId = "WUH009";

        /// <summary>
        /// A hermetic stand-in for the package base classes that declare these hooks, so the
        /// fixtures need neither UnityEngine nor the package itself.
        /// </summary>
        /// <remarks>
        /// A stub can drift from what it stands for, so
        /// <see cref="TheStubbedHooksMatchTheOnesThePackageShips"/> reads the real source and fails
        /// if a hook this analyzer covers stops being declared there.
        /// </remarks>
        private const string PackageTeardownBase =
            @"namespace Package
              {
                  using System;

                  public class Component : IDisposable
                  {
                      protected int handles;

                      protected void Release() { }

                      protected virtual void Awake() { }
                      protected virtual void OnEnable() { }
                      protected virtual void OnDestroy() { }
                      protected virtual void OnDisable() { }
                      protected virtual void OnApplicationQuit() { }
                      protected virtual void Foo() { }
                      public virtual void Dispose() { }
                  }
              }";

        /// <summary>
        /// Every hook, at one statement after the base call and at three, so the count in the
        /// message is asserted rather than assumed.
        /// </summary>
        [TestCase("OnDestroy", 1)]
        [TestCase("OnDestroy", 3)]
        [TestCase("OnDisable", 1)]
        [TestCase("OnDisable", 3)]
        [TestCase("OnApplicationQuit", 1)]
        [TestCase("OnApplicationQuit", 3)]
        [TestCase("Dispose", 1)]
        [TestCase("Dispose", 3)]
        public void ABaseCallBeforeTheRestOfTheTeardownIsReported(string hook, int following)
        {
            Diagnostic reported = Single(Override(hook, "base." + hook + "();", following));

            Assert.AreEqual(DiagnosticId, reported.Id);
            string message = reported.GetMessage();
            StringAssert.Contains("'base." + hook + "()'", message);
            StringAssert.Contains(
                following + " statement(s) run after it",
                message,
                "the message has to say how much of the body runs against a dismantled object"
            );
        }

        [Test]
        public void ABaseCallInTheMiddleOfTheBodyIsReportedAtItsOwnLocation()
        {
            const string body =
                @"protected override void OnDestroy()
                  {
                      Release();
                      handles = 0;
                      base.OnDestroy();
                      Release();
                      handles = 1;
                  }";

            Diagnostic reported = Single(body);

            StringAssert.Contains("2 statement(s) run after it", reported.GetMessage());
            Assert.AreEqual(
                "base.OnDestroy()",
                reported.Location.SourceTree.GetText().ToString(reported.Location.SourceSpan),
                "the diagnostic belongs on the call that has to move, not on the method"
            );
        }

        /// <summary>
        /// Everything that is either already correct or outside what this rule can judge.
        /// </summary>
        [TestCase(
            "the base call as the last statement",
            @"protected override void OnDestroy()
              {
                  Release();
                  base.OnDestroy();
              }"
        )]
        [TestCase(
            "the base call as the only statement",
            @"protected override void OnDestroy()
              {
                  base.OnDestroy();
              }"
        )]
        [TestCase(
            "an expression-bodied override, which cannot have anything after the call",
            @"protected override void OnDestroy() => base.OnDestroy();"
        )]
        [TestCase(
            "a body with no base call at all",
            @"protected override void OnDestroy()
              {
                  Release();
                  handles = 0;
              }"
        )]
        [TestCase(
            "a non-override method that merely spells the name OnDestroy",
            @"private new void OnDestroy()
              {
                  base.OnDestroy();
                  Release();
              }"
        )]
        [TestCase(
            "a base call to a different method",
            @"protected override void OnDestroy()
              {
                  base.Foo();
                  Release();
              }"
        )]
        [TestCase(
            "a local function after the base call, which is a declaration and not work",
            @"protected override void OnDestroy()
              {
                  base.OnDestroy();
                  void Helper() { }
              }"
        )]
        [TestCase(
            "an empty statement after the base call",
            @"protected override void OnDestroy()
              {
                  base.OnDestroy();
                  ;
              }"
        )]
        [TestCase(
            "a local function and an empty statement together after the base call",
            @"protected override void OnDestroy()
              {
                  Release();
                  base.OnDestroy();
                  ;
                  void Helper() { }
              }"
        )]
        [TestCase(
            "a base call nested inside an if, which this rule declines to judge",
            @"protected override void OnDestroy()
              {
                  if (0 < handles)
                  {
                      base.OnDestroy();
                      Release();
                  }
              }"
        )]
        [TestCase(
            "a base call nested inside a try, which this rule declines to judge",
            @"protected override void OnDestroy()
              {
                  try
                  {
                      base.OnDestroy();
                      Release();
                  }
                  finally
                  {
                      handles = 0;
                  }
              }"
        )]
        public void ATeardownThatIsAlreadyCorrectIsNotReported(string shape, string body)
        {
            AssertNothingReported(shape, body);
        }

        /// <summary>
        /// Setup is the opposite rule, and this analyzer must stay silent about it.
        /// </summary>
        /// <remarks>
        /// <c>Awake</c> and <c>OnEnable</c> have to chain base-FIRST, so the shape these two
        /// fixtures write is not a defect -- it is the correct one. Folding the setup rule in here
        /// would turn every correct <c>Awake</c> in the package into a warning.
        /// </remarks>
        [TestCase(
            "Awake",
            @"protected override void Awake()
              {
                  base.Awake();
                  Release();
                  handles = 0;
              }"
        )]
        [TestCase(
            "OnEnable",
            @"protected override void OnEnable()
              {
                  base.OnEnable();
                  Release();
                  handles = 0;
              }"
        )]
        public void ASetupOverrideChainingBaseFirstIsNotReported(string hook, string body)
        {
            AssertNothingReported(hook + " chains base-first and that is correct", body);
        }

        /// <summary>
        /// The consumer contract: on by default, so a project that has taken on this package gets
        /// the safety without discovering it, but never able to fail their build.
        /// </summary>
        [Test]
        public void TheDiagnosticIsOnByDefaultSuppressibleAndNeverAboveAWarning()
        {
            DiagnosticDescriptor descriptor =
                new TeardownBaseCallAnalyzer().SupportedDiagnostics.Single(candidate =>
                    candidate.Id == DiagnosticId
                );

            Assert.IsTrue(
                descriptor.IsEnabledByDefault,
                "a consumer using this package should get the safety without asking for it"
            );
            Assert.AreEqual(
                DiagnosticSeverity.Warning,
                descriptor.DefaultSeverity,
                "a warning is the ceiling; an error would fail a build over statement order"
            );

            string offending = Override("OnDestroy", "base.OnDestroy();", 1);

            Assert.IsNotEmpty(
                Analyze(offending, ReportDiagnostic.Default),
                "a consumer who configures nothing must still be told"
            );
            Assert.IsEmpty(
                Analyze(offending, ReportDiagnostic.Suppress),
                "and one who does not want it must be able to turn it off"
            );
        }

        /// <summary>
        /// The stub above stands in for real shipped code, so it has to keep standing for it.
        /// </summary>
        /// <remarks>
        /// <c>RuntimeSingleton</c> is why this diagnostic exists: its <c>OnDestroy</c> is where the
        /// static instance registration is dropped, so anything a derived class runs after
        /// <c>base.OnDestroy()</c> runs against an object that no longer answers
        /// <c>Instance</c>. If that hook were renamed or stopped being virtual, every fixture here
        /// would keep passing against the stub while the analyzer covered nothing real.
        /// </remarks>
        [Test]
        public void TheStubbedHooksMatchTheOnesThePackageShips()
        {
            string repoRoot = FindRepositoryRoot();
            string shipped = Path.Combine(repoRoot, "Runtime", "Utils", "RuntimeSingleton.cs");
            Assert.IsTrue(File.Exists(shipped), $"expected the shipped singleton at {shipped}");

            string source = File.ReadAllText(shipped);
            foreach (string hook in new[] { "OnDestroy", "OnApplicationQuit" })
            {
                StringAssert.Contains(
                    $"protected virtual void {hook}()",
                    source,
                    $"the analyzer covers {hook} because RuntimeSingleton declares it"
                );
                Assert.IsNotEmpty(
                    Analyze(Override(hook, "base." + hook + "();", 1)),
                    $"{hook} is a shipped teardown hook, so it has to still be in the analyzer's list"
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

        /// <summary>
        /// An override of <paramref name="hook"/> whose body is <paramref name="first"/> followed by
        /// <paramref name="following"/> statements that run after it.
        /// </summary>
        private static string Override(string hook, string first, int following)
        {
            // Dispose comes from IDisposable, so it is the one hook whose override cannot be
            // protected.
            string accessibility = hook == "Dispose" ? "public" : "protected";
            string after = string.Join("\n", Enumerable.Repeat("Release();", following).ToArray());
            return accessibility
                + " override void "
                + hook
                + "()\n{\n"
                + first
                + "\n"
                + after
                + "\n}";
        }

        private static void AssertNothingReported(string shape, string body)
        {
            Assert.IsEmpty(
                Analyze(body).Select(diagnostic => diagnostic.ToString()).ToArray(),
                shape + " must not be reported"
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
            return Analyze(body, ReportDiagnostic.Default);
        }

        /// <summary>
        /// Compiles <paramref name="body"/> and runs the analyzer over it.
        /// </summary>
        /// <param name="body">Members of a class deriving from the stubbed <c>Package.Component</c>.</param>
        /// <param name="reportedAs">
        /// What the compilation says about the diagnostic -- <see cref="ReportDiagnostic.Default"/>
        /// for a consumer who configures nothing, or anything else for the ruleset /
        /// <c>.editorconfig</c> entry they would write, expressed as the option Roslyn resolves both
        /// of them to.
        /// </param>
        /// <returns>Everything the analyzer reported.</returns>
        private static ImmutableArray<Diagnostic> Analyze(string body, ReportDiagnostic reportedAs)
        {
            string source =
                "using System;\n"
                + "namespace Consumer { public class Subject : Package.Component { "
                + body
                + " } }\n"
                + PackageTeardownBase;

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
                    ImmutableArray.Create<DiagnosticAnalyzer>(new TeardownBaseCallAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }
    }
}
