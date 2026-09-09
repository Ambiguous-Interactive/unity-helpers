// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    /// <summary>Configures immutable wire-size, field-count, packed-element, and recursion limits for a read.</summary>
    /// <remarks>
    /// Wire and tag limits apply to each reader region; packed elements share one root budget.
    /// Field counts include
    /// unknown and duplicate tags and group terminators, but not untagged packed elements.
    /// These limits bound encoded input, not the size of objects a custom formatter constructs.
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

        /// <summary>Maximum packed scalar elements across all runs and nested regions in one read.</summary>
        public int MaximumPackedElementCount { get; }

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
            MaximumNestingDepth =
                maximumNestingDepth < 0 ? 0
                : WProtoReader.MaxNestingDepth < maximumNestingDepth ? WProtoReader.MaxNestingDepth
                : maximumNestingDepth;
        }
    }
}
