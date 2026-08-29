// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// One hierarchy declared from the base, and its twin declared from the subtypes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two are member-for-member and tag-for-tag identical, which is what makes them a
    /// controlled comparison: any difference in the bytes is a difference the declaration form
    /// caused. Both carry protobuf-net's own annotations as well, so the oracle serves as the third
    /// opinion rather than the pair merely agreeing with each other.
    /// </para>
    /// <para>
    /// protobuf-net has no equivalent of <c>[WProtoSubtype]</c>, so the subject hierarchy still
    /// carries <c>[ProtoInclude]</c> on its base -- that is the oracle's declaration, not
    /// WallstopProto's, and no <c>[WProtoInclude]</c> appears anywhere in it.
    /// </para>
    /// </remarks>
    [ProtoContract]
    [ProtoInclude(100, typeof(BaseFormAlpha))]
    [ProtoInclude(101, typeof(BaseFormBeta))]
    [WProtoContract]
    [WProtoInclude(100, typeof(BaseFormAlpha))]
    [WProtoInclude(101, typeof(BaseFormBeta))]
    public partial class BaseFormRoot
    {
        /// <summary>A base member, written after the include.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Id;

        /// <summary>A length-delimited base member.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public string Label;
    }

    /// <summary>A leaf subtype the base declares.</summary>
    [ProtoContract]
    [WProtoContract]
    public partial class BaseFormAlpha : BaseFormRoot
    {
        /// <summary>The subtype's own member, in its own tag space.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int AlphaOnly;

        /// <summary>A second one, so the sub-message carries more than a marker.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public string AlphaText;
    }

    /// <summary>A subtype that is itself a base, so the nesting recurses.</summary>
    [ProtoContract]
    [ProtoInclude(200, typeof(BaseFormGamma))]
    [WProtoContract]
    [WProtoInclude(200, typeof(BaseFormGamma))]
    public partial class BaseFormBeta : BaseFormRoot
    {
        /// <summary>A fixed64 member at the middle level.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public double BetaOnly;
    }

    /// <summary>The third level of the base-declared hierarchy.</summary>
    [ProtoContract]
    [WProtoContract]
    public partial class BaseFormGamma : BaseFormBeta
    {
        /// <summary>The deepest member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public bool GammaOnly;
    }

    /// <summary>Holds a base-declared value, so the chain sits under a length prefix.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class BaseFormHolder
    {
        /// <summary>The polymorphic member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public BaseFormRoot Value;

        /// <summary>A scalar after it.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public int Trailer;
    }

    /// <summary>
    /// The same hierarchy with no <c>[WProtoInclude]</c> anywhere: each subtype declares itself.
    /// </summary>
    [ProtoContract]
    [ProtoInclude(100, typeof(SubtypeFormAlpha))]
    [ProtoInclude(101, typeof(SubtypeFormBeta))]
    [WProtoContract]
    public partial class SubtypeFormRoot
    {
        /// <summary>A base member, written after the include.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Id;

        /// <summary>A length-delimited base member.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public string Label;
    }

    /// <summary>A leaf subtype that declares itself.</summary>
    [ProtoContract]
    [WProtoContract]
    [WProtoSubtype(typeof(SubtypeFormRoot), 100)]
    public partial class SubtypeFormAlpha : SubtypeFormRoot
    {
        /// <summary>The subtype's own member, in its own tag space.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int AlphaOnly;

        /// <summary>A second one, so the sub-message carries more than a marker.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public string AlphaText;
    }

    /// <summary>A self-declaring subtype that is itself a base.</summary>
    [ProtoContract]
    [ProtoInclude(200, typeof(SubtypeFormGamma))]
    [WProtoContract]
    [WProtoSubtype(typeof(SubtypeFormRoot), 101)]
    public partial class SubtypeFormBeta : SubtypeFormRoot
    {
        /// <summary>A fixed64 member at the middle level.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public double BetaOnly;
    }

    /// <summary>The third level, declared against the middle one rather than the root.</summary>
    [ProtoContract]
    [WProtoContract]
    [WProtoSubtype(typeof(SubtypeFormBeta), 200)]
    public partial class SubtypeFormGamma : SubtypeFormBeta
    {
        /// <summary>The deepest member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public bool GammaOnly;
    }

    /// <summary>Holds a self-declaring value, so the chain sits under a length prefix.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class SubtypeFormHolder
    {
        /// <summary>The polymorphic member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public SubtypeFormRoot Value;

        /// <summary>A scalar after it.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public int Trailer;
    }

    /// <summary>
    /// One base carrying both declaration forms, for different subtypes.
    /// </summary>
    /// <remarks>
    /// The case a merged include set has to get right: an author adopting the new form on a base
    /// that already ships includes must not have to move the ones already there, because moving a
    /// tag is what silently changes what an old payload deserializes to.
    /// </remarks>
    [ProtoContract]
    [ProtoInclude(100, typeof(MixedFormAlpha))]
    [ProtoInclude(101, typeof(MixedFormBeta))]
    [WProtoContract]
    [WProtoInclude(100, typeof(MixedFormAlpha))]
    public partial class MixedFormRoot
    {
        /// <summary>A base member, written after the include.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Id;

        /// <summary>A length-delimited base member.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public string Label;
    }

    /// <summary>The subtype the base still declares.</summary>
    [ProtoContract]
    [WProtoContract]
    public partial class MixedFormAlpha : MixedFormRoot
    {
        /// <summary>The subtype's own member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int AlphaOnly;

        /// <summary>A second one, so the sub-message carries more than a marker.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public string AlphaText;
    }

    /// <summary>The subtype that declares itself, on the same base.</summary>
    [ProtoContract]
    [WProtoContract]
    [WProtoSubtype(typeof(MixedFormRoot), 101)]
    public partial class MixedFormBeta : MixedFormRoot
    {
        /// <summary>A fixed64 member at the middle level.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public double BetaOnly;
    }

    /// <summary>
    /// A self-declaring subtype whose field number is <b>lower</b> than a base member's.
    /// </summary>
    /// <remarks>
    /// What rules out "includes happen to sort last because their tags are large" for the new form
    /// as well: tag 3 is emitted ahead of base members at 1 and 5.
    /// </remarks>
    [ProtoContract]
    [ProtoInclude(3, typeof(SubtypeLowTagSub))]
    [WProtoContract]
    public partial class SubtypeLowTagBase
    {
        /// <summary>A member numbered below the include.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int First;

        /// <summary>A member numbered above the include.</summary>
        [ProtoMember(5)]
        [WProtoMember(5)]
        public int Fifth;
    }

    /// <summary>The low-tag subtype, declaring itself.</summary>
    [ProtoContract]
    [WProtoContract]
    [WProtoSubtype(typeof(SubtypeLowTagBase), 3)]
    public partial class SubtypeLowTagSub : SubtypeLowTagBase
    {
        /// <summary>The subtype's own member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int SubOnly;
    }

    /// <summary>
    /// An abstract base whose only subtypes declare themselves.
    /// </summary>
    /// <remarks>
    /// WPROTO014 refuses an abstract contract with no subtypes, so this also pins that the merged
    /// set is what that check consults rather than the base's own attributes.
    /// </remarks>
    [WProtoContract]
    public abstract partial class SubtypeAbstractBase
    {
        /// <summary>A base member.</summary>
        [WProtoMember(1)]
        public int Sides;
    }

    /// <summary>The only concrete shape of <see cref="SubtypeAbstractBase"/>.</summary>
    [WProtoContract]
    [WProtoSubtype(typeof(SubtypeAbstractBase), 100)]
    public sealed partial class SubtypeAbstractConcrete : SubtypeAbstractBase
    {
        /// <summary>The concrete member.</summary>
        [WProtoMember(1)]
        public int Edge;
    }

    /// <summary>
    /// A subtype of a self-declared subtype that declares nothing itself.
    /// </summary>
    /// <remarks>
    /// <c>value is SubtypeFormAlpha</c> is true for this, so a chain without a narrowing test
    /// would write it under Alpha's field number and read it back as an Alpha. The new
    /// declaration form has to be refused on exactly the same terms as the old one.
    /// </remarks>
    public sealed class UndeclaredSubtypeFormAlpha : SubtypeFormAlpha { }

    /// <summary>
    /// A base that claims one subtype under two field numbers.
    /// </summary>
    /// <remarks>
    /// Neither declaration form refuses this, and neither does protobuf-net. Which number reaches
    /// the wire is therefore a behaviour rather than a rule, and a durable format cannot leave it
    /// unpinned: a save written today has to still read after someone deletes the declaration they
    /// believed was the dead one.
    /// </remarks>
    [ProtoContract]
    [ProtoInclude(5, typeof(TwiceClaimedSubtype))]
    [ProtoInclude(6, typeof(TwiceClaimedSubtype))]
    [WProtoContract]
    [WProtoInclude(5, typeof(TwiceClaimedSubtype))]
    [WProtoInclude(6, typeof(TwiceClaimedSubtype))]
    public partial class TwiceClaimingBase
    {
        /// <summary>A base member, written after the include.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Id;
    }

    /// <summary>The subtype two field numbers on <see cref="TwiceClaimingBase"/> both name.</summary>
    [ProtoContract]
    [WProtoContract]
    public partial class TwiceClaimedSubtype : TwiceClaimingBase
    {
        /// <summary>A subtype member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Extra;
    }

    /// <summary>
    /// The same hierarchy again, with no field number written anywhere in the source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each subtype says only which base it is written as; the numbers come from the
    /// <c>[assembly: WProtoSubtypeTag]</c> entries in this assembly's AssemblyInfo, which is where
    /// the assignment tool writes them. Tag for tag it matches the <c>BaseForm</c> and
    /// <c>SubtypeForm</c> twins, so any byte that differs is a byte the manifest caused.
    /// </para>
    /// <para>
    /// protobuf-net still needs its own <c>[ProtoInclude]</c> on the base, because it has no
    /// manifest of any kind. That is the oracle's declaration, and it is what makes this a
    /// three-way comparison rather than two spellings agreeing with each other.
    /// </para>
    /// </remarks>
    [ProtoContract]
    [ProtoInclude(100, typeof(ManifestFormAlpha))]
    [ProtoInclude(101, typeof(ManifestFormBeta))]
    [WProtoContract]
    public partial class ManifestFormRoot
    {
        /// <summary>A base member, written after the include.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Id;

        /// <summary>A length-delimited base member.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public string Label;
    }

    /// <summary>A leaf subtype that states no number at all.</summary>
    [ProtoContract]
    [WProtoContract]
    [WProtoSubtype(typeof(ManifestFormRoot))]
    public partial class ManifestFormAlpha : ManifestFormRoot
    {
        /// <summary>The subtype's own member, in its own tag space.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int AlphaOnly;

        /// <summary>A second one, so the sub-message carries more than a marker.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public string AlphaText;
    }

    /// <summary>A numberless subtype that is itself a base.</summary>
    [ProtoContract]
    [ProtoInclude(200, typeof(ManifestFormGamma))]
    [WProtoContract]
    [WProtoSubtype(typeof(ManifestFormRoot))]
    public partial class ManifestFormBeta : ManifestFormRoot
    {
        /// <summary>A fixed64 member at the middle level.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public double BetaOnly;
    }

    /// <summary>The third level, numbered against the middle one by the manifest.</summary>
    [ProtoContract]
    [WProtoContract]
    [WProtoSubtype(typeof(ManifestFormBeta))]
    public partial class ManifestFormGamma : ManifestFormBeta
    {
        /// <summary>The deepest member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public bool GammaOnly;
    }

    /// <summary>Holds a manifest-numbered value, so the chain sits under a length prefix.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class ManifestFormHolder
    {
        /// <summary>The polymorphic member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public ManifestFormRoot Value;

        /// <summary>A scalar after it.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public int Trailer;
    }
}
