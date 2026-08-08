// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Text;

    /// <summary>
    /// Writes protobuf wire-format primitives into a caller-owned <see cref="Span{T}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every write reports success rather than throwing, and a refused write leaves the buffer and
    /// <see cref="Position"/> untouched while latching <see cref="Faulted"/>. The latch means a
    /// formatter can emit a whole message without branching on each call and check once at the end;
    /// once latched, later writes are refused so a truncated message can never look complete.
    /// </para>
    /// <para>
    /// <b>Every</b> refusal latches, including a rejected field number or wire type -- not only
    /// running out of room. A skipped tag is the more dangerous of the two: it costs zero bytes, so
    /// <see cref="WProtoSizes"/> and the writer still agree, an enclosing length prefix is still
    /// correct, and the payload decodes cleanly as a different message. Nothing but the latch
    /// reports it.
    /// </para>
    /// <para>
    /// Sizing comes from <see cref="WProtoSizes"/>. Measuring a sub-message before writing it is
    /// what lets a length prefix be emitted up front with no scratch buffer and no back-patching.
    /// </para>
    /// </remarks>
    public ref struct WProtoWriter
    {
        private readonly Span<byte> _buffer;
        private int _position;
        private bool _faulted;

        /// <summary>
        /// Initializes a writer over <paramref name="buffer"/>.
        /// </summary>
        /// <param name="buffer">Destination storage; an empty span is valid and overflows on first write.</param>
        public WProtoWriter(Span<byte> buffer)
        {
            _buffer = buffer;
            _position = 0;
            _faulted = false;
        }

        /// <summary>Bytes written so far.</summary>
        public int Position => _position;

        /// <summary>Bytes still available in the destination span.</summary>
        public int Remaining => _buffer.Length - _position;

        /// <summary>
        /// Indicates whether any write has been refused for lack of space. Once set, it stays set.
        /// </summary>
        public bool Faulted => _faulted;

        /// <summary>The bytes written so far.</summary>
        public ReadOnlySpan<byte> Written => _buffer.Slice(0, _position);

        /// <summary>
        /// Writes the field key for <paramref name="fieldNumber"/> and <paramref name="wireType"/>.
        /// </summary>
        /// <param name="fieldNumber">The field number, 1 through 536,870,911.</param>
        /// <param name="wireType">One of the <see cref="WProtoWireType"/> constants.</param>
        /// <returns><c>true</c> when the key was written.</returns>
        public bool TryWriteTag(int fieldNumber, int wireType)
        {
            if (
                fieldNumber <= 0
                || fieldNumber > WProtoWireType.MaxFieldNumber
                || !WProtoWireType.IsDefined(wireType)
            )
            {
                _faulted = true;
                return false;
            }

            return TryWriteVarint32(((uint)fieldNumber << 3) | (uint)wireType);
        }

        /// <summary>
        /// Writes <paramref name="value"/> as an unsigned varint.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <returns><c>true</c> when the value was written.</returns>
        public bool TryWriteVarint32(uint value)
        {
            return TryWriteVarint64(value);
        }

        /// <summary>
        /// Writes <paramref name="value"/> as an unsigned varint.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <returns><c>true</c> when the value was written.</returns>
        public bool TryWriteVarint64(ulong value)
        {
            int size = WProtoSizes.Varint64Size(value);
            if (!TryReserve(size, out int start))
            {
                return false;
            }

            int index = start;
            while (value >= 0x80UL)
            {
                _buffer[index++] = (byte)(value | 0x80UL);
                value >>= 7;
            }

            _buffer[index] = (byte)value;
            return true;
        }

        /// <summary>
        /// Writes <paramref name="value"/> as a protobuf <c>int32</c>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <returns><c>true</c> when the value was written.</returns>
        /// <remarks>
        /// Negative values are sign-extended to 64 bits first, which is what makes them ten bytes
        /// wide and what makes them interchangeable with an <c>int64</c> field of the same number.
        /// </remarks>
        public bool TryWriteInt32(int value)
        {
            return TryWriteVarint64((ulong)(long)value);
        }

        /// <summary>
        /// Writes <paramref name="value"/> as a protobuf <c>int64</c>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <returns><c>true</c> when the value was written.</returns>
        public bool TryWriteInt64(long value)
        {
            return TryWriteVarint64((ulong)value);
        }

        /// <summary>
        /// Writes <paramref name="value"/> as a protobuf <c>sint32</c> (ZigZag varint).
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <returns><c>true</c> when the value was written.</returns>
        public bool TryWriteZigZag32(int value)
        {
            return TryWriteVarint32(WProtoZigZag.Encode32(value));
        }

        /// <summary>
        /// Writes <paramref name="value"/> as a protobuf <c>sint64</c> (ZigZag varint).
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <returns><c>true</c> when the value was written.</returns>
        public bool TryWriteZigZag64(long value)
        {
            return TryWriteVarint64(WProtoZigZag.Encode64(value));
        }

        /// <summary>
        /// Writes <paramref name="value"/> as a boolean varint.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <returns><c>true</c> when the value was written.</returns>
        public bool TryWriteBool(bool value)
        {
            return TryWriteVarint32(value ? 1u : 0u);
        }

        /// <summary>
        /// Writes <paramref name="value"/> as four little-endian bytes.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <returns><c>true</c> when the value was written.</returns>
        public bool TryWriteFixed32(uint value)
        {
            if (!TryReserve(sizeof(uint), out int start))
            {
                return false;
            }

            _buffer[start] = (byte)value;
            _buffer[start + 1] = (byte)(value >> 8);
            _buffer[start + 2] = (byte)(value >> 16);
            _buffer[start + 3] = (byte)(value >> 24);
            return true;
        }

        /// <summary>
        /// Writes <paramref name="value"/> as eight little-endian bytes.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <returns><c>true</c> when the value was written.</returns>
        public bool TryWriteFixed64(ulong value)
        {
            if (!TryReserve(sizeof(ulong), out int start))
            {
                return false;
            }

            for (int offset = 0; offset < sizeof(ulong); offset++)
            {
                _buffer[start + offset] = (byte)(value >> (offset * 8));
            }

            return true;
        }

        /// <summary>
        /// Writes <paramref name="value"/> as a protobuf <c>float</c> (fixed32).
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <returns><c>true</c> when the value was written.</returns>
        public bool TryWriteSingle(float value)
        {
            return TryWriteFixed32(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        }

        /// <summary>
        /// Writes <paramref name="value"/> as a protobuf <c>double</c> (fixed64).
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <returns><c>true</c> when the value was written.</returns>
        public bool TryWriteDouble(double value)
        {
            return TryWriteFixed64((ulong)BitConverter.DoubleToInt64Bits(value));
        }

        /// <summary>
        /// Writes a length-delimited header for a payload of <paramref name="payloadLength"/> bytes.
        /// </summary>
        /// <param name="payloadLength">The payload length; a negative length is refused.</param>
        /// <returns><c>true</c> when the header was written.</returns>
        public bool TryWriteLengthPrefix(int payloadLength)
        {
            if (payloadLength < 0)
            {
                _faulted = true;
                return false;
            }

            return TryWriteVarint32((uint)payloadLength);
        }

        /// <summary>
        /// Writes <paramref name="value"/> as a length-delimited byte string.
        /// </summary>
        /// <param name="value">The payload; an empty span writes a zero-length field.</param>
        /// <returns><c>true</c> when the whole field was written.</returns>
        /// <remarks>
        /// The prefix and the payload are reserved together, so a payload that does not fit leaves
        /// no orphaned length prefix behind.
        /// </remarks>
        public bool TryWriteBytes(ReadOnlySpan<byte> value)
        {
            int prefixSize = WProtoSizes.Varint32Size((uint)value.Length);

            // Subtract rather than add: `prefixSize + value.Length` overflows int for a span near
            // int.MaxValue, and a wrapped comparison would let the prefix through and then refuse
            // the payload -- the orphaned prefix this method exists to prevent.
            if (_faulted || value.Length > Remaining - prefixSize)
            {
                _faulted = true;
                return false;
            }

            if (!TryWriteVarint32((uint)value.Length))
            {
                return false;
            }

            return TryWriteRaw(value);
        }

        /// <summary>
        /// Writes <paramref name="value"/> as a length-delimited UTF-8 string.
        /// </summary>
        /// <param name="value">The string; <c>null</c> writes a zero-length field.</param>
        /// <returns><c>true</c> when the whole field was written.</returns>
        public bool TryWriteString(string value)
        {
            int byteCount = WProtoSizes.Utf8ByteCount(value);
            int prefixSize = WProtoSizes.Varint32Size((uint)byteCount);
            if (_faulted || byteCount > Remaining - prefixSize)
            {
                _faulted = true;
                return false;
            }

            if (!TryWriteVarint32((uint)byteCount))
            {
                return false;
            }

            if (byteCount == 0)
            {
                return true;
            }

            if (!TryReserve(byteCount, out int start))
            {
                return false;
            }

            int written;
            try
            {
                written = Encoding.UTF8.GetBytes(value.AsSpan(), _buffer.Slice(start, byteCount));
            }
            catch (ArgumentException)
            {
                // GetBytes wanting more room than GetByteCount promised is the one way this class
                // can throw. It is not reachable for any string tested, but a Try API that throws
                // is worse than one that reports failure, and the prefix is already committed.
                _faulted = true;
                return false;
            }

            if (written != byteCount)
            {
                _faulted = true;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Copies <paramref name="value"/> verbatim, with no tag and no length prefix.
        /// </summary>
        /// <param name="value">The bytes to copy.</param>
        /// <returns><c>true</c> when the bytes were copied.</returns>
        public bool TryWriteRaw(ReadOnlySpan<byte> value)
        {
            if (value.Length == 0)
            {
                return !_faulted;
            }

            if (!TryReserve(value.Length, out int start))
            {
                return false;
            }

            value.CopyTo(_buffer.Slice(start, value.Length));
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryReserve(int count, out int start)
        {
            start = _position;
            if (_faulted || count > _buffer.Length - _position)
            {
                _faulted = true;
                return false;
            }

            _position += count;
            return true;
        }
    }
}
