// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    /// An array-backed <see cref="IList{T}"/> that is neither a <c>T[]</c> nor a
    /// <see cref="List{T}"/>, so a caller reaching it takes the interface path.
    /// </summary>
    /// <remarks>
    /// Deliberately not sealed, and deliberately paired with a second unsealed implementation:
    /// a runtime that can prove the receiver's exact type devirtualizes the indexer and the
    /// interface path is never measured or exercised at all.
    /// </remarks>
    /// <typeparam name="T">The element type.</typeparam>
    public class IndexerOnlyList<T> : IList<T>
    {
        private T[] _storage = Array.Empty<T>();
        private int _count;

        /// <summary>Gets or sets the element at the given index.</summary>
        /// <param name="index">The index to read or write.</param>
        public T this[int index]
        {
            get
            {
                if (index < 0 || _count <= index)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _storage[index];
            }
            set
            {
                if (index < 0 || _count <= index)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                _storage[index] = value;
            }
        }

        /// <summary>Gets the number of elements held.</summary>
        public int Count => _count;

        /// <summary>Always false; this list is mutable.</summary>
        public bool IsReadOnly => false;

        /// <summary>Appends an element.</summary>
        /// <param name="item">The element to append.</param>
        public void Add(T item)
        {
            Grow(_count + 1);
            _storage[_count] = item;
            _count++;
        }

        /// <summary>Appends every element of a sequence.</summary>
        /// <param name="items">The elements to append.</param>
        public void AddRange(IEnumerable<T> items)
        {
            foreach (T item in items)
            {
                Add(item);
            }
        }

        /// <summary>Removes every element.</summary>
        public void Clear()
        {
            Array.Clear(_storage, 0, _count);
            _count = 0;
        }

        /// <summary>Determines whether the list holds an element.</summary>
        /// <param name="item">The element to look for.</param>
        /// <returns>True when the element is present.</returns>
        public bool Contains(T item) => 0 <= IndexOf(item);

        /// <summary>Copies every element into an array.</summary>
        /// <param name="array">The destination array.</param>
        /// <param name="arrayIndex">The destination offset.</param>
        public void CopyTo(T[] array, int arrayIndex)
        {
            Array.Copy(_storage, 0, array, arrayIndex, _count);
        }

        /// <summary>Enumerates the elements in order.</summary>
        /// <returns>An enumerator over the elements.</returns>
        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _count; ++i)
            {
                yield return _storage[i];
            }
        }

        /// <summary>Finds the index of an element.</summary>
        /// <param name="item">The element to look for.</param>
        /// <returns>The index, or -1 when absent.</returns>
        public int IndexOf(T item)
        {
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < _count; ++i)
            {
                if (comparer.Equals(_storage[i], item))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Inserts an element at an index.</summary>
        /// <param name="index">The index to insert at.</param>
        /// <param name="item">The element to insert.</param>
        public void Insert(int index, T item)
        {
            if (index < 0 || _count < index)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            Grow(_count + 1);
            Array.Copy(_storage, index, _storage, index + 1, _count - index);
            _storage[index] = item;
            _count++;
        }

        /// <summary>Removes the first occurrence of an element.</summary>
        /// <param name="item">The element to remove.</param>
        /// <returns>True when an element was removed.</returns>
        public bool Remove(T item)
        {
            int index = IndexOf(item);
            if (index < 0)
            {
                return false;
            }

            RemoveAt(index);
            return true;
        }

        /// <summary>Removes the element at an index.</summary>
        /// <param name="index">The index to remove.</param>
        public void RemoveAt(int index)
        {
            if (index < 0 || _count <= index)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            Array.Copy(_storage, index + 1, _storage, index, _count - index - 1);
            _count--;
            _storage[_count] = default;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void Grow(int required)
        {
            if (required <= _storage.Length)
            {
                return;
            }

            int capacity = _storage.Length == 0 ? 4 : _storage.Length * 2;
            while (capacity < required)
            {
                capacity *= 2;
            }

            Array.Resize(ref _storage, capacity);
        }
    }
}
