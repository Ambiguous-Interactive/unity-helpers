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
    /// Pins the shape <c>WUH017</c> reports -- a same-object <c>GetComponent</c> whose result is
    /// thrown away by a null comparison -- and, at greater length, the shapes it must not.
    /// </summary>
    /// <remarks>
    /// The negatives are why this is an analyzer. Three methods in this repository are spelled
    /// <c>GetComponent</c>, only two of them have a <c>TryGetComponent</c> that means the same
    /// thing, and a fourth family (<c>GetComponentInChildren</c>, <c>GetComponentInParent</c>) has
    /// no non-allocating existence query anywhere -- so a rule matching the name would name a fix
    /// that does not exist for a third of what it found (#741).
    /// </remarks>
    [TestFixture]
    public sealed class GetComponentNullComparisonAnalyzerTests
    {
        private const string GetComponentNullComparisonId = "WUH017";

        /// <summary>
        /// Enough of <c>UnityEngine</c> for the search family to be real.
        /// </summary>
        /// <remarks>
        /// <c>Component</c> and <c>GameObject</c> each declare the whole family themselves, as Unity
        /// does, because the analyzer matches the DECLARING type: a stub that inherited
        /// <c>GetComponent</c> from a shared base would let a fixture pass against a hierarchy Unity
        /// does not have. <c>Object</c> carries the <c>==</c> overload so the comparison arrives as
        /// a user-defined operator with a converted null literal, which is the shape the analyzer
        /// has to see through.
        /// </remarks>
        private const string UnityStubs =
            @"namespace UnityEngine
              {
                  using System;

                  public class Object
                  {
                      public string name;

                      public static bool operator ==(Object left, Object right) => ReferenceEquals(left, right);

                      public static bool operator !=(Object left, Object right) => !ReferenceEquals(left, right);

                      public override bool Equals(object other) => ReferenceEquals(this, other);

                      public override int GetHashCode() => 0;
                  }

                  public class Component : Object
                  {
                      public T GetComponent<T>() => default;

                      public Component GetComponent(Type type) => null;

                      public Component GetComponent(string type) => null;

                      public T GetComponentInChildren<T>() => default;

                      public T GetComponentInParent<T>() => default;

                      public T[] GetComponents<T>() => null;

                      public bool TryGetComponent<T>(out T component)
                      {
                          component = default;
                          return false;
                      }

                      public bool TryGetComponent(Type type, out Component component)
                      {
                          component = null;
                          return false;
                      }
                  }

                  public class MonoBehaviour : Component { }

                  public class SpriteRenderer : Component { }

                  public class GameObject : Object
                  {
                      public T GetComponent<T>() => default;

                      public Component GetComponent(Type type) => null;

                      public Component GetComponent(string type) => null;

                      public T GetComponentInChildren<T>() => default;

                      public T GetComponentInParent<T>() => default;

                      public bool TryGetComponent<T>(out T component)
                      {
                          component = default;
                          return false;
                      }

                      public bool TryGetComponent(Type type, out Component component)
                      {
                          component = null;
                          return false;
                      }
                  }

                  public interface IDamageable { }
              }"
            + "\n"
            + PackageHelperStubs;

        /// <summary>
        /// The package's own dispatching extension, in the shape it ships.
        /// </summary>
        /// <remarks>
        /// <c>Helpers.GetComponent&lt;T&gt;(this Object)</c> returns <c>default</c> for a target
        /// that is neither a <c>GameObject</c> nor a <c>Component</c>, so a null comparison over it
        /// is a test of the TARGET and is correct as written. It is in the stubs to be a negative,
        /// and <c>HasComponent</c> is beside it because the message names it as the fix.
        /// </remarks>
        private const string PackageHelperStubs =
            @"namespace WallstopStudios.UnityHelpers.Core.Helper
              {
                  using UnityEngine;
                  using Object = UnityEngine.Object;

                  public static class Helpers
                  {
                      public static T GetComponent<T>(this Object target) => default;

                      public static bool HasComponent<T>(this GameObject gameObject)
                          where T : Object => false;
                  }
              }";

        /// <summary>
        /// A world with the same method names and no <c>UnityEngine.Component</c> at all.
        /// </summary>
        private const string LookalikeStubs =
            @"namespace Fake
              {
                  using System;

                  public sealed class Widget
                  {
                      public T GetComponent<T>() => default;

                      public object GetComponent(Type type) => null;
                  }
              }";

        [TestCase(
            "a generic search off a GameObject",
            @"public static bool Has(GameObject subject) =>
                  subject.GetComponent<SpriteRenderer>() != null;"
        )]
        [TestCase(
            "a generic search off a Component",
            @"public static bool Missing(Component subject) =>
                  subject.GetComponent<SpriteRenderer>() == null;"
        )]
        [TestCase(
            "a generic search off an implicit this",
            @"public sealed class Probe : MonoBehaviour
              {
                  public bool Has() => GetComponent<SpriteRenderer>() != null;
              }"
        )]
        [TestCase(
            "a Type search off a GameObject",
            @"public static bool Has(GameObject subject, System.Type componentType) =>
                  subject.GetComponent(componentType) != null;"
        )]
        [TestCase(
            "a Type search off a Component",
            @"public static bool Has(Component subject, System.Type componentType) =>
                  subject.GetComponent(componentType) != null;"
        )]
        [TestCase(
            "the null written first",
            @"public static bool Has(GameObject subject) =>
                  null != subject.GetComponent<SpriteRenderer>();"
        )]
        [TestCase(
            "a search inside an if",
            @"public static void Ensure(GameObject subject)
              {
                  if (subject.GetComponent<SpriteRenderer>() == null)
                  {
                      return;
                  }
              }"
        )]
        [TestCase(
            "a search inside a ternary",
            @"public static string Name(GameObject subject) =>
                  subject.GetComponent<SpriteRenderer>() != null ? subject.name : string.Empty;"
        )]
        public void ADiscardedGetComponentComparedAgainstNullIsReported(string shape, string body)
        {
            ImmutableArray<Diagnostic> reported = Analyze(body);

            Assert.IsNotEmpty(reported, shape + " must be reported");
            Assert.IsTrue(
                reported.All(diagnostic => diagnostic.Id == GetComponentNullComparisonId),
                shape + " must report only " + GetComponentNullComparisonId
            );
            Assert.IsTrue(
                reported.All(diagnostic => diagnostic.GetMessage().Contains("TryGetComponent")),
                shape + " must name TryGetComponent as the fix"
            );
        }

        /// <summary>
        /// The pattern forms, which <c>WUH003</c> also reports for a different defect.
        /// </summary>
        /// <remarks>
        /// The overlap is deliberate and is the reason these are covered here at all: WUH003's fix
        /// -- write the comparison out -- leaves the allocation exactly where it was, so an author
        /// told only that would move to <c>!= null</c> and keep paying for it.
        /// </remarks>
        [TestCase(
            "is null",
            @"public static bool Missing(GameObject subject) =>
                  subject.GetComponent<SpriteRenderer>() is null;"
        )]
        [TestCase(
            "is not null",
            @"public static bool Has(GameObject subject) =>
                  subject.GetComponent<SpriteRenderer>() is not null;"
        )]
        public void ANullPatternOverAGetComponentIsReported(string shape, string body)
        {
            ImmutableArray<Diagnostic> reported = Analyze(body);

            Assert.IsNotEmpty(reported, shape + " must be reported");
            Assert.IsTrue(
                reported.All(diagnostic => diagnostic.Id == GetComponentNullComparisonId),
                shape + " must report only " + GetComponentNullComparisonId
            );
        }

        /// <summary>
        /// Every shape a rule matching the method NAME would report, and each one's reason.
        /// </summary>
        [TestCase(
            "TryGetComponent, which is the fix",
            @"public static bool Has(GameObject subject)
              {
                  if (!subject.TryGetComponent(out SpriteRenderer renderer))
                  {
                      return false;
                  }

                  return renderer != null;
              }"
        )]
        [TestCase(
            "HasComponent, which is the other fix",
            @"public static bool Has(GameObject subject) =>
                  WallstopStudios.UnityHelpers.Core.Helper.Helpers.HasComponent<SpriteRenderer>(subject);"
        )]
        [TestCase(
            "a null comparison over something that is not a GetComponent",
            @"public static bool Alive(GameObject subject) => subject != null;"
        )]
        [TestCase(
            "the result held in a local, which is what to do when the component is used",
            @"public static string Name(GameObject subject)
              {
                  SpriteRenderer renderer = subject.GetComponent<SpriteRenderer>();
                  return renderer != null ? renderer.name : string.Empty;
              }"
        )]
        [TestCase(
            "GetComponentInChildren, for which no existence query exists",
            @"public static bool Has(GameObject subject) =>
                  subject.GetComponentInChildren<SpriteRenderer>() != null;"
        )]
        [TestCase(
            "GetComponentInParent, for the same reason",
            @"public static bool Has(GameObject subject) =>
                  subject.GetComponentInParent<SpriteRenderer>() != null;"
        )]
        [TestCase(
            "the string overload, which has no Try counterpart",
            @"public static bool Has(GameObject subject) =>
                  subject.GetComponent(""SpriteRenderer"") != null;"
        )]
        [TestCase(
            "GetComponents, whose array is not a component",
            @"public static bool Any(Component subject) =>
                  subject.GetComponents<SpriteRenderer>() != null;"
        )]
        [TestCase(
            "the package's own dispatching extension, whose null answer is about the target",
            @"public static bool Missing(UnityEngine.Object target) =>
                  WallstopStudios.UnityHelpers.Core.Helper.Helpers.GetComponent<SpriteRenderer>(target)
                      == null;"
        )]
        [TestCase(
            "a consumer type that merely spells the member GetComponent",
            @"public sealed class Rack
              {
                  public SpriteRenderer GetComponent<T>() => null;
              }
              public static bool Has(Rack rack) => rack.GetComponent<SpriteRenderer>() != null;"
        )]
        [TestCase(
            "a type pattern, which is not a null test",
            @"public static bool Typed(GameObject subject) =>
                  subject.GetComponent<Component>() is SpriteRenderer;"
        )]
        [TestCase(
            "a relational comparison, which is not an equality",
            @"public static bool Any(Component subject) =>
                  0 < subject.GetComponents<SpriteRenderer>().Length;"
        )]
        [TestCase(
            "a null type argument, whose Try spelling is the same throw with different syntax",
            @"public static bool Missing(GameObject subject) =>
                  subject.GetComponent((System.Type)null) == null;"
        )]
        public void AShapeWithNoTryGetComponentToOfferIsNotReported(string shape, string body)
        {
            Assert.IsEmpty(Messages(Analyze(body)), shape + " must not be reported");
        }

        /// <summary>
        /// <c>?.</c> and <c>??</c> over a <c>GetComponent</c> result belong to <c>WUH003</c>.
        /// </summary>
        /// <remarks>
        /// They are a correctness defect rather than a discarded result -- the component is USED,
        /// through an operator that cannot see a destroyed object -- so reporting them here would
        /// have been the duplication #741 was explicitly not asking for.
        /// </remarks>
        [TestCase(
            "the null-conditional operator",
            @"public static string Name(GameObject subject) =>
                  subject.GetComponent<SpriteRenderer>()?.name;"
        )]
        [TestCase(
            "the null-coalescing operator",
            @"public static SpriteRenderer Or(GameObject subject, SpriteRenderer fallback) =>
                  subject.GetComponent<SpriteRenderer>() ?? fallback;"
        )]
        public void NullPropagationOverAGetComponentIsLeftToWuh003(string shape, string body)
        {
            Assert.IsEmpty(Messages(Analyze(body)), shape + " is WUH003's, not this rule's");
        }

        [Test]
        public void TheMessageNamesTheCallTheTryFormAndTheHasForm()
        {
            Diagnostic reported = Single(
                @"public static bool Has(GameObject subject) =>
                      subject.GetComponent<SpriteRenderer>() != null;"
            );

            Assert.AreEqual(GetComponentNullComparisonId, reported.Id);
            string message = reported.GetMessage();
            StringAssert.Contains("subject.GetComponent<SpriteRenderer>()", message);
            StringAssert.Contains("TryGetComponent(out SpriteRenderer value)", message);
            StringAssert.Contains("HasComponent<SpriteRenderer>()", message);
        }

        /// <summary>
        /// The <c>System.Type</c> form is offered the author's own spelling of the type argument.
        /// </summary>
        [Test]
        public void TheTypeOverloadMessageQuotesTheArgumentTheAuthorWrote()
        {
            Diagnostic reported = Single(
                @"public static bool Has(GameObject subject, System.Type componentType) =>
                      subject.GetComponent(componentType) != null;"
            );

            string message = reported.GetMessage();
            StringAssert.Contains("TryGetComponent(componentType, out Component value)", message);
            StringAssert.Contains("HasComponent(componentType)", message);
        }

        /// <summary>
        /// An interface type argument is legal for <c>GetComponent&lt;T&gt;</c> and illegal for
        /// <c>HasComponent&lt;T&gt;</c>, whose constraint is <c>where T : Object</c>.
        /// </summary>
        [Test]
        public void AnInterfaceSearchIsNotOfferedHasComponent()
        {
            Diagnostic reported = Single(
                @"public static bool Has(GameObject subject) =>
                      subject.GetComponent<IDamageable>() != null;"
            );

            string message = reported.GetMessage();
            StringAssert.Contains("TryGetComponent(out IDamageable value)", message);
            StringAssert.Contains("TryGetComponent(out IDamageable _)", message);
            Assert.IsFalse(
                message.Contains("HasComponent"),
                "HasComponent constrains T to UnityEngine.Object and would not compile here"
            );
        }

        /// <summary>
        /// A compilation that has never heard of Unity must cost nothing.
        /// </summary>
        /// <remarks>
        /// The analyzer resolves <c>UnityEngine.Component</c> and <c>UnityEngine.GameObject</c> once
        /// at compilation start and registers no action when neither is there, so this also pins
        /// that the match is on the resolved symbol rather than the method name: the fixture calls
        /// both spellings on a type of its own.
        /// </remarks>
        [Test]
        public void NothingIsReportedWhenTheCompilationHasNoUnityComponent()
        {
            Assert.IsEmpty(
                Messages(
                    Analyze(
                        @"public static bool Has(Fake.Widget widget, System.Type componentType) =>
                              widget.GetComponent<string>() != null
                                  || widget.GetComponent(componentType) != null;",
                        LookalikeStubs,
                        ReportDiagnostic.Default,
                        withUnityUsing: false
                    )
                ),
                "a compilation without UnityEngine.Component must report nothing"
            );
        }

        /// <summary>
        /// A <c>#pragma</c> around the one call site an author has decided about.
        /// </summary>
        [Test]
        public void APragmaSuppressionSilencesTheSiteItWraps()
        {
            Assert.IsEmpty(
                Messages(
                    Analyze(
                        @"public static bool Has(GameObject subject)
                          {
#pragma warning disable WUH017
                              bool present = subject.GetComponent<SpriteRenderer>() != null;
#pragma warning restore WUH017
                              return present;
                          }"
                    )
                ),
                "an author who has decided about a call site must be able to say so"
            );
        }

        /// <summary>
        /// The consumer contract: on by default, capped at a warning, and always suppressible.
        /// </summary>
        /// <remarks>
        /// On by default because the population supports it rather than merely permits it. Measured
        /// 2026-09-07 over this package's <c>Runtime/</c>, <c>Editor/</c> and <c>Tests/</c>: seven
        /// sites, all under <c>Tests/</c>, against the 346 and 306 that put <c>WUH010</c> and
        /// <c>WUH013</c> behind an opt-in.
        /// </remarks>
        [Test]
        public void TheDiagnosticIsOnByDefaultSuppressibleAndNeverAboveAWarning()
        {
            const string offending =
                @"public static bool Has(GameObject subject) =>
                      subject.GetComponent<SpriteRenderer>() != null;";
            DiagnosticDescriptor descriptor =
                new GetComponentNullComparisonAnalyzer().SupportedDiagnostics.Single();

            Assert.AreEqual(GetComponentNullComparisonId, descriptor.Id);
            Assert.IsTrue(
                descriptor.IsEnabledByDefault,
                "a consumer using this package should get the finding without asking for it"
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
        /// What the compilation says about the diagnostic -- <see cref="ReportDiagnostic.Default"/>
        /// for a consumer who configures nothing, or anything else for the ruleset /
        /// <c>.editorconfig</c> entry they would write.
        /// </param>
        /// <param name="withUnityUsing">
        /// Whether the fixture opens <c>UnityEngine</c>, which the lookalike world does not have.
        /// </param>
        /// <returns>Everything the analyzer reported.</returns>
        private static ImmutableArray<Diagnostic> Analyze(
            string body,
            string stubs,
            ReportDiagnostic reportedAs,
            bool withUnityUsing = true
        )
        {
            string source =
                "namespace Consumer { "
                + (withUnityUsing ? "using UnityEngine; " : string.Empty)
                + "public static class Subject { "
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
                    ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(
                        GetComponentNullComparisonId,
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
                        new GetComponentNullComparisonAnalyzer()
                    )
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }
    }
}
