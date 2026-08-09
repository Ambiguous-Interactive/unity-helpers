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
    /// Pins the build errors a consumer sees when a contract cannot be serialized.
    /// </summary>
    /// <remarks>
    /// These matter more than the happy path. A generator that silently skips a contract it cannot
    /// handle produces an <c>InvalidOperationException</c> from the first save in a shipped player,
    /// naming a type the developer has long since stopped thinking about. Each case below asserts
    /// both the identifier and that the message names the member, because an error code alone sends
    /// the reader to a search engine.
    /// </remarks>
    [TestFixture]
    public sealed class DiagnosticTests
    {
        [Test]
        public void ANonPartialContractIsAnError()
        {
            AssertDiagnostic(
                "WPROTO001",
                "Loose",
                @"[WProtoContract] public sealed class Loose { [WProtoMember(1)] public int Value; }"
            );
        }

        [Test]
        public void TwoMembersClaimingOneFieldNumberIsAnError()
        {
            AssertDiagnostic(
                "WPROTO002",
                "Second",
                @"[WProtoContract] public sealed partial class Clash
                  {
                      [WProtoMember(1)] public int First;
                      [WProtoMember(1)] public int Second;
                  }"
            );
        }

        [Test]
        public void AnUnsupportedMemberTypeIsAnError()
        {
            AssertDiagnostic(
                "WPROTO003",
                "Values",
                @"[WProtoContract] public sealed partial class Unsupported
                  {
                      [WProtoMember(1)] public System.Collections.Generic.List<int> Values;
                  }"
            );
        }

        [TestCase(0)]
        [TestCase(19500)]
        [TestCase(-3)]
        public void AFieldNumberOutsideTheLegalRangeIsAnError(int tag)
        {
            AssertDiagnostic(
                "WPROTO004",
                "Value",
                @"[WProtoContract] public sealed partial class Ranged
                  {
                      [WProtoMember("
                    + tag
                    + @")] public int Value;
                  }"
            );
        }

        [Test]
        public void ALifecycleHookOnATypeWithNoContractIsAnError()
        {
            // The mistake that shipped inert for two years in Runtime/Tags/Attribute.cs (#370): an
            // attribute advertising a hook nothing was wired to call.
            AssertDiagnostic(
                "WPROTO005",
                "Rebuild",
                @"public sealed partial class Orphan
                  {
                      [WProtoAfterDeserialization] private void Rebuild() { }
                  }"
            );
        }

        [Test]
        public void AReadOnlyMemberIsAnError()
        {
            AssertDiagnostic(
                "WPROTO007",
                "Value",
                @"[WProtoContract] public sealed partial class Frozen
                  {
                      [WProtoMember(1)] public readonly int Value;
                  }"
            );
        }

        [Test]
        public void AHookOnAStructIsAnError()
        {
            // 'in T' makes the compiler copy the struct before the call, so every mutation the hook
            // makes lands on a temporary and is discarded. Silent, and impossible to debug from the
            // outside.
            AssertDiagnostic(
                "WPROTO010",
                "Copied",
                @"[WProtoContract] public partial struct Copied
                  {
                      [WProtoMember(1)] public int Value;
                      [WProtoBeforeSerialization] private void Prepare() { }
                  }"
            );
        }

        [Test]
        public void AClassWithNoParameterlessConstructorIsAnError()
        {
            AssertDiagnostic(
                "WPROTO011",
                "Demanding",
                @"[WProtoContract] public sealed partial class Demanding
                  {
                      public Demanding(int seed) { Value = seed; }
                      [WProtoMember(1)] public int Value;
                  }"
            );
        }

        [Test]
        public void TwoHooksOfTheSameKindIsAnError()
        {
            AssertDiagnostic(
                "WPROTO006",
                "AfterDeserialization",
                @"[WProtoContract] public sealed partial class Twice
                  {
                      [WProtoMember(1)] public int Value;
                      [WProtoAfterDeserialization] private void One() { }
                      [WProtoAfterDeserialization] private void Two() { }
                  }"
            );
        }

        [Test]
        public void AHookThatTakesArgumentsIsAnError()
        {
            AssertDiagnostic(
                "WPROTO008",
                "Prepare",
                @"[WProtoContract] public sealed partial class Awkward
                  {
                      [WProtoMember(1)] public int Value;
                      [WProtoBeforeSerialization] private void Prepare(int unused) { }
                  }"
            );
        }

        [Test]
        public void AGenericContractIsARefusalRatherThanAWrongFormatter()
        {
            AssertDiagnostic(
                "WPROTO009",
                "Boxed",
                @"[WProtoContract] public sealed partial class Boxed<T>
                  {
                      [WProtoMember(1)] public int Value;
                  }"
            );
        }

        [Test]
        public void AValidContractProducesNoDiagnosticsAtAll()
        {
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"[WProtoContract] public sealed partial class Fine
                  {
                      [WProtoMember(1)] public int Value;
                  }"
            );

            Assert.IsEmpty(
                diagnostics.Where(d => d.Id.StartsWith("WPROTO", StringComparison.Ordinal)),
                string.Join("; ", diagnostics.Select(d => d.ToString()))
            );
        }

        private static void AssertDiagnostic(string id, string mustName, string source)
        {
            ImmutableArray<Diagnostic> diagnostics = Run(source);
            Diagnostic match = diagnostics.FirstOrDefault(d => d.Id == id);

            Assert.IsNotNull(
                match,
                "expected " + id + ", saw: " + string.Join("; ", diagnostics.Select(d => d.Id))
            );
            Assert.AreEqual(DiagnosticSeverity.Error, match.Severity);
            Assert.IsTrue(
                match.GetMessage().Contains(mustName),
                "the message must name '" + mustName + "': " + match.GetMessage()
            );
        }

        private static ImmutableArray<Diagnostic> Run(string body)
        {
            string source =
                "namespace Consumer { using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto; "
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
                    out Compilation _,
                    out ImmutableArray<Diagnostic> diagnostics
                );

            return diagnostics;
        }
    }
}
