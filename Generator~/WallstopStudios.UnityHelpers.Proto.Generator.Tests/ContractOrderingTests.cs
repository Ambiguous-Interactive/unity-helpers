// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using NUnit.Framework;

    [TestFixture]
    public sealed class ContractOrderingTests
    {
        [TestCase(0, 1, false)]
        [TestCase(1, 1, false)]
        [TestCase(16, 1, true)]
        [TestCase(17, 1, true)]
        [TestCase(32, 1, false)]
        [TestCase(256, 1, true)]
        [TestCase(128, 4, false)]
        [TestCase(512, 16, true)]
        public void DepthBucketsPreserveStableContractOrdering(
            int count,
            int branching,
            bool reverse
        )
        {
            StringBuilder source = new StringBuilder(
                "using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto; namespace OrderCases { public class Plain {}"
            );
            for (int index = 0; index < count; ++index)
            {
                source.Append("[WProtoContract] public class C").Append(index);
                if (0 < index)
                {
                    source.Append(" : C").Append((index - 1) / branching);
                }
                else
                {
                    source.Append(" : Plain");
                }
                source.Append(" {}");
            }
            source.Append('}');
            List<MetadataReference> references = new List<MetadataReference>();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            CSharpCompilation compilation = CSharpCompilation.Create(
                "Ordering",
                new[] { CSharpSyntaxTree.ParseText(source.ToString()) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            Assert.IsEmpty(
                compilation
                    .GetDiagnostics()
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            );
            List<INamedTypeSymbol> contracts = new List<INamedTypeSymbol>();
            for (int index = 0; index < count; ++index)
            {
                INamedTypeSymbol contract = compilation.GetTypeByMetadataName(
                    "OrderCases.C" + index
                );
                Assert.IsNotNull(contract);
                contracts.Add(contract);
            }
            if (reverse)
            {
                contracts.Reverse();
            }
            INamedTypeSymbol[] before = contracts.ToArray();
            INamedTypeSymbol[] expected = contracts
                .OrderByDescending(contract =>
                {
                    int depth = 0;
                    for (
                        INamedTypeSymbol current = contract.BaseType;
                        current != null && Shape.IsContract(current);
                        current = current.BaseType
                    )
                    {
                        ++depth;
                    }
                    return depth;
                })
                .ToArray();
            CollectionAssert.AreEqual(expected, WProtoGenerator.DeepestFirst(contracts));
            CollectionAssert.AreEqual(before, contracts);
        }
    }
}
