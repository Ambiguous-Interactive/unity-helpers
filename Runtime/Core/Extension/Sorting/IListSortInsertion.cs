// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using Utils;

    public static partial class IListExtensions
    {
        /// <summary>
        /// Sorts the elements in the list using insertion sort algorithm.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <typeparam name="TComparer">The type of comparer.</typeparam>
        /// <param name="list">The list to sort.</param>
        /// <param name="comparer">The comparer to use for element comparisons.</param>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if array is null. Comparer behavior depends on implementation.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>Performance: O(n^2) worst/average case, O(n) best case when nearly sorted. Stable sort.</para>
        /// <para>Allocations: A list that is already a <c>T[]</c> is sorted in place; any other <see cref="IList{T}"/> is copied through one pooled buffer of the list's length and copied back.</para>
        /// <para>Edge cases: Efficient for small or nearly sorted lists. Empty or single element lists require no sorting.</para>
        /// </remarks>
        public static void InsertionSort<T, TComparer>(this IList<T> list, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int count = list.Count;
            if (count < 2)
            {
                return;
            }

            if (list is T[] array)
            {
                InsertionSortCore(array, count, comparer);
                return;
            }

            using PooledArray<T> scratchLease = SystemArrayPool<T>.Get(count, out T[] scratch);
            list.CopyTo(scratch, 0);
            InsertionSortCore(scratch, count, comparer);
            WriteBackSorted(list, scratch, count);
        }

        private static void InsertionSortCore<T, TComparer>(
            T[] array,
            int count,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            InsertionSortRange(array, 0, count - 1, comparer);
        }
    }
}
