// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using System;
    using System.Globalization;
    using System.Text.Json;

    /// <summary>
    /// Refuses a JSON document that exceeds its <see cref="WJsonReadLimits"/> before it is decoded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gate walks the encoded bytes with a <see cref="Utf8JsonReader"/>, which materializes
    /// nothing: no string, no collection, no boxed element. A breach is therefore refused before
    /// the allocation it would have caused, which is the only ordering that helps -- a budget
    /// checked while a converter fills a list has already paid for the list.
    /// </para>
    /// <para>
    /// Malformed input is not this gate's to report. A document the tokenizer rejects is left to
    /// the decoder, which describes it in its own words; only a well-formed document over budget
    /// is refused here.
    /// </para>
    /// </remarks>
    internal static class WJsonReadBudget
    {
        // One counter per depth plus the root slot and the slot a container at the ceiling resets.
        private const int ElementCounterSlots = WJsonReadLimits.MaximumSupportedNestingDepth + 2;

        /// <summary>Refuses <paramref name="utf8"/> when it exceeds <paramref name="limits"/>.</summary>
        /// <param name="utf8">The encoded document, about to be decoded.</param>
        /// <param name="inputLength">Encoded byte count, for the refusal's input descriptor.</param>
        /// <param name="declaredType">The type being restored, for the refusal.</param>
        /// <param name="limits">The limits to hold; null uses the default limits.</param>
        /// <param name="options">
        /// The options the decode will use, so trailing commas and comments are tokenized here
        /// exactly as they will be there.
        /// </param>
        /// <exception cref="SerializationCorruptDataException">The document is over budget.</exception>
        internal static void Enforce(
            ReadOnlySpan<byte> utf8,
            int inputLength,
            Type declaredType,
            WJsonReadLimits limits,
            JsonSerializerOptions options
        )
        {
            WJsonReadLimits resolved = limits ?? WJsonReadLimits.Default;
            Utf8JsonReader reader = new Utf8JsonReader(utf8, ReaderOptionsFor(resolved, options));
            Span<int> elementCounts = stackalloc int[ElementCounterSlots];
            elementCounts.Clear();
            bool afterPropertyName = false;

            try
            {
                while (reader.Read())
                {
                    JsonTokenType token = reader.TokenType;
                    if (token == JsonTokenType.Comment)
                    {
                        // Insignificant: a comment separates neither a member's name from its value.
                        continue;
                    }

                    int depth = reader.CurrentDepth;
                    if (resolved.MaximumNestingDepth < depth)
                    {
                        ThrowRefused(
                            inputLength,
                            declaredType,
                            "nesting "
                                + depth.ToString(CultureInfo.InvariantCulture)
                                + " level(s) deep",
                            nameof(WJsonReadLimits.MaximumNestingDepth),
                            resolved.MaximumNestingDepth
                        );
                    }

                    if (token == JsonTokenType.PropertyName || token == JsonTokenType.String)
                    {
                        long textLength = reader.HasValueSequence
                            ? reader.ValueSequence.Length
                            : reader.ValueSpan.Length;
                        if (resolved.MaximumTextLength < textLength)
                        {
                            ThrowRefused(
                                inputLength,
                                declaredType,
                                "a "
                                    + textLength.ToString(CultureInfo.InvariantCulture)
                                    + "-byte string or property name",
                                nameof(WJsonReadLimits.MaximumTextLength),
                                resolved.MaximumTextLength
                            );
                        }
                    }

                    if (0 < depth && CountsTowardEnclosingContainer(token, afterPropertyName))
                    {
                        int counted = elementCounts[depth] + 1;
                        if (resolved.MaximumElementCount < counted)
                        {
                            ThrowRefused(
                                inputLength,
                                declaredType,
                                counted.ToString(CultureInfo.InvariantCulture)
                                    + " element(s) in one container",
                                nameof(WJsonReadLimits.MaximumElementCount),
                                resolved.MaximumElementCount
                            );
                        }
                        elementCounts[depth] = counted;
                    }

                    if (token == JsonTokenType.StartArray || token == JsonTokenType.StartObject)
                    {
                        elementCounts[depth + 1] = 0;
                    }

                    afterPropertyName = token == JsonTokenType.PropertyName;
                }
            }
            catch (JsonException)
            {
                // The decoder reports a malformed document; this gate only judges a well-formed one.
            }
        }

        /// <summary>
        /// Whether <paramref name="token"/> is one element or member of the container holding it.
        /// </summary>
        /// <param name="token">The token just read.</param>
        /// <param name="afterPropertyName">Whether the previous significant token named a member.</param>
        /// <returns><c>true</c> when the enclosing container's count grows by one.</returns>
        /// <remarks>
        /// A member is counted once, at its name, rather than twice: the value that follows a
        /// property name is that member's value, while a value that follows anything else is an
        /// array element in its own right. The caller excludes the root value, which belongs to no
        /// container, so a zero element budget still admits an empty document.
        /// </remarks>
        private static bool CountsTowardEnclosingContainer(
            JsonTokenType token,
            bool afterPropertyName
        )
        {
            if (token == JsonTokenType.PropertyName)
            {
                return true;
            }

            if (token == JsonTokenType.EndArray || token == JsonTokenType.EndObject)
            {
                return false;
            }

            return !afterPropertyName;
        }

        /// <summary>Builds tokenizer options that match the decode, bounded by the depth limit.</summary>
        /// <param name="limits">The limits being held.</param>
        /// <param name="options">The options the decode will use, or null for the defaults.</param>
        /// <returns>The tokenizer options.</returns>
        /// <remarks>
        /// The tokenizer's own depth bound sits one level above the limit so the breach is reported
        /// as a refusal naming the limit, rather than as a tokenizer error this gate would forward
        /// to the decoder.
        /// </remarks>
        private static JsonReaderOptions ReaderOptionsFor(
            WJsonReadLimits limits,
            JsonSerializerOptions options
        )
        {
            return new JsonReaderOptions
            {
                AllowTrailingCommas = options == null || options.AllowTrailingCommas,
                CommentHandling =
                    options == null ? JsonCommentHandling.Skip : options.ReadCommentHandling,
                MaxDepth = limits.MaximumNestingDepth + 1,
            };
        }

        /// <summary>Reports a document the limits refuse, without decoding any of it.</summary>
        /// <param name="inputLength">Encoded byte count, for the input descriptor.</param>
        /// <param name="declaredType">The type being restored.</param>
        /// <param name="declared">What the document declared, for the message.</param>
        /// <param name="limitName">The <see cref="WJsonReadLimits"/> member that refused it.</param>
        /// <param name="limit">That member's value.</param>
        /// <exception cref="SerializationCorruptDataException">Always.</exception>
        private static void ThrowRefused(
            int inputLength,
            Type declaredType,
            string declared,
            string limitName,
            int limit
        )
        {
            throw new SerializationCorruptDataException(
                SerializationFormat.Json,
                SerializationOperation.Deserialize,
                declaredType,
                SerializationFailureException.DescribeBytes(inputLength),
                SerializationStage.Decode,
                "A JSON payload declared "
                    + declared
                    + ", beyond the "
                    + limit.ToString(CultureInfo.InvariantCulture)
                    + " '"
                    + nameof(WJsonReadLimits)
                    + "."
                    + limitName
                    + "' allows. It is refused before it is decoded, because nesting and element "
                    + "runs cost a payload a few bytes each and cost this process an allocation "
                    + "each.",
                null
            );
        }
    }
}
