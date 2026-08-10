// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// A polymorphic base, shaped like <c>AbstractRandom</c>'s 17 includes at tags 100-116.
    /// </summary>
    /// <remarks>
    /// The field numbers match the contract the differential suite under <c>Generator~/</c> compares
    /// against protobuf-net, so the hex in <c>WProtoIncludeContractTests</c> is the oracle's own
    /// output. What only Unity can prove is that the runtime-type dispatch AOT-compiles under
    /// IL2CPP, which is the whole reason this serializer exists.
    /// </remarks>
    [WProtoContract]
    [WProtoInclude(100, typeof(WProtoIncludeAlpha))]
    [WProtoInclude(101, typeof(WProtoIncludeBeta))]
    public partial class WProtoIncludeBase
    {
        /// <summary>A base member, written after the include.</summary>
        [WProtoMember(1)]
        public int Id;

        /// <summary>A length-delimited base member.</summary>
        [WProtoMember(2)]
        public string Label;
    }

    /// <summary>A leaf subtype.</summary>
    [WProtoContract]
    public partial class WProtoIncludeAlpha : WProtoIncludeBase
    {
        /// <summary>The subtype's own member, in its own tag space.</summary>
        [WProtoMember(1)]
        public int AlphaOnly;

        /// <summary>A second one, so the sub-message carries more than a marker.</summary>
        [WProtoMember(2)]
        public string AlphaText;
    }

    /// <summary>A subtype that is itself a base, so the include nesting recurses.</summary>
    [WProtoContract]
    [WProtoInclude(200, typeof(WProtoIncludeGamma))]
    public partial class WProtoIncludeBeta : WProtoIncludeBase
    {
        /// <summary>A fixed64 member at the middle level.</summary>
        [WProtoMember(1)]
        public double BetaOnly;
    }

    /// <summary>The third level.</summary>
    [WProtoContract]
    public partial class WProtoIncludeGamma : WProtoIncludeBeta
    {
        /// <summary>The deepest member.</summary>
        [WProtoMember(1)]
        public bool GammaOnly;
    }

    /// <summary>Holds a polymorphic value, so the chain sits under a length prefix.</summary>
    [WProtoContract]
    public sealed partial class WProtoIncludeHolder
    {
        /// <summary>The polymorphic member.</summary>
        [WProtoMember(1)]
        public WProtoIncludeBase Value;

        /// <summary>A scalar after it.</summary>
        [WProtoMember(2)]
        public int Trailer;
    }

    /// <summary>
    /// An include whose tag is <b>lower</b> than a base member's, which is what rules out "includes
    /// happen to sort last because their tags are large".
    /// </summary>
    [WProtoContract]
    [WProtoInclude(3, typeof(WProtoLowTagSub))]
    public partial class WProtoLowTagBase
    {
        /// <summary>A member numbered below the include.</summary>
        [WProtoMember(1)]
        public int First;

        /// <summary>A member numbered above the include.</summary>
        [WProtoMember(5)]
        public int Fifth;
    }

    /// <summary>The subtype for <see cref="WProtoLowTagBase"/>.</summary>
    [WProtoContract]
    public partial class WProtoLowTagSub : WProtoLowTagBase
    {
        /// <summary>The subtype's own member.</summary>
        [WProtoMember(1)]
        public int SubOnly;
    }

    /// <summary>An abstract base with an include, which is the shape AbstractRandom has.</summary>
    [WProtoContract]
    [WProtoInclude(100, typeof(WProtoConcreteShape))]
    public abstract partial class WProtoAbstractShape
    {
        /// <summary>A base member.</summary>
        [WProtoMember(1)]
        public int Sides;
    }

    /// <summary>The only concrete shape.</summary>
    [WProtoContract]
    public partial class WProtoConcreteShape : WProtoAbstractShape
    {
        /// <summary>The subtype's own member.</summary>
        [WProtoMember(1)]
        public int Edge;
    }

    /// <summary>
    /// An abstract polymorphic base whose members include a <b>collection</b>.
    /// </summary>
    /// <remarks>
    /// The shape that crashed before review caught it: an abstract base has no instance until an
    /// include tag arrives, so a collection element read before that tag had no member to seed from.
    /// Elements are collected aside and combined once the instance is final, which is generated code
    /// worth AOT-compiling.
    /// </remarks>
    [WProtoContract]
    [WProtoInclude(100, typeof(WProtoPolyListSub))]
    public abstract partial class WProtoPolyListBase
    {
        /// <summary>A collection on an abstract base.</summary>
        [WProtoMember(1)]
        public List<int> Items = new List<int> { 7, 8 };

        /// <summary>An array too, since the two take different epilogue paths.</summary>
        [WProtoMember(2)]
        public int[] Extras;
    }

    /// <summary>The subtype, whose constructor seeds a different collection from its base's.</summary>
    [WProtoContract]
    public partial class WProtoPolyListSub : WProtoPolyListBase
    {
        /// <summary>Replaces the base constructor's seed, so the two are distinguishable.</summary>
        public WProtoPolyListSub()
        {
            Items = new List<int> { 5 };
        }

        /// <summary>The subtype's own member.</summary>
        [WProtoMember(1)]
        public int SubOnly;
    }
}
