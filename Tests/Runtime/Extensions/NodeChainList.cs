// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    /// A <see cref="LinkedList{T}"/>-backed <see cref="IList{T}"/> whose storage is not contiguous
    /// at all, so nothing about it can be indexed as an array.
    /// </summary>
    /// <remarks>
    /// The second unsealed interface-only backing: two of them at one call site keep a runtime from
    /// proving the receiver's exact type and devirtualizing the indexer the sorts write back through.
    /// </remarks>
    /// <typeparam name="T">The element type.</typeparam>
    public class NodeChainList<T> : IList<T>
    {
        private readonly LinkedList<T> _chain = new();

        /// <summary>Gets or sets the element at the given index.</summary>
        /// <param name="index">The index to read or write.</param>
        public T this[int index]
        {
            get => NodeAt(index).Value;
            set => NodeAt(index).Value = value;
        }

        /// <summary>Gets the number of elements held.</summary>
        public int Count => _chain.Count;

        /// <summary>Always false; this list is mutable.</summary>
        public bool IsReadOnly => false;

        /// <summary>Appends an element.</summary>
        /// <param name="item">The element to append.</param>
        public void Add(T item) => _chain.AddLast(item);

        /// <summary>Appends every element of a sequence.</summary>
        /// <param name="items">The elements to append.</param>
        public void AddRange(IEnumerable<T> items)
        {
            foreach (T item in items)
            {
                _chain.AddLast(item);
            }
        }

        /// <summary>Removes every element.</summary>
        public void Clear() => _chain.Clear();

        /// <summary>Determines whether the list holds an element.</summary>
        /// <param name="item">The element to look for.</param>
        /// <returns>True when the element is present.</returns>
        public bool Contains(T item) => _chain.Contains(item);

        /// <summary>Copies every element into an array.</summary>
        /// <param name="array">The destination array.</param>
        /// <param name="arrayIndex">The destination offset.</param>
        public void CopyTo(T[] array, int arrayIndex) => _chain.CopyTo(array, arrayIndex);

        /// <summary>Enumerates the elements in order.</summary>
        /// <returns>An enumerator over the elements.</returns>
        public IEnumerator<T> GetEnumerator() => _chain.GetEnumerator();

        /// <summary>Finds the index of an element.</summary>
        /// <param name="item">The element to look for.</param>
        /// <returns>The index, or -1 when absent.</returns>
        public int IndexOf(T item)
        {
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            int index = 0;
            for (LinkedListNode<T> node = _chain.First; node != null; node = node.Next)
            {
                if (comparer.Equals(node.Value, item))
                {
                    return index;
                }

                index++;
            }

            return -1;
        }

        /// <summary>Inserts an element at an index.</summary>
        /// <param name="index">The index to insert at.</param>
        /// <param name="item">The element to insert.</param>
        public void Insert(int index, T item)
        {
            if (index == _chain.Count)
            {
                _chain.AddLast(item);
                return;
            }

            _chain.AddBefore(NodeAt(index), item);
        }

        /// <summary>Removes the first occurrence of an element.</summary>
        /// <param name="item">The element to remove.</param>
        /// <returns>True when an element was removed.</returns>
        public bool Remove(T item) => _chain.Remove(item);

        /// <summary>Removes the element at an index.</summary>
        /// <param name="index">The index to remove.</param>
        public void RemoveAt(int index) => _chain.Remove(NodeAt(index));

        IEnumerator IEnumerable.GetEnumerator() => _chain.GetEnumerator();

        private LinkedListNode<T> NodeAt(int index)
        {
            if (index < 0 || _chain.Count <= index)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            LinkedListNode<T> node = _chain.First;
            for (int i = 0; i < index; ++i)
            {
                node = node.Next;
            }

            return node;
        }
    }
}
