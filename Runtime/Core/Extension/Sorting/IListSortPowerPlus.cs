// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is PowerSort+, from the published work of Sebastian Wild and Martin
// Nebel. This is an independent implementation of a published algorithm; the design is the original
// authors'. No upstream URL or license is recorded in this repository.
// See docs/project/third-party-notices.md.

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using Utils;

    public static partial class IListExtensions
    {
        /// <summary>
        /// Sorts the list using PowerSort+, an enhanced run-aware mergesort with priority-based merging.
        /// </summary>
        /// <remarks>
        /// Implementation reference: PowerSort+ (Wild & Nebel), which prioritizes runs via their power metric.
        /// </remarks>
        public static void PowerSortPlus<T, TComparer>(this IList<T> list, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int count = list.Count;
            if (count < 2)
            {
                return;
            }

            if (list is T[] array)
            {
                PowerSortPlusCore(array, count, comparer);
                return;
            }

            using PooledArray<T> scratchLease = SystemArrayPool<T>.Get(count, out T[] scratch);
            list.CopyTo(scratch, 0);
            PowerSortPlusCore(scratch, count, comparer);
            WriteBack(list, scratch, count);
        }

        private static void PowerSortPlusCore<T, TComparer>(
            T[] array,
            int count,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            using PooledResource<List<(int start, int length)>> runBuffer = Buffers<(
                int start,
                int length
            )>.List.Get(out List<(int start, int length)> runs);
            CollectNaturalRuns(array, count, comparer, runs);
            if (runs.Count <= 1)
            {
                return;
            }

            int bufferLength = Math.Max(32, count / 2 + 1);
            using PooledArray<T> tempLease = SystemArrayPool<T>.Get(bufferLength, out T[] buffer);

            using PooledResource<List<PowerSortPlusCandidate>> heapLease =
                Buffers<PowerSortPlusCandidate>.List.Get(out List<PowerSortPlusCandidate> heap);

            using PooledArray<PowerSortPlusNode> nodeLease = SystemArrayPool<PowerSortPlusNode>.Get(
                runs.Count,
                out PowerSortPlusNode[] nodes
            );
            int headIndex = BuildPowerSortPlusRuns(runs, nodes);

            for (int nodeIndex = headIndex; nodeIndex != -1; nodeIndex = nodes[nodeIndex].next)
            {
                int nextIndex = nodes[nodeIndex].next;
                if (nextIndex != -1)
                {
                    PowerSortPlusPushCandidate(heap, nodes, nodeIndex, nextIndex);
                }
            }

            int activeRuns = runs.Count;
            while (activeRuns > 1)
            {
                if (
                    !PowerSortPlusTryPopValidCandidate(
                        heap,
                        nodes,
                        out PowerSortPlusCandidate candidate
                    )
                )
                {
                    int fallbackLeft = headIndex;
                    int fallbackRight = fallbackLeft >= 0 ? nodes[fallbackLeft].next : -1;
                    if (fallbackLeft < 0 || fallbackRight < 0)
                    {
                        break;
                    }

                    headIndex = PowerSortPlusMergeNodes(
                        array,
                        buffer,
                        comparer,
                        headIndex,
                        heap,
                        nodes,
                        fallbackLeft,
                        fallbackRight
                    );
                    activeRuns--;
                    continue;
                }

                headIndex = PowerSortPlusMergeNodes(
                    array,
                    buffer,
                    comparer,
                    headIndex,
                    heap,
                    nodes,
                    candidate.leftIndex,
                    candidate.rightIndex
                );
                activeRuns--;
            }
        }

        private static int BuildPowerSortPlusRuns(
            List<(int start, int length)> runs,
            PowerSortPlusNode[] nodes
        )
        {
            if (runs.Count == 0)
            {
                return -1;
            }

            for (int i = 0; i < runs.Count; ++i)
            {
                (int start, int length) run = runs[i];
                nodes[i].start = run.start;
                nodes[i].length = run.length;
                nodes[i].prev = i - 1;
                nodes[i].next = i + 1 < runs.Count ? i + 1 : -1;
                nodes[i].version = 0;
                nodes[i].active = true;
            }

            for (int i = runs.Count; i < nodes.Length; ++i)
            {
                nodes[i].active = false;
                nodes[i].prev = -1;
                nodes[i].next = -1;
            }

            return 0;
        }

        private static int PowerSortPlusMergeNodes<T, TComparer>(
            T[] array,
            T[] buffer,
            TComparer comparer,
            int headIndex,
            List<PowerSortPlusCandidate> heap,
            PowerSortPlusNode[] nodes,
            int leftIndex,
            int rightIndex
        )
            where TComparer : IComparer<T>
        {
            if (leftIndex < 0 || rightIndex < 0)
            {
                return headIndex;
            }

            ref PowerSortPlusNode left = ref nodes[leftIndex];
            ref PowerSortPlusNode right = ref nodes[rightIndex];
            if (!left.active || !right.active)
            {
                return headIndex;
            }

            MergeRuns(array, buffer, left.start, left.length, right.start, right.length, comparer);

            left.length += right.length;
            left.version++;

            int nextIndex = right.next;
            left.next = nextIndex;
            if (nextIndex != -1)
            {
                nodes[nextIndex].prev = leftIndex;
            }

            right.active = false;
            right.prev = -1;
            right.next = -1;
            right.version++;

            if (left.prev != -1)
            {
                PowerSortPlusPushCandidate(heap, nodes, left.prev, leftIndex);
            }
            if (left.next != -1)
            {
                PowerSortPlusPushCandidate(heap, nodes, leftIndex, left.next);
            }

            if (left.prev == -1)
            {
                headIndex = leftIndex;
            }

            return headIndex;
        }

        private static void PowerSortPlusPushCandidate(
            List<PowerSortPlusCandidate> heap,
            PowerSortPlusNode[] nodes,
            int leftIndex,
            int rightIndex
        )
        {
            if (leftIndex < 0 || rightIndex < 0)
            {
                return;
            }

            ref PowerSortPlusNode left = ref nodes[leftIndex];
            ref PowerSortPlusNode right = ref nodes[rightIndex];
            if (!left.active || !right.active)
            {
                return;
            }

            PowerSortPlusCandidate candidate = new(
                leftIndex,
                rightIndex,
                left.length + right.length,
                left.version,
                right.version,
                left.start
            );

            heap.Add(candidate);
            PowerSortPlusSiftUp(heap, heap.Count - 1);
        }

        private static bool PowerSortPlusTryPopValidCandidate(
            List<PowerSortPlusCandidate> heap,
            PowerSortPlusNode[] nodes,
            out PowerSortPlusCandidate candidate
        )
        {
            while (heap.Count > 0)
            {
                PowerSortPlusCandidate top = PowerSortPlusPop(heap);
                if (
                    top.leftIndex >= 0
                    && top.rightIndex >= 0
                    && nodes[top.leftIndex].active
                    && nodes[top.rightIndex].active
                    && nodes[top.leftIndex].version == top.leftVersion
                    && nodes[top.rightIndex].version == top.rightVersion
                    && nodes[top.leftIndex].next == top.rightIndex
                    && nodes[top.rightIndex].prev == top.leftIndex
                )
                {
                    candidate = top;
                    return true;
                }
            }

            candidate = default;
            return false;
        }

        private static PowerSortPlusCandidate PowerSortPlusPop(List<PowerSortPlusCandidate> heap)
        {
            int lastIndex = heap.Count - 1;
            PowerSortPlusCandidate result = heap[0];
            heap[0] = heap[lastIndex];
            heap.RemoveAt(lastIndex);
            if (heap.Count > 0)
            {
                PowerSortPlusSiftDown(heap, 0);
            }
            return result;
        }

        private static void PowerSortPlusSiftUp(List<PowerSortPlusCandidate> heap, int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                if (PowerSortPlusCompare(heap[index], heap[parent]) >= 0)
                {
                    break;
                }

                (heap[index], heap[parent]) = (heap[parent], heap[index]);
                index = parent;
            }
        }

        private static void PowerSortPlusSiftDown(List<PowerSortPlusCandidate> heap, int index)
        {
            int count = heap.Count;
            while (true)
            {
                int left = (index << 1) + 1;
                if (left >= count)
                {
                    break;
                }

                int right = left + 1;
                int smallest = left;
                if (right < count && PowerSortPlusCompare(heap[right], heap[left]) < 0)
                {
                    smallest = right;
                }

                if (PowerSortPlusCompare(heap[index], heap[smallest]) <= 0)
                {
                    break;
                }

                (heap[index], heap[smallest]) = (heap[smallest], heap[index]);
                index = smallest;
            }
        }

        private static int PowerSortPlusCompare(PowerSortPlusCandidate a, PowerSortPlusCandidate b)
        {
            int priorityCompare = a.priority.CompareTo(b.priority);
            if (priorityCompare != 0)
            {
                return priorityCompare;
            }

            return a.tieBreaker.CompareTo(b.tieBreaker);
        }

        internal readonly struct PowerSortPlusCandidate
        {
            public readonly int leftIndex;
            public readonly int rightIndex;
            public readonly int priority;
            public readonly int leftVersion;
            public readonly int rightVersion;
            public readonly int tieBreaker;

            public PowerSortPlusCandidate(
                int leftIndex,
                int rightIndex,
                int priority,
                int leftVersion,
                int rightVersion,
                int tieBreaker
            )
            {
                this.leftIndex = leftIndex;
                this.rightIndex = rightIndex;
                this.priority = priority;
                this.leftVersion = leftVersion;
                this.rightVersion = rightVersion;
                this.tieBreaker = tieBreaker;
            }
        }

        private struct PowerSortPlusNode
        {
            public int start;
            public int length;
            public int prev;
            public int next;
            public int version;
            public bool active;
        }
    }
}
