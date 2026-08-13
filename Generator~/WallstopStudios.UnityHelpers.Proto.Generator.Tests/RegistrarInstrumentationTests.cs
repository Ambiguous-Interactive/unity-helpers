// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Emit;
    using NUnit.Framework;

    /// <summary>
    /// Pins the generated registrar's compile-time name and test-only first-invocation latch.
    /// </summary>
    [TestFixture]
    public sealed class RegistrarInstrumentationTests
    {
        private const string RegistrarName =
            "WallstopStudios.UnityHelpers.Generated.WProtoGeneratedRegistrar";

        private const string ContractSource =
            "using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto; "
            + "namespace Consumer { "
            + "[WProtoContract] public sealed partial class Sample { "
            + "[WProtoMember(1)] public int Value; "
            + "} }";

        [Test]
        public void ProductionRegistrarOmitsTestOnlyTimingSurface()
        {
            Compilation generated = Generate(includeTests: false, out string registrarSource);

            StringAssert.Contains("#if UNITY_INCLUDE_TESTS", registrarSource);
            AssertGeneratedCompilationSucceeded(generated);

            // REFLECTION REQUIRED: the registrar exists only in the synthetic assembly emitted by
            // this test, so the test assembly cannot reference its type or members at compile time.
            Assembly assembly = EmitAndLoad(generated);
            Type registrar = assembly.GetType(RegistrarName, throwOnError: true);
            const BindingFlags StaticInternal = BindingFlags.Static | BindingFlags.NonPublic;
            Assert.IsTrue(
                registrar.GetProperty("FirstRegistrationElapsedTimestampTicks", StaticInternal)
                    == null
            );
            Assert.IsTrue(
                registrar.GetProperty("HasRecordedFirstRegistration", StaticInternal) == null
            );
        }

        [Test]
        public void TestRegistrarRecordsOnlyItsFirstInvocation()
        {
            Compilation generated = Generate(includeTests: true, out string registrarSource);

            StringAssert.Contains(
                "internal static class WProtoGeneratedRegistrar",
                registrarSource
            );
            StringAssert.Contains("FirstRegistrationElapsedTimestampTicks", registrarSource);
            StringAssert.Contains("HasRecordedFirstRegistration", registrarSource);

            // REFLECTION REQUIRED: the registrar exists only in the synthetic assembly emitted by
            // this test, so the test assembly cannot reference its type or members at compile time.
            Assembly assembly = EmitAndLoad(generated);
            Type registrar = assembly.GetType(RegistrarName, throwOnError: true);
            const BindingFlags StaticInternal = BindingFlags.Static | BindingFlags.NonPublic;
            MethodInfo register = registrar.GetMethod("Register", StaticInternal);
            PropertyInfo hasRecorded = registrar.GetProperty(
                "HasRecordedFirstRegistration",
                StaticInternal
            );
            PropertyInfo elapsedTicks = registrar.GetProperty(
                "FirstRegistrationElapsedTimestampTicks",
                StaticInternal
            );

            Assert.IsTrue((bool)hasRecorded.GetValue(null));
            long firstElapsed = (long)elapsedTicks.GetValue(null);
            Assert.GreaterOrEqual(firstElapsed, 0);

            register.Invoke(null, null);
            long afterSecondInvocation = (long)elapsedTicks.GetValue(null);
            register.Invoke(null, null);
            long afterThirdInvocation = (long)elapsedTicks.GetValue(null);

            Assert.AreEqual(firstElapsed, afterSecondInvocation);
            Assert.AreEqual(firstElapsed, afterThirdInvocation);
        }

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

        private static Assembly EmitAndLoad(Compilation generated)
        {
            using MemoryStream assemblyBytes = new MemoryStream();
            EmitResult emitted = generated.Emit(assemblyBytes);
            Assert.IsTrue(
                emitted.Success,
                string.Join(
                    Environment.NewLine,
                    emitted.Diagnostics.Select(diagnostic => diagnostic.ToString())
                )
            );
            return Assembly.Load(assemblyBytes.ToArray());
        }
    }
}
