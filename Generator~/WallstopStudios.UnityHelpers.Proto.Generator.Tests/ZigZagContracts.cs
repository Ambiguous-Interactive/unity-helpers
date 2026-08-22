// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// One member of every signed-integer type that has a ZigZag encoding, beside one that declines
    /// it.
    /// </summary>
    /// <remarks>
    /// The plain neighbor is not filler: both encodings have to survive in one message, and a
    /// generator that applied the annotation to the whole contract rather than to the member that
    /// carries it would pass every test that used only annotated members.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class ZigZagContract
    {
        /// <summary>An <c>sint32</c>.</summary>
        [ProtoMember(1, DataFormat = DataFormat.ZigZag)]
        [WProtoMember(1, DataFormat = WProtoDataFormat.ZigZag)]
        public int Int32;

        /// <summary>An <c>sint64</c>.</summary>
        [ProtoMember(2, DataFormat = DataFormat.ZigZag)]
        [WProtoMember(2, DataFormat = WProtoDataFormat.ZigZag)]
        public long Int64;

        /// <summary>A narrower signed integer, which protobuf widens to <c>sint32</c>.</summary>
        [ProtoMember(3, DataFormat = DataFormat.ZigZag)]
        [WProtoMember(3, DataFormat = WProtoDataFormat.ZigZag)]
        public short Int16;

        /// <summary>The narrowest one.</summary>
        [ProtoMember(4, DataFormat = DataFormat.ZigZag)]
        [WProtoMember(4, DataFormat = WProtoDataFormat.ZigZag)]
        public sbyte Int8;

        /// <summary>A ZigZag member whose absence is expressible, so presence is not just "non-zero".</summary>
        [ProtoMember(5, DataFormat = DataFormat.ZigZag)]
        [WProtoMember(5, DataFormat = WProtoDataFormat.ZigZag)]
        public int? MaybeInt32;

        /// <summary>The neighbor that keeps protobuf's default <c>int32</c>.</summary>
        [ProtoMember(6)]
        [WProtoMember(6)]
        public int Plain;
    }

    /// <summary>
    /// The exact shape of <c>FastVector3IntSurrogate</c>, which the package ships and this project
    /// cannot compile.
    /// </summary>
    /// <remarks>
    /// <c>FastVector3Int</c> lives in the Unity runtime assembly, which does not load outside an
    /// editor, so the bytes its surrogate produces cannot be measured here directly. They can be
    /// measured through a stand-in that declares the same six fields with the same numbers and the
    /// same <c>DataFormat</c>s -- which is the whole of what decides the encoding -- and the golden
    /// hex the Unity fixture pins is copied out of this one, against the real protobuf-net.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class GridCellShape
    {
        /// <summary>The component on the field the shipped surrogate writes it on.</summary>
        [ProtoMember(5, DataFormat = DataFormat.ZigZag)]
        [WProtoMember(5, DataFormat = WProtoDataFormat.ZigZag)]
        public int X;

        /// <summary>As <see cref="X"/>.</summary>
        [ProtoMember(6, DataFormat = DataFormat.ZigZag)]
        [WProtoMember(6, DataFormat = WProtoDataFormat.ZigZag)]
        public int Y;

        /// <summary>As <see cref="X"/>.</summary>
        [ProtoMember(7, DataFormat = DataFormat.ZigZag)]
        [WProtoMember(7, DataFormat = WProtoDataFormat.ZigZag)]
        public int Z;

        /// <summary>The field <c>x</c> occupied while it was an <c>int32</c>.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int LegacyX;

        /// <summary>The field <c>y</c> occupied while it was an <c>int32</c>.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public int LegacyY;

        /// <summary>The field <c>z</c> occupied, skipping 3, which carried the cached hash.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        public int LegacyZ;
    }
}
