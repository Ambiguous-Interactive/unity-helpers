// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System.Collections.Generic;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Every position a rectangular array can occupy (#434).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately <b>not</b> annotated for protobuf-net: it refuses this shape at write on both
    /// 2.4.9 and 3.2.56, which <see cref="RectangularArrayTests.TheOracleRefusesEveryRectangularShape"/>
    /// pins. So there is no payload in the wild with this shape, no oracle to be byte-compatible
    /// with, and the bytes are held to golden vectors and round trips instead.
    /// </para>
    /// <para>
    /// Each member is a wrapper message carrying a dimension header beside its elements --
    /// <c>message Rect { repeated int32 dims = 1; repeated T values = 2; }</c> -- because the
    /// elements alone cannot say whether six of them came from <c>[2,3]</c> or <c>[3,2]</c>.
    /// </para>
    /// </remarks>
    [WProtoContract]
    public sealed partial class RectangularArrayContract
    {
        /// <summary>The headline case: a grid of a packable scalar, so the run is packed.</summary>
        [WProtoMember(1)]
        public int[,] Grid;

        /// <summary>Rank three, which is what says the emitter is not hard-coded to two.</summary>
        [WProtoMember(2)]
        public int[,,] Volume;

        /// <summary>
        /// A length-delimited element, so the run is unpacked -- one key per element inside the
        /// wrapper rather than one key for the run. Same rule as a top-level repeated string.
        /// </summary>
        [WProtoMember(3)]
        public string[,] Labels;

        /// <summary>A grid of contracts, so the wrapper's run holds sub-messages.</summary>
        [WProtoMember(4)]
        public Outer.Point[,] Points;

        /// <summary>
        /// A jagged array OF rectangular arrays: a nested-collection wrapper whose element is a
        /// rectangular wrapper, which is the two capabilities meeting.
        /// </summary>
        [WProtoMember(5)]
        public int[][,] Layers;

        /// <summary>A rectangular array as an element of an ordinary collection.</summary>
        [WProtoMember(6)]
        public List<int[,]> Frames;

        /// <summary>A rectangular array as a map value, the other position a wrapper serves.</summary>
        [WProtoMember(7)]
        public Dictionary<string, int[,]> Named;

        /// <summary>
        /// A rectangular array OF jagged arrays -- <c>int[,][]</c> is a two-dimensional array whose
        /// element is <c>int[]</c>, which is the mirror image of <see cref="Layers"/>.
        /// </summary>
        [WProtoMember(8)]
        public int[,][] Rows;

        /// <summary>
        /// The counter-example, and the reason it belongs here: <c>byte[]</c> is a scalar rather
        /// than a repeated field, so a grid of them is a rectangular array of a length-delimited
        /// value rather than anything jagged.
        /// </summary>
        [WProtoMember(9)]
        public byte[,] Blobs;
    }

    /// <summary>
    /// The same rectangular member, annotated for protobuf-net so the refusal can be measured rather
    /// than asserted from a changelog.
    /// </summary>
    /// <remarks>
    /// The premise of the whole encoding is that no protobuf-net payload has this shape, so there is
    /// nothing to interoperate with and the wire form is free to be chosen. That premise is a
    /// measurement, and this contract is what keeps measuring it.
    /// </remarks>
    [ProtoContract]
    public sealed class OracleRectangularContract
    {
        /// <summary>The member protobuf-net refuses.</summary>
        [ProtoMember(1)]
        public int[,] Grid;
    }

    /// <summary>A rectangular array inside a contract that is built through a constructor.</summary>
    /// <remarks>
    /// A readonly member switches the whole contract to construct-at-end, which is the one path
    /// where a member's decoded value lives in a local rather than on an instance. A wrapper's own
    /// run already uses that path, so this is where the two could collide.
    /// </remarks>
    [WProtoContract]
    public sealed partial class ImmutableRectangularContract
    {
        /// <summary>A readonly grid.</summary>
        [WProtoMember(1)]
        public readonly int[,] Grid;

        /// <summary>The author's own constructor, which the generated one must not collide with.</summary>
        /// <param name="grid">The rectangular member.</param>
        public ImmutableRectangularContract(int[,] grid)
        {
            Grid = grid;
        }
    }

    /// <summary>A generic contract whose member is a rectangular array of its own type parameter.</summary>
    /// <typeparam name="T">The element type, decided at the closure.</typeparam>
    /// <remarks>
    /// Whether the run packs is a property of the closure rather than of the member --
    /// <c>RectangularBox&lt;int&gt;</c> packs and <c>RectangularBox&lt;string&gt;</c> cannot -- so
    /// this is the shape that proves the dimension header is independent of the element encoding.
    /// </remarks>
    [WProtoContract]
    public sealed partial class RectangularBox<T>
    {
        /// <summary>The grid, whose element encoding the closure decides.</summary>
        [WProtoMember(1)]
        public T[,] Grid;
    }
}
