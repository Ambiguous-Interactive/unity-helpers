// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    /// A priority queue implemented as a thin wrapper around <see cref="Heap{T}"/>.
    /// Provides clearer semantics for priority-based task scheduling, A* pathfinding,
    /// event systems, and AI decision making. Supports both min-priority and max-priority modes.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// PriorityQueue<PathNode> open = new PriorityQueue<PathNode>(Comparer<PathNode>.Create((a, b) => a.F.CompareTo(b.F)));
    /// open.Enqueue(startNode);
    /// while (!open.IsEmpty && open.TryDequeue(out PathNode current))
    /// {
    ///     // process node
    /// }
    /// ]]></code>
    /// </example>
    [Serializable]
    public sealed class PriorityQueue<T> : IEnumerable<T>
    {
        /// <summary>
        /// Gets the number of elements in the priority queue.
        /// </summary>
        public int Count => _heap.Count;

        /// <summary>
        /// Gets whether the priority queue is empty.
        /// </summary>
        public bool IsEmpty => _heap.IsEmpty;

        /// <summary>
        /// Gets the current capacity of the underlying heap.
        /// </summary>
        public int Capacity => _heap.Capacity;

        private readonly Heap<T> _heap;

        public PriorityQueue()
            : this(16) { }

        /// <summary>
        /// Constructs a priority queue with the default comparer (min-priority).
        /// </summary>
        public PriorityQueue(int capacity)
            : this(Comparer<T>.Default, capacity) { }

        /// <summary>
        /// Constructs a priority queue with a custom comparer.
        /// </summary>
        public PriorityQueue(IComparer<T> comparer, int capacity = 16)
        {
            _heap = new Heap<T>(comparer, capacity);
        }

        /// <summary>
        /// Constructs a priority queue from an existing collection.
        /// </summary>
        public PriorityQueue(IEnumerable<T> items, IComparer<T> comparer = null)
        {
            _heap = new Heap<T>(items, comparer);
        }

        /// <summary>
        /// Creates a min-priority queue (lowest priority dequeued first).
        /// </summary>
        public static PriorityQueue<T> CreateMin(int capacity = 16)
        {
            return new PriorityQueue<T>(Comparer<T>.Default, capacity);
        }

        /// <summary>
        /// Creates a max-priority queue (highest priority dequeued first).
        /// </summary>
        public static PriorityQueue<T> CreateMax(int capacity = 16)
        {
            return new PriorityQueue<T>(
                Comparer<T>.Create((x, y) => Comparer<T>.Default.Compare(y, x)),
                capacity
            );
        }

        /// <summary>
        /// Creates a min-priority queue from an existing collection.
        /// </summary>
        public static PriorityQueue<T> CreateMin(IEnumerable<T> items)
        {
            return new PriorityQueue<T>(items, Comparer<T>.Default);
        }

        /// <summary>
        /// Creates a max-priority queue from an existing collection.
        /// </summary>
        public static PriorityQueue<T> CreateMax(IEnumerable<T> items)
        {
            return new PriorityQueue<T>(
                items,
                Comparer<T>.Create((x, y) => Comparer<T>.Default.Compare(y, x))
            );
        }

        /// <summary>
        /// Enqueues an element with its priority in O(log n) time.
        /// </summary>
        public void Enqueue(T item)
        {
            _heap.Add(item);
        }

        /// <summary>
        /// Attempts to dequeue the highest priority element in O(log n) time.
        /// </summary>
        public bool TryDequeue(out T result)
        {
            return _heap.TryPop(out result);
        }

        /// <summary>
        /// Attempts to peek at the highest priority element without removing it.
        /// </summary>
        public bool TryPeek(out T result)
        {
            return _heap.TryPeek(out result);
        }

        /// <summary>
        /// Removes all elements from the priority queue.
        /// </summary>
        public void Clear()
        {
            _heap.Clear();
        }

        /// <summary>
        /// Checks if the priority queue contains a specific element in O(n) time.
        /// </summary>
        public bool Contains(T item)
        {
            return _heap.Contains(item);
        }

        /// <summary>
        /// Attempts to update the priority of an element at the specified index.
        /// The index refers to internal heap storage order, not priority order.
        /// Returns false if index is invalid.
        /// </summary>
        public bool TryUpdatePriority(int index, T newValue)
        {
            return _heap.TryUpdatePriority(index, newValue);
        }

        /// <summary>
        /// Attempts to get the element at the specified internal index.
        /// The index refers to internal heap storage order, not priority order.
        /// </summary>
        public bool TryGet(int index, out T result)
        {
            return _heap.TryGet(index, out result);
        }

        /// <summary>
        /// Returns an array containing all elements (not in priority order).
        /// </summary>
        public T[] ToArray()
        {
            return _heap.ToArray();
        }

        public int ToArray(ref T[] result)
        {
            return _heap.ToArray(ref result);
        }

        /// <summary>
        /// Trims excess capacity to match the current count.
        /// </summary>
        public void TrimExcess()
        {
            _heap.TrimExcess();
        }

        /// <summary>
        /// Returns an enumerator that iterates through all elements in internal heap order (not priority order).
        /// Note: Iteration order is NOT sorted by priority. Use DequeueAll for priority-sorted iteration.
        /// </summary>
        public Heap<T>.HeapEnumerator GetEnumerator()
        {
            return _heap.GetEnumerator();
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
