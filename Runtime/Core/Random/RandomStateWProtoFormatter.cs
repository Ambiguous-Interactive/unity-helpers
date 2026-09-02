// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using Serialization.WallstopProto;

    public readonly partial struct RandomState
    {
        /// <summary>
        /// Reads and writes <see cref="RandomState"/> in protobuf wire format, byte-for-byte as
        /// protobuf-net writes it.
        /// </summary>
        /// <remarks>
        /// This is the contract that matters most for saved games: every seedable generator in the
        /// package persists through it, so a byte-level divergence here rewrites a player's world.
        /// It also exercises the widest set of encodings the package uses -- unsigned 64-bit
        /// varints, a boolean, a fixed64 double, a length-delimited byte array, unsigned 32-bit
        /// varints, and signed counters that go negative -- which is why it is the first non-trivial
        /// type ported.
        /// </remarks>
        public sealed class WProtoFormatter : IWProtoFormatter<RandomState>
        {
            private const int BoolSize = 1;
            private const int Fixed64Size = 8;

            /// <summary>The shared instance; the formatter holds no state.</summary>
            public static readonly WProtoFormatter Instance = new();

            /// <inheritdoc />
            public int Measure(in RandomState value)
            {
                int size = 0;
                if (value._state1 != 0UL)
                {
                    size += WProtoSizes.TagSize(1) + WProtoSizes.Varint64Size(value._state1);
                }

                if (value._state2 != 0UL)
                {
                    size += WProtoSizes.TagSize(2) + WProtoSizes.Varint64Size(value._state2);
                }

                if (value._hasGaussian)
                {
                    size += WProtoSizes.TagSize(3) + BoolSize;
                }

                /*
                    Omission tests `== 0`, and -0.0 == 0.0, so a negative zero is dropped and reads
                    back positive. That is protobuf-net's behavior and wire compatibility outranks
                    fidelity here.
                */
                if (value._gaussian != 0d)
                {
                    size += WProtoSizes.TagSize(4) + Fixed64Size;
                }

                /*
                    Null is absent; empty is present with a zero length. Measured against the oracle,
                    not assumed -- the intuition that both are omitted is wrong.
                */
                if (value._payload != null)
                {
                    size +=
                        WProtoSizes.TagSize(5)
                        + WProtoSizes.LengthDelimitedSize(value._payload.Length);
                }

                if (value._bitBuffer != 0u)
                {
                    size += WProtoSizes.TagSize(6) + WProtoSizes.Varint32Size(value._bitBuffer);
                }

                if (value._bitCount != 0)
                {
                    size += WProtoSizes.TagSize(7) + WProtoSizes.Int32Size(value._bitCount);
                }

                if (value._byteBuffer != 0u)
                {
                    size += WProtoSizes.TagSize(8) + WProtoSizes.Varint32Size(value._byteBuffer);
                }

                if (value._byteCount != 0)
                {
                    size += WProtoSizes.TagSize(9) + WProtoSizes.Int32Size(value._byteCount);
                }

                if (value._hashCode != 0)
                {
                    size += WProtoSizes.TagSize(10) + WProtoSizes.Int32Size(value._hashCode);
                }

                return size;
            }

            /// <inheritdoc />
            public bool Write(ref WProtoWriter writer, in RandomState value)
            {
                if (value._state1 != 0UL)
                {
                    if (
                        !writer.TryWriteTag(1, WProtoWireType.Varint)
                        || !writer.TryWriteVarint64(value._state1)
                    )
                    {
                        return false;
                    }
                }

                if (value._state2 != 0UL)
                {
                    if (
                        !writer.TryWriteTag(2, WProtoWireType.Varint)
                        || !writer.TryWriteVarint64(value._state2)
                    )
                    {
                        return false;
                    }
                }

                if (value._hasGaussian)
                {
                    if (!writer.TryWriteTag(3, WProtoWireType.Varint) || !writer.TryWriteBool(true))
                    {
                        return false;
                    }
                }

                if (value._gaussian != 0d)
                {
                    if (
                        !writer.TryWriteTag(4, WProtoWireType.Fixed64)
                        || !writer.TryWriteDouble(value._gaussian)
                    )
                    {
                        return false;
                    }
                }

                if (value._payload != null)
                {
                    if (
                        !writer.TryWriteTag(5, WProtoWireType.LengthDelimited)
                        || !writer.TryWriteBytes(value._payload)
                    )
                    {
                        return false;
                    }
                }

                if (value._bitBuffer != 0u)
                {
                    if (
                        !writer.TryWriteTag(6, WProtoWireType.Varint)
                        || !writer.TryWriteVarint32(value._bitBuffer)
                    )
                    {
                        return false;
                    }
                }

                if (value._bitCount != 0)
                {
                    if (
                        !writer.TryWriteTag(7, WProtoWireType.Varint)
                        || !writer.TryWriteInt32(value._bitCount)
                    )
                    {
                        return false;
                    }
                }

                if (value._byteBuffer != 0u)
                {
                    if (
                        !writer.TryWriteTag(8, WProtoWireType.Varint)
                        || !writer.TryWriteVarint32(value._byteBuffer)
                    )
                    {
                        return false;
                    }
                }

                if (value._byteCount != 0)
                {
                    if (
                        !writer.TryWriteTag(9, WProtoWireType.Varint)
                        || !writer.TryWriteInt32(value._byteCount)
                    )
                    {
                        return false;
                    }
                }

                if (value._hashCode != 0)
                {
                    if (
                        !writer.TryWriteTag(10, WProtoWireType.Varint)
                        || !writer.TryWriteInt32(value._hashCode)
                    )
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <inheritdoc />
            /// <remarks>
            /// The composite hash on the wire is read and discarded. Every constructor derives it
            /// from the other members, so recomputing reproduces exactly what protobuf-net wrote
            /// for any real value, and refuses to hand back a state whose hash disagrees with its
            /// contents when the payload has been tampered with.
            /// </remarks>
            public bool TryRead(ref WProtoReader reader, out RandomState value)
            {
                ulong state1 = 0UL;
                ulong state2 = 0UL;
                bool hasGaussian = false;
                double gaussian = 0d;
                byte[] payload = null;
                uint bitBuffer = 0u;
                int bitCount = 0;
                uint byteBuffer = 0u;
                int byteCount = 0;

                while (reader.TryReadTag(out int fieldNumber, out int wireType))
                {
                    bool read;
                    switch (fieldNumber)
                    {
                        case 1 when wireType == WProtoWireType.Varint:
                        {
                            read = reader.TryReadVarint64(out state1);
                            break;
                        }
                        case 2 when wireType == WProtoWireType.Varint:
                        {
                            read = reader.TryReadVarint64(out state2);
                            break;
                        }
                        case 3 when wireType == WProtoWireType.Varint:
                        {
                            read = reader.TryReadBool(out hasGaussian);
                            break;
                        }
                        case 4 when wireType == WProtoWireType.Fixed64:
                        {
                            read = reader.TryReadDouble(out gaussian);
                            break;
                        }
                        case 5 when wireType == WProtoWireType.LengthDelimited:
                        {
                            read = reader.TryReadBytes(out ReadOnlySpan<byte> bytes);
                            if (read)
                            {
                                payload = bytes.ToArray();
                            }

                            break;
                        }
                        case 6 when wireType == WProtoWireType.Varint:
                        {
                            read = reader.TryReadVarint32(out bitBuffer);
                            break;
                        }
                        case 7 when wireType == WProtoWireType.Varint:
                        {
                            read = reader.TryReadInt32(out bitCount);
                            break;
                        }
                        case 8 when wireType == WProtoWireType.Varint:
                        {
                            read = reader.TryReadVarint32(out byteBuffer);
                            break;
                        }
                        case 9 when wireType == WProtoWireType.Varint:
                        {
                            read = reader.TryReadInt32(out byteCount);
                            break;
                        }
                        case 10 when wireType == WProtoWireType.Varint:
                        {
                            read = reader.TryReadInt32(out _);
                            break;
                        }
                        default:
                        {
                            read = reader.TrySkipField(fieldNumber, wireType);
                            break;
                        }
                    }

                    if (!read)
                    {
                        value = default;
                        return false;
                    }
                }

                if (reader.Malformed)
                {
                    value = default;
                    return false;
                }

                value = new RandomState(
                    state1,
                    state2,
                    hasGaussian ? gaussian : (double?)null,
                    payload,
                    bitBuffer,
                    bitCount,
                    byteBuffer,
                    byteCount
                );
                return true;
            }
        }
    }
}
