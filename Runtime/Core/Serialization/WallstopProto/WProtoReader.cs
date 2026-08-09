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
    /// Every read reports success rather than throwing, and a read that cannot be satisfied latches
    /// <see cref="Malformed"/>. Once latched every later read is refused, so a caller that checks
    /// only at the end cannot mistake garbage recovered mid-message for data.
    /// <see cref="Position"/> is <b>not</b> rewound on failure -- a rejected length or field key may
    /// already have consumed its varint -- so once <see cref="Malformed"/> is set, treat the
    /// position as meaningless rather than as a resume point.
    /// </para>
    /// <para>
    /// Payloads arrive from disk and from the network, so the reader treats every length, tag and
    /// varint as hostile: overlong varints, key and length values above the 32 bits those fields
    /// can mean, zero field numbers, undefined wire types, out-of-range lengths, and groups that
    /// are unterminated or closed by a terminator naming a different field are all rejected rather
    /// than clamped.
    /// </para>
    /// </remarks>
    public ref struct WProtoReader
    {
        /// <summary>
        /// The deepest nesting a payload may request, counting sub-messages and groups together.
        /// </summary>
        /// <remarks>
        /// A formatter reads a sub-message by handing the nested reader to another formatter, so
        /// nesting depth is stack depth, and a stack overflow cannot be caught -- it takes the
        /// process down. Two hundred bytes of hostile input can ask for two thousand levels. The
        /// bound is shared with group skipping because both are the same question. Protobuf's
        /// reference implementations bound recursion at 100; 64 is deliberately below that and far
        /// above any schema that describes real data.
        /// </remarks>
        public const int MaxNestingDepth = 64;

        private readonly ReadOnlySpan<byte> _buffer;
        private readonly int _depth;
        private int _position;
        private bool _malformed;

        /// <summary>
        /// Initializes a reader over <paramref name="buffer"/>.
        /// </summary>
        /// <param name="buffer">The encoded bytes; an empty span reads as an empty message.</param>
        public WProtoReader(ReadOnlySpan<byte> buffer)
            : this(buffer, 0) { }

        private WProtoReader(ReadOnlySpan<byte> buffer, int depth)
        {
            _buffer = buffer;
            _depth = depth;
            _position = 0;
            _malformed = false;
        }

        /// <summary>
        /// How many enclosing sub-messages this reader sits inside; 0 for a top-level message.
        /// </summary>
        public int Depth => _depth;

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

            // Strict: a key whose value exceeds 32 bits cannot name a field, since the field
            // number is the top 29 bits of a 32-bit key. Truncating it would silently accept a
            // payload no writer can produce and decode it as some unrelated small field. This caps
            // the VALUE, not the byte width -- a redundantly padded key is still accepted, matching
            // protobuf-net and Google's implementations.
            if (!TryReadVarint32Strict(out uint key))
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
        /// Reads an unsigned varint that must fit in 32 bits.
        /// </summary>
        /// <param name="value">Receives the value, or 0 on failure.</param>
        /// <returns><c>true</c> when a value that fits 32 bits was read.</returns>
        /// <remarks>
        /// This is the form for structural numbers -- field keys and lengths -- where a wider
        /// encoding carries no extra meaning and only creates a second spelling of the same value.
        /// Field <i>values</i> use <see cref="TryReadVarint32"/>, which truncates, because a
        /// negative <c>int32</c> is legitimately written sign-extended across ten bytes.
        /// </remarks>
        public bool TryReadVarint32Strict(out uint value)
        {
            value = 0;
            if (!TryReadVarint64(out ulong wide))
            {
                return false;
            }

            if (wide > uint.MaxValue)
            {
                _malformed = true;
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
        /// its own length is contained rather than allowed to consume the parent's fields. It also
        /// carries this reader's depth plus one, and a request past
        /// <see cref="MaxNestingDepth"/> is refused as malformed rather than turned into another
        /// stack frame -- see that constant for why the alternative cannot be caught.
        /// </remarks>
        public bool TryReadMessage(out WProtoReader nested)
        {
            if (_depth >= MaxNestingDepth)
            {
                _malformed = true;
                nested = new WProtoReader(default, MaxNestingDepth);
                return false;
            }

            if (!TryReadBytes(out ReadOnlySpan<byte> payload))
            {
                nested = new WProtoReader(default, _depth + 1);
                return false;
            }

            nested = new WProtoReader(payload, _depth + 1);
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

            // Strict for the same reason as a field key: a length whose value exceeds 32 bits
            // cannot address a span, and truncating it would turn an impossible length into a
            // plausible one rather than rejecting the payload.
            if (!TryReadVarint32Strict(out uint raw))
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
        public bool TrySkipField(int fieldNumber, int wireType)
        {
            return TrySkipField(fieldNumber, wireType, 0);
        }

        private bool TrySkipField(int fieldNumber, int wireType, int depth)
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
                    return TrySkipGroup(fieldNumber, depth);
                }
                default:
                {
                    // EndGroup without a matching StartGroup, or an undefined wire type.
                    _malformed = true;
                    return false;
                }
            }
        }

        private bool TrySkipGroup(int groupFieldNumber, int depth)
        {
            if (depth >= MaxNestingDepth)
            {
                _malformed = true;
                return false;
            }

            while (true)
            {
                if (!TryReadTag(out int innerFieldNumber, out int innerWireType))
                {
                    // End of input inside a group is an unterminated group, not a clean finish.
                    _malformed = true;
                    return false;
                }

                if (innerWireType == WProtoWireType.EndGroup)
                {
                    // The terminator must name the group it closes. Accepting any END_GROUP lets
                    // `1<  2<  /1>  /2>` -- crossed, not nested -- read as well-formed, and lets a
                    // group be closed by a terminator that belongs to an enclosing one.
                    if (innerFieldNumber != groupFieldNumber)
                    {
                        _malformed = true;
                        return false;
                    }

                    return true;
                }

                if (!TrySkipField(innerFieldNumber, innerWireType, depth + 1))
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
