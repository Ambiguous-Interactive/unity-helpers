// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using NUnit.Framework;

    /// <summary>
    /// Pins the diagnostics a consumer sees when a contract cannot be serialized or migrated.
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

        /// <summary>
        /// A subtype its base does not declare is refused at build time, not at run time.
        /// </summary>
        /// <remarks>
        /// A subtype is written as its base writes it, so without an include there is no tag to
        /// write it under and the dispatch chain matches no branch. That surfaces as a thrown
        /// exception from the first save in a shipped player, which is exactly the outcome every
        /// other diagnostic here exists to prevent.
        /// </remarks>
        [Test]
        public void ASubtypeItsBaseDoesNotDeclareIsAnError()
        {
            AssertDiagnostic(
                "WPROTO018",
                "Sub",
                @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] public partial class Sub : Base { [WProtoMember(1)] public int B; }"
            );
        }

        [Test]
        public void ASubtypeItsBaseDeclaresIsFine()
        {
            Assert.IsEmpty(
                Run(
                    @"[WProtoContract] [WProtoInclude(100, typeof(Sub))] public partial class Base { [WProtoMember(1)] public int A; }
                      [WProtoContract] public partial class Sub : Base { [WProtoMember(1)] public int B; }"
                )
            );
        }

        [Test]
        public void AFieldInitializerSkipConstructorDiscardsIsAWarning()
        {
            // The shape that shipped for five releases: a scratch buffer whose only guarantee was
            // its field initializer, on a contract asking protobuf-net to allocate uninitialized.
            // Invisible through this package's own reader, which emits a constructor that DOES run
            // initializers -- so nothing but a diagnostic can catch the next one.
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"[WProtoContract(SkipConstructor = true)] public partial class Generator { [WProtoMember(1)] public ulong State; private byte[] _scratch = new byte[16]; }"
            );
            Diagnostic match = diagnostics.Single(diagnostic => diagnostic.Id == "WPROTO033");

            Assert.AreEqual(DiagnosticSeverity.Warning, match.Severity);
            Assert.IsTrue(match.GetMessage().Contains("_scratch"), match.GetMessage());
            Assert.IsTrue(match.GetMessage().Contains("Generator"), match.GetMessage());
        }

        [Test]
        public void OnlyAFieldWhoseValueNeedsAConstructorWarns()
        {
            // Four ways not to be the defect, each of which a coarser rule would report. A
            // diagnostic that fires on correct code is a build break in someone else's project.
            string[] clean =
            {
                // No initializer: its default IS what it holds either way.
                @"[WProtoContract(SkipConstructor = true)] public partial class A { [WProtoMember(1)] public ulong State; private byte[] _scratch; }",
                // On the wire, so the payload restores it.
                @"[WProtoContract(SkipConstructor = true)] public partial class B { [WProtoMember(1)] public ulong State; [WProtoMember(2)] public byte[] Scratch = new byte[16]; }",
                // Static, so no instance allocation is involved.
                @"[WProtoContract(SkipConstructor = true)] public partial class C { [WProtoMember(1)] public ulong State; private static readonly byte[] Shared = new byte[16]; }",
                // No SkipConstructor, so the constructor and its initializers run.
                @"[WProtoContract] public partial class D { [WProtoMember(1)] public ulong State; private byte[] _scratch = new byte[16]; }",
            };

            foreach (string source in clean)
            {
                Assert.IsEmpty(
                    Run(source).Where(diagnostic => diagnostic.Id == "WPROTO033"),
                    source
                );
            }

            // An auto-property initializer is the same mechanism -- the backing field is what the
            // uninitialized allocation leaves at its default -- and names the property.
            ImmutableArray<Diagnostic> property = Run(
                @"[WProtoContract(SkipConstructor = true)] public partial class E { [WProtoMember(1)] public ulong State; private byte[] Scratch { get; } = new byte[16]; }"
            );
            Assert.IsTrue(
                property
                    .Single(diagnostic => diagnostic.Id == "WPROTO033")
                    .GetMessage()
                    .Contains("Scratch")
            );
        }

        [Test]
        public void AnInheritedFieldInitializerIsReportedToo()
        {
            // The shape the diagnostic was written for, and the one it originally missed: the
            // buffer is declared on the BASE while SkipConstructor sits on the concrete contract.
            // protobuf-net allocates the whole object uninitialized, inherited fields included, so
            // the base's initializer is dropped exactly as an own one is -- and this is precisely
            // `AbstractRandom._guidBytes` under twelve generators.
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"public abstract class Machinery { protected byte[] _scratch = new byte[16]; }
                  [WProtoContract(SkipConstructor = true)] public partial class Engine : Machinery { [WProtoMember(1)] public ulong State; }"
            );
            Diagnostic match = diagnostics.Single(diagnostic => diagnostic.Id == "WPROTO033");

            Assert.AreEqual(DiagnosticSeverity.Warning, match.Severity);

            // Names the contract that asked for the uninitialized allocation AND the type that
            // declares the field, because otherwise the reader has nowhere to look.
            Assert.IsTrue(match.GetMessage().Contains("Engine"), match.GetMessage());
            Assert.IsTrue(match.GetMessage().Contains("Machinery._scratch"), match.GetMessage());

            // A base with nothing to drop stays quiet, so the walk is not simply reporting bases.
            Assert.IsEmpty(
                Run(
                        @"public abstract class Bare { protected byte[] _scratch; }
                          [WProtoContract(SkipConstructor = true)] public partial class Plain : Bare { [WProtoMember(1)] public ulong State; }"
                    )
                    .Where(diagnostic => diagnostic.Id == "WPROTO033")
            );
        }

        [Test]
        public void AnImmutableContractDeclaringSkipConstructorStillWarns()
        {
            // This generator IGNORES SkipConstructor on a contract it builds through a constructor,
            // and protobuf-net honours it regardless -- so the hazard is exactly as real there, and
            // asking the emitter's flag rather than the author's would have missed it.
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"[WProtoContract(SkipConstructor = true)] public partial class Frozen { [WProtoMember(1)] public readonly ulong State; private byte[] _scratch = new byte[16]; }"
            );

            Assert.AreEqual(
                DiagnosticSeverity.Warning,
                diagnostics.Single(diagnostic => diagnostic.Id == "WPROTO033").Severity
            );
        }

        [Test]
        public void ALifecycleHookOnASubtypeIsAWarning()
        {
            // Measured against both oracles, and they answer differently, which is the whole reason
            // this is a diagnostic instead of a behaviour change: protobuf-net 3.2.56 runs only the
            // root's callbacks, 2.4.9 runs every level outermost-first, and this generator runs
            // every level innermost-first. The hook is therefore dead code in any build the
            // protobuf-net 3 fallback serves, and nothing said so.
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"[WProtoContract] [WProtoInclude(100, typeof(Leaf))] public partial class Root { [WProtoMember(1)] public int A; }
                  [WProtoContract] public partial class Leaf : Root { [WProtoMember(1)] public int B; [WProtoAfterDeserialization] private void Rebuild() { } }"
            );
            Diagnostic match = diagnostics.Single(diagnostic => diagnostic.Id == "WPROTO034");

            Assert.AreEqual(DiagnosticSeverity.Warning, match.Severity);

            // Names the hook, the subtype that declares it, and the root it belongs on -- the last
            // of those is the fix, and a message without it is a complaint rather than an answer.
            Assert.IsTrue(match.GetMessage().Contains("Rebuild"), match.GetMessage());
            Assert.IsTrue(match.GetMessage().Contains("Leaf"), match.GetMessage());
            Assert.IsTrue(match.GetMessage().Contains("Root"), match.GetMessage());
        }

        [Test]
        public void OnlyAHookNoReaderAgreesOnWarns()
        {
            // Every placement all three readers do agree on, each of which a coarser rule would
            // report. The root of a chain is the one moment they share; a contract with no chain at
            // all is trivially its own root.
            string[] clean =
            {
                @"[WProtoContract] [WProtoInclude(100, typeof(Leaf))] public partial class Root { [WProtoMember(1)] public int A; [WProtoAfterDeserialization] private void Rebuild() { } }
                  [WProtoContract] public partial class Leaf : Root { [WProtoMember(1)] public int B; }",
                @"[WProtoContract] public partial class Alone { [WProtoMember(1)] public int A; [WProtoAfterDeserialization] private void Rebuild() { } }",
                // A contract whose base is not a contract owns its own wire shape, so it IS the root.
                @"public abstract class Machinery { }
                  [WProtoContract] public partial class Engine : Machinery { [WProtoMember(1)] public int A; [WProtoBeforeSerialization] private void Flush() { } }",
            };

            foreach (string source in clean)
            {
                Assert.IsEmpty(
                    Run(source).Where(diagnostic => diagnostic.Id == "WPROTO034"),
                    source
                );
            }

            // All four hooks, not only the one the package happened to ship.
            foreach (
                string hook in new[]
                {
                    "WProtoBeforeSerialization",
                    "WProtoAfterSerialization",
                    "WProtoBeforeDeserialization",
                    "WProtoAfterDeserialization",
                }
            )
            {
                Assert.AreEqual(
                    1,
                    Run(
                            @"[WProtoContract] [WProtoInclude(100, typeof(Leaf))] public partial class Root { [WProtoMember(1)] public int A; }
                              [WProtoContract] public partial class Leaf : Root { [WProtoMember(1)] public int B; ["
                                + hook
                                + @"] private void Hooked() { } }"
                        )
                        .Count(diagnostic => diagnostic.Id == "WPROTO034"),
                    hook
                );
            }
        }

        [Test]
        public void AJsonConverterDeclarationThatCannotBeClosedIsAWarning()
        {
            // Every way the pair cannot work, because the registration the generator would emit is
            // code the developer never wrote: a compile error there names a generated file.
            string[] unusable =
            {
                // Not generic. A single closure needs no generation; add it to Converters instead.
                @"[assembly: global::WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters.WJsonConverter(typeof(Consumer.Plain), typeof(Consumer.PlainConverter))]
                  public sealed class Plain { }
                  public sealed class PlainConverter : global::System.Text.Json.Serialization.JsonConverter<Plain> { public override Plain Read(ref global::System.Text.Json.Utf8JsonReader r, System.Type t, global::System.Text.Json.JsonSerializerOptions o) => null; public override void Write(global::System.Text.Json.Utf8JsonWriter w, Plain v, global::System.Text.Json.JsonSerializerOptions o) { } }",
                // Arity mismatch.
                @"[assembly: global::WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters.WJsonConverter(typeof(Consumer.Box<>), typeof(Consumer.PairConverter<,>))]
                  public sealed class Box<T> { }
                  public sealed class PairConverter<TA, TB> : global::System.Text.Json.Serialization.JsonConverter<Box<TA>> { public override Box<TA> Read(ref global::System.Text.Json.Utf8JsonReader r, System.Type t, global::System.Text.Json.JsonSerializerOptions o) => null; public override void Write(global::System.Text.Json.Utf8JsonWriter w, Box<TA> v, global::System.Text.Json.JsonSerializerOptions o) { } }",
                // Converts something else.
                @"[assembly: global::WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters.WJsonConverter(typeof(Consumer.Box<>), typeof(Consumer.OtherConverter<>))]
                  public sealed class Box<T> { }
                  public sealed class Other<T> { }
                  public sealed class OtherConverter<T> : global::System.Text.Json.Serialization.JsonConverter<Other<T>> { public override Other<T> Read(ref global::System.Text.Json.Utf8JsonReader r, System.Type t, global::System.Text.Json.JsonSerializerOptions o) => null; public override void Write(global::System.Text.Json.Utf8JsonWriter w, Other<T> v, global::System.Text.Json.JsonSerializerOptions o) { } }",
                // No public parameterless constructor, so `new` on it does not compile.
                @"[assembly: global::WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters.WJsonConverter(typeof(Consumer.Box<>), typeof(Consumer.SealedConverter<>))]
                  public sealed class Box<T> { }
                  public sealed class SealedConverter<T> : global::System.Text.Json.Serialization.JsonConverter<Box<T>> { private SealedConverter() { } public override Box<T> Read(ref global::System.Text.Json.Utf8JsonReader r, System.Type t, global::System.Text.Json.JsonSerializerOptions o) => null; public override void Write(global::System.Text.Json.Utf8JsonWriter w, Box<T> v, global::System.Text.Json.JsonSerializerOptions o) { } }",
            };

            foreach (string source in unusable)
            {
                Diagnostic match = Run(source).Single(diagnostic => diagnostic.Id == "WPROTO035");
                Assert.AreEqual(DiagnosticSeverity.Warning, match.Severity, source);
            }
        }

        [Test]
        public void AWorkableJsonConverterDeclarationIsQuiet()
        {
            // Including the constrained shape the package actually ships: both sides carry the same
            // `new()` constraint, and validating that against the serialized type's own parameters
            // reported it unusable until a type parameter stopped being asked for its constructors.
            Assert.IsEmpty(
                Run(
                        @"[assembly: global::WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters.WJsonConverter(typeof(Consumer.Box<>), typeof(Consumer.BoxConverter<>))]
                          public sealed class Box<T> where T : new() { }
                          public sealed class BoxConverter<T> : global::System.Text.Json.Serialization.JsonConverter<Box<T>> where T : new() { public override Box<T> Read(ref global::System.Text.Json.Utf8JsonReader r, System.Type t, global::System.Text.Json.JsonSerializerOptions o) => null; public override void Write(global::System.Text.Json.Utf8JsonWriter w, Box<T> v, global::System.Text.Json.JsonSerializerOptions o) { } }"
                    )
                    .Where(diagnostic => diagnostic.Id == "WPROTO035")
            );
        }

        [Test]
        public void TwoJsonConverterDeclarationsForOneTypeIsAWarning()
        {
            // Which one wins is attribute order, which is not readable at either declaration.
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"[assembly: global::WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters.WJsonConverter(typeof(Consumer.Box<>), typeof(Consumer.BoxConverter<>))]
                  [assembly: global::WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters.WJsonConverter(typeof(Consumer.Box<>), typeof(Consumer.BoxConverter<>))]
                  public sealed class Box<T> { }
                  public sealed class BoxConverter<T> : global::System.Text.Json.Serialization.JsonConverter<Box<T>> { public override Box<T> Read(ref global::System.Text.Json.Utf8JsonReader r, System.Type t, global::System.Text.Json.JsonSerializerOptions o) => null; public override void Write(global::System.Text.Json.Utf8JsonWriter w, Box<T> v, global::System.Text.Json.JsonSerializerOptions o) { } }"
            );

            Assert.AreEqual(
                DiagnosticSeverity.Warning,
                diagnostics.Single(diagnostic => diagnostic.Id == "WPROTO036").Severity
            );
        }

        [Test]
        public void AProtobufContractWithoutAWallstopProtoContractReportsMigrationInfo()
        {
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"[global::ProtoBuf.ProtoContract] public partial class Legacy { [global::ProtoBuf.ProtoMember(1)] public int Value; }"
            );
            Diagnostic match = diagnostics.Single(diagnostic => diagnostic.Id == "WPROTO030");

            Assert.AreEqual(DiagnosticSeverity.Info, match.Severity);
            Assert.IsTrue(match.GetMessage().Contains("Legacy"));
            Assert.IsTrue(match.GetMessage().Contains("WProtoContract"));
            Assert.IsTrue(match.GetMessage().Contains("suppress"));
        }

        [Test]
        public void AContractWithBothAnnotationsReportsNoMigrationInfo()
        {
            Assert.IsFalse(
                Run(
                        @"[global::ProtoBuf.ProtoContract] [WProtoContract] public partial class Ported { [global::ProtoBuf.ProtoMember(1)] [WProtoMember(1)] public int Value; }"
                    )
                    .Any(diagnostic => diagnostic.Id == "WPROTO030")
            );
        }

        [Test]
        public void AnAliasedPartialProtobufContractReportsMigrationInfoOnce()
        {
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"using LegacyContract = global::ProtoBuf.ProtoContractAttribute;
                  [LegacyContract] public partial class Legacy { }
                  public partial class Legacy { public int Value; }"
            );

            Assert.AreEqual(1, diagnostics.Count(diagnostic => diagnostic.Id == "WPROTO030"));
        }

        [Test]
        public void AProtobufContractCanSuppressTheMigrationInfoWithAPragma()
        {
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"public partial class DeliberatelyLegacy { }
                  #pragma warning disable WPROTO030
                  [global::ProtoBuf.ProtoContract] public partial class DeliberatelyLegacy { }
                  #pragma warning restore WPROTO030"
            );
            Diagnostic match = diagnostics.Single(diagnostic => diagnostic.Id == "WPROTO030");

            Assert.IsTrue(match.IsSuppressed);
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

        // Every one of these is a shape a developer would reasonably expect to work, which is why it
        // has to fail the build with a message rather than silently get no formatter.
        //
        // A CONSUMER'S OWN collection interface is the one worth explaining. protobuf-net writes it
        // and then throws InvalidCastException on read, because it fills a List<T> and hands it
        // back for a type that is not one -- measured against 3.2.56. The generator has no
        // implementation it could pick either, so it refuses at build time instead, which is the
        // same answer arriving somewhere it can be acted on.
        //
        // The nested and jagged shapes moved OFF this list in session 187 -- they are served by a
        // wrapper message per inner collection now, and NestedCollectionTests pins their bytes.
        // Rectangular arrays followed in session 189, through a wrapper that carries a dimension
        // header beside its run, and RectangularArrayTests pins those. What is left here has no
        // encoding at all rather than one that had not been written yet.
        //
        // The remaining entries are element-shape refusals: a consumer's own collection interface
        // (protobuf-net writes it and throws InvalidCastException reading it back -- measured), a
        // nullable element (protobuf-net refuses a null element, so Nullable<T>[] is a collection
        // that can only hold values it cannot write), and a BCL type with no mapping in either
        // oracle major. The nested and rectangular spellings of each are here too, because a
        // wrapper must not launder an element its own member would have refused.
        [TestCase("Consumer.IOwnList<int>")]
        [TestCase("System.Collections.Generic.List<Consumer.IOwnList<int>>")]
        [TestCase("Consumer.IOwnList<int>[,]")]
        [TestCase("int?[]")]
        [TestCase("int?[][]")]
        [TestCase("int?[,]")]
        [TestCase("System.DateTimeOffset")]
        [TestCase("System.Collections.Generic.List<System.DateTimeOffset[]>")]
        [TestCase("System.DateTimeOffset[,]")]
        public void AnUnsupportedMemberTypeIsAnError(string declaredType)
        {
            AssertDiagnostic(
                "WPROTO003",
                "Values",
                @"public interface IOwnList<T> : System.Collections.Generic.IList<T> { }

                  [WProtoContract] public sealed partial class Unsupported
                  {
                      [WProtoMember(1)] public "
                    + declaredType
                    + @" Values;
                  }"
            );
        }

        [Test]
        public void EveryConstructibleCollectionIsAccepted()
        {
            // The counterpart to the list above, and the reason it is worth having: WPROTO003 fired
            // on List<int> until this session, so "it errors" is not by itself evidence that the
            // error is right. The requirement is ICollection<T> plus a parameterless constructor
            // plus an accessible Add -- not "is one of the types this generator has heard of".
            AssertNoDiagnostics(
                @"[WProtoContract] public sealed partial class Supported
                  {
                      [WProtoMember(1)] public int[] Ints;
                      [WProtoMember(2)] public System.Collections.Generic.List<string> Texts;
                      [WProtoMember(3)] public byte[][] Blobs;
                      [WProtoMember(4, OverwriteList = true)] public double[] Doubles;
                      [WProtoMember(5)] public System.Collections.Generic.HashSet<int> Set;
                      [WProtoMember(6)] public System.Collections.Generic.SortedSet<string> Sorted;
                      [WProtoMember(7)] public System.Collections.ObjectModel.Collection<int> Owned;
                      [WProtoMember(8)] public System.Collections.Generic.Dictionary<string, int> Map;
                      [WProtoMember(9)] public System.Collections.Generic.SortedDictionary<string, int> SortedMap;
                  }"
            );
        }

        [Test]
        public void EveryNestedCollectionShapeIsAccepted()
        {
            // The list that AnUnsupportedMemberTypeIsAnError used to hold, inverted. Each of these
            // becomes a wrapper message per inner collection; the bytes are pinned by
            // NestedCollectionTests, and this only asserts that the build stops refusing them.
            AssertNoDiagnostics(
                @"[WProtoContract] public sealed partial class Nested
                  {
                      [WProtoMember(1)] public int[][] Rows;
                      [WProtoMember(2)] public System.Collections.Generic.List<int[]> Batches;
                      [WProtoMember(3)] public System.Collections.Generic.List<System.Collections.Generic.List<int>> Grid;
                      [WProtoMember(4)] public int[][][] Cube;
                      [WProtoMember(5)] public System.Collections.Generic.HashSet<int>[] Sets;
                      [WProtoMember(6)] public System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, int>> Tables;
                      [WProtoMember(7)] public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>> Lookup;
                      [WProtoMember(8)] public System.Collections.Generic.Queue<System.Collections.Generic.Stack<string>> Pipelines;
                  }"
            );
        }

        [Test]
        public void EveryBclValueTypeIsAccepted()
        {
            // The counterpart for the base-class-library sweep: DateTime was WPROTO003 until the
            // built-in formatters landed, so acceptance here is the flip side of the same evidence
            // rule. The bytes themselves are pinned against both oracle majors by
            // BclDifferentialTests.
            AssertNoDiagnostics(
                @"[WProtoContract] public sealed partial class Supported
                  {
                      [WProtoMember(1)] public System.DateTime When;
                      [WProtoMember(2)] public System.TimeSpan Duration;
                      [WProtoMember(3)] public System.Guid Identifier;
                      [WProtoMember(4)] public decimal Amount;
                      [WProtoMember(5)] public System.DateTime? MaybeWhen;
                      [WProtoMember(6)] public System.Collections.Generic.List<System.DateTime> Timeline;
                      [WProtoMember(7)] public System.DateTime[,] Grid;
                      [WProtoMember(8)] public System.Collections.Generic.List<System.DateTime[]> Batches;
                      [WProtoMember(9)] public System.Collections.Generic.Dictionary<string, System.Guid> IdsByName;
                      [WProtoMember(10)] public System.Collections.Generic.List<decimal> Amounts;
                      [WProtoMember(11)] public char Code;
                      [WProtoMember(12)] public System.Uri Source;
                      [WProtoMember(13)] public char? MaybeCode;
                      [WProtoMember(14)] public System.Collections.Generic.List<char> CodePoints;
                      [WProtoMember(15)] public char[,] Grid2;
                      [WProtoMember(16)] public System.Collections.Generic.Dictionary<string, System.Uri> HomeByUser;
                  }"
            );
        }

        [Test]
        public void ThePointerAndTypeRefusalsAreDeliberate()
        {
            // These four were measured, not skipped: DateTimeOffset has no encoding in either oracle
            // major, the pointer types have none in 2.x while the value they carry names nothing
            // once its process ends, and Type writes a runtime-bound assembly-qualified name. The
            // refusal is the deliverable; these rows keep it an error rather than a shrug.
            AssertDiagnostic(
                "WPROTO003",
                "Handle",
                @"[WProtoContract] public sealed partial class Refused
                  {
                      [WProtoMember(1)] public System.IntPtr Handle;
                  }"
            );
            AssertDiagnostic(
                "WPROTO003",
                "Handle",
                @"[WProtoContract] public sealed partial class Refused
                  {
                      [WProtoMember(1)] public System.UIntPtr Handle;
                  }"
            );
            AssertDiagnostic(
                "WPROTO003",
                "Kind",
                @"[WProtoContract] public sealed partial class Refused
                  {
                      [WProtoMember(1)] public System.Type Kind;
                  }"
            );
            AssertDiagnostic(
                "WPROTO003",
                "Kind",
                @"[WProtoContract] public sealed partial class Refused
                  {
                      [WProtoMember(1)] public System.Type[] Kinds;
                  }"
            );
        }

        [Test]
        public void TheNestingBoundDoesNotDependOnWhichMemberIsDeclaredFirst()
        {
            // Wrappers are shared across a contract's members, so a shallow member declared first
            // seeds the cache -- and reusing that entry deep inside a longer chain assembles
            // something past the reader's limit without the bound being consulted. Both orders have
            // to answer the same, or the diagnostic is a statement about declaration order.
            const int shallow = 3;
            const int deep = 66;

            AssertDiagnostic("WPROTO032", "Deep", Chain(("Shallow", shallow), ("Deep", deep)));
            AssertDiagnostic("WPROTO032", "Deep", Chain(("Deep", deep), ("Shallow", shallow)));
        }

        [Test]
        public void ADepthRefusalDoesNotMakeAServiceableMemberLookUnsupported()
        {
            // The other half of the same cache. A failed lookup used to stay behind as a negative
            // entry, so every wrapper above a depth refusal was poisoned -- and a shape this suite
            // serializes elsewhere was reported as WPROTO003 purely because a deeper member was
            // declared before it. The deep member must be the ONLY one named.
            ImmutableArray<Diagnostic> diagnostics = Run(Chain(("Deep", 66), ("Shallow", 3)));

            Assert.IsEmpty(
                diagnostics.Where(d => d.Id == "WPROTO003"),
                string.Join("; ", diagnostics.Select(d => d.Id + " " + d.GetMessage()))
            );
        }

        /// <summary>
        /// Builds a contract whose members are <c>List</c> chains of the given depths.
        /// </summary>
        /// <param name="members">Each member's name and how many <c>List</c> levels it has.</param>
        /// <returns>The contract source.</returns>
        private static string Chain(params (string Name, int Levels)[] members)
        {
            StringBuilder source = new StringBuilder(
                "[WProtoContract] public sealed partial class Ordered\n{\n"
            );

            int tag = 0;
            foreach ((string name, int levels) in members)
            {
                tag++;
                source.Append("    [WProtoMember(").Append(tag).Append(")] public ");
                source.Append(
                    string.Concat(Enumerable.Repeat("System.Collections.Generic.List<", levels))
                );
                source.Append("int").Append(new string('>', levels));
                source.Append(' ').Append(name).Append(";\n");
            }

            return source.Append('}').ToString();
        }

        [Test]
        public void ACollectionNestedPastTheReadersDepthIsItsOwnError()
        {
            // WPROTO003 would be true and useless here: the shape IS supported, up to the depth the
            // reader can read back. Sixty-six levels is one wrapper past the sixty-four
            // WProtoReader.MaxNestingDepth allows, so a member this deep would be writable and
            // unreadable -- which is the failure the build is refusing on behalf of.
            const int levels = 66;
            string declared =
                string.Concat(Enumerable.Repeat("System.Collections.Generic.List<", levels))
                + "int"
                + new string('>', levels);

            AssertDiagnostic(
                "WPROTO032",
                "Values",
                @"[WProtoContract] public sealed partial class TooDeep
                  {
                      [WProtoMember(1)] public "
                    + declared
                    + @" Values;
                  }"
            );
        }

        [Test]
        public void EveryStdlibCollectionShapeIsAcceptedAndCompiles()
        {
            // The shapes #395 was filed about. Four of them cannot be filled through
            // ICollection<T>.Add at all -- LinkedList and ReadOnlyCollection implement it
            // explicitly, Queue and Stack do not implement it -- and the interfaces have nothing to
            // construct, so each needed a per-type answer rather than the generic one.
            //
            // Compiled rather than only scanned for diagnostics: an emitter that names the wrong
            // fill method (`pending.Enqueue` on a List<T>, `new ReadOnlyCollection<T>()`) reports
            // nothing and breaks the consumer's build.
            const string source =
                @"[WProtoContract] public sealed partial class Stdlib
                  {
                      [WProtoMember(1)] public System.Collections.Generic.LinkedList<int> Linked;
                      [WProtoMember(2)] public System.Collections.Generic.Queue<int> Queued;
                      [WProtoMember(3)] public System.Collections.Generic.Stack<int> Stacked;
                      [WProtoMember(4)] public System.Collections.ObjectModel.ReadOnlyCollection<int> Frozen;
                      [WProtoMember(5)] public System.Collections.Generic.IList<int> Listed;
                      [WProtoMember(6)] public System.Collections.Generic.ICollection<string> Collected;
                      [WProtoMember(7)] public System.Collections.Generic.IEnumerable<int> Enumerated;
                      [WProtoMember(8)] public System.Collections.Generic.IReadOnlyList<int> ReadOnlyListed;
                      [WProtoMember(9)] public System.Collections.Generic.IReadOnlyCollection<int> ReadOnlyCollected;
                      [WProtoMember(10)] public System.Collections.Generic.ISet<int> SetOf;
                      [WProtoMember(11)] public System.Collections.Generic.IDictionary<string, int> Mapped;
                      [WProtoMember(12)] public System.Collections.Generic.IReadOnlyDictionary<string, int> ReadOnlyMapped;
                      [WProtoMember(13)] public System.Collections.ObjectModel.ReadOnlyDictionary<string, int> FrozenMap;
                      [WProtoMember(14, OverwriteList = true)] public System.Collections.Generic.Stack<int> ReplacedStack;
                      [WProtoMember(15)] public System.Collections.Generic.IReadOnlySet<int> ReadOnlySetOf;
                  }";

            AssertNoDiagnostics(source);
            Assert.IsEmpty(CompileGenerated(source).Select(d => d.Id + " " + d.GetMessage()));
        }

        [Test]
        public void ATypeThatIsBothAMessageAndACollectionIsRefusedRatherThanGuessedAt()
        {
            // Eight of this package's own contracts are exactly this shape -- Deque, CyclicBuffer,
            // SparseSet, BitSet and the four Serializable* collections all carry
            // [ProtoContract(IgnoreListHandling = true)] today. Reading one as a repeated field
            // silently discards its [WProtoMember]s; reading it as a message silently discards its
            // elements. protobuf-net picks list handling and needs a flag to be told otherwise;
            // this refuses to pick.
            AssertDiagnostic(
                "WPROTO012",
                "Items",
                @"[WProtoContract] public sealed partial class Bag : System.Collections.Generic.List<int>
                  {
                      [WProtoMember(1)] public int Capacity;
                  }

                  [WProtoContract] public sealed partial class Holder
                  {
                      [WProtoMember(1)] public Bag Items;
                  }"
            );
        }

        [Test]
        public void IgnoreListHandlingResolvesTheAmbiguityTowardsAMessage()
        {
            AssertNoDiagnostics(
                @"[WProtoContract(IgnoreListHandling = true)] public sealed partial class Bag : System.Collections.Generic.List<int>
                  {
                      [WProtoMember(1)] public int Capacity;
                  }

                  [WProtoContract] public sealed partial class Holder
                  {
                      [WProtoMember(1)] public Bag Items;
                  }"
            );
        }

        // An include names a subtype the wire can identify, so all four of these would produce a
        // formatter that cannot round-trip: a subtype that is not one, a subtype with no contract of
        // its own, a reserved or out-of-range field number, and a number a member already claims.
        [TestCase("[WProtoInclude(100, typeof(Stranger))]", "does not derive")]
        [TestCase("[WProtoInclude(100, typeof(Bare))]", "not itself a [WProtoContract]")]
        [TestCase("[WProtoInclude(19500, typeof(Sub))]", "reserved")]
        [TestCase("[WProtoInclude(1, typeof(Sub))]", "already claimed")]
        public void AnUnusableIncludeIsAnError(string include, string mustSay)
        {
            AssertDiagnostic(
                "WPROTO013",
                mustSay,
                @"[WProtoContract] public partial class Stranger { }
                  public partial class Bare : Root { }

                  [WProtoContract] "
                    + include
                    + @" public partial class Root
                  {
                      [WProtoMember(1)] public int Value;
                  }

                  [WProtoContract] public partial class Sub : Root
                  {
                      [WProtoMember(1)] public int SubValue;
                  }"
            );
        }

        [Test]
        public void AnAbstractContractWithNoIncludesIsAnError()
        {
            // Reading it could never produce an instance, and the failure would otherwise be a
            // generated `new AbstractThing()` that does not compile in a file nobody wrote.
            AssertDiagnostic(
                "WPROTO014",
                "Shape",
                @"[WProtoContract] public abstract partial class Shape
                  {
                      [WProtoMember(1)] public int Sides;
                  }"
            );
        }

        [Test]
        public void AnAbstractContractWithIncludesIsAccepted()
        {
            // The shape AbstractRandom has: no instance of its own, and 17 subtypes that do.
            AssertNoDiagnostics(
                @"[WProtoContract] [WProtoInclude(100, typeof(Square))] public abstract partial class Shape
                  {
                      [WProtoMember(1)] public int Sides;
                  }

                  [WProtoContract] public partial class Square : Shape
                  {
                      [WProtoMember(1)] public int Edge;
                  }"
            );
        }

        [Test]
        public void ASurrogateShippedByAReferencedAssemblyIsFoundFromAConsumerCompilation()
        {
            // The property the whole design exists for, and the one an in-assembly fixture cannot
            // reach: this synthetic compilation is a *consumer*, and `ForeignVector3`'s surrogate is declared
            // by an assembly attribute on the assembly it references. If referenced assemblies were
            // not searched, `ForeignVector3` would be an unsupported member type and this would be WPROTO003 —
            // which is exactly how a game would discover that none of its Vector3s serialize.
            AssertNoDiagnostics(
                @"[WProtoContract] public sealed partial class ConsumerThing
                  {
                      [WProtoMember(1)] public WallstopStudios.UnityHelpers.Proto.Generator.Tests.ForeignVector3 Where;
                      [WProtoMember(2)] public WallstopStudios.UnityHelpers.Proto.Generator.Tests.ForeignVector3[] Path;
                  }"
            );
        }

        [Test]
        public void ACollectionImplementedAsAStructIsAcceptedLikeAnyOther()
        {
            // The assumption being refused: nothing about ICollection<T> requires a class, and an
            // inline or pooled buffer is a natural struct. A generator that emits `member != null`
            // for every collection does not merely produce redundant code for one -- it produces
            // code that does not compile.
            //
            // Which is why the generated code is COMPILED here rather than only scanned for
            // WPROTO diagnostics: `member != null` on a struct is CS0019, a compiler error inside
            // emitted source, and the generator reports nothing at all about it.
            const string source =
                @"public struct Bag : System.Collections.Generic.ICollection<int>
                  {
                      private System.Collections.Generic.List<int> _items;
                      public int Count { get { return _items == null ? 0 : _items.Count; } }
                      public bool IsReadOnly { get { return false; } }
                      public void Add(int item)
                      {
                          if (_items == null) { _items = new System.Collections.Generic.List<int>(); }
                          _items.Add(item);
                      }
                      public void Clear() { _items = null; }
                      public bool Contains(int item) { return _items != null && _items.Contains(item); }
                      public void CopyTo(int[] array, int index) { }
                      public bool Remove(int item) { return false; }
                      public System.Collections.Generic.IEnumerator<int> GetEnumerator()
                      {
                          return (_items ?? new System.Collections.Generic.List<int>()).GetEnumerator();
                      }
                      System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                      {
                          return GetEnumerator();
                      }
                  }

                  [WProtoContract] public sealed partial class Holder
                  {
                      [WProtoMember(1)] public Bag Values;
                  }";

            AssertNoDiagnostics(source);
            Assert.IsEmpty(CompileGenerated(source).Select(d => d.Id + " " + d.GetMessage()));
        }

        [Test]
        public void AMapImplementedAsAStructIsAcceptedLikeAnyOther()
        {
            // The other half of the same assumption (#388). The map path accepts a struct
            // dictionary -- a value type has a parameterless constructor by definition, so
            // `MapMember.TryCreate` says yes -- and then emitted `value.Members != null` and
            // `read.Members ?? new Members()` for it. Both are CS0019 on a struct, so a consumer
            // whose dictionary is an inline buffer got a compiler error inside generated source
            // they never wrote, naming an operator rather than a serializer.
            const string source =
                @"public struct Pairs : System.Collections.Generic.IDictionary<int, int>
                  {
                      private System.Collections.Generic.Dictionary<int, int> _items;
                      private System.Collections.Generic.Dictionary<int, int> Items
                      {
                          get
                          {
                              if (_items == null) { _items = new System.Collections.Generic.Dictionary<int, int>(); }
                              return _items;
                          }
                      }
                      public int this[int key] { get { return Items[key]; } set { Items[key] = value; } }
                      public System.Collections.Generic.ICollection<int> Keys { get { return Items.Keys; } }
                      public System.Collections.Generic.ICollection<int> Values { get { return Items.Values; } }
                      public int Count { get { return Items.Count; } }
                      public bool IsReadOnly { get { return false; } }
                      public void Add(int key, int value) { Items.Add(key, value); }
                      public void Add(System.Collections.Generic.KeyValuePair<int, int> item) { Items.Add(item.Key, item.Value); }
                      public void Clear() { _items = null; }
                      public bool Contains(System.Collections.Generic.KeyValuePair<int, int> item) { return Items.ContainsKey(item.Key); }
                      public bool ContainsKey(int key) { return Items.ContainsKey(key); }
                      public void CopyTo(System.Collections.Generic.KeyValuePair<int, int>[] array, int index) { }
                      public bool Remove(int key) { return Items.Remove(key); }
                      public bool Remove(System.Collections.Generic.KeyValuePair<int, int> item) { return Items.Remove(item.Key); }
                      public bool TryGetValue(int key, out int value) { return Items.TryGetValue(key, out value); }
                      public System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int, int>> GetEnumerator()
                      {
                          return Items.GetEnumerator();
                      }
                      System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                      {
                          return GetEnumerator();
                      }
                  }

                  [WProtoContract] public sealed partial class Holder
                  {
                      [WProtoMember(1)] public Pairs Members;
                  }";

            AssertNoDiagnostics(source);
            Assert.IsEmpty(CompileGenerated(source).Select(d => d.Id + " " + d.GetMessage()));
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
        public void AnImmutableContractIsAccepted()
        {
            // Refused until this session. A readonly field can only be assigned by a constructor of
            // its declaring type -- and the generator reopens the contract as partial, so it emits
            // one there. The type keeps its immutability and gains no public surface.
            AssertNoDiagnostics(
                @"[WProtoContract] public sealed partial class Frozen
                  {
                      [WProtoMember(1)] public readonly int Value;
                      [WProtoMember(2)] public string Name { get; }
                      [WProtoMember(3)] public readonly int[] Marks;
                      public Frozen() { }
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
        public void AGenericContractIsAccepted()
        {
            // Refused until this session. The closure decides each member's wire type, so the
            // emitted code asks WProtoGeneric<T> rather than carrying a constant.
            AssertNoDiagnostics(
                @"[WProtoContract] public sealed partial class Boxed<T>
                  {
                      [WProtoMember(1)] public int Value;
                      [WProtoMember(2)] public T Payload;
                      [WProtoMember(3)] public T[] Many;
                  }"
            );
        }

        [Test]
        public void AContractNestedInsideAGenericTypeIsARefusalToo()
        {
            // The contract itself is not generic; the type it is nested in is. Emission would work
            // -- every enclosing type is reopened with its type parameters -- but REGISTRATION would
            // not: `Inner` has no constructions of its own to discover, so its formatter would be
            // emitted and never registered, and `Get<Holder<int>.Inner>()` would throw at runtime in
            // a shipped player. Refusing at build time is the lesser failure.
            AssertDiagnostic(
                "WPROTO009",
                "Inner",
                @"public static partial class Holder<T>
                  {
                      [WProtoContract] public sealed partial class Inner
                      {
                          [WProtoMember(1)] public int Value;
                      }
                  }"
            );
        }

        [Test]
        public void AContractNestedInAFixtureWithItsOwnHandWrittenFormatterIsAccepted()
        {
            // The shape of WProtoFormatterContractTests.HookedMessage, which is what actually broke
            // the Unity legs the first time the analyzer shipped: a contract nested inside a test
            // fixture, with private members, private hooks, and a hand-written formatter of its own.
            // Every enclosing type has to be partial too, and a hand-written nested formatter must
            // not be mistaken for a conflict.
            AssertNoDiagnostics(
                @"public sealed partial class Fixture
                  {
                      [WProtoContract(Name = ""player_state"")]
                      internal sealed partial class Hooked
                      {
                          [WProtoMember(1, Name = ""health"")] private int _health;
                          [WProtoMember(2, Name = ""label"")] private string _label;
                          [WProtoIgnore] private string _derived;

                          internal Hooked() { }
                          internal Hooked(int health) { _health = health; }

                          [WProtoBeforeSerialization] private void OnBeforeSerialization() { }
                          [WProtoAfterSerialization] private void OnAfterSerialization() { }
                          [WProtoBeforeDeserialization] private void OnBeforeDeserialization() { }
                          [WProtoAfterDeserialization] private void OnAfterDeserialization() { _derived = _label; }

                          internal sealed class Formatter : IWProtoFormatter<Hooked>
                          {
                              public int Measure(in Hooked value) { return 0; }
                              public bool Write(ref WProtoWriter writer, in Hooked value) { return true; }
                              public bool TryRead(ref WProtoReader reader, out Hooked value) { value = null; return true; }
                          }
                      }
                  }"
            );
        }

        [Test]
        public void AValidContractProducesNoDiagnosticsAtAll()
        {
            AssertNoDiagnostics(
                @"[WProtoContract] public sealed partial class Fine
                  {
                      [WProtoMember(1)] public int Value;
                  }"
            );
        }

        private static void AssertNoDiagnostics(string source)
        {
            ImmutableArray<Diagnostic> diagnostics = Run(source);
            Assert.IsEmpty(
                diagnostics.Where(d => d.Id.StartsWith("WPROTO", StringComparison.Ordinal)),
                string.Join("; ", diagnostics.Select(d => d.ToString()))
            );
        }

        [Test]
        public void ASurrogateThatIsNotAContractIsAnError()
        {
            // Without this the pair compiles, emits a formatter lookup for a type that has none, and
            // fails on the first save in a shipped player.
            AssertDiagnostic(
                "WPROTO016",
                "Plain",
                @"[assembly: WProtoSurrogate(typeof(Consumer.Real), typeof(Consumer.Plain))]
                  public struct Real { public int X; }
                  public struct Plain
                  {
                      public int X;
                      public static implicit operator Plain(Real value) => new Plain { X = value.X };
                      public static implicit operator Real(Plain value) => new Real { X = value.X };
                  }"
            );
        }

        [Test]
        public void ASurrogateThatCannotConvertBackIsAnError()
        {
            // The worse of the two failures: one-way conversion writes bytes that look correct and
            // reads a value that never comes back.
            AssertDiagnostic(
                "WPROTO017",
                "OneWay",
                @"[assembly: WProtoSurrogate(typeof(Consumer.Real), typeof(Consumer.OneWay))]
                  public struct Real { public int X; }
                  [WProtoContract] public partial struct OneWay
                  {
                      [WProtoMember(1)] public int X;
                      public static implicit operator OneWay(Real value) => new OneWay { X = value.X };
                  }"
            );
        }

        [Test]
        public void AWellFormedSurrogatePairIsNotAnError()
        {
            // The control. Both checks are cheap to write in a way that fires on everything, and a
            // pair of red tests cannot tell that apart from working.
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"[assembly: WProtoSurrogate(typeof(Consumer.Real), typeof(Consumer.Good))]
                  public struct Real { public int X; }
                  [WProtoContract] public partial struct Good
                  {
                      [WProtoMember(1)] public int X;
                      public static implicit operator Good(Real value) => new Good { X = value.X };
                      public static implicit operator Real(Good value) => new Real { X = value.X };
                  }"
            );

            Assert.IsFalse(
                diagnostics.Any(d => d.Id == "WPROTO016" || d.Id == "WPROTO017"),
                string.Join("; ", diagnostics.Select(d => d.Id + " " + d.GetMessage()))
            );
        }

        [Test]
        public void AnOpenConstructionNestedInATypeArgumentIsNotRegistered()
        {
            // The registrar can only name CLOSED types. `Box<int>` is closed; `Box<Wrapper<T>>` is
            // not, because T is still unbound -- but the check only looked at DIRECT type arguments,
            // and `Wrapper<T>` is not itself a type parameter, so it was recorded as closed and
            // emitted into a registrar that cannot name it.
            //
            // Asserted by compiling the generated code rather than by reading it: "the consumer's
            // build fails" is the actual symptom, and it is what a reader of this test cares about.
            ImmutableArray<Diagnostic> errors = CompileGenerated(
                @"public sealed class Wrapper<T> { public T Item; }
                  [WProtoContract] public partial class Box<T> { [WProtoMember(1)] public int Value; }
                  public static class Holder<T> { public static Box<Wrapper<T>> Nested; }
                  public static class Closed { public static Box<int> Fine; }"
            );

            Assert.IsEmpty(
                errors.Select(d => d.Id + " " + d.GetMessage()),
                "the generated registrar must not name an open construction"
            );
        }

        [Test]
        public void AnImmutableClassWithOnlyAParameterizedConstructorIsAccepted()
        {
            // WPROTO011 exists because the formatter normally calls `new T()` to have something to
            // read into. A contract with a member that cannot be assigned after construction does not
            // take that path at all -- it holds every value in a local and BUILDS the instance with
            // the constructor the generator emits -- so demanding a parameterless one rejected the
            // canonical immutable class for a reason that no longer applied to it.
            ImmutableArray<Diagnostic> reported = Run(
                @"[WProtoContract] public sealed partial class Immutable
                  {
                      [WProtoMember(1)] public readonly int X;
                      public Immutable(int x) { X = x; }
                  }"
            );

            Assert.IsFalse(
                reported.Any(d => d.Id == "WPROTO011"),
                string.Join("; ", reported.Select(d => d.Id + " " + d.GetMessage()))
            );

            // And the emitted constructor has to actually compile, which is the half a diagnostic
            // assertion cannot see.
            Assert.IsEmpty(
                CompileGenerated(
                        @"[WProtoContract] public sealed partial class Immutable
                          {
                              [WProtoMember(1)] public readonly int X;
                              public Immutable(int x) { X = x; }
                          }"
                    )
                    .Select(d => d.Id + " " + d.GetMessage())
            );
        }

        [Test]
        public void AMutableClassWithNoParameterlessConstructorIsStillAnError()
        {
            // The control for the relaxation above: nothing here is immutable, so the formatter does
            // need `new T()` and the diagnostic must still fire.
            AssertDiagnostic(
                "WPROTO011",
                "Mutable",
                @"[WProtoContract] public sealed partial class Mutable
                  {
                      [WProtoMember(1)] public int X;
                      public Mutable(int x) { X = x; }
                  }"
            );
        }

        /// <summary>
        /// Every shape of <c>[assembly: WProtoDeclaredRoot]</c> that cannot be registered.
        /// </summary>
        /// <remarks>
        /// The generated registration is <c>WProtoDeclaredRootProvider.Register&lt;D, R&gt;()</c>,
        /// whose constraints are <c>D : class</c> and <c>R : D</c>. Each case below would otherwise
        /// be a compiler error inside generated code that names neither the attribute nor the file
        /// it is written in -- or, for the two that do compile, a silent wire change.
        /// </remarks>
        [TestCaseSource(nameof(BadDeclaredRoots))]
        public void AnUnusableDeclaredRootIsAnError(string id, string mustName, string source)
        {
            AssertDiagnostic(id, mustName, source);
        }

        [Test]
        public void AWellFormedDeclaredRootReportsNothingAndCompiles()
        {
            // The generator reporting no diagnostic is not the same as the registration compiling:
            // the constraints are checked where the call is emitted, not where the attribute is.
            const string source =
                @"[assembly: WProtoDeclaredRoot(typeof(Consumer.IThing), typeof(Consumer.Thing))]
                  public interface IThing { }
                  [WProtoContract] public partial class Thing : IThing { [WProtoMember(1)] public int A; }";

            Assert.IsEmpty(
                Run(source)
                    .Where(d => d.Id.StartsWith("WPROTO0", StringComparison.Ordinal))
                    .Select(d => d.Id + " " + d.GetMessage())
            );
            Assert.IsEmpty(CompileGenerated(source).Select(d => d.Id + " " + d.GetMessage()));
        }

        [Test]
        public void ADeclaredRootConflictingWithAReferencedAssemblyIsAWarning()
        {
            MetadataReference referencedRoots = CompileReference(
                "ReferencedRoots",
                @"[assembly: WProtoDeclaredRoot(typeof(Reference.IThing), typeof(Reference.ReferenceRoot))]
                  namespace Reference
                  {
                      public interface IThing { }
                      [WProtoContract] public partial class ReferenceRoot : IThing { [WProtoMember(1)] public int A; }
                  }"
            );

            ImmutableArray<Diagnostic> diagnostics = Run(
                @"[assembly: WProtoDeclaredRoot(typeof(Reference.IThing), typeof(Consumer.ConsumerRoot))]
                  [WProtoContract] public partial class ConsumerRoot : Reference.IThing { [WProtoMember(1)] public int A; }",
                referencedRoots
            );
            Diagnostic match = diagnostics.Single(diagnostic => diagnostic.Id == "WPROTO031");

            Assert.AreEqual(DiagnosticSeverity.Warning, match.Severity);
            Assert.IsTrue(match.GetMessage().Contains("Reference.IThing"));
            Assert.IsTrue(match.GetMessage().Contains("Reference.ReferenceRoot"));
            Assert.IsTrue(match.GetMessage().Contains("Consumer.ConsumerRoot"));
            Assert.IsTrue(match.GetMessage().Contains("ReferencedRoots"));
        }

        [Test]
        public void TheSameDeclaredRootInAReferencedAssemblyDoesNotConflict()
        {
            MetadataReference referencedRoots = CompileReference(
                "ReferencedRoots",
                @"[assembly: WProtoDeclaredRoot(typeof(Reference.IThing), typeof(Reference.ReferenceRoot))]
                  namespace Reference
                  {
                      public interface IThing { }
                      [WProtoContract] public partial class ReferenceRoot : IThing { [WProtoMember(1)] public int A; }
                  }"
            );

            Assert.IsFalse(
                Run(
                        @"[assembly: WProtoDeclaredRoot(typeof(Reference.IThing), typeof(Reference.ReferenceRoot))]",
                        referencedRoots
                    )
                    .Any(diagnostic => diagnostic.Id == "WPROTO031")
            );
        }

        [Test]
        public void ConflictingDeclaredRootsInTwoReferencedAssembliesAreAWarning()
        {
            MetadataReference shared = CompileReference(
                "SharedContracts",
                "namespace Shared { public interface IThing { } }"
            );
            MetadataReference first = CompileReference(
                "FirstRoots",
                @"[assembly: WProtoDeclaredRoot(typeof(Shared.IThing), typeof(First.Root))]
                  namespace First { public sealed class Root : Shared.IThing { } }",
                shared
            );
            MetadataReference second = CompileReference(
                "SecondRoots",
                @"[assembly: WProtoDeclaredRoot(typeof(Shared.IThing), typeof(Second.Root))]
                  namespace Second { public sealed class Root : Shared.IThing { } }",
                shared
            );

            ImmutableArray<Diagnostic> diagnostics = Run("", shared, first, second);
            Diagnostic match = diagnostics.Single(diagnostic => diagnostic.Id == "WPROTO031");

            Assert.AreEqual(Location.None, match.Location);
            Assert.IsTrue(match.GetMessage().Contains("Shared.IThing"));
            Assert.IsTrue(match.GetMessage().Contains("First.Root"));
            Assert.IsTrue(match.GetMessage().Contains("Second.Root"));
            Assert.IsTrue(match.GetMessage().Contains("FirstRoots"));
            Assert.IsTrue(match.GetMessage().Contains("SecondRoots"));
        }

        /// <summary>
        /// A closure the registrar cannot name is skipped, and now says so.
        /// </summary>
        /// <remarks>
        /// Skipping is right -- naming a private nested type from the registrar is <c>CS0122</c> in
        /// the build of the assembly that declared it, which is worse than a missing registration.
        /// But until now it was invisible: the type simply threw on its first serialization, in a
        /// shipped player, naming nothing that would lead back here.
        /// </remarks>
        [Test]
        public void AClosureTheRegistrarCannotNameIsAnnounced()
        {
            const string source =
                @"[WProtoContract] public partial class Box<T> { [WProtoMember(1)] public T Value; }
                  public sealed class Holder
                  {
                      private sealed class Hidden { }
                      private Box<Hidden> _box = new Box<Hidden>();
                  }";

            ImmutableArray<Diagnostic> diagnostics = Run(source);
            Diagnostic match = diagnostics.FirstOrDefault(d => d.Id == "WPROTO028");

            Assert.IsTrue(
                match != null,
                "saw: " + string.Join("; ", diagnostics.Select(d => d.Id + " " + d.GetMessage()))
            );
            Assert.AreEqual(DiagnosticSeverity.Warning, match.Severity);
            Assert.IsTrue(
                match.GetMessage().Contains("Hidden"),
                "the message must name the part that cannot be written: " + match.GetMessage()
            );
            Assert.IsEmpty(
                CompileGenerated(source).Select(d => d.Id + " " + d.GetMessage()),
                "the point of skipping is that the consumer's build still succeeds"
            );
        }

        /// <summary>
        /// A generic contract's own declaration is unnameable too, and must stay silent.
        /// </summary>
        /// <remarks>
        /// <c>Box&lt;T&gt;</c> has no name a registrar can write either, for the same reason a
        /// private type does not. Warning about it would fire on every generic contract in the
        /// source that declares it, which is noise nobody can act on.
        /// </remarks>
        [Test]
        public void AnOpenConstructionIsNotAnnounced()
        {
            Assert.IsEmpty(
                Run(
                        @"[WProtoContract] public partial class Box<T> { [WProtoMember(1)] public T Value; }
                          public sealed class Holder<T> { private Box<T> _box; }"
                    )
                    .Where(d => d.Id == "WPROTO028")
                    .Select(d => d.GetMessage())
            );
        }

        /// <summary>
        /// A marshalled collection closed over an unnameable type is announced too.
        /// </summary>
        /// <remarks>
        /// It was not, and the reason is worth pinning: the marshal's formatter is closed over the
        /// SAME arguments as the collection, so whenever an argument is what makes the closure
        /// unnameable -- the only shape this happens in -- the formatter is unnameable too, and
        /// asking about it first short-circuited the report away. Seven live cases in this
        /// repository's own tests were skipped in silence until the two were swapped.
        /// </remarks>
        [Test]
        public void AMarshalledClosureTheRegistrarCannotNameIsAnnounced()
        {
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"[assembly: WProtoRootMarshal(typeof(Consumer.Ring<>), typeof(Consumer.RingFormatter<>))]
                  public sealed class Ring<T> { }
                  public sealed class RingFormatter<T> : IWProtoFormatter<Consumer.Ring<T>>
                  {
                      public int Measure(in Consumer.Ring<T> value) => 0;
                      public bool Write(ref WProtoWriter writer, in Consumer.Ring<T> value) => true;
                      public bool TryRead(ref WProtoReader reader, out Consumer.Ring<T> value) { value = null; return true; }
                  }
                  public sealed class Holder
                  {
                      private sealed class Hidden { }
                      private Ring<Hidden> _ring = new Ring<Hidden>();
                  }"
            );

            Diagnostic match = diagnostics.FirstOrDefault(d => d.Id == "WPROTO028");
            Assert.IsTrue(
                match != null,
                "saw: " + string.Join("; ", diagnostics.Select(d => d.Id + " " + d.GetMessage()))
            );
            Assert.IsTrue(match.GetMessage().Contains("Hidden"), match.GetMessage());
        }

        /// <summary>
        /// Shapes that are unnameable to the compiler's eye but perfectly writable, or the reverse.
        /// </summary>
        /// <remarks>
        /// <c>dynamic</c> is a keyword rather than a declaration, so <c>Box&lt;dynamic&gt;</c> has a
        /// name and warning about it offered advice ("make 'dynamic' public") nobody can take. A
        /// <c>file</c> type is the opposite: it reports <c>Internal</c> and satisfies
        /// <c>IsSymbolAccessibleWithin</c>, so the registrar emitted its name and the consumer's
        /// build failed <c>CS0234</c> — the failure the skip exists to prevent, one file over.
        /// </remarks>
        [Test]
        public void DynamicIsNameableAndAFileLocalTypeIsNot()
        {
            Assert.IsEmpty(
                Run(
                        @"[WProtoContract] public partial class Box<T> { [WProtoMember(1)] public T Value; }
                          public sealed class Holder { private Box<dynamic> _box = new Box<dynamic>(); }"
                    )
                    .Where(d => d.Id == "WPROTO028")
                    .Select(d => d.GetMessage())
            );

            const string fileLocal =
                @"[WProtoContract] public partial class Box<T> { [WProtoMember(1)] public T Value; }
                  file sealed class Hid { }
                  file sealed class FileHolder { private Box<Hid> _box = new Box<Hid>(); }";

            Assert.IsNotEmpty(
                Run(fileLocal).Where(d => d.Id == "WPROTO028").Select(d => d.GetMessage()),
                "a file-local type cannot be named from the registrar"
            );
            Assert.IsEmpty(
                CompileGenerated(fileLocal).Select(d => d.Id + " " + d.GetMessage()),
                "and skipping it is what keeps the consumer's build compiling"
            );
        }

        /// <summary>
        /// A CLOSED generic pair is the shape WPROTO026's own message tells consumers to write.
        /// </summary>
        /// <remarks>
        /// The check was `0 &lt; Arity`, and arity is the number of type parameters -- one for
        /// `IThing&lt;int&gt;` just as much as for `IThing&lt;&gt;`. So the diagnostic rejected the
        /// only form it offers as the remedy.
        /// </remarks>
        [Test]
        public void AClosedGenericDeclaredRootIsAccepted()
        {
            const string source =
                @"[assembly: WProtoDeclaredRoot(typeof(Consumer.IThing<int>), typeof(Consumer.Thing<int>))]
                  public interface IThing<T> { }
                  [WProtoContract] public partial class Thing<T> : IThing<T> { [WProtoMember(1)] public int A; }";

            Assert.IsEmpty(
                Run(source)
                    .Where(d => d.Id.StartsWith("WPROTO0", StringComparison.Ordinal))
                    .Select(d => d.Id + " " + d.GetMessage())
            );
            Assert.IsEmpty(CompileGenerated(source).Select(d => d.Id + " " + d.GetMessage()));
        }

        [Test]
        public void ADeclaredRootWithNoTypesAtAllIsReported()
        {
            // `typeof()` cannot be written, but `null` can, and the pair reader used to drop it --
            // an attribute that neither registered anything nor said why.
            AssertDiagnostic(
                "WPROTO023",
                "<missing>",
                "[assembly: WProtoDeclaredRoot(null, null)]\npublic interface IThing { }"
            );
        }

        private static IEnumerable<TestCaseData> BadDeclaredRoots()
        {
            yield return new TestCaseData(
                "WPROTO023",
                "Consumer.Stray",
                @"[assembly: WProtoDeclaredRoot(typeof(Consumer.IThing), typeof(Consumer.Stray))]
                  public interface IThing { }
                  [WProtoContract] public partial class Stray { [WProtoMember(1)] public int A; }"
            ).SetName("ARootThatIsNotAssignableToItsDeclaredTypeIsAnError");

            yield return new TestCaseData(
                "WPROTO029",
                "int",
                @"[assembly: WProtoDeclaredRoot(typeof(int), typeof(Consumer.Thing))]
                  [WProtoContract] public partial class Thing { [WProtoMember(1)] public int A; }"
            ).SetName("AValueTypeAsTheDeclaredTypeIsAnError");

            yield return new TestCaseData(
                "WPROTO024",
                "Consumer.Thing",
                @"[assembly: WProtoDeclaredRoot(typeof(Consumer.Thing), typeof(Consumer.Thing))]
                  [WProtoContract] public partial class Thing { [WProtoMember(1)] public int A; }"
            ).SetName("ARootThatIsItsOwnDeclaredTypeIsAnError");

            yield return new TestCaseData(
                "WPROTO025",
                "Consumer.Base",
                @"[assembly: WProtoDeclaredRoot(typeof(Consumer.Base), typeof(Consumer.Sub))]
                  [WProtoContract] [WProtoInclude(100, typeof(Consumer.Sub))] public partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] public partial class Sub : Base { [WProtoMember(1)] public int B; }"
            ).SetName("ADeclaredTypeThatIsItselfAContractIsAnError");

            yield return new TestCaseData(
                "WPROTO026",
                "Consumer.IThing",
                @"[assembly: WProtoDeclaredRoot(typeof(Consumer.IThing<>), typeof(Consumer.Thing<>))]
                  public interface IThing<T> { }
                  [WProtoContract] public partial class Thing<T> : IThing<T> { [WProtoMember(1)] public int A; }"
            ).SetName("AGenericDeclaredRootIsAnError");

            yield return new TestCaseData(
                "WPROTO029",
                "Consumer.Plain",
                @"[assembly: WProtoDeclaredRoot(typeof(Consumer.Plain), typeof(Consumer.Sub))]
                  public class Plain { }
                  [WProtoContract] public partial class Sub : Plain { [WProtoMember(1)] public int A; }"
            ).SetName("ADeclaredTypeAValueCanBeIsAnError");

            yield return new TestCaseData(
                "WPROTO029",
                "Consumer.IThing[]",
                @"[assembly: WProtoDeclaredRoot(typeof(Consumer.IThing[]), typeof(Consumer.Thing[]))]
                  public interface IThing { }
                  [WProtoContract] public partial class Thing : IThing { [WProtoMember(1)] public int A; }"
            ).SetName("AnArrayAsTheDeclaredTypeIsAnError");

            yield return new TestCaseData(
                "WPROTO027",
                "Consumer.IThing",
                @"[assembly: WProtoDeclaredRoot(typeof(Consumer.IThing), typeof(Consumer.First))]
                  [assembly: WProtoDeclaredRoot(typeof(Consumer.IThing), typeof(Consumer.Second))]
                  public interface IThing { }
                  [WProtoContract] public partial class First : IThing { [WProtoMember(1)] public int A; }
                  [WProtoContract] public partial class Second : IThing { [WProtoMember(1)] public int B; }"
            ).SetName("TwoRootsForOneDeclaredTypeIsAnError");
        }

        /// <summary>
        /// Every type protobuf has no <c>sint</c> form for is refused rather than quietly widened.
        /// </summary>
        /// <remarks>
        /// The alternative reading -- drop the annotation and encode the member the default way --
        /// is what makes this an error instead of a warning. A dropped <c>DataFormat</c> is not a
        /// missing optimization; it is a member encoded as the thing its author wrote down that it
        /// was not, and the two are different bytes that no round trip in this suite would notice.
        /// </remarks>
        [TestCase("uint Value", TestName = "AZigZagUnsignedIntegerIsAnError")]
        [TestCase("float Value", TestName = "AZigZagFloatIsAnError")]
        [TestCase("string Value", TestName = "AZigZagStringIsAnError")]
        [TestCase("bool Value", TestName = "AZigZagBoolIsAnError")]
        [TestCase("int[] Value", TestName = "AZigZagRepeatedMemberIsAnError")]
        public void AZigZagOnATypeWithNoSuchEncodingIsAnError(string member)
        {
            AssertDiagnostic(
                "WPROTO037",
                "Value",
                "[WProtoContract] public sealed partial class Wrong { "
                    + "[WProtoMember(1, DataFormat = WProtoDataFormat.ZigZag)] public "
                    + member
                    + "; }"
            );
        }

        [TestCase("sbyte Value", TestName = "AZigZagSByteIsAccepted")]
        [TestCase("short Value", TestName = "AZigZagInt16IsAccepted")]
        [TestCase("int Value", TestName = "AZigZagInt32IsAccepted")]
        [TestCase("long Value", TestName = "AZigZagInt64IsAccepted")]
        [TestCase("int? Value", TestName = "AZigZagNullableInt32IsAccepted")]
        public void AZigZagOnASignedIntegerIsAccepted(string member)
        {
            Assert.IsEmpty(
                Run(
                    "[WProtoContract] public sealed partial class Fine { "
                        + "[WProtoMember(1, DataFormat = WProtoDataFormat.ZigZag)] public "
                        + member
                        + "; }"
                )
            );
        }

        private static void AssertDiagnostic(string id, string mustName, string source)
        {
            ImmutableArray<Diagnostic> diagnostics = Run(source);
            Diagnostic match = diagnostics.FirstOrDefault(d => d.Id == id);

            Assert.IsTrue(
                match != null,
                "expected "
                    + id
                    + ", saw: "
                    + string.Join("; ", diagnostics.Select(d => d.Id + " " + d.GetMessage()))
            );
            Assert.AreEqual(DiagnosticSeverity.Error, match.Severity);
            Assert.IsTrue(
                match.GetMessage().Contains(mustName),
                "the message must name '" + mustName + "': " + match.GetMessage()
            );
        }

        private static ImmutableArray<Diagnostic> Run(string body)
        {
            return Run(body, Array.Empty<MetadataReference>(), out Compilation _);
        }

        private static ImmutableArray<Diagnostic> Run(
            string body,
            params MetadataReference[] additionalReferences
        )
        {
            return Run(body, additionalReferences, out Compilation _);
        }

        private static ImmutableArray<Diagnostic> Run(string body, out Compilation generated)
        {
            return Run(body, Array.Empty<MetadataReference>(), out generated);
        }

        private static ImmutableArray<Diagnostic> Run(
            string body,
            IReadOnlyCollection<MetadataReference> additionalReferences,
            out Compilation generated
        )
        {
            // An [assembly:] attribute must precede every namespace, so a fixture that needs one
            // writes it at the top of its body and it is hoisted out here rather than ending up
            // inside `namespace Consumer` where it would not compile.
            List<string> assemblyAttributes = new List<string>();
            List<string> rest = new List<string>();
            foreach (string line in body.Split('\n'))
            {
                (
                    line.TrimStart().StartsWith("[assembly:", StringComparison.Ordinal)
                        ? assemblyAttributes
                        : rest
                ).Add(line);
            }

            string source =
                "using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;\n"
                + string.Join("\n", assemblyAttributes)
                + "\nnamespace Consumer { using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto; "
                + string.Join("\n", rest)
                + " }";

            List<MetadataReference> references = new List<MetadataReference>();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            references.AddRange(additionalReferences);

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
                    out ImmutableArray<Diagnostic> diagnostics
                );

            generated = updated;
            return diagnostics;
        }

        private static MetadataReference CompileReference(
            string assemblyName,
            string body,
            params MetadataReference[] additionalReferences
        )
        {
            List<MetadataReference> references = new List<MetadataReference>();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            references.AddRange(additionalReferences);

            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName,
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        "using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;\n"
                            + body
                    ),
                },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            using (MemoryStream stream = new MemoryStream())
            {
                Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
                Assert.IsTrue(
                    result.Success,
                    string.Join("; ", result.Diagnostics.Select(d => d.Id + " " + d.GetMessage()))
                );
                return MetadataReference.CreateFromImage(stream.ToArray());
            }
        }

        /// <summary>
        /// Compiles the generator's own output and returns its errors.
        /// </summary>
        /// <remarks>
        /// The generator reporting no diagnostic is not the same as the consumer's build succeeding.
        /// Emitted code that does not compile is the failure a developer actually hits, and it is
        /// invisible to a suite that only inspects what the generator chose to report.
        /// </remarks>
        private static ImmutableArray<Diagnostic> CompileGenerated(string body)
        {
            Run(body, out Compilation generated);
            return generated
                .GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();
        }
    }
}
