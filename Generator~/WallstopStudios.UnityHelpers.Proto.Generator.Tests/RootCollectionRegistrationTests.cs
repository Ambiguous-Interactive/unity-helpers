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
        [Test]
        public void ImplicitArrayConstructionsExposeTheirClosedRootType()
        {
            CSharpCompilation compilation = CSharpCompilation.Create(
                nameof(ImplicitArrayConstructionsExposeTheirClosedRootType),
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        "public sealed class Consumer { public object Value = new[] { 1, 2 }; }"
                    ),
                },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            List<IArrayTypeSymbol> arrays = new List<IArrayTypeSymbol>();
            ClosureScan.Types(compilation, new Dictionary<SyntaxTree, SemanticModel>(), arrays);
            Assert.AreEqual(1, arrays.Count);
            Assert.AreEqual(SpecialType.System_Int32, arrays[0].ElementType.SpecialType);
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
