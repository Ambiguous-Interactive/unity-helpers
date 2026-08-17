// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is Ska Sort, by Malte Skarupke,
// https://probablydance.com/2016/12/27/i-wrote-a-faster-sorting-algorithm/. This is an adaptation of
// that work; the design is the original author's. The upstream license is not recorded in this
// repository. See docs/project/third-party-notices.md.

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System.Collections.Generic;

    public static partial class IListExtensions
    {
        /// <summary>
        /// Sorts the list using Ska Sort, a branch-friendly dual-pivot quicksort inspired by Skarupke’s research.
        /// </summary>
        /// <remarks>
        /// Implementation reference: Ska Sort by Malte Skarupke, https://probablydance.com/2016/12/27/i-wrote-a-faster-sorting-algorithm/.
        /// This adaptation applies multi-way partitioning with ninther sampling and tail recursion elimination.
        /// </remarks>
        public static void SkaSort<T, TComparer>(this IList<T> array, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int count = array.Count;
            if (count < 2)
            {
                return;
            }

            SkaSortRange(array, 0, count - 1, comparer, 2 * FloorLog2(count));
        }

        private static void SkaSortRange<T, TComparer>(
            IList<T> array,
            int left,
            int right,
            TComparer comparer,
            int depthLimit
        )
            where TComparer : IComparer<T>
        {
            while (left < right)
            {
                int length = right - left + 1;
                if (length <= 32)
                {
                    InsertionSortRange(array, left, right, comparer);
                    return;
                }

                if (depthLimit == 0)
                {
                    HeapSortRange(array, left, right, comparer);
                    return;
                }

                depthLimit--;
                int pivotIndex = SkaSelectPivot(array, left, right, comparer);
                T pivot = array[pivotIndex];
                (int leftEnd, int rightStart) = SkaPartition(array, left, right, pivot, comparer);

                if (leftEnd - left < right - rightStart)
                {
                    if (left < leftEnd)
                    {
                        SkaSortRange(array, left, leftEnd, comparer, depthLimit);
                    }
                    left = rightStart;
                }
                else
                {
                    if (rightStart < right)
                    {
                        SkaSortRange(array, rightStart, right, comparer, depthLimit);
                    }
                    right = leftEnd;
                }
            }
        }

        private static int SkaSelectPivot<T, TComparer>(
            IList<T> array,
            int left,
            int right,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int length = right - left + 1;
            if (length < 64)
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

        private static (int leftEnd, int rightStart) SkaPartition<T, TComparer>(
            IList<T> array,
            int left,
            int right,
            T pivot,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int lt = left;
            int i = left;
            int gt = right;

            while (i <= gt)
            {
                int compare = comparer.Compare(array[i], pivot);
                if (compare < 0)
                {
                    array.Swap(i, lt);
                    lt++;
                    i++;
                }
                else if (compare > 0)
                {
                    array.Swap(i, gt);
                    gt--;
                }
                else
                {
                    i++;
                }
            }

            return (lt - 1, gt + 1);
        }
    }
}
