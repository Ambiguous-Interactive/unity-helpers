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
    /// Pins what the JSON registrar is emitted into, and what it is kept out of.
    /// </summary>
    /// <remarks>
    /// The second case is the one that matters. An assembly can reference this package, write
    /// <c>SerializableDictionary&lt;byte, decimal&gt;</c>, and never reference System.Text.Json --
    /// which is the default shape of a Unity assembly definition with <c>overrideReferences</c>,
    /// where every precompiled DLL is listed by hand. Emitting a registrar there is <c>CS0012</c> in
    /// a generated file nobody wrote: an attribute silently breaking an assembly that compiled
    /// before. Found by compiling the working tree in a live editor, where eighteen of this
    /// repository's own test assemblies stopped building at once; every plain <c>dotnet</c> gate
    /// passed, because every one of those compilations references System.Text.Json.
    /// </remarks>
    [TestFixture]
    public sealed class JsonRegistrarEmissionTests
    {
        private const string Consumer =
            "namespace Consumer { public sealed class Holder { public global::WallstopStudios.UnityHelpers.Proto.Generator.Tests.ProbeBox<int> Box; } }";

        [Test]
        public void AClosureInAnAssemblyThatCanSeeSystemTextJsonIsRegistered()
        {
            string registrar = Emit(Consumer, withSystemTextJson: true);

            Assert.IsNotNull(registrar, "No JSON registrar was emitted");
            StringAssert.Contains("WJsonConverterRegistry.TryRegister", registrar);
            StringAssert.Contains("ProbeBox<int>", registrar);
            StringAssert.Contains("ProbeBoxConverter<int>", registrar);
        }

        [Test]
        public void AnAssemblyThatCannotSeeSystemTextJsonGetsNoRegistrarAtAll()
        {
            // Not "an empty registrar" and not "a registrar guarded by a define": no file. The
            // registration names a JsonConverter in its argument, so any mention of it here is a
            // compile error in the consumer's build rather than a missing registration.
            Assert.IsNull(Emit(Consumer, withSystemTextJson: false));
        }

        private static string Emit(string body, bool withSystemTextJson)
        {
            List<MetadataReference> references = new List<MetadataReference>();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
                {
                    continue;
                }

                if (!withSystemTextJson && assembly.GetName().Name == "System.Text.Json")
                {
                    continue;
                }

                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }

            CSharpCompilation compilation = CSharpCompilation.Create(
                "ConsumerAssembly",
                new[] { CSharpSyntaxTree.ParseText(body) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            CSharpGeneratorDriver
                .Create(new WProtoGenerator())
                .RunGeneratorsAndUpdateCompilation(
                    compilation,
                    out Compilation updated,
                    out ImmutableArray<Diagnostic> _
                );

            SyntaxTree emitted = updated.SyntaxTrees.FirstOrDefault(tree =>
                tree.FilePath.EndsWith("WJsonGeneratedRegistrar.g.cs", StringComparison.Ordinal)
            );

            return emitted?.ToString();
        }
    }
}
