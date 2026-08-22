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
        /// This type is the reason the emit order is worth stating out loud: its members are
        /// declared x, y, <b>z</b>, hash but tagged 1, 2, <b>4</b>, 3. protobuf-net emits in
        /// ascending field number, so the cached hash goes out <i>before</i> z. Emitting in
        /// declaration order would produce a payload that still parses and still round-trips, and
        /// differs from every byte protobuf-net has ever written for this type.
        /// </remarks>
        public sealed class WProtoFormatter : IWProtoFormatter<FastVector3Int>
        {
            /// <summary>The shared instance; the formatter holds no state.</summary>
            public static readonly WProtoFormatter Instance = new();

            /// <inheritdoc />
            public int Measure(in FastVector3Int value)
            {
                int size = 0;
                if (value.x != 0)
                {
                    size += WProtoSizes.TagSize(1) + WProtoSizes.Int32Size(value.x);
                }

                if (value.y != 0)
                {
                    size += WProtoSizes.TagSize(2) + WProtoSizes.Int32Size(value.y);
                }

                if (value.z != 0)
                {
                    size += WProtoSizes.TagSize(4) + WProtoSizes.Int32Size(value.z);
                }

                return size;
            }

            /// <inheritdoc />
            public bool Write(ref WProtoWriter writer, in FastVector3Int value)
            {
                if (value.x != 0)
                {
                    if (
                        !writer.TryWriteTag(1, WProtoWireType.Varint)
                        || !writer.TryWriteInt32(value.x)
                    )
                    {
                        return false;
                    }
                }

                if (value.y != 0)
                {
                    if (
                        !writer.TryWriteTag(2, WProtoWireType.Varint)
                        || !writer.TryWriteInt32(value.y)
                    )
                    {
                        return false;
                    }
                }

                if (value.z != 0)
                {
                    if (
                        !writer.TryWriteTag(4, WProtoWireType.Varint)
                        || !writer.TryWriteInt32(value.z)
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
            /// is skipped as unknown, which is why z keeps tag 4 rather than moving onto the vacated
            /// 3. See <see cref="FastVector2Int.WProtoFormatter.TryRead"/> for why.
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
                        case 1 when wireType == WProtoWireType.Varint:
                        {
                            if (!reader.TryReadInt32(out x))
                            {
                                value = default;
                                return false;
                            }

                            break;
                        }
                        case 2 when wireType == WProtoWireType.Varint:
                        {
                            if (!reader.TryReadInt32(out y))
                            {
                                value = default;
                                return false;
                            }

                            break;
                        }
                        case 4 when wireType == WProtoWireType.Varint:
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
