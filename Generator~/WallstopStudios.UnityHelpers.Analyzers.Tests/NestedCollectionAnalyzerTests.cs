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
    /// Pins the nesting Unity drops without a message, and -- with more of the suite -- the far
    /// larger set of declarations that merely look like it and must stay quiet.
    /// </summary>
    /// <remarks>
    /// The negatives carry the weight. The whole point of resolving the nesting through the
    /// symbol's serialized fields rather than matching its syntax is that
    /// <c>SerializableDictionary&lt;string, SerializableList&lt;int&gt;&gt;</c> and
    /// <c>SerializableDictionary&lt;string, List&lt;int&gt;&gt;</c> differ by one identifier and by
    /// whether the asset saves anything at all (#548).
    /// </remarks>
    [TestFixture]
    public sealed class NestedCollectionAnalyzerTests
    {
        private const string DiagnosticId = "WUH002";

        /// <summary>
        /// Just enough of UnityEngine for the gate this analyzer applies, declared here so the
        /// fixtures are hermetic.
        /// </summary>
        private const string UnityStub =
            @"namespace UnityEngine
              {
                  using System;

                  [AttributeUsage(AttributeTargets.Field)]
                  public sealed class SerializeField : Attribute { }

                  public class Object { }
                  public class Component : Object { }
                  public class MonoBehaviour : Component { }
                  public class ScriptableObject : Object { }
                  public sealed class Sprite : Object { }
              }";

        /// <summary>
        /// The adapters, reduced to the fields Unity actually serializes.
        /// </summary>
        /// <remarks>
        /// The generic plumbing is reproduced exactly where it matters and nowhere else: the
        /// two-argument dictionary passes its value type through as the array element, which is the
        /// substitution that makes the nesting invisible at the declaration.
        /// <see cref="TheStubbedAdaptersMatchTheOnesThePackageShips"/> reads the real sources and
        /// fails when that stops being true.
        /// </remarks>
        private const string PackageAdapters =
            @"namespace WallstopStudios.UnityHelpers.Core.DataStructure.Adapters
              {
                  using System;
                  using System.Collections.Generic;
                  using UnityEngine;

                  [Serializable]
                  public abstract class SerializableDictionaryBase<TKey, TValue, TValueCache>
                  {
                      [SerializeField] protected internal TKey[] _keys;
                      [SerializeField] protected internal TValueCache[] _values;
                  }

                  [Serializable]
                  public class SerializableDictionary<TKey, TValue>
                      : SerializableDictionaryBase<TKey, TValue, TValue> { }

                  [Serializable]
                  public class SerializableHashSet<T>
                  {
                      [SerializeField] protected internal T[] _items;
                  }

                  [Serializable]
                  public sealed class SerializableList<T>
                  {
                      [SerializeField] private List<T> _items = new List<T>();
                  }
              }";

        [Test]
        public void ADictionaryWithACollectionValueIsReported()
        {
            Diagnostic reported = Single(
                @"[SerializeField] private SerializableDictionary<string, List<int>> _map;"
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
            string message = reported.GetMessage();
            StringAssert.Contains("_map", message);
            StringAssert.Contains("List<int>", message);
            StringAssert.Contains("SerializableList", message);
        }

        [TestCase("List<List<int>>", TestName = "AListOfLists")]
        [TestCase("int[][]", TestName = "AJaggedArray")]
        [TestCase("List<int>[]", TestName = "AnArrayOfLists")]
        [TestCase("List<int[]>", TestName = "AListOfArrays")]
        [TestCase("SerializableHashSet<List<int>>", TestName = "ASetOfLists")]
        [TestCase("SerializableList<int[]>", TestName = "AWrappedListOfArrays")]
        [TestCase(
            "SerializableDictionary<List<int>, string>",
            TestName = "ADictionaryWithACollectionKey"
        )]
        [TestCase(
            "SerializableDictionary<string, SerializableDictionary<string, List<int>>>",
            TestName = "NestingReachedThroughTwoAdapters"
        )]
        public void EveryShapeUnityFlattensIsReported(string declaredType)
        {
            Diagnostic reported = Single($"[SerializeField] private {declaredType} _field;");

            Assert.AreEqual(DiagnosticId, reported.Id);
            StringAssert.Contains("_field", reported.GetMessage());
        }

        /// <summary>
        /// The wrapper the package ships for exactly this, and the ordinary declarations that
        /// resemble the broken one closely enough to be worth pinning.
        /// </summary>
        [TestCase(
            "SerializableDictionary<string, SerializableList<int>> _map;",
            TestName = "TheWrapperThatFixesIt"
        )]
        [TestCase("SerializableDictionary<string, int> _map;", TestName = "AScalarValue")]
        [TestCase("SerializableHashSet<string> _set;", TestName = "ASetOfScalars")]
        [TestCase("List<int> _list;", TestName = "APlainList")]
        [TestCase("string[] _names;", TestName = "AnArrayOfStrings")]
        [TestCase("int[,] _grid;", TestName = "AMultiDimensionalArray")]
        [TestCase("List<Sprite> _sprites;", TestName = "AListOfUnityObjects")]
        [TestCase(
            "SerializableDictionary<string, Sprite> _byName;",
            TestName = "ADictionaryOfUnityObjects"
        )]
        public void AnOrdinaryDeclarationIsNotReported(string declaration)
        {
            Assert.IsEmpty(Analyze($"[SerializeField] private {declaration}"));
        }

        /// <summary>
        /// The gate that keeps this off code Unity never serializes.
        /// </summary>
        /// <remarks>
        /// A plain algorithm's <c>List&lt;List&lt;int&gt;&gt;</c> is not a serialization bug, and
        /// reporting it would be simply wrong. Public is decisive only where Unity itself does the
        /// instantiating, which is why the positive here needs a <c>MonoBehaviour</c>.
        /// </remarks>
        [Test]
        public void OnlyFieldsUnityWillSerializeAreReported()
        {
            Assert.IsEmpty(
                Analyze("private List<List<int>> _grid;"),
                "a private field with no [SerializeField] never reaches Unity's serializer"
            );
            Assert.IsEmpty(
                Analyze("[SerializeField] private static List<List<int>> Shared;"),
                "Unity serializes no static field"
            );
            Assert.IsEmpty(
                Analyze("[NonSerialized] public List<List<int>> Grid;"),
                "[NonSerialized] is the author saying so outright"
            );
            Assert.IsEmpty(
                AnalyzeInPlainClass("public List<List<int>> Grid;"),
                "a public field on a plain class may never reach Unity at all"
            );
            Assert.IsNotEmpty(
                Analyze("public List<List<int>> Grid;"),
                "but a public field on a MonoBehaviour certainly does"
            );
        }

        /// <summary>
        /// A public field of a nested <c>[Serializable]</c> type is serialized, and must be walked.
        /// </summary>
        /// <remarks>
        /// A DTO written the ordinary way -- <c>[Serializable]</c> with public fields and no
        /// <c>[SerializeField]</c> anywhere -- is exactly what a dictionary value or a list element
        /// usually is. Gating public fields on "the containing type derives from UnityEngine.Object"
        /// is right at a top-level declaration and wrong one step in, where the walk has already
        /// established that Unity is serializing the type.
        /// </remarks>
        [Test]
        public void APublicFieldOfANestedSerializableTypeIsReported()
        {
            Diagnostic reported = Single(
                @"[Serializable] public sealed class Dto { public List<List<int>> rows; }
                  [SerializeField] private Dto _dto;"
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
            StringAssert.Contains("_dto", reported.GetMessage());
        }

        [TestCase(
            "SerializableDictionary<string, Dto> _byName;",
            TestName = "ThroughADictionaryValue"
        )]
        [TestCase("SerializableList<Dto> _all;", TestName = "ThroughTheListWrapper")]
        [TestCase("Dto[] _many;", TestName = "ThroughAnArray")]
        [TestCase("List<Dto> _list;", TestName = "ThroughAList")]
        public void NestingInsideAPublicFieldOfADtoIsFoundThroughEveryContainer(string declaration)
        {
            Diagnostic reported = Single(
                @"[Serializable] public sealed class Dto { public List<List<int>> rows; }
                  [SerializeField] private " + declaration
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
        }

        /// <summary>
        /// The relaxation must not leak back out to a top-level public field on a plain class.
        /// </summary>
        [Test]
        public void APublicFieldOfAPlainClassIsStillNotReported()
        {
            Assert.IsEmpty(
                AnalyzeInPlainClass("public List<List<int>> Grid;"),
                "a plain class's public field may never reach Unity's serializer"
            );
            Assert.IsEmpty(
                Analyze(
                    @"[Serializable] public sealed class Dto { public List<List<int>> rows; }
                      [NonSerialized] public Dto Skipped;"
                ),
                "and [NonSerialized] still wins, one level in or not"
            );
        }

        /// <summary>
        /// A type that contains itself must not send the walk round forever.
        /// </summary>
        [Test]
        public void ARecursiveTypeTerminates()
        {
            Assert.IsEmpty(
                Analyze(
                    @"[Serializable] public sealed class Node { [SerializeField] public List<Node> children; }
                      [SerializeField] private List<Node> _roots;"
                )
            );
            Assert.IsNotEmpty(
                Analyze(
                    @"[Serializable] public sealed class Bad { [SerializeField] public List<List<int>> rows; }
                      [SerializeField] private Bad _bad;"
                ),
                "and it must still find the nesting a recursive type reaches"
            );
        }

        /// <summary>
        /// The consumer contract: on by default, so a project that has taken on this package gets
        /// the safety without discovering it, but never able to fail their build.
        /// </summary>
        [Test]
        public void TheDiagnosticIsOnByDefaultSuppressibleAndNeverAboveAWarning()
        {
            DiagnosticDescriptor descriptor =
                new NestedCollectionAnalyzer().SupportedDiagnostics.Single(candidate =>
                    candidate.Id == DiagnosticId
                );

            Assert.IsTrue(
                descriptor.IsEnabledByDefault,
                "a consumer using this package should get the safety without asking for it"
            );
            Assert.AreEqual(
                DiagnosticSeverity.Warning,
                descriptor.DefaultSeverity,
                "a warning is the ceiling; an error would fail a build on a package upgrade"
            );

            const string offending =
                "[SerializeField] private SerializableDictionary<string, List<int>> _map;";

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
        /// The stubs above stand in for real shipped code, so they have to keep standing for it.
        /// </summary>
        /// <remarks>
        /// Every positive here runs against the stub. If the shipped dictionary stopped passing its
        /// value type through to the serialized array, or the wrapper stopped holding a
        /// <c>List&lt;T&gt;</c>, this suite would keep passing while the analyzer covered nothing.
        /// </remarks>
        [Test]
        public void TheStubbedAdaptersMatchTheOnesThePackageShips()
        {
            string adapters = Path.Combine(
                FindRepositoryRoot(),
                "Runtime",
                "Core",
                "DataStructure",
                "Adapters"
            );

            string dictionary = ReadShipped(adapters, "SerializableDictionary.cs");
            StringAssert.Contains(
                "protected internal TKey[] _keys",
                dictionary,
                "the key array is what makes a collection-typed key nest"
            );
            StringAssert.Contains(
                "protected internal TValueCache[] _values",
                dictionary,
                "the value array is what makes a collection-typed value nest"
            );
            StringAssert.Contains(
                "SerializableDictionaryBase<TKey, TValue, TValue>",
                dictionary,
                "the two-argument form passing TValue through as the cache is the whole substitution"
            );

            StringAssert.Contains(
                "protected internal T[] _items",
                ReadShipped(adapters, "SerializableHashSet.cs"),
                "the set's element array is what makes a collection-typed element nest"
            );
            StringAssert.Contains(
                "private List<T> _items",
                ReadShipped(adapters, "SerializableList.cs"),
                "the wrapper's whole job is to hold the List the outer collection may not"
            );
        }

        private static string ReadShipped(string directory, string fileName)
        {
            string path = Path.Combine(directory, fileName);
            Assert.IsTrue(File.Exists(path), $"expected the shipped adapter at {path}");
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

        private static ImmutableArray<Diagnostic> Analyze(string body)
        {
            return Analyze(body, ReportDiagnostic.Default);
        }

        private static ImmutableArray<Diagnostic> Analyze(string body, ReportDiagnostic reportedAs)
        {
            return Analyze(body, reportedAs, "UnityEngine.MonoBehaviour");
        }

        private static ImmutableArray<Diagnostic> AnalyzeInPlainClass(string body)
        {
            return Analyze(body, ReportDiagnostic.Default, null);
        }

        /// <summary>
        /// Compiles <paramref name="body"/> and runs the analyzer over it.
        /// </summary>
        /// <param name="body">Members of a class in namespace <c>Consumer</c>.</param>
        /// <param name="reportedAs">
        /// What the compilation says about the diagnostic -- <see cref="ReportDiagnostic.Default"/>
        /// for a consumer who configures nothing, or anything else for the ruleset /
        /// <c>.editorconfig</c> entry they would write.
        /// </param>
        /// <param name="baseType">Base of the containing class, or null for a plain class.</param>
        /// <returns>Everything the analyzer reported.</returns>
        private static ImmutableArray<Diagnostic> Analyze(
            string body,
            ReportDiagnostic reportedAs,
            string baseType
        )
        {
            string declaration =
                baseType == null ? "public class Subject" : $"public class Subject : {baseType}";
            string source =
                "using System;\n"
                + "using System.Collections.Generic;\n"
                + "using UnityEngine;\n"
                + "using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;\n"
                + "namespace Consumer { "
                + declaration
                + " { "
                + body
                + " } }\n"
                + UnityStub
                + "\n"
                + PackageAdapters;

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
                    ImmutableArray.Create<DiagnosticAnalyzer>(new NestedCollectionAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }
    }
}
