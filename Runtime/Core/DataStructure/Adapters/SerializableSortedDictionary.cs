// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure.Adapters
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using ProtoBuf;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /// <summary>
    /// Base implementation for Unity-friendly sorted dictionaries that preserves ordering across Unity, JSON, and ProtoBuf serialization.
    /// Coordinates the serialized key/value arrays with an underlying <see cref="SortedDictionary{TKey,TValue}"/> and lets derived types decide how values are cached.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// [Serializable]
    /// public sealed class QuestDefinition
    /// {
    ///     public string Title;
    /// }
    ///
    /// [Serializable]
    /// public sealed class QuestCache : SerializableDictionary.Cache<QuestDefinition>
    /// {
    /// }
    ///
    /// [Serializable]
    /// public sealed class QuestDictionary
    ///     : SerializableSortedDictionaryBase<string, QuestDefinition, QuestCache>
    /// {
    ///     public QuestDictionary()
    ///         : base(new SortedDictionary<string, QuestDefinition>(StringComparer.OrdinalIgnoreCase))
    ///     {
    ///     }
    ///
    ///     protected override QuestDefinition GetValue(QuestCache[] cache, int index)
    ///     {
    ///         return cache[index].Data;
    ///     }
    ///
    ///     protected override void SetValue(QuestCache[] cache, int index, QuestDefinition value)
    ///     {
    ///         cache[index] = new QuestCache { Data = value };
    ///     }
    /// }
    /// ]]></code>
    /// </example>
    [Serializable]
    [ProtoContract(IgnoreListHandling = true)]
    public abstract class SerializableSortedDictionaryBase<TKey, TValue, TValueCache>
        : IDictionary<TKey, TValue>,
            IDictionary,
            IReadOnlyDictionary<TKey, TValue>,
            ISerializationCallbackReceiver,
            IDeserializationCallback,
            ISerializable
        where TKey : IComparable<TKey>
    {
        private const string KeysSerializationName = "Keys";
        private const string ValuesSerializationName = "Values";

        internal bool HasDuplicatesOrNulls => _hasDuplicatesOrNulls;

        internal bool PreserveSerializedEntries => _preserveSerializedEntries;

        internal TKey[] SerializedKeys => _keys;

        internal TValueCache[] SerializedValues => _values;

        internal bool SerializationArraysDirty => _arraysDirty;

        [SerializeField]
        [ProtoMember(1, OverwriteList = true)]
        [JsonInclude]
        protected internal TKey[] _keys;

        [SerializeField]
        [ProtoMember(2, OverwriteList = true)]
        [JsonInclude]
        protected internal TValueCache[] _values;

        [ProtoIgnore]
        [JsonIgnore]
        protected internal SortedDictionary<TKey, TValue> _dictionary;

        [NonSerialized]
        protected internal bool _preserveSerializedEntries;

        [NonSerialized]
        protected internal bool _arraysDirty = true;

        [NonSerialized]
        protected internal bool _hasDuplicatesOrNulls;

        protected SerializableSortedDictionaryBase()
        {
            _dictionary = new SortedDictionary<TKey, TValue>();
        }

        protected SerializableSortedDictionaryBase(IDictionary<TKey, TValue> dictionary)
        {
            if (dictionary == null)
            {
                throw new ArgumentNullException(nameof(dictionary));
            }

            _dictionary = new SortedDictionary<TKey, TValue>();
            foreach (KeyValuePair<TKey, TValue> pair in dictionary)
            {
                _dictionary[pair.Key] = pair.Value;
            }
        }

        protected SerializableSortedDictionaryBase(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            _dictionary = new SortedDictionary<TKey, TValue>();

            _keys = (TKey[])info.GetValue(KeysSerializationName, typeof(TKey[]));
            _values = (TValueCache[])info.GetValue(ValuesSerializationName, typeof(TValueCache[]));
            OnAfterDeserialize();
        }

        protected abstract TValue GetValue(TValueCache[] cache, int index);

        protected abstract void SetValue(TValueCache[] cache, int index, TValue value);

        public int Count => _dictionary.Count;

        public bool IsReadOnly => ((IDictionary<TKey, TValue>)_dictionary).IsReadOnly;

        public ICollection<TKey> Keys => _dictionary.Keys;

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => _dictionary.Keys;

        public ICollection<TValue> Values => _dictionary.Values;

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => _dictionary.Values;

        /// <summary>
        /// Gets or sets the value associated with the provided key while preserving sorted order.
        /// </summary>
        /// <param name="key">The key to access.</param>
        /// <returns>The stored value.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scoreboard = new SerializableSortedDictionary<string, int>(StringComparer.Ordinal);
        /// scoreboard["Alice"] = 1200;
        /// int score = scoreboard["Alice"];
        /// ]]></code>
        /// </example>
        public TValue this[TKey key]
        {
            get => _dictionary[key];
            set
            {
                _dictionary[key] = value;
                MarkSerializationCacheDirty();
            }
        }

        /// <summary>
        /// Adds a new entry to the sorted dictionary and invalidates the serialized cache.
        /// </summary>
        /// <param name="key">The key to insert.</param>
        /// <param name="value">The value associated with the key.</param>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scoreboard = new SerializableSortedDictionary<string, int>(StringComparer.Ordinal);
        /// scoreboard.Add("Alice", 1200);
        /// ]]></code>
        /// </example>
        public void Add(TKey key, TValue value)
        {
            _dictionary.Add(key, value);
            MarkSerializationCacheDirty();
        }

        /// <summary>
        /// Attempts to add a new entry without throwing when the key already exists.
        /// </summary>
        /// <param name="key">The key to insert.</param>
        /// <param name="value">The value associated with the key.</param>
        /// <returns><c>true</c> when the entry was added.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scoreboard = new SerializableSortedDictionary<string, int>(StringComparer.Ordinal);
        /// bool added = scoreboard.TryAdd("Alice", 1200);
        /// ]]></code>
        /// </example>
        public bool TryAdd(TKey key, TValue value)
        {
            bool added = _dictionary.TryAdd(key, value);
            if (added)
            {
                MarkSerializationCacheDirty();
            }

            return added;
        }

        /// <summary>
        /// Determines whether the dictionary already contains the specified key.
        /// </summary>
        /// <param name="key">The key to look up.</param>
        /// <returns><c>true</c> when the key exists.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scoreboard = new SerializableSortedDictionary<string, int>(StringComparer.Ordinal);
        /// bool hasAlice = scoreboard.ContainsKey("Alice");
        /// ]]></code>
        /// </example>
        public bool ContainsKey(TKey key)
        {
            return _dictionary.ContainsKey(key);
        }

        /// <summary>
        /// Removes an entry by key and marks the serialized cache as dirty when the key existed.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        /// <returns><c>true</c> when the entry was removed.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scoreboard = new SerializableSortedDictionary<string, int>(StringComparer.Ordinal);
        /// bool removed = scoreboard.Remove("Alice");
        /// ]]></code>
        /// </example>
        public bool Remove(TKey key)
        {
            bool removed = _dictionary.Remove(key);
            if (removed)
            {
                MarkSerializationCacheDirty();
            }

            return removed;
        }

        /// <summary>
        /// Removes an entry and outputs the value that was previously stored.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        /// <param name="value">Receives the removed value.</param>
        /// <returns><c>true</c> when the key existed.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scoreboard = new SerializableSortedDictionary<string, int>(StringComparer.Ordinal);
        /// int score;
        /// bool removed = scoreboard.Remove("Alice", out score);
        /// ]]></code>
        /// </example>
        public bool Remove(TKey key, out TValue value)
        {
            bool removed = _dictionary.Remove(key, out value);
            if (removed)
            {
                MarkSerializationCacheDirty();
            }

            return removed;
        }

        /// <summary>
        /// Retrieves a value without throwing when the key is missing.
        /// </summary>
        /// <param name="key">The key to locate.</param>
        /// <param name="value">Outputs the value when found.</param>
        /// <returns><c>true</c> when the key exists.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scoreboard = new SerializableSortedDictionary<string, int>(StringComparer.Ordinal);
        /// int score;
        /// if (scoreboard.TryGetValue("Alice", out score))
        /// {
        ///     Debug.Log(score);
        /// }
        /// ]]></code>
        /// </example>
        public bool TryGetValue(TKey key, out TValue value)
        {
            return _dictionary.TryGetValue(key, out value);
        }

        /// <summary>
        /// Removes every entry from the dictionary and clears the serialized cache.
        /// </summary>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scoreboard = new SerializableSortedDictionary<string, int>(StringComparer.Ordinal);
        /// scoreboard.Clear();
        /// ]]></code>
        /// </example>
        public void Clear()
        {
            _dictionary.Clear();
            MarkSerializationCacheDirty();
        }

        /// <summary>
        /// Replaces the dictionary contents with the entries from another dictionary.
        /// </summary>
        /// <param name="dictionary">The source map to copy.</param>
        /// <example>
        /// <code><![CDATA[
        /// IDictionary<string, int> seed = new SortedDictionary<string, int>();
        /// SerializableSortedDictionary<string, int> scoreboard = new SerializableSortedDictionary<string, int>(StringComparer.Ordinal);
        /// scoreboard.CopyFrom(seed);
        /// ]]></code>
        /// </example>
        public void CopyFrom(IDictionary<TKey, TValue> dictionary)
        {
            if (dictionary == null)
            {
                throw new ArgumentNullException(nameof(dictionary));
            }

            _dictionary.Clear();
            foreach (KeyValuePair<TKey, TValue> pair in dictionary)
            {
                _dictionary[pair.Key] = pair.Value;
            }

            // Clear cached arrays since we're replacing all content
            _keys = null;
            _values = null;
            MarkSerializationCacheDirty();
        }

        /// <summary>
        /// Creates a new <see cref="global::System.Collections.Generic.SortedDictionary{TKey, TValue}"/> populated with this dictionary's contents.
        /// </summary>
        /// <returns>A copy of the sorted dictionary's current state.</returns>
        public SortedDictionary<TKey, TValue> ToSortedDictionary()
        {
            SortedDictionary<TKey, TValue> copy = new(_dictionary, _dictionary.Comparer);
            return copy;
        }

        /// <summary>
        /// Creates a new array containing all keys in sorted order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Returns keys in the natural sorted order determined by the key's <see cref="IComparable{T}"/> implementation.
        /// This matches the behavior of <see cref="SortedDictionary{TKey, TValue}.Keys"/> and standard sorted collection semantics.
        /// </para>
        /// <para>
        /// The returned array is always a defensive copy - modifications to it do not affect the dictionary.
        /// For empty dictionaries, <see cref="Array.Empty{TKey}()"/> is returned.
        /// </para>
        /// <para>
        /// To retrieve keys in their user-defined serialization order (as shown in the Unity inspector),
        /// use <see cref="ToPersistedOrderKeysArray"/> instead.
        /// </para>
        /// </remarks>
        /// <returns>A new array containing all keys in sorted order.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scores = new SerializableSortedDictionary<string, int>();
        /// scores["Charlie"] = 75;
        /// scores["Alice"] = 100;
        /// scores["Bob"] = 85;
        /// string[] keyArray = scores.ToKeysArray(); // Returns ["Alice", "Bob", "Charlie"]
        /// ]]></code>
        /// </example>
        public TKey[] ToKeysArray()
        {
            int count = _dictionary.Count;
            if (count == 0)
            {
                return Array.Empty<TKey>();
            }

            // Return keys in sorted order (from the underlying SortedDictionary)
            TKey[] result = new TKey[count];
            _dictionary.Keys.CopyTo(result, 0);
            return result;
        }

        /// <summary>
        /// Creates a new array containing all keys in their user-defined serialization order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Returns keys in the order they appear in the serialized backing array, which reflects
        /// the user-defined order from the Unity inspector. This order is preserved across domain
        /// reloads and serialization cycles.
        /// </para>
        /// <para>
        /// The returned array is always a defensive copy - modifications to it do not affect the dictionary.
        /// For empty dictionaries, <see cref="Array.Empty{TKey}()"/> is returned.
        /// </para>
        /// <para>
        /// To retrieve keys in their natural sorted order, use <see cref="ToKeysArray"/> instead.
        /// </para>
        /// </remarks>
        /// <returns>A new array containing all keys in serialization order.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scores = new SerializableSortedDictionary<string, int>();
        /// // After inspector reordering, keys might be in a custom order
        /// string[] keyArray = scores.ToPersistedOrderKeysArray(); // Returns keys in inspector order
        /// ]]></code>
        /// </example>
        public TKey[] ToPersistedOrderKeysArray()
        {
            int count = _dictionary.Count;
            if (count == 0)
            {
                return Array.Empty<TKey>();
            }

            // Ensure serialized state is current before reading from _keys.
            // Check both array structure validity AND that no mutations have occurred since last serialize.
            bool arraysValid =
                _preserveSerializedEntries
                && !_arraysDirty
                && _keys != null
                && _values != null
                && _keys.Length == count;
            if (!arraysValid)
            {
                OnBeforeSerialize();
            }

            // Return a defensive copy preserving user-defined order
            TKey[] result = new TKey[count];
            Array.Copy(_keys, result, count);
            return result;
        }

        /// <summary>
        /// Creates a new array containing all values in sorted key order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Returns values in the order determined by the sorted key order. The value at index <c>i</c>
        /// corresponds to the key at index <c>i</c> from <see cref="ToKeysArray"/>.
        /// This matches the behavior of <see cref="SortedDictionary{TKey, TValue}.Values"/> and standard sorted collection semantics.
        /// </para>
        /// <para>
        /// The returned array is always a defensive copy - modifications to it do not affect the dictionary.
        /// For empty dictionaries, <see cref="Array.Empty{TValue}()"/> is returned.
        /// </para>
        /// <para>
        /// To retrieve values in their user-defined serialization order (as shown in the Unity inspector),
        /// use <see cref="ToPersistedOrderValuesArray"/> instead.
        /// </para>
        /// </remarks>
        /// <returns>A new array containing all values in sorted key order.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scores = new SerializableSortedDictionary<string, int>();
        /// scores["Charlie"] = 75;
        /// scores["Alice"] = 100;
        /// scores["Bob"] = 85;
        /// int[] valueArray = scores.ToValuesArray(); // Returns [100, 85, 75] (sorted by key)
        /// ]]></code>
        /// </example>
        public TValue[] ToValuesArray()
        {
            int count = _dictionary.Count;
            if (count == 0)
            {
                return Array.Empty<TValue>();
            }

            // Return values in sorted key order (from the underlying SortedDictionary)
            TValue[] result = new TValue[count];
            _dictionary.Values.CopyTo(result, 0);
            return result;
        }

        /// <summary>
        /// Creates a new array containing all values in their user-defined serialization order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Returns values in the order they appear in the serialized backing array, which reflects
        /// the user-defined order from the Unity inspector. Values are aligned with keys - the value
        /// at index <c>i</c> corresponds to the key at index <c>i</c> from <see cref="ToPersistedOrderKeysArray"/>.
        /// </para>
        /// <para>
        /// The returned array is always a defensive copy - modifications to it do not affect the dictionary.
        /// For empty dictionaries, <see cref="Array.Empty{TValue}()"/> is returned.
        /// </para>
        /// <para>
        /// To retrieve values in their natural sorted key order, use <see cref="ToValuesArray"/> instead.
        /// </para>
        /// </remarks>
        /// <returns>A new array containing all values in serialization order.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scores = new SerializableSortedDictionary<string, int>();
        /// // After inspector reordering, values are aligned with their persisted key order
        /// int[] valueArray = scores.ToPersistedOrderValuesArray(); // Returns values in inspector order
        /// ]]></code>
        /// </example>
        public TValue[] ToPersistedOrderValuesArray()
        {
            int count = _dictionary.Count;
            if (count == 0)
            {
                return Array.Empty<TValue>();
            }

            // Ensure serialized state is current before reading from _values.
            // Check both array structure validity AND that no mutations have occurred since last serialize.
            bool arraysValid =
                _preserveSerializedEntries
                && !_arraysDirty
                && _keys != null
                && _values != null
                && _values.Length == count;
            if (!arraysValid)
            {
                OnBeforeSerialize();
            }

            // Return a defensive copy preserving user-defined order
            TValue[] result = new TValue[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = GetValue(_values, i);
            }

            return result;
        }

        /// <summary>
        /// Creates a new array containing all key-value pairs in sorted key order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Returns pairs in the natural sorted order determined by the key's <see cref="IComparable{T}"/> implementation.
        /// This matches the behavior of enumerating a <see cref="SortedDictionary{TKey, TValue}"/> and standard sorted collection semantics.
        /// </para>
        /// <para>
        /// The returned array is always a defensive copy - modifications to it do not affect the dictionary.
        /// For empty dictionaries, <see cref="Array.Empty{KeyValuePair{TKey,TValue}}()"/> is returned.
        /// </para>
        /// <para>
        /// To retrieve key-value pairs in their user-defined serialization order (as shown in the Unity inspector),
        /// use <see cref="ToPersistedOrderArray"/> instead.
        /// </para>
        /// </remarks>
        /// <returns>A new array containing all key-value pairs in sorted key order.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scores = new SerializableSortedDictionary<string, int>();
        /// scores["Charlie"] = 75;
        /// scores["Alice"] = 100;
        /// scores["Bob"] = 85;
        /// KeyValuePair<string, int>[] pairArray = scores.ToArray();
        /// // Returns [("Alice", 100), ("Bob", 85), ("Charlie", 75)] in sorted key order
        /// ]]></code>
        /// </example>
        public KeyValuePair<TKey, TValue>[] ToArray()
        {
            int count = _dictionary.Count;
            if (count == 0)
            {
                return Array.Empty<KeyValuePair<TKey, TValue>>();
            }

            // Return pairs in sorted key order (from the underlying SortedDictionary)
            KeyValuePair<TKey, TValue>[] result = new KeyValuePair<TKey, TValue>[count];
            int index = 0;
            foreach (KeyValuePair<TKey, TValue> pair in _dictionary)
            {
                result[index++] = pair;
            }

            return result;
        }

        /// <summary>
        /// Creates a new array containing all key-value pairs in their user-defined serialization order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Returns pairs in the order they appear in the serialized backing arrays, which reflects
        /// the user-defined order from the Unity inspector. This order is preserved across domain
        /// reloads and serialization cycles.
        /// </para>
        /// <para>
        /// The returned array is always a defensive copy - modifications to it do not affect the dictionary.
        /// For empty dictionaries, <see cref="Array.Empty{KeyValuePair{TKey,TValue}}()"/> is returned.
        /// </para>
        /// <para>
        /// To retrieve key-value pairs in their natural sorted key order, use <see cref="ToArray"/> instead.
        /// </para>
        /// </remarks>
        /// <returns>A new array containing all key-value pairs in serialization order.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scores = new SerializableSortedDictionary<string, int>();
        /// // After inspector reordering, pairs are in a custom order
        /// KeyValuePair<string, int>[] pairArray = scores.ToPersistedOrderArray(); // Returns pairs in inspector order
        /// ]]></code>
        /// </example>
        public KeyValuePair<TKey, TValue>[] ToPersistedOrderArray()
        {
            int count = _dictionary.Count;
            if (count == 0)
            {
                return Array.Empty<KeyValuePair<TKey, TValue>>();
            }

            // Ensure serialized state is current before reading from arrays.
            // Check both array structure validity AND that no mutations have occurred since last serialize.
            bool arraysValid =
                _preserveSerializedEntries
                && !_arraysDirty
                && _keys != null
                && _values != null
                && _keys.Length == count
                && _values.Length == count;
            if (!arraysValid)
            {
                OnBeforeSerialize();
            }

            // Return a defensive copy preserving user-defined order
            KeyValuePair<TKey, TValue>[] result = new KeyValuePair<TKey, TValue>[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = new KeyValuePair<TKey, TValue>(_keys[i], GetValue(_values, i));
            }

            return result;
        }

        /// <summary>
        /// Flushes the in-memory dictionary into the serialized key/value arrays prior to Unity or ProtoBuf serialization.
        /// </summary>
        /// <remarks>
        /// <para>
        /// When serialized arrays already exist from a previous deserialization, this method preserves their
        /// order while synchronizing values. This ensures that the user-defined order of entries as shown
        /// in the Unity inspector is maintained across domain reloads and serialization cycles.
        /// </para>
        /// <para>
        /// The synchronization process:
        /// <list type="bullet">
        /// <item><description>Existing keys: Values are updated in-place to reflect runtime changes</description></item>
        /// <item><description>Removed keys: Entries are filtered out from the arrays</description></item>
        /// <item><description>New keys: Appended to the end of the arrays</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// sortedDictionary.OnBeforeSerialize();
        /// var keys = sortedDictionary.SerializedKeys;
        /// </code>
        /// </example>
        public void OnBeforeSerialize()
        {
            bool arraysIntact = _keys != null && _values != null && _keys.Length == _values.Length;

            // If we have valid arrays with duplicates/nulls and should preserve them,
            // skip sync entirely to maintain the inspector's view of problematic data.
            if (
                arraysIntact
                && _preserveSerializedEntries
                && !_arraysDirty
                && _hasDuplicatesOrNulls
            )
            {
                return;
            }

            // If we have valid arrays and should preserve order, sync values while maintaining key order
            if (arraysIntact && _preserveSerializedEntries && !_arraysDirty)
            {
                SyncSerializedArraysPreservingOrder();
                return;
            }

            // If arrays exist but are dirty, try to preserve order while applying changes
            if (arraysIntact && _arraysDirty)
            {
                SyncSerializedArraysPreservingOrder();
                _arraysDirty = false;
                _preserveSerializedEntries = true;
                return;
            }

            // No existing arrays or they're inconsistent - build from scratch (sorted order)
            int count = _dictionary.Count;
            _keys = new TKey[count];
            _values = new TValueCache[count];

            int index = 0;
            foreach (KeyValuePair<TKey, TValue> pair in _dictionary)
            {
                _keys[index] = pair.Key;
                SetValue(_values, index, pair.Value);
                index++;
            }

            _preserveSerializedEntries = true;
            _arraysDirty = false;
        }

        /// <summary>
        /// Synchronizes the serialized arrays with the dictionary while preserving the existing key order.
        /// New keys are appended, removed keys are filtered out, and existing keys have their values updated.
        /// </summary>
        private void SyncSerializedArraysPreservingOrder()
        {
            int dictionaryCount = _dictionary.Count;
            int arrayLength = _keys.Length;

            // Fast path: if counts match and all keys exist, just update values in place
            if (dictionaryCount == arrayLength)
            {
                bool allKeysMatch = true;
                for (int i = 0; i < arrayLength; i++)
                {
                    TKey key = _keys[i];
                    if (key == null || !_dictionary.ContainsKey(key))
                    {
                        allKeysMatch = false;
                        break;
                    }
                }

                if (allKeysMatch)
                {
                    // Just update values in place, preserving key order
                    for (int i = 0; i < arrayLength; i++)
                    {
                        TKey key = _keys[i];
                        TValue value = _dictionary[key];
                        SetValue(_values, i, value);
                    }
                    return;
                }
            }

            // Need to rebuild arrays while preserving order of existing keys
            using PooledResource<List<TKey>> keysResource = Buffers<TKey>.List.Get(
                out List<TKey> newKeys
            );
            using PooledResource<List<TValue>> valuesResource = Buffers<TValue>.List.Get(
                out List<TValue> newValues
            );
            using PooledResource<HashSet<TKey>> seenResource = Buffers<TKey>.HashSet.Get(
                out HashSet<TKey> seenKeys
            );

            // First pass: keep existing keys that still exist in the dictionary, in their original order
            for (int i = 0; i < arrayLength; i++)
            {
                TKey key = _keys[i];
                if (key != null && _dictionary.TryGetValue(key, out TValue value))
                {
                    if (seenKeys.Add(key))
                    {
                        newKeys.Add(key);
                        newValues.Add(value);
                    }
                }
            }

            // Second pass: append new keys that weren't in the original arrays
            foreach (KeyValuePair<TKey, TValue> pair in _dictionary)
            {
                if (seenKeys.Add(pair.Key))
                {
                    newKeys.Add(pair.Key);
                    newValues.Add(pair.Value);
                }
            }

            // Rebuild arrays
            int newCount = newKeys.Count;
            _keys = new TKey[newCount];
            _values = new TValueCache[newCount];

            for (int i = 0; i < newCount; i++)
            {
                _keys[i] = newKeys[i];
                SetValue(_values, i, newValues[i]);
            }
        }

        /// <summary>
        /// Rehydrates the sorted dictionary from the serialized key/value arrays after Unity or ProtoBuf deserialization.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The serialized arrays represent the user-defined order of entries as they appear in the Unity inspector.
        /// This order is preserved across domain reloads and serialization cycles. The internal
        /// <see cref="SortedDictionary{TKey,TValue}"/> maintains sorted iteration order for efficient lookups,
        /// but the serialized arrays always reflect the user's intended order.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// sortedDictionary.OnAfterDeserialize();
        /// TValue value = sortedDictionary[key];
        /// </code>
        /// </example>
        public void OnAfterDeserialize()
        {
            bool keysAndValuesPresent =
                _keys != null && _values != null && _keys.Length == _values.Length;

            if (!keysAndValuesPresent)
            {
                _keys = null;
                _values = null;
                _preserveSerializedEntries = false;
                _arraysDirty = true;
                return;
            }

            _dictionary.Clear();
            bool hasDuplicateKeys = false;
            bool encounteredNullReference = false;
            bool keySupportsNullCheck = TypeSupportsNullReferences(typeof(TKey));
            int length = _keys.Length;
            for (int index = 0; index < length; index++)
            {
                TKey key = _keys[index];
                TValue value = GetValue(_values, index);

                if (keySupportsNullCheck && ReferenceEquals(key, null))
                {
                    encounteredNullReference = true;
                    LogNullReferenceSkip("key", index);
                    continue;
                }

                if (!hasDuplicateKeys && _dictionary.ContainsKey(key))
                {
                    hasDuplicateKeys = true;
                }

                _dictionary[key] = value;
            }

            // Always preserve the serialized arrays after deserialization to maintain user-defined order.
            // The arrays represent the order as it appears in the Unity inspector, which should not
            // change due to domain reloads. Only runtime modifications via Add/Remove/Clear should
            // trigger array rebuilding (handled by MarkSerializationCacheDirty).
            _preserveSerializedEntries = true;
            _arraysDirty = false;

            // Track if we have duplicates/nulls that require special handling in the editor
            _hasDuplicatesOrNulls = hasDuplicateKeys || encounteredNullReference;
        }

        private static bool TypeSupportsNullReferences(Type type)
        {
            return type != null
                && (!type.IsValueType || typeof(UnityEngine.Object).IsAssignableFrom(type));
        }

        private static void LogNullReferenceSkip(string component, int index)
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
                $"SerializableSortedDictionary<{typeof(TKey).FullName}, {typeof(TValue).FullName}> skipped serialized entry at index {index} because the {component} reference was null."
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

            _keys = null;
            _values = null;
        }

        [ProtoAfterDeserialization]
        protected internal void OnProtoAfterDeserialization()
        {
            OnAfterDeserialize();
        }

        /// <summary>
        /// Writes the serialized representation of the dictionary into a <see cref="SerializationInfo"/> instance.
        /// </summary>
        /// <param name="info">The serialization store to populate.</param>
        /// <param name="context">Context for the serialization process.</param>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            OnBeforeSerialize();
            info.AddValue(KeysSerializationName, _keys, typeof(TKey[]));
            info.AddValue(ValuesSerializationName, _values, typeof(TValueCache[]));
        }

        /// <summary>
        /// Finalizes deserialization after <see cref="SerializationInfo"/> data has been applied.
        /// </summary>
        /// <param name="sender">Reserved for future use.</param>
        public void OnDeserialization(object sender)
        {
            // No additional action required. The serialization constructor already
            // reconstructed the sorted dictionary from the serialized key/value arrays.
        }

        private void MarkSerializationCacheDirty()
        {
            _preserveSerializedEntries = false;
            _arraysDirty = true;
            // Note: We intentionally do NOT null out _keys and _values here to preserve order information
            // for SyncSerializedArraysPreservingOrder() during the next OnBeforeSerialize() call.
        }

        /// <summary>
        /// Returns a struct enumerator that iterates over entries in sorted order without allocations.
        /// </summary>
        /// <returns>An enumerator positioned before the first entry.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scoreboard = new SerializableSortedDictionary<string, int>(StringComparer.Ordinal);
        /// foreach (KeyValuePair<string, int> entry in scoreboard)
        /// {
        ///     Debug.Log(entry.Key);
        /// }
        /// ]]></code>
        /// </example>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(_dictionary.GetEnumerator());
        }

        /// <inheritdoc />
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<
            KeyValuePair<TKey, TValue>
        >.GetEnumerator()
        {
            return _dictionary.GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return _dictionary.GetEnumerator();
        }

        /// <summary>
        /// Adds an entry via the <see cref="ICollection{T}"/> interface.
        /// </summary>
        /// <param name="item">The entry to add.</param>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scoreboard = new SerializableSortedDictionary<string, int>(StringComparer.Ordinal);
        /// KeyValuePair<string, int> entry = new KeyValuePair<string, int>("Alice", 1200);
        /// scoreboard.Add(entry);
        /// ]]></code>
        /// </example>
        public void Add(KeyValuePair<TKey, TValue> item)
        {
            ((IDictionary<TKey, TValue>)_dictionary).Add(item);
            MarkSerializationCacheDirty();
        }

        /// <summary>
        /// Determines whether the dictionary contains the provided entry.
        /// </summary>
        /// <param name="item">The entry to locate.</param>
        /// <returns><c>true</c> when both the key and value match.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scoreboard = new SerializableSortedDictionary<string, int>(StringComparer.Ordinal);
        /// KeyValuePair<string, int> entry = new KeyValuePair<string, int>("Alice", 1200);
        /// bool present = scoreboard.Contains(entry);
        /// ]]></code>
        /// </example>
        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return ((IDictionary<TKey, TValue>)_dictionary).Contains(item);
        }

        /// <summary>
        /// Copies the dictionary contents into the provided array.
        /// </summary>
        /// <param name="array">Destination array.</param>
        /// <param name="arrayIndex">Index to begin writing at.</param>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scoreboard = new SerializableSortedDictionary<string, int>(StringComparer.Ordinal);
        /// KeyValuePair<string, int>[] snapshot = new KeyValuePair<string, int>[scoreboard.Count];
        /// scoreboard.CopyTo(snapshot, 0);
        /// ]]></code>
        /// </example>
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            ((IDictionary<TKey, TValue>)_dictionary).CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Removes the specified entry when both the key and value match.
        /// </summary>
        /// <param name="item">The entry to remove.</param>
        /// <returns><c>true</c> when the entry existed.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scoreboard = new SerializableSortedDictionary<string, int>(StringComparer.Ordinal);
        /// KeyValuePair<string, int> entry = new KeyValuePair<string, int>("Alice", 1200);
        /// bool removed = scoreboard.Remove(entry);
        /// ]]></code>
        /// </example>
        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            bool removed = ((IDictionary<TKey, TValue>)_dictionary).Remove(item);
            if (removed)
            {
                MarkSerializationCacheDirty();
            }

            return removed;
        }

        /// <summary>
        /// Indicates whether the non-generic <see cref="IDictionary"/> wrapper has a fixed size.
        /// </summary>
        public bool IsFixedSize => ((IDictionary)_dictionary).IsFixedSize;

        ICollection IDictionary.Keys => _dictionary.Keys;

        ICollection IDictionary.Values => _dictionary.Values;

        /// <summary>
        /// Indicates whether access to the dictionary is synchronized.
        /// </summary>
        public bool IsSynchronized => ((IDictionary)_dictionary).IsSynchronized;

        /// <summary>
        /// Provides an object that callers can lock on when coordinating access from multiple threads.
        /// </summary>
        public object SyncRoot => ((IDictionary)_dictionary).SyncRoot;

        /// <summary>
        /// Gets or sets entries through the non-generic <see cref="IDictionary"/> interface.
        /// </summary>
        /// <param name="key">The boxed key.</param>
        /// <returns>The boxed value.</returns>
        public object this[object key]
        {
            get => ((IDictionary)_dictionary)[key];
            set
            {
                ((IDictionary)_dictionary)[key] = value;
                MarkSerializationCacheDirty();
            }
        }

        /// <summary>
        /// Adds a boxed entry via the non-generic interface.
        /// </summary>
        /// <param name="key">The boxed key.</param>
        /// <param name="value">The boxed value.</param>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedDictionary<string, int> scoreboard = new SerializableSortedDictionary<string, int>(StringComparer.Ordinal);
        /// IDictionary boxed = scoreboard;
        /// boxed.Add((object)"Alice", 1200);
        /// ]]></code>
        /// </example>
        public void Add(object key, object value)
        {
            ((IDictionary)_dictionary).Add(key, value);
            MarkSerializationCacheDirty();
        }

        /// <summary>
        /// Determines whether the dictionary contains the specified boxed key.
        /// </summary>
        /// <param name="key">The key to locate.</param>
        /// <returns><c>true</c> when the key exists.</returns>
        public bool Contains(object key)
        {
            return ((IDictionary)_dictionary).Contains(key);
        }

        /// <inheritdoc />
        IDictionaryEnumerator IDictionary.GetEnumerator()
        {
            return _dictionary.GetEnumerator();
        }

        /// <summary>
        /// Removes a boxed entry and invalidates the serialized cache when the element existed.
        /// </summary>
        /// <param name="key">The boxed key to remove.</param>
        public void Remove(object key)
        {
            IDictionary dictionary = _dictionary;
            bool existed = dictionary.Contains(key);
            dictionary.Remove(key);
            if (existed)
            {
                MarkSerializationCacheDirty();
            }
        }

        /// <summary>
        /// Copies entries into a <see cref="DictionaryEntry"/> array to satisfy <see cref="IDictionary.CopyTo"/>.
        /// </summary>
        /// <param name="array">Destination array.</param>
        /// <param name="index">Destination index.</param>
        public void CopyTo(Array array, int index)
        {
            ((IDictionary)_dictionary).CopyTo(array, index);
        }

        /// <summary>
        /// Allocation-free enumerator returned by <see cref="GetEnumerator()"/>.
        /// </summary>
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private SortedDictionary<TKey, TValue>.Enumerator _enumerator;

            internal Enumerator(SortedDictionary<TKey, TValue>.Enumerator enumerator)
            {
                _enumerator = enumerator;
            }

            public KeyValuePair<TKey, TValue> Current => _enumerator.Current;

            object IEnumerator.Current => _enumerator.Current;

            /// <summary>
            /// Advances the enumerator to the next entry.
            /// </summary>
            /// <returns><c>true</c> when another element is available.</returns>
            public bool MoveNext()
            {
                return _enumerator.MoveNext();
            }

            /// <summary>
            /// Releases the underlying sorted dictionary enumerator.
            /// </summary>
            public void Dispose()
            {
                _enumerator.Dispose();
            }

            /// <summary>
            /// Reset is not supported; enumerators follow <see cref="SortedDictionary{TKey, TValue}.Enumerator"/> semantics.
            /// </summary>
            void IEnumerator.Reset()
            {
                throw new NotSupportedException("Reset is not supported.");
            }
        }
    }

    /// <summary>
    /// Concrete sorted dictionary implementation that saves both keys and values through Unity, ProtoBuf, and JSON serialization.
    /// Use this when you need deterministic iteration order plus inspector support for key/value data.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// SerializableSortedDictionary<string, int> scoreboard = new SerializableSortedDictionary<string, int>(StringComparer.Ordinal);
    /// scoreboard.Add("Alice", 1200);
    /// scoreboard.Add("Bob", 900);
    /// foreach (KeyValuePair<string, int> entry in scoreboard)
    /// {
    ///     Debug.Log($"{entry.Key}: {entry.Value}");
    /// }
    /// ]]></code>
    /// </example>
    [Serializable]
    public class SerializableSortedDictionary<TKey, TValue>
        : SerializableSortedDictionaryBase<TKey, TValue, TValue>
        where TKey : IComparable<TKey>
    {
        /// <summary>
        /// Initializes an empty sorted dictionary compatible with Unity and ProtoBuf serialization.
        /// </summary>
        public SerializableSortedDictionary() { }

        /// <summary>
        /// Initializes the dictionary by copying entries from an existing map.
        /// </summary>
        /// <param name="dictionary">Source dictionary whose contents are copied into the new instance.</param>
        public SerializableSortedDictionary(IDictionary<TKey, TValue> dictionary)
            : base(dictionary) { }

        /// <summary>
        /// Initializes the dictionary during custom serialization scenarios.
        /// </summary>
        protected SerializableSortedDictionary(SerializationInfo info, StreamingContext context)
            : base(info, context) { }

        protected override TValue GetValue(TValue[] cache, int index)
        {
            return cache[index];
        }

        protected override void SetValue(TValue[] cache, int index, TValue value)
        {
            cache[index] = value;
        }
    }

    /// <summary>
    /// Sorted dictionary variant that stores each value in a cache object so complex data can be serialized safely.
    /// Extend this when values require bespoke serialization, such as types containing Unity objects or unmanaged resources.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// [Serializable]
    /// public sealed class RichValue
    /// {
    ///     public string Name;
    ///     public int Cost;
    /// }
    ///
    /// [Serializable]
    /// public sealed class RichValueCache : SerializableDictionary.Cache<RichValue>
    /// {
    /// }
    ///
    /// SerializableSortedDictionary<int, RichValue, RichValueCache> catalog =
    ///     new SerializableSortedDictionary<int, RichValue, RichValueCache>();
    /// catalog[1] = new RichValue { Name = "HealthPotion", Cost = 50 };
    /// ]]></code>
    /// </example>
    [Serializable]
    public class SerializableSortedDictionary<TKey, TValue, TValueCache>
        : SerializableSortedDictionaryBase<TKey, TValue, TValueCache>
        where TKey : IComparable<TKey>
        where TValueCache : SerializableDictionary.Cache<TValue>, new()
    {
        /// <summary>
        /// Initializes an empty sorted dictionary whose values are stored through cache entries.
        /// </summary>
        public SerializableSortedDictionary() { }

        /// <summary>
        /// Initializes the dictionary by copying entries from an existing map.
        /// </summary>
        /// <param name="dictionary">Source dictionary whose contents are copied into the new instance.</param>
        public SerializableSortedDictionary(IDictionary<TKey, TValue> dictionary)
            : base(dictionary) { }

        /// <summary>
        /// Initializes the dictionary during custom serialization scenarios.
        /// </summary>
        protected SerializableSortedDictionary(SerializationInfo info, StreamingContext context)
            : base(info, context) { }

        protected override TValue GetValue(TValueCache[] cache, int index)
        {
            return cache[index].Data;
        }

        protected override void SetValue(TValueCache[] cache, int index, TValue value)
        {
            cache[index] = new TValueCache { Data = value };
        }
    }
}
