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
        /// This adaptation records pile assignments before reconstructing contiguous piles for a k-way merge.
        /// It does not implement the upstream live-phase routing pipeline. See docs/performance/ilist-sorting-performance.md.
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
            using PooledResource<List<JesseCursor>> heapLease = Buffers<JesseCursor>.List.Get(
                out List<JesseCursor> heap
            );
            int offset = InitializeJessePiles(ascending, 0, false, heap);
            InitializeJessePiles(descending, offset, true, heap);
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
            for (int index = heap.Count / 2 - 1; 0 <= index; --index)
            {
                JesseHeapSiftDown(heap, index, storage, comparer);
            }
            for (int index = 0; index < count; ++index)
            {
                JesseCursor cursor = heap[0];
                array[index] = storage[cursor.Position++];
                if (cursor.Position < cursor.End)
                {
                    heap[0] = cursor;
                }
                else
                {
                    heap[0] = heap[heap.Count - 1];
                    heap.RemoveAt(heap.Count - 1);
                }
                if (0 < heap.Count)
                {
                    JesseHeapSiftDown(heap, 0, storage, comparer);
                }
            }
        }

        private static int InitializeJessePiles(
            List<JessePile> piles,
            int offset,
            bool reversed,
            List<JesseCursor> heap
        )
        {
            for (int index = 0; index < piles.Count; ++index)
            {
                JessePile pile = piles[index];
                int end = offset + pile.Count;
                heap.Add(new JesseCursor { Position = offset, End = end });
                pile.Position = reversed ? end - 1 : offset;
                piles[index] = pile;
                offset = end;
            }
            return offset;
        }

        private static void JesseHeapSiftDown<T, TComparer>(
            List<JesseCursor> heap,
            int index,
            T[] storage,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int count = heap.Count;
            while (index < count / 2)
            {
                int child = (index << 1) + 1;
                int right = child + 1;
                if (
                    right < count
                    && comparer.Compare(
                        storage[heap[right].Position],
                        storage[heap[child].Position]
                    ) < 0
                )
                {
                    child = right;
                }
                if (
                    comparer.Compare(storage[heap[index].Position], storage[heap[child].Position])
                    <= 0
                )
                {
                    return;
                }
                JesseCursor cursor = heap[index];
                heap[index] = heap[child];
                heap[child] = cursor;
                index = child;
            }
        }

        private struct JessePile
        {
            public int Tail;
            public int Count;
            public int Position;
        }

        private struct JesseCursor
        {
            public int Position;
            public int End;
        }
    }
}
