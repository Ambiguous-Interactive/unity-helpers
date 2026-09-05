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

        /// <summary>
        /// A consumer closing a generic contract from a REFERENCED assembly registers it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the property the whole approach was chosen for, and it was not implemented: the
        /// generator only scanned for closures of contracts declared in the same compilation, so a
        /// game writing <c>Deque&lt;TheirStruct&gt;</c> got no registration at all. The struct cannot
        /// appear in this package's own sources — it does not exist yet — so nothing else could
        /// register it either, and the type threw on its first serialization in a shipped player.
        /// </para>
        /// <para>
        /// Driven through a real second compilation rather than a synthetic one, because the bug was
        /// precisely that "declared here" and "referenced from here" are different, and a single
        /// compilation cannot tell them apart. <c>SerializableList&lt;T&gt;</c> stands in for any
        /// generic contract a consumer closes.
        /// </para>
        /// </remarks>
        [Test]
        public void AClosureOfAGenericContractFromAReferencedAssemblyIsRegistered()
        {
            IReadOnlyList<string> registrations = Registrations(
                "public static class Use { public static object Make() { return new "
                    + "global::WallstopStudios.UnityHelpers.Proto.Generator.Tests.Box<long>(); } }"
            );

            Assert.That(
                registrations,
                Has.Some.Contains("Box<long>"),
                "A closure of a contract this compilation only references was not registered: "
                    + string.Join(" | ", registrations)
            );
        }

        /// <summary>
        /// A closure over a type the registrar cannot name is skipped rather than emitted.
        /// </summary>
        /// <remarks>
        /// The registrar is a type of its own, so a <c>private</c> nested type is out of its reach
        /// however accessible it is to its container. Emitting the name is <c>CS0122</c> in the build
        /// of the assembly that declared it -- a worse failure than the registration it drops,
        /// because the developer's code stops compiling over a type they never asked to serialize.
        /// </remarks>
        [Test]
        public void AClosureOverAPrivateTypeIsNotRegistered()
        {
            IReadOnlyList<string> registrations = Registrations(
                Contract
                    + "public static class Use { private sealed class Hidden { } "
                    + "public static object Make() { return new Box<Hidden>(); } }"
            );

            Assert.That(
                registrations,
                Has.None.Contains("Hidden"),
                string.Join(" | ", registrations)
            );
        }

        /// <summary>
        /// A container's own type arguments count as much as the closure's.
        /// </summary>
        /// <remarks>
        /// <c>Outer&lt;Hidden&gt;.Inner</c> is a public type nested in a public generic, and its name
        /// still cannot be written when <c>Hidden</c> is private. Walking the containers for
        /// accessibility alone, without their arguments, let exactly this through.
        /// </remarks>
        [Test]
        public void AClosureOverATypeNestedInAPrivatelyClosedGenericIsNotRegistered()
        {
            IReadOnlyList<string> registrations = Registrations(
                Contract
                    + "public sealed class Outer<T> { public sealed class Inner { } } "
                    + "public static class Use { private sealed class Hidden { } "
                    + "public static Box<Outer<Hidden>.Inner> Field; }"
            );

            Assert.That(
                registrations,
                Has.None.Contains("Hidden"),
                string.Join(" | ", registrations)
            );
        }

        /// <summary>
        /// A generic type that is not a contract is left alone, however it is closed.
        /// </summary>
        [Test]
        public void AClosureOfANonContractIsNotRegistered()
        {
            IReadOnlyList<string> registrations = Registrations(
                "public static class Use { public static System.Collections.Generic.List<int> Field; }"
            );

            Assert.That(registrations, Has.None.Contains("List<int>"));
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
