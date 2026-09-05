// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*
    This real implicit hierarchy requires committed inherited-subtype field numbers, just as a consumer’s
    hierarchy does.
*/
[assembly: WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoSubtypeTag(
    "WallstopStudios.UnityHelpers.Tests.Editor.Tools.ClassificationLeaf",
    typeof(WallstopStudios.UnityHelpers.Tests.Editor.Tools.ClassificationRoot),
    100
)]
[assembly: WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoSubtypeTag(
    "WallstopStudios.UnityHelpers.Tests.Editor.Tools.ClassificationGrandchild",
    typeof(WallstopStudios.UnityHelpers.Tests.Editor.Tools.ClassificationLeaf),
    101
)]

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
    /// The closed-generic case is the one review caught and is worth stating plainly: a constructed
    /// <c>Cache&lt;List&lt;float&gt;&gt;</c> is neither an open definition nor does it contain
    /// generic parameters, so the obvious reflection predicates let it through. The manifest writes
    /// its base as <c>typeof(...)</c> from a CLR <c>FullName</c>, and a constructed generic's full
    /// name carries backticks and <c>[[...]]</c> -- so an automatic pass would have written a file
    /// that does not compile.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class WProtoSubtypeTagAssignerClassificationTests : CommonTestBase
    {
        /// <summary>
        /// A closed generic base is refused, so no manifest entry can name one.
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
                    typeof(ClassificationCacheBox)
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
                    typeof(ClassificationCacheBox)
                )
            );
        }

        [Test]
        public void AGenericSubtypeCannotBeCarried()
        {
            Assert.IsFalse(
                WProtoSubtypeTagAssigner.CanCarrySubtype(
                    typeof(ClassificationRoot),
                    typeof(ClassificationGenericLeaf<int>)
                )
            );
        }

        [Test]
        public void ANonGenericPairInOneAssemblyCanBeCarried()
        {
            Assert.IsTrue(
                WProtoSubtypeTagAssigner.CanCarrySubtype(
                    typeof(ClassificationRoot),
                    typeof(ClassificationLeaf)
                )
            );
        }

        /// <summary>
        /// A base in another assembly cannot carry a subtype: its chain was emitted first.
        /// </summary>
        [Test]
        public void ABaseInAnotherAssemblyCannotCarryASubtype()
        {
            Assert.IsFalse(
                WProtoSubtypeTagAssigner.CanCarrySubtype(typeof(ClassificationRoot), typeof(string))
            );
        }

        [Test]
        public void ADeclaredContractIsSerialized()
        {
            Assert.IsTrue(
                WProtoSubtypeTagAssigner.IsSerializedContract(typeof(ClassificationRoot))
            );
        }

        /// <summary>
        /// Deriving from a contract IS the declaration, transitively.
        /// </summary>
        [Test]
        public void AnInheritedContractIsSerializedThroughAnImplicitMiddle()
        {
            Assert.IsTrue(
                WProtoSubtypeTagAssigner.IsSerializedContract(typeof(ClassificationLeaf))
            );
            Assert.IsTrue(
                WProtoSubtypeTagAssigner.IsSerializedContract(typeof(ClassificationGrandchild))
            );
        }

        /// <summary>
        /// The opt-out stops the walk rather than excluding one type.
        /// </summary>
        [Test]
        public void TheOptOutStopsTheWalkForDescendantsToo()
        {
            Assert.IsFalse(
                WProtoSubtypeTagAssigner.IsSerializedContract(typeof(ClassificationOptedOut))
            );
            Assert.IsFalse(
                WProtoSubtypeTagAssigner.IsSerializedContract(typeof(ClassificationBelowOptOut)),
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
            Assert.IsFalse(
                WProtoSubtypeTagAssigner.IsSerializedContract(typeof(ClassificationCacheBox))
            );
        }

        [Test]
        public void APlainTypeIsNotSerialized()
        {
            Assert.IsFalse(
                WProtoSubtypeTagAssigner.IsSerializedContract(typeof(ClassificationUnrelated))
            );
        }
    }

    /// <summary>A declared contract, the root of the fixture hierarchy.</summary>
    /// <remarks>
    /// At namespace scope rather than nested in the fixture, because the generated formatter is a
    /// nested type and <c>WPROTO001</c> requires <c>partial</c> on <b>every</b> enclosing type. A
    /// `[TestFixture]` is not partial, so a contract inside one cannot be generated for.
    /// </remarks>
    [WProtoContract]
    internal partial class ClassificationRoot { }

    /// <summary>An implicit subtype: it inherits its contract by deriving from one.</summary>
    internal partial class ClassificationLeaf : ClassificationRoot { }

    /// <summary>A subclass of an implicit subtype, which inherits it too.</summary>
    internal partial class ClassificationGrandchild : ClassificationLeaf { }

    /// <summary>A generic subtype, which no single field number can identify.</summary>
    internal sealed partial class ClassificationGenericLeaf<T> : ClassificationRoot { }

    /// <summary>An opted-out subclass.</summary>
    [WProtoNotSerialized]
    internal partial class ClassificationOptedOut : ClassificationRoot { }

    /// <summary>A subclass below the opt-out, which is not serialized either.</summary>
    internal sealed partial class ClassificationBelowOptOut : ClassificationOptedOut { }

    /// <summary>A cache box of the shape the documentation tells every consumer to write.</summary>
    internal sealed class ClassificationCacheBox : SerializableDictionary.Cache<List<float>> { }

    /// <summary>A type with no relationship to any contract.</summary>
    internal sealed class ClassificationUnrelated { }
}
