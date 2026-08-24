// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is YamSort, Copyright (c) 2026 Gary Gende, MIT License,
// https://github.com/gendeg/YamSort. This is an adaptation of that work; the design is the
// original author's. See docs/project/third-party-notices.md.

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System.Collections.Generic;
    using Utils;

    public static partial class IListExtensions
    {
        private const int YamGallopThreshold = 5;
        private const int YamSequenceScoreDefault = 4;
        private const int YamSequenceScoreMax = 8;
        private const int YamSequenceScoreMin = 0;

        private static readonly int[] YamSequenceThresholds = { 3, 6, 10, 10, 10, 10, 10, 15, 24 };

        /// <summary>
        /// Sorts the list using YamSort, a stable top-down bisection mergesort that adapts its insertion-sort
        /// threshold and its merge strategy to how sequential the data turns out to be.
        /// </summary>
        /// <remarks>
        /// Implementation reference: YamSort by Gary Gende, https://github.com/gendeg/YamSort (MIT License).
        /// This adaptation replaces the upstream <c>Span</c> and <c>IComparable</c> surface with
        /// <see cref="IList{T}"/> and an explicit comparer, and drops the upstream primitive-type comparison
        /// shortcuts so a caller-supplied comparer is always honoured. NaN ordering is whatever the supplied
        /// comparer says it is; upstream partitions NaNs out because it compares through <c>IComparable</c>.
        /// </remarks>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <typeparam name="TComparer">The type of comparer.</typeparam>
        /// <param name="list">The list to sort.</param>
        /// <param name="comparer">The comparer to use for element comparisons.</param>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if array is null. Comparer behavior depends on implementation.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>Performance: O(n log n) worst/average case, O(n) on sorted or reverse-sorted input. Stable sort.</para>
        /// <para>Allocations: A list that is already a <c>T[]</c> is sorted in place; any other <see cref="IList{T}"/> is copied through one pooled buffer of the list's length and copied back. The merge itself uses a further pooled buffer of half that length.</para>
        /// <para>Edge cases: Empty or single element lists require no sorting.</para>
        /// </remarks>
        public static void YamSort<T, TComparer>(this IList<T> list, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int count = list.Count;
            if (count < 2)
            {
                return;
            }

            if (list is T[] array)
            {
                YamSortCore(array, count, comparer);
                return;
            }

            using PooledArray<T> scratchLease = SystemArrayPool<T>.Get(count, out T[] scratch);
            list.CopyTo(scratch, 0);
            YamSortCore(scratch, count, comparer);
            WriteBack(list, scratch, count);
        }

        private static void YamSortCore<T, TComparer>(T[] array, int count, TComparer comparer)
            where TComparer : IComparer<T>
        {
            YamSortState state = new(YamSequenceScoreDefault);
            if (count <= YamSequenceThresholds[state.sequenceScore])
            {
                InsertionSortRange(array, 0, count - 1, comparer);
                return;
            }

            using PooledArray<T> bufferLease = SystemArrayPool<T>.Get(
                (count >> 1) + 2,
                out T[] buffer
            );
            YamSortRange(array, 0, count, buffer, comparer, ref state);
        }

        private static void YamSortRange<T, TComparer>(
            T[] array,
            int start,
            int length,
            T[] buffer,
            TComparer comparer,
            ref YamSortState state
        )
            where TComparer : IComparer<T>
        {
            if (length <= YamSequenceThresholds[state.sequenceScore])
            {
                InsertionSortRange(array, start, start + length - 1, comparer);
                return;
            }

            int mid = (length + 1) >> 1;
            YamSortRange(array, start, mid, buffer, comparer, ref state);
            YamSortRange(array, start + mid, length - mid, buffer, comparer, ref state);

            int last = start + length - 1;
            if (
                state.reverseSortFound
                && comparer.Compare(array[last], array[start]) < 0
                && YamRotateHalves(array, start, length, mid, buffer)
            )
            {
                if (YamSequenceScoreMin < state.sequenceScore)
                {
                    state.sequenceScore--;
                }

                return;
            }

            if (comparer.Compare(array[start + mid - 1], array[start + mid]) <= 0)
            {
                return;
            }

            if (
                state.sequenceScore == YamSequenceScoreMax
                || state.sequenceScore == YamSequenceScoreMin
            )
            {
                YamGallopMerge(array, start, length, mid, buffer, comparer, ref state);
            }
            else
            {
                YamSimpleMerge(array, start, length, mid, buffer, comparer, ref state);
            }
        }

        // Both halves are sorted and the right half's maximum is below the left half's minimum, so swapping
        // the halves finishes the range without a single further comparison.
        private static bool YamRotateHalves<T>(
            T[] array,
            int start,
            int length,
            int mid,
            T[] buffer
        )
        {
            if (buffer.Length < mid)
            {
                return false;
            }

            for (int i = 0; i < mid; i++)
            {
                buffer[i] = array[start + i];
            }

            int rightLength = length - mid;
            for (int i = 0; i < rightLength; i++)
            {
                array[start + i] = array[start + mid + i];
            }

            for (int i = 0; i < mid; i++)
            {
                array[start + rightLength + i] = buffer[i];
            }

            return true;
        }

        private static void YamSimpleMerge<T, TComparer>(
            T[] array,
            int start,
            int length,
            int mid,
            T[] buffer,
            TComparer comparer,
            ref YamSortState state
        )
            where TComparer : IComparer<T>
        {
            int leftEnd = start + mid - 1;
            int leftIndex = leftEnd;
            int rightIndex = start + length - 1;

            while (0 < comparer.Compare(array[rightIndex], array[leftIndex]))
            {
                rightIndex--;
            }

            int head = buffer.Length - 1;
            int bufferCount = YamFillBuffer(array, buffer, leftEnd + 1, rightIndex, head);

            while (leftEnd < rightIndex)
            {
                if (0 < comparer.Compare(array[leftIndex], buffer[head]))
                {
                    array[rightIndex--] = array[leftIndex--];
                }
                else
                {
                    array[rightIndex--] = buffer[head--];
                    bufferCount--;
                }
            }

            YamUpdateSequenceScore(bufferCount, length, ref state);

            if (leftIndex < start)
            {
                YamDrainBuffer(array, start, buffer, head, bufferCount);
                state.reverseSortFound = true;
                return;
            }

            while (true)
            {
                if (0 < comparer.Compare(array[leftIndex], buffer[head]))
                {
                    array[rightIndex--] = array[leftIndex--];
                    if (leftIndex < start)
                    {
                        YamDrainBuffer(array, start, buffer, head, bufferCount);
                        return;
                    }
                }
                else
                {
                    array[rightIndex--] = buffer[head--];
                    bufferCount--;
                }

                if (bufferCount == 0)
                {
                    return;
                }
            }
        }

        private static void YamGallopMerge<T, TComparer>(
            T[] array,
            int start,
            int length,
            int mid,
            T[] buffer,
            TComparer comparer,
            ref YamSortState state
        )
            where TComparer : IComparer<T>
        {
            int leftEnd = start + mid - 1;
            int leftIndex = leftEnd;
            int rightIndex = start + length - 1;

            if (0 < comparer.Compare(array[rightIndex], array[leftIndex]))
            {
                rightIndex -= YamCountAtLeast(
                    array,
                    rightIndex,
                    leftEnd + 1,
                    array[leftIndex],
                    comparer
                );
            }

            int head = buffer.Length - 1;
            int bufferCount = YamFillBuffer(array, buffer, leftEnd + 1, rightIndex, head);
            int gallopCounter = 0;
            bool lastWasLeft = true;

            while (leftEnd < rightIndex)
            {
                if (0 < comparer.Compare(array[leftIndex], buffer[head]))
                {
                    if (YamGallopThreshold <= gallopCounter)
                    {
                        int lowerBound = leftIndex - (rightIndex - leftEnd - 1);
                        int copyCount = YamCountGreater(
                            array,
                            leftIndex,
                            lowerBound,
                            buffer[head],
                            comparer
                        );
                        YamCopyDown(array, leftIndex, rightIndex, copyCount);
                        leftIndex -= copyCount;
                        rightIndex -= copyCount;
                        gallopCounter = 0;
                    }
                    else
                    {
                        array[rightIndex--] = array[leftIndex--];
                        gallopCounter = lastWasLeft ? gallopCounter + 1 : 0;
                        lastWasLeft = true;
                    }
                }
                else
                {
                    if (YamGallopThreshold <= gallopCounter)
                    {
                        int lowerBound = head + 1 - (rightIndex - leftEnd);
                        int copyCount = YamCountAtLeast(
                            buffer,
                            head,
                            lowerBound,
                            array[leftIndex],
                            comparer
                        );
                        YamCopyFromBuffer(array, rightIndex, buffer, head, copyCount);
                        head -= copyCount;
                        bufferCount -= copyCount;
                        rightIndex -= copyCount;
                        gallopCounter = 0;
                    }
                    else
                    {
                        array[rightIndex--] = buffer[head--];
                        bufferCount--;
                        gallopCounter = lastWasLeft ? 0 : gallopCounter + 1;
                        lastWasLeft = false;
                    }
                }
            }

            YamUpdateSequenceScore(bufferCount, length, ref state);

            if (leftIndex < start)
            {
                YamDrainBuffer(array, start, buffer, head, bufferCount);
                state.reverseSortFound = true;
                return;
            }

            while (0 < bufferCount)
            {
                if (0 < comparer.Compare(array[leftIndex], buffer[head]))
                {
                    if (YamGallopThreshold <= gallopCounter)
                    {
                        int copyCount = YamCountGreater(
                            array,
                            leftIndex,
                            start,
                            buffer[head],
                            comparer
                        );
                        YamCopyDown(array, leftIndex, rightIndex, copyCount);
                        leftIndex -= copyCount;
                        rightIndex -= copyCount;
                        gallopCounter = 0;
                    }
                    else
                    {
                        array[rightIndex--] = array[leftIndex--];
                        gallopCounter = lastWasLeft ? gallopCounter + 1 : 0;
                        lastWasLeft = true;
                    }

                    if (leftIndex < start)
                    {
                        YamDrainBuffer(array, start, buffer, head, bufferCount);
                        return;
                    }
                }
                else
                {
                    if (YamGallopThreshold <= gallopCounter)
                    {
                        int lowerBound = head - bufferCount + 1;
                        int copyCount = YamCountAtLeast(
                            buffer,
                            head,
                            lowerBound,
                            array[leftIndex],
                            comparer
                        );
                        YamCopyFromBuffer(array, rightIndex, buffer, head, copyCount);
                        head -= copyCount;
                        bufferCount -= copyCount;
                        rightIndex -= copyCount;
                        gallopCounter = 0;
                    }
                    else
                    {
                        array[rightIndex--] = buffer[head--];
                        bufferCount--;
                        gallopCounter = lastWasLeft ? 0 : gallopCounter + 1;
                        lastWasLeft = false;
                    }
                }
            }
        }

        // The buffer is a queue that grows downwards from its own tail, so the largest pending element of the
        // right run is always the one at head.
        private static int YamFillBuffer<T>(T[] array, T[] buffer, int from, int to, int head)
        {
            int count = to - from + 1;
            for (int i = 0; i < count; i++)
            {
                buffer[head - count + 1 + i] = array[from + i];
            }

            return count;
        }

        private static void YamDrainBuffer<T>(T[] array, int start, T[] buffer, int head, int count)
        {
            for (int i = 0; i < count; i++)
            {
                array[start + i] = buffer[head - count + 1 + i];
            }
        }

        private static void YamCopyDown<T>(T[] array, int source, int destination, int count)
        {
            for (int i = 0; i < count; i++)
            {
                array[destination - i] = array[source - i];
            }
        }

        private static void YamCopyFromBuffer<T>(
            T[] array,
            int destination,
            T[] buffer,
            int head,
            int count
        )
        {
            for (int i = 0; i < count; i++)
            {
                array[destination - i] = buffer[head - i];
            }
        }

        // Equal elements are excluded, so a run of duplicates in the left half stays put and the right half's
        // copies land above it.
        private static int YamCountGreater<T, TComparer>(
            T[] array,
            int index,
            int lowerBound,
            T target,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            return index - YamGallopBoundary(array, index, lowerBound, target, comparer, 0);
        }

        private static int YamCountAtLeast<T, TComparer>(
            T[] array,
            int index,
            int lowerBound,
            T target,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            return index - YamGallopBoundary(array, index, lowerBound, target, comparer, -1);
        }

        // Walks down from index in doubling steps, then binary searches the overshot window for the last
        // element that must stay put. The comparison floor decides whether equal elements stay or move.
        private static int YamGallopBoundary<T, TComparer>(
            T[] array,
            int index,
            int lowerBound,
            T target,
            TComparer comparer,
            int floor
        )
            where TComparer : IComparer<T>
        {
            int span = index - lowerBound + 1;
            int probe = 1;
            int count = 0;
            while (count < span && floor < comparer.Compare(array[index - count], target))
            {
                count += probe;
                probe <<= 1;
            }

            if (span < count)
            {
                count = span;
            }

            int low = index - count + 1;
            int high = index;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                if (floor < comparer.Compare(array[middle], target))
                {
                    high = middle - 1;
                }
                else
                {
                    low = middle + 1;
                }
            }

            return high;
        }

        private static void YamUpdateSequenceScore(
            int bufferCount,
            int length,
            ref YamSortState state
        )
        {
            int half = length >> 1;
            int threshold = half >> 2;
            if (bufferCount < threshold)
            {
                if (state.sequenceScore < YamSequenceScoreMax)
                {
                    state.sequenceScore++;
                }
            }
            else if (half - threshold < bufferCount && YamSequenceScoreMin < state.sequenceScore)
            {
                state.sequenceScore--;
            }
        }

        private struct YamSortState
        {
            public int sequenceScore;
            public bool reverseSortFound;

            public YamSortState(int sequenceScore)
            {
                this.sequenceScore = sequenceScore;
                reverseSortFound = false;
            }
        }
    }
}
