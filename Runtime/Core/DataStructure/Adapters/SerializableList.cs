// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure.Adapters
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using ProtoBuf;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters;

    /// <summary>
    /// Unity-serializable box around a <see cref="List{T}"/>, for use as the element or value type of
    /// another serialized collection.
    /// Unity does not serialize a nested collection, so a field typed <c>List&lt;T&gt;[]</c> or
    /// <c>List&lt;List&lt;T&gt;&gt;</c> is dropped without a warning; wrapping the inner collection in
    /// this class restores the extra layer of indirection Unity requires.
    /// Use it whenever a <see cref="SerializableDictionary{TKey, TValue}"/> value, or a
    /// <see cref="SerializableHashSet{T}"/> element, is itself a collection.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// public sealed class LootTables : MonoBehaviour
    /// {
    ///     // SerializableDictionary<string, List<int>> would serialize its keys and silently drop
    ///     // every value, because Unity refuses the resulting List<int>[] backing field.
    ///     [SerializeField]
    ///     private SerializableDictionary<string, SerializableList<int>> _weightsByTier = new();
    ///
    ///     public void AddWeight(string tier, int weight)
    ///     {
    ///         if (!_weightsByTier.TryGetValue(tier, out SerializableList<int> weights))
    ///         {
    ///             weights = new SerializableList<int>();
    ///             _weightsByTier[tier] = weights;
    ///         }
    ///
    ///         weights.Add(weight);
    ///     }
    /// }
    /// ]]></code>
    /// </example>
    /// <typeparam name="T">Element type. Must itself be Unity-serializable for the inspector to persist it.</typeparam>
    /// <remarks>
    /// Equality is reference equality, matching <see cref="List{T}"/>. Two instances holding equal
    /// elements are not equal, so this type is a poor <see cref="SerializableHashSet{T}"/> element
    /// unless the containing set is keyed on identity.
    /// <para>
    /// Out-of-range handling is deliberately split. Every member that can express "did nothing"
    /// does: <see cref="RemoveAt"/> and <see cref="CopyTo"/> no-op, <see cref="Insert"/> clamps,
    /// and a null sequence is ignored. The indexer is the one member with no way to signal failure
    /// -- it must produce a <typeparamref name="T"/> -- so it throws exactly as
    /// <see cref="List{T}"/> does, because returning <c>default</c> for a bad index would hand the
    /// caller a value that looks authored.
    /// </para>
    /// </remarks>
    [Serializable]
    [ProtoContract]
    [JsonConverter(typeof(SerializableListConverterFactory))]
    public sealed class SerializableList<T> : IList<T>, IReadOnlyList<T>
    {
        static SerializableList()
        {
            ProtobufUnityModel.EnsureInitialized();
        }

        internal static class SerializedPropertyNames
        {
            internal const string Items = nameof(_items);
        }

        [SerializeField]
        [ProtoMember(1, OverwriteList = true)]
        private List<T> _items = new();

        /// <summary>
        /// Gets the number of elements currently stored.
        /// </summary>
        /// <example>
        /// <code><![CDATA[
        /// SerializableList<int> weights = new SerializableList<int> { 1, 2, 3 };
        /// int count = weights.Count;
        /// ]]></code>
        /// </example>
        public int Count => _items == null ? 0 : _items.Count;

        /// <summary>
        /// Gets a value indicating whether the list rejects mutation. Always <c>false</c>.
        /// </summary>
        /// <example>
        /// <code><![CDATA[
        /// SerializableList<int> weights = new SerializableList<int>();
        /// bool readOnly = weights.IsReadOnly;
        /// ]]></code>
        /// </example>
        public bool IsReadOnly => false;

        /// <summary>
        /// Gets or sets the element at the supplied index, matching <see cref="List{T}"/> bounds semantics.
        /// </summary>
        /// <param name="index">Zero-based index of the element.</param>
        /// <returns>The element stored at <paramref name="index"/>.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableList<int> weights = new SerializableList<int> { 1, 2, 3 };
        /// weights[0] = 10;
        /// int first = weights[0];
        /// ]]></code>
        /// </example>
        public T this[int index]
        {
            get
            {
                List<T> items = _items;
                if (items == null || index < 0 || items.Count <= index)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(index),
                        $"{index} is outside of bounds [0, {Count})"
                    );
                }

                return items[index];
            }
            set
            {
                List<T> items = _items;
                if (items == null || index < 0 || items.Count <= index)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(index),
                        $"{index} is outside of bounds [0, {Count})"
                    );
                }

                items[index] = value;
            }
        }

        /// <summary>
        /// Creates an empty list.
        /// </summary>
        /// <example>
        /// <code><![CDATA[
        /// SerializableList<int> weights = new SerializableList<int>();
        /// ]]></code>
        /// </example>
        public SerializableList() { }

        /// <summary>
        /// Creates an empty list with room reserved for the supplied number of elements.
        /// Negative capacities are treated as zero.
        /// </summary>
        /// <param name="capacity">Number of elements to reserve space for.</param>
        /// <example>
        /// <code><![CDATA[
        /// SerializableList<int> weights = new SerializableList<int>(16);
        /// ]]></code>
        /// </example>
        public SerializableList(int capacity)
        {
            _items = new List<T>(Math.Max(0, capacity));
        }

        /// <summary>
        /// Creates a list holding a copy of the supplied elements. A <c>null</c> source produces an empty list.
        /// </summary>
        /// <param name="items">Elements to copy.</param>
        /// <example>
        /// <code><![CDATA[
        /// int[] seed = { 1, 2, 3 };
        /// SerializableList<int> weights = new SerializableList<int>(seed);
        /// ]]></code>
        /// </example>
        public SerializableList(IEnumerable<T> items)
        {
            _items = items == null ? new List<T>() : new List<T>(items);
        }

        /// <summary>
        /// Exposes the underlying list so callers can reach <see cref="List{T}"/> members such as
        /// <see cref="List{T}.Sort()"/> or <see cref="List{T}.BinarySearch(T)"/>. Mutating the result
        /// mutates this instance.
        /// </summary>
        /// <returns>The backing list, created on demand when serialization left it <c>null</c>.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableList<int> weights = new SerializableList<int> { 3, 1, 2 };
        /// weights.AsList().Sort();
        /// ]]></code>
        /// </example>
        public List<T> AsList()
        {
            return EnsureItems();
        }

        /// <summary>
        /// Appends an element.
        /// </summary>
        /// <param name="item">Element to append.</param>
        /// <example>
        /// <code><![CDATA[
        /// SerializableList<int> weights = new SerializableList<int>();
        /// weights.Add(42);
        /// ]]></code>
        /// </example>
        public void Add(T item)
        {
            EnsureItems().Add(item);
        }

        /// <summary>
        /// Appends every element of the supplied sequence. A <c>null</c> sequence is a no-op.
        /// </summary>
        /// <param name="items">Elements to append.</param>
        /// <example>
        /// <code><![CDATA[
        /// SerializableList<int> weights = new SerializableList<int>();
        /// weights.AddRange(new[] { 1, 2, 3 });
        /// ]]></code>
        /// </example>
        public void AddRange(IEnumerable<T> items)
        {
            if (items == null)
            {
                return;
            }

            EnsureItems().AddRange(items);
        }

        /// <summary>
        /// Removes every element.
        /// </summary>
        /// <example>
        /// <code><![CDATA[
        /// SerializableList<int> weights = new SerializableList<int> { 1, 2, 3 };
        /// weights.Clear();
        /// ]]></code>
        /// </example>
        public void Clear()
        {
            _items?.Clear();
        }

        /// <summary>
        /// Determines whether the supplied element is present.
        /// </summary>
        /// <param name="item">Element to look for.</param>
        /// <returns><c>true</c> when the element is present.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableList<int> weights = new SerializableList<int> { 1, 2, 3 };
        /// bool present = weights.Contains(2);
        /// ]]></code>
        /// </example>
        public bool Contains(T item)
        {
            List<T> items = _items;
            return items != null && items.Contains(item);
        }

        /// <summary>
        /// Copies every element into the supplied array. Invalid destinations are a no-op.
        /// </summary>
        /// <param name="array">Destination array.</param>
        /// <param name="arrayIndex">Index in the destination to start writing at.</param>
        /// <example>
        /// <code><![CDATA[
        /// SerializableList<int> weights = new SerializableList<int> { 1, 2, 3 };
        /// int[] destination = new int[3];
        /// weights.CopyTo(destination, 0);
        /// ]]></code>
        /// </example>
        public void CopyTo(T[] array, int arrayIndex)
        {
            List<T> items = _items;
            if (items == null || items.Count == 0)
            {
                return;
            }

            if (array == null || arrayIndex < 0 || array.Length - arrayIndex < items.Count)
            {
                return;
            }

            items.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Finds the index of the supplied element.
        /// </summary>
        /// <param name="item">Element to look for.</param>
        /// <returns>The zero-based index, or -1 when the element is absent.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableList<int> weights = new SerializableList<int> { 1, 2, 3 };
        /// int index = weights.IndexOf(3);
        /// ]]></code>
        /// </example>
        public int IndexOf(T item)
        {
            List<T> items = _items;
            return items == null ? -1 : items.IndexOf(item);
        }

        /// <summary>
        /// Inserts an element at the supplied index. Out-of-range indices are clamped into the list.
        /// </summary>
        /// <param name="index">Index to insert at.</param>
        /// <param name="item">Element to insert.</param>
        /// <example>
        /// <code><![CDATA[
        /// SerializableList<int> weights = new SerializableList<int> { 1, 3 };
        /// weights.Insert(1, 2);
        /// ]]></code>
        /// </example>
        public void Insert(int index, T item)
        {
            List<T> items = EnsureItems();
            items.Insert(Mathf.Clamp(index, 0, items.Count), item);
        }

        /// <summary>
        /// Removes the first occurrence of the supplied element.
        /// </summary>
        /// <param name="item">Element to remove.</param>
        /// <returns><c>true</c> when an element was removed.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableList<int> weights = new SerializableList<int> { 1, 2, 3 };
        /// bool removed = weights.Remove(2);
        /// ]]></code>
        /// </example>
        public bool Remove(T item)
        {
            List<T> items = _items;
            return items != null && items.Remove(item);
        }

        /// <summary>
        /// Removes the element at the supplied index. Out-of-range indices are a no-op.
        /// </summary>
        /// <param name="index">Index of the element to remove.</param>
        /// <example>
        /// <code><![CDATA[
        /// SerializableList<int> weights = new SerializableList<int> { 1, 2, 3 };
        /// weights.RemoveAt(0);
        /// ]]></code>
        /// </example>
        public void RemoveAt(int index)
        {
            List<T> items = _items;
            if (items == null || index < 0 || items.Count <= index)
            {
                return;
            }

            items.RemoveAt(index);
        }

        /// <summary>
        /// Returns an allocation-free enumerator over the elements.
        /// </summary>
        /// <returns>A <see cref="List{T}.Enumerator"/> over the backing list.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableList<int> weights = new SerializableList<int> { 1, 2, 3 };
        /// foreach (int weight in weights)
        /// {
        ///     Debug.Log(weight);
        /// }
        /// ]]></code>
        /// </example>
        public List<T>.Enumerator GetEnumerator()
        {
            return EnsureItems().GetEnumerator();
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return EnsureItems().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return EnsureItems().GetEnumerator();
        }

        /// <summary>
        /// Wraps an existing list. The returned instance shares storage with <paramref name="items"/>.
        /// </summary>
        /// <param name="items">List to wrap.</param>
        /// <example>
        /// <code><![CDATA[
        /// SerializableList<int> weights = new List<int> { 1, 2, 3 };
        /// ]]></code>
        /// </example>
        public static implicit operator SerializableList<T>(List<T> items)
        {
            return new SerializableList<T> { _items = items ?? new List<T>() };
        }

        /// <summary>
        /// Unwraps to the backing list. Mutating the result mutates the wrapper.
        /// </summary>
        /// <param name="items">Wrapper to unwrap. <c>null</c> unwraps to <c>null</c>.</param>
        /// <example>
        /// <code><![CDATA[
        /// SerializableList<int> weights = new SerializableList<int> { 1, 2, 3 };
        /// List<int> raw = weights;
        /// ]]></code>
        /// </example>
        public static implicit operator List<T>(SerializableList<T> items)
        {
            return items?.EnsureItems();
        }

        // Unity leaves the field non-null, but ProtoBuf and JSON can both produce an instance whose
        // backing list was never written, so every mutating path materializes it first.
        private List<T> EnsureItems()
        {
            return _items ??= new List<T>();
        }
    }

    internal static class SerializableListSerializedPropertyNames
    {
        internal const string Items = SerializableList<int>.SerializedPropertyNames.Items;
    }
}
