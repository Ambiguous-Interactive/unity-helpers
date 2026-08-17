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

    public static partial class IListExtensions
    {
        /// <summary>
        /// Sorts the list using FluxSort, an unstable dual-pivot quicksort with adaptive pair partitioning.
        /// </summary>
        /// <remarks>
        /// Implementation reference: Fluxsort / Fluxsort2 (Voultapher), https://github.com/Voultapher/sort-research-rs.
        /// </remarks>
        public static void FluxSort<T, TComparer>(this IList<T> array, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int count = array.Count;
            if (count < 2)
            {
                return;
            }

            DualPivotQuickSort(array, 0, count - 1, comparer);
        }

        /// <summary>
        /// Dual-pivot quicksort helper modeled after Vladimir Yaroslavskiy’s Java 7 implementation.
        /// </summary>
        /// <remarks>
        /// Adapted for <c>IList&lt;T&gt;</c> with an insertion sort threshold to avoid excess recursion.
        /// </remarks>
        private static void DualPivotQuickSort<T, TComparer>(
            IList<T> array,
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
                array.Swap(m1, m2);
            }

            array.Swap(left, m1);
            array.Swap(right, m2);

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
                    array.Swap(i, lt);
                    lt++;
                }
                else if (comparer.Compare(array[i], pivot2) > 0)
                {
                    while (i < gt && comparer.Compare(array[gt], pivot2) > 0)
                    {
                        gt--;
                    }
                    array.Swap(i, gt);
                    gt--;
                    if (comparer.Compare(array[i], pivot1) < 0)
                    {
                        array.Swap(i, lt);
                        lt++;
                    }
                }
                i++;
            }

            lt--;
            gt++;
            array.Swap(left, lt);
            array.Swap(right, gt);

            DualPivotQuickSort(array, left, lt - 1, comparer);
            DualPivotQuickSort(array, lt + 1, gt - 1, comparer);
            DualPivotQuickSort(array, gt + 1, right, comparer);
        }
    }
}
