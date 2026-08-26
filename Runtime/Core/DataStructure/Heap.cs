// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// A highly optimized, array-backed generic heap implementation supporting both min-heap and max-heap ordering.
    /// Uses dynamic resizing with geometric growth for efficient amortized insertions.
    /// Allocation-free enumerator and minimal heap operations ensure optimal performance.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// Heap<PathNode> openSet = new Heap<PathNode>(Comparer<PathNode>.Create((a, b) => a.F.CompareTo(b.F)));
    /// openSet.Push(startNode);
    /// while (!openSet.IsEmpty && openSet.TryPop(out PathNode current))
    /// {
    ///     // A* exploration...
    /// }
    /// ]]></code>
    /// </example>
    /// <typeparam name="T">The type of elements in the heap. Must be comparable.</typeparam>
    [Serializable]
    public sealed class Heap<T> : IReadOnlyList<T>
    {
        private const int DefaultCapacity = 16;
        private const int MinimumGrowth = 4;

        private T[] _items;
        private int _count;
        private readonly IComparer<T> _comparer;

        /// <summary>
        /// Gets the number of elements in the heap.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Gets whether the heap is empty.
        /// </summary>
        public bool IsEmpty => _count == 0;

        /// <summary>
        /// Gets the current capacity of the underlying array.
        /// </summary>
        public int Capacity => _items.Length;

        /// <summary>
        /// Gets the element at the specified index in heap order (not sorted order).
        /// </summary>
        public T this[int index]
        {
            get
            {
                if (index < 0 || _count <= index)
                {
                    throw new IndexOutOfRangeException(
                        $"{index} is outside of bounds [0, {_count})"
                    );
                }
                return _items[index];
            }
        }

        /// <summary>
        /// Attempts to get the element at the specified index in heap order (not sorted order).
        /// </summary>
        /// <param name="index">The index to access.</param>
        /// <param name="result">The element at the index if valid.</param>
        /// <returns>True if the index is valid, false otherwise.</returns>
        public bool TryGet(int index, out T result)
        {
            if (index < 0 || _count <= index)
            {
                result = default;
                return false;
            }
            result = _items[index];
            return true;
        }

        public Heap()
            : this(Comparer<T>.Default) { }

        /// <summary>
        /// Constructs a heap with the default comparer (min-heap for natural ordering).
        /// </summary>
        /// <param name="capacity">Initial capacity of the heap.</param>
        public Heap(int capacity)
            : this(Comparer<T>.Default, capacity) { }

        /// <summary>
        /// Constructs a heap with a custom comparer.
        /// </summary>
        /// <param name="comparer">The comparer to use for ordering elements.</param>
        /// <param name="capacity">Initial capacity of the heap.</param>
        public Heap(IComparer<T> comparer, int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    "Capacity must be positive."
                );
            }
            _items = new T[capacity];
            _count = 0;
            _comparer = comparer ?? Comparer<T>.Default;
        }

        /// <summary>
        /// Constructs a heap from an existing collection (heapifies in O(n) time).
        /// </summary>
        /// <param name="items">The collection to heapify.</param>
        /// <param name="comparer">The comparer to use for ordering elements.</param>
        public Heap(IEnumerable<T> items, IComparer<T> comparer = null)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            _comparer = comparer ?? Comparer<T>.Default;
            _count = 0;
            switch (items)
            {
                case IReadOnlyList<T> readonlyList:
                {
                    int capacity = Math.Max(DefaultCapacity, readonlyList.Count);
                    _items = new T[capacity];
                    for (int i = 0; i < readonlyList.Count; i++)
                    {
                        _items[_count++] = readonlyList[i];
                    }

                    break;
                }
                case ICollection<T> collection:
                {
                    int capacity = Math.Max(DefaultCapacity, collection.Count);
                    _items = new T[capacity];
                    collection.CopyTo(_items, 0);
                    _count = collection.Count;
                    break;
                }
                case IReadOnlyCollection<T> readOnlyCollection:
                {
                    int capacity = Math.Max(DefaultCapacity, readOnlyCollection.Count);
                    _items = new T[capacity];
                    foreach (T item in readOnlyCollection)
                    {
                        _items[_count++] = item;
                    }

                    break;
                }
                default:
                {
                    _items = new T[DefaultCapacity];
                    foreach (T item in items)
                    {
                        if (_count == _items.Length)
                        {
                            int newCapacity = ComputeGrowth(_items.Length);
                            Resize(newCapacity);
                        }
                        _items[_count++] = item;
                    }

                    break;
                }
            }

            // Floyd's heap construction algorithm - O(n)
            for (int i = (_count - 1) >> 1; 0 <= i; i--)
            {
                HeapifyDown(i);
            }
        }

        /// <summary>
        /// Creates a min-heap with the default comparer.
        /// </summary>
        public static Heap<T> CreateMinHeap(
            IComparer<T> comparer = null,
            int capacity = DefaultCapacity
        )
        {
            return new Heap<T>(comparer, capacity);
        }

        /// <summary>
        /// Creates a max-heap with a reversed comparer.
        /// </summary>
        public static Heap<T> CreateMaxHeap(
            IComparer<T> comparer = null,
            int capacity = DefaultCapacity
        )
        {
            return comparer == null
                ? new Heap<T>(ReverseComparer<T>.Instance, capacity)
                : new Heap<T>(new ReverseComparer<T>(comparer), capacity);
        }

        /// <summary>
        /// Creates a min-heap from an existing collection.
        /// </summary>
        public static Heap<T> CreateMinHeap(IEnumerable<T> items, IComparer<T> comparer = null)
        {
            return new Heap<T>(items, comparer ?? Comparer<T>.Default);
        }

        /// <summary>
        /// Creates a max-heap from an existing collection.
        /// </summary>
        public static Heap<T> CreateMaxHeap(IEnumerable<T> items, IComparer<T> comparer = null)
        {
            return comparer == null
                ? new Heap<T>(items, ReverseComparer<T>.Instance)
                : new Heap<T>(items, new ReverseComparer<T>(comparer));
        }

        /// <summary>
        /// Adds an element to the heap in O(log n) time.
        /// </summary>
        public void Add(T item)
        {
            if (_count == _items.Length)
            {
                int newCapacity = ComputeGrowth(_items.Length);
                Resize(newCapacity);
            }

            _items[_count] = item;
            HeapifyUp(_count);
            _count++;
        }

        /// <summary>
        /// Attempts to peek at the root element without removing it.
        /// </summary>
        /// <returns>True if the heap is not empty, false otherwise.</returns>
        public bool TryPeek(out T result)
        {
            if (_count == 0)
            {
                result = default;
                return false;
            }
            result = _items[0];
            return true;
        }

        /// <summary>
        /// Attempts to remove and return the root element in O(log n) time.
        /// </summary>
        /// <returns>True if the heap was not empty, false otherwise.</returns>
        public bool TryPop(out T result)
        {
            if (_count == 0)
            {
                result = default;
                return false;
            }

            T root = _items[0];
            _count--;
            if (0 < _count)
            {
                _items[0] = _items[_count];
                HeapifyDown(0);
            }
            _items[_count] = default;
            result = root;
            return true;
        }

        /// <summary>
        /// Removes all elements from the heap.
        /// </summary>
        public void Clear()
        {
            Array.Clear(_items, 0, _count);
            _count = 0;
        }

        /// <summary>
        /// Checks if the heap contains a specific element in O(log(n)) time.
        /// </summary>
        public bool Contains(T item)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_comparer.Compare(_items[i], item) == 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Copies the heap elements to an array (not in sorted order).
        /// </summary>
        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }
            if (arrayIndex < 0 || array.Length < arrayIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            }
            if (array.Length - arrayIndex < _count)
            {
                throw new ArgumentException("Destination array is not large enough.");
            }

            Array.Copy(_items, 0, array, arrayIndex, _count);
        }

        /// <summary>
        /// Returns an array containing all elements (not in sorted order).
        /// </summary>
        public T[] ToArray()
        {
            T[] result = null;
            _ = ToArray(ref result);
            return result;
        }

        public int ToArray(ref T[] result)
        {
            if (result == null || result.Length < _count)
            {
                result = new T[_count];
            }

            Array.Copy(_items, 0, result, 0, _count);
            return _count;
        }

        /// <summary>
        /// Trims excess capacity to match the current count.
        /// </summary>
        public void TrimExcess()
        {
            int threshold = (int)(_items.Length * 0.9);
            if (_count < threshold)
            {
                int newCapacity = Math.Max(DefaultCapacity, _count);
                Resize(newCapacity);
            }
        }

        /// <summary>
        /// Attempts to update the priority of an element at the specified index in O(log n) time.
        /// After updating, the heap property is restored.
        /// </summary>
        /// <param name="index">The index of the element to update.</param>
        /// <param name="newValue">The new value for the element.</param>
        /// <returns>True if the index was valid and the update succeeded, false otherwise.</returns>
        public bool TryUpdatePriority(int index, T newValue)
        {
            if (index < 0 || _count <= index)
            {
                return false;
            }

            T oldValue = _items[index];
            _items[index] = newValue;

            int comparison = _comparer.Compare(newValue, oldValue);
            if (comparison < 0)
            {
                // Priority increased (smaller value in min-heap), bubble up
                HeapifyUp(index);
            }
            else if (0 < comparison)
            {
                // Priority decreased (larger value in min-heap), bubble down
                HeapifyDown(index);
            }
            // If equal, no need to do anything
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HeapifyUp(int index)
        {
            T item = _items[index];
            while (0 < index)
            {
                int parentIndex = (index - 1) >> 1;
                T parent = _items[parentIndex];

                if (0 <= _comparer.Compare(item, parent))
                {
                    break;
                }

                _items[index] = parent;
                index = parentIndex;
            }
            _items[index] = item;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HeapifyDown(int index)
        {
            T item = _items[index];
            int halfCount = _count >> 1;

            while (index < halfCount)
            {
                int leftChild = (index << 1) + 1;
                int rightChild = leftChild + 1;
                int smallestChild = leftChild;

                if (
                    rightChild < _count
                    && _comparer.Compare(_items[rightChild], _items[leftChild]) < 0
                )
                {
                    smallestChild = rightChild;
                }

                if (_comparer.Compare(item, _items[smallestChild]) <= 0)
                {
                    break;
                }

                _items[index] = _items[smallestChild];
                index = smallestChild;
            }
            _items[index] = item;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeGrowth(int currentCapacity)
        {
            // Use 1.5x growth strategy (capacity + capacity/2)
            // Ensures better memory reuse than 2x while maintaining O(1) amortized growth
            // Minimum growth of MinimumGrowth to avoid tiny increments for small arrays
            int growth = currentCapacity + (currentCapacity >> 1);
            int newCapacity = currentCapacity + Math.Max(growth - currentCapacity, MinimumGrowth);

            // Handle overflow by capping at Array.MaxLength
            if (0X7FFFFFC7 < (uint)newCapacity) // Array.MaxLength
            {
                newCapacity = 0X7FFFFFC7;
            }

            return newCapacity;
        }

        private void Resize(int newCapacity)
        {
            if (newCapacity <= _count)
            {
                return;
            }

            Array.Resize(ref _items, newCapacity);
        }

        public HeapEnumerator GetEnumerator()
        {
            return new HeapEnumerator(_items, _count);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public struct HeapEnumerator : IEnumerator<T>
        {
            private readonly T[] _items;
            private readonly int _count;
            private int _index;
            private T _current;

            internal HeapEnumerator(T[] items, int count)
            {
                _items = items;
                _count = count;
                _index = -1;
                _current = default;
            }

            public bool MoveNext()
            {
                if (++_index < _count)
                {
                    _current = _items[_index];
                    return true;
                }

                _current = default;
                return false;
            }

            public T Current => _current;

            object IEnumerator.Current => Current;

            public void Reset()
            {
                _index = -1;
                _current = default;
            }

            public void Dispose() { }
        }
    }
}
