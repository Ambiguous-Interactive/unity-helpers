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

        // Every one of these is a shape a developer would reasonably expect to work, which is why it
        // has to fail the build with a message rather than silently get no formatter.
        //
        // LinkedList and
        // ReadOnlyCollection implement ICollection<T> with an explicit Add, so nothing can fill
        // them; Queue and Stack do not implement it at all. The rest are element-shape refusals: a
        // jagged array of anything but bytes, a rank-2 array, and a nullable element (protobuf-net
        // refuses a null element, so Nullable<T>[] is a collection that can only hold values it
        // cannot write).
        [TestCase("System.Collections.Generic.LinkedList<int>")]
        [TestCase("System.Collections.ObjectModel.ReadOnlyCollection<int>")]
        [TestCase("System.Collections.Generic.Queue<int>")]
        [TestCase("System.Collections.Generic.Stack<int>")]
        [TestCase("System.Collections.Generic.IList<int>")]
        [TestCase("System.Collections.Generic.List<System.Collections.Generic.List<int>>")]
        [TestCase("int[][]")]
        [TestCase("int[,]")]
        [TestCase("int?[]")]
        [TestCase("System.DateTime")]
        public void AnUnsupportedMemberTypeIsAnError(string declaredType)
        {
            AssertDiagnostic(
                "WPROTO003",
                "Values",
                @"[WProtoContract] public sealed partial class Unsupported
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
            AssertNoDiagnostics(
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

        private static void AssertDiagnostic(string id, string mustName, string source)
        {
            ImmutableArray<Diagnostic> diagnostics = Run(source);
            Diagnostic match = diagnostics.FirstOrDefault(d => d.Id == id);

            Assert.IsNotNull(
                match,
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
