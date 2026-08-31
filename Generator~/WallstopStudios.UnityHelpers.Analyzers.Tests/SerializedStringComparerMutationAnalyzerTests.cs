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
    public sealed class SerializedStringComparerMutationAnalyzerTests
    {
        private const string DiagnosticId = "WUH011";

        private const string ComparerStub =
            @"namespace WallstopStudios.UnityHelpers.Utils
              {
                  using System.Collections.Generic;
                  public sealed class SerializedStringComparer : IEqualityComparer<string>
                  {
                      public StringCompareMode compareMode;
                      public SerializedStringComparer Freeze() => this;
                      public bool Equals(string x, string y) => x == y;
                      public int GetHashCode(string value) => 0;
                      public enum StringCompareMode { Ordinal = 0, OrdinalIgnoreCase = 1 }
                  }
              }";

        [TestCase(
            "a dictionary",
            @"SerializedStringComparer comparer = new SerializedStringComparer();
              Dictionary<string, int> values = new Dictionary<string, int>(comparer);
              comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
        )]
        [TestCase(
            "a hash set",
            @"SerializedStringComparer comparer = new SerializedStringComparer();
              HashSet<string> values = new HashSet<string>(comparer);
              comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
        )]
        [TestCase(
            "a capacity overload",
            @"SerializedStringComparer comparer = new SerializedStringComparer();
              Dictionary<string, int> values = new Dictionary<string, int>(8, comparer);
              comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
        )]
        [TestCase(
            "a concurrent dictionary",
            @"SerializedStringComparer comparer = new SerializedStringComparer();
              System.Collections.Concurrent.ConcurrentDictionary<string, int> values =
                  new System.Collections.Concurrent.ConcurrentDictionary<string, int>(comparer);
              comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
        )]
        public void AModeWriteAfterCollectionConstructionIsReported(string shape, string body)
        {
            ImmutableArray<Diagnostic> reported = Analyze(body);

            Assert.AreEqual(1, reported.Length, shape + " must be reported once");
            Assert.AreEqual(DiagnosticId, reported[0].Id);
            Assert.AreEqual(
                "comparer.compareMode",
                reported[0].Location.SourceTree.GetText().ToString(reported[0].Location.SourceSpan)
            );
        }

        [Test]
        public void AParameterPassedToACollectionAndThenChangedIsReported()
        {
            Diagnostic reported = Single(
                @"private static void Change(SerializedStringComparer comparer)
                  {
                      Dictionary<string, int> values = new Dictionary<string, int>(comparer);
                      comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;
                  }",
                membersAreComplete: true
            );

            StringAssert.Contains("comparer", reported.GetMessage());
            StringAssert.Contains("Freeze()", reported.GetMessage());
        }

        [Test]
        public void ADirectlyReferencedFieldIsTracked()
        {
            Diagnostic reported = Single(
                @"private static readonly SerializedStringComparer Comparer = new SerializedStringComparer();
                  private static void Change()
                  {
                      Dictionary<string, int> values = new Dictionary<string, int>(Comparer);
                      Comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;
                  }",
                membersAreComplete: true
            );

            StringAssert.Contains("Comparer", reported.GetMessage());
        }

        [TestCase(
            "a write before construction",
            @"SerializedStringComparer comparer = new SerializedStringComparer();
              comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;
              Dictionary<string, int> values = new Dictionary<string, int>(comparer);"
        )]
        [TestCase(
            "a freeze before construction",
            @"SerializedStringComparer comparer = new SerializedStringComparer();
              comparer.Freeze();
              Dictionary<string, int> values = new Dictionary<string, int>(comparer);
              comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
        )]
        [TestCase(
            "a freeze after construction",
            @"SerializedStringComparer comparer = new SerializedStringComparer();
              Dictionary<string, int> values = new Dictionary<string, int>(comparer);
              comparer.Freeze();
              comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
        )]
        [TestCase(
            "a frozen construction expression",
            @"SerializedStringComparer comparer = new SerializedStringComparer();
              Dictionary<string, int> values = new Dictionary<string, int>(comparer.Freeze());
              comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
        )]
        [TestCase(
            "a comparer never handed to a collection",
            @"SerializedStringComparer comparer = new SerializedStringComparer();
              comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
        )]
        [TestCase(
            "a different comparer instance",
            @"SerializedStringComparer used = new SerializedStringComparer();
              SerializedStringComparer changed = new SerializedStringComparer();
              Dictionary<string, int> values = new Dictionary<string, int>(used);
              changed.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
        )]
        public void AModeWriteThatCannotChangeAnExistingCollectionIsNotReported(
            string shape,
            string body
        )
        {
            Assert.IsEmpty(Analyze(body), shape + " must stay quiet");
        }

        [Test]
        public void ACollectionUseInAnotherMethodIsOutsideTheDeclaredFlowScope()
        {
            Assert.IsEmpty(
                Analyze(
                    @"private static void Build(SerializedStringComparer comparer)
                      {
                          Dictionary<string, int> values = new Dictionary<string, int>(comparer);
                      }
                      private static void Change(SerializedStringComparer comparer)
                      {
                          comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;
                      }",
                    ReportDiagnostic.Default,
                    membersAreComplete: true
                )
            );
        }

        [Test]
        public void RebindingTheLocalToANewComparerResetsItsFlowState()
        {
            Assert.IsEmpty(
                Analyze(
                    @"SerializedStringComparer comparer = new SerializedStringComparer();
                      Dictionary<string, int> first = new Dictionary<string, int>(comparer);
                      comparer = new SerializedStringComparer();
                      comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
                )
            );
        }

        [Test]
        public void ReboundComparerIsReportedAfterItIsUsed()
        {
            Diagnostic reported = Single(
                @"SerializedStringComparer comparer = new SerializedStringComparer();
                  Dictionary<string, int> first = new Dictionary<string, int>(comparer);
                  comparer = new SerializedStringComparer();
                  HashSet<string> second = new HashSet<string>(comparer);
                  comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
        }

        [TestCase(
            "compound assignment",
            "comparer.compareMode |= SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
        )]
        [TestCase("increment", "comparer.compareMode++;")]
        public void OtherModeWritesAfterCollectionConstructionAreReported(
            string shape,
            string write
        )
        {
            ImmutableArray<Diagnostic> reported = Analyze(
                @"SerializedStringComparer comparer = new SerializedStringComparer();
                  Dictionary<string, int> values = new Dictionary<string, int>(comparer);" + write
            );

            Assert.AreEqual(1, reported.Length, shape);
            Assert.AreEqual(DiagnosticId, reported[0].Id);
        }

        [Test]
        public void FreezingThroughTheAssignmentReceiverSilencesTheWrite()
        {
            Assert.IsEmpty(
                Analyze(
                    @"SerializedStringComparer comparer = new SerializedStringComparer();
                      Dictionary<string, int> values = new Dictionary<string, int>(comparer);
                      comparer.Freeze().compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
                )
            );
        }

        [Test]
        public void CollectionUseInAMutuallyExclusiveBranchDoesNotWarnAfterTheBranch()
        {
            Assert.IsEmpty(
                Analyze(
                    @"SerializedStringComparer comparer = new SerializedStringComparer();
                      if (System.DateTime.UtcNow.Ticks == 0)
                      {
                          Dictionary<string, int> values = new Dictionary<string, int>(comparer);
                      }
                      else
                      {
                          comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;
                      }"
                )
            );
        }

        [Test]
        public void ConditionalFreezeDoesNotSilenceALaterWrite()
        {
            Diagnostic reported = Single(
                @"SerializedStringComparer comparer = new SerializedStringComparer();
                  Dictionary<string, int> values = new Dictionary<string, int>(comparer);
                  if (System.DateTime.UtcNow.Ticks == 0)
                  {
                      comparer.Freeze();
                  }
                  comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
        }

        [Test]
        public void BracelessMutuallyExclusiveBranchesDoNotShareState()
        {
            Assert.IsEmpty(
                Analyze(
                    @"SerializedStringComparer comparer = new SerializedStringComparer();
                      if (System.DateTime.UtcNow.Ticks == 0)
                          new Dictionary<string, int>(comparer).Clear();
                      else
                          comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
                )
            );
        }

        [Test]
        public void BracelessConditionalFreezeDoesNotSilenceALaterWrite()
        {
            Diagnostic reported = Single(
                @"SerializedStringComparer comparer = new SerializedStringComparer();
                  Dictionary<string, int> values = new Dictionary<string, int>(comparer);
                  if (System.DateTime.UtcNow.Ticks == 0)
                      comparer.Freeze();
                  comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
        }

        [Test]
        public void BracelessConditionalRebindDoesNotSilenceALaterWrite()
        {
            Diagnostic reported = Single(
                @"SerializedStringComparer comparer = new SerializedStringComparer();
                  Dictionary<string, int> values = new Dictionary<string, int>(comparer);
                  if (System.DateTime.UtcNow.Ticks == 0)
                      comparer = new SerializedStringComparer();
                  comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
        }

        [TestCase(
            "logical and",
            "bool frozen = System.DateTime.UtcNow.Ticks == 0 && comparer.Freeze() != null;"
        )]
        [TestCase(
            "logical or",
            "bool frozen = System.DateTime.UtcNow.Ticks != 0 || comparer.Freeze() != null;"
        )]
        [TestCase(
            "null coalescing",
            @"SerializedStringComparer maybe = System.DateTime.UtcNow.Ticks == 0 ? comparer : null;
              SerializedStringComparer frozen = maybe ?? comparer.Freeze();"
        )]
        [TestCase(
            "switch expression arm",
            @"SerializedStringComparer frozen = (System.DateTime.UtcNow.Ticks == 0) switch
              {
                  true => comparer.Freeze(),
                  false => comparer,
              };"
        )]
        [TestCase(
            "coalescing assignment",
            @"SerializedStringComparer holder = new SerializedStringComparer();
              holder ??= comparer.Freeze();"
        )]
        [TestCase(
            "deconstructing foreach body",
            @"foreach ((int left, int right) in new (int, int)[0])
                  comparer.Freeze();"
        )]
        [TestCase(
            "catch filter",
            @"try
              {
                  throw new System.InvalidOperationException();
              }
              catch (System.InvalidOperationException) when (comparer.Freeze() != null)
              {
              }"
        )]
        public void ConditionallyEvaluatedFreezeDoesNotSilenceALaterWrite(
            string shape,
            string conditionalFreeze
        )
        {
            ImmutableArray<Diagnostic> reported = Analyze(
                @"SerializedStringComparer comparer = new SerializedStringComparer();
                  Dictionary<string, int> values = new Dictionary<string, int>(comparer);"
                    + conditionalFreeze
                    + @"comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
            );

            Assert.AreEqual(1, reported.Length, shape);
            Assert.AreEqual(DiagnosticId, reported[0].Id);
        }

        [Test]
        public void ConditionalAccessFreezeOnTheUsedComparerSilencesAReachableWrite()
        {
            Assert.IsEmpty(
                Analyze(
                    @"SerializedStringComparer comparer = new SerializedStringComparer();
                      Dictionary<string, int> values = new Dictionary<string, int>(comparer);
                      comparer?.Freeze();
                      comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
                )
            );
        }

        [Test]
        public void OutArgumentRebindResetsTheTrackedComparer()
        {
            Assert.IsEmpty(
                Analyze(
                    @"private static void Reset(out SerializedStringComparer comparer)
                      {
                          comparer = new SerializedStringComparer();
                      }
                      private static void Change()
                      {
                          SerializedStringComparer comparer = new SerializedStringComparer();
                          Dictionary<string, int> values = new Dictionary<string, int>(comparer);
                          Reset(out comparer);
                          comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;
                      }",
                    ReportDiagnostic.Default,
                    membersAreComplete: true
                )
            );
        }

        [Test]
        public void OutArgumentWritingTheModeIsReported()
        {
            Diagnostic reported = Single(
                @"SerializedStringComparer comparer = new SerializedStringComparer();
                  Dictionary<string, int> values = new Dictionary<string, int>(comparer);
                  System.Enum.TryParse<SerializedStringComparer.StringCompareMode>(
                      ""OrdinalIgnoreCase"", out comparer.compareMode);"
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
        }

        [TestCase("Change(out comparer.compareMode, new Dictionary<string, int>(comparer));")]
        [TestCase("Change(new Dictionary<string, int>(comparer), out comparer.compareMode);")]
        public void RefOutMutationExecutesAfterEveryArgumentIsEvaluated(string call)
        {
            string parameters = call.StartsWith("Change(out", StringComparison.Ordinal)
                ? @"out SerializedStringComparer.StringCompareMode mode,
                    Dictionary<string, int> values"
                : @"Dictionary<string, int> values,
                    out SerializedStringComparer.StringCompareMode mode";
            ImmutableArray<Diagnostic> reported = Analyze(
                @"private static void Change("
                    + parameters
                    + @")
                  {
                      mode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;
                  }
                  private static void Run()
                  {
                      SerializedStringComparer comparer = new SerializedStringComparer();"
                    + call
                    + @"}",
                ReportDiagnostic.Default,
                membersAreComplete: true
            );

            Assert.AreEqual(1, reported.Length, call);
            Assert.AreEqual(DiagnosticId, reported[0].Id);
        }

        [TestCase("Mutate(ref comparer, ref comparer.compareMode);")]
        [TestCase("Mutate(ref comparer.compareMode, ref comparer);")]
        public void RefModeTargetIsCapturedBeforeTheSameCallRebindsTheComparer(string call)
        {
            string parameters = call.StartsWith("Mutate(ref comparer,", StringComparison.Ordinal)
                ? @"ref SerializedStringComparer comparer,
                    ref SerializedStringComparer.StringCompareMode mode"
                : @"ref SerializedStringComparer.StringCompareMode mode,
                    ref SerializedStringComparer comparer";
            ImmutableArray<Diagnostic> reported = Analyze(
                @"private static void Mutate("
                    + parameters
                    + @")
                  {
                      mode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;
                      comparer = new SerializedStringComparer();
                  }
                  private static void Run()
                  {
                      SerializedStringComparer comparer = new SerializedStringComparer();
                      Dictionary<string, int> values = new Dictionary<string, int>(comparer);"
                    + call
                    + @"}",
                ReportDiagnostic.Default,
                membersAreComplete: true
            );

            Assert.AreEqual(1, reported.Length, call);
            Assert.AreEqual(DiagnosticId, reported[0].Id);
        }

        [Test]
        public void CollectionIsUsedBeforeItsInitializerMutatesTheComparer()
        {
            Diagnostic reported = Single(
                @"private static string Change(
                          out SerializedStringComparer.StringCompareMode mode)
                  {
                      mode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;
                      return ""changed"";
                  }
                  private static void Run()
                  {
                      SerializedStringComparer comparer = new SerializedStringComparer();
                      Dictionary<string, int> values = new Dictionary<string, int>(comparer)
                      {
                          [Change(out comparer.compareMode)] = 1,
                      };
                  }",
                membersAreComplete: true
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
        }

        [Test]
        public void InitializerRebindAppliesAfterTheCollectionCapturedTheOldComparer()
        {
            Assert.IsEmpty(
                Analyze(
                    @"private static string Rebind(ref SerializedStringComparer comparer)
                      {
                          comparer = new SerializedStringComparer();
                          return ""changed"";
                      }
                      private static void Run()
                      {
                          SerializedStringComparer comparer = new SerializedStringComparer();
                          Dictionary<string, int> values = new Dictionary<string, int>(comparer)
                          {
                              [Rebind(ref comparer)] = 1,
                          };
                          comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;
                      }",
                    ReportDiagnostic.Default,
                    membersAreComplete: true
                )
            );
        }

        [Test]
        public void DeconstructionRebindResetsTheTrackedComparer()
        {
            Assert.IsEmpty(
                Analyze(
                    @"SerializedStringComparer comparer = new SerializedStringComparer();
                      Dictionary<string, int> values = new Dictionary<string, int>(comparer);
                      int ignored = 0;
                      (comparer, ignored) = (new SerializedStringComparer(), 1);
                      comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
                )
            );
        }

        [Test]
        public void DeconstructionWritingTheModeIsReported()
        {
            Diagnostic reported = Single(
                @"SerializedStringComparer comparer = new SerializedStringComparer();
                  Dictionary<string, int> values = new Dictionary<string, int>(comparer);
                  int ignored = 0;
                  (comparer.compareMode, ignored) =
                      (SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase, 1);"
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
        }

        [Test]
        public void DeconstructionSelfAssignmentDoesNotResetTheTrackedComparer()
        {
            Diagnostic reported = Single(
                @"SerializedStringComparer comparer = new SerializedStringComparer();
                  Dictionary<string, int> values = new Dictionary<string, int>(comparer);
                  int ignored = 0;
                  (comparer, ignored) = (comparer, 1);
                  comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;"
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
        }

        [Test]
        public void DeconstructionWritesTargetsFromLeftToRightBeforeRebinding()
        {
            Diagnostic reported = Single(
                @"SerializedStringComparer comparer = new SerializedStringComparer();
                  Dictionary<string, int> values = new Dictionary<string, int>(comparer);
                  (comparer.compareMode, comparer) =
                      (SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase,
                       new SerializedStringComparer());"
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
        }

        [Test]
        public void DeconstructionRebindBeforeModeWriteTargetsTheNewComparer()
        {
            Assert.IsEmpty(
                Analyze(
                    @"SerializedStringComparer comparer = new SerializedStringComparer();
                      Dictionary<string, int> values = new Dictionary<string, int>(comparer);
                      (comparer, comparer.compareMode) =
                          (new SerializedStringComparer(),
                           SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase);"
                )
            );
        }

        [TestCase("comparer.compareMode = comparer.compareMode;")]
        [TestCase(
            @"int ignored = 0;
              (comparer.compareMode, ignored) = (comparer.compareMode, 1);"
        )]
        public void ModeSelfAssignmentIsNotReported(string assignment)
        {
            Assert.IsEmpty(
                Analyze(
                    @"SerializedStringComparer comparer = new SerializedStringComparer();
                      Dictionary<string, int> values = new Dictionary<string, int>(comparer);"
                        + assignment
                )
            );
        }

        [Test]
        public void AConstructorThatConsumesButDoesNotRetainTheComparerIsNotACollectionUse()
        {
            Assert.IsEmpty(
                Analyze(
                    @"SerializedStringComparer comparer = new SerializedStringComparer();
                      ComparerConsumer consumer = new ComparerConsumer(comparer);
                      comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;",
                    ReportDiagnostic.Default,
                    membersAreComplete: false,
                    additionalType: @"public sealed class ComparerConsumer
                          {
                              public ComparerConsumer(System.Collections.Generic.IEqualityComparer<string> comparer) { }
                          }"
                )
            );
        }

        [Test]
        public void TheDiagnosticIsOnByDefaultSuppressibleAndNeverAboveAWarning()
        {
            DiagnosticDescriptor descriptor =
                new SerializedStringComparerMutationAnalyzer().SupportedDiagnostics.Single(
                    candidate => candidate.Id == DiagnosticId
                );

            Assert.IsTrue(descriptor.IsEnabledByDefault);
            Assert.AreEqual(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);

            const string offending =
                @"SerializedStringComparer comparer = new SerializedStringComparer();
                  Dictionary<string, int> values = new Dictionary<string, int>(comparer);
                  comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;";

            Assert.IsNotEmpty(Analyze(offending, ReportDiagnostic.Default));
            Assert.IsEmpty(Analyze(offending, ReportDiagnostic.Suppress));
        }

        private static Diagnostic Single(string body, bool membersAreComplete = false)
        {
            ImmutableArray<Diagnostic> reported = Analyze(
                body,
                ReportDiagnostic.Default,
                membersAreComplete
            );
            Assert.AreEqual(1, reported.Length, "Expected exactly one diagnostic");
            return reported[0];
        }

        private static ImmutableArray<Diagnostic> Analyze(string body)
        {
            return Analyze(body, ReportDiagnostic.Default);
        }

        private static ImmutableArray<Diagnostic> Analyze(
            string body,
            ReportDiagnostic reportedAs,
            bool membersAreComplete = false,
            string additionalType = ""
        )
        {
            string members = membersAreComplete
                ? body
                : "public static void Run() { " + body + " }";
            string source =
                "using System.Collections.Generic;\n"
                + "using WallstopStudios.UnityHelpers.Utils;\n"
                + "namespace Consumer { public static class Subject { "
                + members
                + " } "
                + additionalType
                + " }\n"
                + ComparerStub;

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

            foreach (
                Type anchor in new[]
                {
                    typeof(object),
                    typeof(Dictionary<string, int>),
                    typeof(HashSet<string>),
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
                    ImmutableArray.Create<DiagnosticAnalyzer>(
                        new SerializedStringComparerMutationAnalyzer()
                    )
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }
    }
}
