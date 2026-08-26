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
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// A highly optimized double-ended queue (deque) implemented with a circular array.
    /// Supports efficient O(1) insertion and removal from both front and back.
    /// Ideal for BFS algorithms, undo/redo systems, and sliding window problems.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// Deque<Vector3> patrolPoints = new Deque<Vector3>();
    /// patrolPoints.PushBack(startPoint);
    /// patrolPoints.PushBack(nextPoint);
    /// Vector3 current = patrolPoints.PopFront();
    /// patrolPoints.PushBack(current); // cycle patrol
    /// ]]></code>
    /// </example>
    /// <typeparam name="T">The type of elements in the deque.</typeparam>
    [Serializable]
#pragma warning disable WPROTO030 // Served at the root through DequeProtoWrapper.
    [ProtoContract(IgnoreListHandling = true)]
#pragma warning restore WPROTO030
    public sealed class Deque<T> : IReadOnlyList<T>
    {
        public const int DefaultCapacity = 16;

        private const int MinimumGrowth = 4;

        [SerializeField]
        [ProtoIgnore]
        private T[] _items;

        [ProtoMember(1)]
        private List<T> _serializedItems;

        [ProtoIgnore]
        private PooledResource<List<T>> _serializedItemsLease;

        [SerializeField]
        [ProtoMember(2)]
        private int _head;

        [SerializeField]
        [ProtoMember(3)]
        private int _tail;

        [SerializeField]
        [ProtoMember(4)]
        private int _count;

        [SerializeField]
        [ProtoMember(5)]
        private int _serializedCapacity;

        /// <summary>
        /// Gets the number of elements in the deque.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Gets whether the deque is empty.
        /// </summary>
        public bool IsEmpty => _count == 0;

        /// <summary>
        /// Gets the current capacity of the underlying array.
        /// </summary>
        public int Capacity => _items.Length;

        /// <summary>
        /// Gets the element at the specified index (0 is front, Count-1 is back).
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
                int actualIndex = (_head + index) % _items.Length;
                return _items[actualIndex];
            }
            set
            {
                if (index < 0 || _count <= index)
                {
                    throw new IndexOutOfRangeException(
                        $"{index} is outside of bounds [0, {_count})"
                    );
                }
                int actualIndex = (_head + index) % _items.Length;
                _items[actualIndex] = value;
            }
        }

        private Deque()
        {
            _items = Array.Empty<T>();
            _head = 0;
            _tail = 0;
            _count = 0;
        }

        /// <summary>
        /// Constructs an empty deque with the specified capacity.
        /// </summary>
        public Deque(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    "Capacity must be positive."
                );
            }

            _items = new T[capacity];
            _head = 0;
            _tail = 0;
            _count = 0;
        }

        /// <summary>
        /// Constructs a deque from an existing collection.
        /// </summary>
        public Deque(IEnumerable<T> collection)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            _head = 0;
            _tail = 0;
            _count = 0;
            switch (collection)
            {
                case IReadOnlyList<T> list:
                {
                    int capacity = Math.Max(DefaultCapacity, list.Count);
                    _items = new T[capacity];
                    for (int i = 0; i < list.Count; i++)
                    {
                        PushBack(list[i]);
                    }

                    break;
                }
                case IReadOnlyCollection<T> readOnlyCollection:
                {
                    int capacity = Math.Max(DefaultCapacity, readOnlyCollection.Count);
                    _items = new T[capacity];
                    foreach (T item in readOnlyCollection)
                    {
                        PushBack(item);
                    }

                    break;
                }
                case ICollection<T> inputCollection:
                {
                    int capacity = Math.Max(DefaultCapacity, inputCollection.Count);
                    _items = new T[capacity];
                    foreach (T item in inputCollection)
                    {
                        PushBack(item);
                    }

                    break;
                }
                default:
                {
                    _items = new T[DefaultCapacity];
                    foreach (T item in collection)
                    {
                        PushBack(item);
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Adds an element to the front of the deque in O(1) time.
        /// </summary>
        public void PushFront(T item)
        {
            if (_count == _items.Length)
            {
                Resize(ComputeGrowth(_items.Length));
            }

            _head = _head.WrappedAdd(-1, _items.Length);
            _items[_head] = item;
            _count++;
        }

        /// <summary>
        /// Adds an element to the back of the deque in O(1) time.
        /// </summary>
        public void PushBack(T item)
        {
            if (_count == _items.Length)
            {
                Resize(ComputeGrowth(_items.Length));
            }

            _items[_tail] = item;
            _tail = _tail.WrappedIncrement(_items.Length);
            _count++;
        }

        /// <summary>
        /// Attempts to remove and return the element at the front of the deque.
        /// </summary>
        /// <returns>True if an element was removed, false if empty.</returns>
        public bool TryPopFront(out T result)
        {
            if (_count == 0)
            {
                result = default;
                return false;
            }

            T popped = _items[_head];
            _items[_head] = default;
            _head = _head.WrappedIncrement(_items.Length);
            _count--;
            result = popped;
            return true;
        }

        /// <summary>
        /// Attempts to remove and return the element at the back of the deque.
        /// </summary>
        /// <returns>True if an element was removed, false if empty.</returns>
        public bool TryPopBack(out T result)
        {
            if (_count == 0)
            {
                result = default;
                return false;
            }

            _tail = _tail.WrappedAdd(-1, _items.Length);
            T popped = _items[_tail];
            _items[_tail] = default;
            _count--;
            result = popped;
            return true;
        }

        /// <summary>
        /// Attempts to peek at the front element without removing it.
        /// </summary>
        /// <returns>True if the deque is not empty, false otherwise.</returns>
        public bool TryPeekFront(out T result)
        {
            if (_count == 0)
            {
                result = default;
                return false;
            }
            result = _items[_head];
            return true;
        }

        /// <summary>
        /// Attempts to peek at the back element without removing it.
        /// </summary>
        /// <returns>True if the deque is not empty, false otherwise.</returns>
        public bool TryPeekBack(out T result)
        {
            if (_count == 0)
            {
                result = default;
                return false;
            }
            int backIndex = (_tail - 1 + _items.Length) % _items.Length;
            result = _items[backIndex];
            return true;
        }

        /// <summary>
        /// Removes all elements from the deque.
        /// </summary>
        public void Clear()
        {
            if (0 < _count)
            {
                if (_head < _tail)
                {
                    Array.Clear(_items, _head, _count);
                }
                else
                {
                    Array.Clear(_items, _head, _items.Length - _head);
                    Array.Clear(_items, 0, _tail);
                }
            }
            _head = 0;
            _tail = 0;
            _count = 0;
        }

        /// <summary>
        /// Checks if the deque contains a specific element in O(n) time.
        /// </summary>
        public bool Contains(T item)
        {
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < _count; i++)
            {
                int actualIndex = (_head + i) % _items.Length;
                if (comparer.Equals(_items[actualIndex], item))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Copies the deque elements to an array.
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
                int actualIndex = (_head + i) % _items.Length;
                array[arrayIndex + i] = _items[actualIndex];
            }
        }

        /// <summary>
        /// Returns an array containing all elements in order.
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
            CopyTo(result, 0);
            return _count;
        }

        /// <summary>
        /// Trims excess capacity to match the current count.
        /// </summary>
        public void TrimExcess()
        {
            int threshold = (int)(_items.Length * 0.9);
            if (threshold <= _count)
            {
                return;
            }

            int newCapacity = Math.Max(DefaultCapacity, _count);
            if (newCapacity < _items.Length)
            {
                Resize(newCapacity);
            }
        }

        [ProtoBeforeSerialization]
        private void OnProtoSerialize()
        {
            _serializedCapacity = _items.Length;

            if (_count == 0)
            {
                _serializedItemsLease.Dispose();
                _serializedItems = null;
                return;
            }

            // Return any previous lease before renting a new one
            _serializedItemsLease.Dispose();

            // Rent a temporary list to avoid allocations during serialization
            _serializedItemsLease = Buffers<T>.List.Get(out List<T> buffer);
            for (int i = 0; i < _count; i++)
            {
                int actualIndex = (_head + i) % _items.Length;
                buffer.Add(_items[actualIndex]);
            }

            _serializedItems = buffer;
        }

        [ProtoAfterSerialization]
        private void OnProtoSerialized()
        {
            // Release rented list back to pool
            _serializedItemsLease.Dispose();
            _serializedItems = null;
        }

        [ProtoAfterDeserialization]
        private void OnProtoDeserialized()
        {
            int itemCount = _serializedItems?.Count ?? 0;
            int capacity = _serializedCapacity;

            if (capacity <= 0)
            {
                capacity = 0 < itemCount ? itemCount : DefaultCapacity;
            }

            // A serialized capacity is a claim, not data: a payload carrying no items can ask for a
            // two-billion-element buffer. The deque grows on demand, so honoring less than was asked
            // for costs a later resize and nothing else.
            capacity = SerializationCapacityLimits.Clamp(capacity, itemCount);

            if (itemCount == 0)
            {
                _items = new T[capacity];
                _head = 0;
                _tail = 0;
                _count = 0;
                _serializedItems = null;
                _serializedCapacity = _items.Length;
                return;
            }

            _items = new T[capacity];
            for (int i = 0; i < itemCount; i++)
            {
                _items[i] = _serializedItems[i];
            }

            _head = 0;
            _count = itemCount;
            _tail = itemCount < capacity ? itemCount : 0;

            _serializedItems = null;
            _serializedCapacity = _items.Length;
            // Ensure no outstanding lease remains
            _serializedItemsLease.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeGrowth(int currentCapacity)
        {
            int growth = currentCapacity + (currentCapacity >> 1);
            int newCapacity = currentCapacity + Math.Max(growth - currentCapacity, MinimumGrowth);

            if (0X7FFFFFC7 < (uint)newCapacity)
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

            T[] newItems = new T[newCapacity];
            if (0 < _count)
            {
                if (_head < _tail)
                {
                    Array.Copy(_items, _head, newItems, 0, _count);
                }
                else
                {
                    int headToEnd = _items.Length - _head;
                    Array.Copy(_items, _head, newItems, 0, headToEnd);
                    Array.Copy(_items, 0, newItems, headToEnd, _tail);
                }
            }

            _items = newItems;
            _head = 0;
            _tail = _count;
        }

        public DequeEnumerator GetEnumerator()
        {
            return new DequeEnumerator(_items, _head, _count, _items.Length);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public struct DequeEnumerator : IEnumerator<T>
        {
            private readonly T[] _items;
            private readonly int _head;
            private readonly int _count;
            private readonly int _capacity;
            private int _index;
            private T _current;

            internal DequeEnumerator(T[] items, int head, int count, int capacity)
            {
                _items = items;
                _head = head;
                _count = count;
                _capacity = capacity;
                _index = -1;
                _current = default;
            }

            public bool MoveNext()
            {
                if (++_index < _count)
                {
                    int actualIndex = _head.WrappedAdd(_index, _capacity);
                    _current = _items[actualIndex];
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
