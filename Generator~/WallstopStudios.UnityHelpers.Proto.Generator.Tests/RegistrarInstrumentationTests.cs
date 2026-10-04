// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using NUnit.Framework;

    /// <summary>
    /// Pins generated registration without test-only instrumentation.
    /// </summary>
    [TestFixture]
    public sealed class RegistrarInstrumentationTests
    {
        private const string ContractSource =
            "using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto; "
            + "namespace Consumer { "
            + "[WProtoContract] public sealed partial class Sample { "
            + "[WProtoMember(1)] public int Value; "
            + "} }";

        private static Compilation Generate(bool includeTests, out string registrarSource)
        {
            CSharpParseOptions parseOptions = CSharpParseOptions.Default.WithLanguageVersion(
                LanguageVersion.Latest
            );
            if (includeTests)
            {
                parseOptions = parseOptions.WithPreprocessorSymbols("UNITY_INCLUDE_TESTS");
            }

            SyntaxTree source = CSharpSyntaxTree.ParseText(ContractSource, parseOptions);
            List<MetadataReference> references = AppDomain
                .CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                .Select(assembly => assembly.Location)
                .Distinct(StringComparer.Ordinal)
                .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
                .ToList();
            CSharpCompilation compilation = CSharpCompilation.Create(
                "RegistrarInstrumentationConsumer" + Guid.NewGuid().ToString("N"),
                new[] { source },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new WProtoGenerator() },
                Array.Empty<AdditionalText>(),
                parseOptions
            );
            driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation generated,
                out ImmutableArray<Diagnostic> diagnostics
            );

            Assert.IsFalse(
                diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                string.Join(
                    Environment.NewLine,
                    diagnostics.Select(diagnostic => diagnostic.ToString())
                )
            );
            SyntaxTree registrar = generated.SyntaxTrees.Single(tree =>
                tree.FilePath.EndsWith("WProtoGeneratedRegistrar.g.cs", StringComparison.Ordinal)
            );
            registrarSource = registrar.GetText().ToString();
            return generated;
        }

        private static void AssertGeneratedCompilationSucceeded(Compilation generated)
        {
            Diagnostic[] errors = generated
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();
            Assert.IsEmpty(
                errors,
                string.Join(Environment.NewLine, errors.Select(diagnostic => diagnostic.ToString()))
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RegistrarOmitsTestOnlyTimingSurface(bool includeTests)
        {
            Compilation generated = Generate(includeTests, out string registrarSource);
            AssertGeneratedCompilationSucceeded(generated);
            StringAssert.Contains(
                "internal static class WProtoGeneratedRegistrar",
                registrarSource
            );
            StringAssert.DoesNotContain("UNITY_INCLUDE_TESTS", registrarSource);
            StringAssert.DoesNotContain("FirstRegistrationElapsedTimestampTicks", registrarSource);
            StringAssert.DoesNotContain("HasRecordedFirstRegistration", registrarSource);
            StringAssert.DoesNotContain("Stopwatch", registrarSource);
        }
    }
}
