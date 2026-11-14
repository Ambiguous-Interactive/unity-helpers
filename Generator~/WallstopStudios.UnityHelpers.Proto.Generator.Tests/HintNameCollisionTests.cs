// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Proto.Generator;

    [TestFixture]
    public sealed class HintNameCollisionTests
    {
        [Test]
        public void SanitizedHintNameCollisionsAreDisambiguated()
        {
            const string source =
                "using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;\n"
                + "namespace Consumer { [WProtoContract] public partial class A_B { [WProtoMember(1)] public int Value; } public partial class A { [WProtoContract] public partial class B { [WProtoMember(1)] public int Value; } } [WProtoContract] public partial class C_D { [WProtoMember(1)] public int Value; } public partial class C { [WProtoContract] public partial class d { [WProtoMember(1)] public int Value; } } }";
            List<MetadataReference> references = new();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            CSharpCompilation compilation = CSharpCompilation.Create(
                "HintNameCollisionConsumer",
                new[] { CSharpSyntaxTree.ParseText(source) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            CSharpGeneratorDriver
                .Create(new WProtoGenerator())
                .RunGeneratorsAndUpdateCompilation(
                    compilation,
                    out Compilation updated,
                    out ImmutableArray<Diagnostic> diagnostics
                );
            Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.Empty);
            Assert.That(
                updated
                    .GetDiagnostics()
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.ToString()),
                Is.Empty
            );
            Assert.That(
                updated.SyntaxTrees.Count(tree =>
                    tree.FilePath.Contains(".WProtoFormatter", StringComparison.Ordinal)
                    && tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal)
                ),
                Is.EqualTo(4)
            );
        }
    }
}
