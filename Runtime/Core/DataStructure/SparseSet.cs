// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using ProtoBuf;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// A sparse set data structure optimized for fast O(1) add, remove, contains, and dense iteration.
    /// Perfect for ECS-style architectures, entity component storage, and scenarios where you need
    /// fast membership testing combined with cache-friendly iteration over active elements.
    /// Elements must be non-negative integers within a specified universe size.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// SparseSet activeEntities = new SparseSet(1024);
    /// activeEntities.Add(entityId);
    /// if (activeEntities.Contains(entityId))
    /// {
    ///     UpdateEntity(entityId);
    /// }
    /// ]]></code>
    /// </example>
    [Serializable]
#pragma warning disable WPROTO030 // Served at the root through SparseSetProtoWrapper.
    [ProtoContract(IgnoreListHandling = true)]
#pragma warning restore WPROTO030
    public sealed class SparseSet : IReadOnlyList<int>
    {
        [SerializeField]
        [ProtoMember(1)]
        private int[] _sparse;

        [SerializeField]
        [ProtoMember(2)]
        private int[] _dense;

        [SerializeField]
        [ProtoMember(3)]
        private int _count;

        /// <summary>
        /// Gets the number of elements in the set.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Gets whether the set is empty.
        /// </summary>
        public bool IsEmpty => _count == 0;

        /// <summary>
        /// Gets the maximum value that can be stored in this sparse set (universe size).
        /// </summary>
        public int Capacity => _sparse.Length;

        /// <summary>
        /// Gets the element at the specified index in dense order.
        /// </summary>
        public int this[int index]
        {
            get
            {
                if (index < 0 || _count <= index)
                {
                    throw new IndexOutOfRangeException(
                        $"{index} is outside of bounds [0, {_count})"
                    );
                }
                return _dense[index];
            }
        }

        private SparseSet()
        {
            _sparse = Array.Empty<int>();
            _dense = Array.Empty<int>();
            _count = 0;
        }

        /// <summary>
        /// Constructs a sparse set with the specified universe size.
        /// </summary>
        /// <param name="universeSize">The maximum value that can be stored + 1.</param>
        public SparseSet(int universeSize)
        {
            if (universeSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(universeSize),
                    "Universe size must be positive."
                );
            }

            _sparse = new int[universeSize];
            _dense = new int[universeSize];
            _count = 0;
        }

        /// <summary>
        /// Adds an element to the set in O(1) time.
        /// If the element is already in the set, does nothing.
        /// </summary>
        /// <param name="value">The value to add (must be in range [0, universeSize)).</param>
        /// <returns>True if added, false if already present.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryAdd(int value)
        {
            if (value < 0 || _sparse.Length <= value)
            {
                return false;
            }

            if (Contains(value))
            {
                return false;
            }

            _dense[_count] = value;
            _sparse[value] = _count;
            _count++;
            return true;
        }

        /// <summary>
        /// Removes an element from the set in O(1) time.
        /// </summary>
        /// <param name="value">The value to remove.</param>
        /// <returns>True if removed, false if not present or invalid.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRemove(int value)
        {
            if (value < 0 || _sparse.Length <= value || !Contains(value))
            {
                return false;
            }

            int indexInDense = _sparse[value];
            int lastElement = _dense[_count - 1];

            // Swap with last element
            _dense[indexInDense] = lastElement;
            _sparse[lastElement] = indexInDense;

            _count--;
            return true;
        }

        /// <summary>
        /// Checks if an element is in the set in O(1) time.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int value)
        {
            if (value < 0 || _sparse.Length <= value)
            {
                return false;
            }

            int index = _sparse[value];
            return index < _count && _dense[index] == value;
        }

        /// <summary>
        /// Removes all elements from the set in O(1) time.
        /// </summary>
        public void Clear()
        {
            _count = 0;
        }

        /// <summary>
        /// Attempts to get the element at the specified index.
        /// </summary>
        public bool TryGet(int index, out int value)
        {
            if (index < 0 || _count <= index)
            {
                value = default;
                return false;
            }
            value = _dense[index];
            return true;
        }

        /// <summary>
        /// Copies all elements to an array.
        /// </summary>
        public void CopyTo(int[] array, int arrayIndex)
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

            Array.Copy(_dense, 0, array, arrayIndex, _count);
        }

        /// <summary>
        /// Returns an array containing all elements.
        /// </summary>
        public int[] ToArray()
        {
            int[] result = null;
            _ = ToArray(ref result);
            return result;
        }

        public int ToArray(ref int[] result)
        {
            if (result == null || result.Length < _count)
            {
                result = new int[_count];
            }
            Array.Copy(_dense, 0, result, 0, _count);
            return _count;
        }

        /// <summary>
        /// Populates the provided list with all elements.
        /// Clears the list before adding. Returns the same list for chaining.
        /// </summary>
        public List<int> ToList(List<int> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            for (int i = 0; i < _count; i++)
            {
                results.Add(_dense[i]);
            }
            return results;
        }

        public SparseSetEnumerator GetEnumerator()
        {
            return new SparseSetEnumerator(_dense, _count);
        }

        IEnumerator<int> IEnumerable<int>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public struct SparseSetEnumerator : IEnumerator<int>
        {
            private readonly int[] _dense;
            private readonly int _count;
            private int _index;
            private int _current;

            internal SparseSetEnumerator(int[] dense, int count)
            {
                _dense = dense;
                _count = count;
                _index = -1;
                _current = default;
            }

            public bool MoveNext()
            {
                if (++_index < _count)
                {
                    _current = _dense[_index];
                    return true;
                }

                _current = default;
                return false;
            }

            public int Current => _current;

            object IEnumerator.Current => Current;

            public void Reset()
            {
                _index = -1;
                _current = default;
            }

            public void Dispose() { }
        }
    }

    /// <summary>
    /// A generic sparse set that maps elements of type T to internal indices.
    /// Provides O(1) operations with support for any element type.
    /// </summary>
    [Serializable]
    public sealed class SparseSet<T> : IReadOnlyList<T>
    {
        private readonly Dictionary<T, int> _elementToIndex;
        private readonly T[] _elements;
        private readonly int[] _sparse;
        private readonly int[] _dense;
        private int _count;
        private int _nextIndex;
        private readonly IEqualityComparer<T> _comparer;

        /// <summary>
        /// Gets the number of elements in the set.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Gets whether the set is empty.
        /// </summary>
        public bool IsEmpty => _count == 0;

        /// <summary>
        /// Gets the maximum number of elements that can be stored.
        /// </summary>
        public int Capacity => _sparse.Length;

        /// <summary>
        /// Gets the element at the specified index in dense order.
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
                int elementIndex = _dense[index];
                return _elements[elementIndex];
            }
        }

        /// <summary>
        /// Constructs a sparse set with the specified capacity.
        /// </summary>
        public SparseSet(int capacity, IEqualityComparer<T> comparer = null)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    "Capacity must be positive."
                );
            }

            _comparer = comparer ?? EqualityComparer<T>.Default;
            _elementToIndex = new Dictionary<T, int>(_comparer);
            _elements = new T[capacity];
            _sparse = new int[capacity];
            _dense = new int[capacity];
            _count = 0;
            _nextIndex = 0;
        }

        /// <summary>
        /// Adds an element to the set in O(1) amortized time.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryAdd(T element)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            if (_elements.Length <= _nextIndex)
            {
                return false; // Capacity reached
            }

            if (!_elementToIndex.TryAdd(element, _nextIndex + 1))
            {
                return false;
            }

            int index = _nextIndex++;
            _elements[index] = element;
            _dense[_count] = index;
            _sparse[index] = _count;
            _count++;
            return true;
        }

        /// <summary>
        /// Removes an element from the set in O(1) time.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRemove(T element)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            if (!_elementToIndex.Remove(element, out int index))
            {
                return false;
            }

            int indexInDense = _sparse[index];
            int lastDenseIndex = _dense[_count - 1];

            // Swap with last element
            _dense[indexInDense] = lastDenseIndex;
            _sparse[lastDenseIndex] = indexInDense;
            _elements[index] = default;
            _count--;
            return true;
        }

        /// <summary>
        /// Checks if an element is in the set in O(1) time.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(T element)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            return _elementToIndex.ContainsKey(element);
        }

        /// <summary>
        /// Removes all elements from the set.
        /// </summary>
        public void Clear()
        {
            _elementToIndex.Clear();
            Array.Clear(_elements, 0, _nextIndex);
            _count = 0;
            _nextIndex = 0;
        }

        /// <summary>
        /// Attempts to get the element at the specified index.
        /// </summary>
        public bool TryGet(int index, out T element)
        {
            if (index < 0 || _count <= index)
            {
                element = default;
                return false;
            }
            int elementIndex = _dense[index];
            element = _elements[elementIndex];
            return true;
        }

        /// <summary>
        /// Copies all elements to an array.
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

            for (int i = 0; i < _count; i++)
            {
                int elementIndex = _dense[i];
                array[arrayIndex + i] = _elements[elementIndex];
            }
        }

        /// <summary>
        /// Returns an array containing all elements.
        /// </summary>
        public T[] ToArray()
        {
            T[] result = new T[_count];
            for (int i = 0; i < _count; i++)
            {
                int elementIndex = _dense[i];
                result[i] = _elements[elementIndex];
            }
            return result;
        }

        /// <summary>
        /// Populates the provided list with all elements.
        /// Clears the list before adding. Returns the same list for chaining.
        /// </summary>
        public List<T> ToList(List<T> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            for (int i = 0; i < _count; i++)
            {
                int elementIndex = _dense[i];
                results.Add(_elements[elementIndex]);
            }
            return results;
        }

        public SparseSetEnumerator GetEnumerator()
        {
            return new SparseSetEnumerator(_elements, _dense, _count);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public struct SparseSetEnumerator : IEnumerator<T>
        {
            private readonly T[] _elements;
            private readonly int[] _dense;
            private readonly int _count;
            private PooledArray<T> _pooledArray;
            private int _index;
            private T _current;
            private bool _initialized;

            internal SparseSetEnumerator(T[] elements, int[] dense, int count)
            {
                _elements = elements;
                _dense = dense;
                _count = count;
                _index = -1;
                _current = default;
                _initialized = false;
                _pooledArray = default;

                // Rent array and populate on first use
                if (0 < count)
                {
                    _pooledArray = SystemArrayPool<T>.Get(count, out T[] temp);
                    for (int i = 0; i < count; i++)
                    {
                        temp[i] = elements[dense[i]];
                    }
                    _initialized = true;
                }
            }

            public bool MoveNext()
            {
                if (++_index < _count)
                {
                    _current = _pooledArray.array[_index];
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

            public void Dispose()
            {
                if (_initialized)
                {
                    _pooledArray.Dispose();
                    _initialized = false;
                }
            }
        }
    }
}
