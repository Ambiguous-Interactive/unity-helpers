// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is greeNsort, by Jens Oehlschlägel, https://www.greensort.org. This is
// an adaptation of that work; the design is the original author's. The upstream license is not
// recorded in this repository. See docs/project/third-party-notices.md.

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using Utils;

    public static partial class IListExtensions
    {
        /// <summary>
        /// Sorts the list using GreenSort, a stable symmetric mergesort that trims already ordered prefixes and suffixes.
        /// </summary>
        /// <remarks>
        /// Implementation reference: greeNsort (Jens Oehlschlägel) — https://www.greensort.org.
        /// This adaptation uses symmetric trimming before merging halves to reduce data movement.
        /// </remarks>
        public static void GreenSort<T, TComparer>(this IList<T> list, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int count = list.Count;
            if (count < 2)
            {
                return;
            }

            if (list is T[] array)
            {
                GreenSortCore(array, count, comparer);
                return;
            }

            using PooledArray<T> scratchLease = SystemArrayPool<T>.Get(count, out T[] scratch);
            list.CopyTo(scratch, 0);
            GreenSortCore(scratch, count, comparer);
            for (int i = 0; i < count; i++)
            {
                list[i] = scratch[i];
            }
        }

        private static void GreenSortCore<T, TComparer>(T[] array, int count, TComparer comparer)
            where TComparer : IComparer<T>
        {
            using PooledArray<T> bufferLease = SystemArrayPool<T>.Get(
                Math.Max(count / 2 + 1, 64),
                out T[] buffer
            );
            GreenSortRange(array, buffer, 0, count - 1, comparer);
        }

        private static void GreenSortRange<T, TComparer>(
            T[] array,
            T[] buffer,
            int left,
            int right,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            if (left >= right)
            {
                return;
            }

            int length = right - left + 1;
            if (length <= 32)
            {
                InsertionSortRange(array, left, right, comparer);
                return;
            }

            int mid = left + ((right - left) >> 1);
            GreenSortRange(array, buffer, left, mid, comparer);
            GreenSortRange(array, buffer, mid + 1, right, comparer);
            GreenSymmetricMerge(array, buffer, left, mid, right, comparer);
        }

        private static void GreenSymmetricMerge<T, TComparer>(
            T[] array,
            T[] buffer,
            int left,
            int mid,
            int right,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            if (comparer.Compare(array[mid], array[mid + 1]) <= 0)
            {
                return;
            }

            int leftTrim = left;
            while (leftTrim <= mid && comparer.Compare(array[leftTrim], array[mid + 1]) <= 0)
            {
                leftTrim++;
            }

            int rightTrim = right;
            while (rightTrim >= mid + 1 && comparer.Compare(array[mid], array[rightTrim]) <= 0)
            {
                rightTrim--;
            }

            if (leftTrim > mid || rightTrim < mid + 1)
            {
                return;
            }

            MergeRuns(
                array,
                buffer,
                leftTrim,
                mid - leftTrim + 1,
                mid + 1,
                rightTrim - mid,
                comparer
            );
        }
    }
}
