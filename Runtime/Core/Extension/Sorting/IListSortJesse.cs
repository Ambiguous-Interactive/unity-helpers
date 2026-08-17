// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is JesseSort, by Jesse Michel, https://github.com/lewj85/jessesort.
// This is an adaptation of that work; the design is the original author's. The upstream license is
// not recorded in this repository. See docs/project/third-party-notices.md.

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System.Collections.Generic;
    using Utils;

    public static partial class IListExtensions
    {
        /// <summary>
        /// Sorts the list using JesseSort, a hybrid that routes natural runs through two patience games
        /// before merging all piles with a k-way heap.
        /// </summary>
        /// <remarks>
        /// Implementation reference: JesseSort by Jesse Michel, https://github.com/lewj85/jessesort.
        /// This adaptation materializes patience piles as <c>IList</c>-backed stacks and performs a k-way merge.
        /// </remarks>
        public static void JesseSort<T, TComparer>(this IList<T> array, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int count = array.Count;
            if (count < 2)
            {
                return;
            }

            using PooledResource<List<List<T>>> ascendingLease = Buffers<List<T>>.List.Get(
                out List<List<T>> ascendingPiles
            );
            using PooledResource<List<List<T>>> descendingLease = Buffers<List<T>>.List.Get(
                out List<List<T>> descendingPiles
            );
            using PooledResource<List<PooledResource<List<T>>>> ascendingPileLeaseListLease =
                Buffers<PooledResource<List<T>>>.List.Get(
                    out List<PooledResource<List<T>>> ascendingPileLeases
                );
            using PooledResource<List<PooledResource<List<T>>>> descendingPileLeaseListLease =
                Buffers<PooledResource<List<T>>>.List.Get(
                    out List<PooledResource<List<T>>> descendingPileLeases
                );

            try
            {
                int index = 0;
                while (index < count)
                {
                    int runStart = index;
                    index++;
                    if (index == count)
                    {
                        JessePatienceInsert(
                            ascendingPiles,
                            ascendingPileLeases,
                            array[runStart],
                            comparer
                        );
                        break;
                    }

                    int compare = comparer.Compare(array[index - 1], array[index]);
                    bool ascendingRun = compare <= 0;
                    while (index < count)
                    {
                        int nextCompare = comparer.Compare(array[index - 1], array[index]);
                        if (ascendingRun)
                        {
                            if (nextCompare <= 0)
                            {
                                index++;
                                continue;
                            }
                        }
                        else
                        {
                            if (nextCompare >= 0)
                            {
                                index++;
                                continue;
                            }
                        }
                        break;
                    }

                    int runEnd = index - 1;
                    if (ascendingRun)
                    {
                        for (int i = runStart; i <= runEnd; ++i)
                        {
                            JessePatienceInsert(
                                ascendingPiles,
                                ascendingPileLeases,
                                array[i],
                                comparer
                            );
                        }
                    }
                    else
                    {
                        for (int i = runEnd; i >= runStart; --i)
                        {
                            JessePatienceInsert(
                                descendingPiles,
                                descendingPileLeases,
                                array[i],
                                comparer
                            );
                        }
                    }
                }

                using PooledResource<List<JesseCursor<T>>> heapLease = Buffers<
                    JesseCursor<T>
                >.List.Get(out List<JesseCursor<T>> heap);
                InitializeJesseHeap(ascendingPiles, heap, comparer);
                InitializeJesseHeap(descendingPiles, heap, comparer);

                using PooledArray<T> outputLease = SystemArrayPool<T>.Get(count, out T[] output);
                int outputIndex = 0;
                while (heap.Count > 0)
                {
                    JesseCursor<T> cursor = heap[0];
                    output[outputIndex] = cursor.Peek();
                    outputIndex++;
                    if (cursor.MoveNext())
                    {
                        heap[0] = cursor;
                    }
                    else
                    {
                        heap[0] = heap[heap.Count - 1];
                        heap.RemoveAt(heap.Count - 1);
                    }

                    if (heap.Count > 0)
                    {
                        JesseHeapSiftDown(heap, 0, comparer);
                    }
                }

                for (int i = 0; i < count; ++i)
                {
                    array[i] = output[i];
                }
            }
            finally
            {
                ClearAndDisposeJessePiles(ascendingPiles, ascendingPileLeases);
                ClearAndDisposeJessePiles(descendingPiles, descendingPileLeases);
            }
        }

        private static void JessePatienceInsert<T, TComparer>(
            List<List<T>> piles,
            List<PooledResource<List<T>>> pileLeases,
            T value,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int left = 0;
            int right = piles.Count;
            while (left < right)
            {
                int mid = (left + right) >> 1;
                List<T> pile = piles[mid];
                T top = pile[pile.Count - 1];
                if (comparer.Compare(value, top) <= 0)
                {
                    right = mid;
                }
                else
                {
                    left = mid + 1;
                }
            }

            if (left == piles.Count)
            {
                PooledResource<List<T>> newPileLease = Buffers<T>.List.Get(out List<T> newPile);
                newPile.Add(value);
                piles.Add(newPile);
                pileLeases.Add(newPileLease);
            }
            else
            {
                piles[left].Add(value);
            }
        }

        private static void InitializeJesseHeap<T, TComparer>(
            List<List<T>> piles,
            List<JesseCursor<T>> heap,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            for (int i = 0; i < piles.Count; ++i)
            {
                List<T> pile = piles[i];
                if (pile.Count == 0)
                {
                    continue;
                }

                JesseCursor<T> cursor = new(pile);
                heap.Add(cursor);
                JesseHeapSiftUp(heap, heap.Count - 1, comparer);
            }
        }

        private static void JesseHeapSiftDown<T, TComparer>(
            List<JesseCursor<T>> heap,
            int index,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int count = heap.Count;
            while (true)
            {
                int leftChild = (index << 1) + 1;
                int rightChild = leftChild + 1;
                int smallest = index;

                if (
                    leftChild < count
                    && comparer.Compare(heap[leftChild].Peek(), heap[smallest].Peek()) < 0
                )
                {
                    smallest = leftChild;
                }

                if (
                    rightChild < count
                    && comparer.Compare(heap[rightChild].Peek(), heap[smallest].Peek()) < 0
                )
                {
                    smallest = rightChild;
                }

                if (smallest == index)
                {
                    return;
                }

                (heap[index], heap[smallest]) = (heap[smallest], heap[index]);
                index = smallest;
            }
        }

        private static void ClearAndDisposeJessePiles<T>(
            List<List<T>> piles,
            List<PooledResource<List<T>>> leases
        )
        {
            for (int i = 0; i < piles.Count; ++i)
            {
                piles[i].Clear();
            }

            for (int i = 0; i < leases.Count; ++i)
            {
                PooledResource<List<T>> lease = leases[i];
                lease.Dispose();
            }

            piles.Clear();
            leases.Clear();
        }

        private static void JesseHeapSiftUp<T, TComparer>(
            List<JesseCursor<T>> heap,
            int index,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                if (comparer.Compare(heap[index].Peek(), heap[parent].Peek()) >= 0)
                {
                    return;
                }

                (heap[index], heap[parent]) = (heap[parent], heap[index]);
                index = parent;
            }
        }

        private struct JesseCursor<T>
        {
            public JesseCursor(List<T> pile)
            {
                Pile = pile;
                Index = pile.Count - 1;
            }

            public List<T> Pile { get; }

            public int Index { get; private set; }

            public T Peek()
            {
                return Pile[Index];
            }

            public bool MoveNext()
            {
                Index--;
                return Index >= 0;
            }
        }
    }
}
