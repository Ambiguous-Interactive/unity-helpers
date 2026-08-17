// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics.Contracts;
    using Utils;

    public static partial class IListExtensions
    {
        /// <summary>
        /// Sorts the elements in the list using the specified comparer and sort algorithm.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <typeparam name="TComparer">The type of comparer.</typeparam>
        /// <param name="array">The list to sort.</param>
        /// <param name="comparer">The comparer to use for element comparisons.</param>
        /// <param name="sortAlgorithm">
        /// The sorting algorithm to use (Ghost, Meteor, PatternDefeatingQuickSort, Grail, Power, or Insertion).
        /// Defaults to Grail.
        /// </param>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if array is null. Comparer behavior depends on implementation.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>
        /// Performance: Ghost, Meteor, PatternDefeatingQuickSort, Grail, and Power sorts are O(n log n) on average.
        /// Insertion sort is O(n^2) worst/average case.
        /// </para>
        /// <para>Allocations: No allocations.</para>
        /// <para>
        /// Edge cases: Empty or single element lists require no sorting. Ghost, Meteor, and PatternDefeatingQuickSort
        /// are currently not stable. Grail and Power sorts are stable.
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidEnumArgumentException">Thrown when sortAlgorithm is not a valid SortAlgorithm value.</exception>
        public static void Sort<T, TComparer>(
            this IList<T> array,
            TComparer comparer,
            SortAlgorithm sortAlgorithm = SortAlgorithm.Grail
        )
            where TComparer : IComparer<T>
        {
            switch (sortAlgorithm)
            {
                case SortAlgorithm.Ghost:
                {
                    array.GhostSort(comparer);
                    return;
                }
                case SortAlgorithm.Insertion:
                {
                    array.InsertionSort(comparer);
                    return;
                }
                case SortAlgorithm.Meteor:
                {
                    array.MeteorSort(comparer);
                    return;
                }
                case SortAlgorithm.PatternDefeatingQuickSort:
                {
                    array.PatternDefeatingQuickSort(comparer);
                    return;
                }
                case SortAlgorithm.Grail:
                {
                    array.GrailSort(comparer);
                    return;
                }
                case SortAlgorithm.Power:
                {
                    array.PowerSort(comparer);
                    return;
                }
                case SortAlgorithm.Tim:
                {
                    array.TimSort(comparer);
                    return;
                }
                case SortAlgorithm.Jesse:
                {
                    array.JesseSort(comparer);
                    return;
                }
                case SortAlgorithm.Green:
                {
                    array.GreenSort(comparer);
                    return;
                }
                case SortAlgorithm.Ska:
                {
                    array.SkaSort(comparer);
                    return;
                }
                case SortAlgorithm.Ipn:
                {
                    array.IpnSort(comparer);
                    return;
                }
                case SortAlgorithm.Smooth:
                {
                    array.SmoothSort(comparer);
                    return;
                }
                case SortAlgorithm.Block:
                {
                    array.BlockMergeSort(comparer);
                    return;
                }
                case SortAlgorithm.Ips4o:
                {
                    array.Ips4oSort(comparer);
                    return;
                }
                case SortAlgorithm.PowerPlus:
                {
                    array.PowerSortPlus(comparer);
                    return;
                }
                case SortAlgorithm.Glide:
                {
                    array.GlideSort(comparer);
                    return;
                }
                case SortAlgorithm.Flux:
                {
                    array.FluxSort(comparer);
                    return;
                }
                case SortAlgorithm.Yam:
                {
                    array.YamSort(comparer);
                    return;
                }
                default:
                {
                    throw new InvalidEnumArgumentException(
                        nameof(sortAlgorithm),
                        (int)sortAlgorithm,
                        typeof(SortAlgorithm)
                    );
                }
            }
        }

        /// <summary>
        /// Sorts a list of Unity Objects by their name property in ascending alphabetical order.
        /// </summary>
        /// <typeparam name="T">The type of Unity Object.</typeparam>
        /// <param name="inputList">The list to sort.</param>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if inputList is null.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. Requires Unity main thread for Object.name access.</para>
        /// <para>Performance: O(n log n) - delegates to Array.Sort or List.Sort when possible for optimized performance.</para>
        /// <para>Allocations: Minimal - uses cached UnityObjectNameComparer.Instance.</para>
        /// <para>Edge cases: Empty or single element lists require no sorting. Null objects may cause exceptions depending on comparer.</para>
        /// </remarks>
        public static void SortByName<T>(this IList<T> inputList)
            where T : UnityEngine.Object
        {
            switch (inputList)
            {
                case T[] array:
                {
                    Array.Sort(array, UnityObjectNameComparer<T>.Instance);
                    return;
                }
                case List<T> list:
                {
                    list.Sort(UnityObjectNameComparer<T>.Instance);
                    return;
                }
                default:
                {
                    inputList.Sort(UnityObjectNameComparer<T>.Instance);
                    break;
                }
            }
        }

        /// <summary>
        /// Determines whether the list is sorted in ascending order according to the specified comparer.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to check.</param>
        /// <param name="comparer">The comparer to use. If null, uses Comparer&lt;T&gt;.Default.</param>
        /// <returns>True if the list is sorted in ascending order, false otherwise.</returns>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if list is null. Comparer defaults to Comparer&lt;T&gt;.Default if null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of elements. Short-circuits on first unsorted pair.</para>
        /// <para>Allocations: No allocations if comparer is provided, otherwise allocates default comparer.</para>
        /// <para>Edge cases: Empty lists and single element lists are considered sorted.</para>
        /// </remarks>
        [Pure]
        public static bool IsSorted<T>(this IList<T> list, IComparer<T> comparer = null)
        {
            if (list.Count <= 1)
            {
                return true;
            }

            comparer ??= Comparer<T>.Default;

            T previous = list[0];
            for (int i = 1; i < list.Count; ++i)
            {
                T current = list[i];
                if (comparer.Compare(previous, current) > 0)
                {
                    return false;
                }

                previous = current;
            }

            return true;
        }
    }
}
