// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System.Collections.Generic;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// The sub-message a duplicated field merges into, with two members so each occurrence can
    /// carry a different one.
    /// </summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class DuplicateChild
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int A;

        [ProtoMember(2)]
        [WProtoMember(2)]
        public int B;

        [ProtoMember(3)]
        [WProtoMember(3)]
        public string Text;
    }

    /// <summary>
    /// Carries a non-repeated scalar, a reference sub-message and a struct sub-message, so one
    /// payload can duplicate each of the three shapes a merge has to answer for.
    /// </summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class DuplicateHolder
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Number;

        [ProtoMember(2)]
        [WProtoMember(2)]
        public DuplicateChild Child;

        [ProtoMember(3)]
        [WProtoMember(3)]
        public Outer.Point Where;
    }

    /// <summary>
    /// The same shape with a sub-message its constructor has already filled in, so what the FIRST
    /// occurrence does to a value that is not null is visible.
    /// </summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class SeededHolder
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Number;

        [ProtoMember(2)]
        [WProtoMember(2)]
        public DuplicateChild Child = new DuplicateChild { A = 9 };
    }

    /// <summary>One level further out, so a merge can be proven to recurse.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class DuplicateGrandparent
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        public DuplicateHolder Holder;
    }

    /// <summary>
    /// Names the <see cref="Box{T}"/> closures the duplicate-field fixtures use, so the generator
    /// registers them.
    /// </summary>
    /// <remarks>
    /// A generic member's encoding is decided by its closure, so "does a duplicated sub-message
    /// merge" has to be asked of a closure that IS message-shaped. These are the two kinds --
    /// a reference contract and one carrying a lifecycle hook -- alongside the struct and scalar
    /// closures <c>BoxClosures</c> already names.
    /// </remarks>
    public static class DuplicateBoxClosures
    {
        /// <summary>A reference-message closure, whose occurrences must merge.</summary>
        public static Box<DuplicateChild> Children;

        /// <summary>A closure with an after-deserialization hook, which must run once.</summary>
        public static Box<HookedContract> Hooked;
    }

    /// <summary>A sub-message whose own field initializer gives it a value to preserve.</summary>
    /// <remarks>
    /// The seed has to come from a field initializer rather than an object initializer at the use
    /// site, so a generic contract -- which cannot name a value for its own type parameter -- can
    /// still produce one through <c>new T()</c>.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class SeededChild
    {
        /// <summary>Set by the initializer, so a payload that does not mention it must keep it.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int A = 9;

        /// <summary>The member a payload sets instead.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public int B;
    }

    /// <summary>
    /// Every shape a seeded sub-message member can take, so the merge is asked of each rather than
    /// generalized from the one the defect was found on.
    /// </summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class SeededShapes
    {
        /// <summary>A reference sub-message.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public DuplicateChild Reference = new DuplicateChild { A = 9 };

        /// <summary>A struct sub-message, which cannot be null and is always seeded.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public Outer.Point Where = new Outer.Point { X = 9 };

        /// <summary>A nullable struct sub-message, seeded through the nullable.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public Outer.Point? Maybe = new Outer.Point { X = 9 };

        /// <summary>A member reached through a surrogate, which is stored as neither wire shape.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        public ForeignVector3 Vector = new ForeignVector3 { x = 9 };
    }

    /// <summary>
    /// The same seed on a contract that reads without running its author's constructor.
    /// </summary>
    /// <remarks>
    /// protobuf-net allocates this one uninitialized, so its member starts <c>null</c> and there is
    /// no seed to merge into. This package's generated read constructor necessarily runs field
    /// initializers, so the seed EXISTS here and must nevertheless be ignored -- the same rule a
    /// repeated member already follows under <c>SkipConstructor</c>.
    /// </remarks>
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
    public sealed partial class SeededSkipHolder
    {
        /// <summary>A seed the oracle's uninitialized instance does not have.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public DuplicateChild Child = new DuplicateChild { A = 9 };

        /// <summary>
        /// The same question for a repeated member, which has appended to its initializer rather
        /// than replacing it ever since <c>SkipConstructor</c> shipped -- a contract declaring the
        /// flag and no constructor of its own never reached the rule that suppresses the seed.
        /// </summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public int[] Values = { 99 };
    }

    /// <summary>A generic contract that seeds its own type parameter.</summary>
    /// <remarks>
    /// The one way a generic member can carry a seed: the contract cannot name a value for
    /// <c>T</c>, but it can construct one, and the closure's own initializers supply the value.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public partial class SeededBox<T>
        where T : new()
    {
        /// <summary>A member typed as the parameter, seeded by construction.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public T Value = new T();
    }

    /// <summary>Names the <see cref="SeededBox{T}"/> closure this assembly uses.</summary>
    public static class SeededBoxClosures
    {
        /// <summary>A message closure whose instance carries a seed.</summary>
        public static SeededBox<SeededChild> Children;
    }

    /// <summary>
    /// A contract whose own constructor builds a <see cref="SeededSkipHolder"/>, so the nested
    /// instance is one the oracle has as well.
    /// </summary>
    /// <remarks>
    /// <c>SkipConstructor</c> says how an instance is CREATED. It says nothing about an instance a
    /// caller already holds, and a parent that constructed one hands over a value whose members the
    /// oracle populated the same way -- so they are real seeds, not artifacts of this generator.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class SkipSeedParent
    {
        /// <summary>Built by this contract's own initializer, initializers and all.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public SeededSkipHolder Child = new SeededSkipHolder();
    }

    /// <summary>
    /// A contract that builds itself -- one <c>readonly</c> member, so every value is held in a
    /// local and passed to a constructor at the end of the read -- whose own constructor seeds
    /// that member.
    /// </summary>
    /// <remarks>
    /// The seed exists for the oracle and not for this generator: protobuf-net assigns a
    /// <c>readonly</c> field by reflection onto an instance it constructed, while a formatter
    /// that constructs at the end has no instance to read a seed off. Which of the two is
    /// correct is a question about protobuf-net, so it is measured rather than assumed.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class SeededImmutableHolder
    {
        /// <summary>A sub-message the constructor fills in and the read cannot assign onto.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public readonly DuplicateChild Child;

        /// <summary>Seeds <see cref="Child"/> so a merge and a replace give different answers.</summary>
        public SeededImmutableHolder()
        {
            Child = new DuplicateChild { A = 9 };
        }
    }

    /// <summary>
    /// Every member kind on a contract that builds itself, each seeded by its own constructor.
    /// </summary>
    /// <remarks>
    /// <see cref="SeededImmutableHolder"/> asked the question of one sub-message. The answer -- the
    /// oracle merges -- does not generalize to the other three kinds on its own, because each has a
    /// different combining rule: repeated appends, a map merges by key, and a generic member's rule
    /// is decided by its closure. Every one is measured here rather than reasoned about.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class SeededImmutableShapes
    {
        /// <summary>A reference sub-message, whose first occurrence merges.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public readonly DuplicateChild Reference;

        /// <summary>A struct sub-message, which cannot be null and is always seeded.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public readonly Outer.Point Where;

        /// <summary>A repeated member, whose occurrences append rather than replace.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public readonly List<int> Values;

        /// <summary>A map member, whose entries merge by key.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        public readonly Dictionary<int, int> Map;

        /// <summary>Fills each member so a merge and a replace give different answers.</summary>
        public SeededImmutableShapes()
        {
            Reference = new DuplicateChild { A = 9 };
            Where = new Outer.Point { X = 9 };
            Values = new List<int> { 99 };
            Map = new Dictionary<int, int> { { 7, 9 } };
        }
    }

    /// <summary>
    /// The same shape with no constructor the oracle can call, so "what is there to merge into"
    /// has a different answer.
    /// </summary>
    /// <remarks>
    /// The canonical immutable class -- one parameterized constructor, all-readonly members -- is a
    /// shape this generator accepts and protobuf-net has no obvious way to create. Whether it
    /// refuses, or allocates uninitialized and therefore has no seed, decides whether seeding can
    /// be unconditional or has to be gated on a constructor being there to run.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class UnseededImmutableHolder
    {
        /// <summary>The member the only constructor fills.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public readonly DuplicateChild Child;

        /// <summary>The author's only constructor, which takes the member's value.</summary>
        /// <param name="child">The value to hold.</param>
        public UnseededImmutableHolder(DuplicateChild child)
        {
            Child = child;
        }
    }

    /// <summary>An immutable contract that declares no constructor of its own.</summary>
    /// <remarks>
    /// The shape that loses its implicit parameterless constructor to the one emitted for the read,
    /// unless a replacement is emitted with it. Both readers need that constructor: a consumer
    /// writing <c>new ImplicitlyConstructedImmutable()</c>, and protobuf-net, which refuses a type
    /// it cannot construct.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class ImplicitlyConstructedImmutable
    {
        /// <summary>A readonly member, which is what forces construction at the end of the read.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public readonly int Id;
    }

    /// <summary>
    /// An immutable contract that also declares <c>SkipConstructor</c>, which is a shape this
    /// package ships -- <c>PcgRandom</c> is one.
    /// </summary>
    /// <remarks>
    /// The two flags answer the same question differently. <c>SkipConstructor</c> asks protobuf-net
    /// to allocate uninitialized, so no constructor runs and no member has a seed; a readonly member
    /// makes this generator build the instance at the end of the read, where a seed would have to
    /// come from a construction the oracle never performs. Which one wins is a fact about the
    /// oracle, so it is measured.
    /// </remarks>
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
    public sealed partial class SeededSkipImmutableHolder
    {
        /// <summary>A sub-message the constructor fills and the read cannot assign onto.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public readonly DuplicateChild Child;

        /// <summary>A scalar the constructor fills, so an absent field has a value to keep.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public readonly int Number;

        /// <summary>Seeds both members, so a construction that ran is visible in the result.</summary>
        public SeededSkipImmutableHolder()
        {
            Child = new DuplicateChild { A = 9 };
            Number = 9;
        }
    }
}
