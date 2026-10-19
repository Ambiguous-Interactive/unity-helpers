// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    /// <summary>Configures immutable wire, parsing-work, resource, and recursion limits for a read.</summary>
    /// <remarks>
    /// Wire and per-region tag limits apply to each reader region; packed elements and total tags
    /// have separate shared root budgets, as do duplicate-message merge copies and decoded payloads.
    /// Field counts include
    /// unknown and duplicate tags and group terminators, but not untagged packed elements.
    /// These limits do not bound the size of objects a custom formatter constructs.
    /// </remarks>
    public sealed class WProtoReadLimits
    {
        internal static readonly WProtoReadLimits Default = new WProtoReadLimits();

        /// <summary>Maximum encoded bytes in a root or nested reader region.</summary>
        public int MaximumMessageBytes { get; }

        /// <summary>Maximum bytes in a length-delimited field, including unknown and packed fields.</summary>
        public int MaximumLengthDelimitedBytes { get; }

        /// <summary>Maximum tags in one reader region, including duplicate and unknown tags.</summary>
        public int MaximumFieldCount { get; }

        /// <summary>Maximum tag reads across all regions and reader copies in one read.</summary>
        /// <remarks>
        /// The default int.MaxValue disables this cumulative cap. Replayed tags spend the
        /// finite work budget again; untagged packed elements do not.
        /// Only readers derived from this root share the budget; independent validation readers
        /// and work performed by formatters are not included.
        /// </remarks>
        public int MaximumTotalFieldCount { get; }

        /// <summary>Maximum packed scalar elements across all runs and nested regions in one read.</summary>
        public int MaximumPackedElementCount { get; }

        /// <summary>Maximum bytes copied while accumulating duplicate sub-message occurrences in one read.</summary>
        /// <remarks>
        /// The default long.MaxValue disables this cumulative cap. Borrowing a first occurrence
        /// costs no copy bytes; consolidation, growth and appends charge the bytes actually copied.
        /// This does not bound ordinary string or byte-array reads, retained capacity, or object graphs.
        /// </remarks>
        public long MaximumMessageMergeCopyBytes { get; }

        /// <summary>Maximum cumulative UTF-8 and byte-array payload bytes processed by owned decoders.</summary>
        /// <remarks>
        /// The default long.MaxValue disables this allowance. Text is charged by its delivered UTF-8
        /// bytes, so its UTF-16 contents can occupy up to twice the charged length. Object headers,
        /// collection capacity, custom formatter allocations, and borrowed raw spans are excluded.
        /// </remarks>
        public long MaximumDecodedPayloadBytes { get; }

        /// <summary>Maximum combined sub-message and group depth, never above 64.</summary>
        public int MaximumNestingDepth { get; }

        /// <summary>Creates default wire limits with nesting bounded to 64.</summary>
        public WProtoReadLimits()
            : this(int.MaxValue, int.MaxValue, int.MaxValue, WProtoReader.MaxNestingDepth) { }

        /// <summary>Creates wire limits with no additional packed-element cap.</summary>
        /// <param name="maximumMessageBytes">Maximum bytes in a reader region.</param>
        /// <param name="maximumLengthDelimitedBytes">Maximum bytes in a length-delimited field.</param>
        /// <param name="maximumFieldCount">Maximum tags in a reader region.</param>
        /// <param name="maximumNestingDepth">Maximum combined message and group depth.</param>
        public WProtoReadLimits(
            int maximumMessageBytes,
            int maximumLengthDelimitedBytes,
            int maximumFieldCount,
            int maximumNestingDepth
        )
            : this(
                maximumMessageBytes,
                maximumLengthDelimitedBytes,
                maximumFieldCount,
                maximumNestingDepth,
                int.MaxValue
            ) { }

        /// <summary>Creates limits, treating negative values as zero and capping nesting at 64.</summary>
        /// <param name="maximumMessageBytes">Maximum bytes in a root or nested reader region.</param>
        /// <param name="maximumLengthDelimitedBytes">Maximum bytes in any length-delimited field.</param>
        /// <param name="maximumFieldCount">Maximum tags read in one region, including skipped groups.</param>
        /// <param name="maximumNestingDepth">Maximum combined sub-message and group depth.</param>
        /// <param name="maximumPackedElementCount">Maximum packed elements across the entire read.</param>
        public WProtoReadLimits(
            int maximumMessageBytes = int.MaxValue,
            int maximumLengthDelimitedBytes = int.MaxValue,
            int maximumFieldCount = int.MaxValue,
            int maximumNestingDepth = WProtoReader.MaxNestingDepth,
            int maximumPackedElementCount = int.MaxValue
        )
        {
            MaximumMessageBytes = maximumMessageBytes < 0 ? 0 : maximumMessageBytes;
            MaximumLengthDelimitedBytes =
                maximumLengthDelimitedBytes < 0 ? 0 : maximumLengthDelimitedBytes;
            MaximumPackedElementCount =
                maximumPackedElementCount < 0 ? 0 : maximumPackedElementCount;
            MaximumFieldCount = maximumFieldCount < 0 ? 0 : maximumFieldCount;
            MaximumTotalFieldCount = int.MaxValue;
            MaximumMessageMergeCopyBytes = long.MaxValue;
            MaximumDecodedPayloadBytes = long.MaxValue;
            MaximumNestingDepth =
                maximumNestingDepth < 0 ? 0
                : WProtoReader.MaxNestingDepth < maximumNestingDepth ? WProtoReader.MaxNestingDepth
                : maximumNestingDepth;
        }

        private WProtoReadLimits(
            WProtoReadLimits limits,
            int maximumTotalFieldCount,
            long maximumMessageMergeCopyBytes,
            long maximumDecodedPayloadBytes
        )
            : this(
                limits.MaximumMessageBytes,
                limits.MaximumLengthDelimitedBytes,
                limits.MaximumFieldCount,
                limits.MaximumNestingDepth,
                limits.MaximumPackedElementCount
            )
        {
            MaximumTotalFieldCount = maximumTotalFieldCount < 0 ? 0 : maximumTotalFieldCount;
            MaximumMessageMergeCopyBytes =
                maximumMessageMergeCopyBytes < 0 ? 0 : maximumMessageMergeCopyBytes;
            MaximumDecodedPayloadBytes =
                maximumDecodedPayloadBytes < 0 ? 0 : maximumDecodedPayloadBytes;
        }

        /// <summary>Copies these limits with a replacement cumulative tag work limit.</summary>
        /// <param name="maximumTotalFieldCount">Maximum total tag reads; negative values become zero.</param>
        /// <returns>New immutable limits with every other setting preserved.</returns>
        /// <remarks>The new budget is independent for each root reader and shared by its descendants and copies.</remarks>
        public WProtoReadLimits WithTotalFieldCount(int maximumTotalFieldCount)
        {
            return new WProtoReadLimits(
                this,
                maximumTotalFieldCount,
                MaximumMessageMergeCopyBytes,
                MaximumDecodedPayloadBytes
            );
        }

        /// <summary>Copies these limits with a replacement cumulative message-merge copy allowance.</summary>
        /// <param name="maximumMessageMergeCopyBytes">Maximum copied bytes; negative values become zero.</param>
        /// <returns>New immutable limits with every other setting preserved.</returns>
        /// <remarks>
        /// Each root reader has an independent allowance shared by its descendants and copies.
        /// The parent-aware accumulator checks the allowance before allocating or copying a merge buffer.
        /// </remarks>
        public WProtoReadLimits WithMessageMergeCopyBytes(long maximumMessageMergeCopyBytes)
        {
            return new WProtoReadLimits(
                this,
                MaximumTotalFieldCount,
                maximumMessageMergeCopyBytes,
                MaximumDecodedPayloadBytes
            );
        }

        /// <summary>Copies these limits with a cumulative decoded text and byte-array payload allowance.</summary>
        /// <param name="maximumDecodedPayloadBytes">Maximum delivered UTF-8 or byte-array bytes; negatives become zero.</param>
        /// <returns>New immutable limits with every other setting preserved.</returns>
        /// <remarks>
        /// Owned decoders charge before materialization, including overwritten values and replays.
        /// Descendants and copies share this allowance; independent root readers do not.
        /// A refused operation has consumed its encoded field and faults its reader. Charges are not refunded.
        /// Regenerate older byte-array member codecs to enforce this allowance; direct borrowed-span copies bypass it.
        /// </remarks>
        public WProtoReadLimits WithDecodedPayloadBytes(long maximumDecodedPayloadBytes)
        {
            return new WProtoReadLimits(
                this,
                MaximumTotalFieldCount,
                MaximumMessageMergeCopyBytes,
                maximumDecodedPayloadBytes
            );
        }
    }
}
