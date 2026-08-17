// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is WikiSort (block merge sort), by Mike McFadden (BonzaiThePenguin),
// Public Domain, https://github.com/BonzaiThePenguin/WikiSort. This is an adaptation of that work;
// the design is the original author's. See docs/project/third-party-notices.md.

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using Utils;

    public static partial class IListExtensions
    {
        /// <summary>
        /// Sorts the list using block-based stable merges backed by a pooled buffer.
        /// </summary>
        /// <remarks>
        /// Implementation reference: WikiSort / block merge sort by Mike McFadden (BonzaiThePenguin, public domain),
        /// https://github.com/BonzaiThePenguin/WikiSort. This adaptation uses a pooled full-size buffer.
        /// </remarks>
        public static void BlockMergeSort<T, TComparer>(this IList<T> list, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int count = list.Count;
            if (count < 2)
            {
                return;
            }

            if (list is T[] array)
            {
                BlockMergeSortCore(array, count, comparer);
                return;
            }

            using PooledArray<T> scratchLease = SystemArrayPool<T>.Get(count, out T[] scratch);
            list.CopyTo(scratch, 0);
            BlockMergeSortCore(scratch, count, comparer);
            WriteBackSorted(list, scratch, count);
        }

        private static void BlockMergeSortCore<T, TComparer>(
            T[] array,
            int count,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            using PooledArray<T> bufferLease = SystemArrayPool<T>.Get(count, out T[] buffer);
            int runSize = 1;
            while (runSize < count)
            {
                for (int left = 0; left < count - runSize; left += runSize << 1)
                {
                    int mid = left + runSize - 1;
                    int right = Math.Min(count - 1, left + (runSize << 1) - 1);
                    BlockMerge(array, buffer, left, mid, right, comparer);
                }

                runSize <<= 1;
            }
        }

        private static void BlockMerge<T, TComparer>(
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

            for (int index = left; index <= right; ++index)
            {
                buffer[index] = array[index];
            }

            int leftIndex = left;
            int rightIndex = mid + 1;
            int dest = left;

            while (leftIndex <= mid && rightIndex <= right)
            {
                if (comparer.Compare(buffer[leftIndex], buffer[rightIndex]) <= 0)
                {
                    array[dest++] = buffer[leftIndex++];
                }
                else
                {
                    array[dest++] = buffer[rightIndex++];
                }
            }

            while (leftIndex <= mid)
            {
                array[dest++] = buffer[leftIndex++];
            }

            while (rightIndex <= right)
            {
                array[dest++] = buffer[rightIndex++];
            }
        }
    }
}
