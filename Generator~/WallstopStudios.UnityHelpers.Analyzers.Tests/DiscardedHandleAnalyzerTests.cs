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
    /// Pins the two discarded-handle rules: an <c>EffectHandle</c> dropped at the call site
    /// (WUH006) and a coroutine handle dropped at the call site (WUH007).
    /// </summary>
    /// <remarks>
    /// The negatives carry the weight. Every one of them is a call to exactly the same method as a
    /// positive, differing only in what happens to the value -- which is the whole rule, and the
    /// only thing separating it from a rule that would fire on every coroutine in the tree.
    /// </remarks>
    [TestFixture]
    public sealed class DiscardedHandleAnalyzerTests
    {
        private const string EffectDiagnosticId = "WUH006";
        private const string CoroutineDiagnosticId = "WUH007";

        /// <summary>
        /// Enough of <c>UnityEngine</c> for the fixtures to be hermetic.
        /// </summary>
        private const string UnityEngineStub =
            @"namespace UnityEngine
              {
                  using System.Collections;

                  public class Object { }

                  public sealed class Coroutine { }

                  public class MonoBehaviour : Object
                  {
                      public Coroutine StartCoroutine(IEnumerator routine) => null;
                      public void StopCoroutine(Coroutine routine) { }
                  }
              }";

        /// <summary>
        /// The package's four coroutine-starting helpers, at the signatures they ship with.
        /// </summary>
        /// <remarks>
        /// A stub can drift from what it stands for, so
        /// <see cref="TheStubbedMembersMatchTheOnesThePackageShips"/> reads the real sources and
        /// fails if these names, namespaces or return types stop existing.
        /// </remarks>
        private const string PackageHelpers =
            @"namespace WallstopStudios.UnityHelpers.Core.Helper
              {
                  using System;
                  using UnityEngine;

                  public static class Helpers
                  {
                      public static Coroutine StartFunctionAsCoroutine(this MonoBehaviour monoBehaviour, Action action, float updateRate, bool useJitter = false, bool waitBefore = false) => null;
                      public static Coroutine ExecuteFunctionAfterDelay(this MonoBehaviour monoBehaviour, Action action, float delay) => null;
                      public static Coroutine ExecuteFunctionNextFrame(this MonoBehaviour monoBehaviour, Action action) => null;
                      public static Coroutine ExecuteFunctionAfterFrame(this MonoBehaviour monoBehaviour, Action action) => null;
                  }
              }";

        /// <summary>
        /// The effects surface: the two members that hand back a handle, and the deliberate
        /// no-handle overload that must never be reported.
        /// </summary>
        private const string PackageTags =
            @"namespace WallstopStudios.UnityHelpers.Tags
              {
                  using UnityEngine;

                  public readonly struct EffectHandle { }

                  public sealed class AttributeEffect { }

                  public sealed class EffectHandler : MonoBehaviour
                  {
                      public EffectHandle? ApplyEffect(AttributeEffect effect) => null;
                  }

                  public sealed class TagHandler : MonoBehaviour
                  {
                      public void ForceApplyEffect(AttributeEffect effect) { }
                  }

                  public static class AttributeUtilities
                  {
                      public static EffectHandle? ApplyEffect(this UnityEngine.Object target, AttributeEffect attributeEffect) => null;
                  }
              }";

        /// <summary>
        /// Members every fixture gets, so a test case is only the shape under test.
        /// </summary>
        private const string SharedFixtureMembers =
            @"private readonly List<Coroutine> _handles = new List<Coroutine>();
              private Coroutine _handle;
              private EffectHandle? _stored;
              private EffectHandler _effectHandler;
              private TagHandler _tagHandler;
              private AttributeEffect _effect;
              private IEnumerator Body() { yield break; }
              private void Consume(Coroutine handle) { }";

        [TestCase(
            "StartCoroutine as a whole statement",
            "StartCoroutine",
            @"public void Go() { StartCoroutine(Body()); }"
        )]
        [TestCase(
            "the periodic-job helper",
            "StartFunctionAsCoroutine",
            @"public void Go() { this.StartFunctionAsCoroutine(() => { }, 0.5f); }"
        )]
        [TestCase(
            "the delay helper",
            "ExecuteFunctionAfterDelay",
            @"public void Go() { this.ExecuteFunctionAfterDelay(() => { }, 1f); }"
        )]
        [TestCase(
            "the next-frame helper",
            "ExecuteFunctionNextFrame",
            @"public void Go() { this.ExecuteFunctionNextFrame(() => { }); }"
        )]
        [TestCase(
            "the after-frame helper",
            "ExecuteFunctionAfterFrame",
            @"public void Go() { this.ExecuteFunctionAfterFrame(() => { }); }"
        )]
        [TestCase(
            "an explicit discard",
            "StartCoroutine",
            @"public void Go() { _ = StartCoroutine(Body()); }"
        )]
        [TestCase(
            "a consumer's own starter, which no name list would carry",
            "BeginWork",
            @"private Coroutine BeginWork() { return StartCoroutine(Body()); }
              public void Go() { BeginWork(); }"
        )]
        public void ADiscardedCoroutineHandleIsReported(string shape, string method, string body)
        {
            Diagnostic reported = Single(body);
            Assert.AreEqual(CoroutineDiagnosticId, reported.Id, shape + " must report WUH007");
            StringAssert.Contains(method, reported.GetMessage());
        }

        /// <summary>
        /// Every one of these calls the same starter as a positive above. The only difference is
        /// that the handle survives the statement, which is the entire rule.
        /// </summary>
        [TestCase(
            "held in a local",
            @"public void Go() { Coroutine handle = StartCoroutine(Body()); StopCoroutine(handle); }"
        )]
        [TestCase("stored in a field", @"public void Go() { _handle = StartCoroutine(Body()); }")]
        [TestCase(
            "added to a list, which is how one owner keeps many",
            @"public void Go() { _handles.Add(StartCoroutine(Body())); }"
        )]
        [TestCase(
            "returned to the caller",
            @"public Coroutine Go() { return StartCoroutine(Body()); }"
        )]
        [TestCase(
            "passed as an argument",
            @"public void Go() { Consume(StartCoroutine(Body())); }"
        )]
        [TestCase(
            "yield returned, which is how a coroutine waits on a coroutine",
            @"private IEnumerator Outer() { yield return StartCoroutine(Body()); }"
        )]
        [TestCase(
            "a package helper whose handle is kept",
            @"public void Go() { _handle = this.ExecuteFunctionNextFrame(() => { }); }"
        )]
        public void ACoroutineHandleThatIsKeptIsNotReported(string shape, string body)
        {
            Assert.IsEmpty(Analyze(body), shape + " must not be reported");
        }

        [TestCase(
            "the component method as a whole statement",
            @"public void Go() { _effectHandler.ApplyEffect(_effect); }"
        )]
        [TestCase(
            "the extension, explicitly discarded",
            @"public void Go() { _ = this.ApplyEffect(_effect); }"
        )]
        public void ADiscardedEffectHandleIsReported(string shape, string body)
        {
            Diagnostic reported = Single(body);
            Assert.AreEqual(EffectDiagnosticId, reported.Id, shape + " must report WUH006");
            StringAssert.Contains("ApplyEffect", reported.GetMessage());
        }

        /// <summary>
        /// <c>ForceApplyEffect</c> is the deliberate no-handle overload, so a call to it is a
        /// decision rather than a mistake and reporting it would make the rule unusable.
        /// </summary>
        [TestCase(
            "ForceApplyEffect, which returns no handle to keep",
            @"public void Go() { _tagHandler.ForceApplyEffect(_effect); }"
        )]
        [TestCase(
            "the handle held in a local",
            @"public void Go() { EffectHandle? handle = _effectHandler.ApplyEffect(_effect); }"
        )]
        [TestCase(
            "the handle stored in a field",
            @"public void Go() { _stored = _effectHandler.ApplyEffect(_effect); }"
        )]
        [TestCase(
            "the handle unwrapped straight into a local",
            @"public void Go() { EffectHandle handle = _effectHandler.ApplyEffect(_effect).Value; }"
        )]
        [TestCase(
            "the extension's handle stored in a field",
            @"public void Go() { _stored = this.ApplyEffect(_effect); }"
        )]
        public void AnEffectHandleThatIsKeptIsNotReported(string shape, string body)
        {
            Assert.IsEmpty(Analyze(body), shape + " must not be reported");
        }

        /// <summary>
        /// A compilation referencing neither handle type registers no per-operation callback at
        /// all, so it costs two symbol lookups and nothing else.
        /// </summary>
        /// <remarks>
        /// The fixture discards calls returning types that are spelled <c>Coroutine</c> and
        /// <c>EffectHandle</c> in somebody else's namespace, so this also proves neither half of
        /// the rule ever matches on a name alone.
        /// </remarks>
        [Test]
        public void NothingIsReportedWhenTheCompilationHasNeitherHandleType()
        {
            Assert.IsEmpty(
                AnalyzeSource(
                    @"namespace Other
                      {
                          public sealed class Coroutine { }

                          public struct EffectHandle { }

                          public static class Subject
                          {
                              public static Coroutine StartCoroutine() => null;
                              public static EffectHandle ApplyEffect() => default;

                              public static void Go()
                              {
                                  StartCoroutine();
                                  ApplyEffect();
                              }
                          }
                      }"
                )
            );
        }

        /// <summary>
        /// The consumer contract for the whole <c>WUH</c> family: on by default, so a project that
        /// has taken on this package gets the safety without discovering it, and never able to fail
        /// their build.
        /// </summary>
        [Test]
        public void BothDiagnosticsAreOnByDefaultAndNeverAboveAWarning()
        {
            ImmutableArray<DiagnosticDescriptor> supported =
                new DiscardedHandleAnalyzer().SupportedDiagnostics;

            CollectionAssert.AreEquivalent(
                new[] { EffectDiagnosticId, CoroutineDiagnosticId },
                supported.Select(descriptor => descriptor.Id).ToArray()
            );
            foreach (DiagnosticDescriptor descriptor in supported)
            {
                Assert.IsTrue(
                    descriptor.IsEnabledByDefault,
                    descriptor.Id + " should reach a consumer who configures nothing"
                );
                Assert.AreEqual(
                    DiagnosticSeverity.Warning,
                    descriptor.DefaultSeverity,
                    descriptor.Id + " must never fail a build over code that already works"
                );
            }
        }

        /// <summary>
        /// The stubs above stand in for real shipped code, so they have to keep standing for it.
        /// </summary>
        /// <remarks>
        /// The analyzer resolves <c>EffectHandle</c> by its full metadata name and matches
        /// <c>ApplyEffect</c> by name. Moving either -- or giving one of the coroutine helpers a
        /// different return type -- would make the analyzer silently stop covering the package's
        /// own code while every test here kept passing against the stubs, which is the one way this
        /// suite could lie.
        /// </remarks>
        [Test]
        public void TheStubbedMembersMatchTheOnesThePackageShips()
        {
            string repoRoot = FindRepositoryRoot();

            string effectHandle = ReadShipped(repoRoot, "Runtime", "Tags", "EffectHandle.cs");
            StringAssert.Contains(
                "namespace WallstopStudios.UnityHelpers.Tags",
                effectHandle,
                "the analyzer resolves EffectHandle through this namespace"
            );
            StringAssert.Contains(
                "struct EffectHandle",
                effectHandle,
                "the analyzer resolves this type by name"
            );

            StringAssert.Contains(
                "public EffectHandle? ApplyEffect(AttributeEffect",
                ReadShipped(repoRoot, "Runtime", "Tags", "EffectHandler.cs"),
                "the analyzer matches ApplyEffect by name and by its EffectHandle return"
            );
            StringAssert.Contains(
                "public static EffectHandle? ApplyEffect(this Object target, AttributeEffect",
                ReadShipped(repoRoot, "Runtime", "Tags", "AttributeUtilities.cs"),
                "the extension reaches the same rule, so it has to keep the same shape"
            );
            StringAssert.Contains(
                "public void ForceApplyEffect(AttributeEffect",
                ReadShipped(repoRoot, "Runtime", "Tags", "TagHandler.cs"),
                "the no-handle overload is out of scope only while it returns void"
            );

            string helpers = ReadShipped(repoRoot, "Runtime", "Core", "Helper", "Helpers.cs");
            StringAssert.Contains(
                "namespace WallstopStudios.UnityHelpers.Core.Helper",
                helpers,
                "the coroutine helpers live here"
            );
            foreach (
                string helper in new[]
                {
                    "StartFunctionAsCoroutine",
                    "ExecuteFunctionAfterDelay",
                    "ExecuteFunctionNextFrame",
                    "ExecuteFunctionAfterFrame",
                }
            )
            {
                StringAssert.Contains(
                    "public static Coroutine " + helper + "(",
                    helpers,
                    helper + " is covered by its return type, so that return type has to stay"
                );
            }
        }

        private static string ReadShipped(string repoRoot, params string[] segments)
        {
            string[] parts = new string[segments.Length + 1];
            parts[0] = repoRoot;
            Array.Copy(segments, 0, parts, 1, segments.Length);
            string path = Path.Combine(parts);
            Assert.IsTrue(File.Exists(path), $"expected shipped source at {path}");
            return File.ReadAllText(path);
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

        /// <summary>
        /// Compiles <paramref name="body"/> as members of a <c>MonoBehaviour</c> and runs the
        /// analyzer over it.
        /// </summary>
        /// <param name="body">Members of a class deriving from <c>UnityEngine.MonoBehaviour</c>.</param>
        /// <returns>Everything the analyzer reported.</returns>
        private static ImmutableArray<Diagnostic> Analyze(string body)
        {
            return AnalyzeSource(
                "using System;\n"
                    + "using System.Collections;\n"
                    + "using System.Collections.Generic;\n"
                    + "using UnityEngine;\n"
                    + "using WallstopStudios.UnityHelpers.Core.Helper;\n"
                    + "using WallstopStudios.UnityHelpers.Tags;\n"
                    + "namespace Consumer { public class Subject : MonoBehaviour { "
                    + body
                    + "\n"
                    + SharedFixtureMembers
                    + " } }\n"
                    + UnityEngineStub
                    + "\n"
                    + PackageHelpers
                    + "\n"
                    + PackageTags
            );
        }

        private static ImmutableArray<Diagnostic> AnalyzeSource(string source)
        {
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
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
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
                    ImmutableArray.Create<DiagnosticAnalyzer>(new DiscardedHandleAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }
    }
}
