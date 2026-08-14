// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System.Collections.Generic;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Every shape in which one collection holds another (#399).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately <b>not</b> annotated for protobuf-net: it refuses every member here, on both
    /// 2.4.9 and 3.2.56, which <see cref="NestedCollectionTests.TheOracleRefusesEveryNestedShape"/>
    /// pins. So the differential that covers every other contract cannot cover this one, and its
    /// bytes are held to golden vectors and to round trips instead.
    /// </para>
    /// <para>
    /// Each member is a wrapper message per inner collection --
    /// <c>message Wrapper { repeated T values = 1; }</c> -- so <see cref="Rows"/> encodes as field 1
    /// repeated, each occurrence a length-delimited message whose own field 1 is the packed inner
    /// run.
    /// </para>
    /// </remarks>
    [WProtoContract]
    public sealed partial class NestedCollectionContract
    {
        /// <summary>A jagged array of a packable scalar: the inner run is packed.</summary>
        [WProtoMember(1)]
        public int[][] Rows;

        /// <summary>A list of arrays -- a different outer form over the same wrapper.</summary>
        [WProtoMember(2)]
        public List<int[]> Batches;

        /// <summary>Both levels a list, which is the shape protobuf-net names in its refusal.</summary>
        [WProtoMember(3)]
        public List<List<int>> Grid;

        /// <summary>
        /// A jagged array of a length-delimited scalar, so the inner run is unpacked -- one key per
        /// element inside the wrapper rather than one key for the run.
        /// </summary>
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
        public Outer.Point[][] Shapes;

        /// <summary>A collection of maps: the wrapper's own member is a map rather than a run.</summary>
        [WProtoMember(8)]
        public List<Dictionary<string, int>> Tables;

        /// <summary>A map whose value is a collection, which is the other position a wrapper serves.</summary>
        [WProtoMember(9)]
        public Dictionary<string, List<int>> Lookup;

        /// <summary>
        /// The counter-example, and the reason it belongs in this contract: <c>byte[]</c> is a
        /// scalar rather than a repeated field, so <c>byte[][]</c> is an ordinary repeated member
        /// and must NOT gain a wrapper. Its bytes have to be identical to what shipped before this
        /// capability existed.
        /// </summary>
        [WProtoMember(10)]
        public byte[][] Blobs;
    }

    /// <summary>
    /// The same jagged member, annotated for protobuf-net so the refusal can be measured rather
    /// than asserted from a changelog.
    /// </summary>
    /// <remarks>
    /// The premise of the whole encoding is that no protobuf-net payload has this shape, so there
    /// is nothing to interoperate with and the wire form is free to be chosen. That premise is a
    /// measurement, and this contract is what keeps measuring it.
    /// </remarks>
    [ProtoContract]
    public sealed class OracleNestedContract
    {
        /// <summary>The member protobuf-net refuses.</summary>
        [ProtoMember(1)]
        public int[][] Rows;
    }

    /// <summary>A nested collection inside a contract that is built through a constructor.</summary>
    /// <remarks>
    /// A readonly member switches the whole contract to construct-at-end, which is the one path
    /// where a member's decoded value lives in a local rather than on an instance. A wrapper's own
    /// member already uses that path, so this is where the two could collide.
    /// </remarks>
    [WProtoContract]
    public sealed partial class ImmutableNestedContract
    {
        /// <summary>A readonly jagged array.</summary>
        [WProtoMember(1)]
        public readonly int[][] Rows;

        /// <summary>A readonly map of collections, alongside it.</summary>
        [WProtoMember(2)]
        public readonly Dictionary<string, List<int>> Lookup;

        /// <summary>The author's own constructor, which the generated one must not collide with.</summary>
        /// <param name="rows">The jagged member.</param>
        /// <param name="lookup">The map member.</param>
        public ImmutableNestedContract(int[][] rows, Dictionary<string, List<int>> lookup)
        {
            Rows = rows;
            Lookup = lookup;
        }
    }

    /// <summary>A polymorphic base whose subtype holds a nested collection.</summary>
    /// <remarks>
    /// Every member of a contract declaring <c>[WProtoInclude]</c> is deferred -- collected aside
    /// and committed once an include tag has settled which instance they land on. A wrapper's member
    /// is never deferred, so this is where a deferred outer and a non-deferred inner meet.
    /// </remarks>
    [WProtoContract]
    [WProtoInclude(10, typeof(NestedIncludeSubtype))]
    public partial class NestedIncludeBase
    {
        /// <summary>A jagged member on the base, deferred like every other.</summary>
        [WProtoMember(1)]
        public int[][] Rows;
    }

    /// <summary>The subtype, with a nested collection of its own.</summary>
    [WProtoContract]
    public sealed partial class NestedIncludeSubtype : NestedIncludeBase
    {
        /// <summary>A nested collection on the subtype.</summary>
        [WProtoMember(2)]
        public List<List<string>> Extra;
    }
}
