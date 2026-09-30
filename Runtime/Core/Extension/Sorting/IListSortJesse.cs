// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// JesseSort by Jesse Lew, https://github.com/lewj85/jessesort, adapted from
// upstream 1bf1f3d5b719c869880d98443050a836a65f47c1 (MIT, Copyright 2026 Jesse Lew).

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using Utils;
    using WallstopStudios.UnityHelpers.Core.Helper;

    public static partial class IListExtensions
    {
        private const int JesseAscendingRoute = 1;
        private const int JesseDescendingRoute = 2;
        private const int JesseDirectRoute = 3;
        private const int JessePatienceRoute = 4;

        /// <summary>
        /// Sorts the list using JesseSort's simulated live-phase pipeline.
        /// </summary>
        /// <remarks>
        /// Adapts Jesse Lew's allocating pipeline at upstream commit
        /// 1bf1f3d5b719c869880d98443050a836a65f47c1. Direct regions use this
        /// package's IpnSort backend; pile searches use binary search. Values remain
        /// direct because the upstream compact-index route regressed measured managed workloads.
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
            if (count < 2)
            {
                return;
            }
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
                    Array.Reverse(array, 0, count);
                }
                return;
            }
            JesseSortUnorderedCore(array, count, orderedEnd, direction, comparer);
        }

        private static void JesseSortUnorderedCore<T, TComparer>(
            T[] array,
            int count,
            int orderedEnd,
            int direction,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            using PooledResource<List<JesseRun>> runLease = Buffers<JesseRun>.List.Get(
                out List<JesseRun> runs
            );
            if (TryGlobalJesseRuns(array, count, comparer, runs))
            {
                MergeJesseInitialRuns(array, count, runs, comparer, true);
                return;
            }
            runs.Clear();
            if (IsSparseJesseMiddleDensity(array, count, comparer))
            {
                for (int begin = 0; begin < count; )
                {
                    int end = begin + Math.Min(128, count - begin);
                    InsertionSortRange(array, begin, end - 1, comparer);
                    runs.Add(new JesseRun { Start = begin, End = end });
                    begin = end;
                }
                MergeJesseInitialRuns(array, count, runs, comparer, false);
                return;
            }
            int position = 0;
            if (32 <= orderedEnd)
            {
                if (direction < 0)
                {
                    Array.Reverse(array, 0, orderedEnd);
                }
                position = orderedEnd;
                runs.Add(new JesseRun { Start = 0, End = position });
            }
            while (position < count)
            {
                int firstEnd = position + Math.Min(1024, count - position);
                int route = ClassifyJesseChunk(array, position, firstEnd, comparer);
                int actualEnd = firstEnd;
                if (route == JesseAscendingRoute || route == JesseDescendingRoute)
                {
                    int runEnd = ExtendJesseRun(
                        array,
                        position,
                        count,
                        route == JesseDescendingRoute,
                        comparer
                    );
                    if (32 <= runEnd - position)
                    {
                        actualEnd = runEnd;
                        if (route == JesseDescendingRoute)
                        {
                            Array.Reverse(array, position, actualEnd - position);
                        }
                    }
                    else
                    {
                        route = JessePatienceRoute;
                    }
                }
                if (route == JesseDirectRoute || route == JessePatienceRoute)
                {
                    int regionEnd = firstEnd;
                    int probeStep = 1024;
                    while (regionEnd < count)
                    {
                        probeStep = Math.Min(8192, probeStep * 2);
                        int probeEnd = regionEnd + Math.Min(probeStep, count - regionEnd);
                        int nextRoute = ClassifyJesseChunk(array, regionEnd, probeEnd, comparer);
                        if (nextRoute != route)
                        {
                            bool persistent = true;
                            for (int confirmation = 1; confirmation <= 2; ++confirmation)
                            {
                                long next = (long)regionEnd + confirmation * 256;
                                if (count <= next)
                                {
                                    persistent = false;
                                    break;
                                }
                                int confirmStart = (int)next;
                                int confirmEnd =
                                    confirmStart + Math.Min(1024, count - confirmStart);
                                if (
                                    ClassifyJesseChunk(array, confirmStart, confirmEnd, comparer)
                                    != nextRoute
                                )
                                {
                                    persistent = false;
                                    break;
                                }
                            }
                            if (persistent)
                            {
                                break;
                            }
                        }
                        regionEnd = probeEnd;
                    }
                    actualEnd = regionEnd;
                    if (route == JesseDirectRoute)
                    {
                        SortJesseDirectRegion(array, position, actualEnd, comparer);
                    }
                    else
                    {
                        SortJessePatienceRegion(array, position, actualEnd, comparer);
                    }
                }
                runs.Add(new JesseRun { Start = position, End = actualEnd });
                position = actualEnd;
            }
            PrecompactJesseRuns(array, runs, comparer);
            MergeJessePreparedRuns(array, 0, count, runs, comparer, false, true);
        }

        private static int ClassifyJesseChunk<T, TComparer>(
            T[] array,
            int start,
            int end,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int length = end - start;
            if (length < 2)
            {
                return JessePatienceRoute;
            }
            bool ascending = true;
            bool descending = true;
            int probeEnd = start + Math.Min(8, length);
            for (int index = start + 1; index < probeEnd; ++index)
            {
                int comparison = comparer.Compare(array[index - 1], array[index]);
                ascending &= comparison <= 0;
                descending &= 0 <= comparison;
            }
            if (ascending && (4 <= length || descending))
            {
                return JesseAscendingRoute;
            }
            if (4 <= length && descending)
            {
                return JesseDescendingRoute;
            }
            Span<int> ascendingTails = stackalloc int[16];
            Span<int> descendingTails = stackalloc int[16];
            int ascendingCount = 0;
            int descendingCount = 0;
            bool descendingMode = comparer.Compare(array[start + 1], array[start]) < 0;
            int sampleEnd = start + Math.Min(16, length);
            for (int index = start; index < sampleEnd; ++index)
            {
                if (start < index)
                {
                    int comparison = comparer.Compare(array[index - 1], array[index]);
                    if (comparison != 0)
                    {
                        descendingMode = 0 < comparison;
                    }
                }
                Span<int> tails = descendingMode ? descendingTails : ascendingTails;
                int tailCount = descendingMode ? descendingCount : ascendingCount;
                int left = 0;
                int right = tailCount;
                while (left < right)
                {
                    int middle = left + ((right - left) >> 1);
                    int comparison = comparer.Compare(array[index], array[tails[middle]]);
                    if (descendingMode ? 0 < comparison : comparison < 0)
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
                    if (descendingMode)
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
            return ascendingCount + descendingCount <= 4 ? JessePatienceRoute : JesseDirectRoute;
        }

        private static int ExtendJesseRun<T, TComparer>(
            T[] array,
            int begin,
            int end,
            bool descending,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int position = begin + 1;
            while (position < end)
            {
                int comparison = comparer.Compare(array[position - 1], array[position]);
                if (descending ? comparison < 0 : 0 < comparison)
                {
                    break;
                }
                ++position;
            }
            return position;
        }

        private static void SortJesseDirectRegion<T, TComparer>(
            T[] array,
            int start,
            int end,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            if (1 < end - start)
            {
                IpnSortRange(array, start, end - 1, comparer, 2 * BitOps.Log2(end - start));
            }
        }

        private static void SortJessePatienceRegion<T, TComparer>(
            T[] array,
            int start,
            int end,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int count = end - start;
            if (count < 2)
            {
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
            ascending.Add(new JessePile { Tail = start, Count = 1 });
            blueprint[0] = 0;
            int lastAscending = 0;
            int lastDescending = 0;
            uint ascendingBits = 0;
            uint descendingBits = 0;
            int ascendingSeen = 0;
            int descendingSeen = 0;
            bool descendingMode = false;
            int prefixCount = count;
            for (int offset = 1; offset < count; ++offset)
            {
                int index = start + offset;
                int edgeComparison = comparer.Compare(array[index - 1], array[index]);
                if (edgeComparison != 0)
                {
                    descendingMode = 0 < edgeComparison;
                }
                List<JessePile> piles = descendingMode ? descending : ascending;
                int hint = descendingMode ? lastDescending : lastAscending;
                int destination = hint;
                if (edgeComparison != 0)
                {
                    destination = FindJessePile(
                        array,
                        index,
                        piles,
                        hint,
                        descendingMode,
                        comparer
                    );
                }
                bool created = destination == piles.Count;
                if (created)
                {
                    piles.Add(new JessePile());
                }
                JessePile pile = piles[destination];
                pile.Tail = index;
                ++pile.Count;
                piles[destination] = pile;
                blueprint[offset] = descendingMode ? ~destination : destination;
                if (descendingMode)
                {
                    lastDescending = destination;
                    descendingBits = (descendingBits << 1) | (created ? 1U : 0U);
                    ++descendingSeen;
                }
                else
                {
                    lastAscending = destination;
                    ascendingBits = (ascendingBits << 1) | (created ? 1U : 0U);
                    ++ascendingSeen;
                }
                int seen = descendingMode ? descendingSeen : ascendingSeen;
                uint bits = descendingMode ? descendingBits : ascendingBits;
                if (
                    edgeComparison != 0
                    && 32 <= piles.Count
                    && 32 <= seen
                    && 9 <= BitOps.PopCount(bits)
                )
                {
                    prefixCount = offset + 1;
                    break;
                }
            }
            using PooledArray<T> storageLease = SystemArrayPool<T>.Get(count, out T[] storage);
            using PooledResource<List<JesseRun>> runLease = Buffers<JesseRun>.List.Get(
                out List<JesseRun> runs
            );
            int output = CollectJesseRuns(descending, 0, runs);
            CollectJesseRuns(ascending, output, runs);
            int probeCount = Math.Min(prefixCount, 64);
            int tagHits = 0;
            for (int offset = 1; offset < probeCount; ++offset)
            {
                tagHits += blueprint[offset] == blueprint[offset - 1] ? 1 : 0;
            }
            bool bulk = 16 <= probeCount && (probeCount - 1) * 3 <= tagHits * 4;
            int descendingCount = descending.Count;
            int runCount = runs.Count;
            using PooledArray<int> cursorLease = SystemArrayPool<int>.Get(
                runCount,
                out int[] cursors
            );
            for (int runIndex = 0; runIndex < runCount; ++runIndex)
            {
                JesseRun run = runs[runIndex];
                cursors[runIndex] = runIndex < descendingCount ? run.End - 1 : run.Start;
            }
            for (int offset = 0; offset < prefixCount; )
            {
                int tag = blueprint[offset];
                bool reversed = tag < 0;
                int runIndex = reversed ? ~tag : descendingCount + tag;
                int cursor = cursors[runIndex];
                if (!bulk)
                {
                    storage[cursor] = array[start + offset];
                    cursors[runIndex] = cursor + (reversed ? -1 : 1);
                    ++offset;
                    continue;
                }
                int spanEnd = offset + 1;
                while (spanEnd < prefixCount && blueprint[spanEnd] == tag)
                {
                    ++spanEnd;
                }
                int length = spanEnd - offset;
                if (reversed)
                {
                    for (int source = offset; source < spanEnd; ++source)
                    {
                        storage[cursor--] = array[start + source];
                    }
                }
                else
                {
                    Array.Copy(array, start + offset, storage, cursor, length);
                    cursor += length;
                }
                cursors[runIndex] = cursor;
                offset = spanEnd;
            }
            if (prefixCount < count)
            {
                SortJesseDirectRegion(array, start + prefixCount, end, comparer);
                Array.Copy(array, start + prefixCount, storage, prefixCount, count - prefixCount);
                runs.Add(new JesseRun { Start = prefixCount, End = count });
            }
            MergeJesseBuffers(storage, 0, array, start, count, runs, comparer, true, false);
        }

        private static int FindJessePile<T, TComparer>(
            T[] array,
            int index,
            List<JessePile> piles,
            int hint,
            bool descending,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int count = piles.Count;
            if (hint < count)
            {
                int comparison = comparer.Compare(array[index], array[piles[hint].Tail]);
                bool fits = descending ? comparison <= 0 : 0 <= comparison;
                if (fits && hint != 0)
                {
                    comparison = comparer.Compare(array[index], array[piles[hint - 1].Tail]);
                    fits = descending ? 0 < comparison : comparison < 0;
                }
                if (fits)
                {
                    return hint;
                }
            }
            int left = 0;
            int right = count;
            while (left < right)
            {
                int middle = left + ((right - left) >> 1);
                int comparison = comparer.Compare(array[index], array[piles[middle].Tail]);
                if (descending ? 0 < comparison : comparison < 0)
                {
                    left = middle + 1;
                }
                else
                {
                    right = middle;
                }
            }
            return left;
        }

        private static int CollectJesseRuns(List<JessePile> piles, int offset, List<JesseRun> runs)
        {
            foreach (JessePile pile in piles)
            {
                int end = offset + pile.Count;
                runs.Add(new JesseRun { Start = offset, End = end });
                offset = end;
            }
            return offset;
        }

        private static void MergeJesseInitialRuns<T, TComparer>(
            T[] array,
            int count,
            List<JesseRun> runs,
            TComparer comparer,
            bool endpoints
        )
            where TComparer : IComparer<T>
        {
            if (endpoints && runs.Count == 2)
            {
                int middle = runs[0].End;
                if (comparer.Compare(array[middle - 1], array[middle]) <= 0)
                {
                    return;
                }
                if (comparer.Compare(array[count - 1], array[0]) <= 0)
                {
                    Array.Reverse(array, 0, middle);
                    Array.Reverse(array, middle, count - middle);
                    Array.Reverse(array, 0, count);
                    return;
                }
            }
            using PooledArray<T> scratchLease = SystemArrayPool<T>.Get(count, out T[] scratch);
            int runCount = runs.Count;
            int merged = 0;
            for (int index = 0; index < runCount; index += 2)
            {
                JesseRun left = runs[index];
                if (index + 1 == runCount)
                {
                    Array.Copy(array, left.Start, scratch, left.Start, left.End - left.Start);
                    runs[merged++] = left;
                }
                else
                {
                    JesseRun right = runs[index + 1];
                    MergeJessePair(
                        array,
                        0,
                        scratch,
                        0,
                        left.Start,
                        left.End,
                        right.End,
                        comparer,
                        int.MaxValue,
                        endpoints
                    );
                    runs[merged++] = new JesseRun { Start = left.Start, End = right.End };
                }
            }
            runs.RemoveRange(merged, runCount - merged);
            MergeJesseBuffers(scratch, 0, array, 0, count, runs, comparer, true, false);
        }

        private static void MergeJessePreparedRuns<T, TComparer>(
            T[] array,
            int start,
            int count,
            List<JesseRun> runs,
            TComparer comparer,
            bool inner,
            bool outer
        )
            where TComparer : IComparer<T>
        {
            if (runs.Count < 2)
            {
                return;
            }
            using PooledArray<T> scratchLease = SystemArrayPool<T>.Get(count, out T[] scratch);
            MergeJesseBuffers(array, start, scratch, 0, count, runs, comparer, inner, outer);
            Array.Copy(scratch, 0, array, start, count);
        }

        private static void MergeJesseBuffers<T, TComparer>(
            T[] initialSource,
            int sourceOffset,
            T[] initialTarget,
            int targetOffset,
            int count,
            List<JesseRun> runs,
            TComparer comparer,
            bool inner,
            bool outer
        )
            where TComparer : IComparer<T>
        {
            T[] source = initialSource;
            T[] target = initialTarget;
            int sourceStart = sourceOffset;
            int targetStart = targetOffset;
            int runCount = runs.Count;
            int gallopTrigger = 7;
            if (inner && 12 <= runCount && runCount <= 128)
            {
                int largest = 0;
                foreach (JesseRun run in runs)
                {
                    largest = Math.Max(largest, run.End - run.Start);
                }
                if ((long)largest * runCount * 4 <= (long)count * 5)
                {
                    gallopTrigger = 5;
                }
            }
            bool probeOrdered = inner;
            int tier = 0;
            while (1 < runCount)
            {
                if (probeOrdered)
                {
                    int previous = runCount;
                    int compacted = 0;
                    JesseRun active = runs[0];
                    for (int index = 1; index < runCount; ++index)
                    {
                        JesseRun next = runs[index];
                        if (
                            comparer.Compare(
                                source[sourceStart + active.End - 1],
                                source[sourceStart + next.Start]
                            ) <= 0
                        )
                        {
                            active.End = next.End;
                        }
                        else
                        {
                            runs[compacted++] = active;
                            active = next;
                        }
                    }
                    runs[compacted++] = active;
                    runCount = compacted;
                    probeOrdered = runCount <= 64 || previous <= (previous - runCount) * 4;
                    if (runCount == 1)
                    {
                        break;
                    }
                }
                int merged = 0;
                for (int index = 0; index < runCount; index += 2)
                {
                    JesseRun left = runs[index];
                    if (index + 1 == runCount)
                    {
                        Array.Copy(
                            source,
                            sourceStart + left.Start,
                            target,
                            targetStart + left.Start,
                            left.End - left.Start
                        );
                        runs[merged++] = left;
                        continue;
                    }
                    JesseRun right = runs[index + 1];
                    int trigger =
                        inner ? gallopTrigger
                        : outer && tier == 0 ? 0
                        : int.MaxValue;
                    MergeJessePair(
                        source,
                        sourceStart,
                        target,
                        targetStart,
                        left.Start,
                        left.End,
                        right.End,
                        comparer,
                        trigger
                    );
                    runs[merged++] = new JesseRun { Start = left.Start, End = right.End };
                }
                runCount = merged;
                (source, target) = (target, source);
                (sourceStart, targetStart) = (targetStart, sourceStart);
                ++tier;
            }
            if (!ReferenceEquals(source, initialTarget) || sourceStart != targetOffset)
            {
                Array.Copy(source, sourceStart, initialTarget, targetOffset, count);
            }
        }

        private static void MergeJessePair<T, TComparer>(
            T[] source,
            int sourceOffset,
            T[] target,
            int targetOffset,
            int begin,
            int middle,
            int end,
            TComparer comparer,
            int gallopTrigger,
            bool endpoints = true
        )
            where TComparer : IComparer<T>
        {
            if (
                endpoints
                && comparer.Compare(
                    source[sourceOffset + middle - 1],
                    source[sourceOffset + middle]
                ) <= 0
            )
            {
                Array.Copy(source, sourceOffset + begin, target, targetOffset + begin, end - begin);
                return;
            }
            if (
                endpoints
                && comparer.Compare(source[sourceOffset + end - 1], source[sourceOffset + begin])
                    <= 0
            )
            {
                Array.Copy(
                    source,
                    sourceOffset + middle,
                    target,
                    targetOffset + begin,
                    end - middle
                );
                Array.Copy(
                    source,
                    sourceOffset + begin,
                    target,
                    targetOffset + begin + end - middle,
                    middle - begin
                );
                return;
            }
            if (gallopTrigger == 0)
            {
                gallopTrigger = ShouldGallopJesseOuter(
                    source,
                    sourceOffset,
                    begin,
                    middle,
                    end,
                    comparer
                )
                    ? 7
                    : int.MaxValue;
            }
            int left = begin;
            int right = middle;
            int output = begin;
            int leftWins = 0;
            int rightWins = 0;
            while (left < middle && right < end)
            {
                if (comparer.Compare(source[sourceOffset + right], source[sourceOffset + left]) < 0)
                {
                    target[targetOffset + output++] = source[sourceOffset + right++];
                    ++rightWins;
                    leftWins = 0;
                    if (gallopTrigger <= rightWins && right < end)
                    {
                        int bound = FindJesseGallopEnd(
                            source,
                            sourceOffset,
                            right,
                            end,
                            source[sourceOffset + left],
                            comparer,
                            false
                        );
                        Array.Copy(
                            source,
                            sourceOffset + right,
                            target,
                            targetOffset + output,
                            bound - right
                        );
                        output += bound - right;
                        right = bound;
                        rightWins = 0;
                    }
                }
                else
                {
                    target[targetOffset + output++] = source[sourceOffset + left++];
                    ++leftWins;
                    rightWins = 0;
                    if (gallopTrigger <= leftWins && left < middle)
                    {
                        int bound = FindJesseGallopEnd(
                            source,
                            sourceOffset,
                            left,
                            middle,
                            source[sourceOffset + right],
                            comparer,
                            true
                        );
                        Array.Copy(
                            source,
                            sourceOffset + left,
                            target,
                            targetOffset + output,
                            bound - left
                        );
                        output += bound - left;
                        left = bound;
                        leftWins = 0;
                    }
                }
            }
            if (left < middle)
            {
                Array.Copy(
                    source,
                    sourceOffset + left,
                    target,
                    targetOffset + output,
                    middle - left
                );
            }
            else if (right < end)
            {
                Array.Copy(
                    source,
                    sourceOffset + right,
                    target,
                    targetOffset + output,
                    end - right
                );
            }
        }

        private static int FindJesseGallopEnd<T, TComparer>(
            T[] source,
            int offset,
            int begin,
            int end,
            T pivot,
            TComparer comparer,
            bool includeEqual
        )
            where TComparer : IComparer<T>
        {
            int step = 1;
            while (step < end - begin)
            {
                int comparison = comparer.Compare(source[offset + begin + step], pivot);
                if (includeEqual ? 0 < comparison : 0 <= comparison)
                {
                    break;
                }
                if ((end - begin) / 2 < step)
                {
                    step = end - begin;
                    break;
                }
                step *= 2;
            }
            int left = begin;
            int right = begin + Math.Min(end - begin, step == int.MaxValue ? step : step + 1);
            while (left < right)
            {
                int middle = left + ((right - left) >> 1);
                int comparison = comparer.Compare(source[offset + middle], pivot);
                if (includeEqual ? comparison <= 0 : comparison < 0)
                {
                    left = middle + 1;
                }
                else
                {
                    right = middle;
                }
            }
            return left;
        }

        private static bool ShouldGallopJesseOuter<T, TComparer>(
            T[] source,
            int offset,
            int begin,
            int middle,
            int end,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int leftLength = middle - begin;
            int rightLength = end - middle;
            bool prefix =
                1024 <= leftLength
                && comparer.Compare(source[offset + begin + 1023], source[offset + middle]) <= 0;
            prefix |=
                1024 <= rightLength
                && comparer.Compare(source[offset + middle + 1023], source[offset + begin]) < 0;
            if (!prefix)
            {
                return false;
            }
            int leftSample = 0;
            int rightSample = 0;
            int previousWinner = 0;
            int switches = 0;
            while (leftSample < 32 && rightSample < 32)
            {
                int leftIndex = begin + (int)(((long)(2 * leftSample + 1) * leftLength) / 64);
                int rightIndex = middle + (int)(((long)(2 * rightSample + 1) * rightLength) / 64);
                int winner;
                if (comparer.Compare(source[offset + rightIndex], source[offset + leftIndex]) < 0)
                {
                    ++rightSample;
                    winner = 2;
                }
                else
                {
                    ++leftSample;
                    winner = 1;
                }
                if (previousWinner != 0 && winner != previousWinner)
                {
                    ++switches;
                    if (2 < switches)
                    {
                        return false;
                    }
                }
                previousWinner = winner;
            }
            return true;
        }

        private static bool IsSparseJesseMiddleDensity<T, TComparer>(
            T[] array,
            int count,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            if (count < 10000)
            {
                return false;
            }
            int prefixInversions = 0;
            for (int index = 1; index < 8; ++index)
            {
                prefixInversions += comparer.Compare(array[index], array[index - 1]) < 0 ? 1 : 0;
            }
            if (2 < prefixInversions)
            {
                return false;
            }
            int total = 0;
            int nonzero = 0;
            for (int window = 0; window < 8; ++window)
            {
                int start = (int)(((long)(count - 64) * (2 * window + 1)) / 16);
                int inversions = 0;
                for (int index = start + 1; index < start + 64; ++index)
                {
                    inversions += comparer.Compare(array[index], array[index - 1]) < 0 ? 1 : 0;
                }
                if (inversions != 0)
                {
                    ++nonzero;
                }
                total += inversions;
                if (16 < inversions || 64 < total || nonzero + 7 - window < 6)
                {
                    return false;
                }
            }
            return 6 <= nonzero && 18 <= total;
        }

        private static bool TryGlobalJesseRuns<T, TComparer>(
            T[] array,
            int count,
            TComparer comparer,
            List<JesseRun> runs
        )
            where TComparer : IComparer<T>
        {
            if (count < 50000)
            {
                return false;
            }
            for (int index = 1; index < 64; ++index)
            {
                if (comparer.Compare(array[index], array[index - 1]) < 0)
                {
                    return false;
                }
            }
            bool drop = false;
            int previous = 0;
            for (int sample = 1; sample < 9; ++sample)
            {
                int index = (int)(((long)(count - 1) * sample) / 8);
                if (comparer.Compare(array[index], array[previous]) < 0)
                {
                    drop = true;
                    break;
                }
                previous = index;
            }
            if (!drop)
            {
                return false;
            }
            int position = 0;
            bool firstTwoOverlap = true;
            while (position < count)
            {
                if (runs.Count == 64)
                {
                    return false;
                }
                int begin = position;
                bool descending =
                    position + 1 < count
                    && comparer.Compare(array[position + 1], array[position]) < 0;
                ++position;
                while (position < count)
                {
                    int comparison = comparer.Compare(array[position], array[position - 1]);
                    if (descending ? 0 <= comparison : comparison < 0)
                    {
                        break;
                    }
                    ++position;
                }
                runs.Add(
                    new JesseRun
                    {
                        Start = begin,
                        End = position,
                        Descending = descending,
                    }
                );
                int runCount = runs.Count;
                if (2 <= runCount && runCount <= 3)
                {
                    firstTwoOverlap &= JesseRunsOverlap(
                        array,
                        runs[runCount - 2],
                        runs[runCount - 1],
                        comparer
                    );
                    if (runCount == 3 && firstTwoOverlap && position <= 8192)
                    {
                        return false;
                    }
                }
            }
            int totalRuns = runs.Count;
            if (totalRuns < 2)
            {
                return false;
            }
            if (8 <= totalRuns)
            {
                int overlaps = 0;
                for (int index = 1; index < totalRuns; ++index)
                {
                    overlaps += JesseRunsOverlap(array, runs[index - 1], runs[index], comparer)
                        ? 1
                        : 0;
                }
                int boundaries = totalRuns - 1;
                if (
                    overlaps == boundaries
                    || (count < totalRuns * 512 && boundaries * 9 <= overlaps * 10)
                )
                {
                    return false;
                }
            }
            foreach (JesseRun run in runs)
            {
                if (run.Descending)
                {
                    Array.Reverse(array, run.Start, run.End - run.Start);
                }
            }
            return true;
        }

        private static bool JesseRunsOverlap<T, TComparer>(
            T[] array,
            JesseRun left,
            JesseRun right,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            T leftMinimum = array[left.Descending ? left.End - 1 : left.Start];
            T leftMaximum = array[left.Descending ? left.Start : left.End - 1];
            T rightMinimum = array[right.Descending ? right.End - 1 : right.Start];
            T rightMaximum = array[right.Descending ? right.Start : right.End - 1];
            return comparer.Compare(rightMinimum, leftMaximum) < 0
                && comparer.Compare(leftMinimum, rightMaximum) < 0;
        }

        private static void PrecompactJesseRuns<T, TComparer>(
            T[] array,
            List<JesseRun> runs,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int count = runs.Count;
            int target = count == 0 ? 0 : 1 << BitOps.Log2(count);
            int excess = count - target;
            if (excess < 1 || target < 32 || (long)target * 20 < (long)excess * 100)
            {
                return;
            }
            using PooledArray<T> scratchLease = SystemArrayPool<T>.Get(
                runs[count - 1].End,
                out T[] scratch
            );
            while (0 < excess)
            {
                int output = 0;
                count = runs.Count;
                for (int index = 0; index < count; )
                {
                    if (0 < excess && index + 2 < count)
                    {
                        JesseRun first = runs[index];
                        JesseRun second = runs[index + 1];
                        JesseRun third = runs[index + 2];
                        if (first.End - first.Start <= third.End - third.Start)
                        {
                            MergeJessePair(
                                array,
                                0,
                                scratch,
                                0,
                                first.Start,
                                first.End,
                                second.End,
                                comparer,
                                int.MaxValue
                            );
                            Array.Copy(
                                scratch,
                                first.Start,
                                array,
                                first.Start,
                                second.End - first.Start
                            );
                            runs[output++] = new JesseRun { Start = first.Start, End = second.End };
                            runs[output++] = third;
                        }
                        else
                        {
                            MergeJessePair(
                                array,
                                0,
                                scratch,
                                0,
                                second.Start,
                                second.End,
                                third.End,
                                comparer,
                                int.MaxValue
                            );
                            Array.Copy(
                                scratch,
                                second.Start,
                                array,
                                second.Start,
                                third.End - second.Start
                            );
                            runs[output++] = first;
                            runs[output++] = new JesseRun { Start = second.Start, End = third.End };
                        }
                        --excess;
                        index += 3;
                    }
                    else
                    {
                        runs[output++] = runs[index++];
                    }
                }
                runs.RemoveRange(output, count - output);
            }
        }

        private struct JessePile
        {
            public int Tail;
            public int Count;
        }

        private struct JesseRun
        {
            public int Start;
            public int End;
            public bool Descending;
        }
    }
}
