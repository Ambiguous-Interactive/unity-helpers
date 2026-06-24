// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure.Adapters
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using ProtoBuf;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Utils;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    internal interface ISerializableSetInspector
    {
        Type ElementType { get; }

        int UniqueCount { get; }

        int SerializedCount { get; }

        bool TryAddElement(object value, out object normalizedValue);

        bool ContainsElement(object value);

        bool RemoveElement(object value);

        void ClearElements();

        Array GetSerializedItemsSnapshot();

        void SetSerializedItemsSnapshot(Array values, bool preserveSerializedEntries);

        void SynchronizeSerializedState();

        bool SupportsSorting { get; }
    }

    internal interface ISerializableSetEditorSync
    {
        void EditorAfterDeserialize();
    }

    /// <summary>
    /// Shared infrastructure for Unity-friendly serialized sets.
    /// Synchronizes the serialized element array with a backing <see cref="ISet{T}"/> so Unity, ProtoBuf, and JSON stay in step with runtime mutations.
    /// Extend this class to build custom set types with specialized equality logic or editor behavior.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// [Serializable]
    /// public sealed class CaseInsensitiveTagSet : SerializableSetBase<string, HashSet<string>>
    /// {
    ///     public CaseInsensitiveTagSet()
    ///         : base(new HashSet<string>(StringComparer.OrdinalIgnoreCase))
    ///     {
    ///     }
    ///
    ///     protected override bool TryGetValueCore(string equalValue, out string actualValue)
    ///     {
    ///         foreach (string value in Set)
    ///         {
    ///             if (string.Equals(value, equalValue, StringComparison.OrdinalIgnoreCase))
    ///             {
    ///                 actualValue = value;
    ///                 return true;
    ///             }
    ///         }
    ///
    ///         actualValue = equalValue;
    ///         return false;
    ///     }
    /// }
    /// ]]></code>
    /// </example>
    [Serializable]
    [ProtoContract(IgnoreListHandling = true)]
    public abstract class SerializableSetBase<T, TSet>
        : ISet<T>,
            IReadOnlyCollection<T>,
            ISerializationCallbackReceiver,
            IDeserializationCallback,
            ISerializable,
            ISerializableSetInspector,
            ISerializableSetEditorSync
        where TSet : class, ISet<T>, new()
    {
        static SerializableSetBase()
        {
            ProtobufUnityModel.EnsureInitialized();
        }

        protected internal bool HasDuplicatesOrNulls => _hasDuplicatesOrNulls;

        internal bool PreserveSerializedEntries => _preserveSerializedEntries;
        public int Count => _set.Count;

        bool ICollection<T>.IsReadOnly => _set.IsReadOnly;
        protected TSet Set => _set;

        protected internal T[] SerializedItems => _items;

        protected virtual bool SupportsSorting => false;

        [SerializeField]
        [ProtoMember(1, OverwriteList = true)]
        [JsonInclude]
        protected internal T[] _items;

        [ProtoIgnore]
        protected internal TSet _set;

        [NonSerialized]
        protected internal bool _preserveSerializedEntries;

        [NonSerialized]
        protected internal bool _itemsDirty;

        /// <summary>
        /// Tracks items added since the last serialization cycle, in insertion order.
        /// This is used to preserve the order in which items were added during the next serialization.
        /// </summary>
        [NonSerialized]
        protected internal List<T> _newItemsOrder;

        [NonSerialized]
        protected internal bool _hasDuplicatesOrNulls;

        /// <summary>
        /// Initializes an empty set. Required for protobuf deserialization.
        /// </summary>
        protected SerializableSetBase()
        {
            _set = new TSet();
        }

        protected SerializableSetBase(TSet set)
        {
            _set = set ?? throw new ArgumentNullException(nameof(set));
        }

        protected SerializableSetBase(
            SerializationInfo serializationInfo,
            StreamingContext streamingContext,
            Func<SerializationInfo, StreamingContext, TSet> factory
        )
        {
            if (serializationInfo == null)
            {
                throw new ArgumentNullException(nameof(serializationInfo));
            }

            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            _set = factory(serializationInfo, streamingContext);
        }

        /// <summary>
        /// Unity inspector helper for identifying serialized array property names.
        /// </summary>
        internal static class SerializedPropertyNames
        {
            private sealed class NameHolder : SerializableHashSet<T>
            {
                public const string ItemsName = nameof(_items);
            }

            internal const string ItemsNameInternal = NameHolder.ItemsName;
        }

        /// <summary>
        /// Adds an element to the set and updates the serialized cache when the value was not already present.
        /// </summary>
        /// <param name="item">The element to insert.</param>
        /// <returns><c>true</c> when the value was added.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> abilities = new SerializableHashSet<string>();
        /// bool added = abilities.Add("Dash");
        /// ]]></code>
        /// </example>
        public bool Add(T item)
        {
            bool added = _set.Add(item);
            if (added)
            {
                TrackNewItem(item);
                MarkSerializationCacheDirty();
            }

            return added;
        }

        /// <summary>
        /// Tracks a newly added item for order preservation during serialization.
        /// </summary>
        private void TrackNewItem(T item)
        {
            _newItemsOrder ??= new List<T>();
            _newItemsOrder.Add(item);
        }

        void ICollection<T>.Add(T item)
        {
            Add(item);
        }

        /// <summary>
        /// Adds all values from the provided sequence to the set.
        /// </summary>
        /// <param name="other">Values to union into this set.</param>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> abilities = new SerializableHashSet<string>();
        /// string[] unlocks = new string[] { "Dash", "Grapple" };
        /// abilities.UnionWith(unlocks);
        /// ]]></code>
        /// </example>
        public void UnionWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            // Track items that will be added for order preservation
            foreach (T item in other)
            {
                if (_set.Add(item))
                {
                    TrackNewItem(item);
                }
            }
            MarkSerializationCacheDirty();
        }

        /// <summary>
        /// Removes any element that is not contained in the provided sequence.
        /// </summary>
        /// <param name="other">Sequence that defines the intersection.</param>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> abilities = new SerializableHashSet<string>();
        /// string[] allowed = new string[] { "Dash" };
        /// abilities.IntersectWith(allowed);
        /// ]]></code>
        /// </example>
        public void IntersectWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            _set.IntersectWith(other);
            // Items may have been removed, so clear tracked new items
            // (they may no longer be in the set)
            _newItemsOrder?.Clear();
            MarkSerializationCacheDirty();
        }

        /// <summary>
        /// Removes all elements that appear in the provided sequence.
        /// </summary>
        /// <param name="other">Sequence whose members should be removed.</param>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> abilities = new SerializableHashSet<string>();
        /// string[] deprecated = new string[] { "Dash" };
        /// abilities.ExceptWith(deprecated);
        /// ]]></code>
        /// </example>
        public void ExceptWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            _set.ExceptWith(other);
            // Items may have been removed, so clear tracked new items
            _newItemsOrder?.Clear();
            MarkSerializationCacheDirty();
        }

        /// <summary>
        /// Modifies the set so it contains elements that appear in exactly one of the sequences.
        /// </summary>
        /// <param name="other">Sequence whose elements are compared against the current set.</param>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> first = new SerializableHashSet<string>();
        /// first.Add("Dash");
        /// string[] second = new string[] { "Dash", "Grapple" };
        /// first.SymmetricExceptWith(second);
        /// ]]></code>
        /// </example>
        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            _set.SymmetricExceptWith(other);
            // Items may have been added or removed, so clear tracked new items
            _newItemsOrder?.Clear();
            MarkSerializationCacheDirty();
        }

        /// <summary>
        /// Determines whether the set is a subset of the provided sequence.
        /// </summary>
        /// <param name="other">Sequence to compare against.</param>
        /// <returns><c>true</c> when every element exists in the other sequence.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> storyUnlocks = new SerializableHashSet<string>();
        /// bool subset = storyUnlocks.IsSubsetOf(new string[] { "Dash", "DoubleJump" });
        /// ]]></code>
        /// </example>
        public bool IsSubsetOf(IEnumerable<T> other)
        {
            return _set.IsSubsetOf(other);
        }

        /// <summary>
        /// Determines whether the set contains all values found in the provided sequence.
        /// </summary>
        /// <param name="other">Sequence that must be contained in the set.</param>
        /// <returns><c>true</c> when the set is a superset.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> storyUnlocks = new SerializableHashSet<string>();
        /// bool superset = storyUnlocks.IsSupersetOf(new string[] { "Dash" });
        /// ]]></code>
        /// </example>
        public bool IsSupersetOf(IEnumerable<T> other)
        {
            return _set.IsSupersetOf(other);
        }

        /// <summary>
        /// Determines whether the set strictly contains all values from the other sequence and has additional elements.
        /// </summary>
        /// <param name="other">Sequence that must be contained in the set.</param>
        /// <returns><c>true</c> when the set is a proper superset.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> storyUnlocks = new SerializableHashSet<string>();
        /// bool properSuperset = storyUnlocks.IsProperSupersetOf(new string[] { "Dash" });
        /// ]]></code>
        /// </example>
        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            return _set.IsProperSupersetOf(other);
        }

        /// <summary>
        /// Determines whether the set is strictly contained inside the provided sequence.
        /// </summary>
        /// <param name="other">Sequence that must contain every element plus at least one additional element.</param>
        /// <returns><c>true</c> when the set is a proper subset.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> storyUnlocks = new SerializableHashSet<string>();
        /// bool properSubset = storyUnlocks.IsProperSubsetOf(new string[] { "Dash", "Grapple" });
        /// ]]></code>
        /// </example>
        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            return _set.IsProperSubsetOf(other);
        }

        /// <summary>
        /// Determines whether the set shares any element with the provided sequence.
        /// </summary>
        /// <param name="other">Sequence to compare to.</param>
        /// <returns><c>true</c> when at least one value is shared.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> storyUnlocks = new SerializableHashSet<string>();
        /// bool overlaps = storyUnlocks.Overlaps(new string[] { "Dash" });
        /// ]]></code>
        /// </example>
        public bool Overlaps(IEnumerable<T> other)
        {
            return _set.Overlaps(other);
        }

        /// <summary>
        /// Determines whether this set and the provided sequence contain the exact same elements.
        /// </summary>
        /// <param name="other">Sequence to compare against.</param>
        /// <returns><c>true</c> when both contain identical members.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> storyUnlocks = new SerializableHashSet<string>();
        /// bool matches = storyUnlocks.SetEquals(new string[] { "Dash", "DoubleJump" });
        /// ]]></code>
        /// </example>
        public bool SetEquals(IEnumerable<T> other)
        {
            return _set.SetEquals(other);
        }

        /// <summary>
        /// Removes every element from the set and clears the serialized cache.
        /// </summary>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> storyUnlocks = new SerializableHashSet<string>();
        /// storyUnlocks.Clear();
        /// ]]></code>
        /// </example>
        public void Clear()
        {
            if (_set.Count == 0)
            {
                return;
            }

            _set.Clear();
            _newItemsOrder?.Clear();
            MarkSerializationCacheDirty();
        }

        /// <summary>
        /// Determines whether the set contains the specified element.
        /// </summary>
        /// <param name="item">The element to look up.</param>
        /// <returns><c>true</c> when the element exists.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> storyUnlocks = new SerializableHashSet<string>();
        /// bool hasDash = storyUnlocks.Contains("Dash");
        /// ]]></code>
        /// </example>
        public bool Contains(T item)
        {
            return _set.Contains(item);
        }

        /// <summary>
        /// Retrieves the stored value that compares equal to the supplied value.
        /// </summary>
        /// <param name="equalValue">The candidate value.</param>
        /// <param name="actualValue">Receives the canonical value from the set.</param>
        /// <returns><c>true</c> when a matching element is found.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> storyUnlocks = new SerializableHashSet<string>();
        /// string normalized;
        /// bool found = storyUnlocks.TryGetValue("Dash", out normalized);
        /// ]]></code>
        /// </example>
        public bool TryGetValue(T equalValue, out T actualValue)
        {
            if (TryGetValueCore(equalValue, out T resolved))
            {
                actualValue = resolved;
                return true;
            }

            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            foreach (T value in _set)
            {
                if (comparer.Equals(value, equalValue))
                {
                    actualValue = value;
                    return true;
                }
            }

            actualValue = default;
            return false;
        }

        /// <summary>
        /// Allows derived types to substitute custom lookup behavior (for example, when values are wrapped).
        /// </summary>
        /// <param name="equalValue">The candidate value.</param>
        /// <param name="actualValue">Receives the resolved value.</param>
        /// <returns><c>true</c> when the derived type resolved a match.</returns>
        /// <example>
        /// <code><![CDATA[
        /// protected override bool TryGetValueCore(string equalValue, out string actualValue)
        /// {
        ///     foreach (string stored in SerializedItems)
        ///     {
        ///         if (string.Equals(stored, equalValue, StringComparison.OrdinalIgnoreCase))
        ///         {
        ///             actualValue = stored;
        ///             return true;
        ///         }
        ///     }
        ///     actualValue = null;
        ///     return false;
        /// }
        /// ]]></code>
        /// </example>
        protected virtual bool TryGetValueCore(T equalValue, out T actualValue)
        {
            actualValue = default;
            return false;
        }

        /// <summary>
        /// Creates a new array containing all elements in the set's natural iteration order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Returns elements in the order determined by the underlying <see cref="HashSet{T}"/>'s iteration order.
        /// This matches the behavior of enumerating a <see cref="HashSet{T}"/> and standard set semantics.
        /// </para>
        /// <para>
        /// The returned array is always a defensive copy - modifications to it do not affect the set.
        /// For empty sets, <see cref="Array.Empty{T}()"/> is returned.
        /// </para>
        /// <para>
        /// To retrieve elements in their user-defined serialization order (as shown in the Unity inspector),
        /// use <see cref="ToPersistedOrderArray"/> instead.
        /// </para>
        /// </remarks>
        /// <returns>A new array containing all elements in set iteration order.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> abilities = new SerializableHashSet<string>();
        /// abilities.Add("Dash");
        /// abilities.Add("Jump");
        /// string[] abilityArray = abilities.ToArray();
        /// ]]></code>
        /// </example>
        public virtual T[] ToArray()
        {
            int count = _set.Count;
            if (count == 0)
            {
                return Array.Empty<T>();
            }

            // Return elements in set iteration order (from the underlying set)
            T[] result = new T[count];
            _set.CopyTo(result, 0);
            return result;
        }

        /// <summary>
        /// Creates a new array containing all elements in their user-defined serialization order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Returns elements in the order they appear in the serialized backing array, which reflects
        /// the user-defined order from the Unity inspector. This order is preserved across domain
        /// reloads and serialization cycles.
        /// </para>
        /// <para>
        /// The returned array is always a defensive copy - modifications to it do not affect the set.
        /// For empty sets, <see cref="Array.Empty{T}()"/> is returned.
        /// </para>
        /// <para>
        /// To retrieve elements in the set's natural iteration order, use <see cref="ToArray"/> instead.
        /// </para>
        /// </remarks>
        /// <returns>A new array containing all elements in serialization order.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> abilities = new SerializableHashSet<string>();
        /// // After inspector reordering, elements might be in a custom order
        /// string[] abilityArray = abilities.ToPersistedOrderArray(); // Returns elements in inspector order
        /// ]]></code>
        /// </example>
        public virtual T[] ToPersistedOrderArray()
        {
            int count = _set.Count;
            if (count == 0)
            {
                return Array.Empty<T>();
            }

            // Ensure serialized state is current before reading from _items
            if (_items == null || _itemsDirty || _items.Length != count)
            {
                OnBeforeSerialize();
            }

            // Return a defensive copy preserving user-defined order
            T[] result = new T[count];
            Array.Copy(_items, result, count);
            return result;
        }

        /// <summary>
        /// Copies the elements into the provided array starting at index zero.
        /// </summary>
        /// <param name="array">Destination array.</param>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> storyUnlocks = new SerializableHashSet<string>();
        /// string[] snapshot = new string[storyUnlocks.Count];
        /// storyUnlocks.CopyTo(snapshot);
        /// ]]></code>
        /// </example>
        public void CopyTo(T[] array)
        {
            CopyTo(array, 0);
        }

        /// <summary>
        /// Copies the elements into the provided array starting at the given index.
        /// </summary>
        /// <param name="array">Destination array.</param>
        /// <param name="arrayIndex">Index in <paramref name="array"/> where copying begins.</param>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> storyUnlocks = new SerializableHashSet<string>();
        /// string[] snapshot = new string[storyUnlocks.Count + 2];
        /// storyUnlocks.CopyTo(snapshot, 1);
        /// ]]></code>
        /// </example>
        public void CopyTo(T[] array, int arrayIndex)
        {
            _set.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Removes a single element from the set and updates the serialized cache.
        /// </summary>
        /// <param name="item">The element to remove.</param>
        /// <returns><c>true</c> when the value existed.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> storyUnlocks = new SerializableHashSet<string>();
        /// bool removed = storyUnlocks.Remove("Dash");
        /// ]]></code>
        /// </example>
        public bool Remove(T item)
        {
            bool removed = _set.Remove(item);
            if (removed)
            {
                // Remove from tracked new items if present
                _newItemsOrder?.Remove(item);
                MarkSerializationCacheDirty();
            }

            return removed;
        }

        /// <summary>
        /// Removes every element that satisfies the provided predicate.
        /// </summary>
        /// <param name="match">Condition that determines which elements are removed.</param>
        /// <returns>The number of elements removed.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> storyUnlocks = new SerializableHashSet<string>();
        /// int removed = storyUnlocks.RemoveWhere(id => id.Contains("Beta"));
        /// ]]></code>
        /// </example>
        public int RemoveWhere(Predicate<T> match)
        {
            if (match == null)
            {
                throw new ArgumentNullException(nameof(match));
            }

            int removed = RemoveWhereInternal(match);
            if (removed > 0)
            {
                // Remove matching items from tracked new items
                _newItemsOrder?.RemoveAll(match);
                MarkSerializationCacheDirty();
            }

            return removed;
        }

        /// <summary>
        /// Allows derived types to customize how batch removals are handled.
        /// </summary>
        /// <param name="match">The predicate describing which elements to remove.</param>
        /// <returns>The number of removed elements.</returns>
        protected virtual int RemoveWhereInternal(Predicate<T> match)
        {
            using PooledResource<List<T>> bufferResource = Buffers<T>.List.Get(out List<T> buffer);
            foreach (T value in _set)
            {
                if (match(value))
                {
                    buffer.Add(value);
                }
            }

            foreach (T value in buffer)
            {
                _set.Remove(value);
            }

            return buffer.Count;
        }

        /// <inheritdoc />
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return _set.GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_set).GetEnumerator();
        }

        /// <summary>
        /// Copies the live set contents into the serialized backing array before Unity or ProtoBuf serialization.
        /// </summary>
        /// <remarks>
        /// <para>
        /// When a serialized array already exists from a previous deserialization, this method preserves its
        /// order while synchronizing with the runtime set. This ensures that the user-defined order of elements
        /// as shown in the Unity inspector is maintained across domain reloads and serialization cycles.
        /// </para>
        /// <para>
        /// The synchronization process:
        /// <list type="bullet">
        /// <item><description>Existing elements: Kept in their original order if still in the set</description></item>
        /// <item><description>Removed elements: Filtered out from the array</description></item>
        /// <item><description>New elements: Appended to the end of the array</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// hashSet.OnBeforeSerialize();
        /// var snapshot = hashSet.SerializedItems;
        /// </code>
        /// </example>
        public void OnBeforeSerialize()
        {
            // If we have valid items with duplicates/nulls and should preserve them,
            // skip sync entirely to maintain the inspector's view of problematic data.
            if (
                _preserveSerializedEntries
                && _items != null
                && !_itemsDirty
                && _hasDuplicatesOrNulls
            )
            {
                return;
            }

            // If we have valid items and should preserve order, sync while maintaining order
            if (_preserveSerializedEntries && _items != null && !_itemsDirty)
            {
                SyncSerializedItemsPreservingOrder();
                return;
            }

            // If items exist but are dirty, try to preserve order while applying changes
            if (_items != null && _itemsDirty)
            {
                SyncSerializedItemsPreservingOrder();
                _itemsDirty = false;
                _preserveSerializedEntries = true;
                return;
            }

            // No existing items - build from scratch (set's natural order)
            int count = _set.Count;
            _items = new T[count];
            _set.CopyTo(_items, 0);
            _preserveSerializedEntries = true;
            _itemsDirty = false;
        }

        /// <summary>
        /// Synchronizes the serialized items array with the set while preserving the existing order.
        /// New items are appended, removed items are filtered out.
        /// </summary>
        private void SyncSerializedItemsPreservingOrder()
        {
            int setCount = _set.Count;
            int arrayLength = _items.Length;

            // Fast path: if counts match, all array items are unique, and all items still exist in the set, no changes needed.
            // We must check for uniqueness because duplicate items in the array can make counts match by coincidence
            // (e.g., array has {3, 3} with setCount=2 after adding item 4, but the array should become {3, 4}).
            if (setCount == arrayLength)
            {
                using PooledResource<HashSet<T>> fastPathSeenResource = Buffers<T>.HashSet.Get(
                    out HashSet<T> fastPathSeenItems
                );

                bool allItemsMatchAndUnique = true;
                for (int i = 0; i < arrayLength; i++)
                {
                    T item = _items[i];
                    // Check both that the item exists in the set AND that it's not a duplicate in the array
                    if (!_set.Contains(item) || !fastPathSeenItems.Add(item))
                    {
                        allItemsMatchAndUnique = false;
                        break;
                    }
                }

                if (allItemsMatchAndUnique)
                {
                    // Clear any tracked new items since no changes needed
                    _newItemsOrder?.Clear();
                    return;
                }
            }

            // Need to rebuild array while preserving order of existing items
            using PooledResource<List<T>> itemsResource = Buffers<T>.List.Get(out List<T> newItems);
            using PooledResource<HashSet<T>> seenResource = Buffers<T>.HashSet.Get(
                out HashSet<T> seenItems
            );

            // First pass: keep existing items that still exist in the set, in their original order
            for (int i = 0; i < arrayLength; i++)
            {
                T item = _items[i];
                if (_set.Contains(item) && seenItems.Add(item))
                {
                    newItems.Add(item);
                }
            }

            // Second pass: append new items in the order they were added (if tracked)
            if (_newItemsOrder is { Count: > 0 })
            {
                foreach (T item in _newItemsOrder)
                {
                    // Only add if it still exists in the set and wasn't already seen
                    if (_set.Contains(item) && seenItems.Add(item))
                    {
                        newItems.Add(item);
                    }
                }
            }
            else
            {
                // Fallback: iterate over the set for items not in the original array
                // (order may not match insertion order)
                foreach (T item in _set)
                {
                    if (seenItems.Add(item))
                    {
                        newItems.Add(item);
                    }
                }
            }

            // Rebuild array
            _items = newItems.ToArray();

            // Clear the tracked new items since they're now in the serialized array
            _newItemsOrder?.Clear();
        }

        /// <summary>
        /// Reconstructs the live set from the serialized array after Unity or ProtoBuf deserialization.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The serialized array represents the user-defined order of elements as they appear in the Unity inspector.
        /// This order is preserved across domain reloads and serialization cycles. The internal set maintains its
        /// natural ordering for efficient operations, but the serialized array always reflects the user's intended order.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// hashSet.OnAfterDeserialize();
        /// bool contains = hashSet.Contains(item);
        /// </code>
        /// </example>
        public void OnAfterDeserialize()
        {
            OnAfterDeserializeInternal(suppressWarnings: false);
        }

        void ISerializableSetEditorSync.EditorAfterDeserialize()
        {
            OnAfterDeserializeInternal(suppressWarnings: true);
        }

        private void OnAfterDeserializeInternal(bool suppressWarnings)
        {
            if (_items == null)
            {
                _preserveSerializedEntries = false;
                _itemsDirty = true;
                _set.Clear();
                return;
            }

            _set.Clear();
            bool hasDuplicates = false;
            bool encounteredNullReference = false;
            bool supportsNullCheck = TypeSupportsNullReferences(typeof(T));
            for (int index = 0; index < _items.Length; index++)
            {
                T value = _items[index];
                if (supportsNullCheck && ReferenceEquals(value, null))
                {
                    encounteredNullReference = true;
                    if (!suppressWarnings)
                    {
                        LogNullEntrySkip(index);
                    }
                    continue;
                }

                if (!_set.Add(value) && !hasDuplicates)
                {
                    hasDuplicates = true;
                }
            }

            // Always preserve the serialized array after deserialization to maintain user-defined order.
            // The array represents the order as it appears in the Unity inspector, which should not
            // change due to domain reloads. Only runtime modifications via Add/Remove/Clear should
            // trigger array rebuilding (handled by MarkSerializationCacheDirty).
            _preserveSerializedEntries = true;
            _itemsDirty = false;

            // Clear tracked new items since we're starting fresh after deserialization
            _newItemsOrder?.Clear();

            // Track if we have duplicates/nulls that require special handling in the editor
            _hasDuplicatesOrNulls = hasDuplicates || encounteredNullReference;
        }

        private static bool TypeSupportsNullReferences(Type type)
        {
            return type != null
                && (!type.IsValueType || typeof(UnityEngine.Object).IsAssignableFrom(type));
        }

        private static void LogNullEntrySkip(int index)
        {
#if UNITY_EDITOR
            if (!EditorShouldLog())
            {
                return;
            }
#endif
            // Recoverable, handled condition: the null entry is skipped and serialization continues
            // with no data corruption, so this is a Warning, not an Error (Error is reserved for
            // unrecoverable faults). Logging at Error also fails PlayMode tests via
            // LogAssert.NoUnexpectedReceived even though nothing is wrong.
            Debug.LogWarning(
                $"SerializableSet<{typeof(T).FullName}> skipped serialized entry at index {index} because the value reference was null."
            );
        }

