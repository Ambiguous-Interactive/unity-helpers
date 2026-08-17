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
        /// Sorts the elements in the list using the Meteor Sort algorithm, a gap-sequence-based hybrid sort.
        /// </summary>
        /// <remarks>
        /// Implementation reference: Meteor Sort by Will Stafford Parsons (wileylooper/meteorsort, repository offline).
        /// Note: Meteor Sort is currently not stable.
        /// </remarks>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <typeparam name="TComparer">The type of comparer.</typeparam>
        /// <param name="array">The list to sort.</param>
        /// <param name="comparer">The comparer to use for element comparisons.</param>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if array is null. Comparer behavior depends on implementation.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>Performance: O(n log n) average case using adaptive gap reductions.</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Not a stable sort - equal elements may be reordered.</para>
        /// </remarks>
        public static void MeteorSort<T, TComparer>(this IList<T> array, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int length = array.Count;
            int gap = length;

            int i;
            int j;
            while (gap > 15)
            {
                gap = (gap >> 2) - (gap >> 4) + (gap >> 3);
                i = gap;

                while (i < length)
                {
                    T element = array[i];
                    j = i;

                    while (j >= gap && 0 < comparer.Compare(array[j - gap], element))
                    {
                        array[j] = array[j - gap];
                        j -= gap;
                    }

                    array[j] = element;
                    i++;
                }
            }

            i = 1;
            gap = 0;

            while (i < length)
            {
                T element = array[i];
                j = i;

                while (j > 0 && 0 < comparer.Compare(array[gap], element))
                {
                    array[j] = array[gap];
                    j = gap;
                    gap--;
                }

                array[j] = element;
                gap = i;
                i++;
            }
        }
    }
}
