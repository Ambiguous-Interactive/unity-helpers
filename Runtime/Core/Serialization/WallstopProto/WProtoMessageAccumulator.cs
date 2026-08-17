// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>
    /// Gathers every occurrence of one non-repeated sub-message field into a single payload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// protobuf says a parser "merges multiple instances of the same field, as if with
    /// <c>Message::MergeFrom</c>", and protobuf-net does -- measured, <c>12 02 08 01</c> followed by
    /// <c>12 02 10 02</c> decodes to both members set, not to the second alone. Replacing instead
    /// loses the first occurrence's members in silence, on a payload that is legal protobuf.
    /// </para>
    /// <para>
    /// The merge is done by concatenating the occurrences rather than by decoding one into another,
    /// because protobuf defines the two to be the same thing: parsing the concatenation of two
    /// encodings yields the merge of what each encodes. That equivalence is what makes this
    /// recursive for free -- a sub-message of a sub-message merges by the same rule, one level
    /// down -- and it is what keeps the decoded value's lifecycle hooks running exactly once, since
    /// the accumulated payload is decoded once at the end rather than once per occurrence.
    /// </para>
    /// <para>
    /// A single occurrence -- every payload this package writes, and nearly every one it reads --
    /// costs nothing: the span is kept as it was handed over and no buffer is allocated at all.
    /// Everything this accumulates has already been delivered by the reader, which refuses a length
    /// longer than the bytes it holds, so the buffer is bounded by the payload rather than by a
    /// number the payload claims.
    /// </para>
    /// </remarks>
    public ref struct WProtoMessageAccumulator
    {
        private ReadOnlySpan<byte> _payload;
        private byte[] _buffer;
        private int _length;
        private bool _seen;

        /// <summary>
        /// Indicates whether the field appeared at all, which is what separates an absent
        /// sub-message from one whose payload is empty.
        /// </summary>
        public bool HasValue => _seen;

        /// <summary>The bytes of every occurrence, in the order they arrived.</summary>
        public ReadOnlySpan<byte> Payload => _payload;

        /// <summary>
        /// Adds one occurrence of the field.
        /// </summary>
        /// <param name="occurrence">The sub-message payload, without its key or length prefix.</param>
        /// <returns>
        /// <c>true</c> when the occurrence was accumulated; <c>false</c> only when the total would
        /// exceed what a single span can address.
        /// </returns>
        public bool TryAdd(ReadOnlySpan<byte> occurrence)
        {
            if (!_seen)
            {
                _seen = true;
                _payload = occurrence;
                return true;
            }

            long required = (long)(_buffer == null ? _payload.Length : _length) + occurrence.Length;
            if (int.MaxValue < required)
            {
                return false;
            }

            if (_buffer == null)
            {
                _buffer = new byte[Capacity(0, (int)required)];
                _payload.CopyTo(_buffer);
                _length = _payload.Length;
            }
            else if (_buffer.Length < required)
            {
                byte[] grown = new byte[Capacity(_buffer.Length, (int)required)];
                Array.Copy(_buffer, grown, _length);
                _buffer = grown;
            }

            occurrence.CopyTo(new Span<byte>(_buffer, _length, occurrence.Length));
            _length += occurrence.Length;
            _payload = new ReadOnlySpan<byte>(_buffer, 0, _length);
            return true;
        }

        // Doubling rather than exact: a payload that repeats one field a hundred thousand times is
        // hostile rather than typical, and copying the accumulation on every occurrence is what
        // turns it into quadratic work.
        private static int Capacity(int current, int required)
        {
            long doubled = (long)current * 2;
            if (doubled <= required)
            {
                return required;
            }

            return int.MaxValue < doubled ? int.MaxValue : (int)doubled;
        }
    }
}
