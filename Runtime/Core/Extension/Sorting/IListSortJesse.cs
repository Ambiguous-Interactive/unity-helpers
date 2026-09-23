// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is JesseSort, by Jesse Lew, https://github.com/lewj85/jessesort.
// This is an adaptation of that work; the design is the original author's.
// Upstream: MIT License - Copyright (c) 2026 Jesse Lew. See docs/project/third-party-notices.md.

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System.Collections.Generic;
    using Utils;

    public static partial class IListExtensions
    {
        /// <summary>
        /// Sorts the list using a simulated dual-patience adaptation of JesseSort.
        /// </summary>
        /// <remarks>
        /// Implementation reference: JesseSort by Jesse Lew, https://github.com/lewj85/jessesort.
        /// This adaptation records pile assignments, reconstructs contiguous ascending piles, then
        /// merges adjacent pile pairs bottom-up with ordered-boundary and reverse-disjoint shortcuts.
        /// A conservative high-entropy route uses IpnSort for large random-looking inputs. It does
        /// not implement the upstream live-phase routing pipeline. See docs/performance/ilist-sorting-performance.md.
        /// </remarks>
        public static void JesseSort<T, TComparer>(this IList<T> list, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int count = list.Count;
            if (count < 2)
            {
                return;
            }

            if (list is T[] array)
            {
                JesseSortCore(array, count, comparer);
                return;
            }

            using PooledArray<T> scratchLease = SystemArrayPool<T>.Get(count, out T[] scratch);
            list.CopyTo(scratch, 0);
            JesseSortCore(scratch, count, comparer);
            WriteBack(list, scratch, count);
        }

        private static void JesseSortCore<T, TComparer>(T[] array, int count, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int direction = 0;
            int orderedEnd = 1;
            while (orderedEnd < count)
            {
                int comparison = comparer.Compare(array[orderedEnd - 1], array[orderedEnd]);
                if (comparison != 0)
                {
                    int nextDirection = comparison < 0 ? 1 : -1;
                    if (direction != 0 && direction != nextDirection)
                    {
                        break;
                    }
                    direction = nextDirection;
                }
                ++orderedEnd;
            }
            if (orderedEnd == count)
            {
                if (direction < 0)
                {
                    System.Array.Reverse(array, 0, count);
                }
                return;
            }

            if (ShouldRouteJesseToDirect(array, count, comparer))
            {
                IpnSortCore(array, count, comparer);
                return;
            }

            using PooledResource<List<JessePile>> ascendingLease = Buffers<JessePile>.List.Get(
                out List<JessePile> ascending
            );
            using PooledResource<List<JessePile>> descendingLease = Buffers<JessePile>.List.Get(
                out List<JessePile> descending
            );
            using PooledArray<int> blueprintLease = SystemArrayPool<int>.Get(
                count,
                out int[] blueprint
            );
            bool descendingRun = direction < 0;
            for (int index = 0; index < count; ++index)
            {
                if (0 < index)
                {
                    int comparison = comparer.Compare(array[index - 1], array[index]);
                    if (comparison != 0)
                    {
                        descendingRun = 0 < comparison;
                    }
                }
                List<JessePile> piles = descendingRun ? descending : ascending;
                int left = 0;
                int right = piles.Count;
                while (left < right)
                {
                    int middle = left + ((right - left) >> 1);
                    int comparison = comparer.Compare(array[index], array[piles[middle].Tail]);
                    if (descendingRun ? 0 < comparison : comparison < 0)
                    {
                        left = middle + 1;
                    }
                    else
                    {
                        right = middle;
                    }
                }
                if (left == piles.Count)
                {
                    piles.Add(new JessePile());
                }
                JessePile pile = piles[left];
                pile.Tail = index;
                ++pile.Count;
                piles[left] = pile;
                blueprint[index] = descendingRun ? ~left : left;
            }

            using PooledArray<T> storageLease = SystemArrayPool<T>.Get(count, out T[] storage);
            using PooledResource<List<JesseRun>> runLease = Buffers<JesseRun>.List.Get(
                out List<JesseRun> runs
            );
            int offset = CollectJesseRuns(ascending, 0, false, runs);
            CollectJesseRuns(descending, offset, true, runs);
            for (int index = 0; index < count; ++index)
            {
                int tag = blueprint[index];
                bool reversed = tag < 0;
                int pileIndex = reversed ? ~tag : tag;
                List<JessePile> piles = reversed ? descending : ascending;
                JessePile pile = piles[pileIndex];
                storage[pile.Position] = array[index];
                pile.Position += reversed ? -1 : 1;
                piles[pileIndex] = pile;
            }

            T[] source = storage;
            T[] target = array;
            int runCount = runs.Count;
            while (1 < runCount)
            {
                int mergedCount = 0;
                for (int index = 0; index < runCount; index += 2)
                {
                    JesseRun left = runs[index];
                    if (runCount - 1 == index)
                    {
                        System.Array.Copy(
                            source,
                            left.Start,
                            target,
                            left.Start,
                            left.End - left.Start
                        );
                        runs[mergedCount++] = left;
                        continue;
                    }
                    JesseRun right = runs[index + 1];
                    MergeJesseRuns(source, target, left, right, comparer);
                    runs[mergedCount++] = new JesseRun { Start = left.Start, End = right.End };
                }
                runCount = mergedCount;
                (source, target) = (target, source);
            }

            if (!ReferenceEquals(source, array))
            {
                System.Array.Copy(source, 0, array, 0, count);
            }
        }

        private static bool ShouldRouteJesseToDirect<T, TComparer>(
            T[] array,
            int count,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            const int minimumLength = 4096;
            const int sampleLength = 16;
            const int sampleCount = 8;
            const int requiredDirectSamples = 6;
            if (count < minimumLength)
            {
                return false;
            }

            int directSamples = 0;
            for (int sample = 0; sample < sampleCount; ++sample)
            {
                int start = (int)((long)(count - sampleLength) * sample / (sampleCount - 1));
                if (IsDirectJesseSample(array, start, comparer))
                {
                    ++directSamples;
                }
                if (directSamples + sampleCount - sample - 1 < requiredDirectSamples)
                {
                    return false;
                }
            }

            int direction = 0;
            int directionChanges = 0;
            for (int index = 1; index < count; ++index)
            {
                int comparison = comparer.Compare(array[index - 1], array[index]);
                int nextDirection =
                    comparison < 0 ? 1
                    : comparison == 0 ? 0
                    : -1;
                if (nextDirection == 0)
                {
                    continue;
                }
                if (direction != 0 && direction != nextDirection)
                {
                    ++directionChanges;
                }
                direction = nextDirection;
            }
            return count / 4 <= directionChanges;
        }

        private static bool IsDirectJesseSample<T, TComparer>(
            T[] array,
            int start,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            const int sampleLength = 16;
            const int patienceTailThreshold = 4;
            System.Span<int> ascendingTails = stackalloc int[sampleLength];
            System.Span<int> descendingTails = stackalloc int[sampleLength];
            int ascendingCount = 0;
            int descendingCount = 0;
            bool descending = comparer.Compare(array[start + 1], array[start]) < 0;
            for (int offset = 0; offset < sampleLength; ++offset)
            {
                int index = start + offset;
                if (0 < offset)
                {
                    int comparison = comparer.Compare(array[index - 1], array[index]);
                    if (comparison < 0)
                    {
                        descending = false;
                    }
                    else if (0 < comparison)
                    {
                        descending = true;
                    }
                }

                System.Span<int> tails = descending ? descendingTails : ascendingTails;
                int tailCount = descending ? descendingCount : ascendingCount;
                int left = 0;
                int right = tailCount;
                while (left < right)
                {
                    int middle = left + ((right - left) >> 1);
                    int comparison = comparer.Compare(array[index], array[tails[middle]]);
                    if (descending ? 0 < comparison : comparison < 0)
                    {
                        left = middle + 1;
                    }
                    else
                    {
                        right = middle;
                    }
                }
                if (left == tailCount)
                {
                    if (descending)
                    {
                        ++descendingCount;
                    }
                    else
                    {
                        ++ascendingCount;
                    }
                }
                tails[left] = index;
            }
            return patienceTailThreshold < ascendingCount + descendingCount;
        }

        private static int CollectJesseRuns(
            List<JessePile> piles,
            int offset,
            bool reversed,
            List<JesseRun> runs
        )
        {
            for (int index = 0; index < piles.Count; ++index)
            {
                JessePile pile = piles[index];
                int end = offset + pile.Count;
                runs.Add(new JesseRun { Start = offset, End = end });
                pile.Position = reversed ? end - 1 : offset;
                piles[index] = pile;
                offset = end;
            }
            return offset;
        }

        private static void MergeJesseRuns<T, TComparer>(
            T[] source,
            T[] target,
            JesseRun left,
            JesseRun right,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int leftStart = left.Start;
            int leftEnd = left.End;
            int rightStart = leftEnd;
            int rightEnd = right.End;
            if (comparer.Compare(source[leftEnd - 1], source[rightStart]) <= 0)
            {
                System.Array.Copy(source, leftStart, target, leftStart, rightEnd - leftStart);
                return;
            }
            if (comparer.Compare(source[rightEnd - 1], source[leftStart]) <= 0)
            {
                System.Array.Copy(source, rightStart, target, leftStart, rightEnd - rightStart);
                System.Array.Copy(
                    source,
                    leftStart,
                    target,
                    leftStart + (rightEnd - rightStart),
                    leftEnd - leftStart
                );
                return;
            }
            int leftPosition = leftStart;
            int rightPosition = rightStart;
            int targetPosition = leftStart;
            while (leftPosition < leftEnd && rightPosition < rightEnd)
            {
                target[targetPosition++] =
                    comparer.Compare(source[leftPosition], source[rightPosition]) <= 0
                        ? source[leftPosition++]
                        : source[rightPosition++];
            }
            if (leftPosition < leftEnd)
            {
                System.Array.Copy(
                    source,
                    leftPosition,
                    target,
                    targetPosition,
                    leftEnd - leftPosition
                );
            }
            else if (rightPosition < rightEnd)
            {
                System.Array.Copy(
                    source,
                    rightPosition,
                    target,
                    targetPosition,
                    rightEnd - rightPosition
                );
            }
        }

        private struct JessePile
        {
            public int Tail;
            public int Count;
            public int Position;
        }

        private struct JesseRun
        {
            public int Start;
            public int End;
        }
    }
}
