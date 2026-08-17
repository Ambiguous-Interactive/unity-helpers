// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;

    public static partial class IListExtensions
    {
        /// <summary>
        /// Sorts the elements in the list using insertion sort algorithm.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <typeparam name="TComparer">The type of comparer.</typeparam>
        /// <param name="array">The list to sort.</param>
        /// <param name="comparer">The comparer to use for element comparisons.</param>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if array is null. Comparer behavior depends on implementation.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>Performance: O(n^2) worst/average case, O(n) best case when nearly sorted. Stable sort.</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Efficient for small or nearly sorted lists. Empty or single element lists require no sorting.</para>
        /// </remarks>
        public static void InsertionSort<T, TComparer>(this IList<T> array, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int arrayCount = array.Count;
            if (arrayCount < 2)
            {
                return;
            }

            InsertionSortRange(array, 0, arrayCount - 1, comparer);
        }
    }
}
