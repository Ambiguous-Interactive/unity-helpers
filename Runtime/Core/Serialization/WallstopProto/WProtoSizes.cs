// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System.Runtime.CompilerServices;
    using System.Text;

    /// <summary>
    /// Encoded byte counts for every wire primitive, so a writer can size a nested message
    /// before emitting its length prefix.
    /// </summary>
    /// <remarks>
    /// Protobuf length-delimits sub-messages, so the length has to be known before the payload
    /// is written. Measuring first is the allocation-free alternative to writing into scratch
    /// storage and copying, or to reserving a maximum-width prefix and shifting the payload back.
    /// </remarks>
    public static class WProtoSizes
    {
        /// <summary>Bytes a ten-byte varint occupies; the widest encoding the format allows.</summary>
        public const int MaxVarintBytes = 10;

        /// <summary>
        /// Returns the encoded size of <paramref name="value"/> as an unsigned varint.
        /// </summary>
        /// <param name="value">The value to measure.</param>
        /// <returns>A byte count between 1 and 10.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Varint64Size(ulong value)
        {
            int size = 1;
            while (value >= 0x80UL)
            {
                value >>= 7;
                size++;
            }

            return size;
        }

        /// <summary>
        /// Returns the encoded size of <paramref name="value"/> as an unsigned varint.
        /// </summary>
        /// <param name="value">The value to measure.</param>
        /// <returns>A byte count between 1 and 5.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Varint32Size(uint value)
        {
            return Varint64Size(value);
        }

        /// <summary>
        /// Returns the encoded size of a protobuf <c>int32</c>.
        /// </summary>
        /// <param name="value">The value to measure.</param>
        /// <returns>1 to 5 bytes for a non-negative value, always 10 for a negative one.</returns>
        /// <remarks>
        /// A negative <c>int32</c> is sign-extended to 64 bits before encoding, which is why it
        /// always costs the full ten bytes. <c>sint32</c> (see <see cref="ZigZag32Size"/>) is the
        /// encoding to use when negatives are common.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Int32Size(int value)
        {
            return value < 0 ? MaxVarintBytes : Varint32Size((uint)value);
        }

        /// <summary>
        /// Returns the encoded size of a protobuf <c>int64</c>.
        /// </summary>
        /// <param name="value">The value to measure.</param>
        /// <returns>A byte count between 1 and 10.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Int64Size(long value)
        {
            return Varint64Size((ulong)value);
        }

        /// <summary>
        /// Returns the encoded size of a protobuf <c>sint32</c> (ZigZag).
        /// </summary>
        /// <param name="value">The value to measure.</param>
        /// <returns>A byte count between 1 and 5.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ZigZag32Size(int value)
        {
            return Varint32Size(WProtoZigZag.Encode32(value));
        }

        /// <summary>
        /// Returns the encoded size of a protobuf <c>sint64</c> (ZigZag).
        /// </summary>
        /// <param name="value">The value to measure.</param>
        /// <returns>A byte count between 1 and 10.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ZigZag64Size(long value)
        {
            return Varint64Size(WProtoZigZag.Encode64(value));
        }

        /// <summary>
        /// Returns the encoded size of the field key for <paramref name="fieldNumber"/>.
        /// </summary>
        /// <param name="fieldNumber">The field number, 1 through 536,870,911.</param>
        /// <returns>A byte count between 1 and 5, or 0 when the field number is out of range.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TagSize(int fieldNumber)
        {
            if (fieldNumber <= 0 || fieldNumber > WProtoWireType.MaxFieldNumber)
            {
                return 0;
            }

            return Varint32Size((uint)fieldNumber << 3);
        }

        /// <summary>
        /// Returns the encoded size of a length-delimited payload, prefix included.
        /// </summary>
        /// <param name="payloadLength">The payload length in bytes.</param>
        /// <returns>
        /// The prefix size plus the payload size, or 0 for a negative length -- which
        /// <see cref="WProtoWriter.TryWriteLengthPrefix"/> refuses outright. The two have to agree
        /// on the byte count for every input, invalid ones included, or a measured-then-written
        /// message carries a length prefix that does not describe its payload.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LengthDelimitedSize(int payloadLength)
        {
            if (payloadLength < 0)
            {
                return 0;
            }

            return Varint32Size((uint)payloadLength) + payloadLength;
        }

        /// <summary>
        /// Returns the encoded size of <paramref name="value"/> as a length-delimited UTF-8 string.
        /// </summary>
        /// <param name="value">The string to measure; <c>null</c> counts as empty.</param>
        /// <returns>The prefix size plus the UTF-8 byte count.</returns>
        public static int StringSize(string value)
        {
            return LengthDelimitedSize(Utf8ByteCount(value));
        }

        /// <summary>
        /// Returns the UTF-8 byte count of <paramref name="value"/> without allocating.
        /// </summary>
        /// <param name="value">The string to measure; <c>null</c> counts as empty.</param>
        /// <returns>The UTF-8 byte count, or 0 when the string is null or empty.</returns>
        public static int Utf8ByteCount(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }

            return Encoding.UTF8.GetByteCount(value);
        }
    }
}
