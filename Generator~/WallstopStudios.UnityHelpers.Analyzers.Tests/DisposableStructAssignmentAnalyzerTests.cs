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
    /// Pins the half of disposable-struct correctness that <c>readonly</c> does not cover: a
    /// <c>Dispose</c> that assigns runs its assignment again on every copy.
    /// </summary>
    /// <remarks>
    /// The negatives carry as much weight as the positives. A struct whose <c>Dispose</c> mutates
    /// something it holds a REFERENCE to is the correct shape -- that object is shared by every copy
    /// and is where this state belongs -- so reporting it would flag every disposable this package
    /// ships and make the rule unusable (#627).
    /// </remarks>
    [TestFixture]
    public sealed class DisposableStructAssignmentAnalyzerTests
    {
        private const string DiagnosticId = "WUH014";

        /// <summary>
        /// A stand-in for whatever global a scope borrows, plus an owner a correct scope calls back
        /// into, so the fixtures need neither UnityEngine nor the package.
        /// </summary>
        private const string Surroundings =
            @"public static class Global
              {
                  public static int Active { get; set; }
                  public static int Depth;
              }

              public sealed class Owner
              {
                  public int value;
                  public int[] buffer = new int[4];

                  public void Release(long identifier) { }
              }";

        /// <summary>
        /// The shape from the issue: a <c>readonly</c> struct that restores a global from its own
        /// field, which every copy re-imposes.
        /// </summary>
        [Test]
        public void AReadonlyScopeRestoringAGlobalFromItsOwnFieldIsReported()
        {
            const string subject =
                @"public readonly struct ActiveScope : IDisposable
                  {
                      private readonly int _previous;

                      public ActiveScope(int next)
                      {
                          _previous = Global.Active;
                          Global.Active = next;
                      }

                      public void Dispose()
                      {
                          Global.Active = _previous;
                      }
                  }";

            Diagnostic reported = Single(subject);

            Assert.AreEqual(DiagnosticId, reported.Id);
            string message = reported.GetMessage();
            StringAssert.Contains("'ActiveScope'", message);
            StringAssert.Contains("the global 'Global.Active'", message);
            StringAssert.Contains(
                "RestorableGlobal<T>",
                message,
                "the message has to name the API that makes the correct shape the easy one"
            );
            Assert.AreEqual(
                "Global.Active",
                reported.Location.SourceTree.GetText().ToString(reported.Location.SourceSpan),
                "the diagnostic belongs on what is assigned, not on the method"
            );
        }

        /// <summary>
        /// Every other shape of assignment that re-runs on a copy.
        /// </summary>
        [TestCase(
            "a mutable struct writing its own disposal flag",
            @"public struct FlagScope : IDisposable
              {
                  private bool _disposed;

                  public void Dispose()
                  {
                      _disposed = true;
                  }
              }",
            "its own '_disposed'"
        )]
        [TestCase(
            "an assignment written through 'this'",
            @"public struct ThisScope : IDisposable
              {
                  private int _held;

                  public void Dispose()
                  {
                      this._held = 0;
                  }
              }",
            "its own '_held'"
        )]
        [TestCase(
            "a compound assignment to a static counter",
            @"public readonly struct DepthScope : IDisposable
              {
                  public void Dispose()
                  {
                      Global.Depth -= 1;
                  }
              }",
            "the global 'Global.Depth'"
        )]
        [TestCase(
            "a decrement of a static counter",
            @"public readonly struct SteppedScope : IDisposable
              {
                  public void Dispose()
                  {
                      Global.Depth--;
                  }
              }",
            "the global 'Global.Depth'"
        )]
        [TestCase(
            "an explicit IDisposable.Dispose implementation",
            @"public readonly struct ExplicitScope : IDisposable
              {
                  private readonly int _previous;

                  public ExplicitScope(int previous)
                  {
                      _previous = previous;
                  }

                  void IDisposable.Dispose()
                  {
                      Global.Active = _previous;
                  }
              }",
            "the global 'Global.Active'"
        )]
        [TestCase(
            "an expression-bodied Dispose",
            @"public readonly struct TerseScope : IDisposable
              {
                  private readonly int _previous;

                  public TerseScope(int previous)
                  {
                      _previous = previous;
                  }

                  public void Dispose() => Global.Active = _previous;
              }",
            "the global 'Global.Active'"
        )]
        [TestCase(
            "a struct's own static field, which every instance shares",
            @"public readonly struct SharedScope : IDisposable
              {
                  private static int _open;

                  public void Dispose()
                  {
                      _open = 0;
                  }
              }",
            "the global 'SharedScope._open'"
        )]
        public void AnAssignmentEveryCopyReRunsIsReported(
            string shape,
            string subject,
            string named
        )
        {
            Diagnostic reported = Single(subject);

            Assert.AreEqual(DiagnosticId, reported.Id, shape + " must be reported");
            StringAssert.Contains(named, reported.GetMessage(), shape);
        }

        /// <summary>
        /// Everything that assigns inside a <c>Dispose</c> and is not this defect.
        /// </summary>
        /// <remarks>
        /// The second case is the one the rule lives or dies on. Mutating an object the struct holds
        /// a reference to is how every correct disposable in this package works -- the reference is
        /// shared by every copy, so the state is agreed rather than duplicated -- and reporting it
        /// would bury the real findings.
        /// </remarks>
        [TestCase(
            "an assignment to a local declared inside Dispose",
            @"public readonly struct LocalScope : IDisposable
              {
                  private readonly Owner _owner;

                  public LocalScope(Owner owner)
                  {
                      _owner = owner;
                  }

                  public void Dispose()
                  {
                      int captured = Global.Active;
                      captured = 0;
                      _owner.Release(captured);
                  }
              }"
        )]
        [TestCase(
            "a write to a field of the object the struct holds, which every copy shares",
            @"public readonly struct OwnedScope : IDisposable
              {
                  private readonly Owner _owner;
                  private readonly int _previous;

                  public OwnedScope(Owner owner, int previous)
                  {
                      _owner = owner;
                      _previous = previous;
                  }

                  public void Dispose()
                  {
                      _owner.value = _previous;
                  }
              }"
        )]
        [TestCase(
            "a write into an array the struct holds, which every copy shares",
            @"public readonly struct BufferScope : IDisposable
              {
                  private readonly Owner _owner;

                  public BufferScope(Owner owner)
                  {
                      _owner = owner;
                  }

                  public void Dispose()
                  {
                      _owner.buffer[0] = 0;
                  }
              }"
        )]
        [TestCase(
            "the correct shape: giving the claim back is a call to the owner",
            @"public readonly struct ClaimScope : IDisposable
              {
                  private readonly Owner _owner;
                  private readonly long _identifier;

                  public ClaimScope(Owner owner, long identifier)
                  {
                      _owner = owner;
                      _identifier = identifier;
                  }

                  public void Dispose()
                  {
                      if (_owner == null)
                      {
                          return;
                      }

                      _owner.Release(_identifier);
                  }
              }"
        )]
        [TestCase(
            "a class, which has one instance rather than a copy per assignment",
            @"public sealed class ClassScope : IDisposable
              {
                  private readonly int _previous;
                  private bool _disposed;

                  public ClassScope(int previous)
                  {
                      _previous = previous;
                  }

                  public void Dispose()
                  {
                      if (_disposed)
                      {
                          return;
                      }

                      _disposed = true;
                      Global.Active = _previous;
                  }
              }"
        )]
        [TestCase(
            "a struct that does not implement IDisposable",
            @"public struct PlainStruct
              {
                  private int _held;

                  public void Dispose()
                  {
                      _held = 0;
                      Global.Active = 0;
                  }
              }"
        )]
        [TestCase(
            "the BCL's Dispose(bool disposing) arity, which is a different protocol",
            @"public struct PatternStruct : IDisposable
              {
                  private int _held;

                  public void Dispose()
                  {
                      Dispose(true);
                  }

                  private void Dispose(bool disposing)
                  {
                      _held = 0;
                      Global.Active = 0;
                  }
              }"
        )]
        [TestCase(
            "a method other than Dispose",
            @"public struct BuilderStruct : IDisposable
              {
                  private int _held;

                  public void Reset()
                  {
                      _held = 0;
                      Global.Active = 0;
                  }

                  public void Dispose()
                  {
                  }
              }"
        )]
        [TestCase(
            "a lambda declared in Dispose, whose body runs somewhere else",
            @"public struct DeferredScope : IDisposable
              {
                  private readonly Owner _owner;

                  public DeferredScope(Owner owner)
                  {
                      _owner = owner;
                      _held = 0;
                  }

                  private int _held;

                  public void Dispose()
                  {
                      Action later = () => Global.Active = 0;
                      Queue(later);
                  }

                  private static void Queue(Action action) { }
              }"
        )]
        [TestCase(
            "a local function declared in Dispose",
            @"public struct LocalFunctionScope : IDisposable
              {
                  public void Dispose()
                  {
                      Register(Reset);

                      void Reset()
                      {
                          Global.Active = 0;
                      }
                  }

                  private static void Register(Action action) { }
              }"
        )]
        public void AnAssignmentThatIsNotThisDefectIsNotReported(string shape, string subject)
        {
            Assert.IsEmpty(
                Analyze(subject).Select(diagnostic => diagnostic.ToString()).ToArray(),
                shape + " must not be reported"
            );
        }

        /// <summary>
        /// The consumer contract: on by default, suppressible, and never able to fail a build.
        /// </summary>
        /// <remarks>
        /// On by default because the criterion for the two opt-in members of this family -- the rule
        /// is right AND the shape is everywhere -- does not hold. Measured 2026-09-02 over
        /// <c>Runtime/</c>, <c>Editor/</c> and <c>Tests/</c>: five findings, in three types, all of
        /// them in <c>Runtime/</c>.
        /// </remarks>
        [Test]
        public void TheDiagnosticIsOnByDefaultSuppressibleAndNeverAboveAWarning()
        {
            DiagnosticDescriptor descriptor =
                new DisposableStructAssignmentAnalyzer().SupportedDiagnostics.Single(candidate =>
                    candidate.Id == DiagnosticId
                );

            Assert.IsTrue(
                descriptor.IsEnabledByDefault,
                "the shape is rare enough that a consumer should be told without asking"
            );
            Assert.AreEqual(
                DiagnosticSeverity.Warning,
                descriptor.DefaultSeverity,
                "a warning is the ceiling; an error would fail a consumer's build on upgrade"
            );

            const string offending =
                @"public readonly struct SuppressibleScope : IDisposable
                  {
                      private readonly int _previous;

                      public SuppressibleScope(int previous)
                      {
                          _previous = previous;
                      }

                      public void Dispose()
                      {
                          Global.Active = _previous;
                      }
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

        /// <summary>
        /// The message points at a shipped type, so that type has to still be shipped and still be
        /// the shape the message describes.
        /// </summary>
        /// <remarks>
        /// A message naming an API that no longer exists sends the reader looking for something that
        /// is not there, and this analyzer's whole value is that it has an answer to point at.
        /// </remarks>
        [Test]
        public void TheApiTheMessageRecommendsIsShippedAndDoesNotItselfAssign()
        {
            string repositoryRoot = FindRepositoryRoot();
            string shipped = Path.Combine(
                repositoryRoot,
                "Runtime",
                "Core",
                "Helper",
                "RestorableGlobal.cs"
            );
            Assert.IsTrue(File.Exists(shipped), $"expected the shipped owner at {shipped}");

            string source = File.ReadAllText(shipped);
            StringAssert.Contains("public sealed class RestorableGlobal<T>", source);
            StringAssert.Contains("public readonly struct Scope : IDisposable", source);
            StringAssert.Contains(
                "_owner.Release(_slot, _identifier);",
                source,
                "the scope has to hand its claim back with a call rather than an assignment"
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

        private static Diagnostic Single(string subject)
        {
            ImmutableArray<Diagnostic> reported = Analyze(subject);
            Assert.AreEqual(1, reported.Length, "Expected exactly one diagnostic");
            return reported[0];
        }

        private static ImmutableArray<Diagnostic> Analyze(string subject)
        {
            return Analyze(subject, ReportDiagnostic.Default);
        }

        /// <summary>
        /// Compiles <paramref name="subject"/> beside the stubbed globals and runs the analyzer.
        /// </summary>
        /// <param name="subject">One or more type declarations.</param>
        /// <param name="reportedAs">
        /// What the compilation says about the diagnostic -- <see cref="ReportDiagnostic.Default"/>
        /// for a consumer who configures nothing, or the option a ruleset or <c>.editorconfig</c>
        /// entry resolves to.
        /// </param>
        /// <returns>Everything the analyzer reported.</returns>
        private static ImmutableArray<Diagnostic> Analyze(
            string subject,
            ReportDiagnostic reportedAs
        )
        {
            string source =
                "using System;\nnamespace Consumer\n{\n" + subject + "\n" + Surroundings + "\n}\n";

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

            // A fixture that does not compile reports nothing and reads as a pass, which is the one
            // way this suite could go quietly green while the analyzer did nothing at all.
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
                        new DisposableStructAssignmentAnalyzer()
                    )
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }
    }
}
