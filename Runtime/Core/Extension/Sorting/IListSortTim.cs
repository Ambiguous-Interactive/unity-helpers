// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is TimSort, by Tim Peters, described by him at
// https://bugs.python.org/file4451/timsort.txt. This is an independent implementation of that
// published description -- the run-management helpers carry the names the description gives them --
// and the design is the original author's. See docs/project/third-party-notices.md.

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using Utils;

    public static partial class IListExtensions
    {
        /// <summary>
        /// Sorts the list using TimSort, a hybrid stable sort that detects natural runs and merges them adaptively.
        /// </summary>
        /// <remarks>
        /// Implementation reference: TimSort by Tim Peters, from his own description at
        /// https://bugs.python.org/file4451/timsort.txt.
        /// </remarks>
        public static void TimSort<T, TComparer>(this IList<T> list, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int count = list.Count;
            if (count < 2)
            {
                return;
            }

            if (list is T[] array)
            {
                TimSortCore(array, count, comparer);
                return;
            }

            using PooledArray<T> scratchLease = SystemArrayPool<T>.Get(count, out T[] scratch);
            list.CopyTo(scratch, 0);
            TimSortCore(scratch, count, comparer);
            WriteBack(list, scratch, count);
        }

        private static void TimSortCore<T, TComparer>(T[] array, int count, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int minRun = ComputeTimSortMinRun(count);
            using PooledArray<T> bufferLease = SystemArrayPool<T>.Get(
                Math.Max(count / 2 + 1, 32),
                out T[] buffer
            );
            using PooledResource<List<(int start, int length)>> runStackLease = Buffers<(
                int start,
                int length
            )>.List.Get(out List<(int start, int length)> runStack);
            runStack.Clear();

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

                runStack.Add((index, runLength));
                TimSortMergeCollapse(array, comparer, buffer, runStack);
                index += runLength;
            }

            TimSortMergeForce(array, comparer, buffer, runStack);
        }

        private static int ComputeTimSortMinRun(int length)
        {
            int remainder = 0;
            while (64 <= length)
            {
                remainder |= length & 1;
                length >>= 1;
            }

            return length + remainder;
        }

        private static void TimSortMergeCollapse<T, TComparer>(
            T[] array,
            TComparer comparer,
            T[] buffer,
            List<(int start, int length)> runStack
        )
            where TComparer : IComparer<T>
        {
            while (1 < runStack.Count)
            {
                int size = runStack.Count;
                if (3 <= size)
                {
                    (int startA, int lengthA) = runStack[size - 3];
                    (int startB, int lengthB) = runStack[size - 2];
                    (int startC, int lengthC) = runStack[size - 1];
                    if (lengthA <= lengthB + lengthC || lengthB <= lengthC)
                    {
                        if (lengthA < lengthC)
                        {
                            TimSortMergeAt(array, comparer, buffer, runStack, size - 3);
                        }
                        else
                        {
                            TimSortMergeAt(array, comparer, buffer, runStack, size - 2);
                        }
                        continue;
                    }
                }

                (int prevStart, int prevLength) = runStack[size - 2];
                (int lastStart, int lastLength) = runStack[size - 1];
                if (prevLength <= lastLength)
                {
                    TimSortMergeAt(array, comparer, buffer, runStack, size - 2);
                    continue;
                }

                break;
            }
        }

        private static void TimSortMergeForce<T, TComparer>(
            T[] array,
            TComparer comparer,
            T[] buffer,
            List<(int start, int length)> runStack
        )
            where TComparer : IComparer<T>
        {
            while (1 < runStack.Count)
            {
                int size = runStack.Count;
                int index = size - 2;
                if (3 <= size && runStack[size - 3].length < runStack[size - 1].length)
                {
                    index = size - 3;
                }

                TimSortMergeAt(array, comparer, buffer, runStack, index);
            }
        }

        private static void TimSortMergeAt<T, TComparer>(
            T[] array,
            TComparer comparer,
            T[] buffer,
            List<(int start, int length)> runStack,
            int index
        )
            where TComparer : IComparer<T>
        {
            (int leftStart, int leftLength) = runStack[index];
            (int rightStart, int rightLength) = runStack[index + 1];
            MergeRuns(array, buffer, leftStart, leftLength, rightStart, rightLength, comparer);
            runStack[index] = (leftStart, leftLength + rightLength);
            runStack.RemoveAt(index + 1);
        }
    }
}
