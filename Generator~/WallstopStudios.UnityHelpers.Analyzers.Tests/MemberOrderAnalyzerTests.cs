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
    public sealed class MemberOrderAnalyzerTests
    {
        private const string DiagnosticId = "WUH029";
        private static readonly string[] OrderedMembers =
        {
            "public const int Constant = 1;",
            "public event Action Changed;",
            "public delegate void Callback();",
            "public static int StaticProperty { get; }",
            "public static int staticField;",
            "public int Property { get; }",
            "public int field;",
            "public Subject() { }",
            "public static void StaticMethod() { }",
            "public void Method() { }",
        };

        private static IEnumerable<TestCaseData> TierPairs()
        {
            for (int first = 0; first < OrderedMembers.Length; ++first)
            {
                for (int second = first + 1; second < OrderedMembers.Length; ++second)
                {
                    yield return new TestCaseData(
                        OrderedMembers[first],
                        OrderedMembers[second],
                        0
                    ).SetName($"MemberOrder.Tier{first}.BeforeTier{second}");
                    yield return new TestCaseData(
                        OrderedMembers[second],
                        OrderedMembers[first],
                        1
                    ).SetName($"MemberOrder.Tier{second}.BeforeTier{first}");
                }
            }
        }

        private static ImmutableArray<Diagnostic> Analyze(
            string source,
            ReportDiagnostic reporting = ReportDiagnostic.Warn,
            string file = "Consumer.cs",
            string symbol = "ACTIVE"
        )
        {
            List<MetadataReference> references = new List<MetadataReference>();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            CSharpParseOptions parsing = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols: new[] { symbol }
            );
            CSharpCompilationOptions options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithSpecificDiagnosticOptions(
                ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(DiagnosticId, reporting)
            );
            SyntaxTree tree = CSharpSyntaxTree.ParseText("using System;\n" + source, parsing, file);
            CSharpCompilation compilation = CSharpCompilation.Create(
                "MemberOrderConsumer",
                new[] { tree },
                references,
                options
            );
            Assert.That(
                compilation
                    .GetDiagnostics()
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                Is.Empty
            );
            return compilation
                .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MemberOrderAnalyzer()))
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }

        [TestCaseSource(nameof(TierPairs))]
        public void EveryTierPairPreservesThePublishedOrdering(
            string first,
            string second,
            int expected
        )
        {
            string source = "public class Subject { " + first + second + " }";
            Assert.That(Analyze(source).Length, Is.EqualTo(expected));
        }

        [TestCase("public int A; protected int B; internal int C; private int D; int E;", 0)]
        [TestCase("private int A; public int B;", 1)]
        [TestCase("internal const int A=0; protected const int B=0;", 1)]
        [TestCase("public int A, B; public int Property => A+B;", 1)]
        [TestCase("private class Nested { } public int Value;", 1)]
        [TestCase("private class Nested { } private struct Other { }", 0)]
        [TestCase("public delegate void Callback(); public static int Value;", 0)]
        [TestCase("public int Value; public delegate void Callback();", 1)]
        [TestCase("public int this[int index] => index; private int value;", 0)]
        [TestCase("public int Value => value; private int value; ~Subject() { }", 0)]
        [TestCase(
            "public static Subject operator +(Subject left, Subject right) { return left; } public int Value;",
            1
        )]
        [TestCase(
            "[Obsolete(\"}; public int Noise;\")] public void Run() { } public int Value => 0;",
            1
        )]
        public void AccessibilityAndMemberShapesHaveExplicitControls(string body, int expected)
        {
            Assert.That(
                Analyze("public class Subject { " + body + " }").Length,
                Is.EqualTo(expected)
            );
        }

        [TestCase("public struct Subject { public void Run() { } public int Value; }", 1)]
        [TestCase("public record Subject { public void Run() { } public int Value; }", 1)]
        [TestCase("public interface Subject { void Run(); int Value { get; } }", 1)]
        [TestCase(
            "public partial class Subject { private int value; } public partial class Subject { public int Value => value; }",
            0
        )]
        [TestCase(
            "public class Outer { public class Subject { public void Run() { } public int Value; } }",
            1
        )]
        public void EachTypeDeclarationIsCheckedIndependently(string source, int expected)
        {
            Assert.That(Analyze(source).Length, Is.EqualTo(expected));
        }

        [TestCase(
            "private int value;\n#if ACTIVE\npublic int Value => value;\n#endif\npublic void Run() { }",
            "ACTIVE",
            0
        )]
        [TestCase(
            "private int value;\n#if ACTIVE\npublic int Value => value;\n#endif\npublic void Run() { }",
            "INACTIVE",
            0
        )]
        [TestCase("#if ACTIVE\nprivate int value; public int Value => value;\n#endif", "ACTIVE", 1)]
        [TestCase(
            "public void Run() {\n#if ACTIVE\nint value=0;\n#endif\n}\npublic int Value => 0;",
            "ACTIVE",
            1
        )]
        [TestCase(
            "#if ACTIVE\nprivate int value;\n#else\npublic int Value => 0;\n#endif",
            "ACTIVE",
            0
        )]
        [TestCase("#if ACTIVE\nprivate class Nested { }\n#endif\npublic int Value;", "ACTIVE", 1)]
        public void ConditionalRegionsPreserveTheirSeparateOrdering(
            string body,
            string symbol,
            int expected
        )
        {
            string source = "public class Subject {\n" + body + "\n}";
            Assert.That(Analyze(source, symbol: symbol).Length, Is.EqualTo(expected));
        }

        [Test]
        public void ThePolicyIsOptInAndSuppressible()
        {
            const string source =
                "public class Subject { private int value; public int Value => value; }";
            Assert.That(Analyze(source, ReportDiagnostic.Default), Is.Empty);
            Assert.That(Analyze(source, ReportDiagnostic.Suppress), Is.Empty);
            ImmutableArray<Diagnostic> enabled = Analyze(source);
            Assert.That(enabled.Length, Is.EqualTo(1));
            Assert.That(enabled[0].Id, Is.EqualTo(DiagnosticId));
            Assert.That(enabled[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(Analyze("#pragma warning disable WUH029\n" + source), Is.Empty);
            Assert.That(Analyze(source, file: "Consumer.g.cs"), Is.Empty);
        }
    }
}
