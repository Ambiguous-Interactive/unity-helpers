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
    public sealed class RelationalFieldAnalyzerTests
    {
        private const string DiagnosticId = "WUH025";
        private const string Stubs =
            @"
namespace UnityEngine {
 public class Object {} public class Component : Object {}
 public class Behaviour : Component {} public class Transform : Component {}
 public class GameObject : Object {} public class ScriptableObject : Object {}
}
namespace WallstopStudios.UnityHelpers.Core.Attributes {
 [System.AttributeUsage(System.AttributeTargets.Field)]
 public abstract class BaseRelationalComponentAttribute : System.Attribute {}
 public class ParentComponentAttribute : BaseRelationalComponentAttribute {}
 public class SiblingComponentAttribute : BaseRelationalComponentAttribute {}
 public class ChildComponentAttribute : BaseRelationalComponentAttribute {}
}
public interface IService {} public interface IGenericService<T> {}
public class DerivedRelationAttribute : WallstopStudios.UnityHelpers.Core.Attributes.ChildComponentAttribute {}
public class Plain {} public struct Value {}
";

        private static IEnumerable<TestCaseData> InvalidCases()
        {
            foreach (
                string relation in new[] { "ParentComponent", "SiblingComponent", "ChildComponent" }
            )
            {
                foreach (
                    string type in new[]
                    {
                        "int",
                        "string",
                        "Value",
                        "Plain",
                        "UnityEngine.GameObject",
                        "UnityEngine.ScriptableObject",
                        "Transform[,]",
                        "Transform[,,]",
                        "Transform[][]",
                        "List<Transform[]>",
                        "HashSet<List<Transform>>",
                        "List<Plain>",
                        "HashSet<int>",
                        "int[]",
                        "List<Transform>[]",
                    }
                )
                {
                    yield return new TestCaseData(relation, type);
                }
            }
        }

        private static ImmutableArray<Diagnostic> Analyze(
            string declaration,
            ReportDiagnostic severity = ReportDiagnostic.Default,
            string extra = "",
            string header = ""
        )
        {
            string source =
                header
                + "\nusing System; using System.Collections.Generic; using UnityEngine; using WallstopStudios.UnityHelpers.Core.Attributes;\n"
                + Stubs
                + extra
                + " public class Subject<T> { "
                + declaration
                + " }";
            System.Reflection.Assembly dynamicAssembly =
                typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly;
            List<MetadataReference> references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(dynamicAssembly.Location),
            };
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (
                    !assembly.IsDynamic
                    && !ReferenceEquals(assembly, dynamicAssembly)
                    && !string.IsNullOrEmpty(assembly.Location)
                )
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            CSharpCompilation compilation = CSharpCompilation.Create(
                "Consumer",
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
                    ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(DiagnosticId, severity)
                )
            );
            Assert.IsEmpty(
                compilation
                    .GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .ToArray(),
                "Fixture must compile"
            );
            return compilation
                .WithAnalyzers(
                    ImmutableArray.Create<DiagnosticAnalyzer>(new RelationalFieldAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }

        [TestCaseSource(nameof(InvalidCases))]
        public void InvalidFieldIsReported(string relation, string type)
        {
            ImmutableArray<Diagnostic> diagnostics = Analyze(
                "[" + relation + "] private " + type + " invalid;"
            );
            Assert.That(diagnostics.Length, Is.EqualTo(1));
            Assert.That(diagnostics[0].Id, Is.EqualTo(DiagnosticId));
            Assert.That(diagnostics[0].GetMessage(), Does.Contain("invalid"));
            Assert.That(diagnostics[0].Location.IsInSource, Is.True);
        }

        [TestCase("Component")]
        [TestCase("Transform")]
        [TestCase("UnityEngine.Object")]
        [TestCase("object")]
        [TestCase("dynamic")]
        [TestCase("IService")]
        [TestCase("IGenericService<Transform>")]
        [TestCase("IEnumerable<Transform>")]
        [TestCase("IList<Transform>")]
        [TestCase("ICollection<Transform>")]
        [TestCase("Transform[]")]
        [TestCase("List<Transform>")]
        [TestCase("HashSet<Transform>")]
        [TestCase("IService[]")]
        [TestCase("List<IService>")]
        [TestCase("HashSet<IService>")]
        [TestCase("List<object>")]
        [TestCase("T")]
        [TestCase("List<T>")]
        public void SupportedOrUnresolvedGenericFieldIsNotReported(string type)
        {
            Assert.IsEmpty(
                Analyze(
                    "[ParentComponent, SiblingComponent, ChildComponent] private "
                        + type
                        + " valid;"
                )
            );
        }

        [Test]
        public void BackingFieldsAndMultipleDeclarationsAreReported()
        {
            Assert.That(
                Analyze("[field: ChildComponent] public int Invalid { get; set; }").Length,
                Is.EqualTo(1)
            );
            Assert.That(
                Analyze("[ParentComponent] private int first, second;").Length,
                Is.EqualTo(2)
            );
        }

        [Test]
        public void DerivedAttributesAndAliasesAreSemantic()
        {
            Assert.That(Analyze("[DerivedRelation] private int invalid;").Length, Is.EqualTo(1));
            Assert.That(
                Analyze(
                    "[global::WallstopStudios.UnityHelpers.Core.Attributes.ChildComponentAttribute] private global::System.Int32 invalid;"
                ).Length,
                Is.EqualTo(1)
            );
            Assert.IsEmpty(
                Analyze(
                    "[ChildComponent] private ConsumerComponent valid;",
                    extra: "public class ConsumerComponent : Transform {}"
                )
            );
            Assert.That(
                Analyze(
                    "[ChildComponent] private ConsumerList<Transform> invalid;",
                    extra: "public class ConsumerList<T> : List<T> {}"
                ).Length,
                Is.EqualTo(1)
            );
        }

        [Test]
        public void UnrelatedAttributesStaticFieldsAndGeneratedCodeAreIgnored()
        {
            Assert.IsEmpty(Analyze("[Obsolete] private int value;"));
            Assert.IsEmpty(Analyze("[ChildComponent] private static int value;"));
            Assert.IsEmpty(
                Analyze("[ChildComponent] private int value;", header: "// <auto-generated/>")
            );
            Assert.IsEmpty(
                Analyze(
                    "[ChildComponent] private int value;",
                    extra: "",
                    header: "#pragma warning disable WUH025"
                )
            );
        }

        [Test]
        public void WarningCanBePromotedToCompilationErrorOrSuppressed()
        {
            const string declaration = "[SiblingComponent] private int invalid;";
            Assert.That(
                Analyze(declaration).Single().Severity,
                Is.EqualTo(DiagnosticSeverity.Warning)
            );
            Assert.That(
                Analyze(declaration, ReportDiagnostic.Error).Single().Severity,
                Is.EqualTo(DiagnosticSeverity.Error)
            );
            Assert.IsEmpty(Analyze(declaration, ReportDiagnostic.Suppress));
        }
    }
}
