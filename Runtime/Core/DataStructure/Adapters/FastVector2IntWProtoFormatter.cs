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
        /// <para>
        /// Hand-written now, generated later. It lives in a separate file declaring the same
        /// <c>partial</c> type because that is exactly what the source generator will emit -- a
        /// nested type is how generated code reaches a private member such as the cached hash
        /// without reflection, which is what makes the whole approach AOT-safe.
        /// </para>
        /// <para>
        /// Components are written as <c>sint32</c> on fields 5 and 6, and the <c>int32</c> fields 1
        /// and 2 are still read. That is additive rather than a renumbering for one reason: a varint
        /// written as <c>int32</c> and read as <c>sint32</c> is a different NUMBER, not a failure, so
        /// reusing the old field numbers would have turned every saved grid into silently halved
        /// coordinates. Fields 5 and 6 are one-byte keys just as 1 and 2 are, so backwards
        /// compatibility here costs nothing on the wire.
        /// </para>
        /// </remarks>
        public sealed class WProtoFormatter : IWProtoFormatter<FastVector2Int>
        {
            /// <summary>The field carrying <c>x</c> as a ZigZag varint.</summary>
            internal const int XTag = 5;

            /// <summary>The field carrying <c>y</c> as a ZigZag varint.</summary>
            internal const int YTag = 6;

            /// <summary>The field <c>x</c> occupied while it was an <c>int32</c>; read, never written.</summary>
            internal const int LegacyXTag = 1;

            /// <summary>The field <c>y</c> occupied while it was an <c>int32</c>; read, never written.</summary>
            internal const int LegacyYTag = 2;

            /// <summary>The shared instance; the formatter holds no state.</summary>
            public static readonly WProtoFormatter Instance = new();

            /// <inheritdoc />
            public int Measure(in FastVector2Int value)
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

                return size;
            }

            /// <inheritdoc />
            public bool Write(ref WProtoWriter writer, in FastVector2Int value)
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

                return true;
            }

            /// <inheritdoc />
            /// <remarks>
            /// The value is rebuilt through the public constructor, which recomputes the cached hash
            /// from x and y. A payload written before the hash left the wire still carries it as
            /// field 3; that field is skipped as unknown, so old data reads back correctly and a
            /// tampered one cannot hand back an object whose <c>GetHashCode</c> disagrees with its
            /// <c>Equals</c> -- which would corrupt every dictionary it was used in. Fields 1 and 2
            /// are read as the <c>int32</c>s they were, for the same reason.
            /// </remarks>
            public bool TryRead(ref WProtoReader reader, out FastVector2Int value)
            {
                int x = 0;
                int y = 0;

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
