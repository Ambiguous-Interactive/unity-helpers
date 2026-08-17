// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is Pattern-Defeating Quicksort (pdqsort), by Orson Peters, zlib License,
// https://github.com/orlp/pdqsort. This is an adaptation of that work; the design is the original
// author's. See docs/project/third-party-notices.md.

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;

    public static partial class IListExtensions
    {
        /// <summary>
        /// Sorts the elements in the list using pattern-defeating quicksort, an adaptive quicksort variant with
        /// introspective fallbacks and pattern detection.
        /// </summary>
        /// <remarks>
        /// Implementation reference: Pattern-Defeating Quicksort by Orson Peters, https://github.com/orlp/pdqsort (zlib License).
        /// This is a C# adaptation that retains the pattern-detection heuristics while operating on <c>IList&lt;T&gt;</c>.
        /// PatternDefeatingQuickSort is not stable.
        /// </remarks>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <typeparam name="TComparer">The type of comparer.</typeparam>
        /// <param name="array">The list to sort.</param>
        /// <param name="comparer">The comparer to use for element comparisons.</param>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if array is null. Comparer behavior depends on implementation.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>Performance: O(n log n) on average with protection against quadratic worst cases via heapsort fallback.</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Not a stable sort - equal elements may be reordered.</para>
        /// </remarks>
        public static void PatternDefeatingQuickSort<T, TComparer>(
            this IList<T> array,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int count = array.Count;
            if (count < 2)
            {
                return;
            }

            int depthLimit = 2 * FloorLog2(count);
            PatternDefeatingQuickSortRange(array, 0, count - 1, comparer, depthLimit);
        }

        private static void PatternDefeatingQuickSortRange<T, TComparer>(
            IList<T> array,
            int left,
            int right,
            TComparer comparer,
            int depthLimit
        )
            where TComparer : IComparer<T>
        {
            const int insertionThreshold = 16;
            while (right - left > insertionThreshold)
            {
                if (depthLimit == 0)
                {
                    HeapSortRange(array, left, right, comparer);
                    return;
                }

                int pivotIndex = SelectPivotIndex(array, left, right, comparer);
                (int pivotStart, int pivotEnd, bool swapped) = PartitionRange(
                    array,
                    left,
                    right,
                    pivotIndex,
                    comparer
                );

                if (!swapped && IsRangeSorted(array, left, right, comparer))
                {
                    return;
                }

                depthLimit--;

                int leftSize = pivotStart - left;
                int rightSize = right - pivotEnd;

                if (leftSize < rightSize)
                {
                    if (leftSize > 0)
                    {
                        PatternDefeatingQuickSortRange(
                            array,
                            left,
                            pivotStart - 1,
                            comparer,
                            depthLimit
                        );
                    }
                    left = pivotEnd + 1;
                }
                else
                {
                    if (rightSize > 0)
                    {
                        PatternDefeatingQuickSortRange(
                            array,
                            pivotEnd + 1,
                            right,
                            comparer,
                            depthLimit
                        );
                    }
                    right = pivotStart - 1;
                }
            }

            InsertionSortRange(array, left, right, comparer);
        }

        private static (int pivotStart, int pivotEnd, bool swapped) PartitionRange<T, TComparer>(
            IList<T> array,
            int left,
            int right,
            int pivotIndex,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            array.Swap(left, pivotIndex);
            T pivot = array[left];
            int i = left + 1;
            int j = right;
            bool swapped = false;

            while (i <= j)
            {
                while (i <= j && comparer.Compare(array[i], pivot) < 0)
                {
                    i++;
                }

                while (i <= j && comparer.Compare(array[j], pivot) > 0)
                {
                    j--;
                }

                if (i > j)
                {
                    break;
                }

                if (i < j)
                {
                    array.Swap(i, j);
                    swapped = true;
                }

                i++;
                j--;
            }

            int pivotPosition = j;
            array.Swap(left, pivotPosition);

            int pivotStart = pivotPosition;
            int pivotEnd = pivotPosition;

            while (pivotStart > left && comparer.Compare(array[pivotStart - 1], pivot) == 0)
            {
                pivotStart--;
            }

            while (pivotEnd < right && comparer.Compare(array[pivotEnd + 1], pivot) == 0)
            {
                pivotEnd++;
            }

            return (pivotStart, pivotEnd, swapped || pivotIndex != pivotPosition);
        }
    }
}
