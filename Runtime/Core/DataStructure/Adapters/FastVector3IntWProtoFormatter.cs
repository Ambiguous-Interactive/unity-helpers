// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure.Adapters
{
    using Serialization.WallstopProto;

    public readonly partial struct FastVector3Int
    {
        /// <summary>
        /// Reads and writes <see cref="FastVector3Int"/> in protobuf wire format, byte-for-byte as
        /// protobuf-net writes it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Components are written as <c>sint32</c> on fields 5, 6 and 7. Fields 1, 2 and 4 -- the
        /// <c>int32</c>s they replace -- are still read, so a grid saved by an earlier build loads
        /// unchanged. See <see cref="FastVector2Int.WProtoFormatter"/> for why that is additive
        /// rather than a renumbering.
        /// </para>
        /// <para>
        /// This type is the reason the emit order is worth stating out loud: while the hash was on
        /// the wire its members were declared x, y, <b>z</b>, hash but tagged 1, 2, <b>4</b>, 3, and
        /// protobuf-net emits in ascending field number, so the cached hash went out <i>before</i>
        /// z. Field 3 is now unused, and z did not move onto it -- a legacy payload would have read
        /// its hash as z.
        /// </para>
        /// </remarks>
        public sealed class WProtoFormatter : IWProtoFormatter<FastVector3Int>
        {
            /// <summary>The field carrying <c>x</c> as a ZigZag varint.</summary>
            internal const int XTag = 5;

            /// <summary>The field carrying <c>y</c> as a ZigZag varint.</summary>
            internal const int YTag = 6;

            /// <summary>The field carrying <c>z</c> as a ZigZag varint.</summary>
            internal const int ZTag = 7;

            /// <summary>The field <c>x</c> occupied while it was an <c>int32</c>; read, never written.</summary>
            internal const int LegacyXTag = 1;

            /// <summary>The field <c>y</c> occupied while it was an <c>int32</c>; read, never written.</summary>
            internal const int LegacyYTag = 2;

            /// <summary>The field <c>z</c> occupied while it was an <c>int32</c>; read, never written.</summary>
            internal const int LegacyZTag = 4;

            /// <summary>The shared instance; the formatter holds no state.</summary>
            public static readonly WProtoFormatter Instance = new();

            /// <inheritdoc />
            public int Measure(in FastVector3Int value)
            {
                int size = 0;
                if (value.x != 0)
                {
                    size += WProtoSizes.TagSize(XTag) + WProtoSizes.ZigZag32Size(value.x);
                }

                if (value.y != 0)
                {
                    size += WProtoSizes.TagSize(YTag) + WProtoSizes.ZigZag32Size(value.y);
                }

                if (value.z != 0)
                {
                    size += WProtoSizes.TagSize(ZTag) + WProtoSizes.ZigZag32Size(value.z);
                }

                return size;
            }

            /// <inheritdoc />
            public bool Write(ref WProtoWriter writer, in FastVector3Int value)
            {
                if (value.x != 0)
                {
                    if (
                        !writer.TryWriteTag(XTag, WProtoWireType.Varint)
                        || !writer.TryWriteZigZag32(value.x)
                    )
                    {
                        return false;
                    }
                }

                if (value.y != 0)
                {
                    if (
                        !writer.TryWriteTag(YTag, WProtoWireType.Varint)
                        || !writer.TryWriteZigZag32(value.y)
                    )
                    {
                        return false;
                    }
                }

                if (value.z != 0)
                {
                    if (
                        !writer.TryWriteTag(ZTag, WProtoWireType.Varint)
                        || !writer.TryWriteZigZag32(value.z)
                    )
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <inheritdoc />
            /// <remarks>
            /// The value is rebuilt through the public constructor, which recomputes the cached
            /// hash. A payload written before the hash left the wire still carries it as field 3 and
            /// is skipped as unknown. See <see cref="FastVector2Int.WProtoFormatter.TryRead"/> for
            /// why the components are not trusted from the wire.
            /// </remarks>
            public bool TryRead(ref WProtoReader reader, out FastVector3Int value)
            {
                int x = 0;
                int y = 0;
                int z = 0;

                while (reader.TryReadTag(out int fieldNumber, out int wireType))
                {
                    switch (fieldNumber)
                    {
                        case XTag when wireType == WProtoWireType.Varint:
                        {
                            if (!reader.TryReadZigZag32(out x))
                            {
                                value = default;
                                return false;
                            }

                            break;
                        }
                        case YTag when wireType == WProtoWireType.Varint:
                        {
                            if (!reader.TryReadZigZag32(out y))
                            {
                                value = default;
                                return false;
                            }

                            break;
                        }
                        case ZTag when wireType == WProtoWireType.Varint:
                        {
                            if (!reader.TryReadZigZag32(out z))
                            {
                                value = default;
                                return false;
                            }

                            break;
                        }
                        case LegacyXTag when wireType == WProtoWireType.Varint:
                        {
                            if (!reader.TryReadInt32(out x))
                            {
                                value = default;
                                return false;
                            }

                            break;
                        }
                        case LegacyYTag when wireType == WProtoWireType.Varint:
                        {
                            if (!reader.TryReadInt32(out y))
                            {
                                value = default;
                                return false;
                            }

                            break;
                        }
                        case LegacyZTag when wireType == WProtoWireType.Varint:
                        {
                            if (!reader.TryReadInt32(out z))
                            {
                                value = default;
                                return false;
                            }

                            break;
                        }
                        default:
                        {
                            if (!reader.TrySkipField(fieldNumber, wireType))
                            {
                                value = default;
                                return false;
                            }

                            break;
                        }
                    }
                }

                if (reader.Malformed)
                {
                    value = default;
                    return false;
                }

                value = new FastVector3Int(x, y, z);
                return true;
            }
        }
    }
}
