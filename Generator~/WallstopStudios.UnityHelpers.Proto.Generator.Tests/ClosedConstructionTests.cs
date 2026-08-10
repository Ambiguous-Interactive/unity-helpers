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
    /// Pins which spellings of a closed generic the registrar discovers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A generic contract's formatter is emitted once, open; only a <b>closed</b> construction can be
    /// registered, because <c>MakeGenericType</c> is the one call an AOT compiler cannot follow. The
    /// generator therefore finds closures by reading the compilation's source -- which makes "what
    /// counts as a construction" a correctness property rather than an implementation detail.
    /// </para>
    /// <para>
    /// Missing one is silent at build time and fatal at run time: the type serializes in the editor
    /// only if something else happened to register it, and throws
    /// <c>InvalidOperationException</c> from the first save in a shipped player. <c>new Box&lt;int&gt;()</c>
    /// with no variable ever declared of that type was missed exactly this way -- it is the most
    /// natural spelling there is, and the whole consumer story rests on it.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class ClosedConstructionTests
    {
        private const string Contract =
            "[WProtoContract] public partial class Box<T> { [WProtoMember(1)] public T Value; } ";

        // Each of these is a spelling a consumer could reasonably use as the ONLY mention of the
        // closure in their assembly.
        [TestCase(
            "public static class Use { public static object Make() { return new Box<int>(); } }",
            TestName = "AnObjectCreationIsAConstruction"
        )]
        [TestCase(
            "public static class Use { public static Box<int> Field; }",
            TestName = "AFieldDeclarationIsAConstruction"
        )]
        [TestCase(
            "public static class Use { public static object Make() { Box<int> local = null; return local; } }",
            TestName = "ALocalDeclarationIsAConstruction"
        )]
        [TestCase(
            "public static class Use { public static object Make() { return typeof(Box<int>); } }",
            TestName = "ATypeofIsAConstruction"
        )]
        [TestCase(
            "public static class Use { public static object Make() { return default(Box<int>); } }",
            TestName = "ADefaultExpressionIsAConstruction"
        )]
        [TestCase(
            "public static class Use { public static Box<int> Make() => null; }",
            TestName = "AReturnTypeIsAConstruction"
        )]
        [TestCase(
            "public static class Use { public static void Take(Box<int> value) { } }",
            TestName = "AParameterTypeIsAConstruction"
        )]
        [TestCase(
            "public static class Use { public static object Make(object o) { return o as Box<int>; } }",
            TestName = "AnAsExpressionIsAConstruction"
        )]
        public void EveryWayOfNamingAClosureRegistersIt(string usage)
        {
            Assert.That(
                Registrations(Contract + usage),
                Has.Some.Contains("Box<int>"),
                "The closure is never registered, so the type throws on its first serialization."
            );
        }

        /// <summary>
        /// A closure that still mentions a type parameter cannot be named by the registrar.
        /// </summary>
        /// <remarks>
        /// Emitting one produces code that fails the CONSUMER's build, which is worse than the
        /// missing registration it was avoiding.
        /// </remarks>
        [Test]
        public void AConstructionThatIsStillOpenIsNotRegistered()
        {
            IReadOnlyList<string> registrations = Registrations(
                Contract + "public static class Use<T> { public static Box<T> Field; }"
            );

            Assert.That(
                registrations,
                Has.None.Contains("Box<"),
                string.Join(" | ", registrations)
            );
        }

        private static IReadOnlyList<string> Registrations(string body)
        {
            string source =
                "using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;\n"
                + "namespace Consumer { using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto; "
                + body
                + " }";

            List<MetadataReference> references = new List<MetadataReference>();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }

            CSharpCompilation compilation = CSharpCompilation.Create(
                "ConsumerAssembly",
                new[] { CSharpSyntaxTree.ParseText(source) },
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

            SyntaxTree registrar = updated.SyntaxTrees.FirstOrDefault(tree =>
                tree.FilePath.Contains("WProtoGeneratedRegistrar", StringComparison.Ordinal)
            );

            if (registrar == null)
            {
                return Array.Empty<string>();
            }

            return registrar
                .GetText()
                .ToString()
                .Split('\n')
                .Where(line => line.Contains(".Register(", StringComparison.Ordinal))
                .Select(line => line.Trim())
                .ToList();
        }
    }
}
