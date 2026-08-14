// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// The nested and jagged collection shapes added for issue 399, at the field numbers the
    /// generator suite uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The numbers match <c>NestedCollectionContract</c> under <c>Generator~/</c> so the golden hex
    /// in <c>WProtoNestedCollectionTests</c> is a transcription of a measured payload rather than
    /// this session's reading of the wire format. That matters more here than anywhere else in the
    /// suite: these are the only shapes with no protobuf-net behaviour behind them, because
    /// protobuf-net refuses all of them, so the desktop suite's oracle cannot check them either.
    /// </para>
    /// <para>
    /// Each nested member is encoded as one wrapper message per inner collection --
    /// <c>message Wrapper { repeated T values = 1; }</c> -- which is what every other protobuf
    /// implementation emits for the equivalent schema.
    /// </para>
    /// </remarks>
    [WProtoContract]
    public sealed partial class WProtoNestedCollectionContract
    {
        /// <summary>A jagged array of a packable scalar: the inner run is packed.</summary>
        [WProtoMember(1)]
        public int[][] Rows;

        /// <summary>A list of arrays -- a different outer form over the same wrapper.</summary>
        [WProtoMember(2)]
        public List<int[]> Batches;

        /// <summary>Both levels a list.</summary>
        [WProtoMember(3)]
        public List<List<int>> Grid;

        /// <summary>A length-delimited element, so the inner run is unpacked.</summary>
        [WProtoMember(4)]
        public string[][] Names;

        /// <summary>Three levels, which is two wrappers deep.</summary>
        [WProtoMember(5)]
        public int[][][] Cube;

        /// <summary>An inner collection with its own form: a set, which cannot be sized.</summary>
        [WProtoMember(6)]
        public HashSet<int>[] Sets;

        /// <summary>A jagged array of contracts, so the wrapper holds a repeated sub-message.</summary>
        [WProtoMember(7)]
        public WProtoNestedPoint[][] Shapes;

        /// <summary>A collection of maps: the wrapper's own member is a map rather than a run.</summary>
        [WProtoMember(8)]
        public List<Dictionary<string, int>> Tables;

        /// <summary>A map whose value is a collection, the other position a wrapper serves.</summary>
        [WProtoMember(9)]
        public Dictionary<string, List<int>> Lookup;

        /// <summary>
        /// The counter-example. <c>byte[]</c> is a scalar rather than a repeated field, so
        /// <c>byte[][]</c> is an ordinary repeated member and must not gain a wrapper -- its bytes
        /// are data that already exists.
        /// </summary>
        [WProtoMember(10)]
        public byte[][] Blobs;
    }

    /// <summary>The element of a jagged array of contracts.</summary>
    [WProtoContract]
    public partial struct WProtoNestedPoint
    {
        /// <summary>The first component.</summary>
        [WProtoMember(1)]
        public int X;

        /// <summary>The second component.</summary>
        [WProtoMember(2)]
        public int Y;
    }
}
