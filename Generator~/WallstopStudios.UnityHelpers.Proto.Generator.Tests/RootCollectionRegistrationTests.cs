// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using NUnit.Framework;

    [TestFixture]
    public sealed class RootCollectionRegistrationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ImplicitArrayConstructionsExposeTheirClosedRootTypesInSourceOrder(bool parallel)
        {
            CSharpCompilation compilation = CSharpCompilation.Create(
                nameof(ImplicitArrayConstructionsExposeTheirClosedRootTypesInSourceOrder),
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        "public sealed class Consumer { public object Value = new[] { 1, 2 }; }"
                    ),
                    CSharpSyntaxTree.ParseText(
                        "public sealed class OtherConsumer { public object Value = new[] { 1.0, 2.0 }; }"
                    ),
                },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            List<IArrayTypeSymbol> arrays = new List<IArrayTypeSymbol>();
            ClosureScan.Types(
                compilation,
                new Dictionary<SyntaxTree, SemanticModel>(),
                arrays,
                parallel
            );
            Assert.AreEqual(2, arrays.Count);
            Assert.AreEqual(SpecialType.System_Int32, arrays[0].ElementType.SpecialType);
            Assert.AreEqual(SpecialType.System_Double, arrays[1].ElementType.SpecialType);
        }

        [Test]
        public void AssembliesWithoutTheRuntimeDoNotReceiveCollectionCodecs()
        {
            CSharpCompilation compilation = CSharpCompilation.Create(
                nameof(AssembliesWithoutTheRuntimeDoNotReceiveCollectionCodecs),
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        "public sealed class Consumer { public System.Collections.Generic.List<int> Items; public int[] Array; }"
                    ),
                },
                new[] { MetadataReference.CreateFromFile(typeof(List<int>).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new WProtoGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation updated,
                out System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics
            );
            Assert.IsEmpty(diagnostics);
            foreach (Diagnostic diagnostic in updated.GetDiagnostics())
            {
                Assert.AreNotEqual(
                    DiagnosticSeverity.Error,
                    diagnostic.Severity,
                    diagnostic.ToString()
                );
            }
            foreach (SyntaxTree tree in driver.GetRunResult().GeneratedTrees)
            {
                Assert.IsFalse(
                    tree.FilePath.Contains(
                        "WProtoGeneratedCollectionRoots",
                        StringComparison.Ordinal
                    )
                );
            }
        }
    }
}
