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

    /// <summary>
    /// Pins the read of a <c>TryXxx</c> <c>out</c> value on a path where nobody looked at the
    /// call's <c>bool</c>.
    /// </summary>
    /// <remarks>
    /// The negative cases carry the weight. <c>if (TryX(out v))</c> and
    /// <c>if (!TryX(out v)) { return; }</c> are the overwhelming majority of every <c>TryXxx</c>
    /// call site in a tree, and a rule that reported them would be turned off the day it shipped;
    /// so would one that matched the discard rather than the read, because
    /// <c>_ = set.TryAdd(x, out Thing unused);</c> is a legitimate fire-and-forget (#629).
    /// </remarks>
    [TestFixture]
    public sealed class UntestedTryOutAnalyzerTests
    {
        private const string DiagnosticId = "WUH008";

        /// <summary>
        /// A consumer's own surface, so the fixtures prove the rule is about the <c>TryXxx</c>
        /// contract rather than about the BCL.
        /// </summary>
        private const string SupportTypes =
            @"namespace Support
              {
                  public sealed class Thing
                  {
                      public int Value;
                      public void Use() { }
                  }

                  public struct Point
                  {
                      public int X;
                      public int Y;
                  }

                  public sealed class Source
                  {
                      public bool TryNext(out Thing thing) { thing = null; return false; }
                      public bool TryGetPoint(int key, out Point point) { point = default; return false; }
                      public bool TryGetPair(int key, out Thing first, out Thing second)
                      {
                          first = null;
                          second = null;
                          return false;
                      }
                      public int TryCount(out Thing thing) { thing = null; return 0; }
                      public bool TryPing() { return true; }
                  }
              }";

        [Test]
        public void AReadOfAnUntestedTryOutIsReportedAtTheRead()
        {
            Diagnostic reported = Single(
                @"public static void Run(Dictionary<string, Thing> map, string key)
                  {
                      _ = map.TryGetValue(key, out Thing thing);
                      thing.Use();
                  }"
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
            string message = reported.GetMessage();
            StringAssert.Contains("TryGetValue", message);
            StringAssert.Contains("thing", message);

            // The read is the actionable location; the discard is only how the value got there.
            Assert.AreEqual(
                "thing",
                reported.Location.SourceTree.GetText().ToString(reported.Location.SourceSpan),
                "the diagnostic must land on the read, not on the call"
            );
        }

        /// <summary>
        /// Every shape where the <c>bool</c> was dropped and the <c>out</c> was then read anyway.
        /// </summary>
        [TestCase(
            "an inline out declaration",
            @"public static void Run(Dictionary<string, Thing> map, string key)
              {
                  _ = map.TryGetValue(key, out Thing thing);
                  thing.Use();
              }"
        )]
        [TestCase(
            "a pre-declared local",
            @"public static void Run(Dictionary<string, Thing> map, string key)
              {
                  Thing thing = null;
                  _ = map.TryGetValue(key, out thing);
                  thing.Use();
              }"
        )]
        [TestCase(
            "a statement-form call, with no discard at all",
            @"public static int Run(Dictionary<string, int> map, string key)
              {
                  map.TryGetValue(key, out int count);
                  return count;
              }"
        )]
        [TestCase(
            "a consumer's own TryXxx with a struct out",
            @"public static int Run(Source source, int key)
              {
                  _ = source.TryGetPoint(key, out Point point);
                  return point.X;
              }"
        )]
        [TestCase(
            "two out parameters where only the second is read",
            @"public static void Run(Source source, int key)
              {
                  _ = source.TryGetPair(key, out Thing first, out Thing second);
                  second.Use();
              }"
        )]
        [TestCase(
            "a read inside a nested block after the call",
            @"public static void Run(Dictionary<string, Thing> map, string key, bool flag)
              {
                  _ = map.TryGetValue(key, out Thing thing);
                  if (flag)
                  {
                      thing.Use();
                  }
              }"
        )]
        public void AnUntestedTryOutThatIsReadIsReported(string shape, string body)
        {
            ImmutableArray<Diagnostic> reported = Analyze(body);
            Assert.AreEqual(1, reported.Length, shape + " must report exactly once");
            Assert.AreEqual(DiagnosticId, reported[0].Id, shape + " must report only WUH008");
        }

        /// <summary>
        /// Everything the issue puts out of scope, plus the two tested shapes that make up nearly
        /// every <c>TryXxx</c> call site in a real tree.
        /// </summary>
        [TestCase(
            "an out discard, which has nothing to read",
            @"public static void Run(Dictionary<string, Thing> map, string key)
              {
                  _ = map.TryGetValue(key, out _);
              }"
        )]
        [TestCase(
            "a fire-and-forget whose out is never read",
            @"public static void Run(Dictionary<string, Thing> map, string key)
              {
                  _ = map.TryGetValue(key, out Thing unused);
              }"
        )]
        [TestCase(
            "the call tested by an if",
            @"public static void Run(Dictionary<string, Thing> map, string key)
              {
                  if (map.TryGetValue(key, out Thing thing))
                  {
                      thing.Use();
                  }
              }"
        )]
        [TestCase(
            "the call tested by a negated early-return if",
            @"public static void Run(Dictionary<string, Thing> map, string key)
              {
                  if (!map.TryGetValue(key, out Thing thing))
                  {
                      return;
                  }

                  thing.Use();
              }"
        )]
        [TestCase(
            "the call tested by a while",
            @"public static void Run(Source source)
              {
                  while (source.TryNext(out Thing thing))
                  {
                      thing.Use();
                  }
              }"
        )]
        [TestCase(
            "the result held in a local and tested through it",
            @"public static void Run(Dictionary<string, Thing> map, string key)
              {
                  bool ok = map.TryGetValue(key, out Thing thing);
                  if (ok)
                  {
                      thing.Use();
                  }
              }"
        )]
        [TestCase(
            "a Try method that does not return bool",
            @"public static void Run(Source source)
              {
                  source.TryCount(out Thing thing);
                  thing.Use();
              }"
        )]
        [TestCase(
            "a bool Try method with no out at all",
            @"public static void Run(Source source, Thing thing)
              {
                  _ = source.TryPing();
                  thing.Use();
              }"
        )]
        [TestCase(
            "a read of a different variable",
            @"public static void Run(Dictionary<string, Thing> map, string key, Thing other)
              {
                  _ = map.TryGetValue(key, out Thing thing);
                  other.Use();
              }"
        )]
        [TestCase(
            "a read after an ordinary reassignment",
            @"public static void Run(Dictionary<string, Thing> map, string key)
              {
                  _ = map.TryGetValue(key, out Thing thing);
                  thing = new Thing();
                  thing.Use();
              }"
        )]
        [TestCase(
            "a read after a tested rebinding of the same variable",
            @"public static void Run(Dictionary<string, Thing> map, string key)
              {
                  _ = map.TryGetValue(key, out Thing thing);
                  if (!map.TryGetValue(key, out thing))
                  {
                      return;
                  }

                  thing.Use();
              }"
        )]
        public void AnOutValueThatWasTestedOrNeverReadIsNotReported(string shape, string body)
        {
            Assert.IsEmpty(Analyze(body), shape + " must not be reported");
        }

        /// <summary>
        /// One mistake is one warning. Reporting per use would put three squiggles on a single
        /// missing guard and train the reader to skip them.
        /// </summary>
        [Test]
        public void AValueReadThreeTimesAfterOneUntestedCallIsReportedOnce()
        {
            Diagnostic reported = Single(
                @"public static int Run(Dictionary<string, Thing> map, string key)
                  {
                      _ = map.TryGetValue(key, out Thing thing);
                      thing.Use();
                      thing.Use();
                      return thing.Value;
                  }"
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
        }

        /// <summary>
        /// The consumer contract: on by default, so a project that has taken on this package gets
        /// the safety without discovering it, but never able to fail their build.
        /// </summary>
        [Test]
        public void TheDiagnosticIsOnByDefaultSuppressibleAndNeverAboveAWarning()
        {
            DiagnosticDescriptor descriptor =
                new UntestedTryOutAnalyzer().SupportedDiagnostics.Single(candidate =>
                    candidate.Id == DiagnosticId
                );

            Assert.IsTrue(
                descriptor.IsEnabledByDefault,
                "a consumer using this package should get the safety without asking for it"
            );
            Assert.AreEqual(
                DiagnosticSeverity.Warning,
                descriptor.DefaultSeverity,
                "a warning is the ceiling; an error would fail a build over code that compiles"
            );

            const string offending =
                @"public static void Run(Dictionary<string, Thing> map, string key)
                  {
                      _ = map.TryGetValue(key, out Thing thing);
                      thing.Use();
                  }";

            Assert.IsNotEmpty(
                Analyze(offending, ReportDiagnostic.Default),
                "a consumer who configures nothing must still be told"
            );
            Assert.IsEmpty(
                Analyze(offending, ReportDiagnostic.Suppress),
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
            return Analyze(body, ReportDiagnostic.Default);
        }

        /// <summary>
        /// Compiles <paramref name="body"/> and runs the analyzer over it.
        /// </summary>
        /// <param name="body">Members of a static class in namespace <c>Consumer</c>.</param>
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
                + "using System.Collections.Generic;\n"
                + "using Support;\n"
                + "namespace Consumer { public static class Subject { "
                + body
                + " } }\n"
                + SupportTypes;

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
                    ImmutableArray.Create<DiagnosticAnalyzer>(new UntestedTryOutAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }
    }
}
