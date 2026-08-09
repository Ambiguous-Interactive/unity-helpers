// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure.Adapters
{
    using Serialization.WallstopProto;

    public partial struct WGuid
    {
        /// <summary>
        /// Reads and writes <see cref="WGuid"/> in protobuf wire format, byte-for-byte as
        /// protobuf-net writes it.
        /// </summary>
        /// <remarks>
        /// The two halves go on the wire as signed <c>int64</c> varints, which is why a GUID whose
        /// high bit is set costs the full ten bytes per half. This is the shape already written by
        /// every shipped save, so it is reproduced rather than improved on.
        /// </remarks>
        public sealed class WProtoFormatter : IWProtoFormatter<WGuid>
        {
            /// <summary>The shared instance; the formatter holds no state.</summary>
            public static readonly WProtoFormatter Instance = new();

            /// <inheritdoc />
            public int Measure(in WGuid value)
            {
                int size = 0;
                if (value._low != 0L)
                {
                    size += WProtoSizes.TagSize(1) + WProtoSizes.Int64Size(value._low);
                }

                if (value._high != 0L)
                {
                    size += WProtoSizes.TagSize(2) + WProtoSizes.Int64Size(value._high);
                }

                return size;
            }

            /// <inheritdoc />
            public bool Write(ref WProtoWriter writer, in WGuid value)
            {
                if (value._low != 0L)
                {
                    if (
                        !writer.TryWriteTag(1, WProtoWireType.Varint)
                        || !writer.TryWriteInt64(value._low)
                    )
                    {
                        return false;
                    }
                }

                if (value._high != 0L)
                {
                    if (
                        !writer.TryWriteTag(2, WProtoWireType.Varint)
                        || !writer.TryWriteInt64(value._high)
                    )
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <inheritdoc />
            /// <remarks>
            /// The halves are assigned directly rather than routed through
            /// <see cref="WGuid(System.Guid)"/>, which rejects anything that is not a version-4
            /// layout. protobuf-net writes the fields directly and reads them back the same way, so
            /// validating here would reject payloads that already exist on disk.
            /// </remarks>
            public bool TryRead(ref WProtoReader reader, out WGuid value)
            {
                WGuid result = default;

                while (reader.TryReadTag(out int fieldNumber, out int wireType))
                {
                    switch (fieldNumber)
                    {
                        case 1 when wireType == WProtoWireType.Varint:
                        {
                            if (!reader.TryReadInt64(out result._low))
                            {
                                value = default;
                                return false;
                            }

                            break;
                        }
                        case 2 when wireType == WProtoWireType.Varint:
                        {
                            if (!reader.TryReadInt64(out result._high))
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

                value = result;
                return true;
            }
        }
    }
}
