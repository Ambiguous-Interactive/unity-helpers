// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    /// <summary>Configures immutable nesting, element-count, and text-length limits for a JSON read.</summary>
    /// <remarks>
    /// Limits apply to every container a document declares: an array's elements and an object's
    /// members are counted per container, and text lengths are counted in encoded UTF-8 bytes for
    /// string values and property names alike. These limits bound the encoded document, not the
    /// size of objects a custom converter constructs.
    /// </remarks>
    public sealed class WJsonReadLimits
    {
        /// <summary>The deepest nesting a JSON read will descend, whatever a caller asks for.</summary>
        /// <remarks>
        /// A converter reads a nested value by calling another converter, so nesting depth is stack
        /// depth, and a stack overflow cannot be caught. Three bytes per level buys a level, which
        /// is why this ceiling is the package's rather than the caller's.
        /// </remarks>
        public const int MaximumSupportedNestingDepth = 64;

        internal static readonly WJsonReadLimits Default = new WJsonReadLimits();

        /// <summary>Maximum elements in one array, or members in one object, counted per container.</summary>
        public int MaximumElementCount { get; }

        /// <summary>Maximum encoded UTF-8 bytes in one string value or one property name.</summary>
        public int MaximumTextLength { get; }

        /// <summary>Maximum combined array and object nesting depth, never above 64.</summary>
        public int MaximumNestingDepth { get; }

        /// <summary>Creates default JSON limits with nesting bounded to 64.</summary>
        public WJsonReadLimits()
            : this(int.MaxValue, int.MaxValue, MaximumSupportedNestingDepth) { }

        /// <summary>Creates limits, treating negative values as zero and capping nesting at 64.</summary>
        /// <param name="maximumElementCount">Maximum elements in one array or members in one object.</param>
        /// <param name="maximumTextLength">Maximum encoded bytes in one string value or property name.</param>
        /// <param name="maximumNestingDepth">Maximum combined array and object nesting depth.</param>
        public WJsonReadLimits(
            int maximumElementCount = int.MaxValue,
            int maximumTextLength = int.MaxValue,
            int maximumNestingDepth = MaximumSupportedNestingDepth
        )
        {
            MaximumElementCount = maximumElementCount < 0 ? 0 : maximumElementCount;
            MaximumTextLength = maximumTextLength < 0 ? 0 : maximumTextLength;
            MaximumNestingDepth =
                maximumNestingDepth < 0 ? 0
                : MaximumSupportedNestingDepth < maximumNestingDepth ? MaximumSupportedNestingDepth
                : maximumNestingDepth;
        }
    }
}
