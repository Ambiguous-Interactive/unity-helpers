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
    /// Pins both halves of the destroyed-object footgun: the null-propagating operators, which walk
    /// straight past a destroyed <c>UnityEngine.Object</c>, and the null assertion, which reports
    /// success about one.
    /// </summary>
    /// <remarks>
    /// The negative cases are the reason this is an analyzer at all. <c>Vector2? p; p?.x</c> is
    /// what <c>?.</c> exists for, and a suite that only proved the positives would not distinguish
    /// this from a regex over the operator -- which is the implementation the issue rejected (#621).
    /// </remarks>
    [TestFixture]
    public sealed class UnityObjectNullAnalyzerTests
    {
        private const string NullPropagationId = "WUH003";
        private const string NullAssertionId = "WUH004";

        /// <summary>
        /// The assertion type the analyzer matches, declared here so the fixtures are hermetic. It
        /// is reached through its full name, never through a <c>using</c>.
        /// </summary>
        /// <remarks>
        /// Every one of these takes <c>object</c>, which is exactly why NUnit is in scope: the
        /// comparison it performs is a CLR-null one, with no <c>UnityEngine.Object</c> overload to
        /// route a destroyed object through the <c>==</c> operator.
        /// </remarks>
        private const string NUnitAssertionStubs =
            @"namespace NUnit.Framework
              {
                  public static class Assert
                  {
                      public static void IsNull(object value) { }
                      public static void IsNotNull(object value) { }
                      public static void IsNotNull(object value, string message) { }
                      public static void Null(object value) { }
                      public static void NotNull(object value) { }
                      public static void AreEqual(object expected, object actual) { }
                      public static void AreNotEqual(object expected, object actual) { }
                      public static void IsTrue(bool condition) { }
                  }
              }";

        /// <summary>
        /// Unity's own <c>Assert</c>, in the shape it actually ships: a generic family AND a
        /// <c>UnityEngine.Object</c> family.
        /// </summary>
        /// <remarks>
        /// The pair is the point. <c>IsNull&lt;T&gt;(T)</c> wins overload resolution for a
        /// <c>GameObject</c> argument and forwards to the <c>UnityEngine.Object</c> overload, which
        /// compares through the <c>==</c> operator -- so the assertion is destroyed-aware and must
        /// not be reported. A stub with only the generic family would let a fixture "prove" a
        /// negative that the real API does not have.
        /// </remarks>
        private const string UnityAssertionStubs =
            @"namespace UnityEngine.Assertions
              {
                  public static class Assert
                  {
                      public static void IsNull<T>(T value) where T : class { }
                      public static void IsNull<T>(T value, string message) where T : class { }
                      public static void IsNull(UnityEngine.Object value) { }
                      public static void IsNull(UnityEngine.Object value, string message) { }
                      public static void IsNotNull<T>(T value) where T : class { }
                      public static void IsNotNull<T>(T value, string message) where T : class { }
                      public static void IsNotNull(UnityEngine.Object value) { }
                      public static void IsNotNull(UnityEngine.Object value, string message) { }
                      public static void AreEqual<T>(T expected, T actual) { }
                      public static void AreNotEqual<T>(T expected, T actual) { }
                      public static void IsTrue(bool condition) { }
                  }
              }";

        /// <summary>
        /// Enough of <c>UnityEngine</c> for the hierarchy to be real.
        /// </summary>
        /// <remarks>
        /// <c>Object</c> carries the <c>==</c> overload rather than only the name, because the
        /// overload is the entire reason these diagnostics exist: a stub without it would be a type
        /// that has nothing wrong with <c>?.</c>. <c>Vector2</c> is a struct for the same reason --
        /// the negative cases need a nullable value type that a name-matching rule would confuse for
        /// a Unity type.
        /// </remarks>
        private const string UnityStubs =
            @"namespace UnityEngine
              {
                  public class Object
                  {
                      public string name;

                      public static bool operator ==(Object left, Object right) => ReferenceEquals(left, right);

                      public static bool operator !=(Object left, Object right) => !ReferenceEquals(left, right);

                      public override bool Equals(object other) => ReferenceEquals(this, other);

                      public override int GetHashCode() => 0;
                  }

                  public class Component : Object { }

                  public class MonoBehaviour : Component { }

                  public class GameObject : Object { }

                  public struct Vector2 { public float x; public float y; }
              }
              "
            + UnityAssertionStubs
            + "\n"
            + NUnitAssertionStubs;

        /// <summary>
        /// A type spelled <c>Object</c> that no compilation would confuse for Unity's, in a
        /// compilation where <c>UnityEngine.Object</c> does not exist at all.
        /// </summary>
        private const string LookalikeStubs =
            @"namespace Fake
              {
                  public class Object { public string name; }
              }
              namespace UnityEngine
              {
                  public struct Vector2 { public float x; public float y; }
              }
              " + NUnitAssertionStubs;

        [TestCase(
            "a field through ?.",
            @"private static GameObject Cached;
              public static string Name() => Cached?.name;",
            "?."
        )]
        [TestCase(
            "a local through ?.",
            @"public static string Name(GameObject candidate)
              {
                  GameObject local = candidate;
                  return local?.name;
              }",
            "?."
        )]
        [TestCase(
            "a property through ?.",
            @"private static MonoBehaviour Behaviour { get; set; }
              public static string Name() => Behaviour?.name;",
            "?."
        )]
        [TestCase(
            "a parameter through ?.",
            @"public static string Name(Component component) => component?.name;",
            "?."
        )]
        [TestCase(
            "an indexer through ?[]",
            @"public sealed class Rack : MonoBehaviour
              {
                  public GameObject this[int index] => null;
              }
              public static GameObject First(Rack rack) => rack?[0];",
            "?[]"
        )]
        [TestCase(
            "two parameters through ??",
            @"public static GameObject Or(GameObject candidate, GameObject fallback) =>
                  candidate ?? fallback;",
            "??"
        )]
        [TestCase(
            "a field through ??=",
            @"private static GameObject Cached;
              public static void Fill(GameObject fallback)
              {
                  Cached ??= fallback;
              }",
            "??="
        )]
        [TestCase(
            "a type parameter constrained to Component",
            @"public static string Name<T>(T value) where T : Component => value?.name;",
            "?."
        )]
        public void ANullPropagatingOperatorOnAUnityObjectIsReported(
            string shape,
            string body,
            string writtenOperator
        )
        {
            ImmutableArray<Diagnostic> reported = Analyze(body);

            Assert.IsNotEmpty(reported, shape + " must be reported");
            Assert.IsTrue(
                reported.All(diagnostic => diagnostic.Id == NullPropagationId),
                shape + " must report only " + NullPropagationId
            );
            Assert.IsTrue(
                reported.All(diagnostic => diagnostic.GetMessage().Contains(writtenOperator)),
                shape + " must quote '" + writtenOperator + "' back at the author"
            );
        }

        /// <summary>
        /// Every operand a name- or operator-matching rule would report and the compiler says is
        /// fine.
        /// </summary>
        [TestCase(
            "a nullable value type, which is what these operators are for",
            @"public static float X(Vector2? point) => point?.x ?? 0f;"
        )]
        [TestCase("a nullable int", @"public static int Count(int? candidate) => candidate ?? 0;")]
        [TestCase(
            "a string",
            @"public static string Trimmed(string candidate) => candidate?.Trim() ?? string.Empty;"
        )]
        [TestCase(
            "a class of the consumer's own",
            @"public sealed class Plain { public string Name; }
              public static string Name(Plain plain) => plain?.Name;"
        )]
        [TestCase(
            "a type parameter with no Unity constraint",
            @"public static string Name<T>(T value) where T : class => value?.ToString();"
        )]
        [TestCase(
            "an unconstrained type parameter, which both operators do accept",
            @"public static string Name<T>(T value) => value?.ToString();
              public static T Or<T>(T candidate, T fallback) => candidate ?? fallback;"
        )]
        [TestCase(
            "the comparison written out longhand, which does reach the overload",
            @"public static string Name(GameObject candidate) =>
                  candidate != null ? candidate.name : string.Empty;"
        )]
        public void ANullPropagatingOperatorOffAUnityObjectIsNotReported(string shape, string body)
        {
            Assert.IsEmpty(Messages(Analyze(body)), shape + " must not be reported");
        }

        [TestCase(
            "NUnit's IsNotNull",
            @"public static void Check(GameObject candidate) =>
                  NUnit.Framework.Assert.IsNotNull(candidate);"
        )]
        [TestCase(
            "NUnit's IsNotNull with a message",
            @"public static void Check(MonoBehaviour candidate) =>
                  NUnit.Framework.Assert.IsNotNull(candidate, ""it is still here"");"
        )]
        [TestCase(
            "NUnit's IsNull",
            @"public static void Check(GameObject candidate) =>
                  NUnit.Framework.Assert.IsNull(candidate);"
        )]
        [TestCase(
            "NUnit's NotNull",
            @"public static void Check(GameObject candidate) =>
                  NUnit.Framework.Assert.NotNull(candidate);"
        )]
        [TestCase(
            "NUnit's Null",
            @"public static void Check(GameObject candidate) =>
                  NUnit.Framework.Assert.Null(candidate);"
        )]
        [TestCase(
            "NUnit's AreEqual with the null first",
            @"public static void Check(GameObject candidate) =>
                  NUnit.Framework.Assert.AreEqual(null, candidate);"
        )]
        [TestCase(
            "NUnit's AreNotEqual with the null second",
            @"public static void Check(GameObject candidate) =>
                  NUnit.Framework.Assert.AreNotEqual(candidate, null);"
        )]
        [TestCase(
            "a type parameter constrained to Component",
            @"public static void Check<T>(T value) where T : Component =>
                  NUnit.Framework.Assert.IsNotNull(value);"
        )]
        public void ANullAssertionOnAUnityObjectIsReported(string shape, string body)
        {
            ImmutableArray<Diagnostic> reported = Analyze(body);

            Assert.IsNotEmpty(reported, shape + " must be reported");
            Assert.IsTrue(
                reported.All(diagnostic => diagnostic.Id == NullAssertionId),
                shape + " must report only " + NullAssertionId
            );
        }

        /// <summary>
        /// Unity's own <c>Assert</c> is destroyed-aware, so none of these is a defect.
        /// </summary>
        /// <remarks>
        /// Measured in a Unity 6000.4.6f1 editor on a destroyed <c>GameObject</c>, with
        /// <c>Assert.raiseExceptions = true</c> and an <c>IsNotNull((string)null)</c> control that
        /// did fail: <c>UnityEngine.Assertions.Assert.IsNull(destroyed)</c> PASSED and
        /// <c>IsNotNull(destroyed)</c> FAILED, both the opposite of their answers for a live object.
        /// <c>IsNull&lt;T&gt;</c> forwards to the <c>UnityEngine.Object</c> overload, which compares
        /// through the <c>==</c> operator; <c>NUnit.Framework.Assert.IsNull(object)</c> has no such
        /// overload and genuinely tests CLR null, which is why NUnit alone stays in scope. These are
        /// negatives because reporting them was a false positive on correct code.
        /// </remarks>
        [TestCase(
            "UnityEngine's IsNotNull",
            @"public static void Check(GameObject candidate) =>
                  UnityEngine.Assertions.Assert.IsNotNull(candidate);"
        )]
        [TestCase(
            "UnityEngine's IsNotNull with a message",
            @"public static void Check(GameObject candidate) =>
                  UnityEngine.Assertions.Assert.IsNotNull(candidate, ""it is still here"");"
        )]
        [TestCase(
            "UnityEngine's IsNull",
            @"public static void Check(Component candidate) =>
                  UnityEngine.Assertions.Assert.IsNull(candidate);"
        )]
        [TestCase(
            "UnityEngine's IsNull with a message",
            @"public static void Check(Component candidate) =>
                  UnityEngine.Assertions.Assert.IsNull(candidate, ""it is gone"");"
        )]
        [TestCase(
            "UnityEngine's IsNotNull through the UnityEngine.Object overload",
            @"public static void Check(UnityEngine.Object candidate) =>
                  UnityEngine.Assertions.Assert.IsNotNull(candidate);"
        )]
        [TestCase(
            "UnityEngine's AreEqual with the null first",
            @"public static void Check(GameObject candidate) =>
                  UnityEngine.Assertions.Assert.AreEqual(null, candidate);"
        )]
        [TestCase(
            "UnityEngine's AreEqual with the null second",
            @"public static void Check(GameObject candidate) =>
                  UnityEngine.Assertions.Assert.AreEqual(candidate, null);"
        )]
        [TestCase(
            "UnityEngine's AreNotEqual",
            @"public static void Check(GameObject candidate) =>
                  UnityEngine.Assertions.Assert.AreNotEqual(null, candidate);"
        )]
        public void UnitysOwnAssertIsDestroyedAwareAndIsNotReported(string shape, string body)
        {
            Assert.IsEmpty(
                Messages(Analyze(body)),
                shape + " is already destroyed-aware and must not be reported"
            );
        }

        [TestCase(
            "an assertion about something that is not a UnityEngine.Object",
            @"public sealed class Plain { }
              public static void Check(Plain plain) => NUnit.Framework.Assert.IsNotNull(plain);"
        )]
        [TestCase(
            "an assertion about an unconstrained type parameter",
            @"public static void Check<T>(T value) => NUnit.Framework.Assert.IsNotNull(value);"
        )]
        [TestCase(
            "the house form, which does reach the overload",
            @"public static void Check(GameObject candidate) =>
                  UnityEngine.Assertions.Assert.IsTrue(candidate != null);"
        )]
        [TestCase(
            "a consumer type that merely spells the member Assert.IsNotNull",
            @"private static class Assert
              {
                  public static void IsNotNull(object value) { }
              }
              public static void Check(GameObject candidate) => Assert.IsNotNull(candidate);"
        )]
        public void AnAssertionThatIsNotAUnityObjectNullTestIsNotReported(string shape, string body)
        {
            Assert.IsEmpty(Messages(Analyze(body)), shape + " must not be reported");
        }

        [Test]
        public void TheNullPropagationMessageNamesTheReceiverItsTypeAndTheOperator()
        {
            Diagnostic reported = Single(
                @"public static string Name(Component component) => component?.name;"
            );

            Assert.AreEqual(NullPropagationId, reported.Id);
            string message = reported.GetMessage();
            StringAssert.Contains("component", message);
            StringAssert.Contains("UnityEngine.Component", message);
            StringAssert.Contains("?.", message);
        }

        [Test]
        public void TheNullAssertionMessageNamesTheAssertionItsArgumentTypeAndTheArgument()
        {
            Diagnostic reported = Single(
                @"public static void Check(GameObject candidate) =>
                      NUnit.Framework.Assert.IsNotNull(candidate);"
            );

            Assert.AreEqual(NullAssertionId, reported.Id);
            string message = reported.GetMessage();
            StringAssert.Contains("Assert.IsNotNull", message);
            StringAssert.Contains("UnityEngine.GameObject", message);
            StringAssert.Contains("candidate", message);
        }

        /// <summary>
        /// A compilation that has never heard of Unity must cost nothing.
        /// </summary>
        /// <remarks>
        /// The analyzer resolves <c>UnityEngine.Object</c> once at compilation start and registers
        /// no action at all when it is absent, so this also pins that the match is on the resolved
        /// symbol: the fixture uses a type of its own spelled <c>Object</c>, on both operators and
        /// through an assertion, and none of it is Unity's.
        /// </remarks>
        [Test]
        public void NothingIsReportedWhenTheCompilationHasNoUnityObject()
        {
            Assert.IsEmpty(
                Messages(
                    Analyze(
                        @"public static string Name(Fake.Object candidate) =>
                              candidate?.name ?? string.Empty;
                          public static void Check(Fake.Object candidate) =>
                              NUnit.Framework.Assert.IsNotNull(candidate);",
                        LookalikeStubs,
                        ReportDiagnostic.Default
                    )
                ),
                "a compilation without UnityEngine.Object must report nothing"
            );
        }

        /// <summary>
        /// The consumer contract: on by default, so a project that has taken on this package gets
        /// the safety without discovering it, but never able to fail their build.
        /// </summary>
        [TestCase(
            NullPropagationId,
            @"public static string Name(Component component) => component?.name;"
        )]
        [TestCase(
            NullAssertionId,
            @"public static void Check(GameObject candidate) =>
                  NUnit.Framework.Assert.IsNotNull(candidate);"
        )]
        public void EachDiagnosticIsOnByDefaultSuppressibleAndNeverAboveAWarning(
            string id,
            string offending
        )
        {
            DiagnosticDescriptor descriptor =
                new UnityObjectNullAnalyzer().SupportedDiagnostics.Single(candidate =>
                    candidate.Id == id
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

            Assert.IsNotEmpty(
                Analyze(offending, UnityStubs, ReportDiagnostic.Default),
                "a consumer who configures nothing must still be told"
            );
            Assert.IsEmpty(
                Messages(Analyze(offending, UnityStubs, ReportDiagnostic.Suppress)),
                "and one who does not want it must be able to turn it off"
            );
        }

        private static string[] Messages(ImmutableArray<Diagnostic> reported)
        {
            return reported.Select(diagnostic => diagnostic.ToString()).ToArray();
        }

        private static Diagnostic Single(string body)
        {
            ImmutableArray<Diagnostic> reported = Analyze(body);
            Assert.AreEqual(1, reported.Length, "Expected exactly one diagnostic");
            return reported[0];
        }

        private static ImmutableArray<Diagnostic> Analyze(string body)
        {
            return Analyze(body, UnityStubs, ReportDiagnostic.Default);
        }

        /// <summary>
        /// Compiles <paramref name="body"/> against <paramref name="stubs"/> and runs the analyzer
        /// over it.
        /// </summary>
        /// <param name="body">Members of a static class in namespace <c>Consumer</c>.</param>
        /// <param name="stubs">The world the fixture is compiled against.</param>
        /// <param name="reportedAs">
        /// What the compilation says about both diagnostics -- <see cref="ReportDiagnostic.Default"/>
        /// for a consumer who configures nothing, or anything else for the ruleset /
        /// <c>.editorconfig</c> entry they would write, expressed as the option Roslyn resolves both
        /// of them to.
        /// </param>
        /// <returns>Everything the analyzer reported.</returns>
        private static ImmutableArray<Diagnostic> Analyze(
            string body,
            string stubs,
            ReportDiagnostic reportedAs
        )
        {
            string source =
                "namespace Consumer { using UnityEngine; public static class Subject { "
                + body
                + " } }\n"
                + stubs;

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
                    ImmutableDictionary<string, ReportDiagnostic>
                        .Empty.Add(NullPropagationId, reportedAs)
                        .Add(NullAssertionId, reportedAs)
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
                    ImmutableArray.Create<DiagnosticAnalyzer>(new UnityObjectNullAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }
    }
}
