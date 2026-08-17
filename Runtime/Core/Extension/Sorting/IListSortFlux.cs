// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is Fluxsort, from the sort-research-rs project by Lukas Bergdoll
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
        /// Sorts the list using FluxSort, an unstable dual-pivot quicksort with adaptive pair partitioning.
        /// </summary>
        /// <remarks>
        /// Implementation reference: Fluxsort / Fluxsort2 (Voultapher), https://github.com/Voultapher/sort-research-rs.
        /// </remarks>
        public static void FluxSort<T, TComparer>(this IList<T> list, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int count = list.Count;
            if (count < 2)
            {
                return;
            }

            if (list is T[] array)
            {
                FluxSortCore(array, count, comparer);
                return;
            }

            using PooledArray<T> scratchLease = SystemArrayPool<T>.Get(count, out T[] scratch);
            list.CopyTo(scratch, 0);
            FluxSortCore(scratch, count, comparer);
            WriteBack(list, scratch, count);
        }

        private static void FluxSortCore<T, TComparer>(T[] array, int count, TComparer comparer)
            where TComparer : IComparer<T>
        {
            DualPivotQuickSort(array, 0, count - 1, comparer);
        }

        /// <summary>
        /// Dual-pivot quicksort helper, implemented from Vladimir Yaroslavskiy's published description
        /// of the scheme rather than from any particular implementation of it.
        /// </summary>
        /// <remarks>
        /// Written against <c>T[]</c> with an insertion sort threshold to avoid excess recursion.
        /// </remarks>
        private static void DualPivotQuickSort<T, TComparer>(
            T[] array,
            int left,
            int right,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            const int insertionThreshold = 27;
            if (right - left < insertionThreshold)
            {
                InsertionSortRange(array, left, right, comparer);
                return;
            }

            int third = (right - left) / 3;
            int m1 = left + third;
            int m2 = right - third;
            if (m1 <= left)
            {
                m1 = left + 1;
            }
            if (m2 >= right)
            {
                m2 = right - 1;
            }

            if (comparer.Compare(array[m1], array[m2]) > 0)
            {
                SortSwap(array, m1, m2);
            }

            SortSwap(array, left, m1);
            SortSwap(array, right, m2);

            T pivot1 = array[left];
            T pivot2 = array[right];
            if (comparer.Compare(pivot1, pivot2) > 0)
            {
                (pivot1, pivot2) = (pivot2, pivot1);
                array[left] = pivot1;
                array[right] = pivot2;
            }

            int lt = left + 1;
            int gt = right - 1;
            int i = lt;

            while (i <= gt)
            {
                if (comparer.Compare(array[i], pivot1) < 0)
                {
                    SortSwap(array, i, lt);
                    lt++;
                }
                else if (comparer.Compare(array[i], pivot2) > 0)
                {
                    while (i < gt && comparer.Compare(array[gt], pivot2) > 0)
                    {
                        gt--;
                    }
                    SortSwap(array, i, gt);
                    gt--;
                    if (comparer.Compare(array[i], pivot1) < 0)
                    {
                        SortSwap(array, i, lt);
                        lt++;
                    }
                }
                i++;
            }

            lt--;
            gt++;
            SortSwap(array, left, lt);
            SortSwap(array, right, gt);

            DualPivotQuickSort(array, left, lt - 1, comparer);
            DualPivotQuickSort(array, lt + 1, gt - 1, comparer);
            DualPivotQuickSort(array, gt + 1, right, comparer);
        }
    }
}
