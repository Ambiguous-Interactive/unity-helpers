// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// A set implemented as a <b>struct</b>, which <see cref="SerializableSetBase{T, TSet}"/> used to
    /// forbid outright.
    /// </summary>
    /// <remarks>
    /// Nothing about <see cref="ISet{T}"/> requires a class, and an inline or pooled buffer is a
    /// natural reason to implement one on a struct. Deliberately lazy about its backing store, so
    /// <c>default(StructIntSet)</c> is a legal empty value -- which is what makes a
    /// <c>_set == null</c> assumption in the base class impossible to hide.
    /// </remarks>
    [Serializable]
    public struct StructIntSet : ISet<int>
    {
        private HashSet<int> _items;

        private HashSet<int> Items => _items ??= new HashSet<int>();

        /// <inheritdoc />
        public int Count => _items == null ? 0 : _items.Count;

        /// <inheritdoc />
        public bool IsReadOnly => false;

        /// <inheritdoc />
        public bool Add(int item)
        {
            return Items.Add(item);
        }

        void ICollection<int>.Add(int item)
        {
            Items.Add(item);
        }

        /// <inheritdoc />
        public void Clear()
        {
            _items = null;
        }

        /// <inheritdoc />
        public bool Contains(int item)
        {
            return _items != null && _items.Contains(item);
        }

        /// <inheritdoc />
        public void CopyTo(int[] array, int arrayIndex)
        {
            _items?.CopyTo(array, arrayIndex);
        }

        /// <inheritdoc />
        public void ExceptWith(IEnumerable<int> other)
        {
            Items.ExceptWith(other);
        }

        /// <inheritdoc />
        public void IntersectWith(IEnumerable<int> other)
        {
            Items.IntersectWith(other);
        }

        /// <inheritdoc />
        public bool IsProperSubsetOf(IEnumerable<int> other)
        {
            return Items.IsProperSubsetOf(other);
        }

        /// <inheritdoc />
        public bool IsProperSupersetOf(IEnumerable<int> other)
        {
            return Items.IsProperSupersetOf(other);
        }

        /// <inheritdoc />
        public bool IsSubsetOf(IEnumerable<int> other)
        {
            return Items.IsSubsetOf(other);
        }

        /// <inheritdoc />
        public bool IsSupersetOf(IEnumerable<int> other)
        {
            return Items.IsSupersetOf(other);
        }

        /// <inheritdoc />
        public bool Overlaps(IEnumerable<int> other)
        {
            return Items.Overlaps(other);
        }

        /// <inheritdoc />
        public bool Remove(int item)
        {
            return _items != null && _items.Remove(item);
        }

        /// <inheritdoc />
        public bool SetEquals(IEnumerable<int> other)
        {
            return Items.SetEquals(other);
        }

        /// <inheritdoc />
        public void SymmetricExceptWith(IEnumerable<int> other)
        {
            Items.SymmetricExceptWith(other);
        }

        /// <inheritdoc />
        public void UnionWith(IEnumerable<int> other)
        {
            Items.UnionWith(other);
        }

        /// <summary>
        /// Returns a non-boxing enumerator, which is what <c>foreach</c> binds to.
        /// </summary>
        /// <returns>The enumerator.</returns>
        public HashSet<int>.Enumerator GetEnumerator()
        {
            return (_items ?? Empty).GetEnumerator();
        }

        IEnumerator<int> IEnumerable<int>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private static readonly HashSet<int> Empty = new HashSet<int>();
    }

    /// <summary>
    /// A serialized set whose backing store is a struct.
    /// </summary>
    /// <remarks>
    /// The declaration is the test: <c>where TSet : class</c> on the base rejected this at the
    /// consumer's own type, so the relaxation is only real if this compiles.
    /// </remarks>
    [Serializable]
    public sealed class StructBackedSerializableSet : SerializableSetBase<int, StructIntSet> { }
}
