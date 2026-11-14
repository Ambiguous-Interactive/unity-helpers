// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Collections.Generic;

    public static partial class WallMath
    {
        private const int ByteValueCount = byte.MaxValue + 1;

        /// <summary>Finds the median of unsorted bytes without copying or sorting them.</summary>
        /// <remarks>Null or empty input returns false and NaN.</remarks>
        public static bool TryMedian(byte[] values, out double median)
        {
            ReadOnlySpan<byte> span = values;
            return TryMedian(span, out median);
        }

        /// <summary>Finds the median of unsorted byte values without copying or sorting them.</summary>
        /// <remarks>Uses a fixed 256-bin stack histogram in O(n + 256) time. Empty input returns false and NaN.</remarks>
        public static bool TryMedian(ReadOnlySpan<byte> values, out double median)
        {
            if (values.IsEmpty)
            {
                median = double.NaN;
                return false;
            }
            Span<int> counts = stackalloc int[ByteValueCount];
            counts.Clear();
            foreach (byte value in values)
            {
                ++counts[value];
            }
            median = ByteMedianFromCounts(counts, values.Length);
            return true;
        }

        /// <summary>Finds the median of unsorted byte values without copying or sorting them.</summary>
        /// <remarks>Uses a fixed 256-bin stack histogram in O(n + 256) time. Null or empty input returns false and NaN.</remarks>
        public static bool TryMedian(IReadOnlyList<byte> values, out double median)
        {
            if (values == null)
            {
                median = double.NaN;
                return false;
            }
            int count = values.Count;
            if (count <= 0)
            {
                median = double.NaN;
                return false;
            }
            Span<int> counts = stackalloc int[ByteValueCount];
            counts.Clear();
            for (int index = 0; index < count; ++index)
            {
                ++counts[values[index]];
            }
            median = ByteMedianFromCounts(counts, count);
            return true;
        }

        private static double ByteMedianFromCounts(ReadOnlySpan<int> counts, int count)
        {
            int lowerRank = (count - 1) / 2;
            int upperRank = count / 2;
            int seen = 0;
            int lower = 0;
            for (int value = 0; value < ByteValueCount; ++value)
            {
                int next = seen + counts[value];
                if (seen <= lowerRank && lowerRank < next)
                {
                    lower = value;
                }
                if (upperRank < next)
                {
                    return (lower + value) * 0.5d;
                }
                seen = next;
            }
            return 0d;
        }
    }
}
