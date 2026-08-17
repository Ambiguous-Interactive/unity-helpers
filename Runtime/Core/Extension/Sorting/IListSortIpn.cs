// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is ipnsort, from the sort-research-rs project by Lukas Bergdoll
// (Voultapher) and Orson Peters, Apache License 2.0 / MIT License (dual-licensed),
// https://github.com/Voultapher/sort-research-rs. This is an adaptation of that work; the design is
// the original authors'. See docs/project/third-party-notices.md.

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System.Collections.Generic;
    using Utils;

    public static partial class IListExtensions
    {
        /// <summary>
        /// Sorts the list using IpnSort, an unstable introspective quicksort with median-of-medians sampling.
        /// </summary>
        /// <remarks>
        /// Implementation reference: ipnsort (Voultapher) — https://github.com/Voultapher/sort-research-rs/tree/main/writeup/ipnsort_introduction.
        /// </remarks>
        public static void IpnSort<T, TComparer>(this IList<T> list, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int count = list.Count;
            if (count < 2)
            {
                return;
            }

            if (list is T[] array)
            {
                IpnSortCore(array, count, comparer);
                return;
            }

            using PooledArray<T> scratchLease = SystemArrayPool<T>.Get(count, out T[] scratch);
            list.CopyTo(scratch, 0);
            IpnSortCore(scratch, count, comparer);
            WriteBack(list, scratch, count);
        }

        private static void IpnSortCore<T, TComparer>(T[] array, int count, TComparer comparer)
            where TComparer : IComparer<T>
        {
            IpnSortRange(array, 0, count - 1, comparer, 2 * FloorLog2(count));
        }

        private static void IpnSortRange<T, TComparer>(
            T[] array,
            int left,
            int right,
            TComparer comparer,
            int depthLimit
        )
            where TComparer : IComparer<T>
        {
            const int insertionThreshold = 32;
            while (right - left > insertionThreshold)
            {
                if (depthLimit == 0)
                {
                    HeapSortRange(array, left, right, comparer);
                    return;
                }

                int pivotIndex = IpnSelectPivotIndex(array, left, right, comparer);
                (int pivotStart, int pivotEnd, bool swapped) = PartitionRange(
                    array,
                    left,
                    right,
                    pivotIndex,
                    comparer
                );

                depthLimit--;
                if (!swapped && IsRangeSorted(array, left, right, comparer))
                {
                    return;
                }

                if (pivotStart - left < right - pivotEnd)
                {
                    if (left < pivotStart)
                    {
                        IpnSortRange(array, left, pivotStart - 1, comparer, depthLimit);
                    }
                    left = pivotEnd + 1;
                }
                else
                {
                    if (pivotEnd < right)
                    {
                        IpnSortRange(array, pivotEnd + 1, right, comparer, depthLimit);
                    }
                    right = pivotStart - 1;
                }
            }

            InsertionSortRange(array, left, right, comparer);
        }

        private static int IpnSelectPivotIndex<T, TComparer>(
            T[] array,
            int left,
            int right,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int length = right - left + 1;
            if (length < 128)
            {
                return SelectPivotIndex(array, left, right, comparer);
            }

            int step = length / 4;
            int a = left;
            int b = left + step;
            int c = left + (step << 1);
            int d = left + step * 3;
            int e = right;
            return MedianOfFiveIndices(array, a, b, c, d, e, comparer);
        }
    }
}
