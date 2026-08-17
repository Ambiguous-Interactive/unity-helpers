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
        /// Sorts the elements in the list using the Power Sort algorithm, which exploits existing runs and merges them
        /// in near-optimal order.
        /// </summary>
        /// <remarks>
        /// Implementation reference: PowerSort (Munro & Wild) — adaptive mergesort leveraging natural runs.
        /// https://arxiv.org/abs/1805.04154 (CC BY 4.0). This adaptation detects runs and merges them with pooled buffers.
        /// </remarks>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <typeparam name="TComparer">The type of comparer.</typeparam>
        /// <param name="array">The list to sort.</param>
        /// <param name="comparer">The comparer to use for element comparisons.</param>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if array is null. Comparer behavior depends on implementation.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>Performance: O(n log n) worst/average case, approaching O(n) on partially sorted data. Stable sort.</para>
        /// <para>Allocations: Uses pooled lists and buffers for run management and merging.</para>
        /// <para>Edge cases: Empty or single element lists require no sorting.</para>
        /// </remarks>
        public static void PowerSort<T, TComparer>(this IList<T> array, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int count = array.Count;
            if (count < 2)
            {
                return;
            }

            using PooledResource<List<(int start, int length)>> runBuffer = Buffers<(
                int start,
                int length
            )>.List.Get(out List<(int start, int length)> runs);
            using PooledResource<List<(int start, int length)>> mergeBuffer = Buffers<(
                int start,
                int length
            )>.List.Get(out List<(int start, int length)> mergedRuns);

            CollectNaturalRuns(array, comparer, runs);
            if (runs.Count <= 1)
            {
                return;
            }

            int bufferLength = count / 2 + 1;
            using PooledArray<T> tempLease = SystemArrayPool<T>.Get(bufferLength, out T[] buffer);

            while (runs.Count > 1)
            {
                mergedRuns.Clear();
                int runCount = runs.Count;
                for (int i = 0; i < runCount; i += 2)
                {
                    if (i + 1 >= runCount)
                    {
                        mergedRuns.Add(runs[i]);
                        continue;
                    }

                    (int start, int length) leftRun = runs[i];
                    (int start, int length) rightRun = runs[i + 1];
                    MergeRuns(
                        array,
                        buffer,
                        leftRun.start,
                        leftRun.length,
                        rightRun.start,
                        rightRun.length,
                        comparer
                    );
                    mergedRuns.Add((leftRun.start, leftRun.length + rightRun.length));
                }

                (runs, mergedRuns) = (mergedRuns, runs);
            }
        }
    }
}
