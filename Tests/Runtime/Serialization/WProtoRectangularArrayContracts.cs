// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// The rectangular array shapes added for issue 434, at the field numbers the generator suite
    /// uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The numbers match <c>RectangularArrayContract</c> under <c>Generator~/</c> so the golden hex
    /// in <c>WProtoRectangularArrayTests</c> is a transcription of a measured payload rather than
    /// this session's reading of the wire format. protobuf-net refuses this shape at write on both
    /// supported majors, so there is no oracle here either -- the golden vectors are the whole
    /// guarantee, and the IL2CPP standalone legs are the only place this code is AOT-compiled.
    /// </para>
    /// <para>
    /// Each member is a wrapper message carrying a dimension header beside its elements --
    /// <c>message Rect { repeated int32 dims = 1; repeated T values = 2; }</c> -- because six
    /// elements alone cannot say whether they came from a two-by-three or a three-by-two.
    /// </para>
    /// </remarks>
    [WProtoContract]
    public sealed partial class WProtoRectangularArrayContract
    {
        /// <summary>The headline case: a grid of a packable scalar, so the run is packed.</summary>
        [WProtoMember(1)]
        public int[,] Grid;

        /// <summary>Rank three, which is what says the emitter is not hard-coded to two.</summary>
        [WProtoMember(2)]
        public int[,,] Volume;

        /// <summary>A length-delimited element, so the run is unpacked.</summary>
        [WProtoMember(3)]
        public string[,] Labels;

        /// <summary>A grid of contracts, so the wrapper's run holds sub-messages.</summary>
        [WProtoMember(4)]
        public WProtoNestedPoint[,] Points;

        /// <summary>A jagged array of rectangular arrays: the two capabilities meeting.</summary>
        [WProtoMember(5)]
        public int[][,] Layers;

        /// <summary>A rectangular array as an element of an ordinary collection.</summary>
        [WProtoMember(6)]
        public List<int[,]> Frames;

        /// <summary>A rectangular array as a map value, the other position a wrapper serves.</summary>
        [WProtoMember(7)]
        public Dictionary<string, int[,]> Named;

        /// <summary>
        /// A rectangular array of jagged arrays -- <c>int[,][]</c> is a two-dimensional array whose
        /// element is <c>int[]</c>, the mirror image of <see cref="Layers"/>.
        /// </summary>
        [WProtoMember(8)]
        public int[,][] Rows;

        /// <summary>
        /// The counter-example. <c>byte[]</c> is a scalar rather than a repeated field, so a grid of
        /// bytes is a rectangular array of a varint element rather than anything jagged.
        /// </summary>
        [WProtoMember(9)]
        public byte[,] Blobs;
    }
}
