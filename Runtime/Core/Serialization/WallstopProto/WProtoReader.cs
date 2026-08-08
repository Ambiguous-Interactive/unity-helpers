// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Text;

    /// <summary>
    /// Reads protobuf wire-format primitives out of a <see cref="ReadOnlySpan{T}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every read reports success rather than throwing, and a read that cannot be satisfied leaves
    /// <see cref="Position"/> untouched while latching <see cref="Malformed"/>. Once latched every
    /// later read is refused, so a caller that checks only at the end cannot mistake garbage
    /// recovered mid-message for data.
    /// </para>
    /// <para>
    /// Payloads arrive from disk and from the network, so the reader treats every length, tag and
    /// varint as hostile: overlong varints, zero field numbers, undefined wire types, negative or
    /// out-of-range lengths and unbalanced groups are all rejected rather than clamped.
    /// </para>
    /// </remarks>
    public ref struct WProtoReader
    {
        private const int MaxGroupDepth = 64;

        private readonly ReadOnlySpan<byte> _buffer;
        private int _position;
        private bool _malformed;

        /// <summary>
        /// Initializes a reader over <paramref name="buffer"/>.
        /// </summary>
        /// <param name="buffer">The encoded bytes; an empty span reads as an empty message.</param>
        public WProtoReader(ReadOnlySpan<byte> buffer)
        {
            _buffer = buffer;
            _position = 0;
            _malformed = false;
        }

        /// <summary>Bytes consumed so far.</summary>
        public int Position => _position;

        /// <summary>Bytes not yet consumed.</summary>
        public int Remaining => _buffer.Length - _position;

        /// <summary>Indicates whether every byte has been consumed.</summary>
        public bool End => _position >= _buffer.Length;

        /// <summary>
        /// Indicates whether any read has failed. Once set, it stays set and refuses later reads.
        /// </summary>
        public bool Malformed => _malformed;

        /// <summary>
        /// Reads the next field key.
        /// </summary>
        /// <param name="fieldNumber">Receives the field number, or 0 on failure.</param>
        /// <param name="wireType">Receives the wire type, or -1 on failure.</param>
        /// <returns><c>true</c> when a valid key was read; <c>false</c> at end of input or on a malformed key.</returns>
        /// <remarks>
        /// A clean end of input returns <c>false</c> without latching <see cref="Malformed"/>, so
        /// <c>while (reader.TryReadTag(...))</c> terminates normally on a well-formed message.
        /// </remarks>
        public bool TryReadTag(out int fieldNumber, out int wireType)
        {
            fieldNumber = 0;
            wireType = -1;
            if (_malformed || End)
            {
                return false;
            }

            if (!TryReadVarint32(out uint key))
            {
                return false;
            }

            int number = (int)(key >> 3);
            int type = (int)(key & 0x7u);
            if (number <= 0 || !WProtoWireType.IsDefined(type))
            {
                _malformed = true;
                return false;
            }

            fieldNumber = number;
            wireType = type;
            return true;
        }

        /// <summary>
        /// Reads an unsigned varint, truncated to 32 bits.
        /// </summary>
        /// <param name="value">Receives the value, or 0 on failure.</param>
        /// <returns><c>true</c> when a value was read.</returns>
        /// <remarks>
        /// A negative <c>int32</c> is written sign-extended across ten bytes, so a 32-bit field can
        /// legitimately carry a ten-byte varint. The upper bits are discarded rather than rejected.
        /// </remarks>
        public bool TryReadVarint32(out uint value)
        {
            if (!TryReadVarint64(out ulong wide))
            {
                value = 0;
                return false;
            }

            value = (uint)wide;
            return true;
        }

        /// <summary>
        /// Reads an unsigned varint.
        /// </summary>
        /// <param name="value">Receives the value, or 0 on failure.</param>
        /// <returns><c>true</c> when a value was read.</returns>
        public bool TryReadVarint64(out ulong value)
        {
            value = 0;
            if (_malformed)
            {
                return false;
            }

            ulong result = 0;
            int index = _position;
            for (int shift = 0; shift < WProtoSizes.MaxVarintBytes; shift++)
            {
                if (index >= _buffer.Length)
                {
                    _malformed = true;
                    return false;
                }

                byte current = _buffer[index++];

                // The tenth byte carries only the single highest bit of a 64-bit value; anything
                // else set there is an overlong encoding, not a large number.
                if (shift == WProtoSizes.MaxVarintBytes - 1 && current > 0x01)
                {
                    _malformed = true;
                    return false;
                }

                result |= (ulong)(current & 0x7F) << (shift * 7);
                if ((current & 0x80) == 0)
                {
                    _position = index;
                    value = result;
                    return true;
                }
            }

            _malformed = true;
            return false;
        }

        /// <summary>
        /// Reads a protobuf <c>int32</c>.
        /// </summary>
        /// <param name="value">Receives the value, or 0 on failure.</param>
        /// <returns><c>true</c> when a value was read.</returns>
        public bool TryReadInt32(out int value)
        {
            bool read = TryReadVarint32(out uint raw);
            value = unchecked((int)raw);
            return read;
        }

        /// <summary>
        /// Reads a protobuf <c>int64</c>.
        /// </summary>
        /// <param name="value">Receives the value, or 0 on failure.</param>
        /// <returns><c>true</c> when a value was read.</returns>
        public bool TryReadInt64(out long value)
        {
            bool read = TryReadVarint64(out ulong raw);
            value = unchecked((long)raw);
            return read;
        }

        /// <summary>
        /// Reads a protobuf <c>sint32</c> (ZigZag varint).
        /// </summary>
        /// <param name="value">Receives the value, or 0 on failure.</param>
        /// <returns><c>true</c> when a value was read.</returns>
        public bool TryReadZigZag32(out int value)
        {
            bool read = TryReadVarint32(out uint raw);
            value = read ? WProtoZigZag.Decode32(raw) : 0;
            return read;
        }

        /// <summary>
        /// Reads a protobuf <c>sint64</c> (ZigZag varint).
        /// </summary>
        /// <param name="value">Receives the value, or 0 on failure.</param>
        /// <returns><c>true</c> when a value was read.</returns>
        public bool TryReadZigZag64(out long value)
        {
            bool read = TryReadVarint64(out ulong raw);
            value = read ? WProtoZigZag.Decode64(raw) : 0;
            return read;
        }

        /// <summary>
        /// Reads a boolean varint.
        /// </summary>
        /// <param name="value">Receives the value, or <c>false</c> on failure.</param>
        /// <returns><c>true</c> when a value was read.</returns>
        /// <remarks>Any non-zero varint reads as <c>true</c>, matching every protobuf implementation.</remarks>
        public bool TryReadBool(out bool value)
        {
            bool read = TryReadVarint64(out ulong raw);
            value = read && raw != 0;
            return read;
        }

        /// <summary>
        /// Reads four little-endian bytes.
        /// </summary>
        /// <param name="value">Receives the value, or 0 on failure.</param>
        /// <returns><c>true</c> when a value was read.</returns>
        public bool TryReadFixed32(out uint value)
        {
            value = 0;
            if (!TryConsume(sizeof(uint), out int start))
            {
                return false;
            }

            value =
                _buffer[start]
                | ((uint)_buffer[start + 1] << 8)
                | ((uint)_buffer[start + 2] << 16)
                | ((uint)_buffer[start + 3] << 24);
            return true;
        }

        /// <summary>
        /// Reads eight little-endian bytes.
        /// </summary>
        /// <param name="value">Receives the value, or 0 on failure.</param>
        /// <returns><c>true</c> when a value was read.</returns>
        public bool TryReadFixed64(out ulong value)
        {
            value = 0;
            if (!TryConsume(sizeof(ulong), out int start))
            {
                return false;
            }

            ulong result = 0;
            for (int offset = 0; offset < sizeof(ulong); offset++)
            {
                result |= (ulong)_buffer[start + offset] << (offset * 8);
            }

            value = result;
            return true;
        }

        /// <summary>
        /// Reads a protobuf <c>float</c> (fixed32).
        /// </summary>
        /// <param name="value">Receives the value, or 0 on failure.</param>
        /// <returns><c>true</c> when a value was read.</returns>
        public bool TryReadSingle(out float value)
        {
            bool read = TryReadFixed32(out uint raw);
            value = read ? BitConverter.Int32BitsToSingle(unchecked((int)raw)) : 0f;
            return read;
        }

        /// <summary>
        /// Reads a protobuf <c>double</c> (fixed64).
        /// </summary>
        /// <param name="value">Receives the value, or 0 on failure.</param>
        /// <returns><c>true</c> when a value was read.</returns>
        public bool TryReadDouble(out double value)
        {
            bool read = TryReadFixed64(out ulong raw);
            value = read ? BitConverter.Int64BitsToDouble(unchecked((long)raw)) : 0d;
            return read;
        }

        /// <summary>
        /// Reads a length-delimited payload without copying it.
        /// </summary>
        /// <param name="value">Receives a view over the payload, or an empty span on failure.</param>
        /// <returns><c>true</c> when the payload was read.</returns>
        public bool TryReadBytes(out ReadOnlySpan<byte> value)
        {
            value = default;
            if (!TryReadLength(out int length))
            {
                return false;
            }

            if (!TryConsume(length, out int start))
            {
                return false;
            }

            value = _buffer.Slice(start, length);
            return true;
        }

        /// <summary>
        /// Reads a length-delimited UTF-8 string.
        /// </summary>
        /// <param name="value">Receives the decoded string, or <c>null</c> on failure.</param>
        /// <returns><c>true</c> when the string was read.</returns>
        /// <remarks>A zero-length field decodes to <see cref="string.Empty"/>, never to <c>null</c>.</remarks>
        public bool TryReadString(out string value)
        {
            value = null;
            if (!TryReadBytes(out ReadOnlySpan<byte> payload))
            {
                return false;
            }

            if (payload.Length == 0)
            {
                value = string.Empty;
                return true;
            }

            value = Encoding.UTF8.GetString(payload);
            return true;
        }

        /// <summary>
        /// Reads a length-delimited sub-message as its own reader.
        /// </summary>
        /// <param name="nested">Receives a reader scoped to the sub-message payload.</param>
        /// <returns><c>true</c> when the sub-message was read.</returns>
        /// <remarks>
        /// The returned reader cannot run past the sub-message, so a nested field that lies about
        /// its own length is contained rather than allowed to consume the parent's fields.
        /// </remarks>
        public bool TryReadMessage(out WProtoReader nested)
        {
            if (!TryReadBytes(out ReadOnlySpan<byte> payload))
            {
                nested = new WProtoReader(default);
                return false;
            }

            nested = new WProtoReader(payload);
            return true;
        }

        /// <summary>
        /// Reads a length prefix.
        /// </summary>
        /// <param name="length">Receives the length, or 0 on failure.</param>
        /// <returns><c>true</c> when a length that fits the remaining input was read.</returns>
        public bool TryReadLength(out int length)
        {
            length = 0;
            if (!TryReadVarint32(out uint raw))
            {
                return false;
            }

            // A length above int.MaxValue cannot address a span, and one above what is left is a
            // truncated or hostile payload. Both are malformed, not merely large.
            if (raw > int.MaxValue || raw > (uint)Remaining)
            {
                _malformed = true;
                return false;
            }

            length = (int)raw;
            return true;
        }

        /// <summary>
        /// Consumes the value of a field whose number the caller does not recognize.
        /// </summary>
        /// <param name="wireType">The wire type from the field's key.</param>
        /// <returns><c>true</c> when the value was skipped.</returns>
        /// <remarks>
        /// Forward compatibility depends on this: a payload written by a newer build carries fields
        /// this build has no member for, and they have to be stepped over exactly rather than
        /// guessed at. Groups are skipped by matching their terminator, bounded so a stream of
        /// openers cannot recurse without end.
        /// </remarks>
        public bool TrySkipField(int wireType)
        {
            return TrySkipField(wireType, 0);
        }

        private bool TrySkipField(int wireType, int depth)
        {
            if (_malformed)
            {
                return false;
            }

            switch (wireType)
            {
                case WProtoWireType.Varint:
                {
                    return TryReadVarint64(out _);
                }
                case WProtoWireType.Fixed64:
                {
                    return TryConsume(sizeof(ulong), out _);
                }
                case WProtoWireType.LengthDelimited:
                {
                    if (!TryReadLength(out int length))
                    {
                        return false;
                    }

                    return TryConsume(length, out _);
                }
                case WProtoWireType.Fixed32:
                {
                    return TryConsume(sizeof(uint), out _);
                }
                case WProtoWireType.StartGroup:
                {
                    return TrySkipGroup(depth);
                }
                default:
                {
                    // EndGroup without a matching StartGroup, or an undefined wire type.
                    _malformed = true;
                    return false;
                }
            }
        }

        private bool TrySkipGroup(int depth)
        {
            if (depth >= MaxGroupDepth)
            {
                _malformed = true;
                return false;
            }

            while (true)
            {
                if (!TryReadTag(out _, out int innerWireType))
                {
                    // End of input inside a group is an unterminated group, not a clean finish.
                    _malformed = true;
                    return false;
                }

                if (innerWireType == WProtoWireType.EndGroup)
                {
                    return true;
                }

                if (!TrySkipField(innerWireType, depth + 1))
                {
                    return false;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryConsume(int count, out int start)
        {
            start = _position;
            if (_malformed || count < 0 || count > _buffer.Length - _position)
            {
                _malformed = true;
                return false;
            }

            _position += count;
            return true;
        }
    }
}