#if UNITY_EDITOR
        private static bool EditorShouldLog()
        {
            try
            {
                return EditorApplication.isPlayingOrWillChangePlaymode;
            }
            catch (UnityException)
            {
                return false;
            }
        }
#endif

        [ProtoBeforeSerialization]
        protected internal void OnProtoBeforeSerialization()
        {
            OnBeforeSerialize();
        }

        [ProtoAfterSerialization]
        protected internal void OnProtoAfterSerialization()
        {
            if (_preserveSerializedEntries)
            {
                return;
            }

            _items = null;
        }

        [ProtoAfterDeserialization]
        protected internal void OnProtoAfterDeserialization()
        {
            if (_set == null)
            {
                _set = new TSet();
            }

            if (_items == null)
            {
                T[] rebuiltItems = new T[_set.Count];
                _set.CopyTo(rebuiltItems, 0);
                _items = rebuiltItems;
                _preserveSerializedEntries = true;
                _itemsDirty = false;
            }

            OnAfterDeserialize();
        }

        /// <summary>
        /// Completes deserialization after <see cref="SerializationInfo"/> data has been applied.
        /// </summary>
        /// <param name="sender">Reserved for future use.</param>
        public void OnDeserialization(object sender)
        {
            if (_set is IDeserializationCallback callback)
            {
                callback.OnDeserialization(sender);
            }
        }

        /// <summary>
        /// Writes the serialized representation of the set into a <see cref="SerializationInfo"/> instance.
        /// </summary>
        /// <param name="info">The serialization store to populate.</param>
        /// <param name="context">Context for the serialization process.</param>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (_set is ISerializable serializable)
            {
                serializable.GetObjectData(info, context);
            }
        }

        protected void MarkSerializationCacheDirty()
        {
            _preserveSerializedEntries = false;
            _itemsDirty = true;
            // Clear the duplicates/nulls flag since we're invalidating the serialization cache.
            // After a mutation, the set's internal state becomes the source of truth, and a Set
            // data structure cannot contain duplicates by definition. The flag will be recalculated
            // during the next OnAfterDeserialize if needed.
            _hasDuplicatesOrNulls = false;
            // Note: We intentionally do NOT null out _items here to preserve order information
            // for SyncSerializedItemsPreservingOrder() during the next OnBeforeSerialize() call.
        }

        /// <summary>
        /// Returns a JSON string describing the serialized items for quick debugging.
        /// </summary>
        /// <returns>A JSON representation of the set.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableHashSet<string> storyUnlocks = new SerializableHashSet<string>();
        /// string snapshot = storyUnlocks.ToString();
        /// ]]></code>
        /// </example>
        public override string ToString()
        {
            return this.ToJson();
        }

        Type ISerializableSetInspector.ElementType => typeof(T);

        int ISerializableSetInspector.UniqueCount => _set.Count;

        int ISerializableSetInspector.SerializedCount => _items?.Length ?? _set.Count;

        bool ISerializableSetInspector.SupportsSorting => SupportsSorting;

        bool ISerializableSetInspector.TryAddElement(object value, out object normalizedValue)
        {
            if (!TryConvertToElement(value, out T converted))
            {
                normalizedValue = default(T);
                return false;
            }

            bool added = _set.Add(converted);
            if (added)
            {
                MarkSerializationCacheDirty();
            }

            normalizedValue = converted;
            return added;
        }

        bool ISerializableSetInspector.ContainsElement(object value)
        {
            if (!TryConvertToElement(value, out T converted))
            {
                return false;
            }

            return _set.Contains(converted);
        }

        bool ISerializableSetInspector.RemoveElement(object value)
        {
            if (!TryConvertToElement(value, out T converted))
            {
                return false;
            }

            bool removed = _set.Remove(converted);
            if (removed)
            {
                MarkSerializationCacheDirty();
            }

            return removed;
        }

        void ISerializableSetInspector.ClearElements()
        {
            if (_set.Count == 0 && (_items == null || _items.Length == 0))
            {
                return;
            }

            _set.Clear();
            _items = null;
            _preserveSerializedEntries = false;
        }

        Array ISerializableSetInspector.GetSerializedItemsSnapshot()
        {
            if (_items is { Length: > 0 })
            {
                return (T[])_items.Clone();
            }

            if (_set.Count == 0)
            {
                return Array.Empty<T>();
            }

            T[] snapshot = new T[_set.Count];
            _set.CopyTo(snapshot, 0);
            return snapshot;
        }

        void ISerializableSetInspector.SetSerializedItemsSnapshot(
            Array values,
            bool preserveSerializedEntries
        )
        {
            if (values == null || values.Length == 0)
            {
                _items = null;
                _preserveSerializedEntries = false;
                _hasDuplicatesOrNulls = false;
                _itemsDirty = false;
                _set.Clear();
                return;
            }

            int length = values.Length;
            T[] convertedItems = new T[length];
            for (int index = 0; index < length; index++)
            {
                object raw = values.GetValue(index);
                if (!TryConvertToElement(raw, out T converted))
                {
                    converted = default;
                }

                convertedItems[index] = converted;
            }

            _items = convertedItems;
            _preserveSerializedEntries = preserveSerializedEntries;
            _itemsDirty = false;

            bool hasDuplicates = false;
            bool hasNulls = false;
            bool supportsNullCheck = TypeSupportsNullReferences(typeof(T));
            _set.Clear();
            foreach (T convertedItem in convertedItems)
            {
                if (supportsNullCheck && ReferenceEquals(convertedItem, null))
                {
                    hasNulls = true;
                }
                else if (!_set.Add(convertedItem))
                {
                    hasDuplicates = true;
                }
            }

            _hasDuplicatesOrNulls = hasDuplicates || hasNulls;
        }

        void ISerializableSetInspector.SynchronizeSerializedState()
        {
            OnBeforeSerialize();
        }

        private bool TryConvertToElement(object value, out T result)
        {
            if (value is T typedValue)
            {
                result = typedValue;
                return true;
            }

            if (value == null)
            {
                if (default(T) == null)
                {
                    result = default;
                    return true;
                }

                result = default;
                return false;
            }

            Type elementType = typeof(T);

            if (elementType.IsInstanceOfType(value))
            {
                result = (T)value;
                return true;
            }

            try
            {
                if (elementType.IsEnum)
                {
                    if (value is string enumName)
                    {
                        result = (T)Enum.Parse(elementType, enumName);
                        return true;
                    }

                    object enumValue = Enum.ToObject(elementType, value);
                    result = (T)enumValue;
                    return true;
                }

                if (value is IConvertible)
                {
                    object converted = Convert.ChangeType(
                        value,
                        elementType,
                        CultureInfo.InvariantCulture
                    );
                    result = (T)converted;
                    return true;
                }
            }
            catch
            {
                // Fallback handled below.
            }

            result = default;
            return false;
        }
    }

    /// <summary>
    /// Unity-serializable hash set that keeps elements deduplicated while remaining compatible with ProtoBuf and System.Text.Json.
    /// Perfect for authoring unlock lists, feature flags, and other boolean membership data in the inspector.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// [Serializable]
    /// public sealed class UnlockState : MonoBehaviour
    /// {
    ///     [SerializeField]
    ///     private SerializableHashSet<string> unlockedLevels = new SerializableHashSet<string>();
    ///
    ///     public bool HasUnlocked(string levelId)
    ///     {
    ///         return unlockedLevels.Contains(levelId);
    ///     }
    /// }
    /// ]]></code>
    /// </example>
    [Serializable]
    public class SerializableHashSet<T> : SerializableSetBase<T, HashSet<T>>
    {
        private sealed class StorageSet : HashSet<T>
        {
            /// <summary>
            /// Initializes an empty storage set using the default comparer.
            /// </summary>
            public StorageSet() { }

            /// <summary>
            /// Initializes an empty storage set that uses the provided comparer.
            /// </summary>
            /// <param name="comparer">Comparer passed to <see cref="HashSet{T}"/>.</param>
            public StorageSet(IEqualityComparer<T> comparer)
                : base(comparer) { }

            /// <summary>
            /// Initializes the storage set with the supplied elements and comparer.
            /// </summary>
            /// <param name="collection">Elements to copy into the backing set.</param>
            /// <param name="comparer">Comparer used to determine uniqueness.</param>
            public StorageSet(IEnumerable<T> collection, IEqualityComparer<T> comparer)
                : base(collection, comparer) { }

            /// <summary>
            /// Deserialization constructor used by <see cref="ISerializable"/>.
            /// </summary>
            /// <param name="info">Serialized data describing the set.</param>
            /// <param name="context">Context describing the serialization source.</param>
            public StorageSet(SerializationInfo info, StreamingContext context)
                : base(info, context) { }
        }

        /// <summary>
        /// Initializes an empty hash set compatible with Unity and ProtoBuf serialization.
        /// </summary>
        public SerializableHashSet()
            : base(new StorageSet()) { }

        /// <summary>
        /// Initializes an empty hash set using the supplied equality comparer.
        /// </summary>
        /// <param name="comparer">Comparer used to evaluate set membership. Defaults to <see cref="EqualityComparer{T}.Default"/> when <c>null</c>.</param>
        public SerializableHashSet(IEqualityComparer<T> comparer)
            : base(new StorageSet(comparer ?? EqualityComparer<T>.Default)) { }

        /// <summary>
        /// Initializes the set with elements copied from the provided collection.
        /// </summary>
        /// <param name="collection">Sequence whose elements are added to the set. <see cref="Array.Empty{T}"/> is used when <c>null</c>.</param>
        public SerializableHashSet(IEnumerable<T> collection)
            : base(new StorageSet(collection ?? Array.Empty<T>(), EqualityComparer<T>.Default)) { }

        /// <summary>
        /// Initializes the set with elements copied from the provided collection and comparer.
        /// </summary>
        /// <param name="collection">Sequence whose elements are added to the set. <see cref="Array.Empty{T}"/> is used when <c>null</c>.</param>
        /// <param name="comparer">Comparer used to evaluate set membership. Defaults to <see cref="EqualityComparer{T}.Default"/> when <c>null</c>.</param>
        public SerializableHashSet(IEnumerable<T> collection, IEqualityComparer<T> comparer)
            : base(
                new StorageSet(
                    collection ?? Array.Empty<T>(),
                    comparer ?? EqualityComparer<T>.Default
                )
            ) { }

        protected SerializableHashSet(SerializationInfo info, StreamingContext context)
            : base(
                info,
                context,
                (serializationInfo, streamingContext) =>
                    new StorageSet(serializationInfo, streamingContext)
            ) { }

        /// <summary>
        /// Gets the equality comparer used by the underlying hash set.
        /// </summary>
        public IEqualityComparer<T> Comparer => Set.Comparer;

        /// <summary>
        /// Creates a new <see cref="global::System.Collections.Generic.HashSet{T}"/> populated with this set's contents.
        /// </summary>
        /// <returns>A copy of the hash set's current state.</returns>
        public HashSet<T> ToHashSet()
        {
            HashSet<T> copy = new(Set, Set.Comparer);
            return copy;
        }

        /// <summary>
        /// Returns an enumerator that iterates through the set.
        /// </summary>
        public HashSet<T>.Enumerator GetEnumerator()
        {
            return Set.GetEnumerator();
        }

        /// <summary>
        /// Copies a subset of elements into the provided array starting at the specified index.
        /// </summary>
        /// <param name="array">Destination array that receives items.</param>
        /// <param name="arrayIndex">Zero-based index indicating where copying starts.</param>
        /// <param name="count">Number of elements to copy.</param>
        public void CopyTo(T[] array, int arrayIndex, int count)
        {
            Set.CopyTo(array, arrayIndex, count);
        }

        /// <summary>
        /// Resizes the underlying hash set to remove unused capacity.
        /// </summary>
        public void TrimExcess()
        {
            Set.TrimExcess();
        }

        protected override int RemoveWhereInternal(Predicate<T> match)
        {
            return Set.RemoveWhere(match);
        }

        protected override bool TryGetValueCore(T equalValue, out T actualValue)
        {
            return Set.TryGetValue(equalValue, out actualValue);
        }
    }

    internal static class SerializableHashSetSerializedPropertyNames
    {
        internal const string Items = SerializableHashSet<int>
            .SerializedPropertyNames
            .ItemsNameInternal;
    }
}
