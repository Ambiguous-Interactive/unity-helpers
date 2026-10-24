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
    public sealed class EnumHelperAnalyzerTests
    {
        private const string DiagnosticId = "WUH024";
        private const string Helpers =
            @"
namespace WallstopStudios.UnityHelpers.Core.Extension {
 public static class EnumExtensions {
  public static bool HasFlagNoAlloc<T>(this T value, T flag) where T : unmanaged, Enum { return value.HasFlag(flag); }
  public static string ToCachedName<T>(this T value) where T : unmanaged, Enum { return value.ToString(""G""); }
 }}";

        private static ImmutableArray<Diagnostic> Analyze(
            string body,
            ReportDiagnostic severity = ReportDiagnostic.Warn,
            bool helpers = true,
            string path = "Consumer.cs",
            string extra = ""
        )
        {
            string source =
                "using System; [Flags] public enum Flags : ulong { None = 0, A = 1, B = 2, Both = 3, High = 1UL << 63 } public enum Other { A = 1 } public sealed class Custom { public bool HasFlag(Flags flag) { return true; } } public static class Consumer { public static object Run(Flags value, Flags flag, Enum boxed, Custom custom) { "
                + body
                + " } "
                + extra
                + " } "
                + (helpers ? Helpers : string.Empty);
            System.Reflection.Assembly expressions =
                typeof(System.Linq.Expressions.Expression).Assembly;
            List<MetadataReference> references = new()
            {
                MetadataReference.CreateFromFile(expressions.Location),
            };
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (
                    !ReferenceEquals(assembly, expressions)
                    && !assembly.IsDynamic
                    && !string.IsNullOrWhiteSpace(assembly.Location)
                )
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            CSharpCompilation compilation = CSharpCompilation.Create(
                "EnumConsumer",
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        source,
                        new CSharpParseOptions(LanguageVersion.CSharp9),
                        path: path
                    ),
                },
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary
                ).WithSpecificDiagnosticOptions(
                    ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(DiagnosticId, severity)
                )
            );
            Assert.That(
                compilation
                    .GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()),
                Is.Empty,
                "Fixture must compile"
            );
            return compilation
                .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new EnumHelperAnalyzer()))
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }

        [TestCase("return value.HasFlag(Flags.A);", "HasFlagNoAlloc")]
        [TestCase("return value.HasFlag(Flags.None);", "HasFlagNoAlloc")]
        [TestCase("return value.HasFlag(flag);", "HasFlagNoAlloc")]
        [TestCase("return (value & Flags.Both) == Flags.Both;", "HasFlagNoAlloc")]
        [TestCase("return Flags.Both == (Flags.Both & value);", "HasFlagNoAlloc")]
        [TestCase("return (value & Flags.None) == Flags.None;", "HasFlagNoAlloc")]
        [TestCase("return (value & Flags.A) != Flags.None;", "HasFlagNoAlloc")]
        [TestCase("return (value & Flags.High) != 0;", "HasFlagNoAlloc")]
        [TestCase("return 0 != (Flags.A & value);", "HasFlagNoAlloc")]
        [TestCase("return value.ToString();", "ToCachedName")]
        public void SafeEnumOperationsReport(string body, string replacement)
        {
            ImmutableArray<Diagnostic> diagnostics = Analyze(body);
            Assert.That(diagnostics.Length, Is.EqualTo(1));
            Assert.That(diagnostics[0].Id, Is.EqualTo(DiagnosticId));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(diagnostics[0].GetMessage(), Does.Contain(replacement));
        }

        [TestCase("return (value & Flags.Both) != Flags.None;")]
        [TestCase("return (value & Flags.None) != Flags.None;")]
        [TestCase("return (value & Flags.Both) != 0;")]
        [TestCase("return (value & flag) != 0;")]
        [TestCase("return (value & flag) == flag;")]
        [TestCase("return (value & Flags.A) == Flags.B;")]
        [TestCase("return value.HasFlag(Other.A);")]
        [TestCase("return ((Enum)value).HasFlag(flag);")]
        [TestCase("return boxed.HasFlag(flag);")]
        [TestCase("return value.ToString(\"D\");")]
        [TestCase("return 42.ToString();")]
        [TestCase("return custom.HasFlag(flag);")]
        [TestCase(
            "return (System.Linq.Expressions.Expression<Func<bool>>)(() => value.HasFlag(flag));"
        )]
        [TestCase(
            "return (System.Linq.Expressions.Expression<Func<string>>)(() => value.ToString());"
        )]
        public void UnprovenOrDifferentSemanticsDoNotReport(string body)
        {
            Assert.That(Analyze(body), Is.Empty);
        }

        [TestCase("sbyte", "-128")]
        [TestCase("byte", "255")]
        [TestCase("short", "-32768")]
        [TestCase("ushort", "65535")]
        [TestCase("int", "int.MinValue")]
        [TestCase("uint", "uint.MaxValue")]
        [TestCase("long", "long.MinValue")]
        [TestCase("ulong", "ulong.MaxValue")]
        public void EveryEnumWidthSupportsTypedChecks(string underlying, string high)
        {
            string members =
                "public enum WidthFlags : "
                + underlying
                + " { High = "
                + high
                + " } public static bool CheckWidth(WidthFlags value) { return value.HasFlag(WidthFlags.High); }";
            Assert.That(Analyze("return true;", extra: members).Length, Is.EqualTo(1));
        }

        [TestCase("sbyte", "sbyte.MinValue")]
        [TestCase("byte", "128")]
        [TestCase("short", "short.MinValue")]
        [TestCase("ushort", "32768")]
        [TestCase("int", "int.MinValue")]
        [TestCase("uint", "2147483648U")]
        [TestCase("long", "long.MinValue")]
        [TestCase("ulong", "9223372036854775808UL")]
        public void SingleBitChecksIncludeSignedHighBits(string underlying, string high)
        {
            string members =
                "public enum WidthFlags : "
                + underlying
                + " { High = "
                + high
                + " } public static bool CheckWidth(WidthFlags value) { return (value & WidthFlags.High) != 0; }";
            Assert.That(Analyze("return true;", extra: members).Length, Is.EqualTo(1));
        }

        [Test]
        public void RuleIsOffByDefault()
        {
            Assert.That(Analyze("return value.HasFlag(flag);", ReportDiagnostic.Default), Is.Empty);
        }

        [Test]
        public void ExplicitSuppressionIsRespected()
        {
            Assert.That(
                Analyze("return value.HasFlag(flag);", ReportDiagnostic.Suppress),
                Is.Empty
            );
        }

        [Test]
        public void MissingHelpersDoNotReport()
        {
            Assert.That(Analyze("return value.HasFlag(flag);", helpers: false), Is.Empty);
        }

        [Test]
        public void GeneratedCodeIsNotReported()
        {
            Assert.That(Analyze("return value.HasFlag(flag);", path: "Consumer.g.cs"), Is.Empty);
        }

        [Test]
        public void ConstrainedGenericCallsDoNotSuggestInvalidUnmanagedSubstitution()
        {
            Assert.That(
                Analyze(
                    "return Generic(value, flag);",
                    extra: "static bool Generic<T>(T value, T flag) where T : Enum { return value.HasFlag(flag); }"
                ),
                Is.Empty
            );
        }
    }
}
