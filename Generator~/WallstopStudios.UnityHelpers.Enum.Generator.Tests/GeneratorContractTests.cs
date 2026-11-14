// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Enum.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using System.Text;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using NUnit.Framework;

    [TestFixture]
    public sealed class GeneratorContractTests
    {
        private const string ConcreteConsumer =
            "namespace Fixture { using WallstopStudios.UnityHelpers.Core.Attributes; using WallstopStudios.UnityHelpers.Core.Extension; [WEnumHelpers] public enum E { None = 0, First = 1 } public static class Probe { public static string Get(E value) { return value.ToCachedName(); } } }";

        private const string GenericConsumer =
            "namespace Fixture { using System; using WallstopStudios.UnityHelpers.Core.Attributes; using WallstopStudios.UnityHelpers.Core.Extension; [WEnumHelpers] public enum E { None = 0, First = 1 } public static class Probe { public static string Get<T>(T value) where T : unmanaged, Enum { return value.ToCachedName(); } } }";

        private const string NoAttribute =
            "namespace Fixture { using WallstopStudios.UnityHelpers.Core.Extension; public enum E { None = 0, First = 1 } public static class Probe { public static string Get(E value) { return value.ToCachedName(); } } }";

        private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp9);

        private static bool HasDiagnostic(ImmutableArray<Diagnostic> diagnostics, string id)
        {
            foreach (Diagnostic diagnostic in diagnostics)
            {
                if (string.Equals(diagnostic.Id, id, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static IMethodSymbol FindInvocationTarget(Compilation compilation, string name)
        {
            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
                {
                    if (
                        node is InvocationExpressionSyntax invocation
                        && invocation.Expression is MemberAccessExpressionSyntax access
                        && string.Equals(
                            access.Name.Identifier.ValueText,
                            name,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        SymbolInfo info = model.GetSymbolInfo(invocation);
                        return info.Symbol as IMethodSymbol;
                    }
                }
            }
            return null;
        }

        private static RunResult Run(string source)
        {
            CSharpCompilation compilation = CreateCompilation(source);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new EnumHelpersGenerator() },
                parseOptions: ParseOptions
            );
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation updated,
                out ImmutableArray<Diagnostic> diagnostics
            );
            GeneratorDriverRunResult runResult = driver.GetRunResult();
            List<string> errors = new();
            foreach (Diagnostic diagnostic in updated.GetDiagnostics())
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    errors.Add(diagnostic.ToString());
                }
            }
            StringBuilder text = new();
            foreach (SyntaxTree tree in runResult.GeneratedTrees)
            {
                text.Append(tree.GetText().ToString());
            }
            return new RunResult(
                updated,
                diagnostics,
                runResult.GeneratedTrees,
                text.ToString(),
                errors
            );
        }

        private static CSharpCompilation CreateCompilation(string source)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source, ParseOptions, "Fixture.cs");
            List<MetadataReference> references = new();
            string platform = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            foreach (string path in platform.Split(Path.PathSeparator))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            references.Add(
                MetadataReference.CreateFromFile(typeof(GeneratorContractTests).Assembly.Location)
            );
            return CSharpCompilation.Create(
                "FixtureAssembly",
                new[] { tree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
        }

        [Test]
        public void ConcreteCallBindsToTheGeneratedOverload()
        {
            RunResult result = Run(ConcreteConsumer);
            Assert.That(result.Errors, Is.Empty);
            IMethodSymbol target = FindInvocationTarget(result.Updated, "ToCachedName");
            Assert.That(target, Is.Not.Null);
            Assert.That(target.ContainingType.Name, Is.EqualTo("EWEnumHelpers"));
        }

        [Test]
        public void ConstrainedGenericCallBindsToTheGeneralizedFallback()
        {
            RunResult result = Run(GenericConsumer);
            Assert.That(result.Errors, Is.Empty);
            IMethodSymbol target = FindInvocationTarget(result.Updated, "ToCachedName");
            Assert.That(target, Is.Not.Null);
            Assert.That(target.ContainingType.Name, Is.EqualTo("EnumExtensions"));
        }

        [Test]
        public void AnEnumWithoutTheAttributeProducesNoSource()
        {
            RunResult result = Run(NoAttribute);
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.GeneratedTrees, Is.Empty);
        }

        [Test]
        public void GenerationIsDeterministic()
        {
            RunResult first = Run(ConcreteConsumer);
            RunResult second = Run(ConcreteConsumer);
            Assert.That(first.Errors, Is.Empty);
            Assert.That(second.Errors, Is.Empty);
            Assert.That(first.GeneratedText, Is.EqualTo(second.GeneratedText));
        }

        [Test]
        public void GlobalNamespaceEnumGeneratesAtGlobalScope()
        {
            const string source =
                "using WallstopStudios.UnityHelpers.Core.Attributes; [WEnumHelpers] public enum GlobalE { None = 0, First = 1 }";
            RunResult result = Run(source);
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.GeneratedText, Does.Contain("class GlobalEWEnumHelpers"));
        }

        [Test]
        public void ObsoleteErrorEnumIsReportedAndSkipped()
        {
            const string source =
                "namespace Fixture { using System; using WallstopStudios.UnityHelpers.Core.Attributes; [Obsolete(\"retired\", true)] [WEnumHelpers] public enum Retired { None = 0, Value = 1 } }";
            RunResult result = Run(source);
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.GeneratedTrees, Is.Empty);
            Assert.That(HasDiagnostic(result.Diagnostics, "WENUM001"), Is.True);
        }

        [Test]
        public void DenseAndSparseEnumsChooseTheirOwnLookupStrategy()
        {
            const string source =
                "namespace Fixture { using WallstopStudios.UnityHelpers.Core.Attributes; [WEnumHelpers] public enum Dense { None = 0, A = 1, B = 2 } [WEnumHelpers] public enum Wide : ulong { A = 1, B = 256, C = 65536, D = 16777216, E = 4294967296, F = 1099511627776, G = 281474976710656, H = 9223372036854775808 } [WEnumHelpers] public enum Sparse { V00 = 0, V01 = 100000, V02 = 200000, V03 = 300000, V04 = 400000, V05 = 500000, V06 = 600000, V07 = 700000, V08 = 800000, V09 = 900000, V10 = 1000000, V11 = 1100000, V12 = 1200000, V13 = 1300000, V14 = 1400000, V15 = 1500000, V16 = 1600000, V17 = 1700000 } }";
            RunResult result = Run(source);
            Assert.That(result.Errors, Is.Empty);
            string text = result.GeneratedText;
            Assert.That(text, Does.Contain("new string[3]"));
            Assert.That(text, Does.Contain("switch (key)"));
            Assert.That(text, Does.Contain("Dictionary<ulong, string>"));
        }

        [Test]
        public void AliasValuesAreLeftToTheGeneralizedPath()
        {
            const string source =
                "namespace Fixture { using WallstopStudios.UnityHelpers.Core.Attributes; [WEnumHelpers] public enum Aliased { None = 0, First = 1, Alias = 1 } }";
            RunResult result = Run(source);
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.GeneratedText, Does.Contain("new string[1]"));
        }

        [Test]
        public void EnumNestedInAGenericTypeIsReportedAndSkipped()
        {
            const string source =
                "namespace Fixture { using WallstopStudios.UnityHelpers.Core.Attributes; public static class Holder<T> { [WEnumHelpers] public enum Nested { None = 0, Value = 1 } } }";
            RunResult result = Run(source);
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.GeneratedTrees, Is.Empty);
            Assert.That(HasDiagnostic(result.Diagnostics, "WENUM001"), Is.True);
        }

        [Test]
        public void PrivateNestedEnumIsReportedAndSkipped()
        {
            const string source =
                "namespace Fixture { using WallstopStudios.UnityHelpers.Core.Attributes; public static class Holder { [WEnumHelpers] private enum Nested { None = 0, Value = 1 } } }";
            RunResult result = Run(source);
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.GeneratedTrees, Is.Empty);
            Assert.That(HasDiagnostic(result.Diagnostics, "WENUM001"), Is.True);
        }

        [Test]
        public void NestedPublicEnumInInternalHolderGetsAnInternalOverload()
        {
            const string source =
                "namespace Fixture { using WallstopStudios.UnityHelpers.Core.Attributes; internal static class Holder { [WEnumHelpers] public enum Nested { None = 0, Value = 1 } } }";
            RunResult result = Run(source);
            Assert.That(result.Errors, Is.Empty);
            Assert.That(
                result.GeneratedText,
                Does.Contain("internal static class Holder_NestedWEnumHelpers")
            );
        }

        private readonly struct RunResult
        {
            public readonly Compilation Updated;
            public readonly ImmutableArray<Diagnostic> Diagnostics;
            public readonly ImmutableArray<SyntaxTree> GeneratedTrees;
            public readonly string GeneratedText;
            public readonly List<string> Errors;

            public RunResult(
                Compilation updated,
                ImmutableArray<Diagnostic> diagnostics,
                ImmutableArray<SyntaxTree> generatedTrees,
                string generatedText,
                List<string> errors
            )
            {
                Updated = updated;
                Diagnostics = diagnostics;
                GeneratedTrees = generatedTrees;
                GeneratedText = generatedText;
                Errors = errors;
            }
        }
    }
}
