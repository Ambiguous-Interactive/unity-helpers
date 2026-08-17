// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is Glidesort, from the sort-research-rs project by Orson Peters and
// Lukas Bergdoll (Voultapher), Apache License 2.0 / MIT License (dual-licensed),
// https://github.com/Voultapher/sort-research-rs. This is an adaptation of that work; the design is
// the original authors'. See docs/project/third-party-notices.md.

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using Utils;

    public static partial class IListExtensions
    {
        private const int GlideSortMinRun = 32;

        private const int GlideGallopTrigger = 7;

        /// <summary>
        /// Sorts the list using GlideSort, a stable mergesort that glides runs together with galloping.
        /// </summary>
        /// <remarks>
        /// Implementation reference: Glidesort (Rust std::sort write-up by Orson Peters & Sebastian W.),
        /// https://github.com/Voultapher/sort-research-rs.
        /// </remarks>
        public static void GlideSort<T, TComparer>(this IList<T> list, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int count = list.Count;
            if (count < 2)
            {
                return;
            }

            if (list is T[] array)
            {
                GlideSortCore(array, count, comparer);
                return;
            }

            using PooledArray<T> scratchLease = SystemArrayPool<T>.Get(count, out T[] scratch);
            list.CopyTo(scratch, 0);
            GlideSortCore(scratch, count, comparer);
            WriteBack(list, scratch, count);
        }

        private static void GlideSortCore<T, TComparer>(T[] array, int count, TComparer comparer)
            where TComparer : IComparer<T>
        {
            using PooledArray<T> bufferLease = SystemArrayPool<T>.Get(count, out T[] buffer);
            using PooledResource<List<(int start, int length)>> runsLease = Buffers<(
                int start,
                int length
            )>.List.Get(out List<(int start, int length)> runs);
            using PooledResource<List<(int start, int length)>> nextRunsLease = Buffers<(
                int start,
                int length
            )>.List.Get(out List<(int start, int length)> nextRuns);

            int minRun = Math.Max(GlideSortMinRun, ComputeTimSortMinRun(count));
            int index = 0;
            while (index < count)
            {
                int runLength = MakeAscendingRun(array, index, count, comparer);
                int forcedLength = Math.Max(runLength, minRun);
                int targetEnd = Math.Min(count, index + forcedLength);
                if (runLength < forcedLength)
                {
                    InsertionSortRange(array, index, targetEnd - 1, comparer);
                    runLength = targetEnd - index;
                }

                runs.Add((index, runLength));
                index += runLength;
            }

            if (runs.Count <= 1)
            {
                return;
            }

            while (runs.Count > 1)
            {
                nextRuns.Clear();
                int i = 0;
                while (i < runs.Count)
                {
                    if (i + 1 >= runs.Count)
                    {
                        nextRuns.Add(runs[i]);
                        break;
                    }

                    (int leftStart, int leftLength) = runs[i];
                    (int rightStart, int rightLength) = runs[i + 1];
                    GlideMergeRuns(
                        array,
                        buffer,
                        leftStart,
                        leftLength,
                        rightStart,
                        rightLength,
                        comparer
                    );
                    nextRuns.Add((leftStart, leftLength + rightLength));
                    i += 2;
                }

                (runs, nextRuns) = (nextRuns, runs);
            }
        }

        private static void GlideMergeRuns<T, TComparer>(
            T[] array,
            T[] buffer,
            int leftStart,
            int leftLength,
            int rightStart,
            int rightLength,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            if (leftLength == 0 || rightLength == 0)
            {
                return;
            }

            int leftEnd = leftStart + leftLength - 1;
            if (comparer.Compare(array[leftEnd], array[rightStart]) <= 0)
            {
                return;
            }

            for (int i = 0; i < leftLength; ++i)
            {
                buffer[i] = array[leftStart + i];
            }

            int leftIndex = 0;
            int rightIndex = rightStart;
            int dest = leftStart;
            int leftRemaining = leftLength;
            int rightRemaining = rightLength;
            int leftWins = 0;
            int rightWins = 0;
            T[] leftBuffer = buffer;

            while (leftRemaining > 0 && rightRemaining > 0)
            {
                if (comparer.Compare(leftBuffer[leftIndex], array[rightIndex]) <= 0)
                {
                    array[dest++] = leftBuffer[leftIndex++];
                    leftRemaining--;
                    leftWins++;
                    rightWins = 0;
                }
                else
                {
                    array[dest++] = array[rightIndex++];
                    rightRemaining--;
                    rightWins++;
                    leftWins = 0;
                }

                if (leftWins >= GlideGallopTrigger && rightRemaining > 0)
                {
                    leftWins = 0;
                    T key = array[rightIndex];
                    int advance = GlideGallopRight(
                        leftBuffer,
                        leftIndex,
                        leftRemaining,
                        key,
                        comparer
                    );
                    if (advance > 0)
                    {
                        for (int k = 0; k < advance; ++k)
                        {
                            array[dest++] = leftBuffer[leftIndex++];
                        }
                        leftRemaining -= advance;
                        if (leftRemaining == 0)
                        {
                            break;
                        }
                    }
                }
                else if (rightWins >= GlideGallopTrigger && leftRemaining > 0)
                {
                    rightWins = 0;
                    T key = leftBuffer[leftIndex];
                    int advance = GlideGallopLeft(array, rightIndex, rightRemaining, key, comparer);
                    if (advance > 0)
                    {
                        for (int k = 0; k < advance; ++k)
                        {
                            array[dest++] = array[rightIndex++];
                        }
                        rightRemaining -= advance;
                        if (rightRemaining == 0)
                        {
                            break;
                        }
                    }
                }
            }

            while (leftRemaining > 0)
            {
                array[dest++] = leftBuffer[leftIndex++];
                leftRemaining--;
            }
        }

        private static int GlideGallopLeft<T, TComparer>(
            T[] list,
            int start,
            int length,
            T key,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int low = 0;
            int high = length;
            while (low < high)
            {
                int mid = low + ((high - low) >> 1);
                if (comparer.Compare(list[start + mid], key) < 0)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }

        private static int GlideGallopRight<T, TComparer>(
            T[] list,
            int start,
            int length,
            T key,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int low = 0;
            int high = length;
            while (low < high)
            {
                int mid = low + ((high - low) >> 1);
                if (comparer.Compare(key, list[start + mid]) < 0)
                {
                    high = mid;
                }
                else
                {
                    low = mid + 1;
                }
            }

            return low;
        }
    }
}
