// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Rewrites a protobuf payload in ways the format says must not change what it decodes to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A round trip proves a decoder agrees with its own encoder. It cannot prove the decoder agrees
    /// with the <b>format</b>, because the encoder only ever emits one of the many byte sequences
    /// that mean the same thing. A metamorphic transform closes that gap: it produces a different,
    /// equally legal spelling of a value the encoder has already written, and the decode has to be
    /// unchanged.
    /// </para>
    /// <para>
    /// Every transform here is schema-aware, which is the part that makes it honest rather than a
    /// fuzzer. Reordering has to keep repeated occurrences in order; an injected field has to carry
    /// a number the contract does not declare, or it is not an unknown field but a different value.
    /// A transform that cannot be made safely reports that it did nothing, and the caller counts it,
    /// so a battery that silently degrades into no-ops fails rather than passes.
    /// </para>
    /// </remarks>
    internal static class WireMetamorphic
    {
        /// <summary>
        /// Splits <paramref name="payload"/> into its top-level fields, or returns <c>null</c> when
        /// it is not a payload this can safely rewrite.
        /// </summary>
        /// <param name="payload">The encoded message.</param>
        /// <remarks>
        /// Null rather than an exception, and null rather than a partial list: a transform that
        /// rewrites half a payload produces bytes that mean something else entirely, which would
        /// show up as a decoder bug in whatever consumed it.
        /// </remarks>
        internal static List<Field> Tokenize(byte[] payload)
        {
            List<Field> fields = new List<Field>();
            WProtoReader reader = new WProtoReader(payload);
            int start = 0;

            while (reader.Position < payload.Length)
            {
                if (!reader.TryReadTag(out int number, out int wireType))
                {
                    return null;
                }

                if (!reader.TrySkipField(number, wireType))
                {
                    return null;
                }

                int end = reader.Position;
                byte[] bytes = new byte[end - start];
                Array.Copy(payload, start, bytes, 0, bytes.Length);
                fields.Add(new Field(number, wireType, bytes));
                start = end;
            }

            return reader.Malformed ? null : fields;
        }

        /// <summary>Concatenates <paramref name="fields"/> back into a payload.</summary>
        /// <param name="fields">The fields, in the order they should appear.</param>
        internal static byte[] Join(IEnumerable<Field> fields)
        {
            List<byte> bytes = new List<byte>();
            foreach (Field field in fields)
            {
                bytes.AddRange(field.Bytes);
            }

            return bytes.ToArray();
        }

        /// <summary>
        /// Reverses the order the field <b>numbers</b> appear in, keeping each number's own
        /// occurrences in their original order.
        /// </summary>
        /// <param name="payload">The encoded message.</param>
        /// <returns>The rewritten payload, or <c>null</c> when there was nothing to reorder.</returns>
        /// <remarks>
        /// Protobuf lets a serializer emit fields in any order, so this is legal for every payload.
        /// The two rules it must not break are the ones that make order observable at all: repeated
        /// elements keep their order, and a non-repeated field that appears twice resolves
        /// last-wins. Grouping by number and reversing the groups preserves both, where a plain
        /// reversal of the field list would break both.
        /// </remarks>
        internal static byte[] ReverseFieldOrder(byte[] payload)
        {
            List<Field> fields = Tokenize(payload);
            if (fields == null)
            {
                return null;
            }

            List<int> numbers = new List<int>();
            foreach (Field field in fields)
            {
                if (!numbers.Contains(field.Number))
                {
                    numbers.Add(field.Number);
                }
            }

            if (numbers.Count < 2)
            {
                return null;
            }

            numbers.Reverse();
            List<Field> reordered = new List<Field>(fields.Count);
            foreach (int number in numbers)
            {
                reordered.AddRange(fields.Where(field => field.Number == number));
            }

            return Join(reordered);
        }

        /// <summary>
        /// Inserts a field the contract does not declare, at the front and at the back.
        /// </summary>
        /// <param name="payload">The encoded message.</param>
        /// <param name="declaredTags">Every field number the contract claims.</param>
        /// <returns>The rewritten payloads; empty when no safe tag was available.</returns>
        /// <remarks>
        /// This is the transform that needs the schema most. An "unknown" field carrying a number
        /// the contract <b>does</b> declare is not an unknown field at all -- it is a second value
        /// for a real member, and asserting the decode is unchanged would then be asserting
        /// something false. The tag is chosen above every declared one and above every one the
        /// payload already carries, and both ends are tried because a reader that skips correctly
        /// only at the end is a reader that has never been asked to resynchronize.
        /// </remarks>
        internal static List<byte[]> InjectUnknownField(byte[] payload, ISet<int> declaredTags)
        {
            List<byte[]> results = new List<byte[]>();
            List<Field> fields = Tokenize(payload);
            if (fields == null)
            {
                return results;
            }

            int tag = 1;
            foreach (int candidate in declaredTags.Concat(fields.Select(field => field.Number)))
            {
                if (tag <= candidate)
                {
                    tag = candidate + 1;
                }
            }

            if (WProtoWireType.MaxFieldNumber < tag)
            {
                return results;
            }

            byte[] unknown = Encode(tag, WProtoWireType.Varint, new byte[] { 0xAC, 0x02 });
            Field injected = new Field(tag, WProtoWireType.Varint, unknown);

            List<Field> front = new List<Field> { injected };
            front.AddRange(fields);
            results.Add(Join(front));

            List<Field> back = new List<Field>(fields) { injected };
            results.Add(Join(back));

            if (1 < fields.Count)
            {
                List<Field> middle = new List<Field>(fields);
                middle.Insert(fields.Count / 2, injected);
                results.Add(Join(middle));
            }

            return results;
        }

        /// <summary>
        /// Splits one occurrence of a sub-message field into two that carry its fields between them.
        /// </summary>
        /// <param name="payload">The encoded message.</param>
        /// <param name="messageTags">The field numbers declared as a non-repeated sub-message.</param>
        /// <returns>The rewritten payloads; empty when no field could be split.</returns>
        /// <remarks>
        /// Protobuf says a parser merges repeated occurrences of a non-repeated sub-message "as if
        /// with <c>Message::MergeFrom</c>", so the concatenation of two encodings <b>is</b> their
        /// merge and splitting one in two must decode identically. This package already depends on
        /// that -- and got it wrong once, decoding each occurrence in turn so the second replaced
        /// the first and silently dropped its members. Only tags the schema says are non-repeated
        /// sub-messages are touched: splitting a <c>string</c> would produce two truncated strings
        /// and last-wins, which is a different value rather than an illegal payload.
        /// </remarks>
        internal static List<byte[]> SplitSubMessages(byte[] payload, ISet<int> messageTags)
        {
            List<byte[]> results = new List<byte[]>();
            List<Field> fields = Tokenize(payload);
            if (fields == null)
            {
                return results;
            }

            for (int index = 0; index < fields.Count; index++)
            {
                Field field = fields[index];
                if (
                    field.WireType != WProtoWireType.LengthDelimited
                    || !messageTags.Contains(field.Number)
                )
                {
                    continue;
                }

                byte[] inner = ValueOf(field);
                if (inner == null)
                {
                    continue;
                }

                List<Field> innerFields = Tokenize(inner);
                if (innerFields == null || innerFields.Count < 2)
                {
                    continue;
                }

                int cut = innerFields.Count / 2;
                byte[] first = Join(innerFields.Take(cut));
                byte[] second = Join(innerFields.Skip(cut));

                List<Field> rewritten = new List<Field>(fields);
                rewritten.RemoveAt(index);
                rewritten.Insert(
                    index,
                    new Field(
                        field.Number,
                        field.WireType,
                        Encode(field.Number, field.WireType, second)
                    )
                );
                rewritten.Insert(
                    index,
                    new Field(
                        field.Number,
                        field.WireType,
                        Encode(field.Number, field.WireType, first)
                    )
                );
                results.Add(Join(rewritten));
            }

            return results;
        }

        /// <summary>Returns a length-delimited field's payload, without its key or length.</summary>
        private static byte[] ValueOf(Field field)
        {
            WProtoReader reader = new WProtoReader(field.Bytes);
            if (!reader.TryReadTag(out int _, out int _))
            {
                return null;
            }

            return reader.TryReadBytes(out ReadOnlySpan<byte> value) ? value.ToArray() : null;
        }

        /// <summary>Builds one field: its key, a length prefix where the wire type has one, its value.</summary>
        /// <remarks>
        /// Written by hand rather than through <c>WProtoWriter</c> because two of the three callers
        /// need to place bytes the writer has no typed method for -- a varint that is already
        /// encoded, and a sub-message payload that has already been carved out of another field.
        /// Going through a typed writer would mean decoding those just to re-encode them.
        /// </remarks>
        private static byte[] Encode(int number, int wireType, byte[] value)
        {
            List<byte> bytes = new List<byte>();
            AppendVarint(bytes, ((ulong)number << 3) | (uint)wireType);
            if (wireType == WProtoWireType.LengthDelimited)
            {
                AppendVarint(bytes, (ulong)value.Length);
            }

            bytes.AddRange(value);
            return bytes.ToArray();
        }

        private static void AppendVarint(List<byte> bytes, ulong value)
        {
            while (0x7F < value)
            {
                bytes.Add((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }

            bytes.Add((byte)value);
        }

        /// <summary>One top-level field occurrence, as the exact bytes it occupied.</summary>
        internal readonly struct Field
        {
            internal Field(int number, int wireType, byte[] bytes)
            {
                Number = number;
                WireType = wireType;
                Bytes = bytes;
            }

            /// <summary>The field number from the key.</summary>
            internal int Number { get; }

            /// <summary>The wire type from the key.</summary>
            internal int WireType { get; }

            /// <summary>The key and the value, verbatim.</summary>
            internal byte[] Bytes { get; }
        }
    }
}
