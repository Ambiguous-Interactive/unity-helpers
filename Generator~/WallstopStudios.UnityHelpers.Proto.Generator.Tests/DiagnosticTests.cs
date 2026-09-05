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
        /// A subtype neither end declared is discovered rather than refused.
        /// </summary>
        /// <remarks>
        /// This was WPROTO018, and it was the right diagnostic for a design where the relationship
        /// had to be written down. Deriving from a contract is the declaration now, so what is left
        /// is the number -- WPROTO041, fixed by running the assigner rather than by editing source
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/613">#613</see>).
        /// </remarks>
        [Test]
        public void ASubtypeNeitherEndDeclaresOnlyNeedsANumber()
        {
            AssertDiagnostic(
                "WPROTO041",
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

        /// <summary>
        /// The subtype may declare the relationship itself, and then nothing else has to.
        /// </summary>
        /// <remarks>
        /// The whole point of the second form: a base that does not know its own subtypes is not a
        /// build error, so a hierarchy can be extended without editing the type at its root.
        /// </remarks>
        [Test]
        public void ASubtypeThatDeclaresItselfNeedsNothingOnItsBase()
        {
            Assert.IsEmpty(
                Run(
                    @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
                      [WProtoContract] [WProtoSubtype(typeof(Base), 100)] public partial class Sub : Base { [WProtoMember(1)] public int B; }"
                )
            );
        }

        [Test]
        public void ADeclarationFromEitherEndEmitsCodeThatCompiles()
        {
            /*
             * Include relationships emit into the base formatter, so validate generated compilation as well
             * as diagnostics.
             */
            Assert.IsEmpty(
                CompileGenerated(
                    @"[WProtoContract] [WProtoInclude(100, typeof(Alpha))] public partial class Base { [WProtoMember(1)] public int A; }
                      [WProtoContract] public partial class Alpha : Base { [WProtoMember(1)] public int B; }
                      [WProtoContract] [WProtoSubtype(typeof(Base), 101)] public partial class Beta : Base { [WProtoMember(1)] public int C; }
                      [WProtoContract] [WProtoSubtype(typeof(Beta), 200)] public partial class Gamma : Beta { [WProtoMember(1)] public int D; }"
                )
            );
        }

        [Test]
        public void AnAbstractContractIsSatisfiedByASubtypeThatDeclaresItself()
        {
            Assert.IsEmpty(
                Run(
                    @"[WProtoContract] public abstract partial class Base { [WProtoMember(1)] public int A; }
                      [WProtoContract] [WProtoSubtype(typeof(Base), 100)] public partial class Sub : Base { [WProtoMember(1)] public int B; }"
                )
            );
        }

        /// <summary>
        /// An enum member taking the value a deleted member held is refused.
        /// </summary>
        /// <remarks>
        /// The #606/#608 hazard one enforcement point over. An enum goes on the wire as a varint of
        /// its underlying value, so deleting <c>Poisoned = 3</c> and adding <c>Frozen = 3</c> reads
        /// every older payload back as <c>Frozen</c>, and the deleted declaration was the only
        /// record that 3 was spent
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/609">#609</see>).
        /// </remarks>
        [Test]
        public void AnEnumMemberTakingAReservedValueIsAnError()
        {
            AssertDiagnostic(
                "WPROTO046",
                "Frozen",
                @"[WProtoReserved(3)] public enum Status { None = 0, Frozen = 3 }
                  [WProtoContract] public partial class Holder { [WProtoMember(1)] public Status State; }"
            );
        }

        [Test]
        public void AnEnumMemberTakingAReservedNameIsAnError()
        {
            AssertDiagnostic(
                "WPROTO046",
                "Poisoned",
                @"[WProtoReserved(""Poisoned"")] public enum Status { None = 0, Poisoned = 7 }
                  [WProtoContract] public partial class Holder { [WProtoMember(1)] public Status State; }"
            );
        }

        [Test]
        public void TheReservedEnumErrorNamesTheValueAndTheNameTogether()
        {
            Diagnostic match = Run(
                    @"[WProtoReserved(3)] [WProtoReserved(""Poisoned"")] public enum Status { None = 0, Poisoned = 3 }"
                )
                .Single(diagnostic => diagnostic.Id == "WPROTO046");

            Assert.IsTrue(match.GetMessage().Contains("the value 3"), match.GetMessage());
            Assert.IsTrue(match.GetMessage().Contains("'Poisoned'"), match.GetMessage());
        }

        [Test]
        public void AnEnumMemberOnAFreeValueIsFine()
        {
            Assert.IsEmpty(
                Run(
                    @"[WProtoReserved(3)] [WProtoReserved(""Poisoned"")] public enum Status { None = 0, Frozen = 4 }
                      [WProtoContract] public partial class Holder { [WProtoMember(1)] public Status State; }"
                )
            );
        }

        /// <summary>
        /// Zero is a legal enum reservation, and it is the one the message range would have eaten.
        /// </summary>
        /// <remarks>
        /// A field number starts at 1, so a shared range filter would have dropped a reservation of
        /// 0 silently. An enum value is any int32, and 0 is the value proto3 most wants pinned.
        /// </remarks>
        [Test]
        public void AnEnumMayReserveZero()
        {
            AssertDiagnostic(
                "WPROTO046",
                "Unset",
                @"[WProtoReserved(0)] public enum Status { Unset = 0, Frozen = 4 }"
            );
        }

        [Test]
        public void AnEnumMayReserveANegativeValue()
        {
            AssertDiagnostic(
                "WPROTO046",
                "Invalid",
                @"[WProtoReserved(-1)] public enum Status { None = 0, Invalid = -1 }"
            );
        }

        /// <summary>
        /// A reservation binds every member sharing the value, aliases included.
        /// </summary>
        [Test]
        public void EveryAliasOfAReservedValueIsRefused()
        {
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"[WProtoReserved(3)] public enum Status { None = 0, Frozen = 3, Chilled = 3 }"
            );

            Assert.AreEqual(
                2,
                diagnostics.Count(diagnostic => diagnostic.Id == "WPROTO046"),
                string.Join("\n", diagnostics.Select(diagnostic => diagnostic.GetMessage()))
            );
        }

        /// <summary>
        /// A value no <c>int</c> can express cannot collide with a reservation, which is written as
        /// one -- and must not be forced through a signed conversion that throws.
        /// </summary>
        [Test]
        public void AnEnumValueTooWideForAReservationIsNotRefused()
        {
            Assert.IsEmpty(
                Run(
                    @"[WProtoReserved(3)] public enum Wide : ulong { None = 0, Huge = 18446744073709551615 }"
                )
            );
        }

        [Test]
        public void AnEnumWithoutAReservationIsNotWalked()
        {
            Assert.IsEmpty(
                Run(
                    @"public enum Status { None = 0, Frozen = 3 }
                      [WProtoContract] public partial class Holder { [WProtoMember(1)] public Status State; }"
                )
            );
        }

        /// <summary>
        /// Deriving from a contract IS the declaration; the only thing missing is a number.
        /// </summary>
        /// <remarks>
        /// This shape used to compile clean and throw on the first save, then briefly became a
        /// build error demanding two attributes. Neither is a pit of success: an attribute you can
        /// forget to write should not decide whether a save works
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/613">#613</see>).
        /// It is now discovered, and what remains is <c>WPROTO041</c> -- the same refusal a tag-less
        /// <c>[WProtoSubtype]</c> gets, fixed by running the assigner rather than by editing source.
        /// </remarks>
        [Test]
        public void AnUnannotatedSubclassOfAContractIsDiscoveredAndOnlyNeedsANumber()
        {
            AssertDiagnostic(
                "WPROTO041",
                "PlasmaCutter",
                @"[WProtoContract] public partial class Weapon { [WProtoMember(1)] public int Damage; }
                  public partial class PlasmaCutter : Weapon { public float Charge; }"
            );
        }

        [Test]
        public void TheInheritedSubtypeMessageDoesNotNameAnAttributeNobodyWrote()
        {
            // Manifest-provided tags must not be described as explicitly declared subtype tags.
            Diagnostic match = Run(
                    @"[WProtoContract] public partial class Weapon { [WProtoMember(1)] public int Damage; }
                      public partial class PlasmaCutter : Weapon { public float Charge; }"
                )
                .Single(diagnostic => diagnostic.Id == "WPROTO041");

            Assert.IsTrue(match.GetMessage().Contains("derives from"), match.GetMessage());
            Assert.IsFalse(
                match.GetMessage().Contains("declares [WProtoSubtype"),
                match.GetMessage()
            );
        }

        /// <summary>
        /// The end state the assigner produces: no attribute on the subclass, and nothing refused.
        /// </summary>
        /// <remarks>
        /// The advisory <c>WPROTO047</c> is the only thing reported, and it is a warning about
        /// legibility rather than a refusal -- the build succeeds and the type round-trips. Asserted
        /// as "no errors" rather than "no diagnostics", so a future advisory does not have to
        /// rewrite this test to stay true.
        /// </remarks>
        [Test]
        public void AnUnannotatedSubclassWithACommittedNumberIsNotRefused()
        {
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"[assembly: WProtoSubtypeTag(""Consumer.PlasmaCutter"", typeof(Consumer.Weapon), 100)]
                  [WProtoContract] public partial class Weapon { [WProtoMember(1)] public int Damage; }
                  public partial class PlasmaCutter : Weapon { [WProtoMember(1)] public float Charge; }"
            );

            Assert.IsEmpty(
                diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                string.Join("\n", diagnostics.Select(diagnostic => diagnostic.GetMessage()))
            );
        }

        /// <summary>
        /// The base having no <c>[WProtoInclude]</c> of its own does not make it safe.
        /// </summary>
        /// <remarks>
        /// <c>EmitIncludeDispatch</c> writes the closing guard for every unsealed reference-type
        /// contract, chain or no chain, so a leaf contract refuses an undeclared runtime type
        /// exactly as a base with subtypes does.
        /// </remarks>
        [Test]
        public void AnUnannotatedSubclassOfALeafContractIsDiscoveredToo()
        {
            AssertDiagnostic(
                "WPROTO041",
                "Derived",
                @"[WProtoContract] public partial class Leaf { [WProtoMember(1)] public int A; }
                  public sealed class Derived : Leaf { }"
            );
        }

        /// <summary>
        /// A subclass of a GENERIC contract is not reported, because none of the fixes exist there.
        /// </summary>
        /// <remarks>
        /// WPROTO040 refuses a <c>[WProtoSubtype]</c> naming a generic base -- one field number
        /// cannot identify a type that is really as many types as it has closures -- so the only
        /// remedy left would be an opt-out on every subclass.
        /// <c>SerializableDictionary.Cache&lt;T&gt;</c> is exactly this shape, and the package's own
        /// documentation instructs every consumer to subclass it, so reporting would put
        /// boilerplate on every consumer of a shipped feature. CI found this before it shipped.
        /// </remarks>
        [Test]
        public void ASubclassOfAGenericContractIsNotReported()
        {
            Assert.IsEmpty(
                Run(
                    @"[WProtoContract] public partial class Box<T> { [WProtoMember(1)] public T Data; }
                      public sealed class IntBoxCache : Box<int> { }"
                )
            );
        }

        /// <summary>
        /// A type that inherits its contract and declares wire members is advised, not refused.
        /// </summary>
        /// <remarks>
        /// The code works -- that is the whole point of deriving-is-declaring -- so this is a
        /// warning about legibility: a reader seeing <c>[WProtoMember(1)]</c> on a field has no way
        /// to know the type is serialized without opening its base.
        /// </remarks>
        [Test]
        public void AnInheritedContractWithMembersOfItsOwnIsAdvisedToSaySo()
        {
            Diagnostic match = Run(
                    @"[assembly: WProtoSubtypeTag(""Consumer.PlasmaCutter"", typeof(Consumer.Weapon), 100)]
                      [WProtoContract] public partial class Weapon { [WProtoMember(1)] public int Damage; }
                      public partial class PlasmaCutter : Weapon { [WProtoMember(1)] public float Charge; }"
                )
                .Single(diagnostic => diagnostic.Id == "WPROTO047");

            Assert.AreEqual(DiagnosticSeverity.Warning, match.Severity, match.GetMessage());
            Assert.IsTrue(match.GetMessage().Contains("WProtoContract"), match.GetMessage());
            Assert.IsTrue(match.GetMessage().Contains("Charge"), match.GetMessage());
        }

        /// <summary>
        /// A subclass that adds only behaviour is not asked for an attribute.
        /// </summary>
        /// <remarks>
        /// Deriving to add behaviour is the ordinary reason to derive from anything. Advising every
        /// one of them would be the noise this design removed, so the warning is gated on the type
        /// declaring a wire member of its own.
        /// </remarks>
        [Test]
        public void AnInheritedContractWithNoMembersOfItsOwnIsLeftAlone()
        {
            Assert.IsEmpty(
                Run(
                    @"[assembly: WProtoSubtypeTag(""Consumer.PreviewWeapon"", typeof(Consumer.Weapon), 100)]
                      [WProtoContract] public partial class Weapon { [WProtoMember(1)] public int Damage; }
                      public partial class PreviewWeapon : Weapon { public float Charge; }"
                )
            );
        }

        [Test]
        public void DeclaringTheContractSilencesTheAdvice()
        {
            Assert.IsEmpty(
                Run(
                    @"[assembly: WProtoSubtypeTag(""Consumer.PlasmaCutter"", typeof(Consumer.Weapon), 100)]
                      [WProtoContract] public partial class Weapon { [WProtoMember(1)] public int Damage; }
                      [WProtoContract] public partial class PlasmaCutter : Weapon { [WProtoMember(1)] public float Charge; }"
                )
            );
        }

        /// <summary>
        /// A grandchild of an IMPLICIT middle is serialized, and is numbered against that middle.
        /// </summary>
        /// <remarks>
        /// The classification walks the chain, so <c>C</c> in <c>A(contract) &lt;- B &lt;- C</c> is a
        /// contract even though neither B nor C carries an attribute. Its number belongs to the
        /// (C, B) pair, because an include names a DIRECT subtype.
        /// </remarks>
        [Test]
        public void AGrandchildOfAnImplicitMiddleIsNumberedAgainstThatMiddle()
        {
            Diagnostic match = Run(
                    @"[assembly: WProtoSubtypeTag(""Consumer.Middle"", typeof(Consumer.Root), 100)]
                      [WProtoContract] public partial class Root { [WProtoMember(1)] public int A; }
                      public partial class Middle : Root { }
                      public partial class Leaf : Middle { }"
                )
                .Single(diagnostic =>
                    diagnostic.Id == "WPROTO041" && diagnostic.GetMessage().Contains("Leaf")
                );

            Assert.IsTrue(match.GetMessage().Contains("Middle"), match.GetMessage());
        }

        /// <summary>
        /// A three-level implicit hierarchy with every number committed reports no errors.
        /// </summary>
        /// <remarks>
        /// The end state the assigner has to be able to reach. It could not: its sweep inventoried
        /// only direct children of types CARRYING the attribute, so a grandchild of an implicit
        /// middle never received an entry, its editor warning never cleared, and its player build
        /// stayed refused. Found by review on this branch.
        /// </remarks>
        [Test]
        public void AThreeLevelImplicitHierarchyIsNotRefusedOnceNumbered()
        {
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"[assembly: WProtoSubtypeTag(""Consumer.Middle"", typeof(Consumer.Root), 100)]
                  [assembly: WProtoSubtypeTag(""Consumer.Leaf"", typeof(Consumer.Middle), 100)]
                  [WProtoContract] public partial class Root { [WProtoMember(1)] public int A; }
                  public partial class Middle : Root { }
                  public partial class Leaf : Middle { }"
            );

            Assert.IsEmpty(
                diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                string.Join("\n", diagnostics.Select(diagnostic => diagnostic.GetMessage()))
            );
        }

        /// <summary>
        /// A <c>[WProtoSubtype]</c> naming an implicit base is honoured.
        /// </summary>
        /// <remarks>
        /// The fix WPROTO041 suggests -- write the number yourself -- read the base's attribute
        /// directly and so rejected an implicit one with "is not itself a [WProtoContract]". That
        /// left the hierarchies this design introduced with no working manual escape.
        /// </remarks>
        [Test]
        public void ASubtypeDeclarationMayNameAnImplicitBase()
        {
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"[assembly: WProtoSubtypeTag(""Consumer.Middle"", typeof(Consumer.Root), 100)]
                  [WProtoContract] public partial class Root { [WProtoMember(1)] public int A; }
                  public partial class Middle : Root { }
                  [WProtoSubtype(typeof(Middle), 200)] public partial class Leaf : Middle { }"
            );

            Assert.IsEmpty(
                diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                string.Join("\n", diagnostics.Select(diagnostic => diagnostic.GetMessage()))
            );
        }

        /// <summary>
        /// An explicit ordinal still wins, and still collides loudly, under implicit discovery.
        /// </summary>
        /// <remarks>
        /// Deriving supplies a number from the manifest; writing one supplies it directly. Both
        /// produce an ordinary <c>Include</c>, so the duplicate check sees one kind of thing --
        /// which is what makes a hand-written number and a committed one collide at build time
        /// rather than at read time.
        /// </remarks>
        [Test]
        public void AnExplicitOrdinalCollidingWithACommittedOneIsRefused()
        {
            AssertDiagnostic(
                "WPROTO039",
                "100",
                @"[assembly: WProtoSubtypeTag(""Consumer.Committed"", typeof(Consumer.Base), 100)]
                  [WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
                  public partial class Committed : Base { }
                  [WProtoSubtype(typeof(Base), 100)] public partial class Written : Base { }"
            );
        }

        [Test]
        public void TwoExplicitOrdinalsThatCollideAreRefused()
        {
            AssertDiagnostic(
                "WPROTO039",
                "100",
                @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoSubtype(typeof(Base), 100)] public partial class First : Base { }
                  [WProtoSubtype(typeof(Base), 100)] public partial class Second : Base { }"
            );
        }

        /// <summary>
        /// An explicit ordinal beside implicit siblings is honoured rather than renumbered.
        /// </summary>
        [Test]
        public void AnExplicitOrdinalIsKeptBesideDiscoveredSiblings()
        {
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"[assembly: WProtoSubtypeTag(""Consumer.Discovered"", typeof(Consumer.Base), 101)]
                  [WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
                  public partial class Discovered : Base { }
                  [WProtoSubtype(typeof(Base), 100)] public partial class Pinned : Base { }"
            );

            Assert.IsEmpty(
                diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                string.Join("\n", diagnostics.Select(diagnostic => diagnostic.GetMessage()))
            );
        }

        [Test]
        public void TheOptOutSilencesTheUnannotatedSubclassError()
        {
            Assert.IsEmpty(
                Run(
                    @"[WProtoContract] public partial class Weapon { [WProtoMember(1)] public int Damage; }
                      [WProtoNotSerialized] public partial class PreviewWeapon : Weapon { public float Charge; }"
                )
            );
        }

        /// <summary>
        /// The opt-out records a decision about ONE type, and says nothing about its descendants.
        /// </summary>
        /// <remarks>
        /// Only the direct base is consulted, so a subclass of an opted-out type has no contract
        /// base and is not asked. Nothing writes the opted-out type as the contract, so nothing
        /// writes its subclasses as the contract either.
        /// </remarks>
        [Test]
        public void ASubclassOfAnOptedOutTypeIsNotAsked()
        {
            Assert.IsEmpty(
                Run(
                    @"[WProtoContract] public partial class Weapon { [WProtoMember(1)] public int Damage; }
                      [WProtoNotSerialized] public partial class PreviewWeapon : Weapon { }
                      public sealed class DebugPreviewWeapon : PreviewWeapon { }"
                )
            );
        }

        /// <summary>
        /// A declared subtype's own undeclared subclass is still refused.
        /// </summary>
        /// <remarks>
        /// An include names a DIRECT subtype, so a grandchild reaches its parent's chain and its
        /// parent's guard. Asking only about the root would have missed exactly the type that
        /// throws.
        /// </remarks>
        [Test]
        public void AnUnannotatedSubclassOfADeclaredSubtypeIsDiscoveredToo()
        {
            AssertDiagnostic(
                "WPROTO041",
                "Grandchild",
                @"[WProtoContract] [WProtoInclude(100, typeof(Middle))] public partial class Root { [WProtoMember(1)] public int A; }
                  [WProtoContract] public partial class Middle : Root { [WProtoMember(1)] public int B; }
                  public sealed class Grandchild : Middle { }"
            );
        }

        /// <summary>
        /// One mistake gets one code: an include naming a non-contract is WPROTO013, on the
        /// declaration the author actually wrote.
        /// </summary>
        [Test]
        public void ASubclassTheBaseAlreadyNamesIsRefusedOnlyByTheIncludeDiagnostic()
        {
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"[WProtoContract] [WProtoInclude(100, typeof(Sub))] public partial class Base { [WProtoMember(1)] public int A; }
                  public sealed class Sub : Base { }"
            );

            Assert.IsNotEmpty(diagnostics.Where(diagnostic => diagnostic.Id == "WPROTO013"));
            Assert.IsEmpty(diagnostics.Where(diagnostic => diagnostic.Id == "WPROTO044"));
        }

        /// <summary>
        /// A <c>[WProtoSubtype]</c> without a contract is WPROTO040's subject, not this one.
        /// </summary>
        [Test]
        public void ASubclassThatNumbersItselfNeedsNoContractAttribute()
        {
            Assert.IsEmpty(
                Run(
                    @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
                      [WProtoSubtype(typeof(Base), 100)] public sealed partial class Sub : Base { }"
                )
            );
        }

        [TestCase(
            "[WProtoContract]",
            "[WProtoContract]",
            TestName = "TheOptOutBesideAContractIsRefused"
        )]
        [TestCase(
            "[WProtoSubtype(typeof(Weapon), 100)]",
            "[WProtoSubtype]",
            TestName = "TheOptOutBesideASubtypeDeclarationIsRefused"
        )]
        public void TheOptOutBesideAContradictoryDeclarationIsRefused(
            string declaration,
            string named
        )
        {
            Diagnostic match = Run(
                    @"[WProtoContract] public partial class Weapon { [WProtoMember(1)] public int Damage; }
                      [WProtoNotSerialized] "
                        + declaration
                        + @" public partial class PlasmaCutter : Weapon { [WProtoMember(1)] public float Charge; }"
                )
                .Single(diagnostic => diagnostic.Id == "WPROTO045");

            Assert.IsTrue(match.GetMessage().Contains(named), match.GetMessage());
        }

        /// <summary>
        /// A partial whose halves disagree about attributes is one type, and is decided once.
        /// </summary>
        /// <remarks>
        /// The receiver sorts DECLARATIONS, not types: the half carrying the opt-out lands in one
        /// list and the bare half in the other. Nothing but the shared <c>seen</c> set stops the
        /// bare half being reported as an undeclared subclass of its own base.
        /// </remarks>
        [Test]
        public void APartialSubclassIsDecidedOnceAcrossItsDeclarations()
        {
            Assert.IsEmpty(
                Run(
                    @"[WProtoContract] public partial class Weapon { [WProtoMember(1)] public int Damage; }
                      [WProtoNotSerialized] public partial class PreviewWeapon : Weapon { }
                      public partial class PreviewWeapon : Weapon { public float Charge; }"
                )
            );
        }

        [TestCase(
            @"[WProtoContract] [WProtoInclude(55, typeof(First))] public partial class Base { [WProtoMember(1)] public int A; }
              [WProtoContract] public partial class First : Base { [WProtoMember(1)] public int B; }
              [WProtoContract] [WProtoSubtype(typeof(Base), 55)] public partial class Second : Base { [WProtoMember(1)] public int C; }",
            TestName = "ASubtypeCannotTakeAFieldNumberAnIncludeAlreadyHas"
        )]
        [TestCase(
            @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
              [WProtoContract] [WProtoSubtype(typeof(Base), 55)] public partial class First : Base { [WProtoMember(1)] public int B; }
              [WProtoContract] [WProtoSubtype(typeof(Base), 55)] public partial class Second : Base { [WProtoMember(1)] public int C; }",
            TestName = "TwoSubtypesCannotTakeTheSameFieldNumber"
        )]
        public void OneFieldNumberCannotIdentifyTwoSubtypes(string source)
        {
            Diagnostic match = Run(source).Single(diagnostic => diagnostic.Id == "WPROTO039");

            Assert.AreEqual(DiagnosticSeverity.Error, match.Severity);
            Assert.IsTrue(match.GetMessage().Contains("First"), match.GetMessage());
            Assert.IsTrue(match.GetMessage().Contains("Second"), match.GetMessage());
            Assert.IsTrue(match.GetMessage().Contains("55"), match.GetMessage());
            Assert.IsTrue(match.GetMessage().Contains("Base"), match.GetMessage());
        }

        [TestCase(
            @"[WProtoContract] public partial class Root { [WProtoMember(1)] public int A; }
              [WProtoContract] [WProtoSubtype(typeof(Root), 5)] public partial class Middle : Root { [WProtoMember(1)] public int B; }
              [WProtoContract] [WProtoSubtype(typeof(Root), 6)] public partial class Leaf : Middle { [WProtoMember(1)] public int C; }",
            "Leaf",
            TestName = "AGrandparentIsNotSilentlyReParentedOntoTheDirectBase"
        )]
        [TestCase(
            @"public partial class Plain { }
              [WProtoContract] [WProtoSubtype(typeof(Plain), 5)] public partial class Sub : Plain { [WProtoMember(1)] public int B; }",
            "Plain",
            TestName = "ABaseThatIsNotAContractIsRefused"
        )]
        [TestCase(
            @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
              [WProtoContract] [WProtoSubtype(typeof(Base), 0)] public partial class Sub : Base { [WProtoMember(1)] public int B; }",
            "Sub",
            TestName = "AFieldNumberBelowOneIsRefused"
        )]
        [TestCase(
            @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
              [WProtoContract] [WProtoSubtype(typeof(Base), 19500)] public partial class Sub : Base { [WProtoMember(1)] public int B; }",
            "Sub",
            TestName = "AFieldNumberInTheReservedRangeIsRefused"
        )]
        [TestCase(
            @"[WProtoContract] public partial class Base { [WProtoMember(5)] public int A; }
              [WProtoContract] [WProtoSubtype(typeof(Base), 5)] public partial class Sub : Base { [WProtoMember(1)] public int B; }",
            "Sub",
            TestName = "AFieldNumberAMemberAlreadyHoldsIsRefused"
        )]
        [TestCase(
            @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
              [WProtoContract] [WProtoSubtype(typeof(Base), 5)] public partial class Sub<T> : Base { [WProtoMember(1)] public int B; }",
            "Sub",
            TestName = "AGenericSubtypeHasNoSingleFieldNumberAndIsRefused"
        )]
        [TestCase(
            @"[WProtoContract] public partial class Base<T> { [WProtoMember(1)] public int A; }
              [WProtoContract] [WProtoSubtype(typeof(Base<int>), 5)] public partial class Sub : Base<int> { [WProtoMember(1)] public int B; }",
            "Base",
            TestName = "AConstructedGenericBaseIsRefusedRatherThanDropped"
        )]
        public void AnUnusableSubtypeDeclarationIsAnError(string source, string mustName)
        {
            AssertDiagnostic("WPROTO040", mustName, source);
        }

        /// <summary>
        /// A numbered declaration needs no <c>[WProtoContract]</c> beside it any more.
        /// </summary>
        /// <remarks>
        /// This was refused, and correctly, while a subtype had to carry its own contract: the base
        /// would have had a field number pointing at a type with no formatter. Deriving from a
        /// contract is the declaration now, so the formatter exists and the number is honoured
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/613">#613</see>).
        /// </remarks>
        [Test]
        public void ASubtypeDeclarationNeedsNoContractAttributeBesideIt()
        {
            Assert.IsEmpty(
                Run(
                    @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
                      [WProtoSubtype(typeof(Base), 5)] public partial class Sub : Base { }"
                )
            );
        }

        /// <summary>
        /// One unusable declaration among several does not orphan the usable ones.
        /// </summary>
        /// <remarks>
        /// <c>[WProtoSubtype]</c> is <c>AllowMultiple</c>, so a type can carry a good declaration
        /// and a bad one at once. The good one is indexed into the base's include set before the
        /// subtype is emitted, and the bad one then stops the subtype being emitted at all -- which
        /// left the base's dispatch chain naming a nested formatter that does not exist. The
        /// developer saw a CS error inside generated code they cannot open, beside the WPROTO040
        /// that actually says what to fix.
        /// </remarks>
        [Test]
        public void AnUnusableSubtypeDeclarationBesideAUsableOneLeavesNoOrphanedInclude()
        {
            string source =
                @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] public partial class Unrelated { [WProtoMember(1)] public int U; }
                  [WProtoContract]
                  [WProtoSubtype(typeof(Base), 5)]
                  [WProtoSubtype(typeof(Unrelated), 6)]
                  public partial class Sub : Base { [WProtoMember(1)] public int B; }";

            CollectionAssert.AreEqual(
                new[] { "WPROTO040" },
                Run(source)
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.Id)
                    .Distinct()
                    .ToArray(),
                "the refusal should be the whole story, with nothing consequential beside it"
            );

            Assert.IsEmpty(
                CompileGenerated(source)
                    .Select(diagnostic => diagnostic.Id + " " + diagnostic.GetMessage())
            );
        }

        /// <summary>
        /// A refusal withholds what would name a missing formatter, and nothing else.
        /// </summary>
        /// <param name="label">What the fixture is.</param>
        /// <param name="source">The fixture.</param>
        /// <param name="expected">The contracts that must still publish a formatter.</param>
        /// <remarks>
        /// The companion the CS-error sweep needs, and the assertion whose absence let a far worse
        /// regression through: "no CS errors" is satisfied by emitting NOTHING, so a gate made only
        /// of that reads as green while every valid formatter in a hierarchy quietly disappears.
        /// A withheld contract is not a harmless omission -- its type falls back to the reflection
        /// path, which does not run under IL2CPP, and the only diagnostic points at some other type.
        /// So this names the survivors rather than counting them.
        /// </remarks>
        [TestCaseSource(nameof(WithholdingShapes))]
        public void ARefusalWithholdsExactlyWhatWouldNameAMissingFormatter(
            string label,
            string source,
            string[] expected
        )
        {
            CollectionAssert.AreEqual(expected, PublishedFormatters(source), label);
        }

        private static IEnumerable<TestCaseData> WithholdingShapes()
        {
            /*
             * The positive emission control distinguishes refusal from a harness that never generates
             * formatters.
             */
            yield return Withholding(
                "nothing refused, so everything publishes",
                @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 5)] public partial class GoodOne : Base { [WProtoMember(1)] public int B; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 6)] public partial class GoodTwo : Base { [WProtoMember(1)] public int C; }",
                "Base",
                "GoodOne",
                "GoodTwo"
            );
            yield return Withholding(
                "one undeclared sibling withholds only itself",
                @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 5)] public partial class GoodOne : Base { [WProtoMember(1)] public int B; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 6)] public partial class GoodTwo : Base { [WProtoMember(1)] public int C; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 7)] public partial class GoodThree : Base { [WProtoMember(1)] public int D; }
                  [WProtoContract] public partial class Undeclared : Base { [WProtoMember(1)] public int E; }",
                "Base",
                "GoodOne",
                "GoodThree",
                "GoodTwo"
            );
            yield return Withholding(
                "a refused subtype leaves its base published",
                @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] public partial class Unrelated { [WProtoMember(1)] public int U; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 5)] [WProtoSubtype(typeof(Unrelated), 6)]
                  public partial class Sub : Base { [WProtoMember(1)] public int B; }",
                "Base",
                "Unrelated"
            );
            yield return Withholding(
                "a refused BASE withholds its whole subtree, and only that",
                @"[WProtoContract] public partial class Base { [WProtoMember(0)] public int A; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 5)] public partial class GoodOne : Base { [WProtoMember(1)] public int B; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 6)] public partial class GoodTwo : Base { [WProtoMember(1)] public int C; }
                  [WProtoContract] public partial class Elsewhere { [WProtoMember(1)] public int Z; }",
                "Elsewhere"
            );
            yield return Withholding(
                "an abstract base with nothing left to dispatch to is withheld",
                @"[WProtoContract] public abstract partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 5)] public partial class Sub : Base { [WProtoMember(0)] public int B; }
                  [WProtoContract] public partial class Elsewhere { [WProtoMember(1)] public int Z; }",
                "Elsewhere"
            );
            yield return Withholding(
                "a refused leaf leaves both levels above it published",
                @"[WProtoContract] public partial class Root { [WProtoMember(1)] public int A; }
                  [WProtoContract] [WProtoSubtype(typeof(Root), 5)] public partial class Middle : Root { [WProtoMember(1)] public int B; }
                  [WProtoContract] [WProtoSubtype(typeof(Middle), 6)] public partial class Leaf : Middle { [WProtoMember(0)] public int C; }",
                "Middle",
                "Root"
            );

            /*
             * An inherited nested formatter can hide a refused middle contract without a compiler error;
             * inspect the published set.
             */
            yield return Withholding(
                "a refused middle withholds the leaf under it, not just the root's subtree",
                @"[WProtoContract] public partial class Root { [WProtoMember(1)] public int A; }
                  [WProtoContract] [WProtoSubtype(typeof(Root), 5)] public partial class Middle : Root { [WProtoMember(0)] public int B; }
                  [WProtoContract] [WProtoSubtype(typeof(Middle), 6)] public partial class Leaf : Middle { [WProtoMember(1)] public int C; }",
                "Root"
            );
        }

        private static TestCaseData Withholding(
            string label,
            string source,
            params string[] expected
        )
        {
            return new TestCaseData(label, source, expected).SetName("{m} - " + label);
        }

        /// <summary>
        /// A published base keeps every surviving branch and loses only the withheld one.
        /// </summary>
        /// <remarks>
        /// The file list says which formatters exist; this says the surviving base still dispatches
        /// to its good subtypes rather than publishing an empty chain that silently stopped writing
        /// them. <c>CanWrite</c> is generated from the same list, so it is asserted here too.
        /// </remarks>
        [Test]
        public void APublishedBaseKeepsItsSurvivingBranchesAndDropsOnlyTheWithheldOne()
        {
            string survivors = PublishedFormatterFor(
                "Consumer.Base",
                @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 5)] public partial class GoodOne : Base { [WProtoMember(1)] public int B; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 6)] public partial class GoodTwo : Base { [WProtoMember(1)] public int C; }
                  [WProtoContract] public partial class Undeclared : Base { [WProtoMember(1)] public int E; }"
            );

            StringAssert.Contains("value is global::Consumer.GoodOne", survivors);
            StringAssert.Contains("value is global::Consumer.GoodTwo", survivors);
            StringAssert.Contains("typeof(global::Consumer.GoodOne).IsAssignableFrom", survivors);
            StringAssert.DoesNotContain("Consumer.Undeclared", survivors);

            string filtered = PublishedFormatterFor(
                "Consumer.Base",
                @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] public partial class Unrelated { [WProtoMember(1)] public int U; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 5)] [WProtoSubtype(typeof(Unrelated), 6)]
                  public partial class Sub : Base { [WProtoMember(1)] public int B; }"
            );

            StringAssert.DoesNotContain("Consumer.Sub", filtered);
        }

        /// <summary>
        /// Every shape that refuses a contract, asserted to leave code that still compiles.
        /// </summary>
        /// <param name="label">What the fixture is.</param>
        /// <param name="source">The fixture.</param>
        /// <remarks>
        /// A generator that reports correctly and then emits code that does not compile is a class
        /// of defect
        /// rather than one instance, so this asks the question of every refusal shape at once
        /// instead of once at the site that happened to be found. The two references that make it
        /// possible run in OPPOSITE directions -- a base's dispatch chain names its subtype's
        /// formatter, and a subtype's root formatter names the root's -- so a fixture is included
        /// here for each end, and for a refusal reached through the older <c>[WProtoInclude]</c>
        /// spelling as well as the newer one.
        /// </remarks>
        [TestCaseSource(nameof(RefusedContractShapes))]
        public void ARefusedContractNeverLeavesGeneratedCodeThatFailsToCompile(
            string label,
            string source
        )
        {
            Assert.IsNotEmpty(
                Run(source)
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .ToArray(),
                label + ": the fixture must actually be refused, or it proves nothing"
            );

            Assert.IsEmpty(
                CompileGenerated(source)
                    .Select(diagnostic => diagnostic.Id + " " + diagnostic.GetMessage()),
                label
            );
        }

        private static IEnumerable<TestCaseData> RefusedContractShapes()
        {
            yield return Refusal(
                "an unusable [WProtoSubtype] beside a usable one",
                @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] public partial class Unrelated { [WProtoMember(1)] public int U; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 5)] [WProtoSubtype(typeof(Unrelated), 6)]
                  public partial class Sub : Base { [WProtoMember(1)] public int B; }"
            );
            yield return Refusal(
                "a self-declared subtype with an unsupported member",
                @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 5)]
                  public partial class Sub : Base { [WProtoMember(1)] public System.DateTimeOffset Bad; }"
            );
            yield return Refusal(
                "a base-declared subtype with an unsupported member",
                @"[WProtoContract] [WProtoInclude(5, typeof(Sub))] public partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] public partial class Sub : Base { [WProtoMember(1)] public System.DateTimeOffset Bad; }"
            );
            yield return Refusal(
                "a subtype whose field number is out of range",
                @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 5)]
                  public partial class Sub : Base { [WProtoMember(0)] public int B; }"
            );
            yield return Refusal(
                "a subtype that is not partial",
                @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 5)]
                  public class Sub : Base { [WProtoMember(1)] public int B; }"
            );
            yield return Refusal(
                "a three-level chain whose leaf is refused",
                @"[WProtoContract] public partial class Root { [WProtoMember(1)] public int A; }
                  [WProtoContract] [WProtoSubtype(typeof(Root), 5)] public partial class Middle : Root { [WProtoMember(1)] public int B; }
                  [WProtoContract] [WProtoSubtype(typeof(Middle), 6)] public partial class Leaf : Middle { [WProtoMember(0)] public int C; }"
            );
            yield return Refusal(
                "an abstract base whose only subtype is refused",
                @"[WProtoContract] public abstract partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 5)]
                  public partial class Sub : Base { [WProtoMember(0)] public int B; }"
            );
            yield return Refusal(
                "a refused subtype with a surviving sibling, which must not name the base either",
                @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 5)] public partial class Good : Base { [WProtoMember(1)] public int B; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 6)] public partial class Bad : Base { [WProtoMember(0)] public int C; }"
            );
            yield return Refusal(
                "a contract held as a member, which is the shape that already degraded gracefully",
                @"[WProtoContract] public partial class Bad { [WProtoMember(0)] public int B; }
                  [WProtoContract] public partial class Holder { [WProtoMember(1)] public Bad Value; }"
            );
        }

        private static TestCaseData Refusal(string label, string source)
        {
            return new TestCaseData(label, source).SetName("{m} - " + label);
        }

        /// <summary>
        /// A subtype in a different assembly from its base is refused, not merged.
        /// </summary>
        /// <remarks>
        /// The boundary of what a per-assembly generator can honour. The base's dispatch chain was
        /// emitted when the base's own assembly was compiled, so a declaration made afterwards could
        /// never appear in it: accepting this would compile and then throw on the first save, which
        /// is the outcome every diagnostic in this file exists to prevent.
        /// </remarks>
        [Test]
        public void ASubtypeCannotDeclareItselfAgainstABaseInAnotherAssembly()
        {
            MetadataReference upstream = CompileReference(
                "UpstreamAssembly",
                @"namespace Upstream { using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
                  [WProtoContract] public partial class Base { [WProtoMember(1)] public int A; } }"
            );

            ImmutableArray<Diagnostic> diagnostics = Run(
                @"[WProtoContract] [WProtoSubtype(typeof(Upstream.Base), 100)] public partial class Sub : Upstream.Base { [WProtoMember(1)] public int B; }",
                upstream
            );
            Diagnostic match = diagnostics.Single(diagnostic => diagnostic.Id == "WPROTO040");

            Assert.AreEqual(DiagnosticSeverity.Error, match.Severity);
            Assert.IsTrue(match.GetMessage().Contains("UpstreamAssembly"), match.GetMessage());
            Assert.IsTrue(match.GetMessage().Contains("ConsumerAssembly"), match.GetMessage());
        }

        /// <summary>
        /// An UNDECLARED subclass of a contract in another assembly is refused.
        /// </summary>
        /// <remarks>
        /// Deriving is the declaration, so this shape carries no attribute at all and reads as
        /// correct. It is also the only WallstopProto diagnostic no check project can produce:
        /// each of the four compiles many asmdefs into ONE assembly, which makes the guard's
        /// same-assembly test true by construction
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/650">#650</see>).
        /// This fixture is where the rule is held instead.
        /// </remarks>
        [Test]
        public void AnUndeclaredSubclassOfAContractInAnotherAssemblyIsRefused()
        {
            MetadataReference upstream = CompileReference(
                "UpstreamAssembly",
                @"namespace Upstream { using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
                  [WProtoContract] public partial class Base { [WProtoMember(1)] public int A; } }"
            );

            ImmutableArray<Diagnostic> diagnostics = Run(
                @"public sealed class Sub : Upstream.Base { }",
                upstream
            );
            Diagnostic match = diagnostics.Single(diagnostic => diagnostic.Id == "WPROTO044");

            Assert.AreEqual(DiagnosticSeverity.Error, match.Severity);
            Assert.IsTrue(match.GetMessage().Contains("Sub"), match.GetMessage());
            Assert.IsTrue(match.GetMessage().Contains("UpstreamAssembly"), match.GetMessage());
        }

        /// <summary>
        /// <c>[WProtoNotSerialized]</c> is the recorded way out of the refusal above.
        /// </summary>
        [Test]
        public void AnUndeclaredCrossAssemblySubclassOptsOutWithNotSerialized()
        {
            MetadataReference upstream = CompileReference(
                "UpstreamAssembly",
                @"namespace Upstream { using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
                  [WProtoContract] public partial class Base { [WProtoMember(1)] public int A; } }"
            );

            Assert.IsEmpty(
                Run(@"[WProtoNotSerialized] public sealed class Sub : Upstream.Base { }", upstream)
                    .Where(diagnostic => diagnostic.Id == "WPROTO044")
            );
        }

        /// <summary>
        /// A GENERIC contract base is deliberately exempt, and this test is the record of it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Deriving from <c>SerializableDictionary&lt;TKey, TValue&gt;</c> is the API's documented
        /// and required usage -- every serialized dictionary a consumer authors is one -- so
        /// reporting here would fail a consumer's build for writing the shape the documentation
        /// tells them to write. Twenty-plus types in this package alone are that shape.
        /// </para>
        /// <para>
        /// The narrower hazard a generic base does carry -- one field number cannot identify a type
        /// that is really as many types as it has closures -- is <c>WPROTO040</c>'s, reported on the
        /// explicit declaration an author wrote. Removing this exemption to "close"
        /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/650">#650</see>
        /// would trade a diagnostic nobody can reach for a diagnostic everybody hits.
        /// </para>
        /// </remarks>
        [Test]
        public void AnUndeclaredSubclassOfAGenericContractInAnotherAssemblyIsExempt()
        {
            MetadataReference upstream = CompileReference(
                "UpstreamAssembly",
                @"namespace Upstream { using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
                  [WProtoContract] public partial class Box<T> { [WProtoMember(1)] public T Value; } }"
            );

            Assert.IsEmpty(
                Run(@"public sealed class IntBox : Upstream.Box<int> { }", upstream)
                    .Where(diagnostic => diagnostic.Id == "WPROTO044")
            );
        }

        /// <summary>
        /// The refusal explains the mechanism and names a fix that works.
        /// </summary>
        /// <remarks>
        /// A developer whose build just failed needs two things: why, and what to write instead.
        /// The "why" is a fact about per-assembly generation -- the base's chain was emitted when
        /// the base's assembly compiled -- and NOT a claim that the feature can never exist:
        /// emitting the chain in the extending assembly is a different mechanism entirely, and is
        /// tracked on
        /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/612">#612</see>.
        /// The runtime registry refused on
        /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/603">#603</see>
        /// is the thing that stays refused.
        /// </remarks>
        [Test]
        public void TheCrossAssemblyRefusalExplainsTheMechanismAndNamesAWorkingAlternative()
        {
            MetadataReference upstream = CompileReference(
                "UpstreamAssembly",
                @"namespace Upstream { using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
                  [WProtoContract] public partial class Base { [WProtoMember(1)] public int A; } }"
            );

            string message = Run(
                    @"[WProtoContract] [WProtoSubtype(typeof(Upstream.Base), 100)] public partial class Sub : Upstream.Base { [WProtoMember(1)] public int B; }",
                    upstream
                )
                .Single(diagnostic => diagnostic.Id == "WPROTO040")
                .GetMessage();

            StringAssert.Contains("generated when its own assembly is compiled", message);

            StringAssert.Contains("[WProtoMember]", message);

            foreach (string promise in new[] { "not yet", "for now", "in a future", "will be" })
            {
                StringAssert.DoesNotContain(promise, message);
            }
        }

        /// <summary>
        /// The alternative the refusal recommends compiles and generates, in the consumer assembly.
        /// </summary>
        /// <remarks>
        /// A diagnostic that names a fix has to name one that works, or the developer spends the
        /// refusal twice. Composition is what a per-assembly generator CAN honour: the member's
        /// declared type resolves through the upstream assembly's own formatter, which carries the
        /// upstream subtypes in the chain that was emitted with it.
        /// </remarks>
        [Test]
        public void TheAlternativeTheCrossAssemblyRefusalRecommendsGenerates()
        {
            MetadataReference upstream = CompileReference(
                "UpstreamAssembly",
                @"namespace Upstream { using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
                  [WProtoContract] [WProtoInclude(100, typeof(UpstreamSub))] public partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] public partial class UpstreamSub : Base { [WProtoMember(1)] public int C; } }"
            );

            CollectionAssert.IsEmpty(
                Run(
                        @"[WProtoContract] public partial class Holder { [WProtoMember(1)] public Upstream.Base Wrapped; [WProtoMember(2)] public int B; }",
                        upstream
                    )
                    .Select(diagnostic => diagnostic.Id + " " + diagnostic.GetMessage())
                    .ToArray()
            );
        }

        /// <summary>
        /// The two declaration forms emit the same formatter, character for character.
        /// </summary>
        /// <remarks>
        /// Stronger than comparing bytes for a handful of values, and it is the property the whole
        /// feature rests on: if the emitted code is the same code, there is no shape of payload the
        /// two forms could disagree about. The only difference permitted is the subtype's name.
        /// </remarks>
        [Test]
        public void BothDeclarationFormsEmitTheSameFormatterSource()
        {
            string fromInclude = GeneratedFormatterFor(
                "Consumer.Base",
                @"[WProtoContract] [WProtoInclude(100, typeof(Alpha))] [WProtoInclude(101, typeof(Beta))] public partial class Base { [WProtoMember(1)] public int A; [WProtoMember(2)] public string B; }
                  [WProtoContract] public partial class Alpha : Base { [WProtoMember(1)] public int C; }
                  [WProtoContract] public partial class Beta : Base { [WProtoMember(1)] public double D; }"
            );
            string fromSubtype = GeneratedFormatterFor(
                "Consumer.Base",
                @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; [WProtoMember(2)] public string B; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 100)] public partial class Alpha : Base { [WProtoMember(1)] public int C; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 101)] public partial class Beta : Base { [WProtoMember(1)] public double D; }"
            );

            Assert.AreEqual(fromInclude, fromSubtype);

            Assert.AreEqual(
                fromInclude,
                GeneratedFormatterFor(
                    "Consumer.Base",
                    @"[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; [WProtoMember(2)] public string B; }
                      [WProtoContract] [WProtoSubtype(typeof(Base), 101)] public partial class Beta : Base { [WProtoMember(1)] public double D; }
                      [WProtoContract] [WProtoSubtype(typeof(Base), 100)] public partial class Alpha : Base { [WProtoMember(1)] public int C; }"
                )
            );
        }

        [Test]
        public void MixingBothDeclarationFormsOnOneBaseIsFine()
        {
            Assert.IsEmpty(
                Run(
                    @"[WProtoContract] [WProtoInclude(100, typeof(Alpha))] public partial class Base { [WProtoMember(1)] public int A; }
                      [WProtoContract] public partial class Alpha : Base { [WProtoMember(1)] public int B; }
                      [WProtoContract] [WProtoSubtype(typeof(Base), 101)] public partial class Beta : Base { [WProtoMember(1)] public int C; }"
                )
            );
        }

        [Test]
        public void AFieldInitializerSkipConstructorDiscardsIsAWarning()
        {
            /*
             * protobuf-net skips these field initializers, while this reader runs them; a round trip alone
             * cannot detect the mismatch.
             */
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
            string[] clean =
            {
                @"[WProtoContract(SkipConstructor = true)] public partial class A { [WProtoMember(1)] public ulong State; private byte[] _scratch; }",
                @"[WProtoContract(SkipConstructor = true)] public partial class B { [WProtoMember(1)] public ulong State; [WProtoMember(2)] public byte[] Scratch = new byte[16]; }",
                @"[WProtoContract(SkipConstructor = true)] public partial class C { [WProtoMember(1)] public ulong State; private static readonly byte[] Shared = new byte[16]; }",
                @"[WProtoContract] public partial class D { [WProtoMember(1)] public ulong State; private byte[] _scratch = new byte[16]; }",
            };

            foreach (string source in clean)
            {
                Assert.IsEmpty(
                    Run(source).Where(diagnostic => diagnostic.Id == "WPROTO033"),
                    source
                );
            }

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
            // SkipConstructor on the concrete subtype also bypasses inherited field initializers.
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"public abstract class Machinery { protected byte[] _scratch = new byte[16]; }
                  [WProtoContract(SkipConstructor = true)] public partial class Engine : Machinery { [WProtoMember(1)] public ulong State; }"
            );
            Diagnostic match = diagnostics.Single(diagnostic => diagnostic.Id == "WPROTO033");

            Assert.AreEqual(DiagnosticSeverity.Warning, match.Severity);

            Assert.IsTrue(match.GetMessage().Contains("Engine"), match.GetMessage());
            Assert.IsTrue(match.GetMessage().Contains("Machinery._scratch"), match.GetMessage());

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
            // The oracle honors the declared SkipConstructor flag even when generated construction cannot.
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
            /*
             * protobuf-net 3 runs root hooks only; version 2 and this generator also run subtype hooks in
             * different orders.
             */
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"[WProtoContract] [WProtoInclude(100, typeof(Leaf))] public partial class Root { [WProtoMember(1)] public int A; }
                  [WProtoContract] public partial class Leaf : Root { [WProtoMember(1)] public int B; [WProtoAfterDeserialization] private void Rebuild() { } }"
            );
            Diagnostic match = diagnostics.Single(diagnostic => diagnostic.Id == "WPROTO034");

            Assert.AreEqual(DiagnosticSeverity.Warning, match.Severity);

            Assert.IsTrue(match.GetMessage().Contains("Rebuild"), match.GetMessage());
            Assert.IsTrue(match.GetMessage().Contains("Leaf"), match.GetMessage());
            Assert.IsTrue(match.GetMessage().Contains("Root"), match.GetMessage());
        }

        [Test]
        public void OnlyAHookNoReaderAgreesOnWarns()
        {
            string[] clean =
            {
                @"[WProtoContract] [WProtoInclude(100, typeof(Leaf))] public partial class Root { [WProtoMember(1)] public int A; [WProtoAfterDeserialization] private void Rebuild() { } }
                  [WProtoContract] public partial class Leaf : Root { [WProtoMember(1)] public int B; }",
                @"[WProtoContract] public partial class Alone { [WProtoMember(1)] public int A; [WProtoAfterDeserialization] private void Rebuild() { } }",
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
            string[] unusable =
            {
                @"[assembly: global::WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters.WJsonConverter(typeof(Consumer.Plain), typeof(Consumer.PlainConverter))]
                  public sealed class Plain { }
                  public sealed class PlainConverter : global::System.Text.Json.Serialization.JsonConverter<Plain> { public override Plain Read(ref global::System.Text.Json.Utf8JsonReader r, System.Type t, global::System.Text.Json.JsonSerializerOptions o) => null; public override void Write(global::System.Text.Json.Utf8JsonWriter w, Plain v, global::System.Text.Json.JsonSerializerOptions o) { } }",
                @"[assembly: global::WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters.WJsonConverter(typeof(Consumer.Box<>), typeof(Consumer.PairConverter<,>))]
                  public sealed class Box<T> { }
                  public sealed class PairConverter<TA, TB> : global::System.Text.Json.Serialization.JsonConverter<Box<TA>> { public override Box<TA> Read(ref global::System.Text.Json.Utf8JsonReader r, System.Type t, global::System.Text.Json.JsonSerializerOptions o) => null; public override void Write(global::System.Text.Json.Utf8JsonWriter w, Box<TA> v, global::System.Text.Json.JsonSerializerOptions o) { } }",
                @"[assembly: global::WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters.WJsonConverter(typeof(Consumer.Box<>), typeof(Consumer.OtherConverter<>))]
                  public sealed class Box<T> { }
                  public sealed class Other<T> { }
                  public sealed class OtherConverter<T> : global::System.Text.Json.Serialization.JsonConverter<Other<T>> { public override Other<T> Read(ref global::System.Text.Json.Utf8JsonReader r, System.Type t, global::System.Text.Json.JsonSerializerOptions o) => null; public override void Write(global::System.Text.Json.Utf8JsonWriter w, Other<T> v, global::System.Text.Json.JsonSerializerOptions o) { } }",
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
            // Type-parameter new() constraints must be checked as constraints, not concrete constructors.
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

        /// <summary>
        /// Every shape WPROTO030 does and does not recognize, and the discriminator it names.
        /// </summary>
        /// <returns>One case per row of the detection matrix.</returns>
        /// <remarks>
        /// The last four rows are the ones a public survey of four Unity codebases found: 80 of 485
        /// protobuf-net contracts (16.5%) were invisible to an exact match on
        /// <c>ProtoBuf.ProtoContractAttribute</c>. The negative rows matter at least as much --
        /// a WPROTO030 on a WCF <c>[DataContract]</c> would break the family's promise that the code
        /// names a serialization contract that cannot be honoured.
        /// </remarks>
        private static IEnumerable<TestCaseData> MigrationSignalCases()
        {
            yield return new TestCaseData(
                @"[global::ProtoBuf.ProtoContract] public partial class Legacy { [global::ProtoBuf.ProtoMember(1)] public int Value; }",
                true,
                "protobuf-net's own contract attribute"
            ).SetName("ProtoBuf.ProtoContract is announced");

            yield return new TestCaseData(
                VendoredProtobufNet
                    + @" [global::Consumer.Vendored.ProtoContract] public partial class Renamed { [global::Consumer.Vendored.ProtoMember(1)] public int Value; }",
                true,
                "vendored under a renamed namespace"
            ).SetName("A renamed-namespace ProtoContract is announced");

            yield return new TestCaseData(
                VendoredProtobufNet
                    + @" [global::Consumer.Vendored.ProtoContract] public partial class Renamed { [global::Consumer.Vendored.ProtoMember(1)] public int Value; }",
                false,
                "vendored under a renamed namespace"
            ).SetName("A vendored ProtoContract needs no separate protobuf-net reference");

            yield return new TestCaseData(
                @"namespace Lonely { public sealed class ProtoContractAttribute : global::System.Attribute { } }
                  [global::Consumer.Lonely.ProtoContract] public partial class Coincidence { public int Value; }",
                true,
                null
            ).SetName("An unrelated type merely named ProtoContractAttribute is not announced");

            yield return new TestCaseData(
                @"[global::System.Runtime.Serialization.DataContract] public partial class Ordered { [global::System.Runtime.Serialization.DataMember(Order = 1)] public int Value; }",
                true,
                "Order"
            ).SetName("A [DataContract] with ordered members is announced");

            yield return new TestCaseData(
                @"[global::System.Runtime.Serialization.DataContract] public partial class Ordered { [global::System.Runtime.Serialization.DataMember(Order = 1)] public int Value; }",
                false,
                null
            ).SetName("A [DataContract] is silent without a protobuf-net reference");

            yield return new TestCaseData(
                @"[global::System.Runtime.Serialization.DataContract] public partial class Wcf { [global::System.Runtime.Serialization.DataMember] public int Value; }",
                true,
                null
            ).SetName("A [DataContract] is silent without an explicit member Order");

            yield return new TestCaseData(
                @"[global::System.Runtime.Serialization.DataContract] [WProtoContract] public sealed partial class Ported { [global::System.Runtime.Serialization.DataMember(Order = 1)] [WProtoMember(1)] public int Value; }",
                true,
                null
            ).SetName("A ported [DataContract] is silent");
        }

        [TestCaseSource(nameof(MigrationSignalCases))]
        public void MigrationSignalMatchesOnlyProtobufNetContracts(
            string body,
            bool referencesProtobufNet,
            string expectedDiscriminator
        )
        {
            ImmutableArray<Diagnostic> diagnostics = Run(
                body,
                new[] { DataContractReference },
                referencesProtobufNet,
                out Compilation _
            );
            Diagnostic[] matches = diagnostics
                .Where(diagnostic => diagnostic.Id == "WPROTO030")
                .ToArray();

            if (expectedDiscriminator == null)
            {
                Assert.IsEmpty(matches.Select(match => match.GetMessage()));
                return;
            }

            Assert.AreEqual(1, matches.Length, string.Join("; ", diagnostics.Select(d => d.Id)));
            Assert.AreEqual(DiagnosticSeverity.Info, matches[0].Severity);
            string message = matches[0].GetMessage();
            Assert.IsTrue(message.Contains(expectedDiscriminator), message);
            Assert.IsTrue(message.Contains("WProtoContract"), message);
            Assert.IsTrue(message.Contains("suppress"), message);
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

        /// <summary>
        /// A member cannot take a field number the contract reserved for a removed one.
        /// </summary>
        /// <remarks>
        /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/608">#608</see>.
        /// WPROTO002 fires on two members that exist at once and so cannot see a number a deletion
        /// freed: every payload written before the removal still carries that field, and giving it
        /// to another member reads those saves back as the wrong thing.
        /// </remarks>
        [Test]
        public void AMemberCannotTakeAReservedFieldNumber()
        {
            AssertDiagnostic(
                "WPROTO043",
                "field number 3",
                @"[WProtoContract] [WProtoReserved(3)] public sealed partial class Save
                  {
                      [WProtoMember(3)] public string Name;
                  }"
            );
        }

        [Test]
        public void AMemberCannotTakeAReservedName()
        {
            AssertDiagnostic(
                "WPROTO043",
                "the name 'Health'",
                @"[WProtoContract] [WProtoReserved(""Health"")] public sealed partial class Save
                  {
                      [WProtoMember(9)] public int Health;
                  }"
            );
        }

        /// <summary>
        /// A reservation is a record, and a record may not touch the wire.
        /// </summary>
        /// <remarks>
        /// Stronger than comparing bytes for a handful of values: if the emitted code is the same
        /// code, there is no payload the two could disagree about. A reservation that changed the
        /// formatter would be a wire break introduced by documenting a wire contract, which is the
        /// one outcome this feature must not have.
        /// </remarks>
        [Test]
        public void AReservationDoesNotChangeTheEmittedFormatter()
        {
            const string Members =
                @" public sealed partial class Save
                   {
                       [WProtoMember(1)] public int Kept;
                       [WProtoMember(4)] public string Name;
                   }";

            Assert.AreEqual(
                GeneratedFormatterFor("Consumer.Save", "[WProtoContract]" + Members),
                GeneratedFormatterFor(
                    "Consumer.Save",
                    @"[WProtoContract] [WProtoReserved(2, 3)] [WProtoReserved(""Health"")]"
                        + Members
                )
            );
        }

        [Test]
        public void AMemberCannotRenameItselfOntoAReservedName()
        {
            AssertDiagnostic(
                "WPROTO043",
                "the name 'Health'",
                @"[WProtoContract] [WProtoReserved(""Health"")] public sealed partial class Save
                  {
                      [WProtoMember(9, Name = ""Health"")] public int Hp;
                  }"
            );
        }

        [Test]
        public void RenamingAwayFromAReservedNameIsAllowed()
        {
            // Different C# and schema names distinguish which identity the reservation check reads.
            CollectionAssert.IsEmpty(
                Run(
                        @"[WProtoContract] [WProtoReserved(""Health"")] public sealed partial class Save
                          {
                              [WProtoMember(9, Name = ""Vitality"")] public int Health;
                          }"
                    )
                    .Select(diagnostic => diagnostic.Id + " " + diagnostic.GetMessage())
                    .ToArray()
            );
        }

        [Test]
        public void AMemberTakingBothAReservedNumberAndNameIsNamedForBoth()
        {
            AssertDiagnostic(
                "WPROTO043",
                "field number 3 and the name 'Health'",
                @"[WProtoContract] [WProtoReserved(3)] [WProtoReserved(""Health"")] public sealed partial class Save
                  {
                      [WProtoMember(3)] public int Health;
                  }"
            );
        }

        [Test]
        public void OneDeclarationCanReserveSeveralNumbers()
        {
            AssertDiagnostic(
                "WPROTO043",
                "field number 9",
                @"[WProtoContract] [WProtoReserved(3, 7, 9)] public sealed partial class Save
                  {
                      [WProtoMember(9)] public int Later;
                  }"
            );
        }

        [Test]
        public void AReservationDoesNotRefuseTheNumbersAroundIt()
        {
            CollectionAssert.IsEmpty(
                Run(
                        @"[WProtoContract] [WProtoReserved(3)] [WProtoReserved(""Health"")] public sealed partial class Save
                          {
                              [WProtoMember(2)] public int Before;
                              [WProtoMember(4)] public int After;
                              [WProtoMember(5)] public int Healthy;
                          }"
                    )
                    .Select(diagnostic => diagnostic.Id + " " + diagnostic.GetMessage())
                    .ToArray()
            );
        }

        [Test]
        public void AReservationOnOneContractDoesNotBindAnother()
        {
            // Reservations belong to one contract, not its bases or siblings.
            CollectionAssert.IsEmpty(
                Run(
                        @"[WProtoContract] [WProtoReserved(3)] public partial class Base { [WProtoMember(1)] public int A; }
                          [WProtoContract] [WProtoSubtype(typeof(Base), 100)] public partial class Sub : Base { [WProtoMember(3)] public int B; }
                          [WProtoContract] public sealed partial class Unrelated { [WProtoMember(3)] public int C; }"
                    )
                    .Select(diagnostic => diagnostic.Id + " " + diagnostic.GetMessage())
                    .ToArray()
            );
        }

        [Test]
        public void ARemovedMemberComingBackUnchangedIsAllowedOnceItsReservationGoes()
        {
            CollectionAssert.IsEmpty(
                Run(
                        @"[WProtoContract] public sealed partial class Save
                          {
                              [WProtoMember(3)] public int Health;
                          }"
                    )
                    .Select(diagnostic => diagnostic.Id + " " + diagnostic.GetMessage())
                    .ToArray()
            );
        }

        /// <summary>
        /// A reservation binds subtype discriminators, not only members.
        /// </summary>
        /// <remarks>
        /// Reported by Cursor Bugbot against the first draft, which checked only
        /// <c>[WProtoMember]</c>. A base's includes are numbered against its members -- one space --
        /// so a rule binding one half is one an author steps around by writing the number on the
        /// other.
        /// </remarks>
        [Test]
        public void AnIncludeCannotTakeAReservedFieldNumber()
        {
            AssertDiagnostic(
                "WPROTO013",
                "is reserved on 'Base'",
                @"[WProtoContract] [WProtoReserved(100)] [WProtoInclude(100, typeof(Sub))] public partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] public partial class Sub : Base { [WProtoMember(1)] public int B; }"
            );
        }

        [Test]
        public void ASubtypeDeclarationCannotTakeAReservedFieldNumber()
        {
            AssertDiagnostic(
                "WPROTO040",
                "is reserved on 'Base'",
                @"[WProtoContract] [WProtoReserved(100)] public partial class Base { [WProtoMember(1)] public int A; }
                  [WProtoContract] [WProtoSubtype(typeof(Base), 100)] public partial class Sub : Base { [WProtoMember(1)] public int B; }"
            );
        }

        [Test]
        public void AReservationOnABaseDoesNotRefuseAnUnreservedDiscriminator()
        {
            CollectionAssert.IsEmpty(
                Run(
                        @"[WProtoContract] [WProtoReserved(100)] [WProtoInclude(101, typeof(Sub))] public partial class Base { [WProtoMember(1)] public int A; }
                          [WProtoContract] public partial class Sub : Base { [WProtoMember(1)] public int B; }"
                    )
                    .Select(diagnostic => diagnostic.Id + " " + diagnostic.GetMessage())
                    .ToArray()
            );
        }

        [Test]
        public void ReservationsDoNotChangeWhatTwoLiveMembersOnOneNumberReport()
        {
            AssertDiagnostic(
                "WPROTO002",
                "Second",
                @"[WProtoContract] [WProtoReserved(42)] public sealed partial class Clash
                  {
                      [WProtoMember(1)] public int First;
                      [WProtoMember(1)] public int Second;
                  }"
            );
        }

        [Test]
        public void EveryMemberOnAReservedNumberIsToldWhyRatherThanOneBeingCalledADuplicate()
        {
            CollectionAssert.AreEqual(
                new[] { "WPROTO043", "WPROTO043" },
                Run(
                        @"[WProtoContract] [WProtoReserved(1)] public sealed partial class Clash
                          {
                              [WProtoMember(1)] public int First;
                              [WProtoMember(1)] public int Second;
                          }"
                    )
                    .Select(diagnostic => diagnostic.Id)
                    .ToArray()
            );
        }

        /*
         * Consumer collection interfaces have no constructible implementation; unsupported elements must
         * remain refused even inside wrappers.
         */
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
            /*
             * These types lack portable encodings: DateTimeOffset is unsupported by both oracles, and
             * pointer/type identities depend on the process.
             */
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
            // A cached shallow wrapper must not bypass the depth limit when reused inside a deeper member.
            const int shallow = 3;
            const int deep = 66;

            AssertDiagnostic("WPROTO032", "Deep", Chain(("Shallow", shallow), ("Deep", deep)));
            AssertDiagnostic("WPROTO032", "Deep", Chain(("Deep", deep), ("Shallow", shallow)));
        }

        [Test]
        public void ADepthRefusalDoesNotMakeAServiceableMemberLookUnsupported()
        {
            // A failed deep lookup must not poison a supported shallower shape in the wrapper cache.
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
            // Sixty-six collection levels exceed the reader's sixty-four nested-message limit.
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
            /*
             * Compile generated output because wrong collection fill methods produce C# errors without
             * generator diagnostics.
             */
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
            /*
             * Collection contracts need an explicit choice between members and elements; silently choosing
             * either loses data.
             */
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
            /*
             * A separate consumer compilation proves surrogate registrations are discovered in referenced
             * assemblies.
             */
            AssertNoDiagnostics(
                @"[WProtoContract] public sealed partial class ConsumerThing
                  {
                      [WProtoMember(1)] public WallstopStudios.UnityHelpers.Proto.Generator.Tests.ForeignVector3 Where;
                      [WProtoMember(2)] public WallstopStudios.UnityHelpers.Proto.Generator.Tests.ForeignVector3[] Path;
                  }"
            );
        }

        [Test]
        public void AnOpenSurrogateShippedByAReferenceRegistersTheConsumerClosure()
        {
            MetadataReference package = CompileGeneratedReference(
                "OpenSurrogatePackage",
                @"[assembly: WProtoSurrogate(typeof(Package.Real<>), typeof(Package.StandIn<>))]
                  namespace Package
                  {
                      public readonly struct Real<T> { }
                      [WProtoContract] public partial struct StandIn<T>
                      {
                          [WProtoMember(1)] public T Value;
                          public static implicit operator StandIn<T>(Real<T> value) => default;
                          public static implicit operator Real<T>(StandIn<T> value) => default;
                      }
                  }"
            );

            ImmutableArray<Diagnostic> diagnostics = Run(
                "public static class Used { public static Package.Real<int> Value; }",
                new[] { package },
                out Compilation generated
            );
            Assert.IsEmpty(diagnostics.Select(d => d.Id + " " + d.GetMessage()));
            Assert.IsEmpty(
                generated
                    .GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Id + " " + d.GetMessage())
            );

            string registrar = generated
                .SyntaxTrees.Single(tree =>
                    tree.FilePath.EndsWith(
                        "WProtoGeneratedRegistrar.g.cs",
                        StringComparison.Ordinal
                    )
                )
                .ToString();
            StringAssert.Contains("Package.StandIn<int>.WProtoFormatter.Instance", registrar);
        }

        [Test]
        public void ACollectionImplementedAsAStructIsAcceptedLikeAnyOther()
        {
            // Struct collection null checks fail in generated C#, not in generator diagnostics.
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
            // Struct maps require generated-code compilation to detect invalid null operators.
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
            // An in-parameter copies a mutable struct before its hook, discarding hook changes.
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
            /*
             * A nested contract in an open generic owner has no discoverable standalone construction to
             * register.
             */
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
        public void AnOpenGenericSurrogateServesClosedMembersAndMapKeys()
        {
            const string source =
                @"[assembly: WProtoSurrogate(typeof(Consumer.Pair<,>), typeof(Consumer.PairSurrogate<,>))]
                  public readonly struct Pair<T1, T2>
                  {
                      public readonly T1 First;
                      public readonly T2 Second;
                      public Pair(T1 first, T2 second) { First = first; Second = second; }
                  }
                  [WProtoContract] public partial struct PairSurrogate<T1, T2>
                  {
                      [WProtoMember(1)] public T1 First;
                      [WProtoMember(2)] public T2 Second;
                      public static implicit operator PairSurrogate<T1, T2>(Pair<T1, T2> value) => new PairSurrogate<T1, T2> { First = value.First, Second = value.Second };
                      public static implicit operator Pair<T1, T2>(PairSurrogate<T1, T2> value) => new Pair<T1, T2>(value.First, value.Second);
                  }
                  [WProtoContract] public sealed partial class Holder
                  {
                      [WProtoMember(1)] public Pair<int, string> Pair;
                      [WProtoMember(2)] public System.Collections.Generic.Dictionary<Pair<int, int>, double> Values;
                  }";

            ImmutableArray<Diagnostic> diagnostics = Run(source, out Compilation generated);
            Assert.IsEmpty(diagnostics.Select(d => d.Id + " " + d.GetMessage()));
            Assert.IsEmpty(
                generated
                    .GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Id + " " + d.GetMessage())
            );

            SyntaxTree registrar = generated.SyntaxTrees.Single(tree =>
                tree.FilePath.EndsWith("WProtoGeneratedRegistrar.g.cs", StringComparison.Ordinal)
            );
            StringAssert.Contains(
                "PairSurrogate<int, string>.WProtoFormatter.Instance",
                registrar.ToString()
            );
            StringAssert.Contains(
                "PairSurrogate<int, int>.WProtoFormatter.Instance",
                registrar.ToString()
            );
        }

        [Test]
        public void AClosedGenericContractPropagatesItsSurrogateAndEnumDependencies()
        {
            const string source =
                @"[assembly: WProtoSurrogate(typeof(Consumer.Real<>), typeof(Consumer.StandIn<>))]
                  public enum Choice : short { None, One }
                  public readonly struct Real<T> { public readonly T Value; }
                  [WProtoContract] public partial struct StandIn<T>
                  {
                      [WProtoMember(1)] public T Value;
                      [WProtoMember(2)] public Child<T> Child;
                      public static implicit operator StandIn<T>(Real<T> value) => new StandIn<T> { Value = value.Value };
                      public static implicit operator Real<T>(StandIn<T> value) => default;
                  }
                  [WProtoContract] public partial struct Child<T>
                  {
                      [WProtoMember(1)] public T Value;
                  }
                  [WProtoContract] public sealed partial class Holder<T>
                  {
                      [WProtoMember(1)] public Real<T> Value;
                      [WProtoMember(2)] public System.Collections.Generic.Dictionary<Real<T>, double> Values;
                  }
                  public static class Used { public static Holder<Choice> Value; }";

            ImmutableArray<Diagnostic> diagnostics = Run(source, out Compilation generated);
            Assert.IsEmpty(diagnostics.Select(d => d.Id + " " + d.GetMessage()));
            Assert.IsEmpty(
                generated
                    .GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Id + " " + d.GetMessage())
            );

            string registrar = generated
                .SyntaxTrees.Single(tree =>
                    tree.FilePath.EndsWith(
                        "WProtoGeneratedRegistrar.g.cs",
                        StringComparison.Ordinal
                    )
                )
                .ToString();
            StringAssert.Contains(
                "StandIn<global::Consumer.Choice>.WProtoFormatter.Instance",
                registrar
            );
            StringAssert.Contains(
                "Child<global::Consumer.Choice>.WProtoFormatter.Instance",
                registrar
            );
            StringAssert.Contains(
                "WProtoScalarFormatters.Enum<global::Consumer.Choice>(2, true)",
                registrar
            );
        }

        [Test]
        public void AnUnrelatedPrivateEnumDoesNotLeakIntoTheRegistrar()
        {
            ImmutableArray<Diagnostic> errors = CompileGenerated(
                @"[WProtoContract] public sealed partial class Fine { [WProtoMember(1)] public int Value; }
                  public sealed class Unrelated
                  {
                      private enum Hidden { None }
                      private System.Collections.Generic.List<Hidden> Values;
                  }"
            );

            Assert.IsEmpty(errors.Select(d => d.Id + " " + d.GetMessage()));
        }

        [Test]
        [TestCase(
            "typeof(Consumer.Real<int>), typeof(Consumer.Good<>)",
            TestName = "ClosedReal.OpenSurrogate.Refused"
        )]
        [TestCase(
            "typeof(Consumer.Real<>), typeof(Consumer.Good<int>)",
            TestName = "OpenReal.ClosedSurrogate.Refused"
        )]
        [TestCase(
            "typeof(Consumer.Real<>), typeof(Consumer.WrongArity<,>)",
            TestName = "OpenPair.DifferentArity.Refused"
        )]
        public void AnOpenGenericSurrogateRequiresMatchingOpennessAndArity(string pair)
        {
            AssertDiagnostic(
                "WPROTO038",
                "Real",
                @"[assembly: WProtoSurrogate("
                    + pair
                    + @")]
                  public readonly struct Real<T> { }
                  [WProtoContract] public partial struct Good<T>
                  {
                      public static implicit operator Good<T>(Real<T> value) => default;
                      public static implicit operator Real<T>(Good<T> value) => default;
                  }
                  [WProtoContract] public partial struct WrongArity<T1, T2> { }"
            );
        }

        [TestCase("where T : System.IComparable<T>", TestName = "InterfaceConstraint.Refused")]
        [TestCase("where T : unmanaged", TestName = "UnmanagedConstraint.Refused")]
        public void AnOpenSurrogateCannotRequireMoreThanItsRealType(string constraint)
        {
            AssertDiagnostic(
                "WPROTO038",
                "Real",
                @"[assembly: WProtoSurrogate(typeof(Consumer.Real<>), typeof(Consumer.Strict<>))]
                  public readonly struct Real<T> { }
                  [WProtoContract] public partial struct Strict<T> "
                    + constraint
                    + @"
                  {
                      public static implicit operator Strict<T>(Real<T> value) => default;
                      public static implicit operator Real<T>(Strict<T> value) => default;
                  }"
            );
        }

        [Test]
        public void MatchingOpenSurrogateConstraintsAreAccepted()
        {
            AssertNoDiagnostics(
                @"[assembly: WProtoSurrogate(typeof(Consumer.Real<>), typeof(Consumer.StandIn<>))]
                  public readonly struct Real<T> where T : System.IComparable<T> { }
                  [WProtoContract] public partial struct StandIn<T> where T : System.IComparable<T>
                  {
                      public static implicit operator StandIn<T>(Real<T> value) => default;
                      public static implicit operator Real<T>(StandIn<T> value) => default;
                  }"
            );
        }

        [Test]
        public void MatchingNestedGenericSurrogateConstraintsAreAccepted()
        {
            AssertNoDiagnostics(
                @"[assembly: WProtoSurrogate(typeof(Consumer.Real<>), typeof(Consumer.StandIn<>))]
                  public sealed class Outer<T> { public interface IMarker { } }
                  public readonly struct Real<T> where T : Outer<T>.IMarker { }
                  [WProtoContract] public partial struct StandIn<T> where T : Outer<T>.IMarker
                  {
                      public static implicit operator StandIn<T>(Real<T> value) => default;
                      public static implicit operator Real<T>(StandIn<T> value) => default;
                  }"
            );
        }

        [Test]
        public void AnOpenConstructionNestedInATypeArgumentIsNotRegistered()
        {
            // A type argument can contain an unbound parameter without being a type parameter itself.
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
            // Immutable construction does not require the parameterless constructor used by mutable reads.
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
            // Converter constraints are checked at the emitted registration call, not at the attribute.
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

        /// <summary>
        /// The formatter source the generator emits for one contract of a fixture.
        /// </summary>
        /// <param name="qualified">The contract's fully qualified name.</param>
        /// <param name="body">The fixture to compile.</param>
        /// <returns>The generated file's text.</returns>
        private static string GeneratedFormatterFor(string qualified, string body)
        {
            Assert.IsEmpty(Run(body, out Compilation _));
            return PublishedFormatterFor(qualified, body);
        }

        /// <summary>
        /// The generated formatter source for one contract, whether or not the fixture was refused.
        /// </summary>
        /// <param name="qualified">The contract's fully qualified name.</param>
        /// <param name="body">The fixture to compile.</param>
        /// <returns>The generated file's text.</returns>
        private static string PublishedFormatterFor(string qualified, string body)
        {
            Run(body, out Compilation generated);

            SyntaxTree emitted = generated
                .SyntaxTrees.Where(tree =>
                    tree.FilePath.EndsWith(
                        qualified.Replace('.', '_') + ".WProtoFormatter.g.cs",
                        StringComparison.Ordinal
                    )
                )
                .Single();

            return emitted.GetText().ToString();
        }

        /// <summary>
        /// The contracts a fixture actually published a formatter for, ordinal by name.
        /// </summary>
        /// <param name="body">The fixture to compile.</param>
        /// <returns>Each published contract's simple name.</returns>
        private static string[] PublishedFormatters(string body)
        {
            Run(body, out Compilation generated);

            return generated
                .SyntaxTrees.Where(tree =>
                    tree.FilePath.EndsWith(".WProtoFormatter.g.cs", StringComparison.Ordinal)
                )
                .Select(tree =>
                    Path.GetFileName(tree.FilePath)
                        .Replace("global__Consumer_", string.Empty)
                        .Replace(".WProtoFormatter.g.cs", string.Empty)
                )
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// protobuf-net's contract vocabulary under a namespace of the consumer's own, which is what
        /// a project does when it has to vendor the library to avoid an assembly conflict.
        /// </summary>
        private const string VendoredProtobufNet =
            @"namespace Vendored
              {
                  public sealed class ProtoContractAttribute : global::System.Attribute { }
                  public sealed class ProtoMemberAttribute : global::System.Attribute { public ProtoMemberAttribute(int tag) { } }
              }";

        /// <summary>
        /// The assembly declaring <c>[DataContract]</c>, referenced explicitly so a fixture using it
        /// does not depend on whether some earlier test happened to load it.
        /// </summary>
        private static readonly MetadataReference DataContractReference =
            MetadataReference.CreateFromFile(
                typeof(System.Runtime.Serialization.DataContractAttribute).Assembly.Location
            );

        /// <summary>
        /// Whether an assembly is one of the protobuf-net oracles, so a fixture can be compiled
        /// against a compilation that has never heard of protobuf-net.
        /// </summary>
        /// <param name="assembly">The candidate assembly.</param>
        /// <returns><c>true</c> when it declares or forwards protobuf-net's attributes.</returns>
        private static bool DeclaresProtobufNet(System.Reflection.Assembly assembly)
        {
            return assembly.GetType(typeof(ProtoBuf.ProtoContractAttribute).FullName, false) != null
                || assembly.GetType(typeof(ProtoBuf.ProtoMemberAttribute).FullName, false) != null;
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
            return Run(body, additionalReferences, true, out generated);
        }

        private static ImmutableArray<Diagnostic> Run(
            string body,
            IReadOnlyCollection<MetadataReference> additionalReferences,
            bool includeProtobufNet,
            out Compilation generated
        )
        {
            // Assembly attributes must be hoisted before the synthetic consumer namespace.
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
                if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
                {
                    continue;
                }

                if (!includeProtobufNet && DeclaresProtobufNet(assembly))
                {
                    continue;
                }

                references.Add(MetadataReference.CreateFromFile(assembly.Location));
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

        private static MetadataReference CompileGeneratedReference(string assemblyName, string body)
        {
            List<MetadataReference> references = new List<MetadataReference>();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }

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
            CSharpGeneratorDriver
                .Create(new WProtoGenerator())
                .RunGeneratorsAndUpdateCompilation(
                    compilation,
                    out Compilation generated,
                    out ImmutableArray<Diagnostic> diagnostics
                );
            Assert.IsEmpty(diagnostics.Select(d => d.Id + " " + d.GetMessage()));

            using (MemoryStream stream = new MemoryStream())
            {
                Microsoft.CodeAnalysis.Emit.EmitResult result = generated.Emit(stream);
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
