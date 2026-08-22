// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure.Adapters
{
    using Serialization.WallstopProto;

    public readonly partial struct FastVector2Int
    {
        /// <summary>
        /// Reads and writes <see cref="FastVector2Int"/> in protobuf wire format, byte-for-byte as
        /// protobuf-net writes it.
        /// </summary>
        /// <remarks>
        /// Hand-written now, generated later. It lives in a separate file declaring the same
        /// <c>partial</c> type because that is exactly what the source generator will emit -- a
        /// nested type is how generated code reaches a private member such as the cached hash
        /// without reflection, which is what makes the whole approach AOT-safe.
        /// </remarks>
        public sealed class WProtoFormatter : IWProtoFormatter<FastVector2Int>
        {
            /// <summary>The shared instance; the formatter holds no state.</summary>
            public static readonly WProtoFormatter Instance = new();

            /// <inheritdoc />
            public int Measure(in FastVector2Int value)
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

                return size;
            }

            /// <inheritdoc />
            public bool Write(ref WProtoWriter writer, in FastVector2Int value)
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

                return true;
            }

            /// <inheritdoc />
            /// <remarks>
            /// The value is rebuilt through the public constructor, which recomputes the cached hash
            /// from x and y. A payload written before the hash left the wire still carries it as
            /// field 3; that field is skipped as unknown, so old data reads back correctly and a
            /// tampered one cannot hand back an object whose <c>GetHashCode</c> disagrees with its
            /// <c>Equals</c> -- which would corrupt every dictionary it was used in.
            /// </remarks>
            public bool TryRead(ref WProtoReader reader, out FastVector2Int value)
            {
                int x = 0;
                int y = 0;

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

                value = new FastVector2Int(x, y);
                return true;
            }
        }
    }
}
