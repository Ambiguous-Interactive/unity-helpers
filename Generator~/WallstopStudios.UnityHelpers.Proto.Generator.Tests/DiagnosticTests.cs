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

            Assert.IsNotNull(
                match,
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
            Assert.IsNotNull(
                match,
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
            return Run(body, out Compilation _);
        }

        private static ImmutableArray<Diagnostic> Run(string body, out Compilation generated)
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
                    out Compilation updated,
                    out ImmutableArray<Diagnostic> diagnostics
                );

            generated = updated;
            return diagnostics;
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
