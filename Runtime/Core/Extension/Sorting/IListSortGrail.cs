// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is Grail Sort, by Mrrl (Andrey Astrelin), MIT License,
// https://github.com/Mrrl/GrailSort. This is an adaptation of that work; the design is the original
// author's. See docs/project/third-party-notices.md.

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using Utils;

    public static partial class IListExtensions
    {
        /// <summary>
        /// Sorts the elements in the list using the Grail Sort algorithm, a stable mergesort that adapts buffer usage.
        /// </summary>
        /// <remarks>
        /// Implementation reference: Grail Sort by Mrrl (MIT License), https://github.com/Mrrl/GrailSort.
        /// This adaptation uses pooled buffers instead of manual block buffers while keeping stability.
        /// </remarks>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <typeparam name="TComparer">The type of comparer.</typeparam>
        /// <param name="list">The list to sort.</param>
        /// <param name="comparer">The comparer to use for element comparisons.</param>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if array is null. Comparer behavior depends on implementation.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>Performance: O(n log n) worst/average case. Stable sort.</para>
        /// <para>Allocations: A list that is already a <c>T[]</c> is sorted in place; any other <see cref="IList{T}"/> is copied through one pooled buffer of the list's length and copied back. The merge itself uses a further pooled buffer of half that length.</para>
        /// <para>Edge cases: Empty or single element lists require no sorting.</para>
        /// </remarks>
        public static void GrailSort<T, TComparer>(this IList<T> list, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int count = list.Count;
            if (count < 2)
            {
                return;
            }

            if (list is T[] array)
            {
                GrailSortCore(array, count, comparer);
                return;
            }

            using PooledArray<T> scratchLease = SystemArrayPool<T>.Get(count, out T[] scratch);
            list.CopyTo(scratch, 0);
            GrailSortCore(scratch, count, comparer);
            for (int i = 0; i < count; i++)
            {
                list[i] = scratch[i];
            }
        }

        private static void GrailSortCore<T, TComparer>(T[] array, int count, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int bufferLength = count / 2 + 1;
            using PooledArray<T> bufferLease = SystemArrayPool<T>.Get(bufferLength, out T[] buffer);
            GrailSortRange(array, buffer, 0, count - 1, comparer);
        }

        private static void GrailSortRange<T, TComparer>(
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

            int mid = left + ((right - left) >> 1);
            GrailSortRange(array, buffer, left, mid, comparer);
            GrailSortRange(array, buffer, mid + 1, right, comparer);

            if (comparer.Compare(array[mid], array[mid + 1]) <= 0)
            {
                return;
            }

            MergeRuns(array, buffer, left, mid - left + 1, mid + 1, right - mid, comparer);
        }
    }
}
