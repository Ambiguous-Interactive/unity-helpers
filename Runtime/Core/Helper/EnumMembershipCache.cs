// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using WallstopStudios.UnityHelpers.Core.Extension;

    internal static class EnumMembershipCache<T>
        where T : struct, Enum
    {
        private static readonly bool[] Dense;
        private static readonly ulong Minimum;
        private static readonly ulong[] Sorted;

        static EnumMembershipCache()
        {
            ReadOnlySpan<T> values = EnumValues<T>.Values;
            int valueCount = values.Length;
            if (
                EnumLookupStrategy<T>.TryComputeArrayWindow(
                    values,
                    out ulong minimum,
                    out int length
                )
            )
            {
                Minimum = minimum;
                Dense = new bool[length];
                foreach (T value in values)
                {
                    if (EnumNumericHelper<T>.TryConvertToUInt64(value, out ulong key))
                    {
                        Dense[unchecked(key - minimum)] = true;
                    }
                }
            }
            else
            {
                Sorted = new ulong[valueCount];
                for (int i = 0; i < valueCount; ++i)
                {
                    if (EnumNumericHelper<T>.TryConvertToUInt64(values[i], out ulong key))
                    {
                        Sorted[i] = key;
                    }
                }
                Array.Sort(Sorted);
            }
        }

        internal static bool IsDefined(T value)
        {
            if (!EnumNumericHelper<T>.TryConvertToUInt64(value, out ulong key))
            {
                return false;
            }
            if (Dense != null)
            {
                ulong index = unchecked(key - Minimum);
                return index < (ulong)Dense.Length && Dense[index];
            }
            int low = 0;
            int high = Sorted.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                ulong candidate = Sorted[middle];
                if (candidate == key)
                {
                    return true;
                }
                if (candidate < key)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }
            return false;
        }
    }
}
