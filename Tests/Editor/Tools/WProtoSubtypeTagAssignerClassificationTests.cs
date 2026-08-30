// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Editor.Tools;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Pins the assigner's classification against the generator's, because they have drifted three
    /// times and a comment saying they must agree has not been enough.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two answer one question -- "is this type serialized, and can this base hold it" -- from
    /// two worlds: the generator from Roslyn symbols at compile time, this from reflection in the
    /// editor. The generator's answer decides which types DEMAND a field number; this one decides
    /// which types GET one. A disagreement is therefore never cosmetic. In one direction a type
    /// sits at <c>WPROTO041</c> with nothing able to clear it; in the other the assigner writes a
    /// manifest entry for a pair the generator refuses.
    /// </para>
    /// <para>
    /// The closed-generic case is the one that shipped in review and is worth stating plainly: a
    /// constructed <c>Cache&lt;List&lt;float&gt;&gt;</c> is neither an open definition nor does it
    /// contain generic parameters, so the obvious reflection predicates let it through. The manifest
    /// writes the base as <c>typeof(...)</c> from a CLR <c>FullName</c>, and a constructed generic's
    /// full name carries backticks and <c>[[...]]</c> -- so an automatic pass would have written a
    /// file that does not compile.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class WProtoSubtypeTagAssignerClassificationTests : CommonTestBase
    {
        /// <summary>
        /// A closed generic base is refused, so no manifest entry names one.
        /// </summary>
        /// <remarks>
        /// <c>SerializableDictionary.Cache&lt;T&gt;</c> is the shape every consumer of a cache-boxed
        /// dictionary writes, so this is the common case rather than a corner.
        /// </remarks>
        [Test]
        public void AClosedGenericBaseCannotCarryASubtype()
        {
            Assert.IsFalse(
                WProtoSubtypeTagAssigner.CanCarrySubtype(
                    typeof(SerializableDictionary.Cache<List<float>>),
                    typeof(CacheBox)
                ),
                "a constructed generic is as many types as it has closures; one field number cannot identify it"
            );
        }

        [Test]
        public void AnOpenGenericBaseCannotCarryASubtype()
        {
            Assert.IsFalse(
                WProtoSubtypeTagAssigner.CanCarrySubtype(
                    typeof(SerializableDictionary.Cache<>),
                    typeof(CacheBox)
                )
            );
        }

        [Test]
        public void AGenericSubtypeCannotBeCarried()
        {
            Assert.IsFalse(
                WProtoSubtypeTagAssigner.CanCarrySubtype(
                    typeof(PlainBase),
                    typeof(GenericLeaf<int>)
                )
            );
        }

        [Test]
        public void ANonGenericPairInOneAssemblyCanBeCarried()
        {
            Assert.IsTrue(
                WProtoSubtypeTagAssigner.CanCarrySubtype(typeof(PlainBase), typeof(PlainLeaf))
            );
        }

        /// <summary>
        /// A base in another assembly cannot carry a subtype: its chain was emitted first.
        /// </summary>
        [Test]
        public void ABaseInAnotherAssemblyCannotCarryASubtype()
        {
            Assert.IsFalse(
                WProtoSubtypeTagAssigner.CanCarrySubtype(typeof(PlainBase), typeof(string))
            );
        }

        [Test]
        public void ADeclaredContractIsSerialized()
        {
            Assert.IsTrue(WProtoSubtypeTagAssigner.IsSerializedContract(typeof(PlainBase)));
        }

        /// <summary>
        /// Deriving from a contract IS the declaration, transitively.
        /// </summary>
        [Test]
        public void AnInheritedContractIsSerializedThroughAnImplicitMiddle()
        {
            Assert.IsTrue(WProtoSubtypeTagAssigner.IsSerializedContract(typeof(PlainLeaf)));
            Assert.IsTrue(WProtoSubtypeTagAssigner.IsSerializedContract(typeof(PlainGrandchild)));
        }

        /// <summary>
        /// The opt-out stops the walk rather than excluding one type.
        /// </summary>
        [Test]
        public void TheOptOutStopsTheWalkForDescendantsToo()
        {
            Assert.IsFalse(WProtoSubtypeTagAssigner.IsSerializedContract(typeof(OptedOut)));
            Assert.IsFalse(
                WProtoSubtypeTagAssigner.IsSerializedContract(typeof(BelowTheOptOut)),
                "a subclass of an opted-out type has no serialized ancestor between it and the contract"
            );
        }

        /// <summary>
        /// A cache box is not serialized: its only path to a contract runs through a generic.
        /// </summary>
        /// <remarks>
        /// The other half of the closed-generic fix. Were this true, every consumer's cache box
        /// would be inventoried and asked for <c>partial</c>.
        /// </remarks>
        [Test]
        public void ACacheBoxIsNotSerialized()
        {
            Assert.IsFalse(WProtoSubtypeTagAssigner.IsSerializedContract(typeof(CacheBox)));
        }

        [Test]
        public void APlainTypeIsNotSerialized()
        {
            Assert.IsFalse(WProtoSubtypeTagAssigner.IsSerializedContract(typeof(Unrelated)));
        }

        /// <summary>A cache box of the shape the documentation tells every consumer to write.</summary>
        private sealed class CacheBox : SerializableDictionary.Cache<List<float>> { }

        /// <summary>A declared contract, the root of the fixture hierarchy.</summary>
        [WProtoContract]
        private partial class PlainBase { }

        /// <summary>An implicit subtype: it inherits its contract.</summary>
        private partial class PlainLeaf : PlainBase { }

        /// <summary>A subclass of an implicit subtype, which inherits it too.</summary>
        private partial class PlainGrandchild : PlainLeaf { }

        /// <summary>A generic subtype, which no single field number can identify.</summary>
        private sealed partial class GenericLeaf<T> : PlainBase { }

        /// <summary>An opted-out subclass, and one below it.</summary>
        [WProtoNotSerialized]
        private partial class OptedOut : PlainBase { }

        private sealed partial class BelowTheOptOut : OptedOut { }

        /// <summary>A type with no relationship to any contract.</summary>
        private sealed class Unrelated { }
    }
}
